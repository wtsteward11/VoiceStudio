using Microsoft.UI.Xaml.Controls;
using VoiceStudio.App.Services;

namespace VoiceStudio.App.Views.Panels
{
  /// <summary>
  /// A/B Testing panel for quality comparison.
  /// Implements IDEA 46: A/B Testing Interface for Quality Comparison.
  /// </summary>
  public sealed partial class ABTestingView : UserControl
  {
    public ABTestingViewModel ViewModel { get; }
    private ToastNotificationService? _toastService;

    public ABTestingView()
    {
      this.InitializeComponent();
      ViewModel = new ABTestingViewModel(
          AppServices.GetRequiredService<VoiceStudio.Core.Services.IViewModelContext>(),
          AppServices.GetRequiredService<IABTestService>(),
          ServiceProvider.GetProfilesClient(),
          ServiceProvider.GetAudioPlayerService()
      );
      this.DataContext = ViewModel;

      // Initialize services
      _toastService = ServiceProvider.GetToastNotificationService();

      // Subscribe to ViewModel events for toast notifications
      ViewModel.PropertyChanged += (s, e) =>
      {
        if (e.PropertyName == nameof(ABTestingViewModel.ErrorMessage) && !string.IsNullOrEmpty(ViewModel.ErrorMessage))
        {
          _toastService?.ShowToast(ToastType.Error, "A/B Testing Error", ViewModel.ErrorMessage);
        }
        else if (e.PropertyName == nameof(ABTestingViewModel.StatusMessage) && !string.IsNullOrEmpty(ViewModel.StatusMessage))
        {
          _toastService?.ShowToast(ToastType.Success, "A/B Testing", ViewModel.StatusMessage);
        }
      };

      // Setup keyboard navigation
      this.Loaded += ABTestingView_KeyboardNavigation_Loaded;

      // Setup Escape key to close help overlay
      KeyboardNavigationHelper.SetupEscapeKeyHandling(this, () =>
      {
        if (HelpOverlay.IsVisible)
        {
          HelpOverlay.IsVisible = false;
        }
      });
    }

    private void ABTestingView_KeyboardNavigation_Loaded(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
      KeyboardNavigationHelper.SetupTabNavigation(this);
    }

    private void HelpButton_Click(object _, Microsoft.UI.Xaml.RoutedEventArgs __)
    {
      HelpOverlay.Title = "A/B Testing Help";
      HelpOverlay.HelpText = "The A/B Testing panel allows you to compare two voice synthesis versions side-by-side for quality evaluation. Create A and B versions with different settings, engines, or configurations, then compare them directly. Use this panel to evaluate voice quality, test different engines, compare prosody settings, or assess the impact of different configurations. Play both versions, switch between them, and rate them to make informed decisions about voice synthesis settings.";

      HelpOverlay.Shortcuts.Clear();
      HelpOverlay.Shortcuts.Add(new Controls.KeyboardShortcut { Key = "Ctrl+T", Description = "Run A/B test" });
      HelpOverlay.Shortcuts.Add(new Controls.KeyboardShortcut { Key = "Space", Description = "Play/pause" });
      HelpOverlay.Shortcuts.Add(new Controls.KeyboardShortcut { Key = "1", Description = "Select version A" });
      HelpOverlay.Shortcuts.Add(new Controls.KeyboardShortcut { Key = "2", Description = "Select version B" });

      HelpOverlay.Tips.Clear();
      HelpOverlay.Tips.Add("A/B testing helps you compare voice synthesis quality objectively");
      HelpOverlay.Tips.Add("Test different engines, settings, or configurations side-by-side");
      HelpOverlay.Tips.Add("Use different emotions or prosody settings for comparison");
      HelpOverlay.Tips.Add("Switch between versions to hear differences clearly");
      HelpOverlay.Tips.Add("Rate each version to track your preferences");
      HelpOverlay.Tips.Add("Compare naturalness, clarity, and emotional expression");
      HelpOverlay.Tips.Add("Save your preferred version for future reference");

      HelpOverlay.Visibility = Microsoft.UI.Xaml.Visibility.Visible;
      HelpOverlay.Show();
    }
  }
}