using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml;
using VoiceStudio.App.ViewModels;
using VoiceStudio.App.Services;
using VoiceStudio.App.Services.UndoableActions;
using System;
using Windows.System;
using Windows.ApplicationModel.DataTransfer;
using TagItem = VoiceStudio.App.ViewModels.TagItem;

namespace VoiceStudio.App.Views.Panels
{
  /// <summary>
  /// TagManagerView panel for managing tags across the application.
  /// </summary>
  public sealed partial class TagManagerView : UserControl
  {
    public TagManagerViewModel ViewModel { get; }
    private ContextMenuService? _contextMenuService;
    private ToastNotificationService? _toastService;
    private UndoRedoService? _undoRedoService;
    private DragDropVisualFeedbackService? _dragDropService;
    private TagItem? _draggedTag;

    public TagManagerView()
    {
      this.InitializeComponent();
      ViewModel = new TagManagerViewModel(
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
      ViewModel.PropertyChanged += (s, e) =>
      {
        if (e.PropertyName == nameof(TagManagerViewModel.ErrorMessage) && !string.IsNullOrEmpty(ViewModel.ErrorMessage))
        {
          _toastService?.ShowToast(ToastType.Error, "Tag Manager Error", ViewModel.ErrorMessage);
        }
        else if (e.PropertyName == nameof(TagManagerViewModel.StatusMessage) && !string.IsNullOrEmpty(ViewModel.StatusMessage))
        {
          _toastService?.ShowToast(ToastType.Success, "Tag Manager", ViewModel.StatusMessage);
        }
      };

      // Setup keyboard navigation
      this.Loaded += TagManagerView_KeyboardNavigation_Loaded;

      // Setup Escape key to close help overlay
      KeyboardNavigationHelper.SetupEscapeKeyHandling(this, () =>
      {
        if (HelpOverlay.IsVisible)
        {
          HelpOverlay.IsVisible = false;
        }
      });

      // Add keyboard handler for multi-select
      this.KeyDown += TagManagerView_KeyDown;
    }

    private void Tag_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
      if (sender is ListViewItem item && item.DataContext is TagItem tag)
      {
        var isCtrlPressed = InputHelper.IsControlPressed();
        var isShiftPressed = InputHelper.IsShiftPressed();

        ViewModel.ToggleTagSelection(tag.Id, isCtrlPressed, isShiftPressed);
        e.Handled = true;
      }
    }

    private void TagManagerView_KeyDown(object sender, KeyRoutedEventArgs e)
    {
      var isCtrlPressed = Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(Windows.System.VirtualKey.Control).HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);

      if (isCtrlPressed && e.Key == Windows.System.VirtualKey.A)
      {
        // Ctrl+A - Select all tags
        if (ViewModel.SelectAllTagsCommand.CanExecute(null))
        {
          ViewModel.SelectAllTagsCommand.Execute(null);
          e.Handled = true;
        }
      }
      else if (e.Key == Windows.System.VirtualKey.Escape)
      {
        // Escape - Clear tag selection
        if (ViewModel.ClearTagSelectionCommand.CanExecute(null))
        {
          ViewModel.ClearTagSelectionCommand.Execute(null);
          e.Handled = true;
        }
      }
    }

    private void HelpButton_Click(object _, Microsoft.UI.Xaml.RoutedEventArgs __)
    {
      HelpOverlay.Title = "Tag Manager Help";
      HelpOverlay.HelpText = "The Tag Manager allows you to create and manage tags for organizing voice profiles and other resources. Tags can be categorized, colored, and used to filter and search. Tags help you organize large collections of voice profiles and find what you need quickly.";

      HelpOverlay.Shortcuts.Clear();
      HelpOverlay.Shortcuts.Add(new Controls.KeyboardShortcut { Key = "Ctrl+N", Description = "Create new tag" });
      HelpOverlay.Shortcuts.Add(new Controls.KeyboardShortcut { Key = "Ctrl+F", Description = "Focus search" });
      HelpOverlay.Shortcuts.Add(new Controls.KeyboardShortcut { Key = "Escape", Description = "Close help" });

      HelpOverlay.Tips.Clear();
      HelpOverlay.Tips.Add("Use categories to organize tags (e.g., 'Voice Type', 'Language', 'Emotion')");
      HelpOverlay.Tips.Add("Color tags to make them visually distinct and easier to identify");
      HelpOverlay.Tips.Add("Tags can only be deleted if they're not in use by any resources");
      HelpOverlay.Tips.Add("Search helps you quickly find tags by name or category");
      HelpOverlay.Tips.Add("Usage count shows how many resources use each tag");

      HelpOverlay.Visibility = Microsoft.UI.Xaml.Visibility.Visible;
      HelpOverlay.Show();
    }

    private void Tag_RightTapped(object sender, RightTappedRoutedEventArgs e)
    {
      if (sender is ListView listView && e.OriginalSource is FrameworkElement element)
      {
        var tag = element.DataContext ?? listView.SelectedItem;
        if (tag != null)
        {
          e.Handled = true;
          if (_contextMenuService != null)
          {
            var menu = new MenuFlyout();

            var editItem = new MenuFlyoutItem { Text = "Edit" };
            editItem.Click += async (_, _) => await HandleTagMenuClick("Edit", tag);
            menu.Items.Add(editItem);

            var duplicateItem = new MenuFlyoutItem { Text = "Duplicate" };
            duplicateItem.Click += async (_, _) => await HandleTagMenuClick("Duplicate", tag);
            menu.Items.Add(duplicateItem);

            menu.Items.Add(new MenuFlyoutSeparator());

            var deleteItem = new MenuFlyoutItem { Text = "Delete" };
            deleteItem.Click += async (_, _) => await HandleTagMenuClick("Delete", tag);
            menu.Items.Add(deleteItem);

            var position = e.GetPosition(listView);
            _contextMenuService.ShowContextMenu(menu, listView, position);
          }
        }
      }
    }

    private async System.Threading.Tasks.Task HandleTagMenuClick(string action, object tagObj)
    {
      try
      {
        var tag = (TagItem)tagObj;
        switch (action.ToLower())
        {
          case "edit":
            ViewModel.SelectedTag = tag;
            _toastService?.ShowToast(ToastType.Info, "Edit Tag", "Tag selected for editing");
            break;
          case "duplicate":
            _toastService?.ShowToast(ToastType.Info, "Duplicate", "Duplicate functionality is planned for a future release. Create a new tag with similar properties instead.");
            break;
          case "delete":
            if (tag is TagItem tagItem && ViewModel.DeleteTagCommand.CanExecute(tagItem))
            {
              var dialog = new ContentDialog
              {
                Title = "Delete Tag",
                Content = "Are you sure you want to delete this tag? This action cannot be undone.",
                PrimaryButtonText = "Delete",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Close,
                XamlRoot = this.XamlRoot
              };

              var result = await dialog.ShowAsync();
              if (result == ContentDialogResult.Primary)
              {
                await ViewModel.DeleteTagCommand.ExecuteAsync(tagItem);

                // Undo/redo is handled by DeleteTagCommand via DeleteTagAction
                _toastService?.ShowToast(ToastType.Success, "Deleted", "Tag deleted");
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

    // Drag-and-drop handlers for tag reordering
    private void Tag_DragStarting(UIElement sender, DragStartingEventArgs e)
    {
      if (sender is ListViewItem listViewItem && listViewItem.DataContext is TagItem tag)
      {
        _draggedTag = tag;

        // Set drag data
        e.Data.SetText(tag.Id);
        e.Data.Properties.Add("TagId", tag.Id);
        e.Data.Properties.Add("TagName", tag.Name ?? "Unnamed Tag");

        // Reduce opacity of source element
        listViewItem.Opacity = 0.5;
      }
    }

    private void Tag_DragItemsCompleted(UIElement sender, DragItemsCompletedEventArgs e)
    {
      // Clean up drag state
      if (sender is ListViewItem listViewItem)
      {
        listViewItem.Opacity = 1.0;
      }

      _dragDropService?.Cleanup();

      _draggedTag = null;
    }

    private void Tag_DragOver(object sender, DragEventArgs e)
    {
      if (sender is ListViewItem listViewItem && _dragDropService != null)
      {
        e.AcceptedOperation = DataPackageOperation.Move;
        e.DragUIOverride.IsGlyphVisible = false;
        e.DragUIOverride.IsContentVisible = false;

        // Show drop target indicator
        var position = e.GetPosition(listViewItem);
        var dropPosition = DetermineTagDropPosition(listViewItem, position);
        _dragDropService.ShowDropTargetIndicator(listViewItem, dropPosition);
      }
    }

    private void Tag_Drop(object sender, DragEventArgs e)
    {
      if (sender is ListViewItem listViewItem && _draggedTag != null && _dragDropService != null)
      {
        e.AcceptedOperation = DataPackageOperation.Move;

        // Hide drop indicator
        _dragDropService.HideDropTargetIndicator();
        _dragDropService.Cleanup();

        // Get target tag
        if (listViewItem.DataContext is TagItem targetTag)
        {
          var draggedTag = _draggedTag;
          var draggedIndex = ViewModel.Tags.IndexOf(draggedTag);
          var targetIndex = ViewModel.Tags.IndexOf(targetTag);

          if (draggedIndex >= 0 && targetIndex >= 0 && draggedIndex != targetIndex)
          {
            // Determine drop position
            var position = e.GetPosition(listViewItem);
            var dropPosition = DetermineTagDropPosition(listViewItem, position);

            // Reorder tags in the collection
            ViewModel.Tags.RemoveAt(draggedIndex);

            if (dropPosition == DropPosition.Before)
            {
              ViewModel.Tags.Insert(targetIndex, draggedTag);
            }
            else if (dropPosition == DropPosition.After)
            {
              var newIndex = targetIndex < draggedIndex ? targetIndex + 1 : targetIndex + 1;
              ViewModel.Tags.Insert(newIndex, draggedTag);
            }
            else
            {
              // On - replace target
              ViewModel.Tags.Insert(targetIndex, draggedTag);
            }

            _toastService?.ShowToast(ToastType.Success, "Reordered", "Tag order updated");
          }
        }

        // Clean up drag state
        _draggedTag = null;

        // Restore source element opacity
        if (e.OriginalSource is ListViewItem sourceItem)
        {
          sourceItem.Opacity = 1.0;
        }
      }
    }

    private void Tag_DragLeave(object sender, DragEventArgs e)
    {
      _dragDropService?.HideDropTargetIndicator();
    }

    private DropPosition DetermineTagDropPosition(ListViewItem target, Windows.Foundation.Point position)
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

    private void TagManagerView_KeyboardNavigation_Loaded(object _, RoutedEventArgs __)
    {
      KeyboardNavigationHelper.SetupTabNavigation(this);
    }
  }
}