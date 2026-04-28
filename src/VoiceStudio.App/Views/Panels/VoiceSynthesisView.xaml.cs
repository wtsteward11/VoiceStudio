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
using VoiceStudio.Core.Events;

namespace VoiceStudio.App.Views.Panels
{
  /// <summary>
  /// GAP-050 state hygiene: panel VM is supplied only via <see cref="UserControl.DataContext"/> from
  /// <see cref="PanelRegistry"/> — no duplicate <see cref="VoiceSynthesisViewModel"/> construction here.
  /// </summary>
  public sealed partial class VoiceSynthesisView : UserControl
  {
    private PanelHost? _parentPanelHost;
    private ContextMenuService? _contextMenuService;
    private ToastNotificationService? _toastService;
    private IDragDropService? _panelDragDropService;
    private IEventAggregator? _eventAggregator;
    private VoiceSynthesisViewModel? _subscribedVm;

    /// <summary>Compiled x:Bind root; mirrors shell-assigned <see cref="UserControl.DataContext"/>.</summary>
    public VoiceSynthesisViewModel? ViewModel => DataContext as VoiceSynthesisViewModel;

    public VoiceSynthesisView()
    {
      this.InitializeComponent();

      RegisterPropertyChangedCallback(DataContextProperty, OnDataContextPropertyChanged);

      _contextMenuService = ServiceProvider.GetContextMenuService();
      _toastService = ServiceProvider.GetToastNotificationService();
      _panelDragDropService = AppServices.TryGetDragDropService();
      _eventAggregator = AppServices.TryGetEventAggregator();

      this.Loaded += VoiceSynthesisView_Loaded;
      this.Unloaded += VoiceSynthesisView_Unloaded;

      if (this.FindName("TextInput") is Microsoft.UI.Xaml.Controls.TextBox textInput)
      {
        textInput.KeyDown += TextInput_KeyDown;
      }

      KeyboardNavigationHelper.SetupEscapeKeyHandling(this, () =>
      {
      });
    }

    private static void OnDataContextPropertyChanged(DependencyObject d, DependencyProperty dp)
    {
      if (d is VoiceSynthesisView view)
      {
        view.SyncViewModelPropertyChangedSubscription();
      }
    }

    private void SyncViewModelPropertyChangedSubscription()
    {
      if (_subscribedVm != null)
      {
        _subscribedVm.PropertyChanged -= ViewModel_PropertyChanged;
        _subscribedVm = null;
      }

      if (DataContext is VoiceSynthesisViewModel vm)
      {
        _subscribedVm = vm;
        _subscribedVm.PropertyChanged += ViewModel_PropertyChanged;
      }
    }

    private void TextInput_KeyDown(object sender, Microsoft.UI.Xaml.Input.KeyRoutedEventArgs e)
    {
      if (e.Key == Windows.System.VirtualKey.Enter && IsModifierDown(Windows.System.VirtualKey.Control))
      {
        var vm = ViewModel;
        if (vm?.SynthesizeCommand.CanExecute(null) == true)
        {
          vm.SynthesizeCommand.Execute(null);
          e.Handled = true;
        }
      }
    }

    private static bool IsModifierDown(Windows.System.VirtualKey key)
    {
      var state = Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(key);
      return (state & Windows.UI.Core.CoreVirtualKeyStates.Down) == Windows.UI.Core.CoreVirtualKeyStates.Down;
    }

    private void VoiceSynthesisView_Loaded(object sender, RoutedEventArgs e)
    {
      SyncViewModelPropertyChangedSubscription();

      _parentPanelHost = FindParentPanelHost(this);
      if (_parentPanelHost != null)
      {
        _parentPanelHost.ShowQualityBadge = true;
        _parentPanelHost.PanelTitle = "Voice Synthesis";
        _parentPanelHost.PanelIcon = "🎙️";
        UpdatePanelHostQualityMetrics();
      }

      KeyboardNavigationHelper.SetupTabNavigation(this, 0);

      if (ViewModel != null)
      {
        _panelDragDropService?.RegisterDropTarget(
            ViewModel.PanelId,
            CanAcceptSynthesisDrop);
      }
    }

    private void VoiceSynthesisView_Unloaded(object sender, RoutedEventArgs e)
    {
      if (ViewModel != null)
      {
        _panelDragDropService?.UnregisterDropTarget(ViewModel.PanelId);
      }
    }

    private static bool CanAcceptSynthesisDrop(DragPayload payload)
    {
      if (payload.PayloadType == DragPayloadType.Profile ||
          payload.PayloadType == DragPayloadType.ReferenceAudio)
        return true;
      if (payload.PayloadType != DragPayloadType.Asset || payload.Items.Count == 0)
        return false;
      return IsVoiceProfileLibraryAsset(payload.Items[0]);
    }

    private static bool IsVoiceProfileLibraryAsset(DragItem item)
    {
      if (item.Metadata == null || !item.Metadata.TryGetValue("AssetType", out var raw))
        return false;
      var t = raw?.ToString() ?? string.Empty;
      var voiceTypes = new[] { "voice", "voice_profile", "profile", "clone", "xtts", "rvc" };
      return voiceTypes.Contains(t.ToLowerInvariant());
    }

    private async Task<DropResult> HandleSynthesisDropAsync(DragPayload payload, CancellationToken cancellationToken)
    {
      _ = cancellationToken;
      var vm = ViewModel;
      if (vm == null)
      {
        return new DropResult { Success = false, TargetPanelId = PanelIds.VoiceSynthesis, ErrorMessage = "ViewModel not ready" };
      }

      if (_eventAggregator == null)
      {
        _toastService?.ShowToast(ToastType.Warning, "Drop Failed", "Cannot apply profile (events unavailable).");
        return new DropResult { Success = false, TargetPanelId = vm.PanelId, ErrorMessage = "Event aggregator unavailable" };
      }

      if (payload.PayloadType == DragPayloadType.Profile ||
          (payload.PayloadType == DragPayloadType.Asset && IsVoiceProfileLibraryAsset(payload.Items[0])))
      {
        var item = payload.Items.FirstOrDefault();
        if (item == null || string.IsNullOrEmpty(item.Id))
        {
          _toastService?.ShowToast(ToastType.Warning, "Drop Failed", "Missing profile identifier on drag payload.");
          return new DropResult { Success = false, TargetPanelId = vm.PanelId, ErrorMessage = "Missing profile id" };
        }
        _eventAggregator.Publish(new ProfileSelectedEvent(
            payload.SourcePanelId,
            item.Id,
            item.DisplayName,
            InteractionIntent.ImmediateUse));
        _toastService?.ShowToast(ToastType.Success, "Voice Selected", $"'{item.DisplayName}' selected for synthesis");
        await Task.CompletedTask.ConfigureAwait(true);
        return new DropResult { Success = true, TargetPanelId = vm.PanelId, Action = nameof(ProfileSelectedEvent) };
      }

      if (payload.PayloadType == DragPayloadType.ReferenceAudio)
      {
        var audioPath = payload.Items.FirstOrDefault()?.Id;
        if (!string.IsNullOrEmpty(audioPath))
          _toastService?.ShowToast(ToastType.Info, "Reference Audio", $"Reference audio path: {audioPath}");
        await Task.CompletedTask.ConfigureAwait(true);
        return new DropResult { Success = true, TargetPanelId = vm.PanelId, Action = "ReferenceAudio" };
      }

      await Task.CompletedTask.ConfigureAwait(true);
      return new DropResult { Success = false, TargetPanelId = vm.PanelId, ErrorMessage = "Unsupported synthesis drop" };
    }

    private void VoiceSynthesisPanel_DragOver(object sender, DragEventArgs e)
    {
      _ = sender;
      var vm = ViewModel;
      if (vm == null)
        return;
      if (_panelDragDropService is { IsDragging: true } && _panelDragDropService.CanDrop(vm.PanelId))
      {
        e.AcceptedOperation = DataPackageOperation.Copy;
        _panelDragDropService.UpdateDragTarget(vm.PanelId);
        e.Handled = true;
      }
    }

    private async void VoiceSynthesisPanel_Drop(object sender, DragEventArgs e)
    {
      _ = sender;
      var vm = ViewModel;
      if (vm == null)
        return;
      if (_panelDragDropService is { IsDragging: true } && _panelDragDropService.CanDrop(vm.PanelId))
      {
        _ = await _panelDragDropService.ExecuteDropAsync(
            vm.PanelId,
            HandleSynthesisDropAsync,
            CancellationToken.None).ConfigureAwait(true);
        e.Handled = true;
      }
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
      if (e.PropertyName == nameof(VoiceSynthesisViewModel.QualityMetrics) ||
          e.PropertyName == nameof(VoiceSynthesisViewModel.HasQualityMetrics))
      {
        UpdatePanelHostQualityMetrics();
      }
    }

    private void UpdatePanelHostQualityMetrics()
    {
      if (_parentPanelHost != null && ViewModel != null)
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
          var vm = ViewModel;
          if (vm != null && vm.LoadProfilesCommand.CanExecute(null))
          {
            await vm.LoadProfilesCommand.ExecuteAsync(null);
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
        ViewModel?.ToggleEngineSelection(engine);
      }
    }

    private void ErrorInfoBar_Closed(object sender, Microsoft.UI.Xaml.Controls.InfoBarClosedEventArgs e)
    {
      var vm = ViewModel;
      if (vm != null && vm.ClearErrorCommand.CanExecute(null))
      {
        vm.ClearErrorCommand.Execute(null);
      }
    }

    private void ConsentInfoBar_Closed(object sender, Microsoft.UI.Xaml.Controls.InfoBarClosedEventArgs e)
    {
      var vm = ViewModel;
      if (vm != null && vm.ClearErrorCommand.CanExecute(null))
        vm.ClearErrorCommand.Execute(null);
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
