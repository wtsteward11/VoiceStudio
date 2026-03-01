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

    private void HelpButton_Click(object _, RoutedEventArgs __)
    {
      HelpOverlay.Title = "Audio Monitoring Help";
      HelpOverlay.HelpText = "The Audio Monitoring Dashboard provides real-time audio level meters and statistics. Enter an audio file ID and click Load to analyze. Enable Live mode for continuous monitoring. Track peak levels, RMS, LUFS loudness, and detect clipping.";

      HelpOverlay.Shortcuts.Clear();
      HelpOverlay.Shortcuts.Add(new Controls.KeyboardShortcut { Key = "F5", Description = "Reload audio" });
      HelpOverlay.Shortcuts.Add(new Controls.KeyboardShortcut { Key = "Escape", Description = "Close help" });

      HelpOverlay.Tips.Clear();
      HelpOverlay.Tips.Add("Peak levels above -3 dB indicate potential clipping risk");
      HelpOverlay.Tips.Add("LUFS is the standard loudness measurement for broadcast audio");
      HelpOverlay.Tips.Add("Enable Live mode for real-time level monitoring at 10fps");
      HelpOverlay.Tips.Add("Dynamic range = max peak minus average RMS");

      HelpOverlay.Visibility = Visibility.Visible;
      HelpOverlay.Show();
    }

    private void AudioMonitoringDashboardView_KeyboardNavigation_Loaded(object sender, RoutedEventArgs e)
    {
      KeyboardNavigationHelper.SetupTabNavigation(this);
    }
  }
}