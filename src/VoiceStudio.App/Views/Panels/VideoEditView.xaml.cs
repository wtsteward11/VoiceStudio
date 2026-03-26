using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml;
using VoiceStudio.App.Controls;
using VoiceStudio.App.Services;
using VoiceStudio.App.Services.UndoableActions;
using VoiceStudio.App.ViewModels;
using System;
using Windows.ApplicationModel.DataTransfer;

namespace VoiceStudio.App.Views.Panels
{
  public sealed partial class VideoEditView : UserControl
  {
    public VideoEditViewModel ViewModel { get; }
    private ContextMenuService? _contextMenuService;
    private ToastNotificationService? _toastService;
    private UndoRedoService? _undoRedoService;

    public VideoEditView()
    {
      this.InitializeComponent();
      var videoEditClient = VoiceStudio.App.Services.ServiceProvider.GetVideoEditClient();
      ViewModel = new VideoEditViewModel(AppServices.GetRequiredService<VoiceStudio.Core.Services.IViewModelContext>(), videoEditClient);
      this.DataContext = ViewModel;

      // Initialize services
      _contextMenuService = ServiceProvider.GetContextMenuService();
      _toastService = ServiceProvider.GetToastNotificationService();
      _undoRedoService = ServiceProvider.GetUndoRedoService();

      // Subscribe to ViewModel events for toast notifications
      ViewModel.PropertyChanged += (s, e) =>
      {
        if (e.PropertyName == nameof(VideoEditViewModel.ErrorMessage) && !string.IsNullOrEmpty(ViewModel.ErrorMessage))
        {
          _toastService?.ShowToast(ToastType.Error, "Video Editing Error", ViewModel.ErrorMessage);
        }
        else if (e.PropertyName == nameof(VideoEditViewModel.StatusMessage) && !string.IsNullOrEmpty(ViewModel.StatusMessage))
        {
          _toastService?.ShowToast(ToastType.Success, "Video Editing", ViewModel.StatusMessage);
        }
      };

      // Setup keyboard navigation
      this.Loaded += VideoEditView_KeyboardNavigation_Loaded;

      // Setup Escape key to close help overlay
      KeyboardNavigationHelper.SetupEscapeKeyHandling(this, () =>
      {
        if (HelpOverlay.IsVisible)
        {
          HelpOverlay.IsVisible = false;
        }
      });
    }

    private void VideoEditView_KeyboardNavigation_Loaded(object _, RoutedEventArgs __)
    {
      KeyboardNavigationHelper.SetupTabNavigation(this);
    }

    private void HelpButton_Click(object _, Microsoft.UI.Xaml.RoutedEventArgs __)
    {
      HelpOverlay.Title = "Video Editing Help";
      HelpOverlay.HelpText = "The Video Editing panel allows you to edit videos by trimming, splitting, applying effects and transitions, and exporting in various formats. Select a video file, use the trim controls to set start and end times, apply effects or transitions, and export your edited video.";

      HelpOverlay.Shortcuts.Clear();
      HelpOverlay.Shortcuts.Add(new Controls.KeyboardShortcut { Key = "Ctrl+O", Description = "Select video file" });
      HelpOverlay.Shortcuts.Add(new Controls.KeyboardShortcut { Key = "Ctrl+T", Description = "Trim video" });
      HelpOverlay.Shortcuts.Add(new Controls.KeyboardShortcut { Key = "Ctrl+S", Description = "Split video" });
      HelpOverlay.Shortcuts.Add(new Controls.KeyboardShortcut { Key = "Ctrl+E", Description = "Export video" });

      HelpOverlay.Tips.Clear();
      HelpOverlay.Tips.Add("Select a video file to begin editing.");
      HelpOverlay.Tips.Add("Use trim controls to set the start and end times for trimming.");
      HelpOverlay.Tips.Add("Split video at a specific time point to create two separate videos.");
      HelpOverlay.Tips.Add("Apply effects like brightness, contrast, or filters to enhance your video.");
      HelpOverlay.Tips.Add("Add transitions like fade in/out or cross fade between clips.");
      HelpOverlay.Tips.Add("Export your edited video in various formats (MP4, AVI, MOV, etc.).");
      HelpOverlay.Tips.Add("Adjust export quality to balance file size and video quality.");

      HelpOverlay.Visibility = Microsoft.UI.Xaml.Visibility.Visible;
      HelpOverlay.Show();
    }

    private void VideoPath_RightTapped(object sender, RightTappedRoutedEventArgs e)
    {
      e.Handled = true;
      if (_contextMenuService != null && ViewModel != null && !string.IsNullOrEmpty(ViewModel.SelectedVideoPath))
      {
        var menu = new MenuFlyout();

        var copyPathItem = new MenuFlyoutItem { Text = "Copy Path" };
        copyPathItem.Click += (_, _) =>
        {
          var dataPackage = new Windows.ApplicationModel.DataTransfer.DataPackage();
          dataPackage.SetText(ViewModel.SelectedVideoPath);
          Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(dataPackage);
          _toastService?.ShowSuccess("Path copied to clipboard", "Copy Path");
        };
        menu.Items.Add(copyPathItem);

        var openFolderItem = new MenuFlyoutItem { Text = "Open in File Explorer" };
        openFolderItem.Click += async (_, _) =>
        {
          try
          {
            var file = await Windows.Storage.StorageFile.GetFileFromPathAsync(ViewModel.SelectedVideoPath);
            var folder = await file.GetParentAsync();
            await Windows.System.Launcher.LaunchFolderAsync(folder);
          }
          catch (Exception ex)
          {
            _toastService?.ShowError($"Failed to open folder: {ex.Message}", "Error");
          }
        };
        menu.Items.Add(openFolderItem);

        var target = sender as UIElement;
        if (target != null)
        {
          _contextMenuService.ShowContextMenu(menu, target, e.GetPosition(target));
        }
      }
    }
  }
}