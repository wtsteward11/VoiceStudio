using Microsoft.UI.Xaml.Controls;
using VoiceStudio.App.ViewModels;
using VoiceStudio.App.Services;

namespace VoiceStudio.App.Views.Panels
{
  /// <summary>
  /// UltimateDashboardView panel - Master dashboard aggregating all data.
  /// </summary>
  public sealed partial class UltimateDashboardView : UserControl
  {
    public UltimateDashboardViewModel ViewModel { get; }
    private ToastNotificationService? _toastService;

    public UltimateDashboardView()
    {
      this.InitializeComponent();
      ViewModel = new UltimateDashboardViewModel(
          AppServices.GetRequiredService<VoiceStudio.Core.Services.IViewModelContext>(),
          AppServices.GetUltimateDashboardClient()
      );
      DataContext = ViewModel;

      // Initialize services
      _toastService = ServiceProvider.GetToastNotificationService();

      // Subscribe to ViewModel events for toast notifications
      ViewModel.PropertyChanged += (s, e) =>
      {
        if (e.PropertyName == nameof(UltimateDashboardViewModel.ErrorMessage) && !string.IsNullOrEmpty(ViewModel.ErrorMessage))
        {
          _toastService?.ShowToast(ToastType.Error, "Dashboard Error", ViewModel.ErrorMessage);
        }
        else if (e.PropertyName == nameof(UltimateDashboardViewModel.StatusMessage) && !string.IsNullOrEmpty(ViewModel.StatusMessage))
        {
          _toastService?.ShowToast(ToastType.Success, "Dashboard", ViewModel.StatusMessage);
        }
      };

      // Setup keyboard navigation
      this.Loaded += UltimateDashboardView_KeyboardNavigation_Loaded;

      // Setup Escape key to close help overlay
      KeyboardNavigationHelper.SetupEscapeKeyHandling(this, () =>
      {
        if (HelpOverlay.IsVisible)
        {
          HelpOverlay.IsVisible = false;
        }
      });
    }

    private void UltimateDashboardView_KeyboardNavigation_Loaded(object _, Microsoft.UI.Xaml.RoutedEventArgs __)
    {
      KeyboardNavigationHelper.SetupTabNavigation(this);
    }

    private void HelpButton_Click(object _, Microsoft.UI.Xaml.RoutedEventArgs __)
    {
      HelpOverlay.Title = "Ultimate Dashboard Help";
      HelpOverlay.HelpText = "The Ultimate Dashboard is a master control panel that aggregates data from all areas of VoiceStudio Quantum+. It provides a comprehensive overview of your projects, voice profiles, audio files, active jobs, and system resources. Quick stats cards show key metrics with trend indicators. Recent activities display the latest actions across the application. System alerts notify you of any issues that require attention. Use the refresh button to update all dashboard data in real-time.";

      HelpOverlay.Shortcuts.Clear();
      HelpOverlay.Shortcuts.Add(new Controls.KeyboardShortcut { Key = "F5", Description = "Refresh dashboard" });
      HelpOverlay.Shortcuts.Add(new Controls.KeyboardShortcut { Key = "Ctrl+D", Description = "Open dashboard" });

      HelpOverlay.Tips.Clear();
      HelpOverlay.Tips.Add("Dashboard automatically refreshes on load");
      HelpOverlay.Tips.Add("Quick stats show trends (up/down arrows) for key metrics");
      HelpOverlay.Tips.Add("System status indicates overall health (healthy/warning/error)");
      HelpOverlay.Tips.Add("GPU usage is only shown if GPU is available");
      HelpOverlay.Tips.Add("Recent activities are sorted by timestamp (newest first)");
      HelpOverlay.Tips.Add("System alerts appear at the top if any issues are detected");
      HelpOverlay.Tips.Add("All data is aggregated from multiple backend sources");
      HelpOverlay.Tips.Add("Use refresh to get the latest data from all systems");

      HelpOverlay.Visibility = Microsoft.UI.Xaml.Visibility.Visible;
      HelpOverlay.Show();
    }
  }
}