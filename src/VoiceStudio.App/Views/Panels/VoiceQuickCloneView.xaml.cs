using VoiceStudio.App.Services;
using VoiceStudio.App.ViewModels;

namespace VoiceStudio.App.Views.Panels
{
  /// <summary>
  /// VoiceQuickCloneView panel - Streamlined, one-click voice cloning interface.
  /// </summary>
  public sealed partial class VoiceQuickCloneView : Microsoft.UI.Xaml.Controls.UserControl
  {
    public VoiceQuickCloneViewModel ViewModel { get; }
    private ToastNotificationService? _toastService;

    public VoiceQuickCloneView()
    {
      this.InitializeComponent();
      ViewModel = new VoiceQuickCloneViewModel(
          AppServices.GetRequiredService<VoiceStudio.Core.Services.IViewModelContext>(),
          AppServices.GetVoiceQuickCloneClient()
      );
      DataContext = ViewModel;

      // Initialize services
      _toastService = ServiceProvider.GetToastNotificationService();

      // Subscribe to ViewModel events for toast notifications
      ViewModel.PropertyChanged += (s, e) =>
      {
        if (e.PropertyName == nameof(VoiceQuickCloneViewModel.ErrorMessage) && !string.IsNullOrEmpty(ViewModel.ErrorMessage))
        {
          _toastService?.ShowToast(ToastType.Error, "Quick Clone Error", ViewModel.ErrorMessage);
        }
        else if (e.PropertyName == nameof(VoiceQuickCloneViewModel.CreatedProfileId) && !string.IsNullOrEmpty(ViewModel.CreatedProfileId))
        {
          _toastService?.ShowToast(ToastType.Success, "Clone Complete", $"Voice cloned successfully! Profile ID: {ViewModel.CreatedProfileId}");
        }
      };

      // Setup keyboard navigation
      this.Loaded += VoiceQuickCloneView_KeyboardNavigation_Loaded;

      // Setup Escape key to close help overlay
      KeyboardNavigationHelper.SetupEscapeKeyHandling(this, () =>
      {
        if (HelpOverlay.IsVisible)
        {
          HelpOverlay.IsVisible = false;
        }
      });
    }

    private void VoiceQuickCloneView_KeyboardNavigation_Loaded(object _, Microsoft.UI.Xaml.RoutedEventArgs __)
    {
      KeyboardNavigationHelper.SetupTabNavigation(this);
    }

    private void HelpButton_Click(object _, Microsoft.UI.Xaml.RoutedEventArgs __)
    {
      HelpOverlay.Title = "Quick Clone Help";
      HelpOverlay.HelpText = "The Quick Clone panel provides a streamlined, one-click voice cloning interface for power users. Simply drop or select an audio file, and the system will automatically detect the best engine and quality settings. The minimal UI focuses on speed and efficiency. After cloning, you'll receive a profile ID and quality score. Use the Reset button to start a new clone.";

      HelpOverlay.Shortcuts.Clear();
      HelpOverlay.Shortcuts.Add(new Controls.KeyboardShortcut { Key = "Ctrl+O", Description = "Browse audio file" });
      HelpOverlay.Shortcuts.Add(new Controls.KeyboardShortcut { Key = "Ctrl+Q", Description = "Quick clone" });
      HelpOverlay.Shortcuts.Add(new Controls.KeyboardShortcut { Key = "Ctrl+R", Description = "Reset" });

      HelpOverlay.Tips.Clear();
      HelpOverlay.Tips.Add("Drag and drop audio files directly onto the upload area");
      HelpOverlay.Tips.Add("Auto-detection analyzes file size to determine optimal settings");
      HelpOverlay.Tips.Add("Larger files (>5MB) use high quality mode");
      HelpOverlay.Tips.Add("Medium files (1-5MB) use standard quality mode");
      HelpOverlay.Tips.Add("Small files (<1MB) use fast quality mode");
      HelpOverlay.Tips.Add("Profile name is optional - defaults to filename if not provided");
      HelpOverlay.Tips.Add("Quality score indicates the cloning quality (0.0-1.0)");
      HelpOverlay.Tips.Add("Use Reset to clear results and start a new clone");

      HelpOverlay.Visibility = Microsoft.UI.Xaml.Visibility.Visible;
      HelpOverlay.Show();
    }
  }
}