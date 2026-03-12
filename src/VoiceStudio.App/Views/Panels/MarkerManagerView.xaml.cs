using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml;
using VoiceStudio.App.ViewModels;
using VoiceStudio.App.Services;
using VoiceStudio.App.Services.UndoableActions;
using System;
using Windows.System;
using MarkerItem = VoiceStudio.App.ViewModels.MarkerItem;

namespace VoiceStudio.App.Views.Panels
{
  /// <summary>
  /// MarkerManagerView panel for managing timeline markers.
  /// </summary>
  public sealed partial class MarkerManagerView : UserControl
  {
    public MarkerManagerViewModel ViewModel { get; }
    private ContextMenuService? _contextMenuService;
    private ToastNotificationService? _toastService;
    private UndoRedoService? _undoRedoService;
    private DragDropVisualFeedbackService? _dragDropService;
    private MarkerItem? _draggedMarker;

    public MarkerManagerView()
    {
      this.InitializeComponent();
      ViewModel = new MarkerManagerViewModel(
          AppServices.GetRequiredService<VoiceStudio.Core.Services.IViewModelContext>(),
          VoiceStudio.App.Services.ServiceProvider.GetBackendClient(),
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
        if (e.PropertyName == nameof(MarkerManagerViewModel.ErrorMessage) && !string.IsNullOrEmpty(ViewModel.ErrorMessage))
        {
          _toastService?.ShowToast(ToastType.Error, "Marker Manager Error", ViewModel.ErrorMessage);
        }
        else if (e.PropertyName == nameof(MarkerManagerViewModel.StatusMessage) && !string.IsNullOrEmpty(ViewModel.StatusMessage))
        {
          _toastService?.ShowToast(ToastType.Success, "Marker Manager", ViewModel.StatusMessage);
        }
      };

      // Setup keyboard navigation
      this.Loaded += MarkerManagerView_KeyboardNavigation_Loaded;

      // Setup Escape key to close help overlay
      KeyboardNavigationHelper.SetupEscapeKeyHandling(this, () =>
      {
        if (HelpOverlay.IsVisible)
        {
          HelpOverlay.IsVisible = false;
        }
      });

      // Add keyboard handler for multi-select
      this.KeyDown += MarkerManagerView_KeyDown;
    }

    private void Marker_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
      if (sender is ListViewItem item && item.DataContext is MarkerItem marker)
      {
        var isCtrlPressed = InputHelper.IsControlPressed();
        var isShiftPressed = InputHelper.IsShiftPressed();

        ViewModel.ToggleMarkerSelection(marker.Id, isCtrlPressed, isShiftPressed);
        e.Handled = true;
      }
    }

    private void MarkerManagerView_KeyDown(object _, KeyRoutedEventArgs e)
    {
      var isCtrlPressed = Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(Windows.System.VirtualKey.Control).HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);

      if (isCtrlPressed && e.Key == Windows.System.VirtualKey.A)
      {
        // Ctrl+A - Select all markers
        if (ViewModel.SelectAllMarkersCommand.CanExecute(null))
        {
          ViewModel.SelectAllMarkersCommand.Execute(null);
          e.Handled = true;
        }
      }
      else if (e.Key == Windows.System.VirtualKey.Escape)
      {
        // Escape - Clear marker selection
        if (ViewModel.ClearMarkerSelectionCommand.CanExecute(null))
        {
          ViewModel.ClearMarkerSelectionCommand.Execute(null);
          e.Handled = true;
        }
      }
    }

    private void HelpButton_Click(object _, Microsoft.UI.Xaml.RoutedEventArgs __)
    {
      HelpOverlay.Title = "Marker Manager Help";
      HelpOverlay.HelpText = "The Marker Manager allows you to create and manage timeline markers for navigation, organization, and reference. Add markers at specific time positions, label them, color-code them, and use them to quickly navigate to important points in your project.";

      HelpOverlay.Shortcuts.Clear();
      HelpOverlay.Shortcuts.Add(new Controls.KeyboardShortcut { Key = "M", Description = "Add marker at playhead" });
      HelpOverlay.Shortcuts.Add(new Controls.KeyboardShortcut { Key = "Ctrl+N", Description = "Create new marker" });
      HelpOverlay.Shortcuts.Add(new Controls.KeyboardShortcut { Key = "Escape", Description = "Close help" });

      HelpOverlay.Tips.Clear();
      HelpOverlay.Tips.Add("Markers help you navigate and organize long projects");
      HelpOverlay.Tips.Add("Color-code markers by type (verse, chorus, bridge, etc.)");
      HelpOverlay.Tips.Add("Jump to markers quickly using the marker list");
      HelpOverlay.Tips.Add("Markers can be exported and shared with project files");
      HelpOverlay.Tips.Add("Use markers to mark important sections or edit points");

      HelpOverlay.Visibility = Microsoft.UI.Xaml.Visibility.Visible;
      HelpOverlay.Show();
    }

    private void Marker_RightTapped(object sender, RightTappedRoutedEventArgs e)
    {
      if (sender is ListView listView && e.OriginalSource is FrameworkElement element)
      {
        var marker = element.DataContext as MarkerItem ?? listView.SelectedItem as MarkerItem;
        if (marker != null)
        {
          e.Handled = true;
          if (_contextMenuService != null)
          {
            var menu = new MenuFlyout();

            var editItem = new MenuFlyoutItem { Text = "Edit" };
            editItem.Click += async (_, __) => await HandleMarkerMenuClick("Edit", marker);
            menu.Items.Add(editItem);

            var duplicateItem = new MenuFlyoutItem { Text = "Duplicate" };
            duplicateItem.Click += async (_, __) => await HandleMarkerMenuClick("Duplicate", marker);
            menu.Items.Add(duplicateItem);

            menu.Items.Add(new MenuFlyoutSeparator());

            var deleteItem = new MenuFlyoutItem { Text = "Delete" };
            deleteItem.Click += async (_, __) => await HandleMarkerMenuClick("Delete", marker);
            menu.Items.Add(deleteItem);

            var position = e.GetPosition(listView);
            _contextMenuService.ShowContextMenu(menu, listView, position);
          }
        }
      }
    }

    private async System.Threading.Tasks.Task HandleMarkerMenuClick(string action, MarkerItem marker)
    {
      try
      {
        switch (action.ToLower())
        {
          case "edit":
            ViewModel.SelectedMarker = marker;
            _toastService?.ShowToast(ToastType.Info, "Edit Marker", $"Editing marker '{marker.Name}'");
            break;
          case "duplicate":
            _toastService?.ShowToast(ToastType.Info, "Duplicate", $"Duplicate functionality for '{marker.Name}' is planned for a future release. Create a new marker with similar properties instead.");
            break;
          case "delete":
            if (ViewModel.DeleteMarkerCommand.CanExecute(marker))
            {
              var dialog = new ContentDialog
              {
                Title = "Delete Marker",
                Content = $"Are you sure you want to delete marker '{marker.Name}'? This action cannot be undone.",
                PrimaryButtonText = "Delete",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Close,
                XamlRoot = this.XamlRoot
              };

              var result = await dialog.ShowAsync();
              if (result == ContentDialogResult.Primary)
              {
                await ViewModel.DeleteMarkerCommand.ExecuteAsync(marker);

                // Undo/redo is handled by DeleteMarkerCommand via DeleteMarkerAction
                _toastService?.ShowToast(ToastType.Success, "Deleted", $"Deleted marker '{marker.Name}'");
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

    // Drag-and-drop handlers for marker reordering
    private void Marker_DragStarting(UIElement sender, DragStartingEventArgs e)
    {
      if (sender is ListViewItem listViewItem && listViewItem.DataContext is MarkerItem marker)
      {
        _draggedMarker = marker;

        // Set drag data
        e.Data.SetText(marker.Id);
        e.Data.Properties.Add("MarkerId", marker.Id);
        e.Data.Properties.Add("MarkerName", marker.Name ?? "Unnamed Marker");

        // Reduce opacity of source element
        listViewItem.Opacity = 0.5;
      }
    }

    private void Marker_DragItemsCompleted(UIElement sender, DragItemsCompletedEventArgs e)
    {
      // Clean up drag state
      if (sender is ListViewItem listViewItem)
      {
        listViewItem.Opacity = 1.0;
      }

      _dragDropService?.Cleanup();

      _draggedMarker = null;
    }

    private void Marker_DragOver(object sender, DragEventArgs e)
    {
      if (sender is ListViewItem listViewItem && _dragDropService != null)
      {
        e.AcceptedOperation = DataPackageOperation.Move;
        e.DragUIOverride.IsGlyphVisible = false;
        e.DragUIOverride.IsContentVisible = false;

        // Show drop target indicator
        var position = e.GetPosition(listViewItem);
        var dropPosition = DetermineMarkerDropPosition(listViewItem, position);
        _dragDropService.ShowDropTargetIndicator(listViewItem, dropPosition);
      }
    }

    private void Marker_Drop(object sender, DragEventArgs e)
    {
      if (sender is ListViewItem listViewItem && _draggedMarker != null && _dragDropService != null)
      {
        e.AcceptedOperation = DataPackageOperation.Move;

        // Hide drop indicator
        _dragDropService.HideDropTargetIndicator();
        _dragDropService.Cleanup();

        // Get target marker
        if (listViewItem.DataContext is MarkerItem targetMarker)
        {
          var draggedMarker = _draggedMarker;
          var draggedIndex = ViewModel.Markers.IndexOf(draggedMarker);
          var targetIndex = ViewModel.Markers.IndexOf(targetMarker);

          if (draggedIndex >= 0 && targetIndex >= 0 && draggedIndex != targetIndex)
          {
            // Determine drop position
            var position = e.GetPosition(listViewItem);
            var dropPosition = DetermineMarkerDropPosition(listViewItem, position);

            // Reorder markers in the collection
            ViewModel.Markers.RemoveAt(draggedIndex);

            if (dropPosition == DropPosition.Before)
            {
              ViewModel.Markers.Insert(targetIndex, draggedMarker);
            }
            else if (dropPosition == DropPosition.After)
            {
              var newIndex = targetIndex < draggedIndex ? targetIndex + 1 : targetIndex;
              ViewModel.Markers.Insert(newIndex, draggedMarker);
            }
            else
            {
              // On - replace target
              ViewModel.Markers.Insert(targetIndex, draggedMarker);
            }

            _toastService?.ShowToast(ToastType.Success, "Reordered", $"Moved '{draggedMarker.Name}' in marker list");
          }
        }

        // Clean up drag state
        _draggedMarker = null;

        // Restore source element opacity
        if (e.OriginalSource is ListViewItem sourceItem)
        {
          sourceItem.Opacity = 1.0;
        }
      }
    }

    private void Marker_DragLeave(object sender, DragEventArgs e)
    {
      _dragDropService?.HideDropTargetIndicator();
    }

    private DropPosition DetermineMarkerDropPosition(ListViewItem target, Windows.Foundation.Point position)
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

    private void MarkerManagerView_KeyboardNavigation_Loaded(object _, RoutedEventArgs __)
    {
      KeyboardNavigationHelper.SetupTabNavigation(this);
    }
  }
}