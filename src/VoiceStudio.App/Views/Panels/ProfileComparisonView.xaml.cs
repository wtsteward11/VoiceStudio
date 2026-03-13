using Microsoft.UI.Xaml.Controls;
using System.Threading;
using VoiceStudio.App.Services;
using VoiceStudio.App.ViewModels;

namespace VoiceStudio.App.Views.Panels
{
  /// <summary>
  /// Profile Comparison panel - Voice Profile Comparison Tool.
  /// Implements IDEA 24: Voice Profile Comparison Tool.
  /// </summary>
  public sealed partial class ProfileComparisonView : UserControl
  {
    public ProfileComparisonViewModel ViewModel { get; }
    private ToastNotificationService? _toastService;

    public ProfileComparisonView()
    {
      this.InitializeComponent();
      ViewModel = new ProfileComparisonViewModel(
          AppServices.GetRequiredService<VoiceStudio.Core.Services.IViewModelContext>(),
          AppServices.GetRequiredService<IVoiceSynthesisService>(),
          ServiceProvider.GetProfilesClient(),
          ServiceProvider.GetAudioPlayerService()
      );
      this.DataContext = ViewModel;

      // Initialize services
      _toastService = ServiceProvider.GetToastNotificationService();

      // Subscribe to ViewModel events for toast notifications
      ViewModel.PropertyChanged += (_, e) =>
      {
        if (e.PropertyName == nameof(ProfileComparisonViewModel.ErrorMessage) && !string.IsNullOrEmpty(ViewModel.ErrorMessage))
        {
          _toastService?.ShowToast(ToastType.Error, "Profile Comparison Error", ViewModel.ErrorMessage);
        }
      };

      // Setup keyboard navigation and initial data load (ADR-047)
      this.Loaded += ProfileComparisonView_Loaded;

      // Setup Escape key to close help overlay
      KeyboardNavigationHelper.SetupEscapeKeyHandling(this, () =>
      {
        if (HelpOverlay.IsVisible)
        {
          HelpOverlay.IsVisible = false;
        }
      });
    }

    private async void ProfileComparisonView_Loaded(object _, Microsoft.UI.Xaml.RoutedEventArgs __)
    {
      this.Loaded -= ProfileComparisonView_Loaded;
      KeyboardNavigationHelper.SetupTabNavigation(this);
      await ViewModel.InitializeAsync(CancellationToken.None);
    }

    private void HelpButton_Click(object _, Microsoft.UI.Xaml.RoutedEventArgs __)
    {
      HelpOverlay.Title = "Profile Comparison Help";
      HelpOverlay.HelpText = "The Profile Comparison tool allows you to compare two voice profiles side-by-side. Select two profiles from the dropdowns, enter preview text, and click 'Compare Profiles' to generate synthesized audio with both profiles. View quality metrics, listen to audio samples, and see which profile performs better.";

      HelpOverlay.Shortcuts.Clear();
      HelpOverlay.Shortcuts.Add(new Controls.KeyboardShortcut { Key = "F5", Description = "Refresh profiles list" });
      HelpOverlay.Shortcuts.Add(new Controls.KeyboardShortcut { Key = "Escape", Description = "Close help" });

      HelpOverlay.Tips.Clear();
      HelpOverlay.Tips.Add("Select two different profiles to compare their quality and characteristics");
      HelpOverlay.Tips.Add("Enter custom preview text to test how each profile sounds with specific content");
      HelpOverlay.Tips.Add("Quality metrics include MOS Score, Similarity, Naturalness, and SNR");
      HelpOverlay.Tips.Add("Play audio samples to hear the difference between profiles");
      HelpOverlay.Tips.Add("The comparison shows which profile has a higher quality score");
      HelpOverlay.Tips.Add("Use the same preview text for both profiles to ensure fair comparison");

      HelpOverlay.Visibility = Microsoft.UI.Xaml.Visibility.Visible;
      HelpOverlay.Show();
    }
  }
}