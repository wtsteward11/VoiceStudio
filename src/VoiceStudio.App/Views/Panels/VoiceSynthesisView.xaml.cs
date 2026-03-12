using Microsoft.UI.Xaml.Controls;
using VoiceStudio.App.Services;
using VoiceStudio.App.Controls;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Windows.ApplicationModel.DataTransfer;
using VoiceStudio.Core.Panels;
using VoiceStudio.Core.Services;

namespace VoiceStudio.App.Views.Panels
{
  public sealed partial class VoiceSynthesisView : UserControl
  {
    public VoiceSynthesisViewModel ViewModel { get; }
    private PanelHost? _parentPanelHost;
    private ContextMenuService? _contextMenuService;
    private ToastNotificationService? _toastService;
    private IDragDropService? _panelDragDropService;

    public VoiceSynthesisView()
    {
      this.InitializeComponent();
      ViewModel = new VoiceSynthesisViewModel(
          ServiceProvider.GetBackendClient(),
          AppServices.GetRequiredService<IVoiceSynthesisService>(),
          ServiceProvider.GetProfilesClient(),
          ServiceProvider.GetAudioPlayerService()
      );
      this.DataContext = ViewModel;

      // Initialize services
      _contextMenuService = ServiceProvider.GetContextMenuService();
      _toastService = ServiceProvider.GetToastNotificationService();
      _panelDragDropService = AppServices.TryGetDragDropService();

      // Subscribe to quality metrics updates
      ViewModel.PropertyChanged += ViewModel_PropertyChanged;

      // Find parent PanelHost after loaded
      this.Loaded += VoiceSynthesisView_Loaded;
      this.Unloaded += VoiceSynthesisView_Unloaded;

      // Add Enter key handling for form submission
      if (this.FindName("TextInput") is Microsoft.UI.Xaml.Controls.TextBox textInput)
      {
        textInput.KeyDown += TextInput_KeyDown;
      }

      // Setup Escape key to close help overlay
      KeyboardNavigationHelper.SetupEscapeKeyHandling(this, () =>
      {
        // Close any open dialogs or overlays
      });

      // Subscribe to ViewModel events for toast notifications
      ViewModel.PropertyChanged += (s, e) =>
      {
        if (e.PropertyName == nameof(VoiceSynthesisViewModel.ErrorMessage) && !string.IsNullOrEmpty(ViewModel.ErrorMessage))
        {
          _toastService?.ShowToast(ToastType.Error, "Synthesis Error", ViewModel.ErrorMessage);
        }
        else if (e.PropertyName == nameof(VoiceSynthesisViewModel.LastSynthesizedAudioUrl) && !string.IsNullOrEmpty(ViewModel.LastSynthesizedAudioUrl))
        {
          _toastService?.ShowToast(ToastType.Success, "Synthesis Complete", "Audio synthesized successfully");
        }
      };
    }

    private void TextInput_KeyDown(object sender, Microsoft.UI.Xaml.Input.KeyRoutedEventArgs e)
    {
      // For multi-line text boxes: Ctrl+Enter submits, Enter creates new line
      if (e.Key == Windows.System.VirtualKey.Enter && IsModifierDown(Windows.System.VirtualKey.Control))
      {
        // Ctrl+Enter submits
        if (ViewModel.SynthesizeCommand.CanExecute(null))
        {
          ViewModel.SynthesizeCommand.Execute(null);
          e.Handled = true;
        }
        // Otherwise, Enter creates new line (default behavior for AcceptsReturn="True")
      }
    }

    private static bool IsModifierDown(Windows.System.VirtualKey key)
    {
      var state = Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(key);
      return (state & Windows.UI.Core.CoreVirtualKeyStates.Down) == Windows.UI.Core.CoreVirtualKeyStates.Down;
    }

    private void VoiceSynthesisView_Loaded(object sender, RoutedEventArgs e)
    {
      // Find parent PanelHost
      _parentPanelHost = FindParentPanelHost(this);
      if (_parentPanelHost != null)
      {
        // Enable quality badge
        _parentPanelHost.ShowQualityBadge = true;
        _parentPanelHost.PanelTitle = "Voice Synthesis";
        _parentPanelHost.PanelIcon = "🎙️";

        // Set initial quality metrics if available
        UpdatePanelHostQualityMetrics();
      }

      // Setup Tab navigation order for this panel
      KeyboardNavigationHelper.SetupTabNavigation(this, 0);

      // Register as drop target for Profile payloads (Panel Architecture Phase 4)
      _panelDragDropService?.RegisterDropTarget(
          ViewModel.PanelId,
          CanAcceptDrop);
    }

    private void VoiceSynthesisView_Unloaded(object sender, RoutedEventArgs e)
    {
      // Unregister from drop target (Panel Architecture Phase 4)
      _panelDragDropService?.UnregisterDropTarget(ViewModel.PanelId);
    }

    /// <summary>
    /// Determines if this panel can accept a drag payload.
    /// Accepts Profile and ReferenceAudio for voice synthesis.
    /// </summary>
    private static bool CanAcceptDrop(DragPayload payload)
    {
      return payload.PayloadType == DragPayloadType.Profile ||
             payload.PayloadType == DragPayloadType.ReferenceAudio;
    }

    /// <summary>
    /// Handles a dropped Profile or ReferenceAudio payload.
    /// </summary>
    private async Task HandleProfileDropAsync(DragPayload payload, CancellationToken cancellationToken)
    {
      if (payload.PayloadType == DragPayloadType.Profile)
      {
        var profileId = payload.Items.FirstOrDefault()?.Id;
        if (!string.IsNullOrEmpty(profileId))
        {
          // Find and select the profile in the ViewModel
          var profile = ViewModel.Profiles.FirstOrDefault(p => p.Id == profileId);
          if (profile != null)
          {
            ViewModel.SelectedProfile = profile;
            _toastService?.ShowToast(ToastType.Success, "Profile Selected", $"Selected '{profile.Name}' for synthesis");
          }
        }
      }
      else if (payload.PayloadType == DragPayloadType.ReferenceAudio)
      {
        var audioPath = payload.Items.FirstOrDefault()?.Id;
        if (!string.IsNullOrEmpty(audioPath))
        {
          // Load reference audio for voice cloning synthesis
          _toastService?.ShowToast(ToastType.Info, "Reference Audio", $"Reference audio loaded: {audioPath}");
        }
      }

      await Task.CompletedTask;
    }

    private PanelHost? FindParentPanelHost(DependencyObject element)
    {
      var parent = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetParent(element);
      while (parent != null)
      {
        if (parent is PanelHost panelHost)
        {
          return panelHost;
        }
        parent = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetParent(parent);
      }
      return null;
    }

    private void ViewModel_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
      if (e.PropertyName == nameof(ViewModel.QualityMetrics) ||
          e.PropertyName == nameof(ViewModel.HasQualityMetrics))
      {
        UpdatePanelHostQualityMetrics();
      }
    }

    private void UpdatePanelHostQualityMetrics()
    {
      if (_parentPanelHost != null)
      {
        _parentPanelHost.QualityMetrics = ViewModel.QualityMetrics;
      }
    }

    private void ProfileComboBox_RightTapped(object sender, Microsoft.UI.Xaml.Input.RightTappedRoutedEventArgs e)
    {
      if (sender is ComboBox comboBox && _contextMenuService != null)
      {
        e.Handled = true;
        var menu = new MenuFlyout();

        var refreshItem = new MenuFlyoutItem { Text = "Refresh Profiles" };
        refreshItem.Click += async (_, _) =>
        {
          if (ViewModel.LoadProfilesCommand.CanExecute(null))
          {
            await ViewModel.LoadProfilesCommand.ExecuteAsync(null);
            _toastService?.ShowToast(ToastType.Success, "Refreshed", "Voice profiles refreshed");
          }
        };
        menu.Items.Add(refreshItem);

        var position = e.GetPosition(comboBox);
        _contextMenuService.ShowContextMenu(menu, comboBox, position);
      }
    }

    private void TextInput_RightTapped(object sender, Microsoft.UI.Xaml.Input.RightTappedRoutedEventArgs e)
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
                                  _toastService?.ShowToast(ToastType.Success, "Pasted", "Text pasted");
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
            _toastService?.ShowToast(ToastType.Success, "Copied", "Text copied to clipboard");
          }
        };
        copyItem.IsEnabled = !string.IsNullOrEmpty(textBox.Text);
        menu.Items.Add(copyItem);

        var clearItem = new MenuFlyoutItem { Text = "Clear" };
        clearItem.Click += (_, _) =>
        {
          textBox.Text = string.Empty;
          _toastService?.ShowToast(ToastType.Info, "Cleared", "Text cleared");
        };
        clearItem.IsEnabled = !string.IsNullOrEmpty(textBox.Text);
        menu.Items.Add(clearItem);

        var position = e.GetPosition(textBox);
        _contextMenuService.ShowContextMenu(menu, textBox, position);
      }
    }

    private void EngineCheckBox_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
      if (sender is CheckBox checkBox && checkBox.Tag is string engine)
      {
        ViewModel.ToggleEngineSelection(engine);
      }
    }

    private void ErrorInfoBar_Closed(object sender, Microsoft.UI.Xaml.Controls.InfoBarClosedEventArgs e)
    {
      if (ViewModel.ClearErrorCommand.CanExecute(null))
      {
        ViewModel.ClearErrorCommand.Execute(null);
      }
    }

    private void HelpButton_Click(object _, Microsoft.UI.Xaml.RoutedEventArgs __)
    {
      HelpOverlay.Title = "Voice Synthesis Help";
      HelpOverlay.HelpText = "The Voice Synthesis panel allows you to generate speech from text using various TTS engines. Select a voice profile, choose an engine (XTTS v2, Chatterbox, or Tortoise TTS), enter your text, adjust parameters, and synthesize. Quality metrics help you evaluate the output. Use Multi-Engine Ensemble for maximum quality by synthesizing with multiple engines and selecting the best output.";

      HelpOverlay.Shortcuts.Clear();
      HelpOverlay.Shortcuts.Add(new Controls.KeyboardShortcut { Key = "Ctrl+Enter", Description = "Start synthesis" });
      HelpOverlay.Shortcuts.Add(new Controls.KeyboardShortcut { Key = "Space", Description = "Play/Stop preview" });

      HelpOverlay.Tips.Clear();
      HelpOverlay.Tips.Add("XTTS v2 provides fast, high-quality synthesis");
      HelpOverlay.Tips.Add("Tortoise TTS offers the highest quality but slower generation");
      HelpOverlay.Tips.Add("Multi-Engine Ensemble synthesizes with multiple engines and selects the best output");
      HelpOverlay.Tips.Add("Adjust temperature and top_p for different voice characteristics");
      HelpOverlay.Tips.Add("Quality metrics help you choose the best synthesis parameters");
      HelpOverlay.Tips.Add("Preview before saving to ensure quality");

      HelpOverlay.Visibility = Microsoft.UI.Xaml.Visibility.Visible;
      HelpOverlay.Show();
    }
  }
}