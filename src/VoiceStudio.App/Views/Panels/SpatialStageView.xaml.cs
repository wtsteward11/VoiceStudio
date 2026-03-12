using Microsoft.UI.Xaml.Controls;
using VoiceStudio.App.ViewModels;
using VoiceStudio.App.Services;

namespace VoiceStudio.App.Views.Panels
{
  /// <summary>
  /// SpatialStageView panel for spatial audio positioning.
  /// </summary>
  public sealed partial class SpatialStageView : UserControl
  {
    public SpatialStageViewModel ViewModel { get; }
    private ToastNotificationService? _toastService;

    public SpatialStageView()
    {
      this.InitializeComponent();
      ViewModel = new SpatialStageViewModel(
          AppServices.GetRequiredService<VoiceStudio.Core.Services.IViewModelContext>(),
          VoiceStudio.App.Services.ServiceProvider.GetBackendClient(),
          AppServices.GetProjectsClient()
      );
      DataContext = ViewModel;

      // Initialize services
      _toastService = ServiceProvider.GetToastNotificationService();

      // Subscribe to ViewModel events for toast notifications
      ViewModel.PropertyChanged += (_, e) =>
      {
        if (e.PropertyName == nameof(SpatialStageViewModel.ErrorMessage) && !string.IsNullOrEmpty(ViewModel.ErrorMessage))
        {
          _toastService?.ShowToast(ToastType.Error, "Spatial Audio Error", ViewModel.ErrorMessage);
        }
        else if (e.PropertyName == nameof(SpatialStageViewModel.StatusMessage) && !string.IsNullOrEmpty(ViewModel.StatusMessage))
        {
          _toastService?.ShowToast(ToastType.Success, "Spatial Audio", ViewModel.StatusMessage);
        }
      };

      // Setup keyboard navigation
      this.Loaded += SpatialStageView_KeyboardNavigation_Loaded;

      // Setup Escape key to close help overlay
      KeyboardNavigationHelper.SetupEscapeKeyHandling(this, () =>
      {
        if (HelpOverlay.IsVisible)
        {
          HelpOverlay.IsVisible = false;
        }
      });
    }

    private void SpatialStageView_KeyboardNavigation_Loaded(object _, Microsoft.UI.Xaml.RoutedEventArgs __)
    {
      KeyboardNavigationHelper.SetupTabNavigation(this);
    }

    private void HelpButton_Click(object _, Microsoft.UI.Xaml.RoutedEventArgs __)
    {
      HelpOverlay.Title = "Spatial Audio Help";
      HelpOverlay.HelpText = "The Spatial Audio panel allows you to position audio sources in 3D space and configure spatial audio effects. Create spatial configurations with 3D positions (X, Y, Z), distance, room size, reverb, occlusion, and Doppler effects. Use HRTF (Head-Related Transfer Function) for binaural audio that creates a realistic 3D listening experience. Adjust the position sliders or interact with the 3D visualization to position audio sources. Preview spatial audio in real-time before applying configurations to audio files.";

      HelpOverlay.Shortcuts.Clear();
      HelpOverlay.Shortcuts.Add(new Controls.KeyboardShortcut { Key = "Ctrl+S", Description = "Save spatial configuration" });
      HelpOverlay.Shortcuts.Add(new Controls.KeyboardShortcut { Key = "Delete", Description = "Delete selected configuration" });
      HelpOverlay.Shortcuts.Add(new Controls.KeyboardShortcut { Key = "P", Description = "Preview spatial audio" });

      HelpOverlay.Tips.Clear();
      HelpOverlay.Tips.Add("X controls left (-1) to right (+1) position");
      HelpOverlay.Tips.Add("Y controls back (-1) to front (+1) position");
      HelpOverlay.Tips.Add("Z controls down (-1) to up (+1) position");
      HelpOverlay.Tips.Add("Distance affects volume attenuation (closer = louder)");
      HelpOverlay.Tips.Add("Room size affects reverb characteristics (larger = more reverb)");
      HelpOverlay.Tips.Add("HRTF enables binaural audio for realistic 3D positioning");
      HelpOverlay.Tips.Add("Doppler effect simulates frequency shift for moving sources");
      HelpOverlay.Tips.Add("Occlusion simulates audio passing through obstacles");
      HelpOverlay.Tips.Add("Preview spatial audio before applying to save processing time");

      HelpOverlay.Visibility = Microsoft.UI.Xaml.Visibility.Visible;
      HelpOverlay.Show();
    }
  }
}