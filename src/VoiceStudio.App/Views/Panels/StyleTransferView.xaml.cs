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
  /// StyleTransferView panel for voice style transfer.
  /// </summary>
  public sealed partial class StyleTransferView : UserControl
  {
    public StyleTransferViewModel ViewModel { get; }
    private ContextMenuService? _contextMenuService;
    private ToastNotificationService? _toastService;
    private UndoRedoService? _undoRedoService;

    public StyleTransferView()
    {
      this.InitializeComponent();
      ViewModel = new StyleTransferViewModel(
          AppServices.GetRequiredService<VoiceStudio.Core.Services.IViewModelContext>(),
          VoiceStudio.App.Services.ServiceProvider.GetBackendClient(),
          AppServices.GetProjectsClient(),
          ServiceProvider.GetProfilesClient()
      );
      DataContext = ViewModel;

      // Initialize services
      _contextMenuService = ServiceProvider.GetContextMenuService();
      _toastService = ServiceProvider.GetToastNotificationService();
      _undoRedoService = ServiceProvider.GetUndoRedoService();

      // Subscribe to ViewModel events for toast notifications
      ViewModel.PropertyChanged += (_, e) =>
      {
        if (e.PropertyName == nameof(StyleTransferViewModel.ErrorMessage) && !string.IsNullOrEmpty(ViewModel.ErrorMessage))
        {
          _toastService?.ShowToast(ToastType.Error, "Style Transfer Error", ViewModel.ErrorMessage);
        }
        else if (e.PropertyName == nameof(StyleTransferViewModel.StatusMessage) && !string.IsNullOrEmpty(ViewModel.StatusMessage))
        {
          _toastService?.ShowToast(ToastType.Success, "Style Transfer", ViewModel.StatusMessage);
        }
      };

      // Setup keyboard navigation
      this.Loaded += StyleTransferView_KeyboardNavigation_Loaded;

      // Setup Escape key to close help overlay
      KeyboardNavigationHelper.SetupEscapeKeyHandling(this, () =>
      {
        if (HelpOverlay.IsVisible)
        {
          HelpOverlay.IsVisible = false;
        }
      });
    }

    private void StyleTransferView_KeyboardNavigation_Loaded(object _, RoutedEventArgs __)
    {
      KeyboardNavigationHelper.SetupTabNavigation(this);
    }

    private void HelpButton_Click(object _, Microsoft.UI.Xaml.RoutedEventArgs __)
    {
      HelpOverlay.Title = "Voice Style Transfer Help";
      HelpOverlay.HelpText = "The Voice Style Transfer panel allows you to transfer voice style characteristics from one voice profile to another while preserving the original content. Select a source audio file and a target voice style preset or voice profile, then apply style transfer with customizable strength. Use style presets for quick style application, or select a voice profile for custom style transfer. Adjust transfer strength to balance between preserving content and applying style characteristics.";

      HelpOverlay.Shortcuts.Clear();
      HelpOverlay.Shortcuts.Add(new Controls.KeyboardShortcut { Key = "Ctrl+T", Description = "Transfer style" });
      HelpOverlay.Shortcuts.Add(new Controls.KeyboardShortcut { Key = "Ctrl+P", Description = "Preview transfer" });

      HelpOverlay.Tips.Clear();
      HelpOverlay.Tips.Add("Style transfer modifies voice characteristics while preserving content");
      HelpOverlay.Tips.Add("Higher transfer strength applies more style characteristics");
      HelpOverlay.Tips.Add("Lower transfer strength preserves more original voice characteristics");
      HelpOverlay.Tips.Add("Use style presets for quick application of predefined styles");
      HelpOverlay.Tips.Add("Select voice profiles for custom style transfer");
      HelpOverlay.Tips.Add("Preview style transfer before applying to save processing time");
      HelpOverlay.Tips.Add("Style transfer can simulate emotions, accents, or speaking styles");

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
            ViewModel.SelectedJob = (StyleTransferJobItem)job;
            _toastService?.ShowToast(ToastType.Info, "View Job", "Job details displayed");
            break;
          case "export":
            await ExportStyleTransferJobAsync(job);
            break;
          case "delete":
            var dialog = new ContentDialog
            {
              Title = "Delete Job",
              Content = "Are you sure you want to delete this style transfer job? This action cannot be undone.",
              PrimaryButtonText = "Delete",
              CloseButtonText = "Cancel",
              DefaultButton = ContentDialogButton.Close,
              XamlRoot = this.XamlRoot
            };

            var result = await dialog.ShowAsync();
            if (result == ContentDialogResult.Primary)
            {
              var jobToDelete = (StyleTransferJobItem)job;
              var jobIndex = ViewModel.Jobs.IndexOf(jobToDelete);

              ViewModel.Jobs.Remove(jobToDelete);

              // Register undo action
              if (_undoRedoService != null && jobIndex >= 0)
              {
                var actionObj = new SimpleAction(
                    "Delete Style Transfer Job",
                    () => ViewModel.Jobs.Insert(jobIndex, jobToDelete),
                    () => ViewModel.Jobs.Remove(jobToDelete));
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

    private async System.Threading.Tasks.Task ExportStyleTransferJobAsync(object job)
    {
      try
      {
        var picker = new Windows.Storage.Pickers.FileSavePicker();
        picker.SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.DocumentsLibrary;
        picker.FileTypeChoices.Add("JSON", new[] { ".json" });
        picker.SuggestedFileName = "style_transfer_job_export";

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
          _toastService?.ShowToast(ToastType.Success, "Export", "Style transfer job exported successfully");
        }
      }
      catch (Exception ex)
      {
        _toastService?.ShowToast(ToastType.Error, "Export Failed", ex.Message);
      }
    }
  }
}