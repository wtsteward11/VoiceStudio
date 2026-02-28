// =============================================================================
// HealthCheckView.xaml.cs — Phase 5.3.3
// Code-behind for health check aggregation panel.
// =============================================================================

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using VoiceStudio.App.Services;

namespace VoiceStudio.App.Views.Panels
{
    /// <summary>
    /// Health check panel showing aggregated system component health.
    /// </summary>
    public sealed partial class HealthCheckView : UserControl
    {
        /// <summary>
        /// Gets the view model for this view.
        /// </summary>
        public HealthCheckViewModel ViewModel { get; }

        /// <summary>
        /// Initializes a new instance of the <see cref="HealthCheckView"/> class.
        /// </summary>
        public HealthCheckView()
        {
            ViewModel = new HealthCheckViewModel();
            InitializeComponent();
            Loaded += OnLoaded;
        }

        private async void OnLoaded(object _, RoutedEventArgs __)
        {
          try
          {
              await ViewModel.LoadHealthChecksAsync();
          }
          catch (Exception ex)
          {
            System.Diagnostics.Debug.WriteLine($"Unhandled error in event handler: {ex.Message}");
          }
        }

        private void HelpButton_Click(object _, RoutedEventArgs __)
        {
            HelpOverlay.Visibility = Visibility.Visible;
        }

        private void HelpOverlay_Tapped(object _, Microsoft.UI.Xaml.Input.TappedRoutedEventArgs __)
        {
            HelpOverlay.Visibility = Visibility.Collapsed;
        }

        private void CloseHelp_Click(object _, RoutedEventArgs __)
        {
            HelpOverlay.Visibility = Visibility.Collapsed;
        }
    }
}
