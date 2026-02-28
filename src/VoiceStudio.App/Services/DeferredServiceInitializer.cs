using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using VoiceStudio.App.Logging;
using VoiceStudio.Core.Services;

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
                Debug.WriteLine("[DeferredInit] Starting deferred service initialization");
                
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
                Debug.WriteLine($"[DeferredInit] Deferred initialization completed in {stopwatch.ElapsedMilliseconds}ms");
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
            
            // Normal priority: Backend health check
            initializer.RegisterAsync(
                "BackendHealthCheck",
                async ct =>
                {
                    var backendClient = serviceProvider.GetService(typeof(IBackendClient)) as IBackendClient;
                    if (backendClient != null)
                    {
                        try
                        {
                            await backendClient.CheckHealthAsync(ct);
                        }
                        // ALLOWED: empty catch - Backend may not be running yet, acceptable
                        catch
                        {
                        }
                    }
                },
                ServicePriority.Normal);
            
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
