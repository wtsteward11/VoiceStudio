using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml;
using VoiceStudio.App.Controls;
using VoiceStudio.App.Services;
using VoiceStudio.App.Services.UndoableActions;
using VoiceStudio.App.ViewModels;
using Windows.Storage;
using Windows.Storage.Pickers;
using System;

namespace VoiceStudio.App.Views.Panels
{
  public sealed partial class VideoGenView : UserControl
  {
    public VideoGenViewModel ViewModel { get; }
    private ContextMenuService? _contextMenuService;
    private ToastNotificationService? _toastService;
    private UndoRedoService? _undoRedoService;

    public VideoGenView()
    {
      this.InitializeComponent();
      var backendClient = VoiceStudio.App.Services.ServiceProvider.GetBackendClient();
      ViewModel = new VideoGenViewModel(AppServices.GetRequiredService<VoiceStudio.Core.Services.IViewModelContext>(), backendClient);
      this.DataContext = ViewModel;

      // Initialize services
      _contextMenuService = ServiceProvider.GetContextMenuService();
      _toastService = ServiceProvider.GetToastNotificationService();
      _undoRedoService = ServiceProvider.GetUndoRedoService();

      // Subscribe to ViewModel events for toast notifications
      ViewModel.PropertyChanged += (s, e) =>
      {
        if (e.PropertyName == nameof(VideoGenViewModel.ErrorMessage) && !string.IsNullOrEmpty(ViewModel.ErrorMessage))
        {
          _toastService?.ShowToast(ToastType.Error, "Video Generation Error", ViewModel.ErrorMessage);
        }
        else if (e.PropertyName == nameof(VideoGenViewModel.StatusMessage) && !string.IsNullOrEmpty(ViewModel.StatusMessage))
        {
          _toastService?.ShowToast(ToastType.Success, "Video Generation", ViewModel.StatusMessage);
        }
      };

      // Setup keyboard navigation
      this.Loaded += VideoGenView_Loaded;

      // Setup Escape key to close help overlay
      KeyboardNavigationHelper.SetupEscapeKeyHandling(this, () =>
      {
        if (HelpOverlay.IsVisible)
        {
          HelpOverlay.Close();
        }
      });

      // Setup Enter key to generate video (when prompt is focused)
      KeyboardNavigationHelper.SetupEnterKeyHandling(this, () =>
      {
        if (ViewModel.GenerateCommand.CanExecute(null))
        {
          ViewModel.GenerateCommand.Execute(null);
        }
      });
    }

    private void VideoGenView_Loaded(object _, RoutedEventArgs __)
    {
      // Setup Tab navigation order for this panel
      KeyboardNavigationHelper.SetupTabNavigation(this, 0);
    }

    private async void QualitySettings_Click(object _, RoutedEventArgs __)
    {
      try
      {
        var dialog = new ContentDialog
        {
          Title = "Quality Settings",
          Content = $"Current Settings:\n\nSteps: {ViewModel.Steps}\nCFG Scale: {ViewModel.CfgScale:F1}\nWidth: {ViewModel.Width}\nHeight: {ViewModel.Height}\nFPS: {ViewModel.Fps}\nDuration: {ViewModel.Duration}s\n\nQuality Preset: {ViewModel.SelectedQualityPreset?.Name ?? "Custom"}",
          PrimaryButtonText = "OK",
          XamlRoot = this.XamlRoot
        };
        await dialog.ShowAsync();
      }
      catch (Exception ex)
      {
        System.Diagnostics.Debug.WriteLine($"Unhandled error in event handler: {ex.Message}");
      }
    }

    private void HelpButton_Click(object _, Microsoft.UI.Xaml.RoutedEventArgs __)
    {
      HelpOverlay.Title = "Video Generation Help";
      HelpOverlay.HelpText = "The Video Generation panel allows you to create videos from text prompts, images, or audio using various AI engines. Select an engine, enter your prompt (or select input image/audio), adjust parameters (width, height, FPS, duration, steps, CFG scale), and generate. You can also upscale generated videos using FFmpeg AI or Real-ESRGAN.";

      HelpOverlay.Shortcuts.Clear();
      HelpOverlay.Shortcuts.Add(new Controls.KeyboardShortcut { Key = "Ctrl+Enter", Description = "Generate video" });
      HelpOverlay.Shortcuts.Add(new Controls.KeyboardShortcut { Key = "Enter", Description = "New line in prompt" });

      HelpOverlay.Tips.Clear();
      HelpOverlay.Tips.Add("Enter a detailed prompt describing the video you want to generate.");
      HelpOverlay.Tips.Add("For image-to-video engines (SVD), select an input image.");
      HelpOverlay.Tips.Add("For audio-to-video engines (SadTalker), select an input audio file.");
      HelpOverlay.Tips.Add("Higher step counts produce better quality but take longer.");
      HelpOverlay.Tips.Add("CFG scale controls how closely the engine follows your prompt.");
      HelpOverlay.Tips.Add("Set a seed value to reproduce the same video.");
      HelpOverlay.Tips.Add("Click 'Upscale Selected' to enhance video resolution.");

      HelpOverlay.Visibility = Microsoft.UI.Xaml.Visibility.Visible;
      HelpOverlay.Show();
    }

    private void Video_RightTapped(object sender, RightTappedRoutedEventArgs e)
    {
      if (sender is ListView listView && e.OriginalSource is FrameworkElement element)
      {
        var video = element.DataContext ?? listView.SelectedItem;
        if (video != null)
        {
          e.Handled = true;
          if (_contextMenuService != null)
          {
            var menu = new MenuFlyout();

            var playItem = new MenuFlyoutItem { Text = "Play" };
            playItem.Click += async (_, _) => await HandleVideoMenuClick("Play", video);
            menu.Items.Add(playItem);

            var exportItem = new MenuFlyoutItem { Text = "Export" };
            exportItem.Click += async (_, _) => await HandleVideoMenuClick("Export", video);
            menu.Items.Add(exportItem);

            var upscaleItem = new MenuFlyoutItem { Text = "Upscale" };
            upscaleItem.Click += async (_, _) => await HandleVideoMenuClick("Upscale", video);
            menu.Items.Add(upscaleItem);

            var duplicateItem = new MenuFlyoutItem { Text = "Duplicate" };
            duplicateItem.Click += async (_, _) => await HandleVideoMenuClick("Duplicate", video);
            menu.Items.Add(duplicateItem);

            menu.Items.Add(new MenuFlyoutSeparator());

            var deleteItem = new MenuFlyoutItem { Text = "Delete" };
            deleteItem.Click += async (_, _) => await HandleVideoMenuClick("Delete", video);
            menu.Items.Add(deleteItem);

            var position = e.GetPosition(listView);
            _contextMenuService.ShowContextMenu(menu, listView, position);
          }
        }
      }
    }

    private async System.Threading.Tasks.Task HandleVideoMenuClick(string action, object video)
    {
      try
      {
        switch (action.ToLower())
        {
          case "play":
            ViewModel.SelectedVideo = (video as GeneratedVideo);
            _toastService?.ShowToast(ToastType.Info, "Play Video", "Playing video");
            break;
          case "export":
            await ExportVideoAsync(video);
            break;
          case "upscale":
            if (ViewModel.UpscaleCommand.CanExecute(null))
            {
              await ((CommunityToolkit.Mvvm.Input.AsyncRelayCommand)ViewModel.UpscaleCommand).ExecuteAsync(null);
            }
            else
            {
              _toastService?.ShowToast(ToastType.Warning, "Upscale", "Please select a video to upscale");
            }
            break;
          case "duplicate":
            DuplicateVideo(video);
            break;
          case "delete":
            var dialog = new ContentDialog
            {
              Title = "Delete Video",
              Content = "Are you sure you want to delete this generated video? This action cannot be undone.",
              PrimaryButtonText = "Delete",
              CloseButtonText = "Cancel",
              DefaultButton = ContentDialogButton.Close,
              XamlRoot = this.XamlRoot
            };

            var result = await dialog.ShowAsync();
            if (result == ContentDialogResult.Primary)
            {
              var videoToDelete = video as GeneratedVideo;
              if (videoToDelete == null) break;
              
              var videoIndex = ViewModel.GeneratedVideos.IndexOf(videoToDelete);
              ViewModel.GeneratedVideos.Remove(videoToDelete);

              // Register undo action
              if (_undoRedoService != null && videoIndex >= 0)
              {
                var actionObj = new SimpleAction(
                    "Delete Generated Video",
                    () => ViewModel.GeneratedVideos.Insert(videoIndex, videoToDelete),
                    () => ViewModel.GeneratedVideos.Remove(videoToDelete));
                _undoRedoService.RegisterAction(actionObj);
              }

              _toastService?.ShowToast(ToastType.Success, "Deleted", "Video deleted");
            }
            break;
        }
      }
      catch (Exception ex)
      {
        _toastService?.ShowToast(ToastType.Error, "Error", $"Failed to {action}: {ex.Message}");
      }
    }

    private async System.Threading.Tasks.Task ExportVideoAsync(object video)
    {
      if (video is not GeneratedVideo generatedVideo)
        return;

      try
      {
        var picker = new FileSavePicker();
        picker.SuggestedStartLocation = PickerLocationId.VideosLibrary;
        picker.FileTypeChoices.Add("MP4", new List<string> { ".mp4" });
        picker.FileTypeChoices.Add("AVI", new List<string> { ".avi" });
        picker.FileTypeChoices.Add("MOV", new List<string> { ".mov" });
        picker.SuggestedFileName = $"generated_video_{DateTime.Now:yyyyMMdd_HHmmss}";

        // Required for WinUI 3 unpackaged desktop apps
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindowInstance);
        WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);

        var file = await picker.PickSaveFileAsync();
        if (file != null)
        {
          if (!string.IsNullOrEmpty(generatedVideo.VideoUrl))
          {
            // Download from URL
            using (var client = new System.Net.Http.HttpClient())
            {
              var videoBytes = await client.GetByteArrayAsync(generatedVideo.VideoUrl);
              await FileIO.WriteBytesAsync(file, videoBytes);
              _toastService?.ShowToast(ToastType.Success, "Exported", $"Video saved to {file.Name}");
            }
          }
          else
          {
            _toastService?.ShowToast(ToastType.Warning, "Export", "Video URL not available");
          }
        }
      }
      catch (Exception ex)
      {
        _toastService?.ShowToast(ToastType.Error, "Export Failed", ex.Message);
      }
    }

    private void DuplicateVideo(object video)
    {
      if (video is GeneratedVideo generatedVideo)
      {
        var duplicate = new GeneratedVideo
        {
          VideoId = Guid.NewGuid().ToString(),
          VideoUrl = generatedVideo.VideoUrl,
          Width = generatedVideo.Width,
          Height = generatedVideo.Height,
          Fps = generatedVideo.Fps,
          Duration = generatedVideo.Duration,
          Prompt = $"{generatedVideo.Prompt} (Copy)",
          Engine = generatedVideo.Engine,
          QualityMetrics = generatedVideo.QualityMetrics
        };
        ViewModel.GeneratedVideos.Insert(0, duplicate);
        ViewModel.SelectedVideo = duplicate;
        _toastService?.ShowToast(ToastType.Success, "Duplicated", "Video duplicated");
      }
    }
  }
}