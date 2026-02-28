using System;
using System.Collections.Generic;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.UI.Dispatching;
using VoiceStudio.Core.Panels;
using VoiceStudio.Core.Services;
using VoiceStudio.Core.State;
using VoiceStudio.App.Core.Commands;
using VoiceStudio.App.UseCases;
using VoiceStudio.App.ViewModels;

namespace VoiceStudio.App.Services
{
  /// <summary>
  /// Static DI facade used by ServiceProvider shim and Views/ViewModels.
  /// Initialize() must be called at app startup (e.g. from App constructor via ServiceProvider.Initialize()).
  /// </summary>
  public static class AppServices
  {
    private static IServiceProvider? _provider;
    private static ToastNotificationService? _toastOverride;

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

      // Config and backend - use environment variable with fallback to 8001
      var apiHost = Environment.GetEnvironmentVariable("VOICESTUDIO_API_HOST") ?? "localhost";
      var apiPort = Environment.GetEnvironmentVariable("VOICESTUDIO_API_PORT") ?? "8001";
      var baseUrl = $"http://{apiHost}:{apiPort}";
      var wsUrl = $"ws://{apiHost}:{apiPort}/ws/realtime";
      services.AddSingleton(new BackendClientConfig { BaseUrl = baseUrl, WebSocketUrl = wsUrl });
      services.AddSingleton<IBackendClient, BackendClient>();

      // Use cases
      services.AddSingleton<IProfilesUseCase, ProfilesUseCase>();

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
      services.AddSingleton<ISettingsService, SettingsService>();
      services.AddSingleton<IUpdateService, UpdateService>();
      services.AddSingleton<IPanelRegistry, PanelRegistry>();
      services.AddSingleton<PanelStateService>();
      services.AddSingleton<INavigationService, NavigationService>();
      services.AddSingleton<IErrorDialogService, ErrorDialogService>();
      services.AddSingleton<IErrorLoggingService, ErrorLoggingService>();
      services.AddSingleton<IAuditLoggingService>(sp => new AuditLoggingService(sp.GetRequiredService<IErrorLoggingService>()));
      services.AddSingleton<IHelpOverlayService, HelpOverlayService>();
      services.AddSingleton<IAudioPlayerService, AudioPlayerService>();
      services.AddSingleton<OperationQueueService>();
      services.AddSingleton<StatePersistenceService>();
      services.AddSingleton<StateCacheService>();
      services.AddSingleton<GracefulDegradationService>();
      services.AddSingleton<PluginManager>();
      services.AddSingleton<RealTimeQualityService>();
      // NOTE: ToastNotificationService requires a StackPanel container and cannot be auto-resolved.
      // It must be registered manually via RegisterToastNotificationService() after UI is created.
      services.AddSingleton<MultiSelectService>();
      services.AddSingleton<DragDropVisualFeedbackService>();
      services.AddSingleton<ContextMenuService>();
      services.AddSingleton<UndoRedoService>();
      services.AddSingleton<RecentProjectsService>();
      services.AddSingleton<ToolbarConfigurationService>();
      services.AddSingleton<StatusBarActivityService>();
      services.AddSingleton<KeyboardShortcutService>();
      services.AddSingleton<IUnifiedCommandRegistry>(sp =>
        new UnifiedCommandRegistry(sp.GetRequiredService<KeyboardShortcutService>()));
      services.AddSingleton<CommandRouter>(sp =>
        new CommandRouter(sp.GetRequiredService<IUnifiedCommandRegistry>()));
      services.AddSingleton<CollaborationService>();
      services.AddSingleton<BackendProcessManager>();
      services.AddSingleton<IFeatureFlagsService, FeatureFlagsService>();
      services.AddSingleton<IErrorPresentationService, ErrorPresentationService>();
      services.AddSingleton<IAnalyticsService, AnalyticsService>();
      services.AddSingleton<EngineManager>();

      // Theme service: unified theme management with persistence
      services.AddSingleton<IUnifiedThemeService, ThemeManager>();

      // Event aggregator for cross-panel synchronization (Phase 4)
      services.AddSingleton<IEventAggregator, EventAggregator>();
      
      // Context manager for centralized active state (Panel Architecture Phase 2)
      // Uses AppStateStore for undo/redo support (Phase 5)
      services.AddSingleton<IContextManager>(sp => new ContextManager(
          sp.GetRequiredService<IEventAggregator>(),
          sp.GetService<AppStateStore>()));
      
      // Layout and Workspace services (Panel Architecture Phase 3)
      services.AddSingleton<ILayoutService, LayoutService>();
      services.AddSingleton<IWorkspaceService>(sp => new WorkspaceService(
          sp.GetService<ILayoutService>(),
          sp.GetService<IEventAggregator>(),
          sp.GetService<Microsoft.Extensions.Logging.ILogger<WorkspaceService>>()));
      
      // Central state store with undo/redo support (Panel Architecture Phase 5)
      services.AddSingleton<IAppStateStore, AppStateStore>();
      services.AddSingleton<AppStateStore>();
      
      // Selection navigation stack for back/forward navigation (Panel Architecture Phase 5)
      services.AddSingleton<ISelectionStack, SelectionStack>();
      
      // Drag and Drop service (Panel Architecture Phase 4)
      // Injecting StateStore for command pattern support
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
          sp.GetService<Microsoft.Extensions.Logging.ILogger<SelectionBroadcastService>>()));
      
      // Synchronized scroll service for cross-panel scroll coordination (Panel Architecture Phase D)
      services.AddSingleton<ISynchronizedScrollService>(sp => new SynchronizedScrollService(
          sp.GetService<IEventAggregator>(),
          sp.GetService<Microsoft.Extensions.Logging.ILogger<SynchronizedScrollService>>()));
      
      // Event replay service for debug capture and replay bundles (Panel Architecture Phase D)
      services.AddSingleton<IEventReplayService>(sp => new EventReplayService(
          sp.GetService<IEventAggregator>(),
          sp.GetService<IAppStateStore>(),
          sp.GetService<IContextManager>(),
          sp.GetService<Microsoft.Extensions.Logging.ILogger<EventReplayService>>()));
      
      // Workflow coordinator for multi-panel sequences (Panel Workflow Integration)
      services.AddSingleton<IWorkflowCoordinatorService, WorkflowCoordinatorService>();

      // ITelemetryService: stub when no dedicated implementation (GAP-003 follow-up can add real impl)
      services.AddSingleton<ITelemetryService, TelemetryServiceStub>();

      // IProjectRepository: JSON-based local storage (local-first, no cloud)
      services.AddSingleton<IProjectRepository, JsonProjectRepository>();

      // ISecretsService: use available implementation
      services.AddSingleton<ISecretsService, DevVaultSecretsService>();

      // Module loader for UI modules
      services.AddSingleton<ModuleLoader>();

      // Panel lazy loading
      services.AddSingleton<PanelLoader>();

      // Error coordination service
      services.AddSingleton<IErrorCoordinator, ErrorCoordinator>();

      // ViewModel factory (needs service provider, so use factory registration)
      services.AddSingleton<IViewModelFactory>(sp => new ViewModelFactory(sp));

      _provider = services.BuildServiceProvider();
    }

    public static T? GetService<T>() where T : class => (T?)_provider?.GetService(typeof(T));
    public static T GetRequiredService<T>() where T : class =>
        GetService<T>() ?? throw new InvalidOperationException($"Service not registered: {typeof(T).FullName}");

    public static IViewModelContext GetViewModelContext() => GetRequiredService<IViewModelContext>();

    public static void RegisterToastNotificationService(ToastNotificationService service) => _toastOverride = service;

    // Typed accessors (forward to GetService / GetRequiredService)
    public static IBackendClient GetBackendClient() => GetRequiredService<IBackendClient>();
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
    public static PluginManager GetPluginManager() => GetRequiredService<PluginManager>();
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
    public static IProjectRepository GetProjectRepository() => GetRequiredService<IProjectRepository>();
    public static IProjectRepository? TryGetProjectRepository() => GetService<IProjectRepository>();
    public static ModuleLoader GetModuleLoader() => GetRequiredService<ModuleLoader>();
    public static ModuleLoader? TryGetModuleLoader() => GetService<ModuleLoader>();
    public static PanelLoader GetPanelLoader() => GetRequiredService<PanelLoader>();
    public static PanelLoader? TryGetPanelLoader() => GetService<PanelLoader>();
    public static IErrorCoordinator GetErrorCoordinator() => GetRequiredService<IErrorCoordinator>();
    public static IErrorCoordinator? TryGetErrorCoordinator() => GetService<IErrorCoordinator>();
    public static IViewModelFactory GetViewModelFactory() => GetRequiredService<IViewModelFactory>();
    public static IViewModelFactory? TryGetViewModelFactory() => GetService<IViewModelFactory>();
    public static IUnifiedCommandRegistry GetCommandRegistry() => GetRequiredService<IUnifiedCommandRegistry>();
    public static IUnifiedCommandRegistry? TryGetCommandRegistry() => GetService<IUnifiedCommandRegistry>();
    public static CommandRouter GetCommandRouter() => GetRequiredService<CommandRouter>();
    public static CommandRouter? TryGetCommandRouter() => GetService<CommandRouter>();
    public static IDialogService GetDialogService() => GetRequiredService<IDialogService>();
    public static IDialogService? TryGetDialogService() => GetService<IDialogService>();
    public static BackendProcessManager GetBackendProcessManager() => GetRequiredService<BackendProcessManager>();
    public static BackendProcessManager? TryGetBackendProcessManager() => GetService<BackendProcessManager>();
    public static IUnifiedThemeService GetThemeService() => GetRequiredService<IUnifiedThemeService>();
    public static IUnifiedThemeService? TryGetThemeService() => GetService<IUnifiedThemeService>();
    public static IWebSocketClientFactory GetWebSocketClientFactory() => GetRequiredService<IWebSocketClientFactory>();
    public static IWebSocketClientFactory? TryGetWebSocketClientFactory() => GetService<IWebSocketClientFactory>();
    public static IEventAggregator GetEventAggregator() => GetRequiredService<IEventAggregator>();
    public static IEventAggregator? TryGetEventAggregator() => GetService<IEventAggregator>();
    public static IContextManager GetContextManager() => GetRequiredService<IContextManager>();
    public static IContextManager? TryGetContextManager() => GetService<IContextManager>();
    public static ILayoutService GetLayoutService() => GetRequiredService<ILayoutService>();
    public static ILayoutService? TryGetLayoutService() => GetService<ILayoutService>();
    public static IWorkspaceService GetWorkspaceService() => GetRequiredService<IWorkspaceService>();
    public static IWorkspaceService? TryGetWorkspaceService() => GetService<IWorkspaceService>();
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