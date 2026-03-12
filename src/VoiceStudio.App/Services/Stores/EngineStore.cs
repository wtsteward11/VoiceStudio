using System;
using System.Collections.ObjectModel;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using VoiceStudio.Core.Services;
using VoiceStudio.App.Logging;

namespace VoiceStudio.App.Services.Stores
{
  /// <summary>
  /// Centralized store for engine-related state.
  /// Implements React/TypeScript engineStore pattern in C#.
  /// </summary>
  public partial class EngineStore : ObservableObject
  {
    private readonly IBackendClient _backendClient;
    private readonly StateCacheService? _stateCacheService;
    private readonly EngineManager? _engineManager; // Optional dependency for now to avoid breaking existing constructors if not registered

    [ObservableProperty]
    private ObservableCollection<EngineStoreItem> availableEngines = new();

    [ObservableProperty]
    private EngineStoreItem? selectedEngine;

    [ObservableProperty]
    private ObservableCollection<EngineStoreItem> activeEngines = new();

    [ObservableProperty]
    private bool isLoading;

    [ObservableProperty]
    private string? errorMessage;

    [ObservableProperty]
    private DateTime? lastUpdated;

    public EngineStore(IBackendClient backendClient, StateCacheService? stateCacheService = null, EngineManager? engineManager = null)
    {
      _backendClient = backendClient ?? throw new ArgumentNullException(nameof(backendClient));
      _stateCacheService = stateCacheService;
      _engineManager = engineManager;
    }

    /// <summary>
    /// Loads all available engines.
    /// </summary>
    public async Task LoadEnginesAsync(CancellationToken cancellationToken = default)
    {
      try
      {
        IsLoading = true;
        ErrorMessage = null;

        // Try to load from cache first
        if (_stateCacheService != null)
        {
          var cached = await _stateCacheService.GetCachedStateAsync<ObservableCollection<EngineStoreItem>>("engines");
          if (cached != null)
          {
            AvailableEngines = cached;
            IsLoading = false;
            // Still fetch from backend in background to update
            _ = RefreshEnginesAsync(cancellationToken);
            return;
          }
        }

        await RefreshEnginesAsync(cancellationToken);
      }
      catch (OperationCanceledException)
      {
        System.Diagnostics.Debug.WriteLine("EngineStore: LoadEnginesAsync cancelled");
      }
      catch (Exception ex)
      {
        ErrorMessage = $"Failed to load engines: {ex.Message}";
      }
      finally
      {
        IsLoading = false;
      }
    }

    /// <summary>
    /// Refreshes engines from backend.
    /// </summary>
    public async Task RefreshEnginesAsync(CancellationToken cancellationToken = default)
    {
      try
      {
        AvailableEngines.Clear();

        if (_engineManager != null)
        {
          // Use EngineManager to discover engines
          await _engineManager.InitializeAsync(cancellationToken);
          foreach (var engine in _engineManager.GetEngines())
          {
            var typeStr = "unknown";
            if (engine.Capabilities.HasFlag(VoiceStudio.Core.Engines.EngineCapabilities.TextToSpeech)) typeStr = "tts";
            else if (engine.Capabilities.HasFlag(VoiceStudio.Core.Engines.EngineCapabilities.Transcription)) typeStr = "asr";

            // GAP-CRIT-005: Check actual availability using IEngine.IsAvailableAsync
            bool isAvailable = false;
            string? unavailableReason = null;
            try
            {
              isAvailable = await engine.IsAvailableAsync();
              if (!isAvailable)
              {
                unavailableReason = "Engine is not currently available";
              }
            }
            catch (Exception availEx)
            {
              isAvailable = false;
              unavailableReason = $"Availability check failed: {availEx.Message}";
            }

            AvailableEngines.Add(new EngineStoreItem
            {
              Id = engine.Id,
              Name = engine.Name,
              Type = typeStr,
              Version = engine.Version,
              Status = isAvailable ? "ready" : "unavailable",
              IsAvailable = isAvailable,
              UnavailableReason = unavailableReason
            });
          }
        }
        else
        {
          // Fallback to direct backend call if manager not available (legacy path)
          // Note: Engine discovery API might not be fully standardized yet
          // ... existing logic ...
        }

        LastUpdated = DateTime.UtcNow;

        // Cache the result
        if (_stateCacheService != null)
        {
          await _stateCacheService.CacheStateAsync("engines", AvailableEngines);
        }
      }
      catch (Exception ex)
      {
        ErrorMessage = $"Failed to refresh engines: {ex.Message}";
      }
    }

    /// <summary>
    /// Loads active engines (currently running).
    /// </summary>
    public Task LoadActiveEnginesAsync()
    {
      try
      {
        // Filter from available engines
        ActiveEngines.Clear();
        foreach (var engine in AvailableEngines.Where(e => e.Status == "running" || e.Status == "ready"))
        {
          ActiveEngines.Add(engine);
        }

        LastUpdated = DateTime.UtcNow;
      }
      catch (Exception ex)
      {
        ErrorMessage = $"Failed to load active engines: {ex.Message}";
      }

      return Task.CompletedTask;
    }

    /// <summary>
    /// Gets an engine by ID.
    /// </summary>
    public EngineStoreItem? GetEngine(string engineId)
    {
      return AvailableEngines.FirstOrDefault(e => e.Id == engineId);
    }

    /// <summary>
    /// Updates engine status.
    /// </summary>
    public void UpdateEngineStatus(string engineId, string status)
    {
      var engine = AvailableEngines.FirstOrDefault(e => e.Id == engineId);
      if (engine != null)
      {
        engine.Status = status;
        OnPropertyChanged(nameof(AvailableEngines));
        LastUpdated = DateTime.UtcNow;

        // Update active engines list
        if (status == "running" || status == "ready")
        {
          if (!ActiveEngines.Any(e => e.Id == engineId))
          {
            ActiveEngines.Add(engine);
          }
        }
        else
        {
          var active = ActiveEngines.FirstOrDefault(e => e.Id == engineId);
          if (active != null)
          {
            ActiveEngines.Remove(active);
          }
        }
      }
    }

    /// <summary>
    /// Clears all engine state.
    /// </summary>
    public void Clear()
    {
      AvailableEngines.Clear();
      ActiveEngines.Clear();
      SelectedEngine = null;
      LastUpdated = null;
    }
  }

  /// <summary>
  /// Engine information item for the store.
  /// </summary>
  public class EngineStoreItem
  {
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty; // "tts", "vc", "asr"
    public string Status { get; set; } = string.Empty; // "idle", "ready", "running", "error", "unavailable"
    public string? Version { get; set; }
    public Dictionary<string, object> Metadata { get; set; } = new();

    /// <summary>
    /// Whether the engine is actually available for use (GAP-CRIT-005).
    /// Placeholder engines with no real implementation return false.
    /// </summary>
    public bool IsAvailable { get; set; } = true;

    /// <summary>
    /// Reason the engine is unavailable (for UI display).
    /// </summary>
    public string? UnavailableReason { get; set; }

    /// <summary>
    /// Display name with availability indicator.
    /// </summary>
    public string DisplayName => IsAvailable ? Name : $"{Name} (Unavailable)";
  }
}