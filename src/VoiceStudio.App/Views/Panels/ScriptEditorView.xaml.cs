using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.ApplicationModel.DataTransfer;
using VoiceStudio.Core.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using VoiceStudio.App.ViewModels;
using VoiceStudio.App.Services;
using VoiceStudio.App.Services.UndoableActions;
using System;
using System.Linq;
using Windows.System;
using ScriptItemModel = VoiceStudio.App.ViewModels.ScriptItem;
using ScriptSegmentModel = VoiceStudio.Core.Models.ScriptSegment;

namespace VoiceStudio.App.Views.Panels
{
  /// <summary>
  /// Script Editor View - Advanced script editor for voice synthesis.
  /// </summary>
  public sealed partial class ScriptEditorView : UserControl
  {
    public ScriptEditorViewModel ViewModel { get; }
    private ContextMenuService? _contextMenuService;
    private ToastNotificationService? _toastService;
    private UndoRedoService? _undoRedoService;
    private DragDropVisualFeedbackService? _dragDropService;
    private ScriptSegmentModel? _draggedSegment;

    public ScriptEditorView()
    {
      InitializeComponent();
      ViewModel = new ScriptEditorViewModel(
          AppServices.GetRequiredService<VoiceStudio.Core.Services.IViewModelContext>(),
          ServiceProvider.GetBackendClient(),
          AppServices.GetRequiredService<IDialogService>()
      );
      DataContext = ViewModel;

      // Initialize services
      _contextMenuService = ServiceProvider.GetContextMenuService();
      _toastService = ServiceProvider.GetToastNotificationService();
      _undoRedoService = ServiceProvider.GetUndoRedoService();
      _dragDropService = ServiceProvider.GetDragDropVisualFeedbackService();

      // Subscribe to ViewModel events for toast notifications
      ViewModel.PropertyChanged += (_, e) =>
      {
        if (e.PropertyName == nameof(ScriptEditorViewModel.ErrorMessage) && !string.IsNullOrEmpty(ViewModel.ErrorMessage))
        {
          _toastService?.ShowToast(ToastType.Error, "Script Editor Error", ViewModel.ErrorMessage);
        }
        else if (e.PropertyName == nameof(ScriptEditorViewModel.StatusMessage) && !string.IsNullOrEmpty(ViewModel.StatusMessage))
        {
          _toastService?.ShowToast(ToastType.Success, "Script Editor", ViewModel.StatusMessage);
        }
      };

      // Setup keyboard navigation
      this.Loaded += ScriptEditorView_KeyboardNavigation_Loaded;

      // Setup Escape key to close help overlay
      KeyboardNavigationHelper.SetupEscapeKeyHandling(this, () =>
      {
        if (HelpOverlay.IsVisible)
        {
          HelpOverlay.IsVisible = false;
        }
      });

      // Add keyboard handler for multi-select
      this.KeyDown += ScriptEditorView_KeyDown;
    }

    private void ScriptEditorView_KeyDown(object _, KeyRoutedEventArgs e)
    {
      var isCtrlPressed = Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(Windows.System.VirtualKey.Control).HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);

      if (isCtrlPressed && e.Key == Windows.System.VirtualKey.A)
      {
        // Ctrl+A - Select all scripts
        if (ViewModel.SelectAllScriptsCommand.CanExecute(null))
        {
          ViewModel.SelectAllScriptsCommand.Execute(null);
          e.Handled = true;
        }
      }
      else if (e.Key == Windows.System.VirtualKey.Escape)
      {
        // Escape - Clear script selection
        if (ViewModel.ClearScriptSelectionCommand.CanExecute(null))
        {
          ViewModel.ClearScriptSelectionCommand.Execute(null);
          e.Handled = true;
        }
      }
    }

    private void Script_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
      if (sender is ListViewItem listViewItem && listViewItem.DataContext is ScriptItemModel script)
      {
        var isCtrlPressed = InputHelper.IsControlPressed();
        var isShiftPressed = InputHelper.IsShiftPressed();

        ViewModel.ToggleScriptSelection(script.Id, isCtrlPressed, isShiftPressed);

        UpdateScriptSelectionVisuals();
        e.Handled = true;
      }
    }

    private void UpdateScriptSelectionVisuals()
    {
      // Update visual indicators for all script list items
      UpdateScriptSelectionVisualsRecursive(this);
    }

    private void UpdateScriptSelectionVisualsRecursive(DependencyObject element)
    {
      if (element == null || ViewModel == null)
        return;

      // Check if this is a ListViewItem with a ScriptItem
      if (element is ListViewItem listViewItem && listViewItem.DataContext is ScriptItemModel script)
      {
        var isSelected = ViewModel.IsScriptSelected(script.Id);

        // Update background to show selection
        if (isSelected)
        {
          listViewItem.Background = new SolidColorBrush(Windows.UI.Color.FromArgb(51, 0, 183, 194)); // VSQ.Accent.Cyan with opacity
        }
        else
        {
          listViewItem.Background = null; // Use default
        }
      }

      // Recursively check children
      var childCount = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChildrenCount(element);
      for (int i = 0; i < childCount; i++)
      {
        var child = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChild(element, i);
        UpdateScriptSelectionVisualsRecursive(child);
      }
    }

    private void HelpButton_Click(object _, Microsoft.UI.Xaml.RoutedEventArgs __)
    {
      HelpOverlay.Title = "Script Editor Help";
      HelpOverlay.HelpText = "The Script Editor panel allows you to create and manage scripts for multi-segment voice synthesis. Scripts contain multiple segments, each with its own text, speaker, voice profile, and timing. Create scripts for dialogue, narration, or multi-voice projects. Edit segments, adjust timing, assign different voices to segments, and export scripts for batch synthesis or sharing.";

      HelpOverlay.Shortcuts.Clear();
      HelpOverlay.Shortcuts.Add(new Controls.KeyboardShortcut { Key = "Ctrl+N", Description = "Create new script" });
      HelpOverlay.Shortcuts.Add(new Controls.KeyboardShortcut { Key = "Ctrl+S", Description = "Save script" });
      HelpOverlay.Shortcuts.Add(new Controls.KeyboardShortcut { Key = "Delete", Description = "Delete segment" });

      HelpOverlay.Tips.Clear();
      HelpOverlay.Tips.Add("Scripts organize multiple voice synthesis segments");
      HelpOverlay.Tips.Add("Each segment can have its own voice profile and speaker");
      HelpOverlay.Tips.Add("Edit segment text, timing, and voice assignments");
      HelpOverlay.Tips.Add("Export scripts for batch processing or collaboration");
      HelpOverlay.Tips.Add("Use scripts for dialogue, narration, or multi-voice projects");

      HelpOverlay.Visibility = Microsoft.UI.Xaml.Visibility.Visible;
      HelpOverlay.Show();
    }

    private void Script_RightTapped(object sender, RightTappedRoutedEventArgs e)
    {
      if (sender is ListView listView && e.OriginalSource is FrameworkElement element)
      {
        var script = element.DataContext as ScriptItemModel ?? listView.SelectedItem as ScriptItemModel;
        if (script != null)
        {
          e.Handled = true;
          if (_contextMenuService != null)
          {
            var menu = new MenuFlyout();

            var synthesizeItem = new MenuFlyoutItem { Text = "Synthesize" };
            synthesizeItem.Click += async (_, _) => await HandleScriptMenuClick("Synthesize", script);
            menu.Items.Add(synthesizeItem);

            var editItem = new MenuFlyoutItem { Text = "Edit" };
            editItem.Click += async (_, _) => await HandleScriptMenuClick("Edit", script);
            menu.Items.Add(editItem);

            menu.Items.Add(new MenuFlyoutSeparator());

            var duplicateItem = new MenuFlyoutItem { Text = "Duplicate" };
            duplicateItem.Click += async (_, _) => await HandleScriptMenuClick("Duplicate", script);
            menu.Items.Add(duplicateItem);

            var exportItem = new MenuFlyoutItem { Text = "Export" };
            exportItem.Click += async (_, _) => await HandleScriptMenuClick("Export", script);
            menu.Items.Add(exportItem);

            menu.Items.Add(new MenuFlyoutSeparator());

            var deleteItem = new MenuFlyoutItem { Text = "Delete" };
            deleteItem.Click += async (_, _) => await HandleScriptMenuClick("Delete", script);
            menu.Items.Add(deleteItem);

            var position = e.GetPosition(listView);
            _contextMenuService.ShowContextMenu(menu, listView, position);
          }
        }
      }
    }

    private void Segment_RightTapped(object sender, RightTappedRoutedEventArgs e)
    {
      if (sender is ListView listView && e.OriginalSource is FrameworkElement element)
      {
        var segment = element.DataContext as ScriptSegment ?? listView.SelectedItem as ScriptSegment;
        if (segment != null)
        {
          e.Handled = true;
          if (_contextMenuService != null)
          {
            var menu = new MenuFlyout();

            var duplicateItem = new MenuFlyoutItem { Text = "Duplicate" };
            duplicateItem.Click += async (_, _) => await HandleSegmentMenuClick("Duplicate", segment);
            menu.Items.Add(duplicateItem);

            menu.Items.Add(new MenuFlyoutSeparator());

            var deleteItem = new MenuFlyoutItem { Text = "Delete" };
            deleteItem.Click += async (_, _) => await HandleSegmentMenuClick("Delete", segment);
            menu.Items.Add(deleteItem);

            var position = e.GetPosition(listView);
            _contextMenuService.ShowContextMenu(menu, listView, position);
          }
        }
      }
    }

    private async System.Threading.Tasks.Task HandleScriptMenuClick(string action, ScriptItemModel script)
    {
      try
      {
        switch (action.ToLower())
        {
          case "synthesize":
            if (ViewModel.SynthesizeScriptCommand.CanExecute(script))
            {
              await ViewModel.SynthesizeScriptCommand.ExecuteAsync(script);
              _toastService?.ShowToast(ToastType.Success, "Synthesizing", $"Synthesizing script '{script.Name}'");
            }
            break;
          case "edit":
            ViewModel.SelectedScript = script;
            _toastService?.ShowToast(ToastType.Info, "Edit Script", $"Editing script '{script.Name}'");
            break;
          case "duplicate":
            _toastService?.ShowToast(ToastType.Info, "Duplicate", $"Duplicate functionality for '{script.Name}' is planned for a future release. Create a new script and copy segments instead.");
            break;
          case "export":
            _toastService?.ShowToast(ToastType.Info, "Export", $"Export functionality for '{script.Name}' is planned for a future release. Scripts are automatically saved to your project.");
            break;
          case "delete":
            if (ViewModel.DeleteScriptCommand.CanExecute(script))
            {
              var dialog = new ContentDialog
              {
                Title = "Delete Script",
                Content = $"Are you sure you want to delete script '{script.Name}'? This action cannot be undone.",
                PrimaryButtonText = "Delete",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Close,
                XamlRoot = this.XamlRoot
              };

              var result = await dialog.ShowAsync();
              if (result == ContentDialogResult.Primary)
              {
                await ViewModel.DeleteScriptCommand.ExecuteAsync(script);

                // Undo/redo is handled by DeleteScriptCommand via DeleteScriptAction
                _toastService?.ShowToast(ToastType.Success, "Deleted", $"Deleted script '{script.Name}'");
              }
            }
            break;
        }
      }
      catch (Exception ex)
      {
        _toastService?.ShowToast(ToastType.Error, "Error", $"Failed to {action}: {ex.Message}");
      }
    }

    private async System.Threading.Tasks.Task HandleSegmentMenuClick(string action, ScriptSegmentModel segment)
    {
      try
      {
        switch (action.ToLower())
        {
          case "duplicate":
            if (ViewModel.SelectedScript != null)
            {
              var duplicatedSegment = new ScriptSegment
              {
                Id = Guid.NewGuid().ToString(),
                Text = segment.Text,
                StartTime = segment.StartTime,
                EndTime = segment.EndTime,
                Speaker = segment.Speaker,
                VoiceProfileId = segment.VoiceProfileId,
                Prosody = segment.Prosody != null ? new System.Collections.Generic.Dictionary<string, object>(segment.Prosody) : null,
                Phonemes = segment.Phonemes != null ? new System.Collections.Generic.List<string>(segment.Phonemes) : null,
                Notes = segment.Notes
              };

              ViewModel.SelectedScript.Segments.Add(duplicatedSegment);

              // Register undo action
              if (_undoRedoService != null)
              {
                var actionObj = new SimpleAction(
                    "Duplicate Segment",
                    () => ViewModel.SelectedScript.Segments.Remove(duplicatedSegment),
                    () => ViewModel.SelectedScript.Segments.Add(duplicatedSegment));
                _undoRedoService.RegisterAction(actionObj);
              }

              _toastService?.ShowToast(ToastType.Success, "Duplicated", "Segment duplicated");
            }
            break;
          case "delete":
            if (ViewModel.SelectedScript != null && ViewModel.RemoveSegmentCommand.CanExecute(segment))
            {
              await ViewModel.RemoveSegmentCommand.ExecuteAsync(segment);

              // Undo/redo is handled by RemoveSegmentCommand via RemoveScriptSegmentAction
              _toastService?.ShowToast(ToastType.Success, "Deleted", "Segment deleted");
            }
            break;
        }
      }
      catch (Exception ex)
      {
        _toastService?.ShowToast(ToastType.Error, "Error", $"Failed to {action}: {ex.Message}");
      }
    }

    // Drag-and-drop handlers for segment reordering
    private void Segment_DragStarting(UIElement sender, DragStartingEventArgs e)
    {
      if (sender is ListViewItem listViewItem && listViewItem.DataContext is ScriptSegment segment && ViewModel.SelectedScript != null)
      {
        _draggedSegment = segment;

        // Set drag data
        e.Data.SetText(segment.Id);
        e.Data.Properties.Add("SegmentId", segment.Id);
        e.Data.Properties.Add("SegmentText", segment.Text ?? "Unnamed Segment");

        // Reduce opacity of source element
        listViewItem.Opacity = 0.5;
      }
    }

    private void Segment_DragItemsCompleted(UIElement sender, DragItemsCompletedEventArgs e)
    {
      // Clean up drag state
      if (sender is ListViewItem listViewItem)
      {
        listViewItem.Opacity = 1.0;
      }

      _dragDropService?.Cleanup();

      _draggedSegment = null;
    }

    private void Segment_DragOver(object sender, DragEventArgs e)
    {
      if (sender is ListViewItem listViewItem && _dragDropService != null && ViewModel.SelectedScript != null)
      {
        e.AcceptedOperation = DataPackageOperation.Move;
        e.DragUIOverride.IsGlyphVisible = false;
        e.DragUIOverride.IsContentVisible = false;

        // Show drop target indicator
        var position = e.GetPosition(listViewItem);
        var dropPosition = DetermineSegmentDropPosition(listViewItem, position);
        _dragDropService.ShowDropTargetIndicator(listViewItem, dropPosition);
      }
    }

    private void Segment_Drop(object sender, DragEventArgs e)
    {
      if (sender is ListViewItem listViewItem && _draggedSegment != null && ViewModel.SelectedScript != null && _dragDropService != null)
      {
        e.AcceptedOperation = DataPackageOperation.Move;

        // Hide drop indicator
        _dragDropService.HideDropTargetIndicator();
        _dragDropService.Cleanup();

        // Get target segment
        if (listViewItem.DataContext is ScriptSegment targetSegment)
        {
          var draggedSegment = _draggedSegment;
          var draggedIndex = ViewModel.SelectedScript.Segments.IndexOf(draggedSegment);
          var targetIndex = ViewModel.SelectedScript.Segments.IndexOf(targetSegment);

          if (draggedIndex >= 0 && targetIndex >= 0 && draggedIndex != targetIndex)
          {
            // Determine drop position
            var position = e.GetPosition(listViewItem);
            var dropPosition = DetermineSegmentDropPosition(listViewItem, position);

            // Reorder segments in the collection
            ViewModel.SelectedScript.Segments.RemoveAt(draggedIndex);

            if (dropPosition == DropPosition.Before)
            {
              ViewModel.SelectedScript.Segments.Insert(targetIndex, draggedSegment);
            }
            else if (dropPosition == DropPosition.After)
            {
              var newIndex = targetIndex < draggedIndex ? targetIndex + 1 : targetIndex + 1;
              ViewModel.SelectedScript.Segments.Insert(newIndex, draggedSegment);
            }
            else
            {
              // On - replace target
              ViewModel.SelectedScript.Segments.Insert(targetIndex, draggedSegment);
            }

            _toastService?.ShowToast(ToastType.Success, "Reordered", "Segment order updated in script");
          }
        }

        // Clean up drag state
        _draggedSegment = null;

        // Restore source element opacity
        if (e.OriginalSource is ListViewItem sourceItem)
        {
          sourceItem.Opacity = 1.0;
        }
      }
    }

    private void Segment_DragLeave(object _, DragEventArgs __)
    {
      _dragDropService?.HideDropTargetIndicator();
    }

    private DropPosition DetermineSegmentDropPosition(ListViewItem target, Windows.Foundation.Point position)
    {
      // Determine if drop is before, after, or on the target
      var targetHeight = target.ActualHeight;
      var relativeY = position.Y;

      if (relativeY < targetHeight * 0.33)
        return DropPosition.Before;
      else if (relativeY > targetHeight * 0.67)
        return DropPosition.After;
      else
        return DropPosition.On;
    }

    private void ScriptEditorView_KeyboardNavigation_Loaded(object _, RoutedEventArgs __)
    {
      KeyboardNavigationHelper.SetupTabNavigation(this);
    }
  }
}