using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml;
using VoiceStudio.App.ViewModels;
using VoiceStudio.App.Services;
using System;
using Windows.ApplicationModel.DataTransfer;

namespace VoiceStudio.App.Views.Panels
{
  /// <summary>
  /// RealTimeAudioVisualizerView panel for real-time audio visualization.
  /// </summary>
  public sealed partial class RealTimeAudioVisualizerView : UserControl
  {
    public RealTimeAudioVisualizerViewModel ViewModel { get; }
    private ContextMenuService? _contextMenuService;
    private ToastNotificationService? _toastService;

    public RealTimeAudioVisualizerView()
    {
      this.InitializeComponent();
      ViewModel = new RealTimeAudioVisualizerViewModel(
          AppServices.GetRequiredService<VoiceStudio.Core.Services.IViewModelContext>(),
          VoiceStudio.App.Services.ServiceProvider.GetRealTimeAudioVisualizerClient()
      );
      DataContext = ViewModel;

      // Initialize services
      _contextMenuService = ServiceProvider.GetContextMenuService();
      _toastService = ServiceProvider.GetToastNotificationService();

      // Subscribe to ViewModel events for toast notifications
      ViewModel.PropertyChanged += (_, e) =>
      {
        if (e.PropertyName == nameof(RealTimeAudioVisualizerViewModel.ErrorMessage) && !string.IsNullOrEmpty(ViewModel.ErrorMessage))
        {
          _toastService?.ShowToast(ToastType.Error, "Visualizer Error", ViewModel.ErrorMessage);
        }
        else if (e.PropertyName == nameof(RealTimeAudioVisualizerViewModel.StatusMessage) && !string.IsNullOrEmpty(ViewModel.StatusMessage))
        {
          _toastService?.ShowToast(ToastType.Success, "Visualizer", ViewModel.StatusMessage);
        }
      };

      // Setup keyboard navigation
      this.Loaded += RealTimeAudioVisualizerView_KeyboardNavigation_Loaded;

      // Setup Escape key to close help overlay
      KeyboardNavigationHelper.SetupEscapeKeyHandling(this, () =>
      {
        if (HelpOverlay.IsVisible)
        {
          HelpOverlay.IsVisible = false;
        }
      });
    }

    private void RealTimeAudioVisualizerView_KeyboardNavigation_Loaded(object _, RoutedEventArgs __)
    {
      KeyboardNavigationHelper.SetupTabNavigation(this);
    }

    private void HelpButton_Click(object _, Microsoft.UI.Xaml.RoutedEventArgs __)
    {
      HelpOverlay.Title = "Real-Time Audio Visualizer Help";
      HelpOverlay.HelpText = "The Real-Time Audio Visualizer provides live audio visualization via WebSocket streaming. Choose visualization type (waveform, spectrogram, or both), configure FFT parameters and update rate, then start a visualization session. The visualizer updates in real-time as audio is streamed, allowing you to monitor audio characteristics as they happen.";

      HelpOverlay.Shortcuts.Clear();
      HelpOverlay.Shortcuts.Add(new Controls.KeyboardShortcut { Key = "Space", Description = "Start/Stop visualization" });

      HelpOverlay.Tips.Clear();
      HelpOverlay.Tips.Add("Real-time visualization requires an active audio stream");
      HelpOverlay.Tips.Add("Adjust update rate (FPS) to balance performance and smoothness");
      HelpOverlay.Tips.Add("Larger FFT sizes provide better frequency resolution but require more processing");
      HelpOverlay.Tips.Add("Window type affects frequency accuracy and time resolution trade-offs");
      HelpOverlay.Tips.Add("Visualization sessions can be saved and resumed later");

      HelpOverlay.Visibility = Microsoft.UI.Xaml.Visibility.Visible;
      HelpOverlay.Show();
    }

    private void SessionId_RightTapped(object sender, RightTappedRoutedEventArgs e)
    {
      e.Handled = true;
      if (_contextMenuService != null && ViewModel != null && !string.IsNullOrEmpty(ViewModel.SessionId))
      {
        var menu = new MenuFlyout();

        var copyIdItem = new MenuFlyoutItem { Text = "Copy Session ID" };
        copyIdItem.Click += (_, _) =>
        {
          var dataPackage = new Windows.ApplicationModel.DataTransfer.DataPackage();
          dataPackage.SetText(ViewModel.SessionId);
          Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(dataPackage);
          _toastService?.ShowSuccess("Session ID copied to clipboard", "Copy Session ID");
        };
        menu.Items.Add(copyIdItem);

        var target = sender as UIElement;
        if (target != null)
        {
          _contextMenuService.ShowContextMenu(menu, target, e.GetPosition(target));
        }
      }
    }
  }
}