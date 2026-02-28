using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Xaml.Media;
using VoiceStudio.Core.Events;
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
  // GAP-005: Updated to inherit from BaseViewModel for standardized error handling
  public partial class VoiceSynthesisViewModel : BaseViewModel, IPanelView
  {
    public string PanelId => "voice_synthesis";
    public string DisplayName => ResourceHelper.GetString("Panel.VoiceSynthesis.DisplayName", "Voice Synthesis");
    public PanelRegion Region => PanelRegion.Center;
    private readonly IBackendClient _backendClient;
    private readonly IAudioPlayerService _audioPlayer;
    private readonly RealTimeQualityService? _qualityService;
    private readonly IErrorLoggingService? _errorLoggingService;
    private readonly IErrorDialogService? _errorDialogService;
    private readonly ToastNotificationService? _toastNotificationService;
    private readonly IErrorPresentationService? _errorService;
    private readonly string _backendBaseUrl;
    private StreamingAudioPlayer? _streamingPlayer;
    private string? _currentSynthesisId;

    // Store event handlers for proper unsubscription
    private EventHandler<bool>? _isPlayingChangedHandler;
    private EventHandler? _playbackCompletedHandler;
    private EventHandler<QualityMetricsUpdatedEventArgs>? _qualityMetricsUpdatedHandler;
    private EventHandler<SynthesisCompletedEventArgs>? _synthesisCompletedHandler;

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

    [ObservableProperty]
    private bool streamingMode;

    [ObservableProperty]
    private bool isStreaming;

    [ObservableProperty]
    private int streamingBufferedChunks;

    [ObservableProperty]
    private int streamingReceivedChunks;

    [ObservableProperty]
    private string streamingStatus = string.Empty;

    [ObservableProperty]
    private bool isLoading;

    [ObservableProperty]
    private string statusMessage = string.Empty;

    [ObservableProperty]
    private string? errorMessage;

    [ObservableProperty]
    private bool hasError;

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

    [ObservableProperty]
    private bool canPlayAudio;

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
    private ObservableCollection<string> availableEngines = new() { "xtts_v2", "chatterbox", "tortoise" };

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

    public VoiceSynthesisViewModel(IBackendClient backendClient, IAudioPlayerService audioPlayer)
        : base(AppServices.GetViewModelContext())
    {
      _backendClient = backendClient ?? throw new ArgumentNullException(nameof(backendClient));
      _audioPlayer = audioPlayer ?? throw new ArgumentNullException(nameof(audioPlayer));

      // Get backend base URL - try to get from local settings, otherwise use default
      // This matches the default in ServiceProvider and BackendClientConfig
      _backendBaseUrl = "http://localhost:8001";
      try
      {
        // Use UnpackagedSettingsHelper for file-based settings (works for both packaged and unpackaged apps)
        var backendJson = UnpackagedSettingsHelper.GetValue<string>("Settings.Backend", null);
        if (!string.IsNullOrEmpty(backendJson))
        {
          using var doc = System.Text.Json.JsonDocument.Parse(backendJson);
          if (doc.RootElement.TryGetProperty("ApiUrl", out var apiUrlElement))
          {
            var apiUrl = apiUrlElement.GetString();
            if (!string.IsNullOrEmpty(apiUrl))
            {
              _backendBaseUrl = apiUrl;
            }
          }
        }
      }
      catch (Exception ex)
      {
        ErrorLogger.LogWarning($"Best effort operation failed: {ex.Message}", "VoiceSynthesisViewModel.Unknown");
      }

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
      }, () => CanPlayAudio && !IsLoading);

      StopAudioCommand = new RelayCommand(StopAudio, () => _audioPlayer.IsPlaying);

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

      // Load profiles on initialization
      var loadCt = new CancellationTokenSource(TimeSpan.FromSeconds(30)).Token;
      _ = LoadProfilesAsync(loadCt).ContinueWith(t =>
      {
        if (t.IsFaulted)
          _errorLoggingService?.LogError(t.Exception?.InnerException ?? new Exception("LoadProfiles failed"), "LoadProfiles");
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
      _isPlayingChangedHandler = (_, _) => PlayAudioCommand.NotifyCanExecuteChanged();
      _playbackCompletedHandler = (_, _) => PlayAudioCommand.NotifyCanExecuteChanged();
      _audioPlayer.IsPlayingChanged += _isPlayingChangedHandler;
      _audioPlayer.PlaybackCompleted += _playbackCompletedHandler;
    }

    public EnhancedAsyncRelayCommand SynthesizeCommand { get; }
    public EnhancedAsyncRelayCommand LoadProfilesCommand { get; }
    public EnhancedAsyncRelayCommand PlayAudioCommand { get; }
    public IRelayCommand StopAudioCommand { get; }
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

    public bool CanSynthesize =>
        SelectedProfile != null &&
        !string.IsNullOrWhiteSpace(Text) &&
        !IsLoading;

    public bool IsEmotionSupported =>
        SelectedEngine == "chatterbox" || SelectedEngine == "xtts";

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

      try
      {
        var profilesList = await _backendClient.GetProfilesAsync(cancellationToken);

        Profiles.Clear();
        foreach (var profile in profilesList)
        {
          Profiles.Add(profile);
        }

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
      ErrorMessage = null;
      HasError = false;
      HasQualityMetrics = false;
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
          EnhanceQuality = EnhanceQuality
        };

        // Update progress estimate (synthesis starting)
        if (_qualityService != null && _currentSynthesisId != null)
        {
          _qualityService.UpdateMetrics(_currentSynthesisId, 0.0, null, 0.5);
        }

        SynthesizeCommand.ReportProgress(25);
        var response = await _backendClient.SynthesizeVoiceAsync(request, cancellationToken);

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

        // Store audio URL, ID, and duration for playback and timeline
        LastSynthesizedAudioUrl = response.AudioUrl;
        LastSynthesizedAudioId = response.AudioId;
        LastSynthesizedDuration = TimeSpan.FromSeconds(response.Duration);
        CanPlayAudio = !string.IsNullOrWhiteSpace(LastSynthesizedAudioUrl);
        PlayAudioCommand.NotifyCanExecuteChanged();
        AddToTimelineCommand.NotifyCanExecuteChanged();

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
      }
      catch (OperationCanceledException)
      {
        // User cancelled - expected
        StatusMessage = ResourceHelper.GetString("Status.SynthesisCancelled", "Synthesis cancelled");
        return;
      }
      catch (Exception ex)
      {
        _errorLoggingService?.LogError(ex, "VoiceSynthesis", new Dictionary<string, object>
                {
                    { "Engine", SelectedEngine },
                    { "ProfileId", SelectedProfile?.Id ?? "unknown" },
                    { "TextLength", Text?.Length ?? 0 }
                });
        var errorMsg = ErrorHandler.GetUserFriendlyMessage(ex);
        ErrorMessage = $"Synthesis failed: {errorMsg}";
        HasError = true;
        StatusMessage = string.Empty;

        _errorService?.ShowError(ex, ResourceHelper.GetString("Timeline.SynthesisFailed", "Failed to synthesize voice"));
        _toastNotificationService?.ShowError(
            ResourceHelper.FormatString("VoiceSynthesis.SynthesisFailedDetail", errorMsg),
            ResourceHelper.GetString("VoiceSynthesis.SynthesisFailed", "Synthesis Failed"));
        await (_errorDialogService?.ShowErrorAsync(ex, ResourceHelper.GetString("Panel.VoiceSynthesis.DisplayName", "Voice Synthesis")) ?? Task.CompletedTask);
      }
      finally
      {
        IsLoading = false;
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
        await _backendClient.StoreQualityHistoryAsync(request, cancellationToken);
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
      SynthesizeCommand.NotifyCanExecuteChanged();
    }

    partial void OnTextChanged(string value)
    {
      SynthesizeCommand.NotifyCanExecuteChanged();
    }

    partial void OnIsLoadingChanged(bool value)
    {
      SynthesizeCommand.NotifyCanExecuteChanged();
      AddToTimelineCommand.NotifyCanExecuteChanged(); // GAP-B04
    }

    partial void OnSelectedEngineChanged(string value)
    {
      OnPropertyChanged(nameof(IsEmotionSupported));
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
    }

    private async Task StartStreamingAsync(CancellationToken cancellationToken)
    {
      if (SelectedProfile == null || string.IsNullOrWhiteSpace(Text))
        return;

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
      StreamingReceivedChunks = e.ChunkIndex + 1;
      StreamingBufferedChunks = _streamingPlayer?.BufferedChunks ?? 0;
      StreamingStatus = $"Receiving: {StreamingReceivedChunks} chunks";
    }

    private void OnStreamingStarted(object? sender, EventArgs e)
    {
      StreamingStatus = "Streaming audio...";
      StartStreamingCommand.NotifyCanExecuteChanged();
      StopStreamingCommand.NotifyCanExecuteChanged();
    }

    private void OnStreamingStopped(object? sender, EventArgs e)
    {
      IsStreaming = false;
      StreamingStatus = "Stopped";
      StartStreamingCommand.NotifyCanExecuteChanged();
      StopStreamingCommand.NotifyCanExecuteChanged();
    }

    private void OnStreamingError(object? sender, StreamingErrorEventArgs e)
    {
      ErrorMessage = e.Message;
      HasError = true;
      StreamingStatus = $"Error: {e.Message}";
      _errorLoggingService?.LogError(e.Exception ?? new Exception(e.Message), "Streaming");
    }

    private void OnStreamingSynthesisComplete(object? sender, SynthesisCompleteEventArgs e)
    {
      StreamingStatus = $"Complete: {e.TotalChunks} chunks, {e.DurationSeconds:F1}s";
      _toastNotificationService?.ShowSuccess(
        $"Synthesis complete ({e.DurationSeconds:F1}s)",
        $"Engine: {e.Engine}"
      );
    }

    private async Task PlayAudioAsync(CancellationToken cancellationToken)
    {
      if (string.IsNullOrWhiteSpace(LastSynthesizedAudioUrl))
        return;

      IsLoading = true;
      ErrorMessage = null;
      HasError = false;
      StatusMessage = ResourceHelper.GetString("Status.LoadingAudio", "Loading audio for playback...");

      try
      {
        // Prefer using GetAudioStreamAsync if we have an audio ID (more reliable)
        Stream? audioStream = null;
        if (!string.IsNullOrEmpty(LastSynthesizedAudioId))
        {
          try
          {
            audioStream = await _backendClient.GetAudioStreamAsync(LastSynthesizedAudioId, cancellationToken);
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

          // Download audio file from URL
          using var httpClient = new System.Net.Http.HttpClient();
          var audioBytes = await httpClient.GetByteArrayAsync(audioUrl, cancellationToken);

          // Save to temporary file
          var tempPath = Path.Combine(Path.GetTempPath(), $"voicestudio_{System.Guid.NewGuid()}.wav");
          await File.WriteAllBytesAsync(tempPath, audioBytes, cancellationToken);

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
        }
        else if (audioStream != null)
        {
          // Use stream from GetAudioStreamAsync
          var tempPath = Path.Combine(Path.GetTempPath(), $"voicestudio_{System.Guid.NewGuid()}.wav");
          await using (var fileStream = File.Create(tempPath))
          {
            await audioStream.CopyToAsync(fileStream, cancellationToken);
          }

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
        }
        else
        {
          throw new InvalidOperationException("No audio source available (neither audio ID nor URL)");
        }

        StatusMessage = ResourceHelper.GetString("Status.PlayingAudio", "Playing audio...");
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
        ErrorMessage = ErrorHandler.GetUserFriendlyMessage(ex);
        HasError = true;
        StatusMessage = string.Empty;
        _errorService?.ShowError(ex, ResourceHelper.GetString("Error.PlayAudioFailed", "Failed to play audio"));
        await (_errorDialogService?.ShowErrorAsync(ex, ResourceHelper.GetString("VoiceSynthesis.AudioPlayback", "Audio Playback")) ?? Task.CompletedTask);
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

      // C.3: Publish AddToTimelineEvent for direct clip addition
      var clipName = $"Synthesis - {SelectedProfile?.Name ?? "Unknown"}";
      eventAggregator.Publish(new AddToTimelineEvent(
          PanelId,
          LastSynthesizedAudioId,
          LastSynthesizedAudioUrl ?? "",
          LastSynthesizedDuration,
          clipName));

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
        TextAnalysis = await _backendClient.AnalyzeTextAsync(Text, Language, cancellationToken);
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
        // Get available engines (typically xtts, chatterbox, tortoise)
        var availableEngines = new List<string> { "xtts", "chatterbox", "tortoise" };

        QualityRecommendation = await _backendClient.GetQualityRecommendationAsync(
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
      ErrorMessage = null;
      HasError = false;
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

        var response = await _backendClient.CreateMultiEngineEnsembleAsync(request, cancellationToken);

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
        var status = await _backendClient.GetMultiEngineEnsembleStatusAsync(EnsembleJobId, cancellationToken);

        if (status != null)
        {
          EnsembleStatus = status;
          HasEnsembleStatus = true;

          // If completed, update audio URL and quality metrics
          if (status.Status == "completed" && !string.IsNullOrEmpty(status.EnsembleAudioId))
          {
            LastSynthesizedAudioUrl = $"/api/audio/{status.EnsembleAudioId}";
            CanPlayAudio = true;

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
            _toastNotificationService?.ShowSuccess(
                "Ensemble Complete",
                $"Best engine selected. MOS Score: {qualityScore:F2}"
            );
          }
          else if (status.Status == "failed")
          {
            ErrorMessage = status.Error ?? "Ensemble synthesis failed";
            HasError = true;
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
        var presetNames = await _backendClient.ListQualityPipelinePresetsAsync(SelectedEngine, cancellationToken);

        // Convert preset names to QualityPipeline objects by loading each configuration
        AvailablePipelines.Clear();
        foreach (var presetName in presetNames)
        {
          cancellationToken.ThrowIfCancellationRequested();

          try
          {
            var config = await _backendClient.GetQualityPipelineAsync(SelectedEngine, presetName, cancellationToken);
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
            config = await _backendClient.GetQualityPipelineAsync(SelectedEngine, presetName, cancellationToken);
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

        PipelinePreview = await _backendClient.PreviewQualityPipelineAsync(
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
            CanPlayAudio = true;
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

        PipelineComparison = await _backendClient.CompareQualityPipelineAsync(
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
          using var client = new System.Net.Http.HttpClient();
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
      }

      base.Dispose(disposing);
    }
  }
}