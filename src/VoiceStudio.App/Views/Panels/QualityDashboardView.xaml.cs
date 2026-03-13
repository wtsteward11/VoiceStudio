using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml;
using VoiceStudio.App.Services;
using VoiceStudio.App.ViewModels;
using System.Threading;

namespace VoiceStudio.App.Views.Panels
{
  /// <summary>
  /// Quality Dashboard panel - Quality metrics visualization dashboard.
  /// Implements IDEA 49: Quality Metrics Visualization Dashboard.
  /// </summary>
  public sealed partial class QualityDashboardView : UserControl
  {
    public QualityDashboardViewModel ViewModel { get; }
    private ToastNotificationService? _toastService;

    public QualityDashboardView()
    {
      this.InitializeComponent();
      ViewModel = new QualityDashboardViewModel(
          AppServices.GetRequiredService<VoiceStudio.Core.Services.IViewModelContext>(),
          AppServices.GetRequiredService<VoiceStudio.Core.Services.IQualityControlClient>()
      );
      this.DataContext = ViewModel;

      // Initialize services
      _toastService = ServiceProvider.GetToastNotificationService();

      // Subscribe to ViewModel events for toast notifications
      ViewModel.PropertyChanged += (_, e) =>
      {
        if (e.PropertyName == nameof(QualityDashboardViewModel.ErrorMessage) && !string.IsNullOrEmpty(ViewModel.ErrorMessage))
        {
          _toastService?.ShowToast(ToastType.Error, "Quality Dashboard Error", ViewModel.ErrorMessage);
        }
        else if (e.PropertyName == nameof(QualityDashboardViewModel.StatusMessage) && !string.IsNullOrEmpty(ViewModel.StatusMessage))
        {
          _toastService?.ShowToast(ToastType.Success, "Quality Dashboard", ViewModel.StatusMessage);
        }
      };

      // Setup keyboard navigation and initial data load (ADR-047)
      this.Loaded += QualityDashboardView_Loaded;

      // Setup Escape key to close help overlay
      KeyboardNavigationHelper.SetupEscapeKeyHandling(this, () =>
      {
        if (HelpOverlay.IsVisible)
        {
          HelpOverlay.IsVisible = false;
        }
      });
    }

    private async void QualityDashboardView_Loaded(object _, RoutedEventArgs __)
    {
      this.Loaded -= QualityDashboardView_Loaded;
      KeyboardNavigationHelper.SetupTabNavigation(this);
      await ViewModel.InitializeAsync(CancellationToken.None);
    }

    private void HelpButton_Click(object _, Microsoft.UI.Xaml.RoutedEventArgs __)
    {
      HelpOverlay.Title = "Quality Dashboard Help";
      HelpOverlay.HelpText = "The Quality Dashboard provides comprehensive visualization of voice quality metrics. View quality overview with average MOS scores, similarity, and naturalness. Explore quality presets and their target metrics. Monitor quality trends over time. The full dashboard requires database integration for historical data aggregation, but basic quality metrics and presets are available now.";

      HelpOverlay.Shortcuts.Clear();
      HelpOverlay.Shortcuts.Add(new Controls.KeyboardShortcut { Key = "F5", Description = "Refresh quality data" });
      HelpOverlay.Shortcuts.Add(new Controls.KeyboardShortcut { Key = "Escape", Description = "Close help" });

      HelpOverlay.Tips.Clear();
      HelpOverlay.Tips.Add("Quality presets define target metrics for different quality tiers (fast, standard, high, ultra)");
      HelpOverlay.Tips.Add("MOS Score (Mean Opinion Score) ranges from 1.0 to 5.0 - higher is better");
      HelpOverlay.Tips.Add("Similarity measures how closely the synthesized voice matches the reference (0.0-1.0)");
      HelpOverlay.Tips.Add("Naturalness measures how natural the synthesized voice sounds (0.0-1.0)");
      HelpOverlay.Tips.Add("Quality trends show how quality metrics change over time");
      HelpOverlay.Tips.Add("The full dashboard with historical data requires database integration");
      HelpOverlay.Tips.Add("Use quality presets to optimize synthesis parameters for your target quality tier");

      HelpOverlay.Visibility = Microsoft.UI.Xaml.Visibility.Visible;
      HelpOverlay.Show();
    }
  }
}