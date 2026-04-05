using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml;
using VoiceStudio.App.ViewModels;
using VoiceStudio.App.Services;
using VoiceStudio.App.Services.UndoableActions;
using System;

namespace VoiceStudio.App.Views.Panels
{
  /// <summary>
  /// RecordingView panel for audio recording functionality.
  /// </summary>
  public sealed partial class RecordingView : UserControl
  {
    public RecordingViewModel ViewModel { get; }
    private ContextMenuService? _contextMenuService;
    private ToastNotificationService? _toastService;
    private UndoRedoService? _undoRedoService;

    public RecordingView()
    {
      this.InitializeComponent();
      ViewModel = new RecordingViewModel(
          AppServices.GetRequiredService<VoiceStudio.Core.Services.IViewModelContext>(),
          AppServices.GetRequiredService<VoiceStudio.Core.Services.IRecordingClient>(),
          AppServices.GetRequiredService<VoiceStudio.Core.Services.IProjectAudioClient>(),
          ServiceProvider.GetAudioPlayerService(),
          AppServices.GetRequiredService<VoiceStudio.Core.Services.IRecordingSessionCoordinator>(),
          AppServices.GetRequiredService<IRecordingCaptureFanoutService>(),
          AppServices.TryGetRecordingInputCommandState(),
          AppServices.TryGetRecordingDeviceAvailabilityService());
      DataContext = ViewModel;

      // Initialize services
      _contextMenuService = ServiceProvider.GetContextMenuService();
      _toastService = ServiceProvider.GetToastNotificationService();
      _undoRedoService = ServiceProvider.GetUndoRedoService();

      // Subscribe to ViewModel events for toast notifications and waveform updates
      ViewModel.PropertyChanged += (_, e) =>
      {
        if (e.PropertyName == nameof(RecordingViewModel.ErrorMessage) && !string.IsNullOrEmpty(ViewModel.ErrorMessage))
        {
          _toastService?.ShowToast(ToastType.Error, "Recording Error", ViewModel.ErrorMessage);
        }
        else if (e.PropertyName == nameof(RecordingViewModel.StatusMessage) && !string.IsNullOrEmpty(ViewModel.StatusMessage))
        {
          _toastService?.ShowToast(ToastType.Success, "Recording", ViewModel.StatusMessage);
        }
        else if (e.PropertyName == nameof(RecordingViewModel.WaveformSamples))
        {
          // Note: Update waveform control with new samples
          // RecordingWaveform control not yet implemented in XAML
        }
      };

      // Setup keyboard navigation
      this.Loaded += RecordingView_Loaded;

      // Setup Escape key to close help overlay
      KeyboardNavigationHelper.SetupEscapeKeyHandling(this, () =>
      {
        if (HelpOverlay.IsVisible)
        {
          HelpOverlay.Hide();
        }
      });

      // Setup Space key for start/stop recording
      KeyboardNavigationHelper.SetupSpaceKeyHandling(this, () =>
      {
        if (ViewModel.IsRecording)
        {
          ViewModel.StopRecordingCommand.Execute(null);
        }
        else
        {
          ViewModel.StartRecordingCommand.Execute(null);
        }
      });
    }

    private void RecordingView_Loaded(object _, RoutedEventArgs __)
    {
      // Setup Tab navigation order for this panel
      KeyboardNavigationHelper.SetupTabNavigation(this, 0);
    }

    private void HelpButton_Click(object _, Microsoft.UI.Xaml.RoutedEventArgs __)
    {
      HelpOverlay.Title = "Recording Help";
      HelpOverlay.HelpText = "The Recording panel allows you to record audio directly in VoiceStudio. Select input device, configure recording settings (sample rate, bit depth, channels), and record audio. Recorded audio is automatically saved and can be used in projects, for training, or for voice synthesis.";

      HelpOverlay.Shortcuts.Clear();
      HelpOverlay.Shortcuts.Add(new Controls.KeyboardShortcut { Key = "Space", Description = "Start/Stop recording" });
      HelpOverlay.Shortcuts.Add(new Controls.KeyboardShortcut { Key = "Escape", Description = "Close help" });

      HelpOverlay.Tips.Clear();
      HelpOverlay.Tips.Add("Select the correct input device before recording");
      HelpOverlay.Tips.Add("Higher sample rates (48kHz, 96kHz) provide better quality but larger files");
      HelpOverlay.Tips.Add("Use mono for voice recordings to save space");
      HelpOverlay.Tips.Add("Record in a quiet environment for best results");
      HelpOverlay.Tips.Add("Recorded audio is automatically saved and available in the library");

      HelpOverlay.Visibility = Microsoft.UI.Xaml.Visibility.Visible;
      HelpOverlay.Show();
    }

    private void DeviceComboBox_RightTapped(object sender, RightTappedRoutedEventArgs e)
    {
      e.Handled = true;
      if (_contextMenuService != null && ViewModel != null)
      {
        var menu = new MenuFlyout();

        var refreshItem = new MenuFlyoutItem { Text = "Refresh Devices" };
        refreshItem.Click += (_, _) => ViewModel.LoadDevicesCommand.Execute(null);
        menu.Items.Add(refreshItem);

        var target = sender as UIElement;
        if (target != null)
        {
          _contextMenuService.ShowContextMenu(menu, target, e.GetPosition(target));
        }
      }
    }
  }
}