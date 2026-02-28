using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using VoiceStudio.App.Services;
using VoiceStudio.Core.Services;

namespace VoiceStudio.App.Views.Panels
{
  /// <summary>
  /// Audio Monitoring Dashboard view.
  /// Implements IDEA 34: Real-Time Audio Monitoring Dashboard.
  /// </summary>
  public sealed partial class AudioMonitoringDashboardView : UserControl
  {
    public AudioMonitoringDashboardViewModel ViewModel { get; }

    public AudioMonitoringDashboardView()
    {
      this.InitializeComponent();
      ViewModel = new AudioMonitoringDashboardViewModel(
          ServiceProvider.GetBackendClient()
      );
      this.DataContext = ViewModel;

      // Setup keyboard navigation
      this.Loaded += AudioMonitoringDashboardView_KeyboardNavigation_Loaded;

      // Setup Escape key to close help overlay
      KeyboardNavigationHelper.SetupEscapeKeyHandling(this, () =>
      {
        if (HelpOverlay.IsVisible)
        {
          HelpOverlay.IsVisible = false;
        }
      });
    }

    private void AudioMonitoringDashboardView_KeyboardNavigation_Loaded(object sender, RoutedEventArgs e)
    {
      KeyboardNavigationHelper.SetupTabNavigation(this);
    }
  }
}