using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.UI.Dispatching;
using VoiceStudio.Core.Panels;
using VoiceStudio.Core.Plugins;
using VoiceStudio.Core.Services;
using VoiceStudio.Core.State;
using VoiceStudio.App.Core.Commands;
using VoiceStudio.App.Core.Services;
using VoiceStudio.App.Services.Stores;
using VoiceStudio.App.UseCases;
using VoiceStudio.App.Utilities;
using VoiceStudio.App.ViewModels;

namespace VoiceStudio.App.Services
{
  /// <summary>
  /// Static DI facade used by ServiceProvider shim and Views/ViewModels.
  /// Initialize() must be called at app startup (e.g. from App constructor via ServiceProvider.Initialize()).
  /// </summary>
  /// <remarks>
  /// <para><b>Registration order (do not reorder lightly):</b> Core infrastructure (config, HttpClient, correlation,
  /// metrics, coordinator, degradation) → BackendClient + domain facades → panel services → UI services.
  /// BackendClient builds its own <c>HttpClient</c> with handler chain; the singleton <c>HttpClient</c> here is for
  /// probes and other call sites — not the same instance as <see cref="IBackendClient"/>.</para>
  /// <para><b>Cluster boundaries (readability only; no split until a transport/DI extraction requires it):</b>
  /// See <c>docs/design/APPSERVICES_SPLIT_PLAN.md</c> — Group A core domain, B timeline, C synthesis/quality,
  /// D panel facades, E integration, F realtime, G stores, H utilities.</para>
  /// </remarks>
  public static class AppServices
  {
    private static IServiceProvider? _provider;
    private static ToastNotificationService? _toastOverride;
    private static CompletionOsNotificationService? _completionOsNotification;

    /// <summary>
    /// Elapsed milliseconds for PanelRegistry initialization (RegisterAllPanels).
    /// Used by StartupDiagnostics for panel_load_ms in startup_diagnostics.json.
    /// </summary>
    public static double PanelRegistrationMs { get; private set; }

    /// <summary>
    /// Total WinUI app startup time (constructor to window ready).
    /// Set from App.xaml.cs after _startupProfiler finishes; used for startup_ms in startup_diagnostics.json.
    /// </summary>
    public static double AppStartupMs { get; set; }

    /// <summary>
    /// Sets the root service provider (e.g. from a built ServiceCollection).
    /// </summary>
    public static void Initialize(IServiceProvider provider)
    {
      _provider = provider ?? throw new ArgumentNullException(nameof(provider));
    }

    /// <summary>
    /// Builds a minimal DI container and sets it as the provider.
    /// Called by ServiceProvider.Initialize() when no external provider is supplied.
    /// </summary>
    public static void Initialize()
    {
      var services = new ServiceCollection();
      // Order: infrastructure first — facades depend on BackendClientConfig, ICorrelationIdProvider, IRequestCoordinator, etc.
      RegisterCoreInfrastructure(services);
      RegisterBackendFacades(services);
      RegisterPanelServices(services);
      RegisterUIServices(services);

      _provider = services.BuildServiceProvider();

      // Wire up command queue service to registry (GAP-B12)
      WireCommandQueueService();

      // Register all panels after services are ready
      RegisterAllPanels();
    }

    /// <summary>
    /// Registers core infrastructure: correlation, config, metrics, coordinator, graceful degradation, HttpClient.
    /// Must run before RegisterBackendFacades.
    /// </summary>
    private static void RegisterCoreInfrastructure(IServiceCollection services)
    {
      // GAP-I12: Correlation ID provider for cross-layer request tracing
      services.AddSingleton<ICorrelationIdProvider, CorrelationIdProvider>();

      services.AddSingleton(BackendClientConfig.FromEnvironment());
      services.AddSingleton<IRequestMetricsService, RequestMetricsService>();
      services.AddSingleton<IRequestCoordinator, RequestCoordinator>();
      services.AddSingleton<GracefulDegradationService>();
      services.AddSingleton<HttpClient>(_ =>
      {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        client.DefaultRequestHeaders.Add("User-Agent", "VoiceStudio-Quantum-Plus/1.0");
        return client;
      });

      // GAP-058: optional unified auth for WebSocket handshake headers (API key / Bearer).
      services.AddSingleton<IUnifiedAuthService>(sp => new AuthService(sp.GetService<HttpClient>()));
    }

    /// <summary>
    /// Registers BackendClient and all domain facades (ProfilesClient, ProjectsClient, Timeline services, etc.).
    /// Must run after RegisterCoreInfrastructure.
    /// </summary>
    private static void RegisterBackendFacades(IServiceCollection services)
    {
      // PR-3: Shared BackendHttpContext for BackendClient and PluginHealthClient (same retry/circuit policy)
      services.AddSingleton<BackendHttpContext>(sp => new BackendHttpContext(
        sp.GetRequiredService<BackendClientConfig>(),
        sp.GetRequiredService<ICorrelationIdProvider>(),
        sp.GetRequiredService<IRequestMetricsService>(),
        sp.GetService<GracefulDegradationService>()));

      services.AddSingleton<IBackendClient>(sp => new BackendClient(
        sp.GetRequiredService<BackendHttpContext>(),
        sp.GetRequiredService<BackendClientConfig>(),
        sp.GetRequiredService<IRequestCoordinator>(),
        sp.GetService<IUnifiedAuthService>()));

      // Profiles domain facade (Block 4.3)
      services.AddSingleton<IProfilesClient, ProfilesClient>();

      // Projects domain facade (Block 8.2)
      services.AddSingleton<IProjectsClient, ProjectsClient>();

      // Timeline clip facade (Task 10.1 hardening)
      services.AddSingleton<ITimelineClipService, TimelineClipService>();

      // Timeline track facade (Timeline hardening Phase 1)
      services.AddSingleton<ITimelineTrackService, TimelineTrackService>();

      // Timeline transcription facade (Timeline hardening Phase 2)
      services.AddSingleton<ITimelineTranscriptionService, TimelineTranscriptionService>();

      // Timeline synthesis facade (Post-Timeline Hardening 1B)
      services.AddSingleton<ITimelineSynthesisService, TimelineSynthesisService>();

      // A/B test facade (Phase 4 Post-Timeline 4A.1)
      services.AddSingleton<IABTestService, ABTestService>();

      // Voice synthesis panel facade (Phase 4 Post-Timeline 4A.2 Phase A)
      services.AddSingleton<IVoiceSynthesisService, VoiceSynthesisService>();
      services.AddSingleton<ISpeechToSpeechService, SpeechToSpeechService>();

      // Engines facade (Phase 5 Post-Timeline 5A)
      services.AddSingleton<IEnginesClient, EnginesClient>();

      // Quality pipeline facade (Phase 5 Post-Timeline 5B)
      services.AddSingleton<IQualityPipelineService, QualityPipelineService>();

      // Ensemble facade (Phase 5 Post-Timeline 5C)
      services.AddSingleton<IEnsembleService, EnsembleService>();

      // Text analysis facade (Phase 5 Post-Timeline 5D)
      services.AddSingleton<ITextAnalysisService, TextAnalysisService>();

      // Quality history facade (Phase 5 Post-Timeline 5D)
      services.AddSingleton<IQualityHistoryService, QualityHistoryService>();

      // Project audio facade (Timeline hardening Phase 3)
      services.AddSingleton<IProjectAudioClient, ProjectAudioClient>();

      // Emotion/style control facade (Mid-Stage Architecture Compression Task 2.1)
      services.AddSingleton<IEmotionStyleClient, EmotionStyleClient>();

      // Emotion control facade (Mypy Reassess and Architecture Pivot Plan Phase 2)
      services.AddSingleton<IEmotionControlClient, EmotionControlClient>();

      // Preset library facade (PresetLibraryViewModel hardening)
      services.AddSingleton<IPresetLibraryClient>(sp => new PresetLibraryClient(
          sp.GetRequiredService<IBackendClient>(),
          sp.GetService<IRequestCoordinator>()));

      // SSML facade (SSMLControlViewModel hardening)
      services.AddSingleton<ISSMLClient, SSMLClient>();

      // Quality control facade (QualityControlViewModel hardening)
      services.AddSingleton<IQualityControlClient, QualityControlClient>();

      // Transcription facade (TranscribeViewModel hardening)
      services.AddSingleton<ITranscriptionClient, TranscriptionClient>();

      // Training facade (TrainingViewModel hardening)
      services.AddSingleton<ITrainingClient, TrainingClient>();

      // Training dataset editor facade (TrainingDatasetEditorViewModel hardening)
      services.AddSingleton<ITrainingDatasetEditorClient, TrainingDatasetEditorClient>();

      // Batch processing facade (BatchProcessingViewModel hardening)
      services.AddSingleton<IBatchProcessingClient, BatchProcessingClient>();

      // Voice cloning wizard facade (VoiceCloningWizardViewModel hardening)
      services.AddSingleton<IVoiceCloningWizardClient, VoiceCloningWizardClient>();

      // Library facade (LibraryViewModel hardening)
      services.AddSingleton<ILibraryClient, LibraryClient>();

      // Import workflow (Transport Coherence Wave 4 Phase 2)
      services.AddSingleton<IImportWorkflowService>(sp => new ImportWorkflowService(
          sp.GetRequiredService<ILibraryClient>(),
          sp.GetRequiredService<IContextManager>(),
          sp.GetRequiredService<IProjectAudioClient>(),
          sp.GetService<IErrorLoggingService>(),
          sp.GetService<IEventAggregator>()));

      // Real-time voice converter facade (RealTimeVoiceConverterViewModel hardening)
      services.AddSingleton<IRealTimeVoiceConverterClient, RealTimeVoiceConverterClient>();

      // Embedding explorer facade (EmbeddingExplorerViewModel hardening)
      services.AddSingleton<IEmbeddingExplorerClient, EmbeddingExplorerClient>();

      // Text-based speech editor facade (TextBasedSpeechEditorViewModel hardening)
      services.AddSingleton<ITextBasedSpeechEditorClient, TextBasedSpeechEditorClient>();

      // Text speech editor facade (TextSpeechEditorViewModel hardening)
      services.AddSingleton<ITextSpeechEditorClient, TextSpeechEditorClient>();

      // Recording facade (RecordingViewModel hardening)
      services.AddSingleton<IRecordingClient, RecordingClient>();

      // Multitrack recording session authority (GAP-042 Slice 1)
      services.AddSingleton<IRecordingSessionCoordinator, RecordingSessionCoordinator>();

      // GAP-035: device availability + command-path mic selection (before fan-out; fan-out depends on availability)
      services.AddSingleton<IRecordingInputCommandState, RecordingInputCommandState>();
      services.AddSingleton<IRecordingDeviceAvailabilityService, RecordingDeviceAvailabilityService>();

      // GAP-033: transcript–clip linkage (project JSON authority)
      services.AddSingleton<IClipTranscriptLinkageService, ClipTranscriptLinkageService>();

      // GAP-045: transcript segment → timeline target + edit-intent foundation
      services.AddSingleton<ITimelineSelectedProjectGate, TimelineSelectedProjectGate>();
      services.AddSingleton<ITranscriptSegmentTargetResolver, TranscriptSegmentTargetResolver>();
      services.AddSingleton<ITranscriptRegenerationClient, TranscriptRegenerationClient>();
      services.AddSingleton<TranscriptSegmentRegenerationCoordinator>(sp => new TranscriptSegmentRegenerationCoordinator(
          sp.GetRequiredService<ITranscriptRegenerationClient>(),
          sp.GetRequiredService<IJobProgressApiClient>(),
          sp.GetRequiredService<IBackendClient>(),
          sp.GetRequiredService<IClipTranscriptLinkageService>(),
          sp.GetRequiredService<ITimelineSelectedProjectGate>(),
          sp.GetRequiredService<ITranscriptSegmentTargetResolver>(),
          sp.GetService<IProjectSessionDirtyState>(),
          sp.GetService<UndoRedoService>(),
          sp.GetService<IEventAggregator>(),
          sp.GetService<IErrorLoggingService>(),
          sp.GetService<ITranscriptionClient>()));
      services.AddSingleton<ITranscriptTruthRefreshCoordinator>(sp => new TranscriptTruthRefreshCoordinator(
          sp.GetRequiredService<ITranscriptionClient>(),
          sp.GetRequiredService<IClipTranscriptLinkageService>(),
          sp.GetService<IProjectSessionDirtyState>(),
          sp.GetService<IEventAggregator>(),
          sp.GetService<IErrorLoggingService>()));
      services.AddSingleton<ITranscriptEditIntentService, TranscriptEditIntentService>();
      services.AddSingleton<TranscriptEditHistoryService>();

      // Multitrack capture fan-out (GAP-042 Slice 3 + GAP-035 churn subscription)
      services.AddSingleton<IRecordingCaptureFanoutService>(sp => new RecordingCaptureFanoutService(
          sp.GetRequiredService<IRecordingClient>(),
          sp.GetRequiredService<IRecordingDeviceAvailabilityService>()));

      // Dataset QA facade (DatasetQAViewModel hardening)
      services.AddSingleton<IDatasetQAClient, DatasetQAClient>();

      // Diagnostics facade (DiagnosticsViewModel hardening)
      services.AddSingleton<IDiagnosticsClient, DiagnosticsClient>();

      // Analyzer facade (AnalyzerViewModel hardening)
      services.AddSingleton<IAnalyzerClient, AnalyzerClient>();

      // Settings facade (SettingsViewModel hardening)
      services.AddSingleton<ISettingsClient, SettingsClient>();

      // Macro facade (PR-9: owns HTTP via pipeline; no IBackendClient delegation)
      services.AddSingleton<IMacroClient>(sp => new MacroClient(sp.GetRequiredService<BackendHttpContext>().Pipeline));

      // Model manager facade (PR-15: owns HTTP via pipeline; no IBackendClient delegation)
      services.AddSingleton<IModelManagerClient>(sp => new ModelManagerClient(sp.GetRequiredService<BackendHttpContext>().Pipeline));

      // Job progress API facade (JobProgressViewModel hardening)
      services.AddSingleton<IJobProgressApiClient, JobProgressApiClient>();

      // Multi-voice generator facade (MultiVoiceGeneratorViewModel migration)
      services.AddSingleton<IMultiVoiceGeneratorClient, MultiVoiceGeneratorClient>();

      // Ensemble synthesis facade (EnsembleSynthesisViewModel migration)
      services.AddSingleton<IEnsembleSynthesisClient, EnsembleSynthesisClient>();

      // Global search facade (PR-4: SearchClient owns HTTP via pipeline)
      services.AddSingleton<ISearchClient>(sp => new SearchClient(sp.GetRequiredService<BackendHttpContext>().Pipeline));

      // Health/version facade (PR-5: extracted from BackendClient)
      services.AddSingleton<IHealthVersionClient>(sp => new HealthVersionClient(sp.GetRequiredService<BackendHttpContext>()));

      // Telemetry/diagnostics facade (PR-6: extracted from BackendClient)
      services.AddSingleton<ITelemetryClient>(sp => new TelemetryClient(sp.GetRequiredService<BackendHttpContext>().Pipeline));

      // Connection status facade (PR-8: extracted from IBackendClient; no HTTP)
      services.AddSingleton<IConnectionStatusClient>(sp => new ConnectionStatusClient(sp.GetRequiredService<BackendHttpContext>()));

      // Backup/restore facade (PR-14: owns HTTP via pipeline; no IBackendClient delegation)
      services.AddSingleton<IBackupRestoreClient>(sp => new BackupRestoreClient(sp.GetRequiredService<BackendHttpContext>().Pipeline));

      // API key manager facade (APIKeyManagerViewModel migration)
      services.AddSingleton<IAPIKeyManagerClient, APIKeyManagerClient>();

      // Script editor facade (PR-7: owns HTTP via pipeline; no IBackendClient delegation)
      services.AddSingleton<IScriptEditorClient>(sp => new ScriptEditorClient(sp.GetRequiredService<BackendHttpContext>().Pipeline));

      // Automation facade (AutomationViewModel migration)
      services.AddSingleton<IAutomationClient, AutomationClient>();

      // Scene builder facade (SceneBuilderViewModel migration)
      services.AddSingleton<ISceneBuilderClient, SceneBuilderClient>();

      // Mix assistant facade (MixAssistantViewModel migration)
      services.AddSingleton<IMixAssistantClient, MixAssistantClient>();
      services.AddSingleton<IAdvancedSettingsClient, AdvancedSettingsClient>();
      services.AddSingleton<IUltimateDashboardClient, UltimateDashboardClient>();
      services.AddSingleton<IImageSearchClient, ImageSearchClient>();
      services.AddSingleton<ITemplateLibraryClient, TemplateLibraryClient>();
      services.AddSingleton<IVoiceMorphClient, VoiceMorphClient>();
      services.AddSingleton<IVoiceStyleTransferClient, VoiceStyleTransferClient>();
      services.AddSingleton<IStyleTransferClient, StyleTransferClient>();
      services.AddSingleton<IUpscalingClient, UpscalingClient>();
      services.AddSingleton<IEngineParameterTuningClient, EngineParameterTuningClient>();
      services.AddSingleton<IImageGenClient, ImageGenClient>();
      services.AddSingleton<ISpectrogramClient, SpectrogramClient>();
      services.AddSingleton<ISpatialAudioClient, SpatialAudioClient>();
      services.AddSingleton<IPluginHealthClient>(sp => new PluginHealthClient(sp.GetRequiredService<BackendHttpContext>().Pipeline));
      services.AddSingleton<IProfileHealthClient, ProfileHealthClient>();
      services.AddSingleton<ISonographyClient, SonographyClient>();
      services.AddSingleton<ILexiconClient, LexiconClient>();
      services.AddSingleton<IHelpClient, HelpClient>();
      services.AddSingleton<IKeyboardShortcutsClient, KeyboardShortcutsClient>();
      services.AddSingleton<ITodoPanelClient, TodoPanelClient>();
      services.AddSingleton<IPronunciationLexiconClient, PronunciationLexiconClient>();
      services.AddSingleton<IProsodyClient, ProsodyClient>();
      services.AddSingleton<ITagManagerClient, TagManagerClient>();
      services.AddSingleton<ITagOrganizationClient, TagOrganizationClient>();
      services.AddSingleton<IMarkerManagerClient, MarkerManagerClient>();
      services.AddSingleton<IVoiceMorphingBlendingClient, VoiceMorphingBlendingClient>();
      services.AddSingleton<IVoiceBrowserClient, VoiceBrowserClient>();
      services.AddSingleton<IVoiceQuickCloneClient, VoiceQuickCloneClient>();
      services.AddSingleton<IWorkflowAutomationClient>(sp => new WorkflowAutomationClient(sp.GetRequiredService<BackendHttpContext>().Pipeline));
      services.AddSingleton<IAIMixingClient, AIMixingClient>();
      services.AddSingleton<IAIProductionAssistantClient, AIProductionAssistantClient>();
      services.AddSingleton<IAdvancedSpectrogramClient, AdvancedSpectrogramClient>();
      services.AddSingleton<IAdvancedWaveformClient, AdvancedWaveformClient>();
      services.AddSingleton<IAnalyticsDashboardClient, AnalyticsDashboardClient>();
      services.AddSingleton<IAudioAnalysisClient, AudioAnalysisClient>();
      services.AddSingleton<IDeepfakeCreatorClient, DeepfakeCreatorClient>();
      services.AddSingleton<IGPUStatusClient, GPUStatusClient>();
      services.AddSingleton<IMCPDashboardClient, MCPDashboardClient>();
      services.AddSingleton<IMultilingualSupportClient, MultilingualSupportClient>();
      services.AddSingleton<IPipelineConversationClient>(sp => new PipelineConversationClient(sp.GetRequiredService<BackendHttpContext>().Pipeline, sp.GetService<IWebSocketService>()));
      services.AddSingleton<IRealTimeAudioVisualizerClient, RealTimeAudioVisualizerClient>();
      services.AddSingleton<ISpatialStageClient, SpatialStageClient>();
      services.AddSingleton<ITextHighlightingClient, TextHighlightingClient>();
      services.AddSingleton<IVideoEditClient, VideoEditClient>();
      services.AddSingleton<IVideoGenClient, VideoGenClient>();
      services.AddSingleton<IAdvancedRealTimeVisualizationClient, AdvancedRealTimeVisualizationClient>();
      services.AddSingleton<IAudioMonitoringDashboardClient, AudioMonitoringDashboardClient>();
      services.AddSingleton<IEffectsMeterClient, EffectsMeterClient>();
      services.AddSingleton<IEffectChainClient>(sp => new EffectChainClient(sp.GetRequiredService<BackendHttpContext>().Pipeline));
      services.AddSingleton<IMixerStateClient, MixerStateClient>();
      services.AddSingleton<IImageVideoEnhancementPipelineClient, ImageVideoEnhancementPipelineClient>();
      services.AddSingleton<ISLODashboardClient, SLODashboardClient>();

      // Assistant facade (AssistantViewModel migration)
      services.AddSingleton<IAssistantClient, AssistantClient>();

      // Audio visualization facade (Timeline hardening Phase 4)
      services.AddSingleton<IAudioVisualizationService, AudioVisualizationService>();

      // GAP-CS-001: WebSocket services for real-time streaming support
      services.AddSingleton<IWebSocketService>(sp => new WebSocketService(
          sp.GetRequiredService<BackendClientConfig>().WebSocketUrl));
      services.AddSingleton<IMeterClient>(sp =>
          new MeterWebSocketClient(sp.GetRequiredService<IWebSocketService>()));
      services.AddSingleton<IWebSocketClientFactory>(sp => new WebSocketClientFactory(
          sp.GetService<IWebSocketService>(),
          sp.GetRequiredService<BackendClientConfig>().BaseUrl));

      services.AddSingleton<IProfilesUseCase, ProfilesUseCase>();
      services.AddSingleton<ITimelineUseCase>(sp => new TimelineUseCase(sp.GetRequiredService<IBackendClient>()));
    }

    /// <summary>
    /// Registers panel-specific services: PanelRegistry, PanelStateService, ContextManager, etc.
    /// Must run after RegisterBackendFacades.
    /// </summary>
    private static void RegisterPanelServices(IServiceCollection services)
    {
      services.AddTransient<VoiceStudio.App.Views.Panels.ProfilesViewModel>();
      services.AddSingleton<IPanelRegistry, PanelRegistry>();
      services.AddSingleton<PanelStateService>();
      services.AddSingleton<IUnifiedWorkspaceService>(sp => sp.GetRequiredService<PanelStateService>());
      services.AddSingleton<INavigationService, NavigationService>();

      // Event aggregator for cross-panel synchronization (Phase 4)
      services.AddSingleton<IEventAggregator, EventAggregator>();

      // Throttled event publisher for high-frequency events (Premium Reliability Pass Task 7)
      services.AddSingleton<ThrottledEventPublisher>(sp => new ThrottledEventPublisher(
          sp.GetRequiredService<IEventAggregator>()));

      // Central state store with undo/redo support (Panel Architecture Phase 5)
      // Must be before ContextManager, DragDropService, EventReplayService
      services.AddSingleton<IAppStateStore, AppStateStore>();
      services.AddSingleton<AppStateStore>();

      // Context manager for centralized active state (Panel Architecture Phase 2)
      services.AddSingleton<IContextManager>(sp => new ContextManager(
          sp.GetRequiredService<IEventAggregator>(),
          sp.GetService<AppStateStore>()));

      // GAP-014: Legacy ILayoutService/IWorkspaceService (LayoutService/WorkspaceService) removed from DI.
      // Runtime workspace authority is PanelStateService (IUnifiedWorkspaceService) + MainWindow orchestration.

      // Selection navigation stack for back/forward navigation (Panel Architecture Phase 5)
      services.AddSingleton<ISelectionStack, SelectionStack>();

      // Drag and Drop service (Panel Architecture Phase 4)
      services.AddSingleton<IDragDropService>(sp => new DragDropService(
          sp.GetService<IEventAggregator>(),
          sp.GetService<IAppStateStore>(),
          sp.GetService<Microsoft.Extensions.Logging.ILogger<DragDropService>>()));

      // Capability service for engine/feature progressive disclosure (Panel Architecture Phase 7)
      services.AddSingleton<ICapabilityService, CapabilityService>();

      // Job service for unified job tracking across panels (Panel Architecture)
      services.AddSingleton<IJobService>(sp => new JobService(
          sp.GetService<IEventAggregator>(),
          sp.GetService<Microsoft.Extensions.Logging.ILogger<JobService>>()));

      // Selection broadcast service for follow-selection behavior (Panel Architecture Phase D)
      services.AddSingleton<ISelectionBroadcastService>(sp => new SelectionBroadcastService(
          sp.GetService<IEventAggregator>(),
          sp.GetService<Microsoft.Extensions.Logging.ILogger<SelectionBroadcastService>>(),
          sp.GetService<ThrottledEventPublisher>()));

      // Synchronized scroll service for cross-panel scroll coordination (Panel Architecture Phase D)
      services.AddSingleton<ISynchronizedScrollService>(sp => new SynchronizedScrollService(
          sp.GetService<IEventAggregator>(),
          sp.GetService<Microsoft.Extensions.Logging.ILogger<SynchronizedScrollService>>(),
          sp.GetService<ThrottledEventPublisher>()));

      // Event replay service for debug capture and replay bundles (Panel Architecture Phase D)
      services.AddSingleton<IEventReplayService>(sp => new EventReplayService(
          sp.GetService<IEventAggregator>(),
          sp.GetService<IAppStateStore>(),
          sp.GetService<IContextManager>(),
          sp.GetService<Microsoft.Extensions.Logging.ILogger<EventReplayService>>()));

      // Workflow coordinator for multi-panel sequences (Panel Workflow Integration)
      services.AddSingleton<IWorkflowCoordinatorService, WorkflowCoordinatorService>();
    }

    /// <summary>
    /// Registers UI services: DialogService, ViewModel factory, ToastNotificationService, etc.
    /// Must run after RegisterPanelServices.
    /// </summary>
    private static void RegisterUIServices(IServiceCollection services)
    {
      // ViewModel context (factory: dispatcher may be null at startup; fallback when resolved)
      services.AddSingleton<IViewModelContext>(_ =>
      {
        var dispatcher = DispatcherQueue.GetForCurrentThread()
                  ?? Microsoft.UI.Dispatching.DispatcherQueueController.CreateOnDedicatedThread().DispatcherQueue;
        return new ViewModelContext(NullLogger.Instance, dispatcher);
      });

      // Core app services (register implementations; order may matter for dependencies)
      // DialogService requires Window - use factory to lazily get App.MainWindowInstance
      services.AddSingleton<IDialogService>(sp =>
      {
        var window = App.MainWindowInstance
          ?? throw new InvalidOperationException("MainWindow not yet created. DialogService must be resolved after OnLaunched.");
        return new DialogService(window);
      });
      services.AddSingleton<IExportLufsPresetUi>(sp =>
          new ExportLufsPresetDialogService(sp.GetRequiredService<IDialogService>()));
      services.AddSingleton<ISettingsService, SettingsService>();
      services.AddSingleton<IProjectSessionDirtyState, ProjectSessionDirtyState>();
      services.AddSingleton<CrashRecoveryService>();
      services.AddSingleton<IMultitrackRecoveryStateService, MultitrackRecoveryStateService>();
      services.AddSingleton<IMultitrackRecoveryApplyService, MultitrackRecoveryApplyService>();
      services.AddSingleton<IErrorDialogService, ErrorDialogService>();
      // GAP-I12: Inject correlation provider into ErrorLoggingService
      services.AddSingleton<IErrorLoggingService>(sp => new ErrorLoggingService(
        sp.GetRequiredService<ICorrelationIdProvider>()));
      services.AddSingleton<IAuditLoggingService>(sp => new AuditLoggingService(sp.GetRequiredService<IErrorLoggingService>()));
      services.AddSingleton<IHelpOverlayService, HelpOverlayService>();
      services.AddSingleton<IUpdateService>(sp => new UpdateService(sp.GetRequiredService<HttpClient>()));
      services.AddSingleton<IAudioPlayerService>(sp => new AudioPlayerService(sp.GetRequiredService<HttpClient>()));
      services.AddSingleton<TransportOrchestrationBootstrap>();
      services.AddSingleton<IGlobalTransportOrchestrator>(sp => new GlobalTransportOrchestrator(
          sp.GetRequiredService<IContextManager>(),
          sp.GetRequiredService<IAudioPlayerService>(),
          sp.GetRequiredService<BackendClientConfig>(),
          sp.GetService<ToastNotificationService>(),
          sp.GetRequiredService<TransportOrchestrationBootstrap>()));
      services.AddSingleton<IProfilePreviewService, ProfilePreviewService>();
      services.AddSingleton<IProfileQualityInsightsService, ProfileQualityInsightsService>();
      services.AddSingleton<IProfileTransferService, ProfileTransferService>();
      services.AddSingleton<IProfileEnhancementService, ProfileEnhancementService>();
      services.AddSingleton<OperationQueueService>();
      services.AddSingleton<StatePersistenceService>();
      services.AddSingleton<StateCacheService>();
      services.AddSingleton<AudioStore>(sp => new AudioStore(
          sp.GetRequiredService<IBackendClient>(),
          sp.GetRequiredService<ITimelineTrackService>(),
          sp.GetService<StateCacheService>()));
      services.AddSingleton<PluginManager>();
      // Plugin Bridge Service for frontend-backend plugin state synchronization (Phase 1)
      services.AddSingleton<IPluginBridgeService, PluginBridgeService>(sp => new PluginBridgeService(
          sp.GetRequiredService<ILogger<PluginBridgeService>>(),
          sp.GetService<IUnifiedAuthService>()));
      services.AddSingleton<RealTimeQualityService>();
      // NOTE: ToastNotificationService requires a StackPanel container and cannot be auto-resolved.
      // It must be registered manually via RegisterToastNotificationService() after UI is created.
      services.AddSingleton<MultiSelectService>();
      services.AddSingleton<DragDropVisualFeedbackService>();
      services.AddSingleton<ContextMenuService>();
      services.AddSingleton<UndoRedoService>();
      services.AddSingleton<RecentProjectsService>();
      services.AddSingleton<ToolbarConfigurationService>();
      services.AddSingleton<INotificationCenterService, NotificationCenterService>();
      services.AddSingleton<IAnimationService, AnimationService>();
      services.AddSingleton<StatusBarActivityService>();
      services.AddSingleton<StatusBarCoordinator>();
      services.AddSingleton<TransportShortcutCoordinator>(sp =>
          new TransportShortcutCoordinator(sp.GetService<IGlobalTransportOrchestrator>()));
      services.AddSingleton<StartupRetryCoordinator>();
      services.AddSingleton<KeyboardShortcutService>();
      services.AddSingleton<IUnifiedKeyboardService>(sp => sp.GetRequiredService<KeyboardShortcutService>());
      services.AddTransient<KeyboardCustomizationViewModel>();
      services.AddSingleton<ToolbarViewModel>(sp =>
          new ToolbarViewModel(
              sp.GetRequiredService<ToolbarConfigurationService>(),
              sp.GetRequiredService<IUnifiedCommandRegistry>(),
              sp.GetRequiredService<IUnifiedWorkspaceService>(),
              sp.GetRequiredService<IAudioPlayerService>(),
              _toastOverride));
      services.AddSingleton<IUnifiedCommandRegistry>(sp =>
        new UnifiedCommandRegistry(
          sp.GetRequiredService<KeyboardShortcutService>(),
          sp.GetRequiredService<IStartupStateService>()));
      services.AddSingleton<CommandRouter>(sp =>
        new CommandRouter(sp.GetRequiredService<IUnifiedCommandRegistry>()));
      services.AddSingleton<ILocalSearchProvider, CommandSearchProvider>();
      services.AddSingleton<ILocalSearchProvider, SettingsSearchProvider>();
      services.AddSingleton<IGlobalSearchService, LocalSearchAggregator>();
      services.AddSingleton<CollaborationService>();
      services.AddSingleton<IStartupStateService, StartupStateService>();
      services.AddSingleton<IStartupDiagnosticsWriter, StartupDiagnosticsWriter>();
      services.AddSingleton<BackendProcessManager>(sp =>
        new BackendProcessManager(
          sp.GetRequiredService<BackendClientConfig>().BaseUrl,
          sp.GetRequiredService<IStartupDiagnosticsWriter>()));
      services.AddSingleton<IFeatureFlagsService, FeatureFlagsService>();
      services.AddSingleton<IErrorPresentationService, ErrorPresentationService>();
      services.AddSingleton<IAnalyticsService, AnalyticsService>();
      services.AddSingleton<EngineManager>();
      services.AddSingleton<OnboardingWizardService>();

      // Theme service: unified theme management with persistence
      services.AddSingleton<IUnifiedThemeService, ThemeManager>();

      // ITelemetryService: stub when no dedicated implementation (GAP-003 follow-up can add real impl)
      services.AddSingleton<ITelemetryService, TelemetryServiceStub>();

      // IProjectRepository: JSON-based local storage (local-first, no cloud)
      services.AddSingleton<IProjectRepository, JsonProjectRepository>();

      // ISecretsService: use available implementation
      services.AddSingleton<ISecretsService, DevVaultSecretsService>();

      // Module loader for UI modules
      services.AddSingleton<ModuleLoader>();

      // Error coordination service
      services.AddSingleton<IErrorCoordinator, ErrorCoordinator>();

      // ViewModel factory (needs service provider, so use factory registration)
      services.AddSingleton<IViewModelFactory>(sp => new ViewModelFactory(sp));

      // GAP-B12: Command queue service for busy-state handling
      services.AddSingleton<ICommandQueueService>(sp =>
        new CommandQueueService(
          sp.GetRequiredService<IUnifiedCommandRegistry>(),
          sp.GetService<ICommandMutexService>(),
          DispatcherQueue.GetForCurrentThread()));
    }

    /// <summary>
    /// Registers all panels in the unified PanelRegistry.
    /// Called after DI container is built.
    /// </summary>
    private static void RegisterAllPanels()
    {
      var sw = Stopwatch.StartNew();
      using var profiler = PerformanceProfiler.StartPanelLoad("RegistryInit");
      var registry = GetPanelRegistry();

      // Register advanced panels (TextSpeechEditor, Prosody, SpatialAudio, etc.)
      AdvancedPanelRegistrationService.RegisterAdvancedPanels(registry);

      // Register core panels - these were previously hardcoded in MainWindow
      CorePanelRegistrationService.RegisterCorePanels(registry);

      // Register module panels (Modules menu items not in Core/Advanced)
      ModulePanelRegistrationService.RegisterModulePanels(registry);

      sw.Stop();
      PanelRegistrationMs = sw.Elapsed.TotalMilliseconds;
      Debug.WriteLine(
        $"[AppServices] Registered {registry.GetAllDescriptors().Count()} panels in PanelRegistry in {PanelRegistrationMs:F1}ms");

#if DEBUG
      var failures = new List<(string panelId, string viewModelType, string message)>();
      var total = 0;
      foreach (var d in registry.GetAllDescriptors())
      {
        if (d.ViewModelType == null)
          continue;
        total++;
        try
        {
          ActivatorUtilities.CreateInstance(_provider!, d.ViewModelType);
        }
        catch (Exception ex)
        {
          failures.Add((d.PanelId, d.ViewModelType.FullName ?? d.ViewModelType.Name ?? "?", ex.Message));
        }
      }
      var passed = total - failures.Count;
      Debug.WriteLine($"[Startup] VM resolvability: {passed}/{total} passed, {failures.Count} failed");
      if (failures.Count > 0)
      {
        try
        {
          var diagDir = System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "VoiceStudio", "crashes");
          System.IO.Directory.CreateDirectory(diagDir);
          var path = System.IO.Path.Combine(diagDir, "vm_resolution_diag.txt");
          var lines = new List<string>
          {
            $"[{DateTime.UtcNow:O}] VM resolvability check: {failures.Count} failures",
            ""
          };
          foreach (var (panelId, vmType, msg) in failures)
            lines.Add($"  {panelId}: {vmType} — {msg}");
          System.IO.File.WriteAllText(path, string.Join(Environment.NewLine, lines));
        }
        catch (Exception ex)
        {
          Debug.WriteLine($"[Startup] Failed to write vm_resolution_diag.txt: {ex.Message}");
        }
      }
#endif
    }

    /// <summary>
    /// Wires the command queue service to the unified command registry.
    /// GAP-B12: Enables busy-state command queueing.
    /// </summary>
    private static void WireCommandQueueService()
    {
      var registry = GetCommandRegistry() as UnifiedCommandRegistry;
      var queueService = GetService<ICommandQueueService>();

      if (registry != null && queueService != null)
      {
        registry.SetQueueService(queueService);
        System.Diagnostics.Debug.WriteLine(
          "[AppServices] Command queue service wired to registry (GAP-B12)");
      }
      else
      {
        System.Diagnostics.Debug.WriteLine(
          "[AppServices] Warning: Could not wire command queue service");
      }
    }

    public static T? GetService<T>() where T : class => (T?)_provider?.GetService(typeof(T));
    public static T GetRequiredService<T>() where T : class =>
        GetService<T>() ?? throw new InvalidOperationException($"Service not registered: {typeof(T).FullName}");

    public static IViewModelContext GetViewModelContext() => GetRequiredService<IViewModelContext>();

    public static void RegisterToastNotificationService(ToastNotificationService service) => _toastOverride = service;

    /// <summary>GAP-034: lazy singleton OS completion notifications (no UI StackPanel required).</summary>
    public static ICompletionOsNotificationService TryGetCompletionOsNotificationService()
    {
        return _completionOsNotification ??= new CompletionOsNotificationService();
    }

    // Typed accessors (forward to GetService / GetRequiredService)
    public static IBackendClient GetBackendClient() => GetRequiredService<IBackendClient>();
    public static IProjectsClient GetProjectsClient() => GetRequiredService<IProjectsClient>();
    public static IProfilesClient GetProfilesClient() => GetRequiredService<IProfilesClient>();
    public static IEnginesClient GetEnginesClient() => GetRequiredService<IEnginesClient>();

    /// <summary>Canonical voice synthesis facade for panels (GAP-052 comparison, Voice Synthesis, etc.).</summary>
    public static IVoiceSynthesisService GetVoiceSynthesisService() => GetRequiredService<IVoiceSynthesisService>();
    public static ISearchClient GetSearchClient() => GetRequiredService<ISearchClient>();
    public static IHealthVersionClient GetHealthVersionClient() => GetRequiredService<IHealthVersionClient>();
    public static ITelemetryClient GetTelemetryClient() => GetRequiredService<ITelemetryClient>();
    public static IBackupRestoreClient GetBackupRestoreClient() => GetRequiredService<IBackupRestoreClient>();
    public static IAPIKeyManagerClient GetAPIKeyManagerClient() => GetRequiredService<IAPIKeyManagerClient>();
    public static IScriptEditorClient GetScriptEditorClient() => GetRequiredService<IScriptEditorClient>();
    public static IAutomationClient GetAutomationClient() => GetRequiredService<IAutomationClient>();
    public static ISceneBuilderClient GetSceneBuilderClient() => GetRequiredService<ISceneBuilderClient>();
    public static IMixAssistantClient GetMixAssistantClient() => GetRequiredService<IMixAssistantClient>();
    public static IAssistantClient GetAssistantClient() => GetRequiredService<IAssistantClient>();
    public static IAdvancedSettingsClient GetAdvancedSettingsClient() => GetRequiredService<IAdvancedSettingsClient>();
    public static IUltimateDashboardClient GetUltimateDashboardClient() => GetRequiredService<IUltimateDashboardClient>();
    public static IImageSearchClient GetImageSearchClient() => GetRequiredService<IImageSearchClient>();
    public static ITemplateLibraryClient GetTemplateLibraryClient() => GetRequiredService<ITemplateLibraryClient>();
    public static IVoiceMorphClient GetVoiceMorphClient() => GetRequiredService<IVoiceMorphClient>();
    public static IVoiceStyleTransferClient GetVoiceStyleTransferClient() => GetRequiredService<IVoiceStyleTransferClient>();
    public static IStyleTransferClient GetStyleTransferClient() => GetRequiredService<IStyleTransferClient>();
    public static IUpscalingClient GetUpscalingClient() => GetRequiredService<IUpscalingClient>();
    public static IEngineParameterTuningClient GetEngineParameterTuningClient() => GetRequiredService<IEngineParameterTuningClient>();
    public static IImageGenClient GetImageGenClient() => GetRequiredService<IImageGenClient>();
    public static ISpectrogramClient GetSpectrogramClient() => GetRequiredService<ISpectrogramClient>();
    public static ISpatialAudioClient GetSpatialAudioClient() => GetRequiredService<ISpatialAudioClient>();
    public static IPluginHealthClient GetPluginHealthClient() => GetRequiredService<IPluginHealthClient>();
    public static IProfileHealthClient GetProfileHealthClient() => GetRequiredService<IProfileHealthClient>();
    public static ISonographyClient GetSonographyClient() => GetRequiredService<ISonographyClient>();
    public static IProjectAudioClient GetProjectAudioClient() => GetRequiredService<IProjectAudioClient>();
    public static ILexiconClient GetLexiconClient() => GetRequiredService<ILexiconClient>();
    public static IHelpClient GetHelpClient() => GetRequiredService<IHelpClient>();
    public static IKeyboardShortcutsClient GetKeyboardShortcutsClient() => GetRequiredService<IKeyboardShortcutsClient>();
    public static IPronunciationLexiconClient GetPronunciationLexiconClient() => GetRequiredService<IPronunciationLexiconClient>();
    public static IProsodyClient GetProsodyClient() => GetRequiredService<IProsodyClient>();
    public static ITagManagerClient GetTagManagerClient() => GetRequiredService<ITagManagerClient>();
    public static ITagOrganizationClient GetTagOrganizationClient() => GetRequiredService<ITagOrganizationClient>();
    public static IMarkerManagerClient GetMarkerManagerClient() => GetRequiredService<IMarkerManagerClient>();
    public static IVoiceMorphingBlendingClient GetVoiceMorphingBlendingClient() => GetRequiredService<IVoiceMorphingBlendingClient>();
    public static IVoiceBrowserClient GetVoiceBrowserClient() => GetRequiredService<IVoiceBrowserClient>();
    public static IVoiceQuickCloneClient GetVoiceQuickCloneClient() => GetRequiredService<IVoiceQuickCloneClient>();
    public static IWorkflowAutomationClient GetWorkflowAutomationClient() => GetRequiredService<IWorkflowAutomationClient>();
    public static IAIMixingClient GetAIMixingClient() => GetRequiredService<IAIMixingClient>();
    public static IAIProductionAssistantClient GetAIProductionAssistantClient() => GetRequiredService<IAIProductionAssistantClient>();
    public static IAdvancedSpectrogramClient GetAdvancedSpectrogramClient() => GetRequiredService<IAdvancedSpectrogramClient>();
    public static IAdvancedWaveformClient GetAdvancedWaveformClient() => GetRequiredService<IAdvancedWaveformClient>();
    public static IAnalyticsDashboardClient GetAnalyticsDashboardClient() => GetRequiredService<IAnalyticsDashboardClient>();
    public static IAudioAnalysisClient GetAudioAnalysisClient() => GetRequiredService<IAudioAnalysisClient>();
    public static IDeepfakeCreatorClient GetDeepfakeCreatorClient() => GetRequiredService<IDeepfakeCreatorClient>();
    public static IGPUStatusClient GetGPUStatusClient() => GetRequiredService<IGPUStatusClient>();
    public static IMCPDashboardClient GetMCPDashboardClient() => GetRequiredService<IMCPDashboardClient>();
    public static IMultilingualSupportClient GetMultilingualSupportClient() => GetRequiredService<IMultilingualSupportClient>();
    public static IPipelineConversationClient GetPipelineConversationClient() => GetRequiredService<IPipelineConversationClient>();
    public static IRealTimeAudioVisualizerClient GetRealTimeAudioVisualizerClient() => GetRequiredService<IRealTimeAudioVisualizerClient>();
    public static ISpatialStageClient GetSpatialStageClient() => GetRequiredService<ISpatialStageClient>();
    public static ITextHighlightingClient GetTextHighlightingClient() => GetRequiredService<ITextHighlightingClient>();
    public static IVideoEditClient GetVideoEditClient() => GetRequiredService<IVideoEditClient>();
    public static IVideoGenClient GetVideoGenClient() => GetRequiredService<IVideoGenClient>();
    public static IAdvancedRealTimeVisualizationClient GetAdvancedRealTimeVisualizationClient() => GetRequiredService<IAdvancedRealTimeVisualizationClient>();
    public static IAudioMonitoringDashboardClient GetAudioMonitoringDashboardClient() => GetRequiredService<IAudioMonitoringDashboardClient>();
    public static IEffectsMeterClient GetEffectsMeterClient() => GetRequiredService<IEffectsMeterClient>();
    public static IMeterClient GetMeterClient() => GetRequiredService<IMeterClient>();
    public static IEffectChainClient GetEffectChainClient() => GetRequiredService<IEffectChainClient>();
    public static IMixerStateClient GetMixerStateClient() => GetRequiredService<IMixerStateClient>();
    public static IImageVideoEnhancementPipelineClient GetImageVideoEnhancementPipelineClient() => GetRequiredService<IImageVideoEnhancementPipelineClient>();
    public static ISLODashboardClient GetSLODashboardClient() => GetRequiredService<ISLODashboardClient>();
    public static IEmotionControlClient GetEmotionControlClient() => GetRequiredService<IEmotionControlClient>();
    public static ITodoPanelClient GetTodoPanelClient() => GetRequiredService<ITodoPanelClient>();
    public static ITimelineClipService GetTimelineClipService() => GetRequiredService<ITimelineClipService>();
    public static ITimelineTrackService GetTimelineTrackService() => GetRequiredService<ITimelineTrackService>();

    public static ITimelineTrackService? TryGetTimelineTrackService() => GetService<ITimelineTrackService>();

    public static IRecordingClient? TryGetRecordingClient() => GetService<IRecordingClient>();

    public static IRecordingDeviceAvailabilityService? TryGetRecordingDeviceAvailabilityService() =>
        GetService<IRecordingDeviceAvailabilityService>();

    public static IRecordingInputCommandState? TryGetRecordingInputCommandState() =>
        GetService<IRecordingInputCommandState>();
    public static IClipTranscriptLinkageService? TryGetClipTranscriptLinkageService() =>
        GetService<IClipTranscriptLinkageService>();

    public static ITimelineSelectedProjectGate? TryGetTimelineSelectedProjectGate() =>
        GetService<ITimelineSelectedProjectGate>();

    public static ITranscriptSegmentTargetResolver? TryGetTranscriptSegmentTargetResolver() =>
        GetService<ITranscriptSegmentTargetResolver>();

    public static ITranscriptEditIntentService? TryGetTranscriptEditIntentService() =>
        GetService<ITranscriptEditIntentService>();

    public static TranscriptSegmentRegenerationCoordinator? TryGetTranscriptSegmentRegenerationCoordinator() =>
        GetService<TranscriptSegmentRegenerationCoordinator>();

    public static TranscriptEditHistoryService? TryGetTranscriptEditHistoryService() =>
        GetService<TranscriptEditHistoryService>();
    public static ITranscriptTruthRefreshCoordinator? TryGetTranscriptTruthRefreshCoordinator() =>
        GetService<ITranscriptTruthRefreshCoordinator>();
    public static ITimelineTranscriptionService GetTimelineTranscriptionService() => GetRequiredService<ITimelineTranscriptionService>();
    public static ITimelineSynthesisService GetTimelineSynthesisService() => GetRequiredService<ITimelineSynthesisService>();
    public static IRequestMetricsService? TryGetRequestMetricsService() => GetService<IRequestMetricsService>();
    public static IAudioPlayerService GetAudioPlayerService() => GetRequiredService<IAudioPlayerService>();
    public static IErrorDialogService GetErrorDialogService() => GetRequiredService<IErrorDialogService>();
    public static IErrorLoggingService GetErrorLoggingService() => GetRequiredService<IErrorLoggingService>();
    public static IErrorLoggingService? TryGetErrorLoggingService() => GetService<IErrorLoggingService>();
    public static IAuditLoggingService GetAuditLoggingService() => GetRequiredService<IAuditLoggingService>();
    public static IAuditLoggingService? TryGetAuditLoggingService() => GetService<IAuditLoggingService>();
    public static OperationQueueService GetOperationQueueService() => GetRequiredService<OperationQueueService>();
    public static StatePersistenceService GetStatePersistenceService() => GetRequiredService<StatePersistenceService>();
    public static StateCacheService GetStateCacheService() => GetRequiredService<StateCacheService>();
    public static GracefulDegradationService GetGracefulDegradationService() => GetRequiredService<GracefulDegradationService>();
    public static IUpdateService GetUpdateService() => GetRequiredService<IUpdateService>();
    public static ISettingsService GetSettingsService() => GetRequiredService<ISettingsService>();
    public static IProjectSessionDirtyState GetProjectSessionDirtyState() => GetRequiredService<IProjectSessionDirtyState>();
    public static CrashRecoveryService GetCrashRecoveryService() => GetRequiredService<CrashRecoveryService>();
    public static PluginManager GetPluginManager() => GetRequiredService<PluginManager>();
    public static PluginBridgeService GetPluginBridgeService() => GetRequiredService<PluginBridgeService>();
    public static PluginBridgeService? TryGetPluginBridgeService() => GetService<PluginBridgeService>();
    public static IPanelRegistry GetPanelRegistry() => GetRequiredService<IPanelRegistry>();
    public static IHelpOverlayService GetHelpOverlayService() => GetRequiredService<IHelpOverlayService>();
    public static RealTimeQualityService GetRealTimeQualityService() => GetRequiredService<RealTimeQualityService>();
    public static PanelStateService GetPanelStateService() => GetRequiredService<PanelStateService>();
    // ToastNotificationService is NOT registered in DI - it must be set via RegisterToastNotificationService()
    // Calling GetService<ToastNotificationService>() would fail because its constructor requires a StackPanel
    public static ToastNotificationService GetToastNotificationService() =>
        _toastOverride ?? throw new InvalidOperationException(
            "ToastNotificationService not registered. Call RegisterToastNotificationService() after UI initialization.");
    public static ToastNotificationService? TryGetToastNotificationService() => _toastOverride;
    public static MultiSelectService GetMultiSelectService() => GetRequiredService<MultiSelectService>();
    public static MultiSelectService? TryGetMultiSelectService() => GetService<MultiSelectService>();
    public static DragDropVisualFeedbackService GetDragDropVisualFeedbackService() => GetRequiredService<DragDropVisualFeedbackService>();
    public static DragDropVisualFeedbackService? TryGetDragDropVisualFeedbackService() => GetService<DragDropVisualFeedbackService>();
    public static ContextMenuService GetContextMenuService() => GetRequiredService<ContextMenuService>();
    public static ContextMenuService? TryGetContextMenuService() => GetService<ContextMenuService>();
    public static UndoRedoService GetUndoRedoService() => GetRequiredService<UndoRedoService>();
    public static UndoRedoService? TryGetUndoRedoService() => GetService<UndoRedoService>();
    public static RecentProjectsService GetRecentProjectsService() => GetRequiredService<RecentProjectsService>();
    public static RecentProjectsService? TryGetRecentProjectsService() => GetService<RecentProjectsService>();
    public static ToolbarConfigurationService GetToolbarConfigurationService() => GetRequiredService<ToolbarConfigurationService>();
    public static ToolbarConfigurationService? TryGetToolbarConfigurationService() => GetService<ToolbarConfigurationService>();
    public static ToolbarViewModel GetToolbarViewModel() => GetRequiredService<ToolbarViewModel>();
    public static ToolbarViewModel? TryGetToolbarViewModel() => GetService<ToolbarViewModel>();
    public static INotificationCenterService GetNotificationCenterService() => GetRequiredService<INotificationCenterService>();
    public static INotificationCenterService? TryGetNotificationCenterService() => GetService<INotificationCenterService>();
    public static IAnimationService GetAnimationService() => GetRequiredService<IAnimationService>();
    public static IAnimationService? TryGetAnimationService() => GetService<IAnimationService>();
    public static StatusBarActivityService GetStatusBarActivityService() => GetRequiredService<StatusBarActivityService>();
    public static StatusBarActivityService? TryGetStatusBarActivityService() => GetService<StatusBarActivityService>();
    public static KeyboardShortcutService GetKeyboardShortcutService() => GetRequiredService<KeyboardShortcutService>();
    public static KeyboardShortcutService? TryGetKeyboardShortcutService() => GetService<KeyboardShortcutService>();
    public static CollaborationService GetCollaborationService() => GetRequiredService<CollaborationService>();
    public static CollaborationService? TryGetCollaborationService() => GetService<CollaborationService>();
    public static IFeatureFlagsService GetFeatureFlagsService() => GetRequiredService<IFeatureFlagsService>();
    public static IFeatureFlagsService? TryGetFeatureFlagsService() => GetService<IFeatureFlagsService>();
    public static IErrorPresentationService GetErrorPresentationService() => GetRequiredService<IErrorPresentationService>();
    public static IErrorPresentationService? TryGetErrorPresentationService() => GetService<IErrorPresentationService>();
    public static IAnalyticsService GetAnalyticsService() => GetRequiredService<IAnalyticsService>();
    public static IAnalyticsService? TryGetAnalyticsService() => GetService<IAnalyticsService>();
    public static ITelemetryService GetTelemetryService() => GetRequiredService<ITelemetryService>();
    public static ITelemetryService? TryGetTelemetryService() => GetService<ITelemetryService>();
    public static EngineManager GetEngineManager() => GetRequiredService<EngineManager>();
    public static INavigationService GetNavigationService() => GetRequiredService<INavigationService>();
    public static INavigationService? TryGetNavigationService() => GetService<INavigationService>();
    public static ISecretsService GetSecretsService() => GetRequiredService<ISecretsService>();
    public static ISecretsService? TryGetSecretsService() => GetService<ISecretsService>();
    public static IProfilesUseCase GetProfilesUseCase() => GetRequiredService<IProfilesUseCase>();
    public static IProfilesUseCase? TryGetProfilesUseCase() => GetService<IProfilesUseCase>();
    public static ITimelineUseCase GetTimelineUseCase() => GetRequiredService<ITimelineUseCase>();
    public static ITimelineUseCase? TryGetTimelineUseCase() => GetService<ITimelineUseCase>();
    public static IProjectRepository GetProjectRepository() => GetRequiredService<IProjectRepository>();
    public static IProjectRepository? TryGetProjectRepository() => GetService<IProjectRepository>();
    public static ModuleLoader GetModuleLoader() => GetRequiredService<ModuleLoader>();
    public static ModuleLoader? TryGetModuleLoader() => GetService<ModuleLoader>();
    public static IErrorCoordinator GetErrorCoordinator() => GetRequiredService<IErrorCoordinator>();
    public static IErrorCoordinator? TryGetErrorCoordinator() => GetService<IErrorCoordinator>();
    public static IViewModelFactory GetViewModelFactory() => GetRequiredService<IViewModelFactory>();
    public static IViewModelFactory? TryGetViewModelFactory() => GetService<IViewModelFactory>();
    public static IUnifiedCommandRegistry GetCommandRegistry() => GetRequiredService<IUnifiedCommandRegistry>();
    public static IUnifiedCommandRegistry? TryGetCommandRegistry() => GetService<IUnifiedCommandRegistry>();
    public static CommandRouter GetCommandRouter() => GetRequiredService<CommandRouter>();
    public static CommandRouter? TryGetCommandRouter() => GetService<CommandRouter>();
    public static ICommandQueueService GetCommandQueueService() => GetRequiredService<ICommandQueueService>();
    public static ICommandQueueService? TryGetCommandQueueService() => GetService<ICommandQueueService>();
    public static IDialogService GetDialogService() => GetRequiredService<IDialogService>();
    public static IDialogService? TryGetDialogService() => GetService<IDialogService>();

    public static IExportLufsPresetUi? TryGetExportLufsPresetUi() => GetService<IExportLufsPresetUi>();
    public static BackendProcessManager GetBackendProcessManager() => GetRequiredService<BackendProcessManager>();
    public static BackendProcessManager? TryGetBackendProcessManager() => GetService<BackendProcessManager>();

    /// <summary>GAP-063: First-run wizard onboarding state (singleton; no tooltip flow from <see cref="OnboardingWizardService.StartWizardAsync"/> in wizard).</summary>
    public static OnboardingWizardService? GetOnboardingWizardService() => GetService<OnboardingWizardService>();
    public static IStartupStateService GetStartupStateService() => GetRequiredService<IStartupStateService>();
    public static IUnifiedThemeService GetThemeService() => GetRequiredService<IUnifiedThemeService>();
    public static IUnifiedThemeService? TryGetThemeService() => GetService<IUnifiedThemeService>();
    public static IWebSocketClientFactory GetWebSocketClientFactory() => GetRequiredService<IWebSocketClientFactory>();
    public static IWebSocketClientFactory? TryGetWebSocketClientFactory() => GetService<IWebSocketClientFactory>();
    public static IEventAggregator GetEventAggregator() => GetRequiredService<IEventAggregator>();
    public static IEventAggregator? TryGetEventAggregator() => GetService<IEventAggregator>();
    public static ThrottledEventPublisher GetThrottledEventPublisher() => GetRequiredService<ThrottledEventPublisher>();
    public static ThrottledEventPublisher? TryGetThrottledEventPublisher() => GetService<ThrottledEventPublisher>();
    public static IContextManager GetContextManager() => GetRequiredService<IContextManager>();
    public static IContextManager? TryGetContextManager() => GetService<IContextManager>();

    public static IRecordingSessionCoordinator? TryGetRecordingSessionCoordinator() => GetService<IRecordingSessionCoordinator>();

    public static IRecordingCaptureFanoutService? TryGetRecordingCaptureFanoutService() => GetService<IRecordingCaptureFanoutService>();
    public static IDragDropService GetDragDropService() => GetRequiredService<IDragDropService>();
    public static IDragDropService? TryGetDragDropService() => GetService<IDragDropService>();
    public static IWorkflowCoordinatorService GetWorkflowCoordinatorService() => GetRequiredService<IWorkflowCoordinatorService>();
    public static IWorkflowCoordinatorService? TryGetWorkflowCoordinatorService() => GetService<IWorkflowCoordinatorService>();
    public static ICapabilityService GetCapabilityService() => GetRequiredService<ICapabilityService>();
    public static ICapabilityService? TryGetCapabilityService() => GetService<ICapabilityService>();
    public static IJobService GetJobService() => GetRequiredService<IJobService>();
    public static ISelectionBroadcastService GetSelectionBroadcastService() => GetRequiredService<ISelectionBroadcastService>();
    public static ISelectionBroadcastService? TryGetSelectionBroadcastService() => GetService<ISelectionBroadcastService>();
    public static ISynchronizedScrollService GetSynchronizedScrollService() => GetRequiredService<ISynchronizedScrollService>();
    public static ISynchronizedScrollService? TryGetSynchronizedScrollService() => GetService<ISynchronizedScrollService>();
    public static IEventReplayService GetEventReplayService() => GetRequiredService<IEventReplayService>();
    public static IEventReplayService? TryGetEventReplayService() => GetService<IEventReplayService>();
    public static IJobService? TryGetJobService() => GetService<IJobService>();
    public static ISelectionStack GetSelectionStack() => GetRequiredService<ISelectionStack>();
    public static ISelectionStack? TryGetSelectionStack() => GetService<ISelectionStack>();
  }

  /// <summary>
  /// Local-first stub for ITelemetryService (no external telemetry by default).
  /// 
  /// Phase 9 Gap Resolution (2026-02-10):
  /// This stub is intentionally a no-op implementation to support local-first,
  /// offline-capable operation. Per project rules:
  /// - local-first.mdc: "Telemetry and remote calls are opt-in only"
  /// - free-only.mdc: No paid services required
  /// 
  /// TD-011 Status: CLOSED - This is the expected production implementation.
  /// 
  /// To enable telemetry, register a custom ITelemetryService implementation
  /// in AppServices.Initialize() that sends metrics to a user-configured endpoint.
  /// The stub methods are available to trace execution flow during development.
  /// </summary>
  internal sealed class TelemetryServiceStub : ITelemetryService
  {
    public void TrackEvent(string eventName, IDictionary<string, object>? properties = null)
    {
      // No-op by design: local-first, privacy-respecting telemetry
      System.Diagnostics.Debug.WriteLine($"[Telemetry] Event: {eventName}");
    }

    public void TrackMetric(string metricName, double value, IDictionary<string, string>? dimensions = null)
    {
      // No-op by design: metrics stay local
      System.Diagnostics.Debug.WriteLine($"[Telemetry] Metric: {metricName}={value}");
    }

    public void TrackException(Exception exception, IDictionary<string, string>? properties = null)
    {
      // Log exceptions locally for debugging
      System.Diagnostics.Debug.WriteLine($"[Telemetry] Exception: {exception.GetType().Name}: {exception.Message}");
    }

    public IDisposable TrackOperation(string operationName) => new TelemetryOperationStub(operationName);

    public void Flush()
    {
      // No-op: no buffered data to flush
    }

    public void ApplyDiagnosticsSettings(object settings)
    {
      // No-op: no external configuration needed
    }
  }

  internal sealed class TelemetryOperationStub : IDisposable
  {
    private readonly string _operationName;
    private readonly System.Diagnostics.Stopwatch _stopwatch;

    public TelemetryOperationStub(string operationName)
    {
      _operationName = operationName;
      _stopwatch = System.Diagnostics.Stopwatch.StartNew();
      System.Diagnostics.Debug.WriteLine($"[Telemetry] Operation started: {operationName}");
    }

    public void Dispose()
    {
      _stopwatch.Stop();
      System.Diagnostics.Debug.WriteLine($"[Telemetry] Operation completed: {_operationName} ({_stopwatch.ElapsedMilliseconds}ms)");
    }
  }
}