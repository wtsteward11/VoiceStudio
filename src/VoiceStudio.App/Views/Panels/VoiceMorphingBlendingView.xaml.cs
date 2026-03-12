using VoiceStudio.App.Services;
using VoiceStudio.App.ViewModels;

namespace VoiceStudio.App.Views.Panels
{
  /// <summary>
  /// VoiceMorphingBlendingView panel - Voice morphing and blending.
  /// </summary>
  public sealed partial class VoiceMorphingBlendingView : Microsoft.UI.Xaml.Controls.UserControl
  {
    public VoiceMorphingBlendingViewModel ViewModel { get; }
    private ToastNotificationService? _toastService;

    public VoiceMorphingBlendingView()
    {
      this.InitializeComponent();
      ViewModel = new VoiceMorphingBlendingViewModel(
          AppServices.GetRequiredService<VoiceStudio.Core.Services.IViewModelContext>(),
          ServiceProvider.GetBackendClient(),
          ServiceProvider.GetProfilesClient()
      );
      DataContext = ViewModel;

      // Initialize services
      _toastService = ServiceProvider.GetToastNotificationService();

      // Subscribe to ViewModel events for toast notifications
      ViewModel.PropertyChanged += (s, e) =>
      {
        if (e.PropertyName == nameof(VoiceMorphingBlendingViewModel.ErrorMessage) && !string.IsNullOrEmpty(ViewModel.ErrorMessage))
        {
          _toastService?.ShowToast(ToastType.Error, "Voice Morphing/Blending Error", ViewModel.ErrorMessage);
        }
        else if (e.PropertyName == nameof(VoiceMorphingBlendingViewModel.StatusMessage) && !string.IsNullOrEmpty(ViewModel.StatusMessage))
        {
          _toastService?.ShowToast(ToastType.Success, "Voice Morphing/Blending", ViewModel.StatusMessage);
        }
      };

      // Setup keyboard navigation
      this.Loaded += VoiceMorphingBlendingView_KeyboardNavigation_Loaded;

      // Setup Escape key to close help overlay
      KeyboardNavigationHelper.SetupEscapeKeyHandling(this, () =>
      {
        if (HelpOverlay.IsVisible)
        {
          HelpOverlay.IsVisible = false;
        }
      });
    }

    private void VoiceMorphingBlendingView_KeyboardNavigation_Loaded(object _, Microsoft.UI.Xaml.RoutedEventArgs __)
    {
      KeyboardNavigationHelper.SetupTabNavigation(this);
    }

    private void HelpButton_Click(object _, Microsoft.UI.Xaml.RoutedEventArgs __)
    {
      HelpOverlay.Title = "Voice Morphing/Blending Help";
      HelpOverlay.HelpText = "The Voice Morphing/Blending panel is a cutting-edge feature that allows you to blend two or more voice models to create a new hybrid voice, or morph a voice from one identity to another over time. This enables unique voice creation (e.g., a voice that is 50% Speaker A and 50% Speaker B) and special effects like smoothly transitioning between characters in a story. In 'Blend Voices' mode, select two voice profiles (Voice A and Voice B), adjust the blend ratio slider (0% A to 100% B), enter preview text, and click 'Preview' to hear the blended voice or 'Blend' to create it. You can save the blended voice as a new voice profile for future use. In 'Morph Timeline' mode, provide a source audio ID, select Voice A (starting voice) and Voice B (ending voice), set the start and end ratios, adjust the morph speed, and click 'Morph Voice' to create a gradual transition from Voice A to Voice B over time. This is useful for character transformations or special effects.";

      HelpOverlay.Shortcuts.Clear();
      HelpOverlay.Shortcuts.Add(new Controls.KeyboardShortcut { Key = "Ctrl+L", Description = "Load voice profiles" });
      HelpOverlay.Shortcuts.Add(new Controls.KeyboardShortcut { Key = "Ctrl+P", Description = "Preview blend" });
      HelpOverlay.Shortcuts.Add(new Controls.KeyboardShortcut { Key = "Ctrl+B", Description = "Blend voices" });
      HelpOverlay.Shortcuts.Add(new Controls.KeyboardShortcut { Key = "Ctrl+M", Description = "Morph voice" });

      HelpOverlay.Tips.Clear();
      HelpOverlay.Tips.Add("Blend Voices mode creates a static hybrid voice at a specific ratio");
      HelpOverlay.Tips.Add("Morph Timeline mode creates a gradual transition from Voice A to Voice B");
      HelpOverlay.Tips.Add("Blend ratio 0.0 = 100% Voice A, 1.0 = 100% Voice B, 0.5 = 50/50");
      HelpOverlay.Tips.Add("Preview lets you hear the blended voice before creating it");
      HelpOverlay.Tips.Add("Save as profile to use the blended voice in future projects");
      HelpOverlay.Tips.Add("Morph speed controls how quickly the transition happens");
      HelpOverlay.Tips.Add("Start ratio 0.0 means beginning is 100% Voice A");
      HelpOverlay.Tips.Add("End ratio 1.0 means ending is 100% Voice B");
      HelpOverlay.Tips.Add("Morphing is useful for character transformations in stories");
      HelpOverlay.Tips.Add("Blended voices can be used like any other voice profile for TTS");

      HelpOverlay.Visibility = Microsoft.UI.Xaml.Visibility.Visible;
      HelpOverlay.Show();
    }
  }
}