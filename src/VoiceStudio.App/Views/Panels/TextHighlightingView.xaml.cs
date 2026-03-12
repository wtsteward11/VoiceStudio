using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml;
using VoiceStudio.App.ViewModels;
using VoiceStudio.App.Services;
using VoiceStudio.App.Services.UndoableActions;
using System;

namespace VoiceStudio.App.Views.Panels
{
  /// <summary>
  /// TextHighlightingView panel for text highlighting with audio synchronization.
  /// </summary>
  public sealed partial class TextHighlightingView : UserControl
  {
    public TextHighlightingViewModel ViewModel { get; }
    private ContextMenuService? _contextMenuService;
    private ToastNotificationService? _toastService;
    private UndoRedoService? _undoRedoService;

    public TextHighlightingView()
    {
      this.InitializeComponent();
      ViewModel = new TextHighlightingViewModel(
          AppServices.GetRequiredService<VoiceStudio.Core.Services.IViewModelContext>(),
          VoiceStudio.App.Services.ServiceProvider.GetBackendClient(),
          AppServices.GetProjectsClient()
      );
      DataContext = ViewModel;

      // Initialize services
      _contextMenuService = ServiceProvider.GetContextMenuService();
      _toastService = ServiceProvider.GetToastNotificationService();
      _undoRedoService = ServiceProvider.GetUndoRedoService();

      // Subscribe to ViewModel events for toast notifications
      ViewModel.PropertyChanged += (s, e) =>
      {
        if (e.PropertyName == nameof(TextHighlightingViewModel.ErrorMessage) && !string.IsNullOrEmpty(ViewModel.ErrorMessage))
        {
          _toastService?.ShowToast(ToastType.Error, "Text Highlighting Error", ViewModel.ErrorMessage);
        }
        else if (e.PropertyName == nameof(TextHighlightingViewModel.StatusMessage) && !string.IsNullOrEmpty(ViewModel.StatusMessage))
        {
          _toastService?.ShowToast(ToastType.Success, "Text Highlighting", ViewModel.StatusMessage);
        }
      };

      // Setup keyboard navigation
      this.Loaded += TextHighlightingView_KeyboardNavigation_Loaded;

      // Setup Escape key to close help overlay
      KeyboardNavigationHelper.SetupEscapeKeyHandling(this, () =>
      {
        if (HelpOverlay.IsVisible)
        {
          HelpOverlay.IsVisible = false;
        }
      });
    }

    private void TextHighlightingView_KeyboardNavigation_Loaded(object _, RoutedEventArgs __)
    {
      KeyboardNavigationHelper.SetupTabNavigation(this);
    }

    private void HelpButton_Click(object _, Microsoft.UI.Xaml.RoutedEventArgs __)
    {
      HelpOverlay.Title = "Text Highlighting Help";
      HelpOverlay.HelpText = "The Text Highlighting panel synchronizes text with audio playback, highlighting words as they are spoken. Create a highlighting session by selecting an audio file and entering the corresponding text, then sync the highlighting with audio playback time. The panel displays text segments with their time ranges and highlights the active segment during playback, helping you visualize word timing and audio-text alignment.";

      HelpOverlay.Shortcuts.Clear();
      HelpOverlay.Shortcuts.Add(new Controls.KeyboardShortcut { Key = "Space", Description = "Play/Pause audio" });

      HelpOverlay.Tips.Clear();
      HelpOverlay.Tips.Add("Create a session to link text with audio for highlighting");
      HelpOverlay.Tips.Add("Sync highlighting synchronizes text segments with audio timing");
      HelpOverlay.Tips.Add("Active segments are highlighted as audio plays");
      HelpOverlay.Tips.Add("Time slider controls the current playback position");
      HelpOverlay.Tips.Add("Text segments show their time ranges and durations");
      HelpOverlay.Tips.Add("Update session to modify highlighting timing and segments");

      HelpOverlay.Visibility = Microsoft.UI.Xaml.Visibility.Visible;
      HelpOverlay.Show();
    }

    private void Segment_RightTapped(object sender, RightTappedRoutedEventArgs e)
    {
      if (sender is ListView listView && e.OriginalSource is FrameworkElement element)
      {
        var segment = element.DataContext ?? listView.SelectedItem;
        if (segment != null)
        {
          e.Handled = true;
          if (_contextMenuService != null)
          {
            var menu = new MenuFlyout();

            var editItem = new MenuFlyoutItem { Text = "Edit" };
            editItem.Click += async (_, _) => await HandleSegmentMenuClick("Edit", segment);
            menu.Items.Add(editItem);

            var jumpToItem = new MenuFlyoutItem { Text = "Jump to Time" };
            jumpToItem.Click += async (_, _) => await HandleSegmentMenuClick("JumpTo", segment);
            menu.Items.Add(jumpToItem);

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

    private async System.Threading.Tasks.Task HandleSegmentMenuClick(string action, object segmentObj)
    {
      try
      {
        var segment = (HighlightTextSegmentItem)segmentObj;
        switch (action.ToLower())
        {
          case "edit":
            ViewModel.ActiveSegment = segment;
            _toastService?.ShowToast(ToastType.Info, "Edit Segment", "Segment selected for editing");
            break;
          case "jumpto":
            _toastService?.ShowToast(ToastType.Info, "Jump to Time", "Jumping to segment time");
            break;
          case "duplicate":
            DuplicateSegment(segment);
            break;
          case "delete":
            var dialog = new ContentDialog
            {
              Title = "Delete Segment",
              Content = "Are you sure you want to delete this text segment? This action cannot be undone.",
              PrimaryButtonText = "Delete",
              CloseButtonText = "Cancel",
              DefaultButton = ContentDialogButton.Close,
              XamlRoot = this.XamlRoot
            };

            var result = await dialog.ShowAsync();
            if (result == ContentDialogResult.Primary)
            {
              var segmentToDelete = segment;
              var segmentIndex = ViewModel.Segments.IndexOf(segment);

              ViewModel.Segments.Remove(segment);

              // Register undo action
              if (_undoRedoService != null && segmentIndex >= 0)
              {
                var actionObj = new SimpleAction(
                    "Delete Text Segment",
                    () => ViewModel.Segments.Insert(segmentIndex, segmentToDelete),
                    () => ViewModel.Segments.Remove(segmentToDelete));
                _undoRedoService.RegisterAction(actionObj);
              }

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

    private void DuplicateSegment(object segment)
    {
      try
      {
        var segmentType = segment.GetType();
        var duplicatedSegment = Activator.CreateInstance(segmentType);
        if (duplicatedSegment != null)
        {
          foreach (var prop in segmentType.GetProperties())
          {
            if (prop.CanRead && prop.CanWrite && prop.GetIndexParameters().Length == 0)
            {
              var value = prop.GetValue(segment);
              prop.SetValue(duplicatedSegment, value);
            }
          }

          var index = ViewModel.Segments.IndexOf((HighlightTextSegmentItem)segment);
          ViewModel.Segments.Insert(index + 1, (HighlightTextSegmentItem)duplicatedSegment);
          _toastService?.ShowToast(ToastType.Success, "Duplicated", "Segment duplicated");
        }
      }
      catch (Exception ex)
      {
        _toastService?.ShowToast(ToastType.Error, "Duplicate Failed", ex.Message);
      }
    }
  }
}