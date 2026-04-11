using System;
using System.Collections.ObjectModel;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Xaml.Controls;
using VoiceStudio.Core.Events;
using VoiceStudio.Core.Models;
using VoiceStudio.Core.Panels;
using VoiceStudio.Core.Services;
using VoiceStudio.App.Services;
using VoiceStudio.App.Services.UndoableActions;
using VoiceStudio.App.Utilities;

namespace VoiceStudio.App.Views.Panels
{
  public partial class EffectsMixerViewModel : ObservableObject, IPanelView, IPanelLifecycle, IDisposable
  {
    private readonly IEffectsMeterClient _effectsMeterClient;
    private readonly IEffectChainClient _effectChainClient;
    private readonly IMixerStateClient _mixerStateClient;
    private readonly IMeterClient? _meterClient;
    private readonly UndoRedoService? _undoRedoService;
    private readonly ToastNotificationService? _toastNotificationService;
    private readonly MultiSelectService _multiSelectService;
    private readonly IErrorPresentationService? _errorService;
    private readonly IErrorLoggingService? _logService;
    private MultiSelectState? _multiSelectState;
    private IEventAggregator? _eventAggregator;
    private IContextManager? _contextManager;
    private ISubscriptionToken? _projectChangedToken;
    private CancellationTokenSource? _pollingCts;
    private CancellationTokenSource? _disposalCts;
    private CancellationTokenSource? _selectionLoadCts;
    private bool _isPolling;
    private bool _disposed;

    /// <summary>GAP-012: last known bypass for undo pairing (session flag, not persisted on chain).</summary>
    private bool _lastBypassValue;

    /// <summary>GAP-012: suppress bypass undo when applying from undo/redo stack.</summary>
    private bool _suppressBypassUndoRegistration;

    public string PanelId => PanelIds.EffectsMixer;
    public string DisplayName => ResourceHelper.GetString("Panel.EffectsMixer.DisplayName", "Effects & Mixer");
    public PanelRegion Region => PanelRegion.Right;

    [ObservableProperty]
    private ObservableCollection<MixerChannel> channels = new();

    // Mixer state management
    [ObservableProperty]
    private MixerState? mixerState;

    [ObservableProperty]
    private ObservableCollection<MixerSend> sends = new();

    [ObservableProperty]
    private ObservableCollection<MixerReturn> returns = new();

    [ObservableProperty]
    private ObservableCollection<MixerSubGroup> subGroups = new();

    [ObservableProperty]
    private ObservableCollection<MixerPreset> mixerPresets = new();

    [ObservableProperty]
    private MixerPreset? selectedMixerPreset;

    [ObservableProperty]
    private string? selectedAudioId;

    [ObservableProperty]
    private bool isLoading;

    [ObservableProperty]
    private string? errorMessage;

    [ObservableProperty]
    private bool isRealTimeUpdatesEnabled;

    // Effect chain management
    [ObservableProperty]
    private string? selectedProjectId;

    [ObservableProperty]
    private ObservableCollection<EffectChain> effectChains = new();

    [ObservableProperty]
    private EffectChain? selectedEffectChain;

    [ObservableProperty]
    private Effect? selectedEffect;

    [ObservableProperty]
    private ObservableCollection<EffectPreset> effectPresets = new();

    [ObservableProperty]
    private bool showEffectChainsView = true; // Toggle between Chains and Presets

    /// <summary>GAP-039: When true, effect chain process requests use bypass_chain (dry signal).</summary>
    [ObservableProperty]
    private bool isEffectChainBypassed;

    /// <summary>GAP-048: Studio Sound one-click processing in progress (drives ProgressRing).</summary>
    [ObservableProperty]
    private bool isStudioSoundRunning;

    /// <summary>GAP-048: Last Studio Sound output artifact id (session only).</summary>
    [ObservableProperty]
    private string? studioSoundOutputAudioId;

    [ObservableProperty]
    private MixerMaster master = new();

    [ObservableProperty]
    private MixerSubGroup? selectedSubGroup;

    [ObservableProperty]
    private MixerSend? selectedSend;

    [ObservableProperty]
    private MixerReturn? selectedReturn;

    // Multi-select support
    [ObservableProperty]
    private int selectedChannelCount;

    [ObservableProperty]
    private bool hasMultipleChannelSelection;

    /// <summary>GAP-048: Whether the one-click Studio Sound action can run.</summary>
    public bool CanRunStudioSound =>
        !string.IsNullOrWhiteSpace(SelectedProjectId)
        && !string.IsNullOrWhiteSpace(SelectedAudioId)
        && !IsLoading
        && !IsStudioSoundRunning;

    public bool IsChannelSelected(string channelId) => _multiSelectState?.SelectedIds.Contains(channelId) ?? false;

    // Available effect types
    /// <summary>GAP-048: Curated Studio Sound chain (denoise → compressor → normalize). Parameters are applied at run time.</summary>
    private static readonly List<Effect> StudioSoundEffects = new()
    {
      new Effect { Id = "studio-denoise", Type = "denoise", Name = "Noise Reduction", Enabled = true, Order = 0 },
      new Effect { Id = "studio-compressor", Type = "compressor", Name = "Voice Compressor", Enabled = true, Order = 1 },
      new Effect { Id = "studio-normalize", Type = "normalize", Name = "Normalize", Enabled = true, Order = 2 },
    };

    public List<string> AvailableEffectTypes { get; } = new()
        {
            "normalize",
            "denoise",
            "eq",
            "compressor",
            "reverb",
            "delay",
            "filter",
            "chorus",
            "pitch_correction",
            "convolution_reverb",
            "formant_shifter",
            "distortion",
            "multi_band_processor",
            "dynamic_eq",
            "spectral_processor",
            "granular_synthesizer",
            "vocoder"
        };

    public EffectsMixerViewModel(IEffectsMeterClient effectsMeterClient, IEffectChainClient effectChainClient, IMixerStateClient mixerStateClient, IMeterClient? meterClient = null)
    {
      _effectsMeterClient = effectsMeterClient ?? throw new ArgumentNullException(nameof(effectsMeterClient));
      _effectChainClient = effectChainClient ?? throw new ArgumentNullException(nameof(effectChainClient));
      _mixerStateClient = mixerStateClient ?? throw new ArgumentNullException(nameof(mixerStateClient));
      _meterClient = meterClient;
      _disposalCts = new CancellationTokenSource();
      _lastBypassValue = IsEffectChainBypassed;

      // Get multi-select service
      var multiSelectService = AppServices.TryGetMultiSelectService();
      _multiSelectService = multiSelectService ?? throw new InvalidOperationException("MultiSelectService is required but not registered");
      _multiSelectState = _multiSelectService.GetState(PanelId);

      // Get undo/redo service (may be null if not initialized)
      try
      {
        _undoRedoService = AppServices.TryGetUndoRedoService();
      }
      catch
      {
        // Service may not be initialized yet - that's okay
        _undoRedoService = null;
      }

      // Get toast notification service (may be null if not initialized)
      try
      {
        _toastNotificationService = AppServices.TryGetToastNotificationService();
      }
      catch
      {
        // Service may not be initialized yet - that's okay
        _toastNotificationService = null;
      }

      // Get error services
      _errorService = ServiceProvider.TryGetErrorPresentationService();
      _logService = ServiceProvider.TryGetErrorLoggingService();

      // Pass 02: Resolve context/event services for project sync
      _eventAggregator = AppServices.TryGetEventAggregator();
      _contextManager = AppServices.TryGetContextManager();

      // Multi-select commands
      SelectAllChannelsCommand = new RelayCommand(SelectAllChannels, () => Channels?.Count > 0);
      ClearChannelSelectionCommand = new RelayCommand(ClearChannelSelection);

      // Subscribe to selection changes
      _multiSelectService.SelectionChanged += (s, e) =>
      {
        if (e.PanelId == PanelId)
        {
          UpdateChannelSelectionProperties();
          OnPropertyChanged(nameof(SelectedChannelCount));
          OnPropertyChanged(nameof(HasMultipleChannelSelection));
        }
      };
      LoadMetersCommand = new EnhancedAsyncRelayCommand(async (ct) =>
      {
        using var profiler = PerformanceProfiler.StartCommand("LoadMeters");
        await LoadMetersAsync(ct);
      }, () => !string.IsNullOrWhiteSpace(SelectedAudioId) && !IsLoading);

      ToggleRealTimeUpdatesCommand = new RelayCommand(ToggleRealTimeUpdates, () => !string.IsNullOrWhiteSpace(SelectedAudioId));

      LoadEffectChainsCommand = new EnhancedAsyncRelayCommand(async (ct) =>
      {
        using var profiler = PerformanceProfiler.StartCommand("LoadEffectChains");
        await LoadEffectChainsAsync(ct);
      }, () => !string.IsNullOrWhiteSpace(SelectedProjectId) && !IsLoading);

      LoadEffectPresetsCommand = new EnhancedAsyncRelayCommand(async (ct) =>
      {
        using var profiler = PerformanceProfiler.StartCommand("LoadEffectPresets");
        await LoadEffectPresetsAsync(ct);
      }, () => !IsLoading);

      CreateEffectChainCommand = new EnhancedAsyncRelayCommand<string>(async (string? name, CancellationToken ct) =>
      {
        using var profiler = PerformanceProfiler.StartCommand("CreateEffectChain");
        await CreateEffectChainAsync(name, ct);
      }, (string? _) => !IsLoading && !string.IsNullOrWhiteSpace(SelectedProjectId));

      DeleteEffectChainCommand = new EnhancedAsyncRelayCommand<string>(async (string? chainId, CancellationToken ct) =>
      {
        using var profiler = PerformanceProfiler.StartCommand("DeleteEffectChain");
        await DeleteEffectChainAsync(chainId, ct);
      }, (string? _) => !IsLoading);

      ApplyEffectChainCommand = new EnhancedAsyncRelayCommand<string>(async (string? chainId, CancellationToken ct) =>
      {
        using var profiler = PerformanceProfiler.StartCommand("ApplyEffectChain");
        await RunEffectChainProcessAsync(chainId, isPreview: false, ct);
      }, (string? _) => !IsLoading && !string.IsNullOrWhiteSpace(SelectedProjectId) && !string.IsNullOrWhiteSpace(SelectedAudioId));

      PreviewEffectChainCommand = new EnhancedAsyncRelayCommand<string>(async (string? chainId, CancellationToken ct) =>
      {
        using var profiler = PerformanceProfiler.StartCommand("PreviewEffectChain");
        await RunEffectChainProcessAsync(chainId, isPreview: true, ct);
      }, (string? _) => !IsLoading && !string.IsNullOrWhiteSpace(SelectedProjectId) && !string.IsNullOrWhiteSpace(SelectedAudioId));

      RunStudioSoundCommand = new EnhancedAsyncRelayCommand(async (ct) =>
      {
        using var profiler = PerformanceProfiler.StartCommand("StudioSound");
        await RunStudioSoundAsync(ct);
      }, () => CanRunStudioSound);

      AddEffectCommand = new EnhancedAsyncRelayCommand<string>(async (string? effectType, CancellationToken ct) =>
      {
        using var profiler = PerformanceProfiler.StartCommand("AddEffect");
        await AddEffectToChainAsync(effectType, ct);
      }, (string? _) => !IsLoading && SelectedEffectChain != null);

      RemoveEffectCommand = new EnhancedAsyncRelayCommand<string>(async (string? effectId, CancellationToken ct) =>
      {
        using var profiler = PerformanceProfiler.StartCommand("RemoveEffect");
        await RemoveEffectFromChainAsync(effectId, ct);
      }, (string? _) => !IsLoading && SelectedEffectChain != null);

      MoveEffectUpCommand = new EnhancedAsyncRelayCommand<string>(async (string? effectId, CancellationToken ct) =>
      {
        using var profiler = PerformanceProfiler.StartCommand("MoveEffectUp");
        await MoveEffectUpAsync(effectId, ct);
      }, (string? _) => !IsLoading && SelectedEffectChain != null);

      MoveEffectDownCommand = new EnhancedAsyncRelayCommand<string>(async (string? effectId, CancellationToken ct) =>
      {
        using var profiler = PerformanceProfiler.StartCommand("MoveEffectDown");
        await MoveEffectDownAsync(effectId, ct);
      }, (string? _) => !IsLoading && SelectedEffectChain != null);

      SaveEffectChainCommand = new EnhancedAsyncRelayCommand(async (ct) =>
      {
        using var profiler = PerformanceProfiler.StartCommand("SaveEffectChain");
        await SaveEffectChainAsync(ct);
      }, () => !IsLoading && SelectedEffectChain != null && !string.IsNullOrWhiteSpace(SelectedProjectId));

      // Mixer state management commands
      LoadMixerStateCommand = new EnhancedAsyncRelayCommand(async (ct) =>
      {
        using var profiler = PerformanceProfiler.StartCommand("LoadMixerState");
        await LoadMixerStateAsync(ct);
      }, () => !string.IsNullOrWhiteSpace(SelectedProjectId) && !IsLoading);

      SaveMixerStateCommand = new EnhancedAsyncRelayCommand(async (ct) =>
      {
        using var profiler = PerformanceProfiler.StartCommand("SaveMixerState");
        await SaveMixerStateAsync(ct);
      }, () => !IsLoading && MixerState != null && !string.IsNullOrWhiteSpace(SelectedProjectId));

      ResetMixerStateCommand = new EnhancedAsyncRelayCommand(async (ct) =>
      {
        using var profiler = PerformanceProfiler.StartCommand("ResetMixerState");
        await ResetMixerStateAsync(ct);
      }, () => !IsLoading && !string.IsNullOrWhiteSpace(SelectedProjectId));

      LoadMixerPresetsCommand = new EnhancedAsyncRelayCommand(async (ct) =>
      {
        using var profiler = PerformanceProfiler.StartCommand("LoadMixerPresets");
        await LoadMixerPresetsAsync(ct);
      }, () => !string.IsNullOrWhiteSpace(SelectedProjectId) && !IsLoading);

      CreateMixerPresetCommand = new EnhancedAsyncRelayCommand<string>(async (string? name, CancellationToken ct) =>
      {
        using var profiler = PerformanceProfiler.StartCommand("CreateMixerPreset");
        await CreateMixerPresetAsync(name, ct);
      }, (string? _) => !IsLoading && !string.IsNullOrWhiteSpace(SelectedProjectId));

      ApplyMixerPresetCommand = new EnhancedAsyncRelayCommand<string>(async (string? presetId, CancellationToken ct) =>
      {
        using var profiler = PerformanceProfiler.StartCommand("ApplyMixerPreset");
        await ApplyMixerPresetAsync(presetId, ct);
      }, (string? _) => !IsLoading && !string.IsNullOrWhiteSpace(SelectedProjectId));

      // Mixer sends/returns/sub-groups commands
      CreateSendCommand = new EnhancedAsyncRelayCommand(async (ct) =>
      {
        using var profiler = PerformanceProfiler.StartCommand("CreateSend");
        await CreateSendAsync(ct);
      }, () => !IsLoading && !string.IsNullOrWhiteSpace(SelectedProjectId));

      CreateReturnCommand = new EnhancedAsyncRelayCommand(async (ct) =>
      {
        using var profiler = PerformanceProfiler.StartCommand("CreateReturn");
        await CreateReturnAsync(ct);
      }, () => !IsLoading && !string.IsNullOrWhiteSpace(SelectedProjectId));

      CreateSubGroupCommand = new EnhancedAsyncRelayCommand(async (ct) =>
      {
        using var profiler = PerformanceProfiler.StartCommand("CreateSubGroup");
        await CreateSubGroupAsync(ct);
      }, () => !IsLoading && !string.IsNullOrWhiteSpace(SelectedProjectId));

      DeleteSendCommand = new EnhancedAsyncRelayCommand<MixerSend>(async (send, ct) =>
      {
        using var profiler = PerformanceProfiler.StartCommand("DeleteSend");
        await DeleteSendAsync(send, ct);
      }, send => send != null && !IsLoading);

      DeleteReturnCommand = new EnhancedAsyncRelayCommand<MixerReturn>(async (ret, ct) =>
      {
        using var profiler = PerformanceProfiler.StartCommand("DeleteReturn");
        await DeleteReturnAsync(ret, ct);
      }, ret => ret != null && !IsLoading);

      UpdateSendCommand = new EnhancedAsyncRelayCommand<MixerSend>(async (send, ct) =>
      {
        using var profiler = PerformanceProfiler.StartCommand("UpdateSend");
        await UpdateSendAsync(send, ct);
      }, send => send != null && !IsLoading);

      UpdateReturnCommand = new EnhancedAsyncRelayCommand<MixerReturn>(async (ret, ct) =>
      {
        using var profiler = PerformanceProfiler.StartCommand("UpdateReturn");
        await UpdateReturnAsync(ret, ct);
      }, ret => ret != null && !IsLoading);

      DeleteSubGroupCommand = new EnhancedAsyncRelayCommand<MixerSubGroup>(async (sg, ct) =>
      {
        using var profiler = PerformanceProfiler.StartCommand("DeleteSubGroup");
        await DeleteSubGroupAsync(sg, ct);
      }, sg => sg != null && !IsLoading);

      UpdateSubGroupCommand = new EnhancedAsyncRelayCommand<MixerSubGroup>(async (sg, ct) =>
      {
        using var profiler = PerformanceProfiler.StartCommand("UpdateSubGroup");
        await UpdateSubGroupAsync(sg, ct);
      }, sg => sg != null && !IsLoading);

      // Initialize with default channels (will be replaced when mixer state loads)
      for (int i = 1; i <= 4; i++)
      {
        Channels.Add(new MixerChannel
        {
          ChannelNumber = i,
          Name = $"Ch {i}",
          PeakLevel = 0.0,
          RmsLevel = 0.0,
          Volume = 1.0, // 0 dB default
          Pan = 0.0, // Center default
          IsMuted = false,
          IsSoloed = false
        });
      }
    }

    public EnhancedAsyncRelayCommand LoadMetersCommand { get; }
    public IRelayCommand ToggleRealTimeUpdatesCommand { get; }
    public EnhancedAsyncRelayCommand LoadEffectChainsCommand { get; }
    public EnhancedAsyncRelayCommand LoadEffectPresetsCommand { get; }
    public EnhancedAsyncRelayCommand<string> CreateEffectChainCommand { get; }
    public EnhancedAsyncRelayCommand<string> DeleteEffectChainCommand { get; }
    public EnhancedAsyncRelayCommand<string> ApplyEffectChainCommand { get; }
    public EnhancedAsyncRelayCommand<string> PreviewEffectChainCommand { get; }
    public EnhancedAsyncRelayCommand RunStudioSoundCommand { get; }
    public EnhancedAsyncRelayCommand<string> AddEffectCommand { get; }
    public EnhancedAsyncRelayCommand<string> RemoveEffectCommand { get; }
    public EnhancedAsyncRelayCommand<string> MoveEffectUpCommand { get; }
    public EnhancedAsyncRelayCommand<string> MoveEffectDownCommand { get; }
    public EnhancedAsyncRelayCommand SaveEffectChainCommand { get; }

    public EnhancedAsyncRelayCommand LoadMixerStateCommand { get; }
    public EnhancedAsyncRelayCommand SaveMixerStateCommand { get; }
    public EnhancedAsyncRelayCommand ResetMixerStateCommand { get; }
    public EnhancedAsyncRelayCommand LoadMixerPresetsCommand { get; }
    public EnhancedAsyncRelayCommand<string> CreateMixerPresetCommand { get; }
    public EnhancedAsyncRelayCommand<string> ApplyMixerPresetCommand { get; }
    public EnhancedAsyncRelayCommand CreateSendCommand { get; }
    public EnhancedAsyncRelayCommand CreateReturnCommand { get; }
    public EnhancedAsyncRelayCommand CreateSubGroupCommand { get; }
    public EnhancedAsyncRelayCommand<MixerSend> DeleteSendCommand { get; }
    public EnhancedAsyncRelayCommand<MixerReturn> DeleteReturnCommand { get; }
    public EnhancedAsyncRelayCommand<MixerSend> UpdateSendCommand { get; }
    public EnhancedAsyncRelayCommand<MixerReturn> UpdateReturnCommand { get; }
    public EnhancedAsyncRelayCommand<MixerSubGroup> DeleteSubGroupCommand { get; }
    public EnhancedAsyncRelayCommand<MixerSubGroup> UpdateSubGroupCommand { get; }

    // Multi-select commands
    public IRelayCommand SelectAllChannelsCommand { get; }
    public IRelayCommand ClearChannelSelectionCommand { get; }

    public bool ShowEffectPresetsView => !ShowEffectChainsView;

    partial void OnShowEffectChainsViewChanged(bool value)
    {
      OnPropertyChanged(nameof(ShowEffectPresetsView));
    }

    partial void OnSelectedProjectIdChanged(string? value)
    {
      _contextManager?.SetActiveEffectChain(null, null);

      LoadEffectChainsCommand.NotifyCanExecuteChanged();
      CreateEffectChainCommand.NotifyCanExecuteChanged();
      ApplyEffectChainCommand.NotifyCanExecuteChanged();
      PreviewEffectChainCommand.NotifyCanExecuteChanged();
      RunStudioSoundCommand.NotifyCanExecuteChanged();
      LoadMixerStateCommand.NotifyCanExecuteChanged();
      SaveMixerStateCommand.NotifyCanExecuteChanged();
      ResetMixerStateCommand.NotifyCanExecuteChanged();
      LoadMixerPresetsCommand.NotifyCanExecuteChanged();
      CreateMixerPresetCommand.NotifyCanExecuteChanged();
      ApplyMixerPresetCommand.NotifyCanExecuteChanged();

      if (!string.IsNullOrWhiteSpace(value))
      {
        CancelSelectionLoad();
        var projectId = value;
        _selectionLoadCts = CancellationTokenSource.CreateLinkedTokenSource(_disposalCts!.Token);
        var ct = _selectionLoadCts.Token;
        _ = LoadProjectSelectionDataAsync(projectId, ct);
      }
      else
      {
        // Pass 02 C6: Clear stale state on project switch
        CancelSelectionLoad();
        EffectChains.Clear();
        MixerPresets.Clear();
        SelectedMixerPreset = null;
        SelectedEffectChain = null;
        SelectedEffect = null;
        EffectPresets.Clear();
        Sends.Clear();
        Returns.Clear();
        SubGroups.Clear();
        MixerState = null;
        Master = new MixerMaster();
      }
    }

    partial void OnSelectedEffectChainChanged(EffectChain? value)
    {
      _contextManager?.SetActiveEffectChain(value?.Id, SelectedProjectId);
    }

    partial void OnIsEffectChainBypassedChanged(bool value)
    {
      if (_suppressBypassUndoRegistration)
      {
        _lastBypassValue = value;
        return;
      }

      var previous = _lastBypassValue;
      _lastBypassValue = value;
      if (previous == value)
        return;

      RegisterBypassUndo(previous, value);
    }

    private void RegisterBypassUndo(bool before, bool after)
    {
      if (_undoRedoService == null)
        return;
      var action = new ToggleBypassUndoAction(
          before,
          after,
          v =>
          {
            _suppressBypassUndoRegistration = true;
            try
            {
              IsEffectChainBypassed = v;
            }
            finally
            {
              _suppressBypassUndoRegistration = false;
            }
          });
      _undoRedoService.RegisterAction(action);
    }

    private void SyncSelectedEffectChainAfterUndo(EffectChain updated)
    {
      if (SelectedEffectChain?.Id != updated.Id)
        return;
      var effectId = SelectedEffect?.Id;
      SelectedEffectChain = updated;
      if (!string.IsNullOrEmpty(effectId))
        SelectedEffect = updated.Effects?.FirstOrDefault(e => e.Id == effectId);
    }

    /// <summary>GAP-012: Called from view when effect enable checkbox toggles (after TwoWay binding applies).</summary>
    public async Task PersistEffectEnabledToggleAsync(
        Effect? effect,
        bool previousEnabled,
        bool newEnabled,
        CancellationToken cancellationToken = default)
    {
      if (effect == null || SelectedEffectChain == null || string.IsNullOrWhiteSpace(SelectedProjectId))
        return;
      if (previousEnabled == newEnabled)
        return;

      IsLoading = true;
      ErrorMessage = null;
      try
      {
        SelectedEffectChain.Modified = DateTime.UtcNow;
        var updated = await _effectChainClient.UpdateEffectChainAsync(
            SelectedProjectId,
            SelectedEffectChain.Id,
            SelectedEffectChain,
            cancellationToken);

        var index = EffectChains.IndexOf(SelectedEffectChain);
        if (index >= 0 && updated != null)
        {
          EffectChains[index] = updated;
          SelectedEffectChain = updated;
        }

        if (_undoRedoService != null && SelectedEffectChain != null)
        {
          var log = ServiceProvider.TryGetErrorLoggingService();
          var action = new ToggleEffectEnabledUndoAction(
              EffectChains,
              _effectChainClient,
              SelectedProjectId,
              SelectedEffectChain.Id,
              effect.Id,
              previousEnabled,
              newEnabled,
              log,
              SyncSelectedEffectChainAfterUndo);
          _undoRedoService.RegisterAction(action);
        }
      }
      catch (OperationCanceledException)
      {
        return;
      }
      catch (Exception ex)
      {
        ErrorMessage = ErrorHandler.GetUserFriendlyMessage(ex);
        _errorService?.ShowError(ex, ResourceHelper.GetString("EffectsMixer.EffectChainSaveFailed", "Failed to save effect chain"));
        _logService?.LogError(ex, "PersistEffectEnabledToggle");
      }
      finally
      {
        IsLoading = false;
      }
    }

    partial void OnIsLoadingChanged(bool value)
    {
      LoadMixerStateCommand.NotifyCanExecuteChanged();
      SaveMixerStateCommand.NotifyCanExecuteChanged();
      ResetMixerStateCommand.NotifyCanExecuteChanged();
      LoadMixerPresetsCommand.NotifyCanExecuteChanged();
      CreateMixerPresetCommand.NotifyCanExecuteChanged();
      ApplyMixerPresetCommand.NotifyCanExecuteChanged();
      ApplyEffectChainCommand.NotifyCanExecuteChanged();
      PreviewEffectChainCommand.NotifyCanExecuteChanged();
      RunStudioSoundCommand.NotifyCanExecuteChanged();
    }

    partial void OnIsStudioSoundRunningChanged(bool value)
    {
      RunStudioSoundCommand.NotifyCanExecuteChanged();
    }

    partial void OnMixerStateChanged(MixerState? value)
    {
      SaveMixerStateCommand.NotifyCanExecuteChanged();
    }

    partial void OnSelectedAudioIdChanged(string? value)
    {
      LoadMetersCommand.NotifyCanExecuteChanged();
      ToggleRealTimeUpdatesCommand.NotifyCanExecuteChanged();
      ApplyEffectChainCommand.NotifyCanExecuteChanged();
      PreviewEffectChainCommand.NotifyCanExecuteChanged();
      RunStudioSoundCommand.NotifyCanExecuteChanged();

      // Stop polling if audio ID changes
      if (IsRealTimeUpdatesEnabled)
      {
        StopPolling();
      }

      if (!string.IsNullOrWhiteSpace(value))
      {
        CancelSelectionLoad();
        var audioId = value;
        _selectionLoadCts = CancellationTokenSource.CreateLinkedTokenSource(_disposalCts!.Token);
        var ct = _selectionLoadCts.Token;
        _ = LoadAudioSelectionDataAsync(audioId, ct);
      }
    }

    partial void OnIsRealTimeUpdatesEnabledChanged(bool value)
    {
      if (value)
      {
        StartPolling();
      }
      else
      {
        StopPolling();
      }
    }

    private void ToggleRealTimeUpdates()
    {
      IsRealTimeUpdatesEnabled = !IsRealTimeUpdatesEnabled;
    }

    private void StartPolling()
    {
      if (_isPolling || string.IsNullOrWhiteSpace(SelectedAudioId))
        return;

      _isPolling = true;
      _pollingCts = new CancellationTokenSource();
      _ = PollMetersAsync(_pollingCts.Token);
    }

    private void StopPolling()
    {
      _isPolling = false;
      _pollingCts?.Cancel();
      _pollingCts?.Dispose();
      _pollingCts = null;
    }

    private void CancelSelectionLoad()
    {
      _selectionLoadCts?.Cancel();
      _selectionLoadCts?.Dispose();
      _selectionLoadCts = null;
    }

    /// <summary>
    /// Lifecycle fire-and-forget: loads project data when selection changes.
    /// Uses _selectionLoadCts; staleness guard before each apply.
    /// </summary>
    private async Task LoadProjectSelectionDataAsync(string projectId, CancellationToken ct)
    {
      try
      {
        await LoadEffectChainsForProjectAsync(projectId, ct);
        if (SelectedProjectId != projectId || ct.IsCancellationRequested) return;
        await LoadMixerStateForProjectAsync(projectId, ct);
        if (SelectedProjectId != projectId || ct.IsCancellationRequested) return;
        await LoadMixerPresetsForProjectAsync(projectId, ct);
      }
      // ALLOWED: empty catch - OperationCanceledException expected when selection changes or panel deactivates
      catch (OperationCanceledException) { }
      catch (Exception ex)
      {
        _logService?.LogError(ex, "LoadProjectSelectionData");
        _toastNotificationService?.ShowToast(ToastType.Error, "Effects/Mixer sync failed", ex.Message);
      }
    }

    /// <summary>
    /// Lifecycle fire-and-forget: loads meters when audio selection changes.
    /// Uses _selectionLoadCts; staleness guard before apply.
    /// </summary>
    private async Task LoadAudioSelectionDataAsync(string audioId, CancellationToken ct)
    {
      try
      {
        await LoadMetersForAudioAsync(audioId, ct);
      }
      // ALLOWED: empty catch - OperationCanceledException expected when selection changes or panel deactivates
      catch (OperationCanceledException) { }
      catch (Exception ex)
      {
        _logService?.LogError(ex, "LoadAudioSelectionData");
      }
    }

    async Task IPanelLifecycle.OnActivatedAsync(CancellationToken ct)
    {
      // Pass 02 C4: Sync SelectedProjectId from context when panel activates
      if (_contextManager != null)
      {
        var activeId = _contextManager.ActiveProjectId;
        if (SelectedProjectId != activeId)
          SelectedProjectId = activeId;
      }

      // Pass 02 C2: Subscribe to ProjectChangedEvent for project switch propagation
      if (_eventAggregator != null && _projectChangedToken == null)
      {
        _projectChangedToken = _eventAggregator.Subscribe<ProjectChangedEvent>(OnProjectChanged);
      }

      await Task.CompletedTask;
    }

    private void OnProjectChanged(ProjectChangedEvent e)
    {
      if (_disposed) return;
      SelectedProjectId = e.ProjectId;
    }

    async Task IPanelLifecycle.RefreshAsync(CancellationToken ct)
    {
      if (!string.IsNullOrWhiteSpace(SelectedProjectId))
      {
        await LoadEffectChainsForProjectAsync(SelectedProjectId, ct);
        if (ct.IsCancellationRequested) return;
        await LoadMixerStateForProjectAsync(SelectedProjectId, ct);
        if (ct.IsCancellationRequested) return;
        await LoadMixerPresetsForProjectAsync(SelectedProjectId, ct);
      }
      if (!string.IsNullOrWhiteSpace(SelectedAudioId))
      {
        await LoadMetersForAudioAsync(SelectedAudioId, ct);
      }
    }

    Task IPanelLifecycle.OnDeactivatedAsync(CancellationToken ct)
    {
      StopPolling();
      CancelSelectionLoad();
      return Task.CompletedTask;
    }

    public void Dispose()
    {
      if (_disposed) return;
      _disposed = true;
      if (_projectChangedToken != null)
      {
        _eventAggregator?.Unsubscribe(_projectChangedToken);
        _projectChangedToken.Dispose();
        _projectChangedToken = null;
      }
      StopPolling();
      CancelSelectionLoad();
      _disposalCts?.Cancel();
      _disposalCts?.Dispose();
      _disposalCts = null;
    }

    private async Task PollMetersAsync(CancellationToken cancellationToken)
    {
      if (_meterClient != null)
      {
        await RunRealtimeMetersAsync(cancellationToken);
        return;
      }

      while (!cancellationToken.IsCancellationRequested && _isPolling)
      {
        try
        {
          await LoadMetersAsync(cancellationToken);
          await Task.Delay(500, cancellationToken); // Cap at 2 req/sec to avoid rate-limit
        }
        catch (TaskCanceledException)
        {
          // Polling was cancelled, exit
          break;
        }
        catch (Exception ex)
        {
          // Log error but continue polling
          System.Diagnostics.Debug.WriteLine($"Error polling meters: {ex.Message}");
          await Task.Delay(2000, cancellationToken); // Back off on error
        }
      }
    }

    /// <summary>
    /// WebSocket-first metering: one-shot HTTP seed then <see cref="IMeterClient"/> until cancelled.
    /// </summary>
    private async Task RunRealtimeMetersAsync(CancellationToken cancellationToken)
    {
      try
      {
        if (!string.IsNullOrWhiteSpace(SelectedAudioId))
          await LoadMetersForAudioAsync(SelectedAudioId, cancellationToken);

        await _meterClient!.ConnectAsync(cancellationToken);
        _meterClient.LevelsUpdated += OnRealtimeMeterLevelsUpdated;
        try
        {
          await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        }
        // ALLOWED: empty catch - cancellation ends infinite wait for WS metering loop (user disables realtime or changes selection)
        catch (OperationCanceledException)
        {
          // Expected when realtime is disabled or audio selection changes
        }
        finally
        {
          _meterClient.LevelsUpdated -= OnRealtimeMeterLevelsUpdated;
        }
      }
      // ALLOWED: empty catch - outer cancellation when PollMetersAsync CTS is cancelled during teardown
      catch (OperationCanceledException)
      {
        // Selection/teardown
      }
      catch (Exception ex)
      {
        _logService?.LogError(ex, "RunRealtimeMeters");
        System.Diagnostics.Debug.WriteLine($"Realtime meters error: {ex.Message}");
      }
    }

    private void OnRealtimeMeterLevelsUpdated(object? sender, MeterLevelUpdate e)
    {
      if (_disposed || !_isPolling)
        return;

      if (!string.IsNullOrEmpty(e.ProjectId) &&
          !string.Equals(e.ProjectId, SelectedProjectId, StringComparison.Ordinal))
      {
        return;
      }

      MixerChannel? target = null;
      if (!string.IsNullOrEmpty(e.ChannelId))
      {
        target = Channels.FirstOrDefault(c => c.Id == e.ChannelId);
      }

      if (target == null &&
          !string.IsNullOrEmpty(SelectedAudioId) &&
          string.Equals(e.ChannelId, SelectedAudioId, StringComparison.Ordinal) &&
          Channels.Count > 0)
      {
        target = Channels[0];
      }

      if (target == null && Channels.Count == 1)
      {
        target = Channels[0];
      }

      if (target == null)
      {
        return;
      }

      var peak = e.PeakLevelLinear;
      var rms = e.RmsLevelLinear;
      if (Math.Abs(target.PeakLevel - peak) > 0.001)
      {
        target.PeakLevel = peak;
      }
      if (Math.Abs(target.RmsLevel - rms) > 0.001)
      {
        target.RmsLevel = rms;
      }
    }

    private async Task LoadMetersAsync(CancellationToken cancellationToken)
    {
      if (string.IsNullOrWhiteSpace(SelectedAudioId))
        return;
      await LoadMetersForAudioAsync(SelectedAudioId, cancellationToken);
    }

    private async Task LoadMetersForAudioAsync(string audioId, CancellationToken cancellationToken)
    {
      try
      {
        // Don't set IsLoading for meter updates to avoid UI flicker during polling
        // Only update meter values, not the entire state
        var meters = await _effectsMeterClient.GetAudioMetersAsync(audioId, cancellationToken);

        // Staleness guard: selection may have changed during await
        if (SelectedAudioId != audioId || cancellationToken.IsCancellationRequested)
          return;

        // Update channels with meter data
        if (meters.Channels?.Count > 0)
        {
          // Ensure we have enough channels (only add if needed, don't recreate)
          while (Channels.Count < meters.Channels.Count)
          {
            Channels.Add(new MixerChannel
            {
              Id = Guid.NewGuid().ToString(),
              ChannelNumber = Channels.Count + 1,
              Name = ResourceHelper.FormatString("EffectsMixer.ChannelName", Channels.Count + 1),
              PeakLevel = 0.0,
              RmsLevel = 0.0,
              Volume = 1.0,
              Pan = 0.0,
              IsMuted = false,
              IsSoloed = false,
              MainDestination = RoutingDestination.Master
            });
          }

          // Update each channel with meter data (only update if changed to minimize notifications)
          for (int i = 0; i < meters.Channels.Count && i < Channels.Count; i++)
          {
            var channel = Channels[i];
            var meterData = meters.Channels[i];

            // Only update if values changed (reduces property change notifications)
            if (Math.Abs(channel.PeakLevel - meterData.Peak) > 0.001)
              channel.PeakLevel = meterData.Peak;
            if (Math.Abs(channel.RmsLevel - meterData.Rms) > 0.001)
              channel.RmsLevel = meterData.Rms;
          }
        }
        else
        {
          // Use overall meters for first channel
          if (Channels.Count > 0)
          {
            if (Math.Abs(Channels[0].PeakLevel - meters.Peak) > 0.001)
              Channels[0].PeakLevel = meters.Peak;
            if (Math.Abs(Channels[0].RmsLevel - meters.Rms) > 0.001)
              Channels[0].RmsLevel = meters.Rms;
          }
        }

        // Update master bus meters if available
        if (Master != null && meters.Master != null)
        {
          if (Math.Abs(Master.PeakLevel - meters.Master.Peak) > 0.001)
            Master.PeakLevel = meters.Master.Peak;
          if (Math.Abs(Master.RmsLevel - meters.Master.Rms) > 0.001)
            Master.RmsLevel = meters.Master.Rms;
        }
      }
      catch (OperationCanceledException)
      {
        // User cancelled - expected
        return;
      }
      catch (Exception ex)
      {
        // Don't show error message for meter polling failures (too noisy)
        // Only log for debugging
        _logService?.LogError(ex, "LoadMeters");
        System.Diagnostics.Debug.WriteLine($"Error loading meters: {ex.Message}");
      }
    }

    // Effect chain management methods
    private async Task LoadEffectChainsAsync(CancellationToken cancellationToken)
    {
      if (string.IsNullOrWhiteSpace(SelectedProjectId))
        return;
      await LoadEffectChainsForProjectAsync(SelectedProjectId, cancellationToken);
    }

    private async Task LoadEffectChainsForProjectAsync(string projectId, CancellationToken cancellationToken)
    {
      try
      {
        IsLoading = true;
        ErrorMessage = null;

        var chains = await _effectChainClient.GetEffectChainsAsync(projectId, cancellationToken);

        // Staleness guard: selection may have changed during await
        if (SelectedProjectId != projectId || cancellationToken.IsCancellationRequested)
          return;

        EffectChains.Clear();
        foreach (var chain in chains)
        {
          EffectChains.Add(chain);
        }

        if (EffectChains.Count > 0)
        {
          _toastNotificationService?.ShowSuccess(
              ResourceHelper.GetString("EffectsMixer.EffectChainsLoaded", "Effect Chains Loaded"),
              ResourceHelper.FormatString("EffectsMixer.EffectChainsLoadedCount", EffectChains.Count));
        }
      }
      catch (OperationCanceledException)
      {
        // User cancelled - expected
        return;
      }
      catch (Exception ex)
      {
        ErrorMessage = ErrorHandler.GetUserFriendlyMessage(ex);
        _errorService?.ShowError(ex, ResourceHelper.GetString("EffectsMixer.EffectChainsLoadFailed", "Failed to load effect chains"));
        _logService?.LogError(ex, "LoadEffectChains");
        _toastNotificationService?.ShowError(
            ResourceHelper.GetString("EffectsMixer.EffectChainsLoadFailed", "Failed to Load Effect Chains"),
            ErrorHandler.GetUserFriendlyMessage(ex));
      }
      finally
      {
        IsLoading = false;
      }
    }

    private async Task LoadEffectPresetsAsync(CancellationToken cancellationToken)
    {
      IsLoading = true;
      ErrorMessage = null;

      try
      {
        var presets = await _effectChainClient.GetEffectPresetsAsync(null, cancellationToken);

        EffectPresets.Clear();
        foreach (var preset in presets)
        {
          EffectPresets.Add(preset);
        }

        if (EffectPresets.Count > 0)
        {
          _toastNotificationService?.ShowSuccess(
              ResourceHelper.GetString("EffectsMixer.EffectPresetsLoaded", "Effect Presets Loaded"),
              ResourceHelper.FormatString("EffectsMixer.EffectPresetsLoadedCount", EffectPresets.Count));
        }
      }
      catch (OperationCanceledException)
      {
        // User cancelled - expected
        return;
      }
      catch (Exception ex)
      {
        ErrorMessage = ErrorHandler.GetUserFriendlyMessage(ex);
        _errorService?.ShowError(ex, "Failed to load effect presets");
        _logService?.LogError(ex, "LoadEffectPresets");
        _toastNotificationService?.ShowError("Failed to Load Effect Presets", ErrorHandler.GetUserFriendlyMessage(ex));
      }
      finally
      {
        IsLoading = false;
      }
    }

    private async Task CreateEffectChainAsync(string? name, CancellationToken cancellationToken)
    {
      if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(SelectedProjectId))
        return;

      IsLoading = true;
      ErrorMessage = null;

      try
      {
        var chain = new EffectChain
        {
          Id = Guid.NewGuid().ToString(),
          Name = name,
          ProjectId = SelectedProjectId,
          Effects = new List<Effect>(),
          Created = DateTime.UtcNow,
          Modified = DateTime.UtcNow
        };

        var createdChain = await _effectChainClient.CreateEffectChainAsync(SelectedProjectId, chain, cancellationToken);
        EffectChains.Insert(0, createdChain); // Add to beginning
        SelectedEffectChain = createdChain;

        // Register undo action
        if (_undoRedoService != null)
        {
          var action = new CreateEffectChainAction(
              EffectChains,
              _effectChainClient,
              createdChain,
              onUndo: (c) =>
              {
                if (SelectedEffectChain?.Id == c.Id)
                {
                  SelectedEffectChain = EffectChains.FirstOrDefault();
                }
              },
              onRedo: (c) => SelectedEffectChain = c);
          _undoRedoService.RegisterAction(action);
        }

        _toastNotificationService?.ShowSuccess(
            ResourceHelper.GetString("EffectsMixer.EffectChainCreated", "Effect Chain Created"),
            ResourceHelper.FormatString("EffectsMixer.EffectChainCreatedDetail", createdChain.Name));
      }
      catch (OperationCanceledException)
      {
        // User cancelled - expected
        return;
      }
      catch (Exception ex)
      {
        ErrorMessage = ErrorHandler.GetUserFriendlyMessage(ex);
        _errorService?.ShowError(ex, ResourceHelper.GetString("EffectsMixer.EffectChainCreateFailed", "Failed to create effect chain"));
        _logService?.LogError(ex, "CreateEffectChain");
        _toastNotificationService?.ShowError(
            ResourceHelper.GetString("EffectsMixer.EffectChainCreateFailed", "Failed to Create Effect Chain"),
            ErrorHandler.GetUserFriendlyMessage(ex));
      }
      finally
      {
        IsLoading = false;
      }
    }

    private async Task DeleteEffectChainAsync(string? chainId, CancellationToken cancellationToken)
    {
      if (string.IsNullOrWhiteSpace(chainId) || string.IsNullOrWhiteSpace(SelectedProjectId))
        return;

      IsLoading = true;
      ErrorMessage = null;

      try
      {
        var success = await _effectChainClient.DeleteEffectChainAsync(SelectedProjectId, chainId, cancellationToken);
        if (success)
        {
          var chain = EffectChains.FirstOrDefault(c => c.Id == chainId);
          if (chain != null)
          {
            var originalIndex = EffectChains.IndexOf(chain);
            EffectChains.Remove(chain);
            var deletedChain = chain;
            if (SelectedEffectChain?.Id == chainId)
            {
              SelectedEffectChain = null;
            }

            // Register undo action
            if (_undoRedoService != null)
            {
                  var action = new DeleteEffectChainAction(
                  EffectChains,
                  _effectChainClient,
                  deletedChain,
                  originalIndex,
                  onUndo: (c) => SelectedEffectChain = c,
                  onRedo: (c) =>
                  {
                    if (SelectedEffectChain?.Id == c.Id)
                    {
                      SelectedEffectChain = null;
                    }
                  });
              _undoRedoService.RegisterAction(action);
            }

            var chainName = deletedChain.Name ?? ResourceHelper.GetString("EffectsMixer.UnknownChain", "Unknown Chain");
            _toastNotificationService?.ShowSuccess(
                ResourceHelper.GetString("EffectsMixer.EffectChainDeleted", "Effect Chain Deleted"),
                ResourceHelper.FormatString("EffectsMixer.EffectChainDeletedDetail", chainName));
          }
        }
      }
      catch (OperationCanceledException)
      {
        // User cancelled - expected
        return;
      }
      catch (Exception ex)
      {
        ErrorMessage = ErrorHandler.GetUserFriendlyMessage(ex);
        _errorService?.ShowError(ex, ResourceHelper.GetString("EffectsMixer.EffectChainDeleteFailed", "Failed to delete effect chain"));
        _logService?.LogError(ex, "DeleteEffectChain");
        _toastNotificationService?.ShowError(
            ResourceHelper.GetString("EffectsMixer.EffectChainDeleteFailed", "Failed to Delete Effect Chain"),
            ErrorHandler.GetUserFriendlyMessage(ex));
      }
      finally
      {
        IsLoading = false;
      }
    }

    /// <summary>GAP-039: Single path for apply vs preview; bypass is explicit via <see cref="IsEffectChainBypassed"/>.</summary>
    private async Task RunEffectChainProcessAsync(string? chainId, bool isPreview, CancellationToken cancellationToken)
    {
      if (string.IsNullOrWhiteSpace(chainId) || string.IsNullOrWhiteSpace(SelectedProjectId) || string.IsNullOrWhiteSpace(SelectedAudioId))
        return;

      IsLoading = true;
      ErrorMessage = null;

      try
      {
        var response = await _effectChainClient.ProcessAudioWithChainAsync(
            SelectedProjectId,
            chainId,
            SelectedAudioId,
            outputFilename: null,
            bypassChain: IsEffectChainBypassed,
            preview: isPreview,
            cancellationToken);
        if (!response.Success)
        {
          ErrorMessage = response.ErrorMessage ?? ResourceHelper.GetString("EffectsMixer.EffectChainApplyFailed", "Failed to apply effect chain");
          _toastNotificationService?.ShowError(
              ResourceHelper.GetString("EffectsMixer.EffectChainApplyFailed", "Failed to Apply Effect Chain"),
              response.ErrorMessage ?? ResourceHelper.GetString("EffectsMixer.EffectChainApplyFailed", "Failed to apply effect chain"));
        }
        else
        {
          var chain = EffectChains.FirstOrDefault(c => c.Id == chainId);
          var chainName = chain?.Name ?? ResourceHelper.GetString("EffectsMixer.UnknownChain", "Unknown Chain");
          if (isPreview)
          {
            _toastNotificationService?.ShowSuccess(
                ResourceHelper.GetString("EffectsMixer.EffectChainPreviewed", "Effect Chain Preview"),
                ResourceHelper.FormatString("EffectsMixer.EffectChainPreviewedDetail", chainName));
          }
          else
          {
            _toastNotificationService?.ShowSuccess(
                ResourceHelper.GetString("EffectsMixer.EffectChainApplied", "Effect Chain Applied"),
                ResourceHelper.FormatString("EffectsMixer.EffectChainAppliedDetail", chainName));
          }
        }
      }
      catch (OperationCanceledException)
      {
        // User cancelled - expected
        return;
      }
      catch (Exception ex)
      {
        ErrorMessage = ErrorHandler.GetUserFriendlyMessage(ex);
        _errorService?.ShowError(ex, ResourceHelper.GetString("EffectsMixer.EffectChainApplyFailed", "Failed to apply effect chain"));
        _logService?.LogError(ex, isPreview ? "PreviewEffectChain" : "ApplyEffectChain");
        _toastNotificationService?.ShowError(
            ResourceHelper.GetString("EffectsMixer.EffectChainApplyFailed", "Failed to Apply Effect Chain"),
            ErrorHandler.GetUserFriendlyMessage(ex));
      }
      finally
      {
        IsLoading = false;
      }
    }

    /// <summary>GAP-048: One-click denoise → compressor → normalize via canonical effect-chain APIs; transient chain is deleted after processing.</summary>
    private async Task RunStudioSoundAsync(CancellationToken cancellationToken)
    {
      var projectId = SelectedProjectId;
      var audioId = SelectedAudioId;
      if (string.IsNullOrWhiteSpace(projectId) || string.IsNullOrWhiteSpace(audioId))
        return;

      IsLoading = true;
      IsStudioSoundRunning = true;
      StudioSoundOutputAudioId = null;
      ErrorMessage = null;
      string? transientChainId = null;

      try
      {
        var effects = new List<Effect>(StudioSoundEffects.Count);
        foreach (var template in StudioSoundEffects)
        {
          var effectType = template.Type ?? string.Empty;
          effects.Add(new Effect
          {
            Id = template.Id,
            Type = effectType,
            Name = template.Name,
            Enabled = template.Enabled,
            Order = template.Order,
            Parameters = GetDefaultParametersForEffectType(effectType)
          });
        }

        var chain = new EffectChain
        {
          Id = Guid.NewGuid().ToString(),
          Name = "Studio Sound",
          ProjectId = projectId,
          Effects = effects,
          Created = DateTime.UtcNow,
          Modified = DateTime.UtcNow
        };

        var created = await _effectChainClient.CreateEffectChainAsync(projectId, chain, cancellationToken);
        transientChainId = created.Id;

        var response = await _effectChainClient.ProcessAudioWithChainAsync(
            projectId,
            created.Id,
            audioId,
            outputFilename: null,
            bypassChain: false,
            preview: false,
            cancellationToken);

        if (!response.Success)
        {
          StudioSoundOutputAudioId = null;
          ErrorMessage = response.ErrorMessage ?? ResourceHelper.GetString("EffectsMixer.StudioSoundFailed", "Studio Sound processing failed");
          _toastNotificationService?.ShowError(
              ResourceHelper.GetString("EffectsMixer.StudioSoundFailed", "Studio Sound Failed"),
              response.ErrorMessage ?? ErrorMessage ?? ResourceHelper.GetString("EffectsMixer.StudioSoundFailed", "Studio Sound processing failed"));
        }
        else
        {
          StudioSoundOutputAudioId = response.OutputAudioId;
          _toastNotificationService?.ShowSuccess(
              ResourceHelper.GetString("EffectsMixer.StudioSoundApplied", "Studio Sound Applied"),
              ResourceHelper.GetString("EffectsMixer.StudioSoundAppliedDetail", "A new processed audio clip was created."));
        }
      }
      catch (OperationCanceledException)
      {
        return;
      }
      catch (Exception ex)
      {
        ErrorMessage = ErrorHandler.GetUserFriendlyMessage(ex);
        StudioSoundOutputAudioId = null;
        _errorService?.ShowError(ex, ResourceHelper.GetString("EffectsMixer.StudioSoundFailed", "Studio Sound failed"));
        _logService?.LogError(ex, "RunStudioSound");
        _toastNotificationService?.ShowError(
            ResourceHelper.GetString("EffectsMixer.StudioSoundFailed", "Studio Sound Failed"),
            ErrorHandler.GetUserFriendlyMessage(ex));
      }
      finally
      {
        if (!string.IsNullOrWhiteSpace(transientChainId) && !string.IsNullOrWhiteSpace(projectId))
        {
          try
          {
            await _effectChainClient.DeleteEffectChainAsync(projectId, transientChainId, CancellationToken.None);
          }
          catch (Exception ex)
          {
            _logService?.LogError(ex, "StudioSoundDeleteTransientChain");
          }
        }

        IsStudioSoundRunning = false;
        IsLoading = false;
        RunStudioSoundCommand.NotifyCanExecuteChanged();
      }
    }

    // Effect chain editing methods
    private Task AddEffectToChainAsync(string? effectType, CancellationToken cancellationToken)
    {
      if (string.IsNullOrWhiteSpace(effectType) || SelectedEffectChain == null)
        return Task.CompletedTask;

      cancellationToken.ThrowIfCancellationRequested();

      try
      {
        var effect = new Effect
        {
          Id = Guid.NewGuid().ToString(),
          Type = effectType,
          Name = GetEffectDisplayName(effectType),
          Enabled = true,
          Order = SelectedEffectChain.Effects.Count,
          Parameters = GetDefaultParametersForEffectType(effectType)
        };

        SelectedEffectChain.Effects.Add(effect);
        SelectedEffectChain.Modified = DateTime.UtcNow;

        // Register undo action
        if (_undoRedoService != null && SelectedEffectChain != null)
        {
          var action = new AddEffectAction(
              EffectChains,
              SelectedEffectChain.Id,
              effect,
              onUndo: (e) =>
              {
                if (SelectedEffect?.Id == e.Id)
                {
                  SelectedEffect = null;
                }
              },
              onRedo: (e) => SelectedEffect = e);
          _undoRedoService.RegisterAction(action);
        }

        // Notify that the chain has changed
        OnPropertyChanged(nameof(SelectedEffectChain));
        SelectedEffect = effect; // Auto-select newly added effect
        _toastNotificationService?.ShowSuccess(
            ResourceHelper.GetString("EffectsMixer.EffectAdded", "Effect Added"),
            ResourceHelper.FormatString("EffectsMixer.EffectAddedDetail", GetEffectDisplayName(effectType)));
      }
      catch (OperationCanceledException)
      {
        // User cancelled - expected
        return Task.CompletedTask;
      }
      catch (Exception ex)
      {
        ErrorMessage = ErrorHandler.GetUserFriendlyMessage(ex);
        _errorService?.ShowError(ex, ResourceHelper.GetString("EffectsMixer.EffectAddFailed", "Failed to add effect"));
        _logService?.LogError(ex, "AddEffectToChain");
        _toastNotificationService?.ShowError(
            ResourceHelper.GetString("EffectsMixer.EffectAddFailed", "Failed to Add Effect"),
            ErrorHandler.GetUserFriendlyMessage(ex));
      }

      return Task.CompletedTask;
    }

    private Task RemoveEffectFromChainAsync(string? effectId, CancellationToken cancellationToken)
    {
      var chain = SelectedEffectChain;
      if (string.IsNullOrWhiteSpace(effectId) || chain == null)
        return Task.CompletedTask;

      cancellationToken.ThrowIfCancellationRequested();

      try
      {
        var effect = chain.Effects.FirstOrDefault(e => e.Id == effectId);
        if (effect != null)
        {
          var originalOrder = effect.Order;
          var removedEffect = effect;
          chain.Effects.Remove(effect);
          if (SelectedEffect?.Id == effectId)
          {
            SelectedEffect = null;
          }
          // Reorder remaining effects
          for (int i = 0; i < chain.Effects.Count; i++)
          {
            chain.Effects[i].Order = i;
          }

          // Register undo action
          if (_undoRedoService != null)
          {
            var action = new RemoveEffectAction(
                EffectChains,
                chain.Id,
                removedEffect,
                originalOrder,
                onUndo: (e) => SelectedEffect = e,
                onRedo: (e) =>
                {
                  if (SelectedEffect?.Id == e.Id)
                  {
                    SelectedEffect = null;
                  }
                });
            _undoRedoService.RegisterAction(action);
          }

          chain.Modified = DateTime.UtcNow;
          OnPropertyChanged(nameof(SelectedEffectChain));
          OnPropertyChanged($"{nameof(SelectedEffectChain)}.{nameof(EffectChain.Effects)}");
          _toastNotificationService?.ShowSuccess(
              ResourceHelper.GetString("EffectsMixer.EffectRemoved", "Effect Removed"),
              ResourceHelper.GetString("EffectsMixer.EffectRemovedDetail", "Effect removed from chain successfully"));
        }
      }
      catch (OperationCanceledException)
      {
        // User cancelled - expected
        return Task.CompletedTask;
      }
      catch (Exception ex)
      {
        ErrorMessage = ErrorHandler.GetUserFriendlyMessage(ex);
        _errorService?.ShowError(ex, ResourceHelper.GetString("EffectsMixer.EffectRemoveFailed", "Failed to remove effect"));
        _logService?.LogError(ex, "RemoveEffectFromChain");
        _toastNotificationService?.ShowError(
            ResourceHelper.GetString("EffectsMixer.EffectRemoveFailed", "Failed to Remove Effect"),
            ErrorHandler.GetUserFriendlyMessage(ex));
      }

      return Task.CompletedTask;
    }

    private Task MoveEffectUpAsync(string? effectId, CancellationToken cancellationToken)
    {
      if (string.IsNullOrWhiteSpace(effectId) || SelectedEffectChain == null)
        return Task.CompletedTask;

      cancellationToken.ThrowIfCancellationRequested();

      try
      {
        var effect = SelectedEffectChain.Effects.FirstOrDefault(e => e.Id == effectId);
        if (effect?.Order > 0)
        {
          var previousEffect = SelectedEffectChain.Effects.FirstOrDefault(e => e.Order == effect.Order - 1);
          if (previousEffect != null)
          {
            var oldOrder = effect.Order;
            var newOrder = previousEffect.Order;
            effect.Order--;
            previousEffect.Order++;
            SelectedEffectChain.Modified = DateTime.UtcNow;
            // Sort effects by order in place
            SelectedEffectChain.Effects.Sort((a, b) => a.Order.CompareTo(b.Order));

            // Register undo action
            if (_undoRedoService != null && SelectedEffectChain != null)
            {
              var action = new MoveEffectAction(
                  EffectChains,
                  SelectedEffectChain.Id,
                  effect.Id,
                  oldOrder,
                  newOrder,
                  isMovingUp: true);
              _undoRedoService.RegisterAction(action);
            }

            OnPropertyChanged(nameof(SelectedEffectChain));
            OnPropertyChanged($"{nameof(SelectedEffectChain)}.{nameof(EffectChain.Effects)}");
          }
        }
      }
      catch (OperationCanceledException)
      {
        // User cancelled - expected
        return Task.CompletedTask;
      }
      catch (Exception ex)
      {
        ErrorMessage = ErrorHandler.GetUserFriendlyMessage(ex);
        _errorService?.ShowError(ex, ResourceHelper.GetString("EffectsMixer.MoveEffectFailed", "Failed to move effect"));
        _logService?.LogError(ex, "MoveEffectUp");
      }

      return Task.CompletedTask;
    }

    private Task MoveEffectDownAsync(string? effectId, CancellationToken cancellationToken)
    {
      if (string.IsNullOrWhiteSpace(effectId) || SelectedEffectChain == null)
        return Task.CompletedTask;

      cancellationToken.ThrowIfCancellationRequested();

      try
      {
        var effect = SelectedEffectChain.Effects.FirstOrDefault(e => e.Id == effectId);
        if (effect != null && effect.Order < SelectedEffectChain.Effects.Count - 1)
        {
          var nextEffect = SelectedEffectChain.Effects.FirstOrDefault(e => e.Order == effect.Order + 1);
          if (nextEffect != null)
          {
            var oldOrder = effect.Order;
            var newOrder = nextEffect.Order;
            effect.Order++;
            nextEffect.Order--;
            SelectedEffectChain.Modified = DateTime.UtcNow;
            // Sort effects by order in place
            SelectedEffectChain.Effects.Sort((a, b) => a.Order.CompareTo(b.Order));

            // Register undo action
            if (_undoRedoService != null && SelectedEffectChain != null)
            {
              var action = new MoveEffectAction(
                  EffectChains,
                  SelectedEffectChain.Id,
                  effect.Id,
                  oldOrder,
                  newOrder,
                  isMovingUp: false);
              _undoRedoService.RegisterAction(action);
            }

            OnPropertyChanged(nameof(SelectedEffectChain));
            OnPropertyChanged($"{nameof(SelectedEffectChain)}.{nameof(EffectChain.Effects)}");
          }
        }
      }
      catch (OperationCanceledException)
      {
        // User cancelled - expected
        return Task.CompletedTask;
      }
      catch (Exception ex)
      {
        ErrorMessage = ErrorHandler.GetUserFriendlyMessage(ex);
        _errorService?.ShowError(ex, "Failed to move effect");
        _logService?.LogError(ex, "MoveEffectDown");
      }

      return Task.CompletedTask;
    }

    private async Task SaveEffectChainAsync(CancellationToken cancellationToken)
    {
      if (SelectedEffectChain == null || string.IsNullOrWhiteSpace(SelectedProjectId))
        return;

      IsLoading = true;
      ErrorMessage = null;

      try
      {
        var projectId = SelectedProjectId;
        var chainId = SelectedEffectChain.Id;
        var beforeSnapshot = EffectChainUndoSnapshots.CloneEffectChain(SelectedEffectChain);

        SelectedEffectChain.Modified = DateTime.UtcNow;
        var updatedChain = await _effectChainClient.UpdateEffectChainAsync(projectId, chainId, SelectedEffectChain, cancellationToken);

        // Update in collection
        var index = EffectChains.IndexOf(SelectedEffectChain);
        if (index >= 0)
        {
          EffectChains[index] = updatedChain;
          SelectedEffectChain = updatedChain;
          if (_undoRedoService != null && updatedChain != null)
          {
            var log = ServiceProvider.TryGetErrorLoggingService();
            var afterSnapshot = EffectChainUndoSnapshots.CloneEffectChain(updatedChain);
            var undoAction = new UpdateEffectChainSnapshotUndoAction(
                EffectChains,
                _effectChainClient,
                projectId,
                chainId,
                beforeSnapshot,
                afterSnapshot,
                log,
                SyncSelectedEffectChainAfterUndo);
            _undoRedoService.RegisterAction(undoAction);
          }

          _toastNotificationService?.ShowSuccess(
              ResourceHelper.GetString("EffectsMixer.EffectChainSaved", "Effect Chain Saved"),
              ResourceHelper.FormatString("EffectsMixer.EffectChainSavedDetail", updatedChain?.Name ?? string.Empty));
        }
      }
      catch (OperationCanceledException)
      {
        // User cancelled - expected
        return;
      }
      catch (Exception ex)
      {
        ErrorMessage = ErrorHandler.GetUserFriendlyMessage(ex);
        _errorService?.ShowError(ex, ResourceHelper.GetString("EffectsMixer.EffectChainSaveFailed", "Failed to save effect chain"));
        _logService?.LogError(ex, "SaveEffectChain");
        _toastNotificationService?.ShowError(
            ResourceHelper.GetString("EffectsMixer.EffectChainSaveFailed", "Failed to Save Effect Chain"),
            ErrorHandler.GetUserFriendlyMessage(ex));
      }
      finally
      {
        IsLoading = false;
      }
    }

    /// <summary>
    /// Get default parameters for an effect type.
    /// </summary>
    private List<EffectParameter> GetDefaultParametersForEffectType(string effectType)
    {
      return effectType.ToLower() switch
      {
        "normalize" => new List<EffectParameter>
                {
                    new EffectParameter { Name = "Target LUFS", Value = -23.0, MinValue = -30.0, MaxValue = -6.0, Unit = "LUFS" }
                },
        "denoise" => new List<EffectParameter>
                {
                    new EffectParameter { Name = "Strength", Value = 0.5, MinValue = 0.0, MaxValue = 1.0, Unit = "" }
                },
        "eq" => new List<EffectParameter>
                {
                    new EffectParameter { Name = "Low Gain", Value = 0.0, MinValue = -12.0, MaxValue = 12.0, Unit = "dB" },
                    new EffectParameter { Name = "Mid Gain", Value = 0.0, MinValue = -12.0, MaxValue = 12.0, Unit = "dB" },
                    new EffectParameter { Name = "High Gain", Value = 0.0, MinValue = -12.0, MaxValue = 12.0, Unit = "dB" }
                },
        "compressor" => new List<EffectParameter>
                {
                    new EffectParameter { Name = "Threshold", Value = -12.0, MinValue = -40.0, MaxValue = 0.0, Unit = "dB" },
                    new EffectParameter { Name = "Ratio", Value = 4.0, MinValue = 1.0, MaxValue = 20.0, Unit = ":1" },
                    new EffectParameter { Name = "Attack", Value = 5.0, MinValue = 0.1, MaxValue = 100.0, Unit = "ms" },
                    new EffectParameter { Name = "Release", Value = 50.0, MinValue = 10.0, MaxValue = 500.0, Unit = "ms" }
                },
        "reverb" => new List<EffectParameter>
                {
                    new EffectParameter { Name = "Room Size", Value = 0.5, MinValue = 0.0, MaxValue = 1.0, Unit = "" },
                    new EffectParameter { Name = "Damping", Value = 0.5, MinValue = 0.0, MaxValue = 1.0, Unit = "" },
                    new EffectParameter { Name = "Wet Level", Value = 0.3, MinValue = 0.0, MaxValue = 1.0, Unit = "" }
                },
        "delay" => new List<EffectParameter>
                {
                    new EffectParameter { Name = "Delay Time", Value = 250.0, MinValue = 10.0, MaxValue = 2000.0, Unit = "ms" },
                    new EffectParameter { Name = "Feedback", Value = 0.3, MinValue = 0.0, MaxValue = 0.95, Unit = "" },
                    new EffectParameter { Name = "Mix", Value = 0.3, MinValue = 0.0, MaxValue = 1.0, Unit = "" }
                },
        "filter" => new List<EffectParameter>
                {
                    new EffectParameter { Name = "Cutoff", Value = 1000.0, MinValue = 20.0, MaxValue = 20000.0, Unit = "Hz" },
                    new EffectParameter { Name = "Resonance", Value = 0.7, MinValue = 0.0, MaxValue = 1.0, Unit = "" },
                    new EffectParameter { Name = "Type", Value = 0.0, MinValue = 0.0, MaxValue = 2.0, Unit = "" } // 0=lowpass, 1=highpass, 2=bandpass
                },
        "chorus" => new List<EffectParameter>
                {
                    new EffectParameter { Name = "Depth", Value = 0.5, MinValue = 0.0, MaxValue = 1.0, Unit = "" },
                    new EffectParameter { Name = "Rate", Value = 1.5, MinValue = 0.1, MaxValue = 10.0, Unit = "Hz" },
                    new EffectParameter { Name = "Feedback", Value = 0.3, MinValue = 0.0, MaxValue = 1.0, Unit = "" },
                    new EffectParameter { Name = "Mix", Value = 0.5, MinValue = 0.0, MaxValue = 1.0, Unit = "" }
                },
        "pitch_correction" => new List<EffectParameter>
                {
                    new EffectParameter { Name = "Key", Value = 0.0, MinValue = 0.0, MaxValue = 11.0, Unit = "" }, // 0=C, 1=C#, ..., 11=B
                    new EffectParameter { Name = "Scale", Value = 0.0, MinValue = 0.0, MaxValue = 2.0, Unit = "" }, // 0=major, 1=minor, 2=chromatic
                    new EffectParameter { Name = "Strength", Value = 0.8, MinValue = 0.0, MaxValue = 1.0, Unit = "" },
                    new EffectParameter { Name = "Speed", Value = 0.1, MinValue = 0.0, MaxValue = 1.0, Unit = "" }
                },
        "convolution_reverb" => new List<EffectParameter>
                {
                    new EffectParameter { Name = "IR Path", Value = 0.0, MinValue = 0.0, MaxValue = 0.0, Unit = "" }, // File path (string, handled separately)
                    new EffectParameter { Name = "Wet Level", Value = 0.5, MinValue = 0.0, MaxValue = 1.0, Unit = "" },
                    new EffectParameter { Name = "Pre Delay", Value = 0.0, MinValue = 0.0, MaxValue = 200.0, Unit = "ms" },
                    new EffectParameter { Name = "High Cut", Value = 20000.0, MinValue = 1000.0, MaxValue = 20000.0, Unit = "Hz" },
                    new EffectParameter { Name = "Low Cut", Value = 20.0, MinValue = 20.0, MaxValue = 500.0, Unit = "Hz" }
                },
        "formant_shifter" => new List<EffectParameter>
                {
                    new EffectParameter { Name = "Formant Shift", Value = 0.0, MinValue = -1.0, MaxValue = 1.0, Unit = "" },
                    new EffectParameter { Name = "Formant Scale", Value = 1.0, MinValue = 0.5, MaxValue = 2.0, Unit = "" },
                    new EffectParameter { Name = "Preserve Pitch", Value = 1.0, MinValue = 0.0, MaxValue = 1.0, Unit = "" }, // Boolean (1.0 = true, 0.0 = false)
                    new EffectParameter { Name = "Mix", Value = 1.0, MinValue = 0.0, MaxValue = 1.0, Unit = "" }
                },
        "distortion" => new List<EffectParameter>
                {
                    new EffectParameter { Name = "Drive", Value = 0.5, MinValue = 0.0, MaxValue = 1.0, Unit = "" },
                    new EffectParameter { Name = "Tone", Value = 0.5, MinValue = 0.0, MaxValue = 1.0, Unit = "" },
                    new EffectParameter { Name = "Level", Value = 0.7, MinValue = 0.0, MaxValue = 1.0, Unit = "" },
                    new EffectParameter { Name = "Mix", Value = 1.0, MinValue = 0.0, MaxValue = 1.0, Unit = "" }
                },
        "multi_band_processor" => new List<EffectParameter>
                {
                    new EffectParameter { Name = "Low Gain", Value = 0.0, MinValue = -24.0, MaxValue = 24.0, Unit = "dB" },
                    new EffectParameter { Name = "Mid Gain", Value = 0.0, MinValue = -24.0, MaxValue = 24.0, Unit = "dB" },
                    new EffectParameter { Name = "High Gain", Value = 0.0, MinValue = -24.0, MaxValue = 24.0, Unit = "dB" },
                    new EffectParameter { Name = "Low Freq", Value = 200.0, MinValue = 50.0, MaxValue = 1000.0, Unit = "Hz" },
                    new EffectParameter { Name = "High Freq", Value = 5000.0, MinValue = 2000.0, MaxValue = 15000.0, Unit = "Hz" }
                },
        "dynamic_eq" => new List<EffectParameter>
                {
                    new EffectParameter { Name = "Frequency", Value = 1000.0, MinValue = 20.0, MaxValue = 20000.0, Unit = "Hz" },
                    new EffectParameter { Name = "Threshold", Value = -12.0, MinValue = -60.0, MaxValue = 0.0, Unit = "dB" },
                    new EffectParameter { Name = "Ratio", Value = 4.0, MinValue = 1.0, MaxValue = 20.0, Unit = "" },
                    new EffectParameter { Name = "Attack", Value = 5.0, MinValue = 0.1, MaxValue = 100.0, Unit = "ms" },
                    new EffectParameter { Name = "Release", Value = 50.0, MinValue = 10.0, MaxValue = 500.0, Unit = "ms" },
                    new EffectParameter { Name = "Gain", Value = 0.0, MinValue = -12.0, MaxValue = 12.0, Unit = "dB" },
                    new EffectParameter { Name = "Q", Value = 1.0, MinValue = 0.1, MaxValue = 10.0, Unit = "" }
                },
        "spectral_processor" => new List<EffectParameter>
                {
                    new EffectParameter { Name = "Mode", Value = 0.0, MinValue = 0.0, MaxValue = 2.0, Unit = "" }, // 0=enhance, 1=suppress, 2=shift
                    new EffectParameter { Name = "Frequency", Value = 1000.0, MinValue = 20.0, MaxValue = 20000.0, Unit = "Hz" },
                    new EffectParameter { Name = "Bandwidth", Value = 500.0, MinValue = 50.0, MaxValue = 5000.0, Unit = "Hz" },
                    new EffectParameter { Name = "Strength", Value = 0.5, MinValue = 0.0, MaxValue = 1.0, Unit = "" },
                    new EffectParameter { Name = "Shift Amount", Value = 0.0, MinValue = -2000.0, MaxValue = 2000.0, Unit = "Hz" }
                },
        "granular_synthesizer" => new List<EffectParameter>
                {
                    new EffectParameter { Name = "Grain Size", Value = 50.0, MinValue = 10.0, MaxValue = 200.0, Unit = "ms" },
                    new EffectParameter { Name = "Grain Density", Value = 10.0, MinValue = 1.0, MaxValue = 50.0, Unit = "grains/s" },
                    new EffectParameter { Name = "Pitch", Value = 1.0, MinValue = 0.5, MaxValue = 2.0, Unit = "" },
                    new EffectParameter { Name = "Position", Value = 0.0, MinValue = 0.0, MaxValue = 1.0, Unit = "" },
                    new EffectParameter { Name = "Spread", Value = 0.0, MinValue = 0.0, MaxValue = 1.0, Unit = "" },
                    new EffectParameter { Name = "Mix", Value = 1.0, MinValue = 0.0, MaxValue = 1.0, Unit = "" }
                },
        "vocoder" => new List<EffectParameter>
                {
                    new EffectParameter { Name = "Carrier Type", Value = 0.0, MinValue = 0.0, MaxValue = 2.0, Unit = "" }, // 0=noise, 1=sawtooth, 2=square
                    new EffectParameter { Name = "Bandwidth", Value = 0.5, MinValue = 0.0, MaxValue = 1.0, Unit = "" },
                    new EffectParameter { Name = "Depth", Value = 0.8, MinValue = 0.0, MaxValue = 1.0, Unit = "" },
                    new EffectParameter { Name = "Formant Shift", Value = 0.0, MinValue = -1.0, MaxValue = 1.0, Unit = "" },
                    new EffectParameter { Name = "Mix", Value = 1.0, MinValue = 0.0, MaxValue = 1.0, Unit = "" }
                },
        _ => new List<EffectParameter>()
      };
    }

    /// <summary>
    /// Get display name for an effect type.
    /// </summary>
    private string GetEffectDisplayName(string effectType)
    {
      return effectType.ToLower() switch
      {
        "normalize" => "Normalize",
        "denoise" => "Denoise",
        "eq" => "Equalizer",
        "compressor" => "Compressor",
        "reverb" => "Reverb",
        "delay" => "Delay",
        "filter" => "Filter",
        "chorus" => "Chorus",
        "pitch_correction" => "Pitch Correction",
        "convolution_reverb" => "Convolution Reverb",
        "formant_shifter" => "Formant Shifter",
        "distortion" => "Distortion",
        "multi_band_processor" => "Multi-Band Processor",
        "dynamic_eq" => "Dynamic EQ",
        "spectral_processor" => "Spectral Processor",
        "granular_synthesizer" => "Granular Synthesizer",
        "vocoder" => "Vocoder",
        _ => effectType
      };
    }

    // Mixer state management methods
    private async Task LoadMixerStateAsync(CancellationToken cancellationToken)
    {
      if (string.IsNullOrWhiteSpace(SelectedProjectId))
        return;
      await LoadMixerStateForProjectAsync(SelectedProjectId, cancellationToken);
    }

    private async Task LoadMixerStateForProjectAsync(string projectId, CancellationToken cancellationToken)
    {
      IsLoading = true;
      ErrorMessage = null;

      try
      {
        var state = await _mixerStateClient.GetMixerStateAsync(projectId, cancellationToken);

        // Staleness guard: selection may have changed during await
        if (SelectedProjectId != projectId || cancellationToken.IsCancellationRequested)
          return;

        MixerState = state;
        Master = state.Master ?? new MixerMaster { Id = "master", Volume = 1.0, Pan = 0.0, IsMuted = false };

        // Update channels from state (convert VoiceStudio.Core.Models.MixerChannel to ViewModel.MixerChannel)
        Channels.Clear();
        foreach (var coreChannel in state.Channels)
        {
          var vmChannel = new MixerChannel
          {
            Id = coreChannel.Id,
            ChannelNumber = coreChannel.ChannelNumber,
            Name = coreChannel.Name,
            PeakLevel = coreChannel.PeakLevel,
            RmsLevel = coreChannel.RmsLevel,
            Volume = coreChannel.Volume,
            Pan = coreChannel.Pan,
            IsMuted = coreChannel.IsMuted,
            IsSoloed = coreChannel.IsSoloed,
            MainDestination = Enum.TryParse<RoutingDestination>(coreChannel.MainDestination, out var dest) ? dest : RoutingDestination.Master,
            SubGroupId = coreChannel.SubGroupId,
            SendLevels = new Dictionary<string, double>(coreChannel.SendLevels),
            SendEnabled = new Dictionary<string, bool>(coreChannel.SendEnabled)
          };
          Channels.Add(vmChannel);
        }

        // Update sends, returns, and sub-groups
        Sends.Clear();
        foreach (var send in state.Sends)
        {
          Sends.Add(send);
        }

        Returns.Clear();
        foreach (var ret in state.Returns)
        {
          Returns.Add(ret);
        }

        SubGroups.Clear();
        foreach (var subGroup in state.SubGroups)
        {
          SubGroups.Add(subGroup);
        }
      }
      catch (OperationCanceledException)
      {
        // User cancelled - expected
        return;
      }
      catch (Exception ex)
      {
        ErrorMessage = ErrorHandler.GetUserFriendlyMessage(ex);
        _errorService?.ShowError(ex, ResourceHelper.GetString("EffectsMixer.MixerStateLoadFailed", "Failed to load mixer state"));
        _logService?.LogError(ex, "LoadMixerState");
      }
      finally
      {
        IsLoading = false;
      }
    }

    private async Task SaveMixerStateAsync(CancellationToken cancellationToken)
    {
      if (MixerState == null || string.IsNullOrWhiteSpace(SelectedProjectId))
        return;

      IsLoading = true;
      ErrorMessage = null;

      try
      {
        // Convert ViewModel.MixerChannel to VoiceStudio.Core.Models.MixerChannel


        // Update state from current UI values
        MixerState.Channels = Channels.Select(vmCh => new VoiceStudio.Core.Models.MixerChannel
        {
          Id = vmCh.Id,
          ChannelNumber = vmCh.ChannelNumber,
          Name = vmCh.Name,
          PeakLevel = vmCh.PeakLevel,
          RmsLevel = vmCh.RmsLevel,
          Volume = vmCh.Volume,
          Pan = vmCh.Pan,
          IsMuted = vmCh.IsMuted,
          IsSoloed = vmCh.IsSoloed,
          MainDestination = vmCh.MainDestination.ToString(),
          SubGroupId = vmCh.SubGroupId,
          SendLevels = new Dictionary<string, double>(vmCh.SendLevels),
          SendEnabled = new Dictionary<string, bool>(vmCh.SendEnabled)
        }).ToList();
        MixerState.Master = Master;
        MixerState.Sends = Sends.ToList();
        MixerState.Returns = Returns.ToList();
        MixerState.SubGroups = SubGroups.ToList();

        var updatedState = await _mixerStateClient.UpdateMixerStateAsync(SelectedProjectId, MixerState, cancellationToken);
        MixerState = updatedState;

        // Update Master from returned state (no need to reload everything)
        if (updatedState.Master != null)
        {
          Master = updatedState.Master;
        }
      }
      catch (OperationCanceledException)
      {
        // User cancelled - expected
        return;
      }
      catch (Exception ex)
      {
        ErrorMessage = ErrorHandler.GetUserFriendlyMessage(ex);
        _errorService?.ShowError(ex, ResourceHelper.GetString("EffectsMixer.MixerStateSaveFailed", "Failed to save mixer state"));
        _logService?.LogError(ex, "SaveMixerState");
      }
      finally
      {
        IsLoading = false;
      }
    }

    private async Task ResetMixerStateAsync(CancellationToken cancellationToken)
    {
      if (string.IsNullOrWhiteSpace(SelectedProjectId))
        return;

      IsLoading = true;
      ErrorMessage = null;

      try
      {
        var state = await _mixerStateClient.ResetMixerStateAsync(SelectedProjectId, cancellationToken);
        MixerState = state;
        Master = state.Master ?? new MixerMaster { Id = "master", Volume = 1.0, Pan = 0.0, IsMuted = false };

        // Reload channels (convert VoiceStudio.Core.Models.MixerChannel to ViewModel.MixerChannel)
        Channels.Clear();
        foreach (var coreChannel in state.Channels)
        {
          var vmChannel = new MixerChannel
          {
            Id = coreChannel.Id,
            ChannelNumber = coreChannel.ChannelNumber,
            Name = coreChannel.Name,
            PeakLevel = coreChannel.PeakLevel,
            RmsLevel = coreChannel.RmsLevel,
            Volume = coreChannel.Volume,
            Pan = coreChannel.Pan,
            IsMuted = coreChannel.IsMuted,
            IsSoloed = coreChannel.IsSoloed,
            MainDestination = Enum.TryParse<RoutingDestination>(coreChannel.MainDestination, out var dest) ? dest : RoutingDestination.Master,
            SubGroupId = coreChannel.SubGroupId,
            SendLevels = new Dictionary<string, double>(coreChannel.SendLevels),
            SendEnabled = new Dictionary<string, bool>(coreChannel.SendEnabled)
          };
          Channels.Add(vmChannel);
        }
      }
      catch (OperationCanceledException)
      {
        // User cancelled - expected
        return;
      }
      catch (Exception ex)
      {
        ErrorMessage = ErrorHandler.GetUserFriendlyMessage(ex);
        _errorService?.ShowError(ex, ResourceHelper.GetString("EffectsMixer.MixerStateResetFailed", "Failed to reset mixer state"));
        _logService?.LogError(ex, "ResetMixerState");
      }
      finally
      {
        IsLoading = false;
      }
    }

    private async Task LoadMixerPresetsAsync(CancellationToken cancellationToken)
    {
      if (string.IsNullOrWhiteSpace(SelectedProjectId))
        return;
      await LoadMixerPresetsForProjectAsync(SelectedProjectId, cancellationToken);
    }

    private async Task LoadMixerPresetsForProjectAsync(string projectId, CancellationToken cancellationToken)
    {
      IsLoading = true;
      ErrorMessage = null;

      try
      {
        var presets = await _mixerStateClient.GetMixerPresetsAsync(projectId, cancellationToken);

        // Staleness guard: selection may have changed during await
        if (SelectedProjectId != projectId || cancellationToken.IsCancellationRequested)
          return;

        MixerPresets.Clear();
        foreach (var preset in presets)
        {
          MixerPresets.Add(preset);
        }
      }
      catch (OperationCanceledException)
      {
        // User cancelled - expected
        return;
      }
      catch (Exception ex)
      {
        ErrorMessage = ErrorHandler.GetUserFriendlyMessage(ex);
        _errorService?.ShowError(ex, ResourceHelper.GetString("EffectsMixer.MixerPresetsLoadFailed", "Failed to load mixer presets"));
        _logService?.LogError(ex, "LoadMixerPresets");
      }
      finally
      {
        IsLoading = false;
      }
    }

    private async Task CreateMixerPresetAsync(string? name, CancellationToken cancellationToken)
    {
      if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(SelectedProjectId) || MixerState == null)
        return;

      IsLoading = true;
      ErrorMessage = null;

      try
      {
        var preset = new MixerPreset
        {
          Id = Guid.NewGuid().ToString(),
          Name = name,
          ProjectId = SelectedProjectId,
          State = MixerState,
          Created = DateTime.UtcNow,
          Modified = DateTime.UtcNow
        };

        var createdPreset = await _mixerStateClient.CreateMixerPresetAsync(SelectedProjectId, preset, cancellationToken);
        MixerPresets.Add(createdPreset);
      }
      catch (OperationCanceledException)
      {
        // User cancelled - expected
        return;
      }
      catch (Exception ex)
      {
        ErrorMessage = ErrorHandler.GetUserFriendlyMessage(ex);
        _errorService?.ShowError(ex, ResourceHelper.GetString("EffectsMixer.MixerPresetCreateFailed", "Failed to create mixer preset"));
        _logService?.LogError(ex, "CreateMixerPreset");
      }
      finally
      {
        IsLoading = false;
      }
    }

    private async Task ApplyMixerPresetAsync(string? presetId, CancellationToken cancellationToken)
    {
      if (string.IsNullOrWhiteSpace(presetId) || string.IsNullOrWhiteSpace(SelectedProjectId))
        return;

      IsLoading = true;
      ErrorMessage = null;

      try
      {
        var state = await _mixerStateClient.ApplyMixerPresetAsync(SelectedProjectId, presetId, cancellationToken);
        MixerState = state;
        Master = state.Master ?? new MixerMaster { Id = "master", Volume = 1.0, Pan = 0.0, IsMuted = false };

        // Reload channels (convert VoiceStudio.Core.Models.MixerChannel to ViewModel.MixerChannel)
        Channels.Clear();
        foreach (var coreChannel in state.Channels)
        {
          var vmChannel = new MixerChannel
          {
            Id = coreChannel.Id,
            ChannelNumber = coreChannel.ChannelNumber,
            Name = coreChannel.Name,
            PeakLevel = coreChannel.PeakLevel,
            RmsLevel = coreChannel.RmsLevel,
            Volume = coreChannel.Volume,
            Pan = coreChannel.Pan,
            IsMuted = coreChannel.IsMuted,
            IsSoloed = coreChannel.IsSoloed,
            MainDestination = Enum.TryParse<RoutingDestination>(coreChannel.MainDestination, out var dest) ? dest : RoutingDestination.Master,
            SubGroupId = coreChannel.SubGroupId,
            SendLevels = new Dictionary<string, double>(coreChannel.SendLevels),
            SendEnabled = new Dictionary<string, bool>(coreChannel.SendEnabled)
          };
          Channels.Add(vmChannel);
        }
      }
      catch (OperationCanceledException)
      {
        // User cancelled - expected
        return;
      }
      catch (Exception ex)
      {
        ErrorMessage = ErrorHandler.GetUserFriendlyMessage(ex);
        _errorService?.ShowError(ex, ResourceHelper.GetString("EffectsMixer.MixerPresetApplyFailed", "Failed to apply mixer preset"));
        _logService?.LogError(ex, "ApplyMixerPreset");
      }
      finally
      {
        IsLoading = false;
      }
    }

    private async Task CreateSendAsync(CancellationToken cancellationToken)
    {
      if (string.IsNullOrWhiteSpace(SelectedProjectId))
        return;

      IsLoading = true;
      ErrorMessage = null;

      try
      {
        var send = new MixerSend
        {
          Id = Guid.NewGuid().ToString(),
          Name = ResourceHelper.FormatString("EffectsMixer.SendName", Sends.Count + 1),
          BusNumber = Sends.Count + 1,
          Volume = 1.0,
          IsEnabled = true
        };

        var created = await _mixerStateClient.CreateMixerSendAsync(SelectedProjectId, send, cancellationToken);
        Sends.Add(created);
        await SaveMixerStateAsync(cancellationToken); // Save the updated state
      }
      catch (OperationCanceledException)
      {
        // User cancelled - expected
        return;
      }
      catch (Exception ex)
      {
        ErrorMessage = ErrorHandler.GetUserFriendlyMessage(ex);
        _errorService?.ShowError(ex, ResourceHelper.GetString("EffectsMixer.SendCreateFailed", "Failed to create send"));
        _logService?.LogError(ex, "CreateSend");
      }
      finally
      {
        IsLoading = false;
      }
    }

    private async Task CreateReturnAsync(CancellationToken cancellationToken)
    {
      if (string.IsNullOrWhiteSpace(SelectedProjectId))
        return;

      IsLoading = true;
      ErrorMessage = null;

      try
      {
        var returnBus = new MixerReturn
        {
          Id = Guid.NewGuid().ToString(),
          Name = $"Return {Returns.Count + 1}",
          BusNumber = Returns.Count + 1,
          Volume = 1.0,
          Pan = 0.0,
          IsEnabled = true
        };

        var created = await _mixerStateClient.CreateMixerReturnAsync(SelectedProjectId, returnBus, cancellationToken);
        Returns.Add(created);
        await SaveMixerStateAsync(cancellationToken); // Save the updated state
      }
      catch (OperationCanceledException)
      {
        // User cancelled - expected
        return;
      }
      catch (Exception ex)
      {
        ErrorMessage = ErrorHandler.GetUserFriendlyMessage(ex);
        _errorService?.ShowError(ex, ResourceHelper.GetString("EffectsMixer.ReturnCreateFailed", "Failed to create return"));
        _logService?.LogError(ex, "CreateReturn");
      }
      finally
      {
        IsLoading = false;
      }
    }

    private async Task CreateSubGroupAsync(CancellationToken cancellationToken)
    {
      if (string.IsNullOrWhiteSpace(SelectedProjectId))
        return;

      IsLoading = true;
      ErrorMessage = null;

      try
      {
        var subGroup = new MixerSubGroup
        {
          Id = Guid.NewGuid().ToString(),
          Name = ResourceHelper.FormatString("EffectsMixer.SubGroupName", SubGroups.Count + 1),
          BusNumber = SubGroups.Count + 1,
          Volume = 1.0,
          Pan = 0.0,
          IsMuted = false,
          IsSoloed = false,
          ChannelIds = new List<string>()
        };

        var created = await _mixerStateClient.CreateMixerSubGroupAsync(SelectedProjectId, subGroup, cancellationToken);
        SubGroups.Add(created);
        await SaveMixerStateAsync(cancellationToken); // Save the updated state
      }
      catch (OperationCanceledException)
      {
        // User cancelled - expected
        return;
      }
      catch (Exception ex)
      {
        ErrorMessage = ErrorHandler.GetUserFriendlyMessage(ex);
        _errorService?.ShowError(ex, ResourceHelper.GetString("EffectsMixer.SubGroupCreateFailed", "Failed to create sub-group"));
        _logService?.LogError(ex, "CreateSubGroup");
      }
      finally
      {
        IsLoading = false;
      }
    }

    private async Task DeleteSubGroupAsync(MixerSubGroup? subGroup, CancellationToken cancellationToken)
    {
      if (subGroup == null || string.IsNullOrWhiteSpace(SelectedProjectId))
        return;

      IsLoading = true;
      ErrorMessage = null;

      try
      {
        await _mixerStateClient.DeleteMixerSubGroupAsync(SelectedProjectId, subGroup.Id, cancellationToken);
        SubGroups.Remove(subGroup);

        // Update channels that were routed to this sub-group
        foreach (var channel in Channels)
        {
          if (channel.SubGroupId == subGroup.Id)
          {
            channel.MainDestination = VoiceStudio.Core.Models.RoutingDestination.Master;
            channel.SubGroupId = null;
          }
        }

        await SaveMixerStateAsync(cancellationToken);
      }
      catch (OperationCanceledException)
      {
        // User cancelled - expected
        return;
      }
      catch (Exception ex)
      {
        ErrorMessage = ErrorHandler.GetUserFriendlyMessage(ex);
        _errorService?.ShowError(ex, ResourceHelper.GetString("EffectsMixer.SubGroupDeleteFailed", "Failed to delete sub-group"));
        _logService?.LogError(ex, "DeleteSubGroup");
      }
      finally
      {
        IsLoading = false;
      }
    }

    private async Task UpdateSubGroupAsync(MixerSubGroup? subGroup, CancellationToken cancellationToken)
    {
      if (subGroup == null || string.IsNullOrWhiteSpace(SelectedProjectId))
        return;

      IsLoading = true;
      ErrorMessage = null;

      try
      {
        var updated = await _mixerStateClient.UpdateMixerSubGroupAsync(SelectedProjectId, subGroup.Id, subGroup, cancellationToken);
        var index = SubGroups.ToList().FindIndex(sg => sg.Id == subGroup.Id);
        if (index >= 0)
        {
          SubGroups[index] = updated;
        }
        await SaveMixerStateAsync(cancellationToken);
      }
      catch (OperationCanceledException)
      {
        // User cancelled - expected
        return;
      }
      catch (Exception ex)
      {
        ErrorMessage = ErrorHandler.GetUserFriendlyMessage(ex);
        _errorService?.ShowError(ex, ResourceHelper.GetString("EffectsMixer.SubGroupUpdateFailed", "Failed to update sub-group"));
        _logService?.LogError(ex, "UpdateSubGroup");
      }
      finally
      {
        IsLoading = false;
      }
    }

    private async Task UpdateSendAsync(MixerSend? send, CancellationToken cancellationToken)
    {
      if (send == null || string.IsNullOrWhiteSpace(SelectedProjectId))
        return;

      IsLoading = true;
      ErrorMessage = null;

      try
      {
        var updated = await _mixerStateClient.UpdateMixerSendAsync(SelectedProjectId, send.Id, send, cancellationToken);
        var index = Sends.ToList().FindIndex(s => s.Id == send.Id);
        if (index >= 0)
        {
          Sends[index] = updated;
        }
        await SaveMixerStateAsync(cancellationToken);
      }
      catch (OperationCanceledException)
      {
        // User cancelled - expected
        return;
      }
      catch (Exception ex)
      {
        ErrorMessage = ErrorHandler.GetUserFriendlyMessage(ex);
        _errorService?.ShowError(ex, ResourceHelper.GetString("EffectsMixer.SendUpdateFailed", "Failed to update send"));
        _logService?.LogError(ex, "UpdateSend");
      }
      finally
      {
        IsLoading = false;
      }
    }

    private async Task UpdateReturnAsync(MixerReturn? returnBus, CancellationToken cancellationToken)
    {
      if (returnBus == null || string.IsNullOrWhiteSpace(SelectedProjectId))
        return;

      IsLoading = true;
      ErrorMessage = null;

      try
      {
        var updated = await _mixerStateClient.UpdateMixerReturnAsync(SelectedProjectId, returnBus.Id, returnBus, cancellationToken);
        var index = Returns.ToList().FindIndex(r => r.Id == returnBus.Id);
        if (index >= 0)
        {
          Returns[index] = updated;
        }
        await SaveMixerStateAsync(cancellationToken);
      }
      catch (OperationCanceledException)
      {
        // User cancelled - expected
        return;
      }
      catch (Exception ex)
      {
        ErrorMessage = ErrorHandler.GetUserFriendlyMessage(ex);
        _errorService?.ShowError(ex, ResourceHelper.GetString("EffectsMixer.ReturnUpdateFailed", "Failed to update return"));
        _logService?.LogError(ex, "UpdateReturn");
      }
      finally
      {
        IsLoading = false;
      }
    }

    private async Task DeleteSendAsync(MixerSend? send, CancellationToken cancellationToken)
    {
      if (send == null || string.IsNullOrWhiteSpace(SelectedProjectId))
        return;

      IsLoading = true;
      ErrorMessage = null;

      try
      {
        await _mixerStateClient.DeleteMixerSendAsync(SelectedProjectId, send.Id, cancellationToken);
        Sends.Remove(send);

        // Remove send references from all channels
        foreach (var channel in Channels)
        {
          channel.SendLevels.Remove(send.Id);
          channel.SendEnabled.Remove(send.Id);
        }

        await SaveMixerStateAsync(cancellationToken);
      }
      catch (OperationCanceledException)
      {
        // User cancelled - expected
        return;
      }
      catch (Exception ex)
      {
        ErrorMessage = ErrorHandler.GetUserFriendlyMessage(ex);
        _errorService?.ShowError(ex, ResourceHelper.GetString("EffectsMixer.SendDeleteFailed", "Failed to delete send"));
        _logService?.LogError(ex, "DeleteSend");
      }
      finally
      {
        IsLoading = false;
      }
    }

    private async Task DeleteReturnAsync(MixerReturn? returnBus, CancellationToken cancellationToken)
    {
      if (returnBus == null || string.IsNullOrWhiteSpace(SelectedProjectId))
        return;

      IsLoading = true;
      ErrorMessage = null;

      try
      {
        await _mixerStateClient.DeleteMixerReturnAsync(SelectedProjectId, returnBus.Id, cancellationToken);
        Returns.Remove(returnBus);
        await SaveMixerStateAsync(cancellationToken);
      }
      catch (OperationCanceledException)
      {
        // User cancelled - expected
        return;
      }
      catch (Exception ex)
      {
        ErrorMessage = ErrorHandler.GetUserFriendlyMessage(ex);
        _errorService?.ShowError(ex, ResourceHelper.GetString("EffectsMixer.ReturnDeleteFailed", "Failed to delete return"));
        _logService?.LogError(ex, "DeleteReturn");
      }
      finally
      {
        IsLoading = false;
      }
    }

    // Multi-select methods
    public void ToggleChannelSelection(string channelId, bool isCtrlPressed, bool isShiftPressed)
    {
      if (_multiSelectState == null)
        return;

      if (isShiftPressed && !string.IsNullOrEmpty(_multiSelectState.RangeAnchorId))
      {
        // Range selection
        var allChannelIds = Channels.Select(c => c.Id).ToList();
        _multiSelectState.SetRange(_multiSelectState.RangeAnchorId, channelId, allChannelIds);
      }
      else if (isCtrlPressed)
      {
        // Toggle selection
        _multiSelectState.Toggle(channelId);
      }
      else
      {
        // Single selection (clear others)
        _multiSelectState.SetSingle(channelId);
      }

      UpdateChannelSelectionProperties();
      _multiSelectService.OnSelectionChanged(PanelId, _multiSelectState);
    }

    private void SelectAllChannels()
    {
      if (_multiSelectState == null)
        return;

      _multiSelectState.Clear();
      foreach (var channel in Channels)
      {
        _multiSelectState.Add(channel.Id);
      }
      if (Channels.Count > 0)
      {
        _multiSelectState.RangeAnchorId = Channels[0].Id;
      }

      UpdateChannelSelectionProperties();
      _multiSelectService.OnSelectionChanged(PanelId, _multiSelectState);
      SelectAllChannelsCommand.NotifyCanExecuteChanged();
    }

    private void ClearChannelSelection()
    {
      if (_multiSelectState == null)
        return;

      _multiSelectState.Clear();
      UpdateChannelSelectionProperties();
      _multiSelectService.OnSelectionChanged(PanelId, _multiSelectState);
    }

    private void UpdateChannelSelectionProperties()
    {
      if (_multiSelectState == null)
      {
        SelectedChannelCount = 0;
        HasMultipleChannelSelection = false;
      }
      else
      {
        SelectedChannelCount = _multiSelectState.Count;
        HasMultipleChannelSelection = _multiSelectState.IsMultipleSelection;
      }

      OnPropertyChanged(nameof(SelectedChannelCount));
      OnPropertyChanged(nameof(HasMultipleChannelSelection));
    }
  }

  /// <summary>
  /// Represents a mixer channel with VU meter data, volume, and pan controls.
  /// </summary>
  public partial class MixerChannel : ObservableObject
  {
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public int ChannelNumber { get; set; }
    public string Name { get; set; } = string.Empty;

    [ObservableProperty]
    private double peakLevel;

    [ObservableProperty]
    private double rmsLevel;

    [ObservableProperty]
    private double volume = 1.0; // 0.0 to 2.0 (0 = -∞ dB, 1.0 = 0 dB, 2.0 = +6 dB)

    [ObservableProperty]
    private double pan; // -1.0 (left) to 1.0 (right), 0.0 = center

    [ObservableProperty]
    private bool isMuted;

    [ObservableProperty]
    private bool isSoloed;

    // Routing properties
    [ObservableProperty]
    private RoutingDestination mainDestination = RoutingDestination.Master;

    [ObservableProperty]
    private string? subGroupId;

    // Send levels (send ID -> level 0.0-1.0)
    public Dictionary<string, double> SendLevels { get; set; } = new();

    // Send enabled (send ID -> enabled)
    public Dictionary<string, bool> SendEnabled { get; set; } = new();

    /// <summary>
    /// Volume in decibels for display (-∞ to +6 dB).
    /// </summary>
    public string VolumeDisplay
    {
      get
      {
        if (Volume <= 0.0)
          return "-∞ dB";
        var db = 20.0 * Math.Log10(Volume);
        return $"{db:F1} dB";
      }
    }

    /// <summary>
    /// Pan display as percentage (L 100% to R 100%).
    /// </summary>
    public string PanDisplay
    {
      get
      {
        if (Math.Abs(Pan) < 0.01)
          return "Center";
        var percent = Pan * 100.0;
        if (Pan < 0)
          return $"L {Math.Abs(percent):F0}%";

        return $"R {percent:F0}%";
      }
    }

    partial void OnVolumeChanged(double value)
    {
      OnPropertyChanged(nameof(VolumeDisplay));
    }

    partial void OnPanChanged(double value)
    {
      OnPropertyChanged(nameof(PanDisplay));
    }
  }
}