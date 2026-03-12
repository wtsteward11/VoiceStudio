using Microsoft.UI.Xaml.Controls;
using VoiceStudio.App.ViewModels;
using VoiceStudio.App.Services;

namespace VoiceStudio.App.Views.Panels
{
  /// <summary>
  /// Voice Profile Health Dashboard panel.
  /// Implements IDEA 35: Voice Profile Health Dashboard.
  /// </summary>
  public sealed partial class ProfileHealthDashboardView : UserControl
  {
    public ProfileHealthDashboardViewModel ViewModel { get; }

    public ProfileHealthDashboardView()
    {
      this.InitializeComponent();
      ViewModel = new ProfileHealthDashboardViewModel(
          AppServices.GetRequiredService<VoiceStudio.Core.Services.IViewModelContext>(),
          VoiceStudio.App.Services.ServiceProvider.GetBackendClient(),
          VoiceStudio.App.Services.ServiceProvider.GetProfilesClient()
      );
      DataContext = ViewModel;

      // Load health data when view is loaded
      Loaded += async (_, __) =>
      {
        if (ViewModel.Profiles.Count == 0)
        {
          await ViewModel.LoadHealthDataAsync(CancellationToken.None);
        }
      };

      HelpOverlay.HelpText = "The Profile Health Dashboard provides comprehensive health monitoring for all voice profiles. View summary statistics (total, healthy, degraded, critical profiles) at the top. Select a profile from the list to view detailed health information including quality metrics, degradation alerts, baseline comparisons, and trends. Use the Refresh button to update health data for all profiles. Health status indicators use color coding: Green (Healthy), Orange (Degraded), Red (Critical). The dashboard integrates with quality degradation detection and quality consistency monitoring to provide actionable insights.";

      // Setup keyboard navigation
      this.Loaded += ProfileHealthDashboardView_KeyboardNavigation_Loaded;

      // Setup Escape key to close help overlay
      KeyboardNavigationHelper.SetupEscapeKeyHandling(this, () =>
      {
        if (HelpOverlay.IsVisible)
        {
          HelpOverlay.IsVisible = false;
        }
      });
    }

    private void ProfileHealthDashboardView_KeyboardNavigation_Loaded(object _, Microsoft.UI.Xaml.RoutedEventArgs __)
    {
      KeyboardNavigationHelper.SetupTabNavigation(this);
    }

    private void HelpButton_Click(object _, Microsoft.UI.Xaml.RoutedEventArgs __)
    {
      HelpOverlay.IsVisible = !HelpOverlay.IsVisible;
    }
  }
}