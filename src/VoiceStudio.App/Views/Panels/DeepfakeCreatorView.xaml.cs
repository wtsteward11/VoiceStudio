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
  /// DeepfakeCreatorView panel for face swapping and face replacement.
  /// </summary>
  public sealed partial class DeepfakeCreatorView : UserControl
  {
    public DeepfakeCreatorViewModel ViewModel { get; }
    private ToastNotificationService? _toastService;
    private ContextMenuService? _contextMenuService;
    private UndoRedoService? _undoRedoService;

    public DeepfakeCreatorView()
    {
      this.InitializeComponent();
      ViewModel = new DeepfakeCreatorViewModel(
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
        if (e.PropertyName == nameof(DeepfakeCreatorViewModel.ErrorMessage) && !string.IsNullOrEmpty(ViewModel.ErrorMessage))
        {
          _toastService?.ShowToast(ToastType.Error, "Deepfake Creator Error", ViewModel.ErrorMessage);
        }
        else if (e.PropertyName == nameof(DeepfakeCreatorViewModel.StatusMessage) && !string.IsNullOrEmpty(ViewModel.StatusMessage))
        {
          _toastService?.ShowToast(ToastType.Success, "Deepfake Creator", ViewModel.StatusMessage);
        }
      };

      // Setup keyboard navigation
      this.Loaded += DeepfakeCreatorView_KeyboardNavigation_Loaded;

      // Setup Escape key to close help overlay
      KeyboardNavigationHelper.SetupEscapeKeyHandling(this, () =>
      {
        if (HelpOverlay.IsVisible)
        {
          HelpOverlay.IsVisible = false;
        }
      });
    }

    private void DeepfakeCreatorView_KeyboardNavigation_Loaded(object sender, RoutedEventArgs e)
    {
      KeyboardNavigationHelper.SetupTabNavigation(this);
    }

    private async void BrowseSourceFaceButton_Click(object _, RoutedEventArgs __)
    {
      try
      {
        var picker = new FileOpenPicker();
        picker.FileTypeFilter.Add(".jpg");
        picker.FileTypeFilter.Add(".jpeg");
        picker.FileTypeFilter.Add(".png");
        picker.FileTypeFilter.Add(".bmp");
        picker.FileTypeFilter.Add(".webp");

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
          ViewModel.SourceFaceFilePath = file.Path;
        }
      }
      catch (Exception ex)
      {
        System.Diagnostics.Debug.WriteLine($"Unhandled error in event handler: {ex.Message}");
      }
    }

    private async void BrowseTargetMediaButton_Click(object _, RoutedEventArgs __)
    {
      try
      {
        var picker = new FileOpenPicker();

        // Add image formats
        picker.FileTypeFilter.Add(".jpg");
        picker.FileTypeFilter.Add(".jpeg");
        picker.FileTypeFilter.Add(".png");
        picker.FileTypeFilter.Add(".bmp");
        picker.FileTypeFilter.Add(".gif");
        picker.FileTypeFilter.Add(".webp");

        // Add video formats
        picker.FileTypeFilter.Add(".mp4");
        picker.FileTypeFilter.Add(".avi");
        picker.FileTypeFilter.Add(".mov");
        picker.FileTypeFilter.Add(".mkv");
        picker.FileTypeFilter.Add(".webm");

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
          ViewModel.TargetMediaFilePath = file.Path;
        }
      }
      catch (Exception ex)
      {
        System.Diagnostics.Debug.WriteLine($"Unhandled error in event handler: {ex.Message}");
      }
    }

    private void HelpButton_Click(object _, RoutedEventArgs __)
    {
      HelpOverlay.Title = "Deepfake Creator Help";
      HelpOverlay.HelpText = "The Deepfake Creator panel allows you to create face swaps and face replacements in images and videos using AI-powered deepfake engines. Select a source face image and a target image or video, then choose a deepfake engine (DeepFaceLab, FOMM, FaceSwap). IMPORTANT: Explicit consent from all parties is required for deepfake creation. All deepfakes are watermarked by default and logged for audit purposes. Use this feature responsibly and ethically. Different engines support different media types - DeepFaceLab supports both images and videos, while FOMM is optimized for video motion transfer.";

      HelpOverlay.Shortcuts.Clear();
      HelpOverlay.Shortcuts.Add(new Controls.KeyboardShortcut { Key = "Ctrl+O", Description = "Browse for files" });
      HelpOverlay.Shortcuts.Add(new Controls.KeyboardShortcut { Key = "Ctrl+C", Description = "Create deepfake" });
      HelpOverlay.Shortcuts.Add(new Controls.KeyboardShortcut { Key = "F5", Description = "Refresh jobs" });
      HelpOverlay.Shortcuts.Add(new Controls.KeyboardShortcut { Key = "Delete", Description = "Delete selected job" });

      HelpOverlay.Tips.Clear();
      HelpOverlay.Tips.Add("⚠️ CONSENT IS REQUIRED - You must have explicit consent from all parties");
      HelpOverlay.Tips.Add("All deepfakes are watermarked by default for ethical use");
      HelpOverlay.Tips.Add("DeepFaceLab is best for high-quality face replacement in videos");
      HelpOverlay.Tips.Add("FOMM is optimized for motion transfer and face animation");
      HelpOverlay.Tips.Add("Source face should be a clear, front-facing image");
      HelpOverlay.Tips.Add("Higher quality settings take longer but produce better results");
      HelpOverlay.Tips.Add("All operations are logged for audit purposes");
      HelpOverlay.Tips.Add("Use this feature responsibly and ethically");

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

    private async System.Threading.Tasks.Task HandleJobMenuClick(string action, object job)
    {
      try
      {
        switch (action.ToLower())
        {
          case "view":
            ViewModel.SelectedJob = (job as DeepfakeJobItem);
            _toastService?.ShowToast(ToastType.Info, "View Details", "Showing job details");
            break;
          case "export":
            await ExportDeepfakeJobAsync(job);
            break;
          case "delete":
            var dialog = new ContentDialog
            {
              Title = "Delete Deepfake Job",
              Content = "Are you sure you want to delete this deepfake job? This action cannot be undone.",
              PrimaryButtonText = "Delete",
              CloseButtonText = "Cancel",
              DefaultButton = ContentDialogButton.Close,
              XamlRoot = this.XamlRoot
            };

            var result = await dialog.ShowAsync();
            if (result == ContentDialogResult.Primary)
            {
              var jobToDelete = job as DeepfakeJobItem;
              if (jobToDelete == null) break;
              
              var jobIndex = ViewModel.DeepfakeJobs.IndexOf(jobToDelete);
              ViewModel.DeepfakeJobs.Remove(jobToDelete);

              // Register undo action
              if (_undoRedoService != null && jobIndex >= 0)
              {
                var actionObj = new SimpleAction(
                    "Delete Deepfake Job",
                    () => ViewModel.DeepfakeJobs.Insert(jobIndex, jobToDelete),
                    () => ViewModel.DeepfakeJobs.Remove(jobToDelete));
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

    private async System.Threading.Tasks.Task ExportDeepfakeJobAsync(object job)
    {
      try
      {
        var picker = new Windows.Storage.Pickers.FileSavePicker();
        picker.SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.DocumentsLibrary;
        picker.FileTypeChoices.Add("JSON", new[] { ".json" });
        picker.SuggestedFileName = "deepfake_job_export";

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
          _toastService?.ShowToast(ToastType.Success, "Export", "Deepfake job exported successfully");
        }
      }
      catch (Exception ex)
      {
        _toastService?.ShowToast(ToastType.Error, "Export Failed", ex.Message);
      }
    }
  }
}