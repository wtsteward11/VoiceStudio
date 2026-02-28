using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml;
using Windows.Storage.Pickers;
using VoiceStudio.App.ViewModels;
using VoiceStudio.App.Services;
using VoiceStudio.App.Services.UndoableActions;
using System;

namespace VoiceStudio.App.Views.Panels
{
  /// <summary>
  /// UpscalingView panel for image and video upscaling.
  /// </summary>
  public sealed partial class UpscalingView : UserControl
  {
    public UpscalingViewModel ViewModel { get; }
    private ToastNotificationService? _toastService;
    private ContextMenuService? _contextMenuService;
    private UndoRedoService? _undoRedoService;

    public UpscalingView()
    {
      this.InitializeComponent();
      ViewModel = new UpscalingViewModel(
          AppServices.GetRequiredService<VoiceStudio.Core.Services.IViewModelContext>(),
          VoiceStudio.App.Services.ServiceProvider.GetBackendClient()
      );
      DataContext = ViewModel;

      // Initialize services
      _toastService = ServiceProvider.GetToastNotificationService();
      _contextMenuService = ServiceProvider.GetContextMenuService();
      _undoRedoService = ServiceProvider.GetUndoRedoService();

      // Subscribe to ViewModel events for toast notifications
      ViewModel.PropertyChanged += (s, e) =>
      {
        if (e.PropertyName == nameof(UpscalingViewModel.ErrorMessage) && !string.IsNullOrEmpty(ViewModel.ErrorMessage))
        {
          _toastService?.ShowToast(ToastType.Error, "Upscaling Error", ViewModel.ErrorMessage);
        }
        else if (e.PropertyName == nameof(UpscalingViewModel.StatusMessage) && !string.IsNullOrEmpty(ViewModel.StatusMessage))
        {
          _toastService?.ShowToast(ToastType.Success, "Upscaling", ViewModel.StatusMessage);
        }
      };

      // Setup keyboard navigation
      this.Loaded += UpscalingView_KeyboardNavigation_Loaded;

      // Setup Escape key to close help overlay
      KeyboardNavigationHelper.SetupEscapeKeyHandling(this, () =>
      {
        if (HelpOverlay.IsVisible)
        {
          HelpOverlay.IsVisible = false;
        }
      });
    }

    private void UpscalingView_KeyboardNavigation_Loaded(object _, RoutedEventArgs __)
    {
      KeyboardNavigationHelper.SetupTabNavigation(this);
    }

    private async void BrowseButton_Click(object sender, RoutedEventArgs e)
    {
      try
      {
        var picker = new FileOpenPicker();

        // Set up file type filters based on selected media type
        if (ViewModel.SelectedMediaType == "image")
        {
          picker.FileTypeFilter.Add(".jpg");
          picker.FileTypeFilter.Add(".jpeg");
          picker.FileTypeFilter.Add(".png");
          picker.FileTypeFilter.Add(".bmp");
          picker.FileTypeFilter.Add(".gif");
          picker.FileTypeFilter.Add(".webp");
        }
        else
        {
          picker.FileTypeFilter.Add(".mp4");
          picker.FileTypeFilter.Add(".avi");
          picker.FileTypeFilter.Add(".mov");
          picker.FileTypeFilter.Add(".mkv");
          picker.FileTypeFilter.Add(".webm");
        }

        var window = App.MainWindowInstance;
        if (window == null)
        {
          return;
        }
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(window);
        WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);

        var file = await picker.PickSingleFileAsync();
        if (file != null)
        {
          ViewModel.SelectedFilePath = file.Path;
        }
      }
      catch (Exception ex)
      {
        System.Diagnostics.Debug.WriteLine($"Unhandled error in event handler: {ex.Message}");
      }
    }

    private void HelpButton_Click(object _, RoutedEventArgs __)
    {
      HelpOverlay.Title = "Upscaling Help";
      HelpOverlay.HelpText = "The Upscaling panel allows you to upscale images and videos using various AI-powered upscaling engines. Select a file (image or video), choose an upscaling engine (Real-ESRGAN, ESRGAN, Waifu2x, SwinIR), and set the scale factor (2×, 4×, or 8×). The upscaling process will enhance the resolution of your media while preserving quality. Different engines are optimized for different types of content - Real-ESRGAN works well for general images and videos, while Waifu2x is specialized for anime-style images. Monitor upscaling jobs in the jobs list to track progress and download completed upscaled files.";

      HelpOverlay.Shortcuts.Clear();
      HelpOverlay.Shortcuts.Add(new Controls.KeyboardShortcut { Key = "Ctrl+O", Description = "Browse for file" });
      HelpOverlay.Shortcuts.Add(new Controls.KeyboardShortcut { Key = "Ctrl+U", Description = "Start upscaling" });
      HelpOverlay.Shortcuts.Add(new Controls.KeyboardShortcut { Key = "F5", Description = "Refresh jobs" });
      HelpOverlay.Shortcuts.Add(new Controls.KeyboardShortcut { Key = "Delete", Description = "Delete selected job" });

      HelpOverlay.Tips.Clear();
      HelpOverlay.Tips.Add("Real-ESRGAN supports both images and videos");
      HelpOverlay.Tips.Add("Waifu2x is optimized for anime-style images");
      HelpOverlay.Tips.Add("Higher scale factors (4×, 8×) take longer to process");
      HelpOverlay.Tips.Add("Upscaling quality depends on the original image resolution");
      HelpOverlay.Tips.Add("Video upscaling processes frame-by-frame and can be time-consuming");
      HelpOverlay.Tips.Add("Check job status in the jobs list to monitor progress");
      HelpOverlay.Tips.Add("Completed jobs show output file paths for download");
      HelpOverlay.Tips.Add("Different engines have different supported scale factors");

      HelpOverlay.Visibility = Microsoft.UI.Xaml.Visibility.Visible;
      HelpOverlay.Show();
    }

    private void Job_RightTapped(object sender, RightTappedRoutedEventArgs e)
    {
      if (sender is ListView listView && e.OriginalSource is FrameworkElement element)
      {
        var job = element.DataContext ?? listView.SelectedItem;
        if (job != null)
        {
          e.Handled = true;
          if (_contextMenuService != null)
          {
            var menu = new MenuFlyout();

            var viewItem = new MenuFlyoutItem { Text = "View Details" };
            viewItem.Click += async (_, _) => await HandleJobMenuClick("View", job);
            menu.Items.Add(viewItem);

            var exportItem = new MenuFlyoutItem { Text = "Export Result" };
            exportItem.Click += async (_, _) => await HandleJobMenuClick("Export", job);
            menu.Items.Add(exportItem);

            menu.Items.Add(new MenuFlyoutSeparator());

            var deleteItem = new MenuFlyoutItem { Text = "Delete" };
            deleteItem.Click += async (_, _) => await HandleJobMenuClick("Delete", job);
            menu.Items.Add(deleteItem);

            var position = e.GetPosition(listView);
            _contextMenuService.ShowContextMenu(menu, listView, position);
          }
        }
      }
    }

    private async System.Threading.Tasks.Task HandleJobMenuClick(string action, object jobObj)
    {
      try
      {
        var job = jobObj as UpscalingJobItem;
        if (job == null) return;
        
        switch (action.ToLower())
        {
          case "view":
            ViewModel.SelectedJob = job;
            _toastService?.ShowToast(ToastType.Info, "View Details", "Showing job details");
            break;
          case "export":
            await ExportUpscalingJobAsync(job);
            break;
          case "delete":
            var dialog = new ContentDialog
            {
              Title = "Delete Upscaling Job",
              Content = "Are you sure you want to delete this upscaling job? This action cannot be undone.",
              PrimaryButtonText = "Delete",
              CloseButtonText = "Cancel",
              DefaultButton = ContentDialogButton.Close,
              XamlRoot = this.XamlRoot
            };

            var result = await dialog.ShowAsync();
            if (result == ContentDialogResult.Primary)
            {
              var jobToDelete = job;
              var jobIndex = ViewModel.UpscalingJobs.IndexOf(job);

              ViewModel.UpscalingJobs.Remove(job);

              // Register undo action
              if (_undoRedoService != null && jobIndex >= 0)
              {
                var actionObj = new SimpleAction(
                    "Delete Upscaling Job",
                    () => ViewModel.UpscalingJobs.Insert(jobIndex, jobToDelete),
                    () => ViewModel.UpscalingJobs.Remove(jobToDelete));
                _undoRedoService.RegisterAction(actionObj);
              }

              _toastService?.ShowToast(ToastType.Success, "Deleted", "Job deleted");
            }
            break;
        }
      }
      catch (Exception ex)
      {
        _toastService?.ShowToast(ToastType.Error, "Error", $"Failed to {action}: {ex.Message}");
      }
    }

    private async System.Threading.Tasks.Task ExportUpscalingJobAsync(object job)
    {
      try
      {
        var picker = new Windows.Storage.Pickers.FileSavePicker();
        picker.SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.DocumentsLibrary;
        picker.FileTypeChoices.Add("JSON", new[] { ".json" });
        picker.SuggestedFileName = "upscaling_job_export";

        var file = await picker.PickSaveFileAsync();
        if (file != null)
        {
          var jobType = job.GetType();
          var jsonData = new
          {
            Id = jobType.GetProperty("Id")?.GetValue(job)?.ToString() ?? "unknown",
            Status = jobType.GetProperty("Status")?.GetValue(job)?.ToString() ?? "unknown",
            Created = jobType.GetProperty("Created")?.GetValue(job)?.ToString() ?? DateTime.UtcNow.ToString()
          };
          var content = System.Text.Json.JsonSerializer.Serialize(jsonData, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
          await Windows.Storage.FileIO.WriteTextAsync(file, content);
          _toastService?.ShowToast(ToastType.Success, "Export", "Upscaling job exported successfully");
        }
      }
      catch (Exception ex)
      {
        _toastService?.ShowToast(ToastType.Error, "Export Failed", ex.Message);
      }
    }
  }
}