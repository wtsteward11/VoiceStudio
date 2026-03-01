using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using VoiceStudio.App.Logging;
using VoiceStudio.Core.Services;
using System.Diagnostics;

namespace VoiceStudio.App.Services
{
    /// <summary>
    /// Manages deferred initialization of non-critical services to improve startup time.
    /// Services are initialized after the main window is visible.
    /// </summary>
    public class DeferredServiceInitializer
    {
        private readonly List<DeferredService> _services;
        private readonly SemaphoreSlim _initLock;
        private bool _initialized;
        
        /// <summary>
        /// Event raised when deferred initialization starts.
        /// </summary>
        public event EventHandler? InitializationStarted;
        
        /// <summary>
        /// Event raised when all deferred services are initialized.
        /// </summary>
        public event EventHandler<DeferredInitCompletedEventArgs>? InitializationCompleted;
        
        /// <summary>
        /// Event raised for each service initialization.
        /// </summary>
        public event EventHandler<ServiceInitializedEventArgs>? ServiceInitialized;
        
        public DeferredServiceInitializer()
        {
            _services = new List<DeferredService>();
            _initLock = new SemaphoreSlim(1, 1);
        }
        
        /// <summary>
        /// Registers a service for deferred initialization.
        /// </summary>
        public void Register<T>(
            string serviceName,
            Func<T> factory,
            Action<T> initializer,
            ServicePriority priority = ServicePriority.Normal)
        {
            _services.Add(new DeferredService
            {
                Name = serviceName,
                Priority = priority,
                Initialize = () =>
                {
                    var instance = factory();
                    initializer(instance);
                }
            });
        }
        
        /// <summary>
        /// Registers an async initialization action.
        /// </summary>
        public void RegisterAsync(
            string serviceName,
            Func<CancellationToken, Task> initializeAsync,
            ServicePriority priority = ServicePriority.Normal)
        {
            _services.Add(new DeferredService
            {
                Name = serviceName,
                Priority = priority,
                InitializeAsync = initializeAsync
            });
        }
        
        /// <summary>
        /// Runs all deferred initializations.
        /// Call after main window is visible.
        /// </summary>
        public async Task InitializeAllAsync(CancellationToken cancellationToken = default)
        {
            if (_initialized)
                return;
            
            await _initLock.WaitAsync(cancellationToken);
            
            try
            {
                if (_initialized)
                    return;
                
                var stopwatch = Stopwatch.StartNew();
                InitializationStarted?.Invoke(this, EventArgs.Empty);
                ErrorLogger.LogDebug("[DeferredInit] Starting deferred service initialization", "DeferredServiceInitializer");
                
                // Sort by priority (highest first)
                _services.Sort((a, b) => b.Priority.CompareTo(a.Priority));
                
                var results = new List<ServiceInitResult>();
                
                foreach (var service in _services)
                {
                    if (cancellationToken.IsCancellationRequested)
                        break;
                    
                    var serviceStopwatch = Stopwatch.StartNew();
                    var success = true;
                    string? error = null;
                    
                    try
                    {
                        if (service.InitializeAsync != null)
                        {
                            await service.InitializeAsync(cancellationToken);
                        }
                        else if (service.Initialize != null)
                        {
                            await Task.Run(service.Initialize, cancellationToken);
                        }
                    }
                    catch (Exception ex)
                    {
                        success = false;
                        error = ex.Message;
                        ErrorLogger.LogWarning($"Deferred init failed for {service.Name}: {ex.Message}", "DeferredInit");
                    }
                    
                    serviceStopwatch.Stop();
                    
                    var result = new ServiceInitResult
                    {
                        ServiceName = service.Name,
                        Success = success,
                        DurationMs = serviceStopwatch.ElapsedMilliseconds,
                        Error = error
                    };
                    
                    results.Add(result);
                    ServiceInitialized?.Invoke(this, new ServiceInitializedEventArgs(result));
                    
                    // Small yield to keep UI responsive
                    await Task.Delay(1, cancellationToken);
                }
                
                stopwatch.Stop();
                _initialized = true;
                
                var completedArgs = new DeferredInitCompletedEventArgs
                {
                    TotalDurationMs = stopwatch.ElapsedMilliseconds,
                    Results = results
                };
                
                InitializationCompleted?.Invoke(this, completedArgs);
                ErrorLogger.LogInfo($"[DeferredInit] Deferred initialization completed in {stopwatch.ElapsedMilliseconds}ms", "DeferredServiceInitializer");
            }
            finally
            {
                _initLock.Release();
            }
        }
        
        /// <summary>
        /// Gets whether deferred initialization has completed.
        /// </summary>
        public bool IsInitialized => _initialized;
        
        /// <summary>
        /// Creates a standard set of deferred initializations for VoiceStudio.
        /// </summary>
        public static DeferredServiceInitializer CreateDefault(IServiceProvider serviceProvider)
        {
            var initializer = new DeferredServiceInitializer();
            
            // These services don't need to be ready at startup
            // They can be initialized after the window is visible
            
            // Low priority: Plugin discovery
            initializer.RegisterAsync(
                "PluginDiscovery",
                async ct =>
                {
                    var pluginManager = serviceProvider.GetService(typeof(PluginManager)) as PluginManager;
                    if (pluginManager != null)
                    {
                        await pluginManager.LoadPluginsAsync();
                    }
                },
                ServicePriority.Low);
            
            // Low priority: Recent projects loading
            // Note: RecentProjectsService loads on construction, but we ensure it's resolved
            initializer.RegisterAsync(
                "RecentProjects",
                async ct =>
                {
                    // Just resolve the service to ensure it's instantiated
                    _ = serviceProvider.GetService(typeof(RecentProjectsService)) as RecentProjectsService;
                    await Task.CompletedTask;
                },
                ServicePriority.Low);
            
            // Normal priority: Crash recovery check
            initializer.RegisterAsync(
                "CrashRecoveryCheck",
                async ct =>
                {
                    var crashRecovery = serviceProvider.GetService(typeof(CrashRecoveryService)) as CrashRecoveryService;
                    if (crashRecovery != null)
                    {
                        await crashRecovery.InitializeAsync();
                    }
                },
                ServicePriority.Normal);
            
            // High priority: Start backend process if not already running
            initializer.RegisterAsync(
                "BackendAutoStart",
                async ct =>
                {
                    var bpm = serviceProvider.GetService(typeof(BackendProcessManager)) as BackendProcessManager;
                    if (bpm != null)
                    {
                        var started = await bpm.EnsureBackendRunningAsync(ct);
                        ErrorLogger.LogInfo(
                            $"[DeferredInit] Backend auto-start: {(started ? "running" : "failed")}",
                            "DeferredServiceInitializer");
                    }
                },
                ServicePriority.High);

            // Normal priority: Backend health check (runs after auto-start)
            initializer.RegisterAsync(
                "BackendHealthCheck",
                async ct =>
                {
                    var backendClient = serviceProvider.GetService(typeof(IBackendClient)) as IBackendClient;
                    if (backendClient != null)
                    {
                        try
                        {
                            var healthy = await backendClient.CheckHealthAsync(ct);
                            ErrorLogger.LogInfo(
                                $"[DeferredInit] Backend health: {(healthy ? "connected" : "unreachable")}",
                                "DeferredServiceInitializer");
                        }
                        catch (Exception ex)
                        {
                            ErrorLogger.LogWarning(
                                $"[DeferredInit] Backend health check failed: {ex.Message}",
                                "DeferredServiceInitializer");
                        }
                    }
                },
                ServicePriority.Normal);

            // Normal priority: Initialize EngineManager (fetches real engine list from backend)
            initializer.RegisterAsync(
                "EngineManagerInit",
                async ct =>
                {
                    var engineManager = serviceProvider.GetService(typeof(EngineManager)) as EngineManager;
                    if (engineManager != null)
                    {
                        await engineManager.InitializeAsync(ct);
                        var count = engineManager.GetEngines().Count();
                        ErrorLogger.LogInfo(
                            $"[DeferredInit] EngineManager initialized: {count} engine(s) discovered",
                            "DeferredServiceInitializer");

                        if (count == 0)
                        {
                            ErrorLogger.LogInfo(
                                "[DeferredInit] No engines installed. Engine Setup Wizard available via Settings > Engines.",
                                "DeferredServiceInitializer");
                        }
                    }
                },
                ServicePriority.Normal);

            // Normal priority: Ensure a default project exists and set as active (unblocks Import, Timeline, Effects)
            initializer.RegisterAsync(
                "DefaultProjectCheck",
                async ct =>
                {
                    var backendClient = serviceProvider.GetService(typeof(IBackendClient)) as IBackendClient;
                    if (backendClient is BackendClient client)
                    {
                        try
                        {
                            var projects = await client.GetProjectsAsync(ct);
                            VoiceStudio.Core.Models.Project? activeProject = null;

                            if (projects == null || projects.Count == 0)
                            {
                                activeProject = await client.CreateProjectAsync("My Project", "Default project created automatically", ct);
                                ErrorLogger.LogInfo(
                                    $"[DeferredInit] Default project created: {activeProject?.Id ?? "unknown"}",
                                    "DeferredServiceInitializer");
                            }
                            else
                            {
                                activeProject = projects[0];
                                ErrorLogger.LogInfo(
                                    $"[DeferredInit] Found {projects.Count} existing project(s), using: {activeProject.Name}",
                                    "DeferredServiceInitializer");
                            }

                            if (activeProject != null)
                            {
                                var bootstrapper = VoiceStudio.App.Commands.CommandHandlerBootstrapper.Instance;
                                if (bootstrapper?.FileHandler != null)
                                {
                                    bootstrapper.FileHandler.SetActiveProject(activeProject);
                                    ErrorLogger.LogInfo(
                                        $"[DeferredInit] Active project set: {activeProject.Name}",
                                        "DeferredServiceInitializer");
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            ErrorLogger.LogWarning(
                                $"[DeferredInit] Project check failed: {ex.Message}",
                                "DeferredServiceInitializer");
                        }
                    }
                },
                ServicePriority.Normal);

            // Low priority: Start backend connection monitor (polls health, fires Connected/Disconnected events)
            initializer.RegisterAsync(
                "BackendConnectionMonitor",
                async ct =>
                {
                    var monitor = serviceProvider.GetService(typeof(BackendConnectionMonitor)) as BackendConnectionMonitor;
                    if (monitor != null)
                    {
                        monitor.StartMonitoring();
                        ErrorLogger.LogInfo("[DeferredInit] Backend connection monitor started", "DeferredServiceInitializer");
                    }
                    await Task.CompletedTask;
                },
                ServicePriority.Low);

            // Low priority: Start status bar activity monitoring
            initializer.RegisterAsync(
                "StatusBarActivityMonitor",
                async ct =>
                {
                    var activityService = serviceProvider.GetService(typeof(StatusBarActivityService)) as StatusBarActivityService;
                    if (activityService != null)
                    {
                        activityService.StartMonitoring();
                        ErrorLogger.LogInfo("[DeferredInit] Status bar activity monitor started", "DeferredServiceInitializer");
                    }
                    await Task.CompletedTask;
                },
                ServicePriority.Low);

            return initializer;
        }
    }
    
    public enum ServicePriority
    {
        Low = 0,
        Normal = 1,
        High = 2,
        Critical = 3
    }
    
    internal class DeferredService
    {
        public string Name { get; set; } = string.Empty;
        public ServicePriority Priority { get; set; }
        public Action? Initialize { get; set; }
        public Func<CancellationToken, Task>? InitializeAsync { get; set; }
    }
    
    public class ServiceInitResult
    {
        public string ServiceName { get; set; } = string.Empty;
        public bool Success { get; set; }
        public long DurationMs { get; set; }
        public string? Error { get; set; }
    }
    
    public class ServiceInitializedEventArgs : EventArgs
    {
        public ServiceInitResult Result { get; }
        
        public ServiceInitializedEventArgs(ServiceInitResult result)
        {
            Result = result;
        }
    }
    
    public class DeferredInitCompletedEventArgs : EventArgs
    {
        public long TotalDurationMs { get; set; }
        public List<ServiceInitResult> Results { get; set; } = new();
    }
}
