using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml;
using VoiceStudio.App.Services;
using VoiceStudio.Core.Models;
using VoiceStudio.App.Services.UndoableActions;
using System;
using System.Threading;
using Windows.ApplicationModel.DataTransfer;

namespace VoiceStudio.App.Views.Panels
{
  public sealed partial class TranscribeView : Microsoft.UI.Xaml.Controls.UserControl
  {
    public TranscribeViewModel ViewModel { get; }
    private ContextMenuService? _contextMenuService;
    private ToastNotificationService? _toastService;
    private UndoRedoService? _undoRedoService;
    private DragDropVisualFeedbackService? _dragDropService;
    private TranscriptionResponse? _draggedTranscription;

    public TranscribeView()
    {
      this.InitializeComponent();
      ViewModel = new TranscribeViewModel(
          AppServices.GetRequiredService<VoiceStudio.Core.Services.IViewModelContext>(),
          AppServices.GetRequiredService<VoiceStudio.Core.Services.ITranscriptionClient>()
      );
      this.DataContext = ViewModel;

      // Initialize services
      _contextMenuService = ServiceProvider.GetContextMenuService();
      _toastService = ServiceProvider.GetToastNotificationService();
      _undoRedoService = ServiceProvider.GetUndoRedoService();
      _dragDropService = ServiceProvider.GetDragDropVisualFeedbackService();

      // Track collection changes to toggle empty state visibility
      ViewModel.Transcriptions.CollectionChanged += (s, e) => UpdateEmptyStateVisibility();

      // Add keyboard handler for multi-select
      this.KeyDown += TranscribeView_KeyDown;

      // Setup keyboard navigation and initial data load (ADR-047)
      this.Loaded += TranscribeView_Loaded;

      // Setup Escape key to close help overlay
      KeyboardNavigationHelper.SetupEscapeKeyHandling(this, () =>
      {
        // Close any open dialogs or overlays
      });

      // Subscribe to ViewModel events for toast notifications
      ViewModel.PropertyChanged += (s, e) =>
      {
        if (e.PropertyName == nameof(TranscribeViewModel.ErrorMessage) && !string.IsNullOrEmpty(ViewModel.ErrorMessage))
        {
          _toastService?.ShowToast(ToastType.Error, "Transcribe Error", ViewModel.ErrorMessage);
        }
        else if (e.PropertyName == nameof(TranscribeViewModel.StatusMessage) && !string.IsNullOrEmpty(ViewModel.StatusMessage))
        {
          _toastService?.ShowToast(ToastType.Success, "Transcribe", ViewModel.StatusMessage);
        }
      };
    }

    private void TranscribeView_KeyDown(object sender, KeyRoutedEventArgs e)
    {
      var isCtrlPressed = Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Control).HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);

      if (isCtrlPressed && e.Key == VirtualKey.A)
      {
        // Ctrl+A - Select all transcriptions
        ViewModel.SelectAllTranscriptionsCommand.Execute(null);
        UpdateTranscriptionSelectionVisuals();
        e.Handled = true;
      }
      else if (e.Key == VirtualKey.Escape)
      {
        // Escape - Clear transcription selection
        ViewModel.ClearTranscriptionSelectionCommand.Execute(null);
        UpdateTranscriptionSelectionVisuals();
        e.Handled = true;
      }
    }

    private void Transcription_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
      if (sender is ListViewItem listViewItem && listViewItem.DataContext is TranscriptionResponse transcription)
      {
        var isCtrlPressed = InputHelper.IsControlPressed();
        var isShiftPressed = InputHelper.IsShiftPressed();

        ViewModel.ToggleTranscriptionSelection(transcription.Id, isCtrlPressed, isShiftPressed);

        UpdateTranscriptionSelectionVisuals();
        e.Handled = true;
      }
    }

    private void UpdateTranscriptionSelectionVisuals()
    {
      // Update visual indicators for all transcription list items
      UpdateTranscriptionSelectionVisualsRecursive(this);
    }

    private void UpdateTranscriptionSelectionVisualsRecursive(DependencyObject element)
    {
      if (element == null || ViewModel == null)
        return;

      // Check if this is a ListViewItem with a TranscriptionResponse
      if (element is ListViewItem listViewItem && listViewItem.DataContext is TranscriptionResponse transcription)
      {
        var isSelected = ViewModel.IsTranscriptionSelected(transcription.Id);

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
        UpdateTranscriptionSelectionVisualsRecursive(child);
      }
    }

    private void UpdateEmptyStateVisibility()
    {
      if (EmptyTranscriptionState != null)
      {
        EmptyTranscriptionState.Visibility = ViewModel.Transcriptions.Count == 0
            ? Microsoft.UI.Xaml.Visibility.Visible
            : Microsoft.UI.Xaml.Visibility.Collapsed;
      }
    }

    private void CopyTranscriptionText_Click(object _, Microsoft.UI.Xaml.RoutedEventArgs __)
    {
      if (ViewModel.SelectedTranscription != null && !string.IsNullOrEmpty(ViewModel.TranscriptionText))
      {
        var dataPackage = new DataPackage();
        dataPackage.SetText(ViewModel.TranscriptionText);
        Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(dataPackage);
        _toastService?.ShowToast(ToastType.Success, "Copied", "Transcription text copied to clipboard");
      }
    }

    private void HelpButton_Click(object _, Microsoft.UI.Xaml.RoutedEventArgs __)
    {
      HelpOverlay.Title = "Transcribe Help";
      HelpOverlay.HelpText = "The Transcribe panel converts audio files to text using speech-to-text engines. Enter an audio ID and optional project ID, select an engine (Whisper, WhisperX, etc.) and language, then transcribe. Enable word timestamps for precise timing information, or diarization for speaker identification. View and edit transcriptions in the text editor below.";

      HelpOverlay.Shortcuts.Clear();
      HelpOverlay.Shortcuts.Add(new Controls.KeyboardShortcut { Key = "F5", Description = "Refresh transcriptions list" });
      HelpOverlay.Shortcuts.Add(new Controls.KeyboardShortcut { Key = "Ctrl+T", Description = "Start transcription" });

      HelpOverlay.Tips.Clear();
      HelpOverlay.Tips.Add("Use 'auto' language detection for automatic language identification");
      HelpOverlay.Tips.Add("Word timestamps provide precise timing for each word in the transcription");
      HelpOverlay.Tips.Add("Diarization identifies different speakers (requires WhisperX engine)");
      HelpOverlay.Tips.Add("Transcriptions can be edited directly in the text editor below");
      HelpOverlay.Tips.Add("Different engines offer different features - WhisperX supports diarization");

      HelpOverlay.Visibility = Microsoft.UI.Xaml.Visibility.Visible;
      HelpOverlay.Show();
    }

    private void Transcription_RightTapped(object sender, RightTappedRoutedEventArgs e)
    {
      if (sender is ListView listView && e.OriginalSource is FrameworkElement element)
      {
        var transcription = element.DataContext ?? listView.SelectedItem;
        if (transcription != null)
        {
          e.Handled = true;
          if (_contextMenuService != null)
          {
            var menu = new MenuFlyout();

            var editItem = new MenuFlyoutItem { Text = "Edit" };
            editItem.Click += async (_, _) => await HandleTranscriptionMenuClick("Edit", transcription);
            menu.Items.Add(editItem);

            var exportItem = new MenuFlyoutItem { Text = "Export" };
            exportItem.Click += async (_, _) => await HandleTranscriptionMenuClick("Export", transcription);
            menu.Items.Add(exportItem);

            menu.Items.Add(new MenuFlyoutSeparator());

            var deleteItem = new MenuFlyoutItem { Text = "Delete" };
            deleteItem.Click += async (_, _) => await HandleTranscriptionMenuClick("Delete", transcription);
            menu.Items.Add(deleteItem);

            var position = e.GetPosition(listView);
            _contextMenuService.ShowContextMenu(menu, listView, position);
          }
        }
      }
    }

    private async System.Threading.Tasks.Task HandleTranscriptionMenuClick(string action, object transcription)
    {
      try
      {
        switch (action.ToLower())
        {
          case "edit":
            // Select the transcription for editing
            ViewModel.SelectedTranscription = (TranscriptionResponse)transcription;
            _toastService?.ShowToast(ToastType.Info, "Edit Transcription", "Transcription selected for editing");
            break;
          case "export":
            _toastService?.ShowToast(ToastType.Info, "Export", "Export functionality is planned for a future release. Use the download button to save transcription results.");
            break;
          case "delete":
            if (ViewModel.DeleteTranscriptionCommand.CanExecute(transcription))
            {
              var dialog = new ContentDialog
              {
                Title = "Delete Transcription",
                Content = "Are you sure you want to delete this transcription? This action cannot be undone.",
                PrimaryButtonText = "Delete",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Close,
                XamlRoot = this.XamlRoot
              };

              var result = await dialog.ShowAsync();
              if (result == ContentDialogResult.Primary)
              {
                var transcriptionToDelete = (TranscriptionResponse)transcription;
                var transcriptionIndex = ViewModel.Transcriptions.IndexOf(transcriptionToDelete);

                await ViewModel.DeleteTranscriptionCommand.ExecuteAsync(transcriptionToDelete);

                // Register undo action
                if (_undoRedoService != null && transcriptionIndex >= 0)
                {
                  var actionObj = new SimpleAction(
                      "Delete Transcription",
                      () => ViewModel.Transcriptions.Insert(transcriptionIndex, transcriptionToDelete),
                      () => ViewModel.Transcriptions.Remove(transcriptionToDelete));
                  _undoRedoService.RegisterAction(actionObj);
                }

                _toastService?.ShowToast(ToastType.Success, "Deleted", "Transcription deleted");
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

    // Drag-and-drop handlers for transcription item reordering
    private void Transcription_DragStarting(UIElement sender, DragStartingEventArgs e)
    {
      if (sender is ListViewItem listViewItem && listViewItem.DataContext is TranscriptionResponse transcription)
      {
        _draggedTranscription = transcription;

        // Set drag data
        e.Data.SetText(transcription.Id);
        e.Data.Properties.Add("TranscriptionId", transcription.Id);
        e.Data.Properties.Add("TranscriptionText", transcription.Text ?? "Unnamed Transcription");

        // Reduce opacity of source element
        listViewItem.Opacity = 0.5;
      }
    }

    private void Transcription_DragItemsCompleted(UIElement sender, DragItemsCompletedEventArgs e)
    {
      // Clean up drag state
      if (sender is ListViewItem listViewItem)
      {
        listViewItem.Opacity = 1.0;
      }

      _dragDropService?.Cleanup();

      _draggedTranscription = null;
    }

    private void Transcription_DragOver(object sender, DragEventArgs e)
    {
      if (sender is ListViewItem listViewItem && _dragDropService != null)
      {
        e.AcceptedOperation = DataPackageOperation.Move;
        e.DragUIOverride.IsGlyphVisible = false;
        e.DragUIOverride.IsContentVisible = false;

        // Show drop target indicator
        var position = e.GetPosition(listViewItem);
        var dropPosition = DetermineTranscriptionDropPosition(listViewItem, position);
        _dragDropService.ShowDropTargetIndicator(listViewItem, dropPosition);
      }
    }

    private void Transcription_Drop(object sender, DragEventArgs e)
    {
      if (sender is ListViewItem listViewItem && _draggedTranscription != null && _dragDropService != null)
      {
        e.AcceptedOperation = DataPackageOperation.Move;

        // Hide drop indicator
        _dragDropService.HideDropTargetIndicator();
        _dragDropService.Cleanup();

        // Get target transcription
        if (listViewItem.DataContext is TranscriptionResponse targetTranscription)
        {
          var draggedTranscription = _draggedTranscription;
          var draggedIndex = ViewModel.Transcriptions.IndexOf(draggedTranscription);
          var targetIndex = ViewModel.Transcriptions.IndexOf(targetTranscription);

          if (draggedIndex >= 0 && targetIndex >= 0 && draggedIndex != targetIndex)
          {
            // Determine drop position
            var position = e.GetPosition(listViewItem);
            var dropPosition = DetermineTranscriptionDropPosition(listViewItem, position);

            // Reorder transcriptions in the collection
            ViewModel.Transcriptions.RemoveAt(draggedIndex);

            if (dropPosition == DropPosition.Before)
            {
              ViewModel.Transcriptions.Insert(targetIndex, draggedTranscription);
            }
            else if (dropPosition == DropPosition.After)
            {
              var newIndex = targetIndex < draggedIndex ? targetIndex + 1 : targetIndex;
              ViewModel.Transcriptions.Insert(newIndex, draggedTranscription);
            }
            else
            {
              // On - replace target
              ViewModel.Transcriptions.Insert(targetIndex, draggedTranscription);
            }

            _toastService?.ShowToast(ToastType.Success, "Reordered", "Transcription list reordered");
          }
        }

        // Clean up drag state
        _draggedTranscription = null;

        // Restore source element opacity
        if (e.OriginalSource is ListViewItem sourceItem)
        {
          sourceItem.Opacity = 1.0;
        }
      }
    }

    private void Transcription_DragLeave(object sender, DragEventArgs e)
    {
      _dragDropService?.HideDropTargetIndicator();
    }

    private async void TranscribeView_Loaded(object _, RoutedEventArgs __)
    {
      this.Loaded -= TranscribeView_Loaded;
      KeyboardNavigationHelper.SetupTabNavigation(this, 0);
      await ViewModel.InitializeAsync(CancellationToken.None);
    }

    private DropPosition DetermineTranscriptionDropPosition(ListViewItem target, Windows.Foundation.Point position)
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
  }
}