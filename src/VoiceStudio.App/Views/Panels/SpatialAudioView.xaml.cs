using VoiceStudio.App.Services;
using VoiceStudio.App.ViewModels;

namespace VoiceStudio.App.Views.Panels
{
  /// <summary>
  /// SpatialAudioView panel - 3D audio positioning and spatialization.
  /// </summary>
  public sealed partial class SpatialAudioView : Microsoft.UI.Xaml.Controls.UserControl
  {
    public SpatialAudioViewModel ViewModel { get; }
    private ToastNotificationService? _toastService;

    public SpatialAudioView()
    {
      this.InitializeComponent();
      ViewModel = new SpatialAudioViewModel(
          AppServices.GetRequiredService<VoiceStudio.Core.Services.IViewModelContext>(),
          ServiceProvider.GetSpatialAudioClient()
      );
      DataContext = ViewModel;

      // Initialize services
      _toastService = ServiceProvider.GetToastNotificationService();

      // Subscribe to ViewModel events for toast notifications
      ViewModel.PropertyChanged += (_, e) =>
      {
        if (e.PropertyName == nameof(SpatialAudioViewModel.ErrorMessage) && !string.IsNullOrEmpty(ViewModel.ErrorMessage))
        {
          _toastService?.ShowToast(ToastType.Error, "Spatial Audio Error", ViewModel.ErrorMessage);
        }
        else if (e.PropertyName == nameof(SpatialAudioViewModel.StatusMessage) && !string.IsNullOrEmpty(ViewModel.StatusMessage))
        {
          _toastService?.ShowToast(ToastType.Success, "Spatial Audio", ViewModel.StatusMessage);
        }
      };

      // Setup keyboard navigation
      this.Loaded += SpatialAudioView_KeyboardNavigation_Loaded;

      // Setup Escape key to close help overlay
      KeyboardNavigationHelper.SetupEscapeKeyHandling(this, () =>
      {
        if (HelpOverlay.IsVisible)
        {
          HelpOverlay.IsVisible = false;
        }
      });
    }

    private void SpatialAudioView_KeyboardNavigation_Loaded(object _, Microsoft.UI.Xaml.RoutedEventArgs __)
    {
      KeyboardNavigationHelper.SetupTabNavigation(this);
    }

    private void HelpButton_Click(object _, Microsoft.UI.Xaml.RoutedEventArgs __)
    {
      HelpOverlay.Title = "Spatial Audio Help";
      HelpOverlay.HelpText = "The Spatial Audio panel introduces 3D spatial positioning and environment simulation for voices, allowing you to place voice clips in a virtual space for immersive audio experiences. Perfect for VR/AR content, games, or film post-production. Use the 3D position controls to place audio sources in space (X = left/right, Y = back/front, Z = up/down). Adjust distance to control volume falloff. Configure environment settings like room size, material, and reverb to simulate different acoustic spaces. Enable HRTF for binaural audio output suitable for headphones. Use presets for quick setup, or fine-tune manually. Process audio to apply spatial effects permanently, or preview in real-time.";

      HelpOverlay.Shortcuts.Clear();
      HelpOverlay.Shortcuts.Add(new Controls.KeyboardShortcut { Key = "Ctrl+P", Description = "Set position" });
      HelpOverlay.Shortcuts.Add(new Controls.KeyboardShortcut { Key = "Ctrl+E", Description = "Configure environment" });
      HelpOverlay.Shortcuts.Add(new Controls.KeyboardShortcut { Key = "Ctrl+R", Description = "Process audio" });
      HelpOverlay.Shortcuts.Add(new Controls.KeyboardShortcut { Key = "Space", Description = "Preview audio" });
      HelpOverlay.Shortcuts.Add(new Controls.KeyboardShortcut { Key = "Ctrl+Shift+R", Description = "Reset to defaults" });

      HelpOverlay.Tips.Clear();
      HelpOverlay.Tips.Add("X axis: -1 = left, 1 = right");
      HelpOverlay.Tips.Add("Y axis: -1 = back, 1 = front");
      HelpOverlay.Tips.Add("Z axis: -1 = down, 1 = up");
      HelpOverlay.Tips.Add("Distance affects volume falloff (closer = louder)");
      HelpOverlay.Tips.Add("Room size affects reverb characteristics");
      HelpOverlay.Tips.Add("Material affects reverb timbre (concrete = bright, carpet = warm)");
      HelpOverlay.Tips.Add("Enable HRTF for binaural audio (best for headphones)");
      HelpOverlay.Tips.Add("Use presets for quick environment setup");
      HelpOverlay.Tips.Add("Preview allows real-time testing before processing");
      HelpOverlay.Tips.Add("Processed audio can be exported or added to timeline");

      HelpOverlay.Visibility = Microsoft.UI.Xaml.Visibility.Visible;
      HelpOverlay.Show();
    }
  }
}