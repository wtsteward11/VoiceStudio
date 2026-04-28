using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using VoiceStudio.Core.Events;
using VoiceStudio.Core.Exceptions;
using VoiceStudio.Core.Models;
using VoiceStudio.Core.Panels;
using VoiceStudio.Core.Services;
using VoiceStudio.App.Helpers;
using VoiceStudio.App.Services;
using VoiceStudio.App.Utilities;
using VoiceStudio.App.Logging;
using VoiceStudio.App.ViewModels;
// Resolve ambiguity with VoiceStudio.App.ViewModels.QualityRecommendation
using QualityRecommendation = VoiceStudio.Core.Models.QualityRecommendation;

namespace VoiceStudio.App.Views.Panels
{
  /// <summary>Explicit workflow states for upload -> synthesize -> playback.</summary>
  public enum SynthesisWorkflowState
  {
    Idle,
    Uploading,
    ReadyToSynthesize,
    Synthesizing,
    AudioReady,
    Error
  }

  /// <summary>Profile vs. selected engine(s) allow-list state when <c>vs:engines:</c> tag is present.</summary>
  public enum ProfileEngineCompatibilityStatus
  {
    Unknown,
    Compatible,
    Incompatible
  }

  /// <summary>
  /// In-memory entry for the Voice Synthesis recent-results mini-list (not persisted; max 5 in VM).
  /// </summary>
  public sealed class VoiceSynthesisRecentResult
  {
    public string? AudioId { get; init; }
    public string? AudioReference { get; init; }
    public TimeSpan Duration { get; init; }
    public double QualityScore { get; init; }
    public string? ProfileId { get; init; }
    public string? ProfileName { get; init; }
    public string? Engine { get; init; }
    public DateTime CreatedAtLocal { get; init; }

    public string Summary
    {
      get
      {
        if (!string.IsNullOrWhiteSpace(AudioId))
          return AudioId.Length > 12 ? string.Concat(AudioId.AsSpan(0, 12), "…") : AudioId;
        if (!string.IsNullOrWhiteSpace(AudioReference))
          return AudioReference.Length > 32 ? string.Concat(AudioReference.AsSpan(0, 32), "…") : AudioReference;
        return "(unknown)";
      }
    }

    public string DetailLine
    {
      get
      {
        var eng = string.IsNullOrWhiteSpace(Engine) ? "—" : Engine;
        var prof = string.IsNullOrWhiteSpace(ProfileName) ? "—" : ProfileName;
        var t = Duration < TimeSpan.Zero ? TimeSpan.Zero : Duration;
        var dur = string.Format(
            System.Globalization.CultureInfo.InvariantCulture,
            "{0}:{1:D2}",
            (int)t.TotalMinutes,
            t.Seconds);
        var qs = QualityScore > 0
            ? string.Format(System.Globalization.CultureInfo.CurrentCulture, " · {0:P0}", QualityScore)
            : string.Empty;
        return $"{eng} · {dur} · {prof}{qs}";
      }
    }

    public string CreatedAtLabel => CreatedAtLocal.ToString("g", System.Globalization.CultureInfo.CurrentCulture);
  }

  // GAP-005: Updated to inherit from BaseViewModel for standardized error handling
  public partial class VoiceSynthesisViewModel : BaseViewModel, IPanelView, IPanelLifecycle, IPanelStatePersistable
  {
    public string PanelId => PanelIds.VoiceSynthesis;
    public string DisplayName => ResourceHelper.GetString("Panel.VoiceSynthesis.DisplayName", "Voice Synthesis");
    public PanelRegion Region => PanelRegion.Center;
    private readonly IVoiceSynthesisService _voiceSynthesisService;
    private readonly IEnginesClient _enginesClient;
    private readonly IQualityPipelineService _qualityPipelineService;
    private readonly IEnsembleService _ensembleService;
    private readonly ITextAnalysisService _textAnalysisService;
    private readonly IQualityHistoryService _qualityHistoryService;
    private readonly IProfilesClient _profilesClient;
    private readonly IAudioPlayerService _audioPlayer;
    private readonly RealTimeQualityService? _qualityService;
    private readonly IErrorLoggingService? _errorLoggingService;
    private readonly IErrorDialogService? _errorDialogService;
    private readonly IToastNotificationService? _toastNotificationService;
    private readonly IErrorPresentationService? _errorService;
    private readonly string _backendBaseUrl;
    private StreamingAudioPlayer? _streamingPlayer;
    private string? _currentSynthesisId;

    // Store event handlers for proper unsubscription
    private EventHandler<bool>? _isPlayingChangedHandler;
    private EventHandler? _playbackCompletedHandler;
    private EventHandler<QualityMetricsUpdatedEventArgs>? _qualityMetricsUpdatedHandler;
    private EventHandler<SynthesisCompletedEventArgs>? _synthesisCompletedHandler;

    private readonly IEventAggregator? _eventAggregator;
    private ISubscriptionToken? _profileSelectedToken;

    /// <summary>GAP-050 hygiene: last bound profile id for detecting cross-profile switches.</summary>
    private string? _lastNonNullProfileIdForEmotionHygiene;

    /// <summary>While true, profile changes from panel restore do not clear emotion preset.</summary>
    private bool _suppressEmotionClearForPanelRestore;

    private string? _pendingRestoreProfileId;
    private string? _pendingRestoreEngine;
    private bool _pendingRestoreHasEmotionKey;
    private string? _pendingRestoreEmotionRaw;

    private const string CustomKeyEmotionPreset = "VoiceSynthesis_EmotionPreset";
    private const string CustomKeySelectedEngine = "VoiceSynthesis_SelectedEngine";
    private const string CustomKeyAdvancedControlsExpanded = "VoiceSynthesis_AdvancedControlsExpanded";
    private const string CustomKeyRecentResults = "VoiceSynthesis_RecentResults";

    [ObservableProperty]
    private ObservableCollection<VoiceProfile> profiles = new();

    [ObservableProperty]
    private VoiceProfile? selectedProfile;

    [ObservableProperty]
    private string selectedEngine = "xtts";

    [ObservableProperty]
    private string text = string.Empty;

    [ObservableProperty]
    private string language = "en";

    [ObservableProperty]
    private string? emotion;

    [ObservableProperty]
    private bool enhanceQuality;

    /// <summary>GAP-067 slice 5: progressive disclosure for stability/temperature/mode knobs (persisted).</summary>
    [ObservableProperty]
    private bool isAdvancedSynthesisControlsExpanded;

    [ObservableProperty]
    private bool streamingMode;

    [ObservableProperty]
    private bool isStreaming;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StreamingChunksSummary))]
    private int streamingBufferedChunks;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StreamingChunksSummary))]
    private int streamingReceivedChunks;

    /// <summary>Single-line streaming chunk line for XAML (avoids x:Bind on Run inlines).</summary>
    public string StreamingChunksSummary =>
        $"Chunks: {StreamingReceivedChunks} / Buffered: {StreamingBufferedChunks}";

    [ObservableProperty]
    private string streamingStatus = string.Empty;

    [ObservableProperty]
    private bool isLoading;

    /// <summary>GAP-049: Use server-side chunked long-form synthesis and merge.</summary>
    [ObservableProperty]
    private bool useLongForm;

    /// <summary>GAP-049: Long-form synthesis in progress (distinct from generic loading for progress text).</summary>
    [ObservableProperty]
    private bool isLongFormRunning;

    /// <summary>GAP-049: Status line while long-form synthesis runs.</summary>
    [ObservableProperty]
    private string longFormProgressText = string.Empty;

    [ObservableProperty]
    private string statusMessage = string.Empty;

    [ObservableProperty]
    private string? errorMessage;

    [ObservableProperty]
    private bool hasError;

    [ObservableProperty]
    private bool isConsentRequired;

    [ObservableProperty]
    private string? consentRequiredProfileId;

    [ObservableProperty]
    private string? consentRequiredMessage;

    [ObservableProperty]
    private bool isPlaybackError;

    [ObservableProperty]
    private string? playbackErrorMessage;

    [ObservableProperty]
    private string? playbackErrorDetails;

    [ObservableProperty]
    private string? playbackErrorAudioId;

    [ObservableProperty]
    private string? playbackErrorAudioReference;

    private const int MaxRecentSynthesisResults = 5;

    /// <summary>Observable collection of recent synthesis outputs (newest first; max <see cref="MaxRecentSynthesisResults"/>).</summary>
    public ObservableCollection<VoiceSynthesisRecentResult> RecentSynthesisResults { get; } = new();

    [ObservableProperty]
    private VoiceSynthesisRecentResult? selectedRecentResult;

    public bool HasRecentSynthesisResults => RecentSynthesisResults.Count > 0;

    [ObservableProperty]
    private SynthesisWorkflowState workflowState = SynthesisWorkflowState.Idle;

    /// <summary>Copy-friendly error text for clipboard (e.g. "Copy details" affordance).</summary>
    public string? LastError => HasError ? ErrorMessage : null;

    /// <summary>When consent is required, the generic error InfoBar is hidden so the consent callout is the single primary surface.</summary>
    public bool ShowGenericSynthesisError => HasError && !IsConsentRequired;

    /// <summary>Playback failed but generated audio is still available; hidden while consent is primary.</summary>
    public bool ShowPlaybackError => IsPlaybackError && !IsConsentRequired;

    [ObservableProperty]
    private QualityMetrics? qualityMetrics;

    [ObservableProperty]
    private bool hasQualityMetrics;

    // Adaptive Quality Optimization (IDEA 53)
    [ObservableProperty]
    private TextAnalysisResult? textAnalysis;

    [ObservableProperty]
    private QualityRecommendation? qualityRecommendation;

    [ObservableProperty]
    private bool hasQualityRecommendation;

    [ObservableProperty]
    private bool isAnalyzingText;

    [ObservableProperty]
    private bool autoApplyRecommendations;

    [ObservableProperty]
    private string? lastSynthesizedAudioUrl;

    [ObservableProperty]
    private string? lastSynthesizedAudioId;

    /// <summary>Play enabled when workflow state is AudioReady (audio asset exists).</summary>
    public bool CanPlayAudio => WorkflowState == SynthesisWorkflowState.AudioReady && !IsLoading;

    public bool HasSynthesisResult =>
        WorkflowState == SynthesisWorkflowState.AudioReady &&
        !IsLoading &&
        (!string.IsNullOrWhiteSpace(LastSynthesizedAudioId) ||
         !string.IsNullOrWhiteSpace(LastSynthesizedAudioUrl));

    public string SynthesisResultSummary
    {
      get
      {
        if (!HasSynthesisResult)
          return ResourceHelper.GetString("VoiceSynthesis.GeneratedAudioUnavailable", "No generated audio yet.");

        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(LastSynthesizedAudioId))
          parts.Add($"Audio ID: {LastSynthesizedAudioId}");
        if (!string.IsNullOrWhiteSpace(LastSynthesizedAudioUrl))
          parts.Add($"Reference: {LastSynthesizedAudioUrl}");
        return string.Join(Environment.NewLine, parts);
      }
    }

    public bool CanCopyAudioId => HasSynthesisResult && !string.IsNullOrWhiteSpace(LastSynthesizedAudioId);

    public bool CanCopyAudioReference => HasSynthesisResult && !string.IsNullOrWhiteSpace(LastSynthesizedAudioUrl);

    public bool CanOpenOutputLocation => HasSynthesisResult && TryResolveExistingLocalOutputPath(LastSynthesizedAudioUrl, out _, out _);

    [ObservableProperty]
    private TimeSpan lastSynthesizedDuration;

    [ObservableProperty]
    private RealTimeQualityFeedback? realTimeQualityFeedback;

    [ObservableProperty]
    private bool hasRealTimeQualityFeedback;

    // Multi-Engine Ensemble (IDEA 55)
    [ObservableProperty]
    private bool useMultiEngineEnsemble;

    [ObservableProperty]
    private ObservableCollection<string> selectedEngines = new();

    [ObservableProperty]
    private ObservableCollection<string> availableEngines = new();

    [ObservableProperty]
    private string ensembleSelectionMode = "voting"; // voting, hybrid, fusion

    [ObservableProperty]
    private MultiEngineEnsembleStatus? ensembleStatus;

    [ObservableProperty]
    private bool hasEnsembleStatus;

    [ObservableProperty]
    private bool isEnsembleProcessing;

    [ObservableProperty]
    private string? ensembleJobId;

    /// <summary>Optional allow-list from profile tags: first <c>vs:engines:id1,id2</c> (case-sensitive prefix); ids compared with <see cref="StringComparer.OrdinalIgnoreCase"/>.</summary>
    public const string VoiceStudioProfileEnginesTagPrefix = "vs:engines:";

    [ObservableProperty]
    private ProfileEngineCompatibilityStatus selectedProfileEngineCompatibilityStatus = ProfileEngineCompatibilityStatus.Unknown;

    [ObservableProperty]
    private bool isProfileEngineCompatibilityKnown;

    [ObservableProperty]
    private bool isSelectedProfileEngineCompatible = true;

    [ObservableProperty]
    private string profileEngineCompatibilityMessage = string.Empty;

    [ObservableProperty]
    private bool hasCompatibleProfilesForSelectedEngine;

    [ObservableProperty]
    private bool showNoCompatibleProfilesHint;

    /// <summary>Profiles in <see cref="Profiles"/> order that satisfy <see cref="ProfileMatchesCurrentEngineSelection"/> for the current engine UI state.</summary>
    public ObservableCollection<VoiceProfile> CompatibleProfilesForSelectedEngine { get; } = new();

    private readonly NotifyCollectionChangedEventHandler _selectedEnginesCollectionChangedHandler;

    // Engine-Specific Quality Pipelines (IDEA 58)
    [ObservableProperty]
    private ObservableCollection<QualityPipeline> availablePipelines = new();

    [ObservableProperty]
    private QualityPipeline? selectedPipeline;

    [ObservableProperty]
    private string? selectedPipelinePreset;

    [ObservableProperty]
    private PipelineConfiguration? selectedPipelineConfig;

    [ObservableProperty]
    private bool isPreviewingPipeline;

    [ObservableProperty]
    private PreviewPipelineResponse? pipelinePreview;

    [ObservableProperty]
    private PipelineComparisonResponse? pipelineComparison;

    [ObservableProperty]
    private bool hasPipelineComparison;

    // Synthesis parameters (Phase 1.5 - wire sliders to API)
    [ObservableProperty]
    private double speed = 1.0;

    [ObservableProperty]
    private double pitch = 0.0;

    [ObservableProperty]
    private double stability = 0.72;

    [ObservableProperty]
    private double clarity = 0.58;

    [ObservableProperty]
    private double temperature = 0.35;

    /// <summary>Optional <paramref name="toastNotificationService"/> enables unit tests to capture toasts without WinUI.</summary>
    public VoiceSynthesisViewModel(IVoiceSynthesisService voiceSynthesisService, IEnginesClient enginesClient, IQualityPipelineService qualityPipelineService, IEnsembleService ensembleService, ITextAnalysisService textAnalysisService, IQualityHistoryService qualityHistoryService, IProfilesClient profilesClient, IAudioPlayerService audioPlayer, IToastNotificationService? toastNotificationService = null)
        : base(AppServices.GetViewModelContext())
    {
      _voiceSynthesisService = voiceSynthesisService ?? throw new ArgumentNullException(nameof(voiceSynthesisService));
      _enginesClient = enginesClient ?? throw new ArgumentNullException(nameof(enginesClient));
      _qualityPipelineService = qualityPipelineService ?? throw new ArgumentNullException(nameof(qualityPipelineService));
      _ensembleService = ensembleService ?? throw new ArgumentNullException(nameof(ensembleService));
      _textAnalysisService = textAnalysisService ?? throw new ArgumentNullException(nameof(textAnalysisService));
      _qualityHistoryService = qualityHistoryService ?? throw new ArgumentNullException(nameof(qualityHistoryService));
      _profilesClient = profilesClient ?? throw new ArgumentNullException(nameof(profilesClient));
      _audioPlayer = audioPlayer ?? throw new ArgumentNullException(nameof(audioPlayer));

      // Use BackendClientConfig as single source of truth (Phase 3: API URL alignment)
      _backendBaseUrl = AppServices.GetService<BackendClientConfig>()?.BaseUrl?.TrimEnd('/')
          ?? BackendClientConfig.DefaultHttpBaseUrl;

      // Try to get quality service (may not be available)
      try
      {
        _qualityService = ServiceProvider.GetRealTimeQualityService();
        _qualityMetricsUpdatedHandler = (s, e) => OnQualityMetricsUpdated(e);
        _synthesisCompletedHandler = (s, e) => OnSynthesisCompleted(e);
        _qualityService.QualityMetricsUpdated += _qualityMetricsUpdatedHandler;
        _qualityService.SynthesisCompleted += _synthesisCompletedHandler;
      }
      catch
      {
        // Quality service may not be registered
        _qualityService = null;
      }

      // Get error services
      try
      {
        _errorLoggingService = ServiceProvider.GetErrorLoggingService();
        _errorDialogService = ServiceProvider.GetErrorDialogService();
      }
      catch (Exception ex)
      {
        ErrorLogger.LogWarning($"Best effort operation failed: {ex.Message}", "VoiceSynthesisViewModel.Unknown");
      }

      // Toast surface (inject for tests, else app singleton when available)
      if (toastNotificationService != null)
      {
        _toastNotificationService = toastNotificationService;
      }
      else
      {
        try
        {
          _toastNotificationService = AppServices.TryGetToastNotificationService();
        }
        catch
        {
          _toastNotificationService = null;
        }
      }

      // EventAggregator for ProfileSelectedEvent (subscription in OnActivatedAsync per lifecycle rule)
      _eventAggregator = AppServices.TryGetEventAggregator();

      // Get error presentation service
      _errorService = ServiceProvider.TryGetErrorPresentationService();

      SynthesizeCommand = new EnhancedAsyncRelayCommand(async (ct) =>
      {
        using var profiler = PerformanceProfiler.Start("Command: Synthesize", PerformanceBudgets.CommandExecutionMs);
        await SynthesizeAsync(ct);
      }, () => CanSynthesize);

      LoadProfilesCommand = new EnhancedAsyncRelayCommand(async (ct) =>
      {
        using var profiler = PerformanceProfiler.Start("Command: LoadProfiles", PerformanceBudgets.CommandExecutionMs);
        await LoadProfilesAsync(ct);
      });

      PlayAudioCommand = new EnhancedAsyncRelayCommand(async (ct) =>
      {
        using var profiler = PerformanceProfiler.Start("Command: PlayAudio", PerformanceBudgets.CommandExecutionMs);
        await PlayAudioAsync(ct);
      }, () => CanPlayAudio);

      StopAudioCommand = new RelayCommand(StopAudio, () => _audioPlayer.IsPlaying);

      CopyLastErrorCommand = new RelayCommand(CopyLastErrorToClipboard, () => !string.IsNullOrEmpty(LastError));
      ClearErrorCommand = new RelayCommand(ClearError, () => HasError);
      CopyAudioIdCommand = new RelayCommand(CopyAudioIdToClipboard, () => CanCopyAudioId);
      CopyAudioReferenceCommand = new RelayCommand(CopyAudioReferenceToClipboard, () => CanCopyAudioReference);
      OpenOutputLocationCommand = new RelayCommand(OpenOutputLocation, () => CanOpenOutputLocation);

      RetryPlaybackCommand = new EnhancedAsyncRelayCommand(async (ct) =>
      {
        using var profiler = PerformanceProfiler.Start("Command: RetryPlayback", PerformanceBudgets.CommandExecutionMs);
        await PlayAudioAsync(ct);
      }, () => CanPlayAudio && !IsLoading);

      CopyPlaybackErrorCommand = new RelayCommand(
          CopyPlaybackErrorToClipboard,
          () => IsPlaybackError && !string.IsNullOrWhiteSpace(PlaybackErrorMessage));

      RestoreRecentResultCommand = new RelayCommand<VoiceSynthesisRecentResult>(
          RestoreRecentResult,
          r => r != null);

      RemoveRecentResultCommand = new RelayCommand<VoiceSynthesisRecentResult>(
          RemoveRecentResult,
          r => r != null);

      ClearRecentResultsCommand = new RelayCommand(
          ClearRecentResults,
          () => HasRecentSynthesisResults);

      OpenProfileConsentCommand = new RelayCommand(
          OpenProfileConsent,
          () => IsConsentRequired && !string.IsNullOrEmpty(ConsentRequiredProfileId));

      RetrySynthesisCommand = new EnhancedAsyncRelayCommand(
          async (ct) => { await SynthesizeAsync(ct).ConfigureAwait(false); },
          () => IsConsentRequired && CanSynthesize);

      // Add to Timeline command (Audit X-6: Synthesis -> Timeline)
      // GAP-B04: Disabled when busy or no synthesis output
      AddToTimelineCommand = new RelayCommand(AddSynthesizedAudioToTimeline,
          () => !string.IsNullOrEmpty(LastSynthesizedAudioId) && !IsLoading);

      // Streaming synthesis commands
      StartStreamingCommand = new EnhancedAsyncRelayCommand(async (ct) =>
      {
        using var profiler = PerformanceProfiler.Start("Command: StartStreaming", PerformanceBudgets.CommandExecutionMs);
        await StartStreamingAsync(ct);
      }, () => CanSynthesize && !IsStreaming && StreamingMode);

      StopStreamingCommand = new RelayCommand(StopStreaming, () => IsStreaming);

      // Adaptive Quality Optimization commands (IDEA 53)
      AnalyzeTextCommand = new EnhancedAsyncRelayCommand(async (ct) =>
      {
        using var profiler = PerformanceProfiler.Start("Command: AnalyzeText", PerformanceBudgets.CommandExecutionMs);
        await AnalyzeTextAsync(ct);
      }, () => !string.IsNullOrWhiteSpace(Text) && !IsAnalyzingText);

      GetQualityRecommendationCommand = new EnhancedAsyncRelayCommand(async (ct) =>
      {
        using var profiler = PerformanceProfiler.Start("Command: GetQualityRecommendation", PerformanceBudgets.CommandExecutionMs);
        await GetQualityRecommendationAsync(ct);
      }, () => !string.IsNullOrWhiteSpace(Text) && !IsAnalyzingText);

      ApplyRecommendationCommand = new RelayCommand(ApplyRecommendation, () => HasQualityRecommendation);

      // Engine-Specific Quality Pipelines commands (IDEA 58)
      LoadPipelinesCommand = new EnhancedAsyncRelayCommand(async (ct) =>
      {
        using var profiler = PerformanceProfiler.Start("Command: LoadPipelines", PerformanceBudgets.CommandExecutionMs);
        await LoadPipelinesAsync(ct);
      }, () => !string.IsNullOrEmpty(SelectedEngine));

      PreviewPipelineCommand = new EnhancedAsyncRelayCommand(async (ct) =>
      {
        using var profiler = PerformanceProfiler.Start("Command: PreviewPipeline", PerformanceBudgets.CommandExecutionMs);
        await PreviewPipelineAsync(ct);
      }, () => CanPlayAudio && !string.IsNullOrEmpty(LastSynthesizedAudioId) && !string.IsNullOrEmpty(SelectedPipelinePreset) && !IsPreviewingPipeline);

      ComparePipelineCommand = new EnhancedAsyncRelayCommand(async (ct) =>
      {
        using var profiler = PerformanceProfiler.Start("Command: ComparePipeline", PerformanceBudgets.CommandExecutionMs);
        await ComparePipelineAsync(ct);
      }, () => CanPlayAudio && !string.IsNullOrEmpty(LastSynthesizedAudioId) && !string.IsNullOrEmpty(SelectedPipelinePreset) && !IsPreviewingPipeline);

      SelectFirstCompatibleProfileCommand = new RelayCommand(
          () =>
          {
            var first = CompatibleProfilesForSelectedEngine.FirstOrDefault();
            if (first != null)
              SelectedProfile = first;
          },
          () => HasCompatibleProfilesForSelectedEngine);

      _selectedEnginesCollectionChangedHandler = (_, _) => RefreshProfileEngineCompatibility();
      SelectedEngines.CollectionChanged += _selectedEnginesCollectionChangedHandler;

      // Multi-Engine Ensemble commands (IDEA 55)
      CreateEnsembleCommand = new EnhancedAsyncRelayCommand(async (ct) =>
      {
        using var profiler = PerformanceProfiler.Start("Command: CreateEnsemble", PerformanceBudgets.CommandExecutionMs);
        await CreateEnsembleAsync(ct);
      }, () => SelectedProfile != null && !string.IsNullOrWhiteSpace(Text) && SelectedEngines.Count > 0 && !IsEnsembleProcessing);

      CheckEnsembleStatusCommand = new EnhancedAsyncRelayCommand(async (ct) =>
      {
        using var profiler = PerformanceProfiler.Start("Command: CheckEnsembleStatus", PerformanceBudgets.CommandExecutionMs);
        await CheckEnsembleStatusAsync(ct);
      }, () => !string.IsNullOrEmpty(EnsembleJobId) && !IsEnsembleProcessing);

      // Load profiles and engines on initialization
      var loadCt = new CancellationTokenSource(TimeSpan.FromSeconds(30)).Token;
      _ = LoadProfilesAsync(loadCt).ContinueWith(t =>
      {
        if (t.IsFaulted)
          _errorLoggingService?.LogError(t.Exception?.InnerException ?? new Exception("LoadProfiles failed"), "LoadProfiles");
      }, TaskScheduler.Default);
      _ = LoadEnginesAsync(loadCt).ContinueWith(t =>
      {
        if (t.IsFaulted)
          _errorLoggingService?.LogError(t.Exception?.InnerException ?? new Exception("LoadEngines failed"), "LoadEngines");
      }, TaskScheduler.Default);

      // Load pipelines when engine changes
      PropertyChanged += (s, e) =>
      {
        if (e.PropertyName == nameof(SelectedEngine))
        {
          var ct = new CancellationTokenSource(TimeSpan.FromSeconds(30)).Token;
          _ = LoadPipelinesAsync(ct).ContinueWith(t =>
                {
                  if (t.IsFaulted)
                    _errorLoggingService?.LogError(t.Exception?.InnerException ?? new Exception("LoadPipelines failed"), "LoadPipelines");
                }, TaskScheduler.Default);
        }
        else if (e.PropertyName == nameof(SelectedPipeline))
        {
          // Update preset name when pipeline is selected
          if (SelectedPipeline != null)
          {
            SelectedPipelinePreset = SelectedPipeline.Name;
          }
          PreviewPipelineCommand.NotifyCanExecuteChanged();
          ComparePipelineCommand.NotifyCanExecuteChanged();
        }
      };

      // Subscribe to audio player events (store handlers for disposal)
      _isPlayingChangedHandler = (_, _) =>
      {
        PlayAudioCommand.NotifyCanExecuteChanged();
        RefreshPlaybackErrorCommandState();
      };
      _playbackCompletedHandler = (_, _) =>
      {
        PlayAudioCommand.NotifyCanExecuteChanged();
        RefreshPlaybackErrorCommandState();
      };
      _audioPlayer.IsPlayingChanged += _isPlayingChangedHandler;
      _audioPlayer.PlaybackCompleted += _playbackCompletedHandler;

      RefreshProfileEngineCompatibility();
    }

    public EnhancedAsyncRelayCommand SynthesizeCommand { get; }
    public EnhancedAsyncRelayCommand LoadProfilesCommand { get; }
    public EnhancedAsyncRelayCommand PlayAudioCommand { get; }
    public IRelayCommand StopAudioCommand { get; }
    public IRelayCommand CopyLastErrorCommand { get; }
    public IRelayCommand ClearErrorCommand { get; }
    public IRelayCommand CopyAudioIdCommand { get; }
    public IRelayCommand CopyAudioReferenceCommand { get; }
    public IRelayCommand OpenOutputLocationCommand { get; }
    public EnhancedAsyncRelayCommand RetryPlaybackCommand { get; }
    public IRelayCommand CopyPlaybackErrorCommand { get; }
    public IRelayCommand RestoreRecentResultCommand { get; }
    public IRelayCommand RemoveRecentResultCommand { get; }
    public IRelayCommand ClearRecentResultsCommand { get; }
    public IRelayCommand OpenProfileConsentCommand { get; }
    public EnhancedAsyncRelayCommand RetrySynthesisCommand { get; }
    public IRelayCommand AddToTimelineCommand { get; }
    public EnhancedAsyncRelayCommand StartStreamingCommand { get; }
    public IRelayCommand StopStreamingCommand { get; }

    // Adaptive Quality Optimization commands (IDEA 53)
    public EnhancedAsyncRelayCommand AnalyzeTextCommand { get; }
    public EnhancedAsyncRelayCommand GetQualityRecommendationCommand { get; }
    public IRelayCommand ApplyRecommendationCommand { get; }

    // Multi-Engine Ensemble commands (IDEA 55)
    public EnhancedAsyncRelayCommand CreateEnsembleCommand { get; }
    public EnhancedAsyncRelayCommand CheckEnsembleStatusCommand { get; }

    // Engine-Specific Quality Pipelines commands (IDEA 58)
    public EnhancedAsyncRelayCommand LoadPipelinesCommand { get; }
    public EnhancedAsyncRelayCommand PreviewPipelineCommand { get; }
    public EnhancedAsyncRelayCommand ComparePipelineCommand { get; }

    public RelayCommand SelectFirstCompatibleProfileCommand { get; }

    public bool CanSynthesize =>
        SelectedProfile != null &&
        !string.IsNullOrWhiteSpace(Text) &&
        !IsLoading &&
        !IsLongFormRunning &&
        (!IsProfileEngineCompatibilityKnown || IsSelectedProfileEngineCompatible);

    /// <summary>Shown when the profile/engine compatibility InfoBar should appear (known incompatible only).</summary>
    public bool IsProfileEngineCompatibilityInfoBarOpen =>
        IsProfileEngineCompatibilityKnown && !IsSelectedProfileEngineCompatible;

    public InfoBarSeverity ProfileEngineCompatibilityInfoBarSeverity =>
        SelectedProfileEngineCompatibilityStatus == ProfileEngineCompatibilityStatus.Incompatible
            ? InfoBarSeverity.Warning
            : InfoBarSeverity.Informational;

    /// <summary>GAP-050: Preset prosody is applied post-synthesis for any engine once a profile is selected.</summary>
    public bool IsEmotionSupported => SelectedProfile != null;

    /// <summary>GAP-050 canonical emotion presets (matches backend mapper / VoiceSynthesisService).</summary>
    public IReadOnlyList<string> CanonicalEmotionPresets { get; } =
        new[] { "neutral", "warm", "energetic", "calm" };

    /// <summary>Normalize user/restored input to a canonical preset or null if unsupported.</summary>
    public string? NormalizeCanonicalEmotionPreset(string? raw)
    {
      if (string.IsNullOrWhiteSpace(raw))
        return null;
      var t = raw.Trim();
      foreach (var preset in CanonicalEmotionPresets)
      {
        if (string.Equals(preset, t, StringComparison.OrdinalIgnoreCase))
          return preset;
      }
      return null;
    }

    partial void OnEmotionChanged(string? value)
    {
      var normalized = NormalizeCanonicalEmotionPreset(value);
      if (!string.Equals(normalized, value, StringComparison.Ordinal))
        Emotion = normalized;
    }

    /// <summary>Parses the first <c>vs:engines:</c> tag into engine ids. Returns false if none or empty payload.</summary>
    private static bool TryParseEnginesAllowList(IReadOnlyList<string>? tags, out HashSet<string>? allowList)
    {
      allowList = null;
      if (tags is null || tags.Count == 0)
        return false;

      foreach (var raw in tags)
      {
        if (string.IsNullOrWhiteSpace(raw))
          continue;
        if (!raw.StartsWith(VoiceStudioProfileEnginesTagPrefix, StringComparison.Ordinal))
          continue;

        var payload = raw.Length > VoiceStudioProfileEnginesTagPrefix.Length
            ? raw.Substring(VoiceStudioProfileEnginesTagPrefix.Length)
            : string.Empty;
        var parts = payload.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0)
          return false;

        allowList = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in parts)
        {
          if (!string.IsNullOrWhiteSpace(p))
            allowList.Add(p.Trim());
        }

        return allowList.Count > 0;
      }

      return false;
    }

    private bool IsEnsembleEngineSelectionAmbiguousForCompatibility() =>
        UseMultiEngineEnsemble && SelectedEngines.Count == 0;

    private string GetEngineSelectionSummaryLabel()
    {
      if (UseMultiEngineEnsemble && SelectedEngines.Count > 0)
        return string.Join(", ", SelectedEngines);
      if (!string.IsNullOrWhiteSpace(SelectedEngine))
        return SelectedEngine;
      return "—";
    }

    private bool ProfileMatchesCurrentEngineSelection(VoiceProfile profile)
    {
      if (!TryParseEnginesAllowList(profile.Tags, out var allowOrNull))
        return true;
      // SAFETY: TryParseEnginesAllowList assigns a non-empty HashSet when returning true
      var allow = allowOrNull!;
      if (IsEnsembleEngineSelectionAmbiguousForCompatibility())
        return true;
      if (UseMultiEngineEnsemble)
        return SelectedEngines.All(e => allow.Contains(e));
      return !string.IsNullOrWhiteSpace(SelectedEngine) && allow.Contains(SelectedEngine);
    }

    private void RefreshProfileEngineCompatibility()
    {
      CompatibleProfilesForSelectedEngine.Clear();

      if (SelectedProfile == null)
      {
        IsProfileEngineCompatibilityKnown = false;
        IsSelectedProfileEngineCompatible = true;
        SelectedProfileEngineCompatibilityStatus = ProfileEngineCompatibilityStatus.Unknown;
        ProfileEngineCompatibilityMessage = "Select a voice profile and an engine to see compatibility.";
        HasCompatibleProfilesForSelectedEngine = false;
        ShowNoCompatibleProfilesHint = false;
        NotifyProfileEngineCompatibilitySurface();
        return;
      }

      var profileName = string.IsNullOrWhiteSpace(SelectedProfile.Name) ? SelectedProfile.Id : SelectedProfile.Name;
      var engineLabel = GetEngineSelectionSummaryLabel();
      var ambiguous = IsEnsembleEngineSelectionAmbiguousForCompatibility();

      bool known;
      HashSet<string>? selectedAllow = null;
      if (ambiguous)
      {
        known = false;
      }
      else
      {
        known = TryParseEnginesAllowList(SelectedProfile.Tags, out selectedAllow);
      }

      if (!known)
      {
        IsProfileEngineCompatibilityKnown = false;
        IsSelectedProfileEngineCompatible = true;
        SelectedProfileEngineCompatibilityStatus = ProfileEngineCompatibilityStatus.Unknown;
        ProfileEngineCompatibilityMessage =
            $"{engineLabel} · {profileName}. Profile metadata does not restrict engines for this project.";
      }
      else
      {
        IsProfileEngineCompatibilityKnown = true;
        var enginesOk = UseMultiEngineEnsemble
            ? SelectedEngines.All(e => selectedAllow!.Contains(e))
            : !string.IsNullOrWhiteSpace(SelectedEngine) && selectedAllow!.Contains(SelectedEngine);

        IsSelectedProfileEngineCompatible = enginesOk;
        SelectedProfileEngineCompatibilityStatus = enginesOk
            ? ProfileEngineCompatibilityStatus.Compatible
            : ProfileEngineCompatibilityStatus.Incompatible;

        ProfileEngineCompatibilityMessage = enginesOk
            ? $"{engineLabel} · {profileName}. Selected engine(s) satisfy this profile's engine allow-list."
            : $"{engineLabel} · {profileName}. This profile's metadata restricts synthesis to other engine(s); change engine or profile to continue.";
      }

      foreach (var p in Profiles)
      {
        if (ProfileMatchesCurrentEngineSelection(p))
          CompatibleProfilesForSelectedEngine.Add(p);
      }

      HasCompatibleProfilesForSelectedEngine = CompatibleProfilesForSelectedEngine.Count > 0;
      ShowNoCompatibleProfilesHint = Profiles.Count > 0 && !HasCompatibleProfilesForSelectedEngine;

      NotifyProfileEngineCompatibilitySurface();
    }

    private void NotifyProfileEngineCompatibilitySurface()
    {
      OnPropertyChanged(nameof(CanSynthesize));
      OnPropertyChanged(nameof(IsProfileEngineCompatibilityInfoBarOpen));
      OnPropertyChanged(nameof(ProfileEngineCompatibilityInfoBarSeverity));
      SynthesizeCommand.NotifyCanExecuteChanged();
      RetrySynthesisCommand.NotifyCanExecuteChanged();
      StartStreamingCommand.NotifyCanExecuteChanged();
      SelectFirstCompatibleProfileCommand.NotifyCanExecuteChanged();
    }

    /// <summary>Clears prior operation error/capability UI state before a new synthesis attempt.</summary>
    private void BeginSynthesisOperationNarrativeHygiene()
    {
      ErrorMessage = null;
      HasError = false;
      HasQualityMetrics = false;
      ClearConsentState();
      ClearPlaybackError();
      ResetLastSynthesisOutput();
    }

    private void ResetLastSynthesisOutput()
    {
      LastSynthesizedAudioId = string.Empty;
      LastSynthesizedAudioUrl = string.Empty;
      LastSynthesizedDuration = TimeSpan.Zero;
    }

    // Quality metrics display properties
    public string MosScore =>
        QualityMetrics?.MosScore.HasValue == true
            ? $"{QualityMetrics.MosScore:F2}/5.0"
            : "N/A";

    public string Similarity =>
        QualityMetrics?.Similarity.HasValue == true
            ? $"{QualityMetrics.Similarity.Value * 100:F1}%"
            : "N/A";

    public string Naturalness =>
        QualityMetrics?.Naturalness.HasValue == true
            ? $"{QualityMetrics.Naturalness.Value * 100:F1}%"
            : "N/A";

    public string OverallQuality =>
        QualityMetrics != null
            ? CalculateOverallQuality()
            : "N/A";

    public Brush QualityColor
    {
      get
      {
        if (QualityMetrics == null) return new SolidColorBrush(Microsoft.UI.Colors.Gray);

        var quality = CalculateOverallQualityValue();
        if (quality >= 0.85) return new SolidColorBrush(Microsoft.UI.Colors.Green);
        if (quality >= 0.70) return new SolidColorBrush(Microsoft.UI.Colors.Orange);
        return new SolidColorBrush(Microsoft.UI.Colors.Red);
      }
    }

    private async Task LoadProfilesAsync(CancellationToken cancellationToken)
    {
      IsLoading = true;
      ErrorMessage = null;
      HasError = false;
      WorkflowState = SynthesisWorkflowState.Uploading;

      try
      {
        var profilesList = await _profilesClient.GetProfilesAsync(cancellationToken);

        Profiles.Clear();
        foreach (var profile in profilesList)
        {
          Profiles.Add(profile);
        }

        await LoadEnginesAsync(cancellationToken);

        if (Profiles.Count > 0)
        {
          _toastNotificationService?.ShowSuccess(
              ResourceHelper.GetString("VoiceSynthesis.ProfilesLoaded", "Profiles Loaded"),
              ResourceHelper.FormatString("VoiceSynthesis.ProfilesLoadedCount", Profiles.Count));
        }
      }
      catch (OperationCanceledException)
      {
        // User cancelled - expected
        return;
      }
      catch (Exception ex)
      {
        _errorLoggingService?.LogError(ex, "LoadProfiles");
        ErrorMessage = ErrorHandler.GetUserFriendlyMessage(ex);
        HasError = true;
        _errorService?.ShowError(ex, ResourceHelper.GetString("Profile.LoadFailed", "Failed to load profiles"));
        _toastNotificationService?.ShowError(
            ResourceHelper.GetString("VoiceSynthesis.ProfilesLoadFailed", "Failed to Load Profiles"),
            ErrorHandler.GetUserFriendlyMessage(ex));
        await (_errorDialogService?.ShowErrorAsync(ex, ResourceHelper.GetString("Panel.Profiles.DisplayName", "Load Profiles")) ?? Task.CompletedTask);
      }
      finally
      {
        IsLoading = false;
        TryCompletePendingPanelRestore();
        UpdateWorkflowStateFromInputs();
        RefreshProfileEngineCompatibility();
      }
    }

    private void UpdateWorkflowStateFromInputs()
    {
      if (HasError)
      {
        WorkflowState = SynthesisWorkflowState.Error;
        return;
      }
      if (!string.IsNullOrEmpty(LastSynthesizedAudioId) || !string.IsNullOrEmpty(LastSynthesizedAudioUrl))
      {
        WorkflowState = SynthesisWorkflowState.AudioReady;
        return;
      }
      if (SelectedProfile != null && !string.IsNullOrWhiteSpace(Text))
      {
        WorkflowState = SynthesisWorkflowState.ReadyToSynthesize;
        return;
      }
      WorkflowState = SynthesisWorkflowState.Idle;
    }

    private async Task LoadEnginesAsync(CancellationToken cancellationToken)
    {
      try
      {
        var engines = await _enginesClient.GetEnginesAsync(cancellationToken);
        AvailableEngines.Clear();
        foreach (var engine in engines)
        {
          AvailableEngines.Add(engine);
        }
        if (AvailableEngines.Count > 0 && string.IsNullOrEmpty(SelectedEngine))
        {
          SelectedEngine = AvailableEngines[0];
        }
      }
      catch (OperationCanceledException)
      {
        return;
      }
      catch (Exception ex)
      {
        _errorLoggingService?.LogError(ex, "LoadEngines");
      }
    }

    private async Task SynthesizeAsync(CancellationToken cancellationToken)
    {
      if (SelectedProfile == null || string.IsNullOrWhiteSpace(Text))
        return;

      // If multi-engine ensemble is enabled, use ensemble synthesis instead
      if (UseMultiEngineEnsemble && SelectedEngines.Count > 0)
      {
        await CreateEnsembleAsync(cancellationToken);
        return;
      }

      IsLoading = true;
      BeginSynthesisOperationNarrativeHygiene();
      WorkflowState = SynthesisWorkflowState.Synthesizing;
      StatusMessage = ResourceHelper.GetString("Status.Synthesizing", "Synthesizing voice...");

      try
      {
        SynthesizeCommand.ReportProgress(0);

        // Validate input
        var textValidation = InputValidator.ValidateSynthesisText(Text);
        if (!textValidation.IsValid)
        {
          ErrorMessage = textValidation.ErrorMessage;
          HasError = true;
          WorkflowState = SynthesisWorkflowState.Error;
          StatusMessage = string.Empty;
          return;
        }

        // Generate synthesis ID for tracking
        _currentSynthesisId = $"synth_{SelectedProfile.Id}_{System.Guid.NewGuid():N}";

        // Start tracking quality metrics
        if (_qualityService != null)
        {
          RealTimeQualityFeedback = _qualityService.StartTracking(
              _currentSynthesisId,
              SelectedProfile.Id,
              SelectedEngine
          );
          HasRealTimeQualityFeedback = true;
          OnPropertyChanged(nameof(RealTimeQualityFeedback));
        }

        SynthesizeCommand.ReportProgress(10);

        var request = new VoiceSynthesisRequest
        {
          Engine = SelectedEngine,
          ProfileId = SelectedProfile.Id,
          Text = Text!,
          Language = Language,
          Emotion = Emotion,
          EnhanceQuality = EnhanceQuality,
          Speed = (float)Speed,
          Pitch = (float)Pitch,
          Stability = (float)Stability,
          Clarity = (float)Clarity,
          Temperature = (float)Temperature
        };

        // Update progress estimate (synthesis starting)
        if (_qualityService != null && _currentSynthesisId != null)
        {
          _qualityService.UpdateMetrics(_currentSynthesisId, 0.0, null, 0.5);
        }

        SynthesizeCommand.ReportProgress(25);

        VoiceSynthesisResponse response;
        if (UseLongForm)
        {
          IsLongFormRunning = true;
          LongFormProgressText = ResourceHelper.GetString(
              "VoiceSynthesis.LongFormProgress",
              "Processing long-form audio...");
          try
          {
            var lfRequest = new LongFormSynthesisRequest
            {
              Engine = SelectedEngine,
              ProfileId = SelectedProfile.Id,
              Text = Text!,
              Language = Language,
              Emotion = Emotion,
              EnhanceQuality = EnhanceQuality,
              Speed = (float)Speed,
              Pitch = (float)Pitch,
              Stability = (float)Stability,
              Clarity = (float)Clarity,
              Temperature = (float)Temperature,
              ChunkSizeChars = 1800,
            };
            var lf = await _voiceSynthesisService.SynthesizeLongFormAsync(lfRequest, cancellationToken);
            response = new VoiceSynthesisResponse
            {
              AudioId = lf.AudioId,
              AudioUrl = lf.AudioUrl,
              Duration = lf.Duration,
              QualityScore = lf.QualityScore,
              QualityMetrics = null,
            };
            if (lf.PartialFailure && lf.FailedChunks is { Count: > 0 })
            {
              var failedIdx = string.Join(", ", lf.FailedChunks.ConvertAll(c => c.ChunkIndex.ToString()));
              _toastNotificationService?.ShowWarning(
                  $"Some chunks failed (indices: {failedIdx}). Output merges successful chunks only.",
                  ResourceHelper.GetString(
                      "VoiceSynthesis.LongFormPartialTitle",
                      "Long-form synthesis"));
            }
          }
          finally
          {
            LongFormProgressText = string.Empty;
            IsLongFormRunning = false;
          }
        }
        else
        {
          response = await _voiceSynthesisService.SynthesizeVoiceAsync(request, cancellationToken);
        }

        SynthesizeCommand.ReportProgress(50);

        // Update progress estimate (synthesis in progress)
        if (_qualityService != null && _currentSynthesisId != null)
        {
          _qualityService.UpdateMetrics(_currentSynthesisId, 0.5, response.QualityMetrics, response.QualityScore);
        }

        SynthesizeCommand.ReportProgress(75);

        // Update quality metrics
        if (response.QualityMetrics != null)
        {
          QualityMetrics = response.QualityMetrics;
          HasQualityMetrics = true;
          OnPropertyChanged(nameof(MosScore));
          OnPropertyChanged(nameof(Similarity));
          OnPropertyChanged(nameof(Naturalness));
          OnPropertyChanged(nameof(OverallQuality));
          OnPropertyChanged(nameof(QualityColor));
        }

        // Complete quality tracking with final metrics
        if (_qualityService != null && _currentSynthesisId != null)
        {
          RealTimeQualityFeedback = _qualityService.CompleteTracking(
              _currentSynthesisId,
              response.QualityMetrics,
              response.QualityScore
          );
          OnPropertyChanged(nameof(RealTimeQualityFeedback));
        }

        ClearConsentState();

        // Store audio URL, ID, and duration for playback and timeline
        LastSynthesizedAudioUrl = response.AudioUrl;
        LastSynthesizedAudioId = response.AudioId;
        LastSynthesizedDuration = TimeSpan.FromSeconds(response.Duration);
        WorkflowState = (!string.IsNullOrWhiteSpace(LastSynthesizedAudioUrl) ||
                         !string.IsNullOrWhiteSpace(LastSynthesizedAudioId))
            ? SynthesisWorkflowState.AudioReady
            : WorkflowState;
        PlayAudioCommand.NotifyCanExecuteChanged();
        AddToTimelineCommand.NotifyCanExecuteChanged();

        AddRecentSynthesisResult(
            response.AudioId,
            response.AudioUrl,
            response.Duration,
            response.QualityScore,
            SelectedProfile?.Id,
            SelectedProfile?.Name,
            SelectedEngine);

        StatusMessage = ResourceHelper.FormatString("Status.SynthesisComplete", response.Duration, response.QualityScore);

        SynthesizeCommand.ReportProgress(90);

        // Store quality history (IDEA 30)
        if (response.QualityMetrics != null && SelectedProfile != null)
        {
          var ct = new CancellationTokenSource(TimeSpan.FromSeconds(30)).Token;
          _ = StoreQualityHistoryAsync(
              SelectedProfile.Id,
              SelectedEngine,
              response.QualityMetrics,
              response.QualityScore,
              Text,
              response.AudioUrl,
              EnhanceQuality,
              ct
          ).ContinueWith(t =>
          {
            if (t.IsFaulted)
              _errorLoggingService?.LogError(t.Exception?.InnerException ?? new Exception("StoreQualityHistory failed"), "StoreQualityHistory");
          }, TaskScheduler.Default);
        }

        SynthesizeCommand.ReportProgress(100);

        // Show success toast
        var qualityPercent = $"{response.QualityScore:P0}";
        _toastNotificationService?.ShowSuccess(
            ResourceHelper.FormatString("VoiceSynthesis.SynthesisCompleteDetail", response.Duration, qualityPercent),
            ResourceHelper.GetString("VoiceSynthesis.SynthesisComplete", "Synthesis Complete")
        );

        var combined = ActionableErrorTranslator.BuildSynthesisCapabilityCombinedNotice(
            response.SsmlHandling,
            response.ProsodyHandling,
            response.EmotionPresetApplyFailureMessage);
        if (combined != null)
        {
          _toastNotificationService?.ShowWarning(combined.PrimaryMessage, combined.Title);
        }
      }
      catch (OperationCanceledException)
      {
        // User cancelled - expected
        StatusMessage = ResourceHelper.GetString("Status.SynthesisCancelled", "Synthesis cancelled");
        return;
      }
      catch (ConsentRequiredException consentEx)
      {
        _errorLoggingService?.LogError(consentEx, "VoiceSynthesis.ConsentRequired", new Dictionary<string, object>
        {
            { "Engine", SelectedEngine },
            { "ProfileId", SelectedProfile?.Id ?? "unknown" },
            { "TextLength", Text?.Length ?? 0 }
        });
        var actionable = ActionableErrorTranslator.Translate(consentEx, ActionableOperationContext.VoiceSynthesize);
        var errorMsg = actionable.PrimaryMessage;
        ErrorMessage = errorMsg;
        HasError = true;
        IsConsentRequired = true;
        ConsentRequiredProfileId = SelectedProfile?.Id;
        ConsentRequiredMessage = string.IsNullOrWhiteSpace(actionable.RecommendedAction)
            ? ResourceHelper.GetString(
                "VoiceSynthesis.ConsentRequiredDefaultDetail",
                "Open the profile in Profiles and complete voice consent, then retry.")
            : actionable.RecommendedAction;
        WorkflowState = SynthesisWorkflowState.Error;
        StatusMessage = string.Empty;

        _errorService?.ShowError(consentEx, ResourceHelper.GetString("Timeline.SynthesisFailed", "Failed to synthesize voice"));
        var toastDetail = string.IsNullOrWhiteSpace(actionable.SecondaryDetail)
            ? errorMsg
            : $"{errorMsg}{Environment.NewLine}{actionable.SecondaryDetail}";
        _toastNotificationService?.ShowError(
            ResourceHelper.FormatString("VoiceSynthesis.SynthesisFailedDetail", toastDetail),
            ResourceHelper.GetString("VoiceSynthesis.SynthesisFailed", "Synthesis Failed"));
        await (_errorDialogService?.ShowErrorAsync(consentEx, ResourceHelper.GetString("Panel.VoiceSynthesis.DisplayName", "Voice Synthesis")) ?? Task.CompletedTask);
      }
      catch (Exception ex)
      {
        _errorLoggingService?.LogError(ex, "VoiceSynthesis", new Dictionary<string, object>
                {
                    { "Engine", SelectedEngine },
                    { "ProfileId", SelectedProfile?.Id ?? "unknown" },
                    { "TextLength", Text?.Length ?? 0 }
                });
        var actionable = ActionableErrorTranslator.Translate(ex, ActionableOperationContext.VoiceSynthesize);
        var errorMsg = actionable.PrimaryMessage;
        ErrorMessage = errorMsg;
        HasError = true;
        IsConsentRequired = false;
        WorkflowState = SynthesisWorkflowState.Error;
        StatusMessage = string.Empty;

        _errorService?.ShowError(ex, ResourceHelper.GetString("Timeline.SynthesisFailed", "Failed to synthesize voice"));
        var toastDetail = string.IsNullOrWhiteSpace(actionable.SecondaryDetail)
            ? errorMsg
            : $"{errorMsg}{Environment.NewLine}{actionable.SecondaryDetail}";
        _toastNotificationService?.ShowError(
            ResourceHelper.FormatString("VoiceSynthesis.SynthesisFailedDetail", toastDetail),
            ResourceHelper.GetString("VoiceSynthesis.SynthesisFailed", "Synthesis Failed"));
        await (_errorDialogService?.ShowErrorAsync(ex, ResourceHelper.GetString("Panel.VoiceSynthesis.DisplayName", "Voice Synthesis")) ?? Task.CompletedTask);
      }
      finally
      {
        IsLoading = false;
        if (WorkflowState != SynthesisWorkflowState.AudioReady && WorkflowState != SynthesisWorkflowState.Error)
        {
          UpdateWorkflowStateFromInputs();
        }
        PlayAudioCommand.NotifyCanExecuteChanged();
      }
    }

    /// <summary>
    /// Stores quality history entry after synthesis (IDEA 30).
    /// </summary>
    private async Task StoreQualityHistoryAsync(
        string profileId,
        string engine,
        QualityMetrics metrics,
        double qualityScore,
        string? synthesisText,
        string? audioUrl,
        bool enhancedQuality,
        CancellationToken cancellationToken)
    {
      try
      {
        // Convert QualityMetrics to dictionary for backend API
        var metricsDict = ConvertQualityMetricsToDictionary(metrics);

        // Create quality history request
        var request = new QualityHistoryRequest
        {
          ProfileId = profileId,
          Engine = engine,
          Metrics = metricsDict,
          QualityScore = qualityScore,
          SynthesisText = synthesisText,
          AudioUrl = audioUrl,
          EnhancedQuality = enhancedQuality
        };

        // Store via backend client
        await _qualityHistoryService.StoreQualityHistoryAsync(request, cancellationToken);
      }
      catch (OperationCanceledException)
      {
        // User cancelled - expected
        return;
      }
      catch (Exception ex)
      {
        // Log error but don't break synthesis flow - quality history is non-critical
        _errorLoggingService?.LogError(ex, "StoreQualityHistory", new Dictionary<string, object>
                {
                    { "ProfileId", profileId },
                    { "Engine", engine }
                });
        // Don't show error toast - quality history failures shouldn't interrupt user workflow
      }
    }

    /// <summary>
    /// Converts QualityMetrics object to dictionary format for backend API.
    /// </summary>
    private Dictionary<string, object> ConvertQualityMetricsToDictionary(QualityMetrics? metrics)
    {
      var dict = new Dictionary<string, object>();

      if (metrics == null)
        return dict;

      if (metrics.MosScore.HasValue)
        dict["mos_score"] = metrics.MosScore.Value;

      if (metrics.Similarity.HasValue)
        dict["similarity"] = metrics.Similarity.Value;

      if (metrics.Naturalness.HasValue)
        dict["naturalness"] = metrics.Naturalness.Value;

      if (metrics.SnrDb.HasValue)
        dict["snr_db"] = metrics.SnrDb.Value;

      if (metrics.ArtifactScore.HasValue)
        dict["artifact_score"] = metrics.ArtifactScore.Value;

      if (metrics.HasClicks.HasValue)
        dict["has_clicks"] = metrics.HasClicks.Value;

      if (metrics.HasDistortion.HasValue)
        dict["has_distortion"] = metrics.HasDistortion.Value;

      if (metrics.VoiceProfileMatch != null)
        dict["voice_profile_match"] = metrics.VoiceProfileMatch;

      return dict;
    }

    private string CalculateOverallQuality()
    {
      if (QualityMetrics == null) return "N/A";

      var value = CalculateOverallQualityValue();
      return $"{value:P0}";
    }

    private double CalculateOverallQualityValue()
    {
      if (QualityMetrics == null) return 0.0;

      var scores = new System.Collections.Generic.List<double>();

      if (QualityMetrics.MosScore.HasValue)
        scores.Add(QualityMetrics.MosScore.Value / 5.0);

      if (QualityMetrics.Similarity.HasValue)
        scores.Add(QualityMetrics.Similarity.Value);

      if (QualityMetrics.Naturalness.HasValue)
        scores.Add(QualityMetrics.Naturalness.Value);

      return scores.Count > 0 ? scores.Average() : 0.0;
    }

    partial void OnSelectedProfileChanged(VoiceProfile? value)
    {
      ClearError();
      OnPropertyChanged(nameof(IsEmotionSupported));
      if (value == null)
      {
        Emotion = null;
        _lastNonNullProfileIdForEmotionHygiene = null;
      }
      else
      {
        if (!_suppressEmotionClearForPanelRestore &&
            _lastNonNullProfileIdForEmotionHygiene != null &&
            !string.Equals(_lastNonNullProfileIdForEmotionHygiene, value.Id, StringComparison.Ordinal))
        {
          Emotion = null;
        }
        _lastNonNullProfileIdForEmotionHygiene = value.Id;
      }
      RefreshProfileEngineCompatibility();
      if (!IsLoading && WorkflowState != SynthesisWorkflowState.Synthesizing)
        UpdateWorkflowStateFromInputs();
    }

    /// <inheritdoc />
    /// <summary>Subscribe to ProfileSelectedEvent (Fix 3: lifecycle rule - subscribe on activate).</summary>
    public Task OnActivatedAsync(CancellationToken cancellationToken = default)
    {
      _profileSelectedToken?.Dispose();
      _profileSelectedToken = null;
      if (_eventAggregator != null)
        _profileSelectedToken = _eventAggregator.Subscribe<ProfileSelectedEvent>(OnProfileSelected);

      // GAP-026: pick up profile activated while this panel was inactive (event may have fired before subscribe).
      var ctx = AppServices.TryGetContextManager();
      var activeId = ctx?.ActiveProfileId;
      if (!string.IsNullOrEmpty(activeId) && activeId != SelectedProfile?.Id)
      {
        OnProfileSelected(new ProfileSelectedEvent(
            "context-manager-sync",
            activeId,
            ctx?.ActiveProfileName,
            InteractionIntent.Navigation));
      }

      return Task.CompletedTask;
    }

    /// <inheritdoc />
    public async Task RefreshAsync(CancellationToken cancellationToken = default) =>
        await LoadProfilesAsync(cancellationToken);

    /// <inheritdoc />
    /// <summary>Unsubscribe from EventAggregator to prevent memory leaks (GAP-W3).</summary>
    public Task OnDeactivatedAsync(CancellationToken cancellationToken = default)
    {
      _profileSelectedToken?.Dispose();
      _profileSelectedToken = null;
      return Task.CompletedTask;
    }

    /// <summary>
    /// Handles ProfileSelectedEvent from Profiles panel. Updates synthesis target when user selects a profile elsewhere.
    /// Panel Communication Matrix: ProfilesView publishes, VoiceSynthesisView subscribes.
    /// </summary>
    private void OnProfileSelected(ProfileSelectedEvent e)
    {
      var match = Profiles.FirstOrDefault(p => p.Id == e.ProfileId);
      if (match != null)
      {
        Dispatcher.TryEnqueue(() => SelectedProfile = match);
        System.Diagnostics.Debug.WriteLine($"VoiceSynthesisViewModel: Profile selected from {e.SourcePanelId} - {e.ProfileId}");
        return;
      }
      // Profile not in list yet (e.g. just created or profiles not loaded) - reload and select
      _ = LoadProfilesAsync(CancellationToken.None).ContinueWith(t =>
      {
        if (t.IsCompletedSuccessfully)
        {
          var found = Profiles.FirstOrDefault(p => p.Id == e.ProfileId);
          if (found != null)
            Dispatcher.TryEnqueue(() => SelectedProfile = found);
        }
      }, TaskScheduler.Default);
    }

    partial void OnTextChanged(string value)
    {
      SynthesizeCommand.NotifyCanExecuteChanged();
      RetrySynthesisCommand.NotifyCanExecuteChanged();
      if (!IsLoading && WorkflowState != SynthesisWorkflowState.Synthesizing)
        UpdateWorkflowStateFromInputs();
    }

    partial void OnIsLoadingChanged(bool value)
    {
      SynthesizeCommand.NotifyCanExecuteChanged();
      AddToTimelineCommand.NotifyCanExecuteChanged(); // GAP-B04
      OnPropertyChanged(nameof(CanPlayAudio));
      RefreshSynthesisResultState();
      PlayAudioCommand.NotifyCanExecuteChanged();
      RetrySynthesisCommand.NotifyCanExecuteChanged();
    }

    partial void OnIsConsentRequiredChanged(bool value)
    {
      OnPropertyChanged(nameof(ShowGenericSynthesisError));
      OnPropertyChanged(nameof(ShowPlaybackError));
      OpenProfileConsentCommand.NotifyCanExecuteChanged();
      RetrySynthesisCommand.NotifyCanExecuteChanged();
    }

    partial void OnConsentRequiredProfileIdChanged(string? value)
    {
      OpenProfileConsentCommand.NotifyCanExecuteChanged();
    }

    partial void OnIsLongFormRunningChanged(bool value)
    {
      SynthesizeCommand.NotifyCanExecuteChanged();
    }

    partial void OnUseMultiEngineEnsembleChanged(bool value)
    {
      RefreshProfileEngineCompatibility();
    }

    partial void OnWorkflowStateChanged(SynthesisWorkflowState value)
    {
      OnPropertyChanged(nameof(CanPlayAudio));
      RefreshSynthesisResultState();
      PlayAudioCommand.NotifyCanExecuteChanged();
    }

    partial void OnLastSynthesizedAudioIdChanged(string? value)
    {
      RefreshSynthesisResultState();
      AddToTimelineCommand.NotifyCanExecuteChanged();
    }

    partial void OnLastSynthesizedAudioUrlChanged(string? value)
    {
      RefreshSynthesisResultState();
    }

    partial void OnHasErrorChanged(bool value)
    {
      OnPropertyChanged(nameof(LastError));
      OnPropertyChanged(nameof(ShowGenericSynthesisError));
      CopyLastErrorCommand.NotifyCanExecuteChanged();
      ClearErrorCommand.NotifyCanExecuteChanged();
    }

    partial void OnErrorMessageChanged(string? value)
    {
      OnPropertyChanged(nameof(LastError));
      CopyLastErrorCommand.NotifyCanExecuteChanged();
    }

    private void ClearConsentState()
    {
      IsConsentRequired = false;
      ConsentRequiredProfileId = null;
      ConsentRequiredMessage = null;
    }

    private void OpenProfileConsent()
    {
      if (_eventAggregator == null || string.IsNullOrEmpty(ConsentRequiredProfileId))
        return;

      _eventAggregator.Publish(new NavigateToEvent(
          PanelIds.VoiceSynthesis,
          PanelIds.Profiles,
          new Dictionary<string, object> { ["profileId"] = ConsentRequiredProfileId! }));
    }

    private void CopyLastErrorToClipboard()
    {
      var text = LastError;
      if (string.IsNullOrEmpty(text))
        return;
      var dataPackage = new Windows.ApplicationModel.DataTransfer.DataPackage();
      dataPackage.SetText(text);
      Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(dataPackage);
      _toastNotificationService?.ShowSuccess(
          ResourceHelper.GetString("VoiceSynthesis.ErrorCopied", "Copied"),
          ResourceHelper.GetString("VoiceSynthesis.ErrorDetailsCopied", "Error details copied to clipboard"));
    }

    private void CopyAudioIdToClipboard()
    {
      CopyTextToClipboard(
          LastSynthesizedAudioId,
          ResourceHelper.GetString("VoiceSynthesis.AudioIdCopied", "Copied"),
          ResourceHelper.GetString("VoiceSynthesis.AudioIdCopiedDetail", "Audio ID copied to clipboard"));
    }

    private void CopyAudioReferenceToClipboard()
    {
      CopyTextToClipboard(
          LastSynthesizedAudioUrl,
          ResourceHelper.GetString("VoiceSynthesis.AudioReferenceCopied", "Copied"),
          ResourceHelper.GetString("VoiceSynthesis.AudioReferenceCopiedDetail", "Audio reference copied to clipboard"));
    }

    private void CopyTextToClipboard(string? text, string title, string message)
    {
      if (string.IsNullOrWhiteSpace(text))
        return;

      var dataPackage = new Windows.ApplicationModel.DataTransfer.DataPackage();
      dataPackage.SetText(text);
      Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(dataPackage);
      _toastNotificationService?.ShowSuccess(title, message);
    }

    private void OpenOutputLocation()
    {
      if (!TryResolveExistingLocalOutputPath(LastSynthesizedAudioUrl, out var localPath, out var isDirectory) ||
          string.IsNullOrWhiteSpace(localPath))
      {
        return;
      }

      try
      {
        Process.Start(new ProcessStartInfo
        {
          FileName = "explorer.exe",
          Arguments = isDirectory ? $"\"{localPath}\"" : $"/select,\"{localPath}\"",
          UseShellExecute = true
        });
      }
      catch (Exception ex)
      {
        _errorLoggingService?.LogError(ex, "VoiceSynthesis.OpenOutputLocation", new Dictionary<string, object>
        {
          { "Path", localPath }
        });
        _toastNotificationService?.ShowError(
            ResourceHelper.GetString("VoiceSynthesis.OpenOutputLocationFailed", "Unable to Open Output Location"),
            ErrorHandler.GetUserFriendlyMessage(ex));
      }
    }

    private static bool TryResolveExistingLocalOutputPath(string? reference, out string? localPath, out bool isDirectory)
    {
      localPath = null;
      isDirectory = false;

      if (string.IsNullOrWhiteSpace(reference))
        return false;

      var candidate = reference.Trim();
      if (candidate.StartsWith("/api/", StringComparison.OrdinalIgnoreCase) ||
          candidate.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
          candidate.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
      {
        return false;
      }

      if (Uri.TryCreate(candidate, UriKind.Absolute, out var uri))
      {
        if (uri.IsFile)
          candidate = uri.LocalPath;
        else
          return false;
      }
      else if (!Path.IsPathFullyQualified(candidate))
      {
        return false;
      }

      if (File.Exists(candidate))
      {
        localPath = candidate;
        return true;
      }

      if (Directory.Exists(candidate))
      {
        localPath = candidate;
        isDirectory = true;
        return true;
      }

      return false;
    }

    private void RefreshSynthesisResultState()
    {
      OnPropertyChanged(nameof(HasSynthesisResult));
      OnPropertyChanged(nameof(SynthesisResultSummary));
      OnPropertyChanged(nameof(CanCopyAudioId));
      OnPropertyChanged(nameof(CanCopyAudioReference));
      OnPropertyChanged(nameof(CanOpenOutputLocation));
      CopyAudioIdCommand.NotifyCanExecuteChanged();
      CopyAudioReferenceCommand.NotifyCanExecuteChanged();
      OpenOutputLocationCommand.NotifyCanExecuteChanged();
      RefreshPlaybackErrorCommandState();
    }

    private void ClearPlaybackError()
    {
      IsPlaybackError = false;
      PlaybackErrorMessage = null;
      PlaybackErrorDetails = null;
      PlaybackErrorAudioId = null;
      PlaybackErrorAudioReference = null;
      RefreshPlaybackErrorCommandState();
    }

    private void RefreshPlaybackErrorCommandState()
    {
      OnPropertyChanged(nameof(ShowPlaybackError));
      RetryPlaybackCommand.NotifyCanExecuteChanged();
      CopyPlaybackErrorCommand.NotifyCanExecuteChanged();
    }

    /// <summary>Dismisses the playback error callout (e.g. InfoBar closed).</summary>
    public void DismissPlaybackError()
    {
      ClearPlaybackError();
    }

    private void AddRecentSynthesisResult(
        string? audioId,
        string? audioReference,
        double durationSeconds,
        double qualityScore,
        string? profileId,
        string? profileName,
        string? engine)
    {
      if (string.IsNullOrWhiteSpace(audioId) && string.IsNullOrWhiteSpace(audioReference))
        return;

      var result = new VoiceSynthesisRecentResult
      {
        AudioId = string.IsNullOrWhiteSpace(audioId) ? null : audioId,
        AudioReference = string.IsNullOrWhiteSpace(audioReference) ? null : audioReference,
        Duration = TimeSpan.FromSeconds(Math.Max(0, durationSeconds)),
        QualityScore = qualityScore,
        ProfileId = profileId,
        ProfileName = profileName,
        Engine = engine,
        CreatedAtLocal = DateTime.Now,
      };
      RecentSynthesisResults.Insert(0, result);
      while (RecentSynthesisResults.Count > MaxRecentSynthesisResults)
        RecentSynthesisResults.RemoveAt(RecentSynthesisResults.Count - 1);
      NotifyRecentSynthesisResultsChanged();
    }

    private void RemoveRecentResult(VoiceSynthesisRecentResult? item)
    {
      if (item is null)
        return;
      if (!RecentSynthesisResults.Contains(item))
        return;

      if (ReferenceEquals(SelectedRecentResult, item))
        SelectedRecentResult = null;

      RecentSynthesisResults.Remove(item);
      NotifyRecentSynthesisResultsChanged();
    }

    private void ClearRecentResults()
    {
      if (RecentSynthesisResults.Count == 0)
        return;

      RecentSynthesisResults.Clear();
      SelectedRecentResult = null;
      NotifyRecentSynthesisResultsChanged();
    }

    /// <summary>After mutating <see cref="RecentSynthesisResults"/>, keep derived state and command gating in sync.</summary>
    private void NotifyRecentSynthesisResultsChanged()
    {
      OnPropertyChanged(nameof(HasRecentSynthesisResults));
      RestoreRecentResultCommand.NotifyCanExecuteChanged();
      RemoveRecentResultCommand.NotifyCanExecuteChanged();
      ClearRecentResultsCommand.NotifyCanExecuteChanged();
    }

    private void RestoreRecentResult(VoiceSynthesisRecentResult? item)
    {
      if (item is null)
        return;

      LastSynthesizedAudioId = item.AudioId ?? string.Empty;
      LastSynthesizedAudioUrl = item.AudioReference ?? string.Empty;
      LastSynthesizedDuration = item.Duration;
      WorkflowState = SynthesisWorkflowState.AudioReady;
      ClearPlaybackError();
      RefreshSynthesisResultState();
      PlayAudioCommand.NotifyCanExecuteChanged();
      AddToTimelineCommand.NotifyCanExecuteChanged();
    }

    private void CopyPlaybackErrorToClipboard()
    {
      if (string.IsNullOrWhiteSpace(PlaybackErrorMessage))
        return;

      var details = new StringBuilder();
      details.AppendLine($"Playback Error: {PlaybackErrorMessage}");
      if (!string.IsNullOrWhiteSpace(PlaybackErrorDetails))
        details.AppendLine($"Details: {PlaybackErrorDetails}");
      if (!string.IsNullOrWhiteSpace(PlaybackErrorAudioId))
        details.AppendLine($"Audio ID: {PlaybackErrorAudioId}");
      if (!string.IsNullOrWhiteSpace(PlaybackErrorAudioReference))
        details.AppendLine($"Reference: {PlaybackErrorAudioReference}");

      CopyTextToClipboard(
          details.ToString(),
          ResourceHelper.GetString("VoiceSynthesis.PlaybackErrorCopied", "Copied"),
          ResourceHelper.GetString("VoiceSynthesis.PlaybackErrorCopiedDetail", "Playback error details copied to clipboard"));
    }

    private void ClearError()
    {
      ErrorMessage = null;
      HasError = false;
      ClearConsentState();
      UpdateWorkflowStateFromInputs();
    }

    partial void OnSelectedEngineChanged(string value)
    {
      OnPropertyChanged(nameof(IsEmotionSupported));
      if (!IsLoading && WorkflowState != SynthesisWorkflowState.Synthesizing)
        UpdateWorkflowStateFromInputs();
      // Reset emotion if not supported
      if (!IsEmotionSupported)
      {
        Emotion = null;
      }
      // Load pipelines for the selected engine
      LoadPipelinesCommand.NotifyCanExecuteChanged();
      var ct = new CancellationTokenSource(TimeSpan.FromSeconds(30)).Token;
      _ = LoadPipelinesAsync(ct).ContinueWith(t =>
      {
        if (t.IsFaulted)
          _errorLoggingService?.LogError(t.Exception?.InnerException ?? new Exception("LoadPipelines failed"), "LoadPipelines");
      }, TaskScheduler.Default);

      RefreshProfileEngineCompatibility();
    }

    private async Task StartStreamingAsync(CancellationToken cancellationToken)
    {
      if (SelectedProfile == null || string.IsNullOrWhiteSpace(Text))
        return;

      BeginSynthesisOperationNarrativeHygiene();

      try
      {
        IsStreaming = true;
        StreamingStatus = "Connecting...";
        StreamingReceivedChunks = 0;
        StreamingBufferedChunks = 0;

        // Initialize streaming player if needed
        if (_streamingPlayer == null)
        {
          _streamingPlayer = new StreamingAudioPlayer();
          _streamingPlayer.ChunkReceived += OnStreamingChunkReceived;
          _streamingPlayer.StreamingStarted += OnStreamingStarted;
          _streamingPlayer.StreamingStopped += OnStreamingStopped;
          _streamingPlayer.ErrorOccurred += OnStreamingError;
          _streamingPlayer.SynthesisComplete += OnStreamingSynthesisComplete;
        }

        // Build WebSocket URL
        var wsUrl = _backendBaseUrl.Replace("http://", "ws://").Replace("https://", "wss://");
        var streamUrl = $"{wsUrl}/api/voice/synthesize/stream";

        // Build synthesis request
        var request = new
        {
          type = "synthesize",
          engine = SelectedEngine,
          profile_id = SelectedProfile.Id,
          text = Text,
          language = Language,
          chunk_size = 100,
          overlap = 20,
        };

        StreamingStatus = "Starting synthesis...";
        await _streamingPlayer.StartStreamingAsync(streamUrl, request, cancellationToken);
      }
      catch (OperationCanceledException)
      {
        StreamingStatus = "Streaming cancelled";
      }
      catch (Exception ex)
      {
        ErrorMessage = ex.Message;
        HasError = true;
        StreamingStatus = $"Error: {ex.Message}";
        _errorLoggingService?.LogError(ex, "StartStreaming", new Dictionary<string, object>
        {
          { "engine", SelectedEngine },
          { "profile", SelectedProfile?.Id ?? "null" },
        });
      }
      finally
      {
        StartStreamingCommand.NotifyCanExecuteChanged();
        StopStreamingCommand.NotifyCanExecuteChanged();
      }
    }

    private void StopStreaming()
    {
      try
      {
        _ = Task.Run(async () =>
        {
          if (_streamingPlayer != null)
          {
            await _streamingPlayer.StopStreamingAsync();
          }
        });
      }
      catch (Exception ex)
      {
        ErrorLogger.LogWarning($"Error stopping streaming: {ex.Message}", "VoiceSynthesisViewModel");
      }
    }

    private void OnStreamingChunkReceived(object? sender, AudioChunkReceivedEventArgs e)
    {
      var chunkIndex = e.ChunkIndex;
      var player = _streamingPlayer;
      Dispatcher.TryEnqueue(() =>
      {
        StreamingReceivedChunks = chunkIndex + 1;
        StreamingBufferedChunks = player?.BufferedChunks ?? 0;
        StreamingStatus = $"Receiving: {StreamingReceivedChunks} chunks";
      });
    }

    private void OnStreamingStarted(object? sender, EventArgs e)
    {
      Dispatcher.TryEnqueue(() =>
      {
        StreamingStatus = "Streaming audio...";
        StartStreamingCommand.NotifyCanExecuteChanged();
        StopStreamingCommand.NotifyCanExecuteChanged();
      });
    }

    private void OnStreamingStopped(object? sender, EventArgs e)
    {
      Dispatcher.TryEnqueue(() =>
      {
        IsStreaming = false;
        StreamingStatus = "Stopped";
        StartStreamingCommand.NotifyCanExecuteChanged();
        StopStreamingCommand.NotifyCanExecuteChanged();
      });
    }

    private void OnStreamingError(object? sender, StreamingErrorEventArgs e)
    {
      var msg = e.Message;
      var ex = e.Exception;
      _errorLoggingService?.LogError(ex ?? new Exception(msg), "Streaming");
      Dispatcher.TryEnqueue(() =>
      {
        ErrorMessage = msg;
        HasError = true;
        WorkflowState = SynthesisWorkflowState.Error;
        StreamingStatus = $"Error: {msg}";
      });
    }

    private void OnStreamingSynthesisComplete(object? sender, SynthesisCompleteEventArgs e)
    {
      var totalChunks = e.TotalChunks;
      var duration = e.DurationSeconds;
      var engine = e.Engine;
      var toast = _toastNotificationService;
      Dispatcher.TryEnqueue(() =>
      {
        StreamingStatus = $"Complete: {totalChunks} chunks, {duration:F1}s";
        toast?.ShowSuccess(
          $"Synthesis complete ({duration:F1}s)",
          $"Engine: {engine}"
        );
      });
    }

    private async Task PlayAudioAsync(CancellationToken cancellationToken)
    {
      if (string.IsNullOrWhiteSpace(LastSynthesizedAudioUrl) && string.IsNullOrWhiteSpace(LastSynthesizedAudioId))
        return;

      // Own global transport so main Play routes here
      if (!string.IsNullOrEmpty(LastSynthesizedAudioId))
      {
        var ctx = AppServices.TryGetContextManager();
        if (ctx != null)
          ctx.SetCurrentPlayable(LastSynthesizedAudioId, TransportSource.Synthesis, SelectedProfile?.Name ?? "Synthesis");
      }

      IsLoading = true;
      ErrorMessage = null;
      HasError = false;
      ClearPlaybackError();
      StatusMessage = ResourceHelper.GetString("Status.LoadingAudio", "Loading audio for playback...");

      try
      {
        // Prefer using GetAudioStreamAsync if we have an audio ID (more reliable)
        Stream? audioStream = null;
        if (!string.IsNullOrEmpty(LastSynthesizedAudioId))
        {
          try
          {
            audioStream = await _voiceSynthesisService.GetAudioStreamAsync(LastSynthesizedAudioId, cancellationToken);
          }
          catch
          {
            // Fall back to URL-based approach if GetAudioStreamAsync fails
            audioStream = null;
          }
        }

        // If we don't have a stream from audio ID, construct URL from LastSynthesizedAudioUrl
        if (audioStream == null && !string.IsNullOrEmpty(LastSynthesizedAudioUrl))
        {
          var audioUrl = LastSynthesizedAudioUrl;
          if (!Uri.IsWellFormedUriString(audioUrl, UriKind.Absolute))
          {
            // Construct full URL using backend base URL
            var baseUri = new Uri(_backendBaseUrl);
            audioUrl = new Uri(baseUri, audioUrl).ToString();
          }

          // Download audio file from URL (Phase 4: use shared HttpClient)
          var httpClient = AppServices.GetService<System.Net.Http.HttpClient>();
          if (httpClient == null)
            throw new InvalidOperationException("HttpClient not available");
          var audioBytes = await httpClient.GetByteArrayAsync(audioUrl, cancellationToken);

          // Save to temporary file
          var tempPath = Path.Combine(Path.GetTempPath(), $"voicestudio_{System.Guid.NewGuid()}.wav");
          await File.WriteAllBytesAsync(tempPath, audioBytes, cancellationToken);

          StatusMessage = ResourceHelper.GetString("Status.PlayingAudio", "Playing audio...");

          // Play audio file
          await _audioPlayer.PlayFileAsync(tempPath, () =>
          {
            // Cleanup temp file after playback
            try
            {
              if (File.Exists(tempPath))
                File.Delete(tempPath);
            }
            catch (Exception ex) { ErrorLogger.LogWarning($"Best effort operation failed: {ex.Message}", "full.Unknown"); }
          });
          ClearPlaybackError();
        }
        else if (audioStream != null)
        {
          // Use stream from GetAudioStreamAsync
          var tempPath = Path.Combine(Path.GetTempPath(), $"voicestudio_{System.Guid.NewGuid()}.wav");
          await using (var fileStream = File.Create(tempPath))
          {
            await audioStream.CopyToAsync(fileStream, cancellationToken);
          }

          StatusMessage = ResourceHelper.GetString("Status.PlayingAudio", "Playing audio...");

          // Play audio file
          await _audioPlayer.PlayFileAsync(tempPath, () =>
          {
            // Cleanup temp file and stream after playback
            try
            {
              if (File.Exists(tempPath))
                File.Delete(tempPath);
              audioStream?.Dispose();
            }
            catch (Exception ex) { ErrorLogger.LogWarning($"Best effort operation failed: {ex.Message}", "full.Unknown"); }
          });
          ClearPlaybackError();
        }
        else
        {
          throw new InvalidOperationException("No audio source available (neither audio ID nor URL)");
        }
      }
      catch (OperationCanceledException)
      {
        // User cancelled - expected
        StatusMessage = ResourceHelper.GetString("Status.PlaybackCancelled", "Playback cancelled");
        return;
      }
      catch (Exception ex)
      {
        _errorLoggingService?.LogError(ex, "PlayAudio");
        IsPlaybackError = true;
        PlaybackErrorMessage = ErrorHandler.GetUserFriendlyMessage(ex);
        PlaybackErrorDetails = ex.Message;
        PlaybackErrorAudioId = LastSynthesizedAudioId;
        PlaybackErrorAudioReference = LastSynthesizedAudioUrl;
        RefreshPlaybackErrorCommandState();
        StatusMessage = string.Empty;
        _errorService?.ShowError(ex, ResourceHelper.GetString("Error.PlayAudioFailed", "Failed to play audio"));
      }
      finally
      {
        IsLoading = false;
      }
    }

    private void StopAudio()
    {
      try
      {
        _audioPlayer.Stop();
        StatusMessage = ResourceHelper.GetString("Status.PlaybackStopped", "Playback stopped");
      }
      catch (Exception ex)
      {
        _errorLoggingService?.LogError(ex, "StopAudio");
        ErrorMessage = $"Failed to stop playback: {ErrorHandler.GetUserFriendlyMessage(ex)}";
        HasError = true;
      }
    }

    /// <summary>
    /// Send synthesized audio to the Timeline panel as a new clip.
    /// Audit remediation X-6/C.3: Synthesis -> Timeline shortcut.
    /// </summary>
    private void AddSynthesizedAudioToTimeline()
    {
      if (string.IsNullOrEmpty(LastSynthesizedAudioId))
      {
        return;
      }

      var eventAggregator = AppServices.TryGetEventAggregator();
      if (eventAggregator == null)
      {
        _toastNotificationService?.ShowWarning("Timeline", "Event system unavailable");
        return;
      }

      // C.3: Publish AddToTimelineEvent for direct clip addition (Pass 01: include ProfileId)
      var clipName = $"Synthesis - {SelectedProfile?.Name ?? "Unknown"}";
      eventAggregator.Publish(new AddToTimelineEvent(
          PanelId,
          LastSynthesizedAudioId,
          LastSynthesizedAudioUrl ?? "",
          LastSynthesizedDuration,
          clipName,
          targetTrackIndex: null,
          insertPosition: null,
          profileId: SelectedProfile?.Id));

      // Also publish AssetAddedEvent so Library knows
      eventAggregator.Publish(new AssetAddedEvent(
          PanelId,
          LastSynthesizedAudioId,
          "audio"));

      _toastNotificationService?.ShowSuccess("Added to Timeline", $"'{clipName}' sent to Timeline");
    }

    private void OnQualityMetricsUpdated(QualityMetricsUpdatedEventArgs e)
    {
      if (e.SynthesisId == _currentSynthesisId)
      {
        // Update real-time feedback display
        if (RealTimeQualityFeedback != null)
        {
          OnPropertyChanged(nameof(RealTimeQualityFeedback));
        }
      }
    }

    private void OnSynthesisCompleted(SynthesisCompletedEventArgs e)
    {
      if (e.SynthesisId == _currentSynthesisId)
      {
        RealTimeQualityFeedback = e.Feedback;
        OnPropertyChanged(nameof(RealTimeQualityFeedback));
      }
    }

    // Adaptive Quality Optimization Methods (IDEA 53)
    private async Task AnalyzeTextAsync(CancellationToken cancellationToken)
    {
      if (string.IsNullOrWhiteSpace(Text))
        return;

      IsAnalyzingText = true;
      AnalyzeTextCommand.NotifyCanExecuteChanged();
      GetQualityRecommendationCommand.NotifyCanExecuteChanged();

      try
      {
        TextAnalysis = await _textAnalysisService.AnalyzeTextAsync(Text, Language, cancellationToken);
      }
      catch (OperationCanceledException)
      {
        // User cancelled - expected
        return;
      }
      catch (Exception ex)
      {
        _errorLoggingService?.LogError(ex, "AnalyzeText");
        _errorService?.ShowError(ex, ResourceHelper.GetString("VoiceSynthesis.TextAnalysisFailed", "Text analysis failed"));
        _toastNotificationService?.ShowError(
            ResourceHelper.GetString("VoiceSynthesis.TextAnalysisFailed", "Text Analysis Failed"),
            ErrorHandler.GetUserFriendlyMessage(ex)
        );
        TextAnalysis = null;
      }
      finally
      {
        IsAnalyzingText = false;
        AnalyzeTextCommand.NotifyCanExecuteChanged();
        GetQualityRecommendationCommand.NotifyCanExecuteChanged();
      }
    }

    private async Task GetQualityRecommendationAsync(CancellationToken cancellationToken)
    {
      if (string.IsNullOrWhiteSpace(Text))
        return;

      IsAnalyzingText = true;
      AnalyzeTextCommand.NotifyCanExecuteChanged();
      GetQualityRecommendationCommand.NotifyCanExecuteChanged();

      try
      {
        var availableEngines = AvailableEngines.Count > 0
            ? AvailableEngines.ToList()
            : await _enginesClient.GetEnginesAsync(cancellationToken);

        QualityRecommendation = await _textAnalysisService.GetQualityRecommendationAsync(
            Text,
            Language,
            availableEngines,
            null, // No target quality - auto-determine
            cancellationToken
        );

        HasQualityRecommendation = QualityRecommendation != null;

        // Auto-apply if enabled
        if (AutoApplyRecommendations && QualityRecommendation != null)
        {
          ApplyRecommendation();
        }

        ApplyRecommendationCommand.NotifyCanExecuteChanged();
      }
      catch (OperationCanceledException)
      {
        // User cancelled - expected
        return;
      }
      catch (Exception ex)
      {
        _errorLoggingService?.LogError(ex, "GetQualityRecommendation");
        _errorService?.ShowError(ex, ResourceHelper.GetString("VoiceSynthesis.QualityRecommendationFailed", "Quality recommendation failed"));
        _toastNotificationService?.ShowError(
            ResourceHelper.GetString("VoiceSynthesis.QualityRecommendationFailed", "Quality Recommendation Failed"),
            ErrorHandler.GetUserFriendlyMessage(ex)
        );
        QualityRecommendation = null;
        HasQualityRecommendation = false;
      }
      finally
      {
        IsAnalyzingText = false;
        AnalyzeTextCommand.NotifyCanExecuteChanged();
        GetQualityRecommendationCommand.NotifyCanExecuteChanged();
      }
    }

    private void ApplyRecommendation()
    {
      if (QualityRecommendation == null)
        return;

      try
      {
        // Apply recommended settings
        SelectedEngine = QualityRecommendation.RecommendedEngine;
        EnhanceQuality = QualityRecommendation.RecommendedEnhanceQuality;

        _toastNotificationService?.ShowSuccess(
            ResourceHelper.GetString("VoiceSynthesis.RecommendationsApplied", "Recommendations Applied"),
            ResourceHelper.FormatString("VoiceSynthesis.RecommendationsAppliedDetail", QualityRecommendation.RecommendedEngine, QualityRecommendation.RecommendedEnhanceQuality)
        );
      }
      catch (Exception ex)
      {
        _errorLoggingService?.LogError(ex, "ApplyRecommendation");
        _toastNotificationService?.ShowError(
            ResourceHelper.GetString("VoiceSynthesis.ApplyRecommendationsFailed", "Failed to Apply Recommendations"),
            ErrorHandler.GetUserFriendlyMessage(ex)
        );
      }
    }

    // Multi-Engine Ensemble methods (IDEA 55)
    private async Task CreateEnsembleAsync(CancellationToken cancellationToken)
    {
      if (SelectedProfile == null || string.IsNullOrWhiteSpace(Text) || SelectedEngines.Count == 0)
        return;

      IsEnsembleProcessing = true;
      IsLoading = true;
      BeginSynthesisOperationNarrativeHygiene();
      CreateEnsembleCommand.NotifyCanExecuteChanged();
      CheckEnsembleStatusCommand.NotifyCanExecuteChanged();

      try
      {
        var request = new MultiEngineEnsembleRequest
        {
          Text = Text,
          ProfileId = SelectedProfile.Id,
          Engines = SelectedEngines.ToList(),
          Language = Language,
          Emotion = Emotion,
          SelectionMode = EnsembleSelectionMode,
          QualityThreshold = 0.85
        };

        var response = await _ensembleService.CreateMultiEngineEnsembleAsync(request, cancellationToken);

        if (response != null)
        {
          EnsembleJobId = response.JobId;
          HasEnsembleStatus = false;
          EnsembleStatus = null;

          _toastNotificationService?.ShowSuccess(
              "Ensemble Started",
              $"Multi-engine ensemble synthesis started with {SelectedEngines.Count} engine(s)"
          );

          // Start polling for status
          var pollCt = new CancellationTokenSource(TimeSpan.FromMinutes(5)).Token;
          _ = PollEnsembleStatusAsync(pollCt).ContinueWith(t =>
          {
            if (t.IsFaulted)
              _errorLoggingService?.LogError(t.Exception?.InnerException ?? new Exception("PollEnsembleStatus failed"), "PollEnsembleStatus");
          }, TaskScheduler.Default);
        }
      }
      catch (OperationCanceledException)
      {
        // User cancelled - expected
        return;
      }
      catch (Exception ex)
      {
        _errorLoggingService?.LogError(ex, "CreateEnsemble");
        ErrorMessage = ErrorHandler.GetUserFriendlyMessage(ex);
        HasError = true;
        _errorService?.ShowError(ex, ResourceHelper.GetString("VoiceSynthesis.EnsembleFailed", "Ensemble synthesis failed"));
        _toastNotificationService?.ShowError(
            ResourceHelper.GetString("VoiceSynthesis.EnsembleFailed", "Ensemble Failed"),
            ErrorHandler.GetUserFriendlyMessage(ex)
        );
      }
      finally
      {
        IsLoading = false;
        IsEnsembleProcessing = false;
        CreateEnsembleCommand.NotifyCanExecuteChanged();
        CheckEnsembleStatusCommand.NotifyCanExecuteChanged();
      }
    }

    private async Task CheckEnsembleStatusAsync(CancellationToken cancellationToken)
    {
      if (string.IsNullOrEmpty(EnsembleJobId))
        return;

      IsEnsembleProcessing = true;
      CheckEnsembleStatusCommand.NotifyCanExecuteChanged();

      try
      {
        var status = await _ensembleService.GetMultiEngineEnsembleStatusAsync(EnsembleJobId, cancellationToken);

        if (status != null)
        {
          EnsembleStatus = status;
          HasEnsembleStatus = true;

          // If completed, update audio URL and quality metrics
          if (status.Status == "completed" && !string.IsNullOrEmpty(status.EnsembleAudioId))
          {
            LastSynthesizedAudioId = status.EnsembleAudioId;
            LastSynthesizedAudioUrl = $"/api/audio/{status.EnsembleAudioId}";
            WorkflowState = SynthesisWorkflowState.AudioReady;

            // Convert ensemble quality to QualityMetrics if available
            if (status.EnsembleQuality != null)
            {
              QualityMetrics = new QualityMetrics
              {
                MosScore = status.EnsembleQuality.ContainsKey("mos_score")
                      ? Convert.ToDouble(status.EnsembleQuality["mos_score"])
                      : null,
                Similarity = status.EnsembleQuality.ContainsKey("similarity")
                      ? Convert.ToDouble(status.EnsembleQuality["similarity"])
                      : null,
                Naturalness = status.EnsembleQuality.ContainsKey("naturalness")
                      ? Convert.ToDouble(status.EnsembleQuality["naturalness"])
                      : null
              };
              HasQualityMetrics = true;
            }

            var qualityScore = QualityMetrics?.MosScore ?? 0.0;
            LastSynthesizedDuration = TimeSpan.Zero;
            PlayAudioCommand.NotifyCanExecuteChanged();
            AddToTimelineCommand.NotifyCanExecuteChanged();

            var engineLabel = SelectedEngines.Count > 0
                ? string.Join(", ", SelectedEngines)
                : SelectedEngine;
            AddRecentSynthesisResult(
                status.EnsembleAudioId,
                $"/api/audio/{status.EnsembleAudioId}",
                0.0,
                qualityScore,
                SelectedProfile?.Id,
                SelectedProfile?.Name,
                engineLabel);

            _toastNotificationService?.ShowSuccess(
                "Ensemble Complete",
                $"Best engine selected. MOS Score: {qualityScore:F2}"
            );
          }
          else if (status.Status == "failed")
          {
            ErrorMessage = status.Error ?? "Ensemble synthesis failed";
            HasError = true;
            WorkflowState = SynthesisWorkflowState.Error;
            _toastNotificationService?.ShowError(
                "Ensemble Failed",
                status.Error ?? "Ensemble synthesis failed"
            );
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
        _errorLoggingService?.LogError(ex, "CheckEnsembleStatus");
        _errorService?.ShowError(ex, ResourceHelper.GetString("VoiceSynthesis.CheckStatusFailed", "Failed to check ensemble status"));
        _toastNotificationService?.ShowError(
            ResourceHelper.GetString("VoiceSynthesis.CheckStatusFailed", "Failed to Check Status"),
            ErrorHandler.GetUserFriendlyMessage(ex)
        );
      }
      finally
      {
        IsEnsembleProcessing = false;
        CheckEnsembleStatusCommand.NotifyCanExecuteChanged();
      }
    }

    private async Task PollEnsembleStatusAsync(CancellationToken cancellationToken)
    {
      // Poll every 2 seconds until complete or failed
      while (!string.IsNullOrEmpty(EnsembleJobId) && IsEnsembleProcessing)
      {
        cancellationToken.ThrowIfCancellationRequested();
        await Task.Delay(2000, cancellationToken);
        await CheckEnsembleStatusAsync(cancellationToken);

        if (EnsembleStatus?.Status == "completed" || EnsembleStatus?.Status == "failed")
        {
          IsEnsembleProcessing = false;
          break;
        }
      }
    }

    // Helper methods for ensemble
    public void ToggleEngineSelection(string engine)
    {
      if (SelectedEngines.Contains(engine))
      {
        SelectedEngines.Remove(engine);
      }
      else
      {
        if (SelectedEngines.Count < 5)
        {
          SelectedEngines.Add(engine);
        }
      }
      CreateEnsembleCommand.NotifyCanExecuteChanged();
      RefreshProfileEngineCompatibility();
    }

    public bool IsEngineSelected(string engine) => SelectedEngines.Contains(engine);

    // Engine-Specific Quality Pipelines methods (IDEA 58)
    private async Task LoadPipelinesAsync(CancellationToken cancellationToken)
    {
      if (string.IsNullOrEmpty(SelectedEngine))
        return;

      try
      {
        // Get available preset names
        var presetNames = await _qualityPipelineService.ListQualityPipelinePresetsAsync(SelectedEngine, cancellationToken);

        // Convert preset names to QualityPipeline objects by loading each configuration
        AvailablePipelines.Clear();
        foreach (var presetName in presetNames)
        {
          cancellationToken.ThrowIfCancellationRequested();

          try
          {
            var config = await _qualityPipelineService.GetQualityPipelineAsync(SelectedEngine, presetName, cancellationToken);
            if (config != null)
            {
              var pipeline = new QualityPipeline
              {
                EngineId = config.EngineId,
                Name = config.PresetName ?? presetName,
                Description = config.Description ?? string.Empty,
                Steps = config.Steps.Select(s => new VoiceStudio.Core.Models.PipelineStep
                {
                  Name = s,
                  Enabled = true,
                  Parameters = config.Settings.ContainsKey(s) && config.Settings[s] is Dictionary<string, object> dict
                        ? dict
                        : new Dictionary<string, object>()
                }).ToList()
              };
              AvailablePipelines.Add(pipeline);
            }
          }
          catch (Exception ex)
          {
            _errorLoggingService?.LogError(ex, $"LoadPipelinePreset_{presetName}");
            // Continue loading other presets
          }
        }

        // Select default pipeline if available
        if (AvailablePipelines.Count > 0 && SelectedPipeline == null)
        {
          SelectedPipeline = AvailablePipelines.FirstOrDefault(p => p.Name == "default")
              ?? AvailablePipelines.First();
          SelectedPipelinePreset = SelectedPipeline.Name;
        }
      }
      catch (OperationCanceledException)
      {
        // User cancelled - expected
        return;
      }
      catch (Exception ex)
      {
        _errorLoggingService?.LogError(ex, "LoadPipelines");
        _errorService?.ShowError(ex, "Failed to load pipelines");
        _toastNotificationService?.ShowError("Failed to Load Pipelines", ErrorHandler.GetUserFriendlyMessage(ex));
      }
    }

    private async Task PreviewPipelineAsync(CancellationToken cancellationToken)
    {
      if (string.IsNullOrEmpty(LastSynthesizedAudioId) || string.IsNullOrEmpty(SelectedPipelinePreset))
        return;

      IsPreviewingPipeline = true;
      ErrorMessage = null;

      try
      {
        var presetName = SelectedPipelinePreset;
        PipelineConfiguration? config = null;

        // Load pipeline configuration if needed
        if (SelectedPipelineConfig == null && !string.IsNullOrEmpty(presetName))
        {
          try
          {
            config = await _qualityPipelineService.GetQualityPipelineAsync(SelectedEngine, presetName, cancellationToken);
          }
          catch (Exception ex)
      {
        ErrorLogger.LogWarning($"Best effort operation failed: {ex.Message}", "full.PreviewPipelineAsync");
      }
        }
        else
        {
          config = SelectedPipelineConfig;
        }

        PipelinePreview = await _qualityPipelineService.PreviewQualityPipelineAsync(
            LastSynthesizedAudioId,
            SelectedEngine,
            presetName,
            config,
            cancellationToken
        );

        if (PipelinePreview != null)
        {
          // Update audio URL if enhanced audio ID is available
          if (!string.IsNullOrEmpty(PipelinePreview.EnhancedAudioId))
          {
            LastSynthesizedAudioId = PipelinePreview.EnhancedAudioId;
            LastSynthesizedAudioUrl = $"/api/audio/{PipelinePreview.EnhancedAudioId}";
            WorkflowState = SynthesisWorkflowState.AudioReady;
            PlayAudioCommand.NotifyCanExecuteChanged();
          }

          _toastNotificationService?.ShowSuccess(
              ResourceHelper.GetString("VoiceSynthesis.PipelinePreview", "Pipeline Preview"),
              ResourceHelper.GetString("VoiceSynthesis.PreviewGenerated", "Preview generated successfully"));
        }
      }
      catch (OperationCanceledException)
      {
        // User cancelled - expected
        return;
      }
      catch (Exception ex)
      {
        _errorLoggingService?.LogError(ex, "PreviewPipeline");
        ErrorMessage = ErrorHandler.GetUserFriendlyMessage(ex);
        _errorService?.ShowError(ex, ResourceHelper.GetString("VoiceSynthesis.PreviewFailed", "Failed to preview pipeline"));
        _toastNotificationService?.ShowError(
            ResourceHelper.GetString("VoiceSynthesis.PreviewFailed", "Preview Failed"),
            ErrorHandler.GetUserFriendlyMessage(ex));
      }
      finally
      {
        IsPreviewingPipeline = false;
      }
    }

    private async Task ComparePipelineAsync(CancellationToken cancellationToken)
    {
      if (string.IsNullOrEmpty(LastSynthesizedAudioId) || string.IsNullOrEmpty(SelectedPipelinePreset))
        return;

      IsPreviewingPipeline = true;
      ErrorMessage = null;

      try
      {
        var presetName = SelectedPipelinePreset;

        PipelineComparison = await _qualityPipelineService.CompareQualityPipelineAsync(
            LastSynthesizedAudioId,
            SelectedEngine,
            presetName,
            cancellationToken
        );

        HasPipelineComparison = PipelineComparison != null;

        if (HasPipelineComparison)
        {
          _toastNotificationService?.ShowSuccess(
              ResourceHelper.GetString("VoiceSynthesis.PipelineComparison", "Pipeline Comparison"),
              ResourceHelper.GetString("VoiceSynthesis.ComparisonCompleted", "Comparison completed"));
        }
      }
      catch (OperationCanceledException)
      {
        // User cancelled - expected
        return;
      }
      catch (Exception ex)
      {
        _errorLoggingService?.LogError(ex, "ComparePipeline");
        ErrorMessage = ErrorHandler.GetUserFriendlyMessage(ex);
        _errorService?.ShowError(ex, ResourceHelper.GetString("VoiceSynthesis.ComparisonFailed", "Failed to compare pipeline"));
        _toastNotificationService?.ShowError(
            ResourceHelper.GetString("VoiceSynthesis.ComparisonFailed", "Comparison Failed"),
            ErrorHandler.GetUserFriendlyMessage(ex));
      }
      finally
      {
        IsPreviewingPipeline = false;
      }
    }

    private async Task<Stream?> LoadAudioStreamAsync(string url)
    {
      try
      {
        if (Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
          var client = AppServices.GetService<System.Net.Http.HttpClient>();
          if (client == null)
            return null;
          var response = await client.GetAsync(uri);
          if (response.IsSuccessStatusCode)
          {
            var stream = new MemoryStream();
            await response.Content.CopyToAsync(stream);
            stream.Position = 0;
            return stream;
          }
        }
        else if (System.IO.File.Exists(url))
        {
          return System.IO.File.OpenRead(url);
        }
      }
      catch (Exception ex)
      {
        ErrorLogger.LogWarning($"Best effort operation failed: {ex.Message}", "full.Task");
      }
      return null;
    }

    private async Task<string> SavePreviewAudioAsync(byte[] audioData)
    {
      // Save to temp file
      var tempPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"voicestudio_preview_{Guid.NewGuid()}.wav");
      await System.IO.File.WriteAllBytesAsync(tempPath, audioData);
      return tempPath;
    }

    /// <summary>JSON shape for <see cref="CustomKeyRecentResults"/> (panel layout persistence).</summary>
    private sealed class RecentResultPersistDto
    {
      public string? AudioId { get; set; }
      public string? AudioReference { get; set; }
      public double DurationSeconds { get; set; }
      public double QualityScore { get; set; }
      public string? ProfileId { get; set; }
      public string? ProfileName { get; set; }
      public string? Engine { get; set; }
      public string? CreatedAtUtc { get; set; }
    }

    #region IPanelStatePersistable (GAP-050 state hygiene)

    /// <inheritdoc />
    public PanelStateData? GetCurrentState()
    {
      try
      {
        var state = new PanelStateData
        {
          PanelId = PanelId,
          SelectedItemId = SelectedProfile?.Id,
          CapturedAt = DateTime.UtcNow,
          CustomData = new Dictionary<string, object>()
        };

        if (!string.IsNullOrEmpty(SelectedEngine))
          state.CustomData[CustomKeySelectedEngine] = SelectedEngine;
        if (!string.IsNullOrEmpty(Emotion))
          state.CustomData[CustomKeyEmotionPreset] = Emotion!;
        state.CustomData[CustomKeyAdvancedControlsExpanded] = IsAdvancedSynthesisControlsExpanded;

        if (RecentSynthesisResults.Count > 0)
        {
          var dtos = RecentSynthesisResults.Select(r => new RecentResultPersistDto
          {
            AudioId = r.AudioId,
            AudioReference = r.AudioReference,
            DurationSeconds = r.Duration.TotalSeconds,
            QualityScore = r.QualityScore,
            ProfileId = r.ProfileId,
            ProfileName = r.ProfileName,
            Engine = r.Engine,
            CreatedAtUtc = r.CreatedAtLocal.ToUniversalTime()
                .ToString("O", System.Globalization.CultureInfo.InvariantCulture),
          }).ToList();
          state.CustomData[CustomKeyRecentResults] = JsonSerializer.Serialize(dtos);
        }

        return state;
      }
      catch (Exception ex)
      {
        System.Diagnostics.Debug.WriteLine($"VoiceSynthesisViewModel.GetCurrentState failed: {ex.Message}");
        return null;
      }
    }

    /// <inheritdoc />
    public Task RestoreStateAsync(PanelStateData state, CancellationToken cancellationToken = default)
    {
      if (state == null)
        return Task.CompletedTask;

      try
      {
        _pendingRestoreProfileId = state.SelectedItemId;
        _pendingRestoreEngine = null;
        _pendingRestoreHasEmotionKey = false;
        _pendingRestoreEmotionRaw = null;

        if (state.CustomData != null)
        {
          if (state.CustomData.TryGetValue(CustomKeyAdvancedControlsExpanded, out var advObj) && advObj != null)
          {
            try
            {
              IsAdvancedSynthesisControlsExpanded = System.Convert.ToBoolean(advObj, System.Globalization.CultureInfo.InvariantCulture);
            }
            catch (InvalidCastException ex)
            {
              System.Diagnostics.Debug.WriteLine($"VoiceSynthesisViewModel.RestoreStateAsync advanced expanded: {ex.Message}");
            }
            catch (FormatException ex)
            {
              System.Diagnostics.Debug.WriteLine($"VoiceSynthesisViewModel.RestoreStateAsync advanced expanded: {ex.Message}");
            }
          }
          if (state.CustomData.TryGetValue(CustomKeySelectedEngine, out var engObj))
          {
            var engStr = CoerceCustomStateString(engObj);
            if (!string.IsNullOrWhiteSpace(engStr))
              _pendingRestoreEngine = engStr.Trim();
          }
          if (state.CustomData.TryGetValue(CustomKeyEmotionPreset, out var emoObj))
          {
            _pendingRestoreHasEmotionKey = true;
            _pendingRestoreEmotionRaw = CoerceCustomStateString(emoObj);
          }
          if (state.CustomData.TryGetValue(CustomKeyRecentResults, out var recentObj))
            RestoreRecentResultsFromCustomData(recentObj);
        }

        TryCompletePendingPanelRestore();
      }
      catch (Exception ex)
      {
        System.Diagnostics.Debug.WriteLine($"VoiceSynthesisViewModel.RestoreStateAsync failed: {ex.Message}");
      }

      return Task.CompletedTask;
    }

    private void RestoreRecentResultsFromCustomData(object? value)
    {
      try
      {
        var json = CoerceCustomStateString(value);
        if (string.IsNullOrWhiteSpace(json))
          return;

        var dtos = JsonSerializer.Deserialize<List<RecentResultPersistDto>>(json);
        if (dtos == null)
          return;

        RecentSynthesisResults.Clear();
        var count = 0;
        foreach (var dto in dtos)
        {
          if (count >= MaxRecentSynthesisResults)
            break;
          if (string.IsNullOrWhiteSpace(dto.AudioId) && string.IsNullOrWhiteSpace(dto.AudioReference))
            continue;

          DateTime createdLocal;
          if (!string.IsNullOrWhiteSpace(dto.CreatedAtUtc) &&
              DateTime.TryParse(
                  dto.CreatedAtUtc,
                  System.Globalization.CultureInfo.InvariantCulture,
                  System.Globalization.DateTimeStyles.RoundtripKind,
                  out var parsed))
            createdLocal = parsed.ToLocalTime();
          else
            createdLocal = DateTime.Now;

          RecentSynthesisResults.Add(new VoiceSynthesisRecentResult
          {
            AudioId = string.IsNullOrWhiteSpace(dto.AudioId) ? null : dto.AudioId,
            AudioReference = string.IsNullOrWhiteSpace(dto.AudioReference) ? null : dto.AudioReference,
            Duration = TimeSpan.FromSeconds(Math.Max(0, dto.DurationSeconds)),
            QualityScore = dto.QualityScore,
            ProfileId = dto.ProfileId,
            ProfileName = dto.ProfileName,
            Engine = dto.Engine,
            CreatedAtLocal = createdLocal,
          });
          count++;
        }

        NotifyRecentSynthesisResultsChanged();
      }
      catch (JsonException ex)
      {
        System.Diagnostics.Debug.WriteLine(
            $"VoiceSynthesisViewModel.RestoreRecentResults: malformed JSON ignored: {ex.Message}");
      }
      catch (Exception ex)
      {
        System.Diagnostics.Debug.WriteLine(
            $"VoiceSynthesisViewModel.RestoreRecentResults: unexpected error: {ex.Message}");
      }
    }

    private static string? CoerceCustomStateString(object? value)
    {
      if (value == null)
        return null;
      if (value is string s)
        return s;
      if (value is System.Text.Json.JsonElement je)
      {
        if (je.ValueKind is System.Text.Json.JsonValueKind.String or System.Text.Json.JsonValueKind.Null)
        {
          if (je.ValueKind == System.Text.Json.JsonValueKind.Null)
            return null;
          return je.GetString();
        }
        return je.GetRawText();
      }
      return Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture);
    }

    /// <summary>Applies deferred workspace restore once profiles/engines are available.</summary>
    private void TryCompletePendingPanelRestore()
    {
      var hasWork = !string.IsNullOrEmpty(_pendingRestoreProfileId) ||
                    !string.IsNullOrEmpty(_pendingRestoreEngine) ||
                    _pendingRestoreHasEmotionKey;
      if (!hasWork)
        return;

      _suppressEmotionClearForPanelRestore = true;
      try
      {
        if (!string.IsNullOrEmpty(_pendingRestoreEngine) && AvailableEngines.Count > 0)
        {
          var eng = _pendingRestoreEngine!;
          SelectedEngine = AvailableEngines.Contains(eng) ? eng : AvailableEngines[0];
          _pendingRestoreEngine = null;
        }

        if (!string.IsNullOrEmpty(_pendingRestoreProfileId) && Profiles.Count > 0)
        {
          var profile = Profiles.FirstOrDefault(p => p.Id == _pendingRestoreProfileId);
          if (profile != null)
          {
            SelectedProfile = profile;
            _pendingRestoreProfileId = null;
          }
        }

        if (_pendingRestoreHasEmotionKey)
        {
          Emotion = NormalizeCanonicalEmotionPreset(_pendingRestoreEmotionRaw);
          _pendingRestoreHasEmotionKey = false;
          _pendingRestoreEmotionRaw = null;
        }
      }
      finally
      {
        _suppressEmotionClearForPanelRestore = false;
      }
    }

    #endregion

    protected override void Dispose(bool disposing)
    {
      if (IsDisposed)
        return;

      if (disposing)
      {
        // Unsubscribe from quality service events
        if (_qualityService != null)
        {
          if (_qualityMetricsUpdatedHandler != null)
            _qualityService.QualityMetricsUpdated -= _qualityMetricsUpdatedHandler;
          if (_synthesisCompletedHandler != null)
            _qualityService.SynthesisCompleted -= _synthesisCompletedHandler;
        }

        // Unsubscribe from audio player events
        if (_audioPlayer != null)
        {
          if (_isPlayingChangedHandler != null)
            _audioPlayer.IsPlayingChanged -= _isPlayingChangedHandler;
          if (_playbackCompletedHandler != null)
            _audioPlayer.PlaybackCompleted -= _playbackCompletedHandler;
        }

        // Clear collections
        Profiles.Clear();
        SelectedEngines.CollectionChanged -= _selectedEnginesCollectionChangedHandler;
      }

      base.Dispose(disposing);
    }
  }
}