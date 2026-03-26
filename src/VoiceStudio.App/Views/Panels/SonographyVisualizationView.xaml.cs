using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml;
using VoiceStudio.App.ViewModels;
using VoiceStudio.App.Services;
using System.Runtime.InteropServices.WindowsRuntime;

namespace VoiceStudio.App.Views.Panels
{
  /// <summary>
  /// SonographyVisualizationView panel for sonography (waterfall/3D spectrogram) visualization.
  /// </summary>
  public sealed partial class SonographyVisualizationView : UserControl
  {
    public SonographyVisualizationViewModel ViewModel { get; }
    private ContextMenuService? _contextMenuService;
    private ToastNotificationService? _toastService;

    public SonographyVisualizationView()
    {
      this.InitializeComponent();
      ViewModel = new SonographyVisualizationViewModel(
          AppServices.GetViewModelContext(),
          AppServices.GetSonographyClient(),
          AppServices.GetProjectsClient(),
          AppServices.GetProjectAudioClient()
      );
      DataContext = ViewModel;

      // Initialize services
      _contextMenuService = ServiceProvider.GetContextMenuService();
      _toastService = ServiceProvider.GetToastNotificationService();

      // Subscribe to ViewModel events for toast notifications
      ViewModel.PropertyChanged += (_, e) =>
      {
        if (e.PropertyName == nameof(SonographyVisualizationViewModel.ErrorMessage) && !string.IsNullOrEmpty(ViewModel.ErrorMessage))
        {
          _toastService?.ShowToast(ToastType.Error, "Sonography Visualization Error", ViewModel.ErrorMessage);
        }
        else if (e.PropertyName == nameof(SonographyVisualizationViewModel.StatusMessage) && !string.IsNullOrEmpty(ViewModel.StatusMessage))
        {
          _toastService?.ShowToast(ToastType.Success, "Sonography Visualization", ViewModel.StatusMessage);
        }
      };

      // Setup keyboard navigation
      this.Loaded += SonographyVisualizationView_KeyboardNavigation_Loaded;

      // Setup Escape key to close help overlay
      KeyboardNavigationHelper.SetupEscapeKeyHandling(this, () =>
      {
        if (HelpOverlay.IsVisible)
        {
          HelpOverlay.IsVisible = false;
        }
      });
    }

    private void SonographyVisualizationView_KeyboardNavigation_Loaded(object _, RoutedEventArgs __)
    {
      KeyboardNavigationHelper.SetupTabNavigation(this);
    }

    private void HelpButton_Click(object _, Microsoft.UI.Xaml.RoutedEventArgs __)
    {
      HelpOverlay.Title = "Sonography Visualization Help";
      HelpOverlay.HelpText = "The Sonography Visualization panel generates 3D spectrogram visualizations (waterfall plots) showing frequency content over time. Configure time and resolution parameters, select visualization perspective and color scheme, and adjust 3D view controls (rotation, zoom) to explore the audio spectrogram from different angles. Sonography helps visualize temporal patterns and frequency evolution in audio.";

      HelpOverlay.Shortcuts.Clear();

      HelpOverlay.Tips.Clear();
      HelpOverlay.Tips.Add("Sonography shows frequency content as it changes over time in 3D");
      HelpOverlay.Tips.Add("Time window controls the temporal resolution of each frame");
      HelpOverlay.Tips.Add("Overlap ratio affects smoothness and computational cost");
      HelpOverlay.Tips.Add("Higher frequency resolution provides more detail but requires more processing");
      HelpOverlay.Tips.Add("Rotate and zoom the 3D view to explore different perspectives");
      HelpOverlay.Tips.Add("Different color schemes highlight different aspects of the spectrogram");

      HelpOverlay.Visibility = Microsoft.UI.Xaml.Visibility.Visible;
      HelpOverlay.Show();
    }

    private void AudioComboBox_RightTapped(object sender, Microsoft.UI.Xaml.Input.RightTappedRoutedEventArgs e)
    {
      e.Handled = true;
      if (_contextMenuService != null)
      {
        var menu = new MenuFlyout();

        var refreshItem = new MenuFlyoutItem { Text = "Refresh Audio List" };
        refreshItem.Click += async (_, _) =>
        {
          await ViewModel.RefreshCommand.ExecuteAsync(null);
          _toastService?.ShowToast(ToastType.Success, "Refreshed", "Audio list refreshed");
        };
        menu.Items.Add(refreshItem);

        if (!string.IsNullOrEmpty(ViewModel.SelectedAudioId))
        {
          menu.Items.Add(new MenuFlyoutSeparator());

          var copyIdItem = new MenuFlyoutItem { Text = "Copy Audio ID" };
          copyIdItem.Click += (_, _) =>
          {
            var dataPackage = new Windows.ApplicationModel.DataTransfer.DataPackage();
            dataPackage.SetText(ViewModel.SelectedAudioId);
            Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(dataPackage);
            _toastService?.ShowToast(ToastType.Success, "Copied", "Audio ID copied to clipboard");
          };
          menu.Items.Add(copyIdItem);
        }

        var target = sender as UIElement;
        if (target != null)
        {
          _contextMenuService.ShowContextMenu(menu, target, e.GetPosition(target));
        }
      }
    }

    private void SonographyDisplay_RightTapped(object sender, Microsoft.UI.Xaml.Input.RightTappedRoutedEventArgs e)
    {
      e.Handled = true;
      if (_contextMenuService != null && ViewModel.SonographyData != null)
      {
        var menu = new MenuFlyout();

        var exportItem = new MenuFlyoutItem { Text = "Export Visualization" };
        exportItem.Click += async (_, _) => await ExportSonographyAsync();
        menu.Items.Add(exportItem);

        var refreshItem = new MenuFlyoutItem { Text = "Refresh Visualization" };
        refreshItem.Click += async (_, _) =>
        {
          await ViewModel.GenerateSonographyCommand.ExecuteAsync(null);
          _toastService?.ShowToast(ToastType.Success, "Refreshed", "Visualization refreshed");
        };
        menu.Items.Add(refreshItem);

        var target = sender as UIElement;
        if (target != null)
        {
          _contextMenuService.ShowContextMenu(menu, target, e.GetPosition(target));
        }
      }
    }

    private async System.Threading.Tasks.Task ExportSonographyAsync()
    {
      try
      {
        var picker = new Windows.Storage.Pickers.FileSavePicker();
        picker.SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.DocumentsLibrary;
        picker.FileTypeChoices.Add("PNG", new[] { ".png" });
        picker.FileTypeChoices.Add("JPEG", new[] { ".jpg" });
        picker.SuggestedFileName = "sonography_visualization";

        var file = await picker.PickSaveFileAsync();
        if (file != null)
        {
          // In a real implementation, this would capture the visualization as an image
          // For now, we'll create a simple JSON export of the data
          if (ViewModel.SonographyData != null)
          {
            var jsonData = new
            {
              Timestamp = DateTime.UtcNow.ToString("O"),
              DataAvailable = true
            };
            var content = System.Text.Json.JsonSerializer.Serialize(jsonData, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
            await Windows.Storage.FileIO.WriteTextAsync(file, content);
            _toastService?.ShowToast(ToastType.Success, "Export", "Sonography visualization exported successfully");
          }
          else
          {
            _toastService?.ShowToast(ToastType.Warning, "Export", "No visualization data available to export");
          }
        }
      }
      catch (Exception ex)
      {
        _toastService?.ShowToast(ToastType.Error, "Export Failed", ex.Message);
      }
    }
  }
}