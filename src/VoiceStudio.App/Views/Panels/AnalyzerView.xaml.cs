using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using VoiceStudio.App.Services;
using VoiceStudio.Core.Panels;
using VoiceStudio.Core.Services;
using Microsoft.UI.Xaml;
using Windows.ApplicationModel.DataTransfer;

namespace VoiceStudio.App.Views.Panels
{
  /// <summary>
  /// Analyzer panel for audio visualization and analysis.
  /// Implements INavigatablePanel for search-result focus (audio by ID).
  /// </summary>
  public sealed partial class AnalyzerView : UserControl, INavigatablePanel
  {
    public AnalyzerViewModel ViewModel { get; }
    private ContextMenuService? _contextMenuService;
    private ToastNotificationService? _toastService;

    public AnalyzerView()
    {
      this.InitializeComponent();
      // Wire DataContext with BackendClient and AudioPlayerService
      ViewModel = new AnalyzerViewModel(
          AppServices.GetRequiredService<IAnalyzerClient>(),
          AppServices.GetRequiredService<IAudioVisualizationService>(),
          ServiceProvider.GetAudioPlayerService()
      );
      this.DataContext = ViewModel;

      // Initialize services
      _contextMenuService = ServiceProvider.GetContextMenuService();
      _toastService = ServiceProvider.GetToastNotificationService();

      // Wire TabView selection to ViewModel
      TabView.SelectionChanged += (_, _) =>
      {
        if (TabView.SelectedItem is TabViewItem selectedTab)
        {
          ViewModel.SelectedTab = selectedTab.Header?.ToString() ?? "Waveform";
        }
      };

      // Add Enter key handling for Load button
      this.KeyDown += AnalyzerView_KeyDown;

      // Subscribe to ViewModel events for toast notifications
      ViewModel.PropertyChanged += (s, e) =>
      {
        if (e.PropertyName == nameof(AnalyzerViewModel.ErrorMessage) && !string.IsNullOrEmpty(ViewModel.ErrorMessage))
        {
          _toastService?.ShowToast(ToastType.Error, "Analysis Error", ViewModel.ErrorMessage);
        }
      };

      // Setup keyboard navigation
      this.Loaded += AnalyzerView_Loaded;

      // Setup Escape key to close help overlay
      KeyboardNavigationHelper.SetupEscapeKeyHandling(this, () =>
      {
        if (HelpOverlay.IsVisible)
        {
          HelpOverlay.Hide();
        }
      });
    }

    private void AnalyzerView_Loaded(object sender, RoutedEventArgs e)
    {
      // Setup Tab navigation order for this panel
      KeyboardNavigationHelper.SetupTabNavigation(this, 0);
    }

    private void AnalyzerView_KeyDown(object sender, Microsoft.UI.Xaml.Input.KeyRoutedEventArgs e)
    {
      // Enter key in Audio ID TextBox should trigger Load
      if (e.Key == Windows.System.VirtualKey.Enter)
      {
        var focusedElement = Microsoft.UI.Xaml.Input.FocusManager.GetFocusedElement(this.XamlRoot);
        if (focusedElement is Microsoft.UI.Xaml.Controls.TextBox && ViewModel.LoadVisualizationCommand.CanExecute(null))
        {
          ViewModel.LoadVisualizationCommand.Execute(null);
          e.Handled = true;
        }
      }
    }

    private void AudioIdTextBox_RightTapped(object sender, Microsoft.UI.Xaml.Input.RightTappedRoutedEventArgs e)
    {
      if (sender is TextBox textBox && _contextMenuService != null)
      {
        e.Handled = true;
        var menu = new MenuFlyout();

        var pasteItem = new MenuFlyoutItem { Text = "Paste" };
        pasteItem.Click += (_, _) =>
        {
          var clipboard = Windows.ApplicationModel.DataTransfer.Clipboard.GetContent();
          if (clipboard.Contains(Windows.ApplicationModel.DataTransfer.StandardDataFormats.Text))
          {
            _ = clipboard.GetTextAsync().AsTask().ContinueWith(task =>
                    {
                      if (task.IsCompletedSuccessfully)
                      {
                        DispatcherQueue.TryEnqueue(() =>
                                {
                                  textBox.Text = task.Result;
                                  _toastService?.ShowToast(ToastType.Success, "Pasted", "Audio ID pasted");
                                });
                      }
                    });
          }
        };
        menu.Items.Add(pasteItem);

        var copyItem = new MenuFlyoutItem { Text = "Copy" };
        copyItem.Click += (_, _) =>
        {
          if (!string.IsNullOrEmpty(textBox.Text))
          {
            var dataPackage = new Windows.ApplicationModel.DataTransfer.DataPackage();
            dataPackage.SetText(textBox.Text);
            Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(dataPackage);
            _toastService?.ShowToast(ToastType.Success, "Copied", "Audio ID copied to clipboard");
          }
        };
        copyItem.IsEnabled = !string.IsNullOrEmpty(textBox.Text);
        menu.Items.Add(copyItem);

        var clearItem = new MenuFlyoutItem { Text = "Clear" };
        clearItem.Click += (_, _) =>
        {
          textBox.Text = string.Empty;
          _toastService?.ShowToast(ToastType.Info, "Cleared", "Audio ID cleared");
        };
        clearItem.IsEnabled = !string.IsNullOrEmpty(textBox.Text);
        menu.Items.Add(clearItem);

        var position = e.GetPosition(textBox);
        _contextMenuService.ShowContextMenu(menu, textBox, position);
      }
    }

    private void HelpButton_Click(object _, Microsoft.UI.Xaml.RoutedEventArgs __)
    {
      HelpOverlay.Title = "Audio Analyzer Help";
      HelpOverlay.HelpText = "The Audio Analyzer provides various visualizations and analysis tools for audio files. Use different tabs to view waveforms, spectrograms, radar charts, loudness analysis, and phase information. Load an audio file by entering its ID or filename and clicking Load.";

      HelpOverlay.Shortcuts.Clear();
      HelpOverlay.Shortcuts.Add(new Controls.KeyboardShortcut { Key = "Enter", Description = "Load audio for analysis" });
      HelpOverlay.Shortcuts.Add(new Controls.KeyboardShortcut { Key = "Tab", Description = "Switch between visualization tabs" });
      HelpOverlay.Shortcuts.Add(new Controls.KeyboardShortcut { Key = "Mouse Wheel", Description = "Zoom in/out on waveform/spectrogram" });
      HelpOverlay.Shortcuts.Add(new Controls.KeyboardShortcut { Key = "Drag", Description = "Pan waveform/spectrogram" });
      HelpOverlay.Shortcuts.Add(new Controls.KeyboardShortcut { Key = "Click", Description = "Seek to position in audio" });

      HelpOverlay.Tips.Clear();
      HelpOverlay.Tips.Add("Waveform view shows amplitude over time");
      HelpOverlay.Tips.Add("Spectrogram shows frequency content over time");
      HelpOverlay.Tips.Add("Loudness analysis helps ensure proper audio levels");
      HelpOverlay.Tips.Add("Phase analysis can reveal stereo imaging issues");
      HelpOverlay.Tips.Add("Radar charts provide multi-dimensional audio analysis");
      HelpOverlay.Tips.Add("Use mouse wheel to zoom, drag to pan, and click to seek");

      HelpOverlay.Visibility = Microsoft.UI.Xaml.Visibility.Visible;
      HelpOverlay.Show();
    }

    private void ZoomInButton_Click(object _, Microsoft.UI.Xaml.RoutedEventArgs __)
    {
      if (ViewModel.IsWaveformTab == Microsoft.UI.Xaml.Visibility.Visible && WaveformControl != null)
      {
        WaveformControl.ZoomLevel = Math.Min(10.0, WaveformControl.ZoomLevel * 1.2);
      }
      else if (ViewModel.IsSpectralTab == Microsoft.UI.Xaml.Visibility.Visible && SpectrogramControl != null)
      {
        SpectrogramControl.ZoomLevel = Math.Min(10.0, SpectrogramControl.ZoomLevel * 1.2);
      }
    }

    private void ZoomOutButton_Click(object _, Microsoft.UI.Xaml.RoutedEventArgs __)
    {
      if (ViewModel.IsWaveformTab == Microsoft.UI.Xaml.Visibility.Visible && WaveformControl != null)
      {
        WaveformControl.ZoomLevel = Math.Max(0.1, WaveformControl.ZoomLevel / 1.2);
      }
      else if (ViewModel.IsSpectralTab == Microsoft.UI.Xaml.Visibility.Visible && SpectrogramControl != null)
      {
        SpectrogramControl.ZoomLevel = Math.Max(0.1, SpectrogramControl.ZoomLevel / 1.2);
      }
    }

    private void ResetZoomButton_Click(object _, Microsoft.UI.Xaml.RoutedEventArgs __)
    {
      if (ViewModel.IsWaveformTab == Microsoft.UI.Xaml.Visibility.Visible && WaveformControl != null)
      {
        WaveformControl.ZoomLevel = 1.0;
      }
      else if (ViewModel.IsSpectralTab == Microsoft.UI.Xaml.Visibility.Visible && SpectrogramControl != null)
      {
        SpectrogramControl.ZoomLevel = 1.0;
      }
    }

    /// <inheritdoc />
    public Task<bool> NavigateToItemAsync(
        string itemId,
        string resultType,
        CancellationToken ct,
        IReadOnlyDictionary<string, object>? searchMetadata = null)
    {
      _ = searchMetadata;
      var type = resultType?.ToLowerInvariant() ?? string.Empty;
      if (type != "audio" && type != "project_audio")
        return Task.FromResult(false);
      return ViewModel.NavigateToAudioAsync(itemId, ct);
    }
  }
}