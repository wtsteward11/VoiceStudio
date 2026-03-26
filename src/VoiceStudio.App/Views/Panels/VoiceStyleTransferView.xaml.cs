using VoiceStudio.App.Services;
using VoiceStudio.App.ViewModels;

namespace VoiceStudio.App.Views.Panels
{
  /// <summary>
  /// VoiceStyleTransferView panel - Voice style transfer from reference audio.
  /// </summary>
  public sealed partial class VoiceStyleTransferView : Microsoft.UI.Xaml.Controls.UserControl
  {
    public VoiceStyleTransferViewModel ViewModel { get; }
    private ToastNotificationService? _toastService;

    public VoiceStyleTransferView()
    {
      this.InitializeComponent();
      ViewModel = new VoiceStyleTransferViewModel(
          AppServices.GetRequiredService<VoiceStudio.Core.Services.IViewModelContext>(),
          ServiceProvider.GetVoiceStyleTransferClient(),
          ServiceProvider.GetProfilesClient()
      );
      DataContext = ViewModel;

      // Initialize services
      _toastService = ServiceProvider.GetToastNotificationService();

      // Subscribe to ViewModel events for toast notifications
      ViewModel.PropertyChanged += (s, e) =>
      {
        if (e.PropertyName == nameof(VoiceStyleTransferViewModel.ErrorMessage) && !string.IsNullOrEmpty(ViewModel.ErrorMessage))
        {
          _toastService?.ShowToast(ToastType.Error, "Voice Style Transfer Error", ViewModel.ErrorMessage);
        }
        else if (e.PropertyName == nameof(VoiceStyleTransferViewModel.StatusMessage) && !string.IsNullOrEmpty(ViewModel.StatusMessage))
        {
          _toastService?.ShowToast(ToastType.Success, "Voice Style Transfer", ViewModel.StatusMessage);
        }
      };

      // Setup keyboard navigation
      this.Loaded += VoiceStyleTransferView_KeyboardNavigation_Loaded;

      // Setup Escape key to close help overlay
      KeyboardNavigationHelper.SetupEscapeKeyHandling(this, () =>
      {
        if (HelpOverlay.IsVisible)
        {
          HelpOverlay.IsVisible = false;
        }
      });
    }

    private void VoiceStyleTransferView_KeyboardNavigation_Loaded(object _, Microsoft.UI.Xaml.RoutedEventArgs __)
    {
      KeyboardNavigationHelper.SetupTabNavigation(this);
    }

    private void HelpButton_Click(object _, Microsoft.UI.Xaml.RoutedEventArgs __)
    {
      HelpOverlay.Title = "Voice Style Transfer Help";
      HelpOverlay.HelpText = "The Voice Style Transfer panel allows you to capture the speaking style from a reference audio and apply it to synthesized speech. The AI can mimic the tone, pacing, emotion, or accent from one recording and impose those characteristics on any chosen voice and script. This is a powerful creative tool for achieving highly expressive or context-matched voice outputs that go beyond canned 'emotion presets.' First, provide a reference audio ID and click 'Extract Style' to analyze the style characteristics. The panel will display a style profile showing prosodic features (pitch, energy, speaking rate) and emotion tags. You can also click 'Analyze Style' for detailed analysis including pitch/energy contours and style markers. Then, select a target voice profile, enter text to synthesize, adjust the style intensity slider (from subtle to strong mimicry), and click 'Generate' to create new audio where the target voice reads the text in the style from the reference.";

      HelpOverlay.Shortcuts.Clear();
      HelpOverlay.Shortcuts.Add(new Controls.KeyboardShortcut { Key = "Ctrl+E", Description = "Extract style from reference" });
      HelpOverlay.Shortcuts.Add(new Controls.KeyboardShortcut { Key = "Ctrl+A", Description = "Analyze style characteristics" });
      HelpOverlay.Shortcuts.Add(new Controls.KeyboardShortcut { Key = "Ctrl+G", Description = "Generate with style transfer" });
      HelpOverlay.Shortcuts.Add(new Controls.KeyboardShortcut { Key = "Ctrl+L", Description = "Load voice profiles" });

      HelpOverlay.Tips.Clear();
      HelpOverlay.Tips.Add("Reference audio should be clear and representative of the desired style");
      HelpOverlay.Tips.Add("Style intensity controls how strongly the style is applied (0.0 = subtle, 1.0 = strong)");
      HelpOverlay.Tips.Add("Style profile shows prosodic features: pitch, energy, speaking rate, and emotion");
      HelpOverlay.Tips.Add("Style analysis provides detailed pitch/energy contours and style markers");
      HelpOverlay.Tips.Add("You can use any voice profile as the target voice");
      HelpOverlay.Tips.Add("The generated audio combines the target voice with the reference style");
      HelpOverlay.Tips.Add("Lower style intensity (0.3-0.5) for subtle influence");
      HelpOverlay.Tips.Add("Higher style intensity (0.7-1.0) for strong mimicry");
      HelpOverlay.Tips.Add("Emotion tags help identify the emotional characteristics of the reference");
      HelpOverlay.Tips.Add("Style markers indicate pauses, emphasis points, and other style elements");

      HelpOverlay.Visibility = Microsoft.UI.Xaml.Visibility.Visible;
      HelpOverlay.Show();
    }
  }
}