using Microsoft.UI.Xaml.Controls;
using VoiceStudio.App.ViewModels;
using VoiceStudio.App.Services;

namespace VoiceStudio.App.Views.Panels
{
  /// <summary>
  /// GPUStatusView panel for GPU monitoring.
  /// </summary>
  public sealed partial class GPUStatusView : UserControl
  {
    public GPUStatusViewModel ViewModel { get; }
    private ToastNotificationService? _toastService;

    public GPUStatusView()
    {
      this.InitializeComponent();
      ViewModel = new GPUStatusViewModel(
          AppServices.GetRequiredService<VoiceStudio.Core.Services.IViewModelContext>(),
          VoiceStudio.App.Services.ServiceProvider.GetGPUStatusClient()
      );
      DataContext = ViewModel;

      // Initialize services
      _toastService = ServiceProvider.GetToastNotificationService();

      // Subscribe to ViewModel events for toast notifications
      ViewModel.PropertyChanged += (_, e) =>
      {
        if (e.PropertyName == nameof(GPUStatusViewModel.ErrorMessage) && !string.IsNullOrEmpty(ViewModel.ErrorMessage))
        {
          _toastService?.ShowToast(ToastType.Error, "GPU Status Error", ViewModel.ErrorMessage);
        }
        else if (e.PropertyName == nameof(GPUStatusViewModel.StatusMessage) && !string.IsNullOrEmpty(ViewModel.StatusMessage))
        {
          _toastService?.ShowToast(ToastType.Success, "GPU Status", ViewModel.StatusMessage);
        }
      };

      // Setup keyboard navigation
      this.Loaded += GPUStatusView_KeyboardNavigation_Loaded;

      // Setup Escape key to close help overlay
      KeyboardNavigationHelper.SetupEscapeKeyHandling(this, () =>
      {
        if (HelpOverlay.IsVisible)
        {
          HelpOverlay.IsVisible = false;
        }
      });
    }

    private void GPUStatusView_KeyboardNavigation_Loaded(object _, Microsoft.UI.Xaml.RoutedEventArgs __)
    {
      KeyboardNavigationHelper.SetupTabNavigation(this);
    }

    private void HelpButton_Click(object _, Microsoft.UI.Xaml.RoutedEventArgs __)
    {
      HelpOverlay.Title = "GPU Status Help";
      HelpOverlay.HelpText = "The GPU Status panel provides real-time monitoring of GPU devices and their utilization. View detailed information about each GPU including vendor, device ID, driver version, memory usage, utilization percentage, temperature, and power consumption. Enable auto-refresh to continuously monitor GPU status. Select a GPU from the list to view detailed information. This panel helps you monitor GPU resources for voice synthesis and model processing operations.";

      HelpOverlay.Shortcuts.Clear();
      HelpOverlay.Shortcuts.Add(new Controls.KeyboardShortcut { Key = "F5", Description = "Refresh GPU status" });
      HelpOverlay.Shortcuts.Add(new Controls.KeyboardShortcut { Key = "Tab", Description = "Navigate between GPUs" });

      HelpOverlay.Tips.Clear();
      HelpOverlay.Tips.Add("GPU utilization shows how much the GPU is currently being used");
      HelpOverlay.Tips.Add("VRAM (Video RAM) usage is critical for model loading and processing");
      HelpOverlay.Tips.Add("High utilization may indicate active voice synthesis or training");
      HelpOverlay.Tips.Add("Temperature monitoring helps prevent GPU overheating");
      HelpOverlay.Tips.Add("Power usage indicates GPU workload and energy consumption");
      HelpOverlay.Tips.Add("Auto-refresh keeps GPU status up-to-date automatically");
      HelpOverlay.Tips.Add("Select a GPU to view detailed device information and performance metrics");
      HelpOverlay.Tips.Add("GPU availability affects which engines can be used for synthesis");

      HelpOverlay.Visibility = Microsoft.UI.Xaml.Visibility.Visible;
      HelpOverlay.Show();
    }
  }
}