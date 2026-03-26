using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using VoiceStudio.App.Services;
using VoiceStudio.App.Views.Panels;

namespace VoiceStudio.App.Views.Panels
{
  /// <summary>
  /// Advanced Real-Time Visualization view.
  /// Implements IDEA 131: Advanced Visualization and Real-Time Audio Display (remaining 50%).
  /// </summary>
  public sealed partial class AdvancedRealTimeVisualizationView : UserControl
  {
    public AdvancedRealTimeVisualizationViewModel ViewModel { get; }

    public AdvancedRealTimeVisualizationView()
    {
      this.InitializeComponent();
      ViewModel = new AdvancedRealTimeVisualizationViewModel(
          ServiceProvider.GetAdvancedRealTimeVisualizationClient()
      );
      this.DataContext = ViewModel;

      // Setup keyboard navigation
      this.Loaded += AdvancedRealTimeVisualizationView_KeyboardNavigation_Loaded;

      // Setup Escape key to close help overlay
      KeyboardNavigationHelper.SetupEscapeKeyHandling(this, () =>
      {
        if (HelpOverlay.IsVisible)
        {
          HelpOverlay.IsVisible = false;
        }
      });
    }

    private void AdvancedRealTimeVisualizationView_KeyboardNavigation_Loaded(object sender, RoutedEventArgs e)
    {
      KeyboardNavigationHelper.SetupTabNavigation(this);
    }
  }
}