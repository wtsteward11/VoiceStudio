using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using VoiceStudio.App.Logging;

namespace VoiceStudio.App.Services
{
    /// <summary>
    /// Provides lazy loading and caching for UI panels.
    /// Improves startup time by deferring panel initialization until needed.
    /// </summary>
    /// <remarks>
    /// DEPRECATED (GAP-F04): This class is being phased out in favor of the unified
    /// <see cref="PanelRegistry"/> which uses <see cref="IViewModelFactory"/> for
    /// DI-based panel creation. New code should use <see cref="IPanelRegistry.CreatePanel"/>
    /// instead of this loader. This class will be removed in a future release.
    /// </remarks>
    [Obsolete("Use PanelRegistry.CreatePanel() instead. This class will be removed in a future release.")]
    public class PanelLoader : IDisposable
    {
        private readonly ConcurrentDictionary<string, PanelInfo> _panelRegistry;
        private readonly ConcurrentDictionary<string, UserControl> _loadedPanels;
        private readonly SemaphoreSlim _loadLock;
        private bool _disposed;
        
        /// <summary>
        /// Event raised when a panel starts loading.
        /// </summary>
        public event EventHandler<PanelLoadingEventArgs>? PanelLoading;
        
        /// <summary>
        /// Event raised when a panel finishes loading.
        /// </summary>
        public event EventHandler<PanelLoadedEventArgs>? PanelLoaded;
        
        public PanelLoader()
        {
            _panelRegistry = new ConcurrentDictionary<string, PanelInfo>();
            _loadedPanels = new ConcurrentDictionary<string, UserControl>();
            _loadLock = new SemaphoreSlim(1, 1);
        }
        
        /// <summary>
        /// Registers a panel type for lazy loading.
        /// </summary>
        public void RegisterPanel<T>(
            string panelId, 
            PanelPriority priority = PanelPriority.Normal,
            bool preloadOnIdle = false) where T : UserControl, new()
        {
            _panelRegistry[panelId] = new PanelInfo
            {
                PanelId = panelId,
                PanelType = typeof(T),
                Priority = priority,
                PreloadOnIdle = preloadOnIdle,
                Factory = () => new T(),
            };
        }
        
        /// <summary>
        /// Registers a panel with a custom factory.
        /// </summary>
        public void RegisterPanel(
            string panelId,
            Func<UserControl> factory,
            PanelPriority priority = PanelPriority.Normal,
            bool preloadOnIdle = false)
        {
            _panelRegistry[panelId] = new PanelInfo
            {
                PanelId = panelId,
                PanelType = null,
                Priority = priority,
                PreloadOnIdle = preloadOnIdle,
                Factory = factory,
            };
        }
        
        /// <summary>
        /// Gets a panel, loading it if necessary.
        /// </summary>
        public async Task<UserControl?> GetPanelAsync(
            string panelId, 
            CancellationToken cancellationToken = default)
        {
            // Return cached panel if already loaded
            if (_loadedPanels.TryGetValue(panelId, out var cached))
            {
                return cached;
            }
            
            // Load the panel
            return await LoadPanelAsync(panelId, cancellationToken);
        }
        
        /// <summary>
        /// Gets a panel synchronously (blocks if not loaded).
        /// Prefer GetPanelAsync for better UI responsiveness.
        /// </summary>
        public UserControl? GetPanel(string panelId)
        {
            if (_loadedPanels.TryGetValue(panelId, out var cached))
            {
                return cached;
            }
            
            // Synchronous load on UI thread
            if (!_panelRegistry.TryGetValue(panelId, out var info))
            {
                return null;
            }
            
            try
            {
                var panel = info.Factory();
                _loadedPanels[panelId] = panel;
                info.IsLoaded = true;
                info.LoadedAt = DateTime.UtcNow;
                return panel;
            }
            catch (Exception ex)
            {
                ErrorLogger.LogError($"Failed to load panel {panelId}: {ex.Message}", "PanelLoader");
                return null;
            }
        }
        
        /// <summary>
        /// Checks if a panel is loaded.
        /// </summary>
        public bool IsPanelLoaded(string panelId)
        {
            return _loadedPanels.ContainsKey(panelId);
        }
        
        /// <summary>
        /// Unloads a panel to free memory.
        /// </summary>
        public void UnloadPanel(string panelId)
        {
            if (_loadedPanels.TryRemove(panelId, out var panel))
            {
                if (_panelRegistry.TryGetValue(panelId, out var info))
                {
                    info.IsLoaded = false;
                    info.LoadedAt = null;
                }
                
                // Dispose if panel implements IDisposable
                if (panel is IDisposable disposable)
                {
                    disposable.Dispose();
                }
                
                Debug.WriteLine($"[PanelLoader] Unloaded panel: {panelId}");
            }
        }
        
        /// <summary>
        /// Preloads high-priority panels in the background.
        /// Call after initial UI is rendered for improved perceived performance.
        /// </summary>
        public async Task PreloadHighPriorityPanelsAsync(
            CancellationToken cancellationToken = default)
        {
            var highPriorityPanels = new List<string>();
            
            foreach (var kvp in _panelRegistry)
            {
                if (kvp.Value.Priority == PanelPriority.High && !kvp.Value.IsLoaded)
                {
                    highPriorityPanels.Add(kvp.Key);
                }
            }
            
            foreach (var panelId in highPriorityPanels)
            {
                if (cancellationToken.IsCancellationRequested)
                    break;
                    
                await LoadPanelAsync(panelId, cancellationToken);
                
                // Small delay to avoid blocking UI
                await Task.Delay(10, cancellationToken);
            }
        }
        
        /// <summary>
        /// Preloads panels marked for idle preloading.
        /// Call when application is idle.
        /// </summary>
        public async Task PreloadIdlePanelsAsync(
            CancellationToken cancellationToken = default)
        {
            foreach (var kvp in _panelRegistry)
            {
                if (cancellationToken.IsCancellationRequested)
                    break;
                    
                if (kvp.Value.PreloadOnIdle && !kvp.Value.IsLoaded)
                {
                    await LoadPanelAsync(kvp.Key, cancellationToken);
                    await Task.Delay(50, cancellationToken);
                }
            }
        }
        
        /// <summary>
        /// Gets statistics about loaded panels.
        /// </summary>
        public PanelLoaderStats GetStats()
        {
            var stats = new PanelLoaderStats
            {
                RegisteredCount = _panelRegistry.Count,
                LoadedCount = _loadedPanels.Count,
            };
            
            foreach (var kvp in _panelRegistry)
            {
                if (kvp.Value.IsLoaded && kvp.Value.LoadedAt.HasValue)
                {
                    stats.LoadedPanels.Add(new PanelLoadInfo
                    {
                        PanelId = kvp.Key,
                        LoadedAt = kvp.Value.LoadedAt.Value,
                        Priority = kvp.Value.Priority
                    });
                }
            }
            
            return stats;
        }
        
        private async Task<UserControl?> LoadPanelAsync(
            string panelId, 
            CancellationToken cancellationToken)
        {
            if (!_panelRegistry.TryGetValue(panelId, out var info))
            {
                return null;
            }
            
            await _loadLock.WaitAsync(cancellationToken);
            
            try
            {
                // Double-check after acquiring lock
                if (_loadedPanels.TryGetValue(panelId, out var existing))
                {
                    return existing;
                }
                
                PanelLoading?.Invoke(this, new PanelLoadingEventArgs(panelId));
                var startTime = DateTime.UtcNow;
                
                UserControl? panel = null;
                
                // Create panel on UI thread
                await DispatcherQueue.GetForCurrentThread().EnqueueAsync(() =>
                {
                    panel = info.Factory();
                });
                
                if (panel != null)
                {
                    _loadedPanels[panelId] = panel;
                    info.IsLoaded = true;
                    info.LoadedAt = DateTime.UtcNow;
                    
                    var loadTime = DateTime.UtcNow - startTime;
                    PanelLoaded?.Invoke(this, new PanelLoadedEventArgs(panelId, loadTime));
                    Debug.WriteLine($"[PanelLoader] Loaded panel {panelId} in {loadTime.TotalMilliseconds:F1}ms");
                }
                
                return panel;
            }
            catch (Exception ex)
            {
                ErrorLogger.LogError($"Failed to load panel {panelId}: {ex.Message}", "PanelLoader");
                return null;
            }
            finally
            {
                _loadLock.Release();
            }
        }
        
        public void Dispose()
        {
            if (_disposed)
                return;
            
            _disposed = true;
            
            // Dispose all loaded panels
            foreach (var kvp in _loadedPanels)
            {
                if (kvp.Value is IDisposable disposable)
                {
                    disposable.Dispose();
                }
            }
            
            _loadedPanels.Clear();
            _loadLock.Dispose();
        }
    }
    
    public enum PanelPriority
    {
        Low = 0,
        Normal = 1,
        High = 2,
        Critical = 3
    }
    
    internal class PanelInfo
    {
        public string PanelId { get; set; } = string.Empty;
        public Type? PanelType { get; set; }
        public PanelPriority Priority { get; set; }
        public bool PreloadOnIdle { get; set; }
        public Func<UserControl> Factory { get; set; } = null!;
        public bool IsLoaded { get; set; }
        public DateTime? LoadedAt { get; set; }
    }
    
    public class PanelLoadingEventArgs : EventArgs
    {
        public string PanelId { get; }
        
        public PanelLoadingEventArgs(string panelId)
        {
            PanelId = panelId;
        }
    }
    
    public class PanelLoadedEventArgs : EventArgs
    {
        public string PanelId { get; }
        public TimeSpan LoadTime { get; }
        
        public PanelLoadedEventArgs(string panelId, TimeSpan loadTime)
        {
            PanelId = panelId;
            LoadTime = loadTime;
        }
    }
    
    public class PanelLoaderStats
    {
        public int RegisteredCount { get; set; }
        public int LoadedCount { get; set; }
        public List<PanelLoadInfo> LoadedPanels { get; set; } = new();
    }
    
    public class PanelLoadInfo
    {
        public string PanelId { get; set; } = string.Empty;
        public DateTime LoadedAt { get; set; }
        public PanelPriority Priority { get; set; }
    }
    
    /// <summary>
    /// Extension methods for DispatcherQueue.
    /// </summary>
    internal static class DispatcherQueueExtensions
    {
        public static Task EnqueueAsync(
            this Microsoft.UI.Dispatching.DispatcherQueue dispatcher,
            Action action)
        {
            var tcs = new TaskCompletionSource();
            
            if (!dispatcher.TryEnqueue(() =>
            {
                try
                {
                    action();
                    tcs.SetResult();
                }
                catch (Exception ex)
                {
                    tcs.SetException(ex);
                }
            }))
            {
                tcs.SetException(new InvalidOperationException("Failed to enqueue action"));
            }
            
            return tcs.Task;
        }
    }
}
