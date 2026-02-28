using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using VoiceStudio.Core.Services;

namespace VoiceStudio.App.Services.Stores
{
  /// <summary>
  /// Centralized store for system-related state.
  /// Implements React/TypeScript systemStore pattern in C#.
  /// </summary>
  public partial class SystemStore : ObservableObject
  {
    private readonly IBackendClient _backendClient;
    private readonly StateCacheService? _stateCacheService;

    [ObservableProperty]
    private bool isBackendConnected = true;

    [ObservableProperty]
    private string? backendUrl;

    [ObservableProperty]
    private DateTime? lastBackendCheck;

    [ObservableProperty]
    private bool isInitialized;

    [ObservableProperty]
    private string? errorMessage;

    [ObservableProperty]
    private ObservableCollection<string> recentErrors = new();

    [ObservableProperty]
    private int errorCount;

    [ObservableProperty]
    private DateTime? lastUpdated;

    /// <summary>
    /// Last recorded backend latency in milliseconds.
    /// </summary>
    [ObservableProperty]
    private double? latencyMs;

    /// <summary>
    /// Number of consecutive backend failures.
    /// </summary>
    [ObservableProperty]
    private int consecutiveFailures;

    public SystemStore(IBackendClient backendClient, StateCacheService? stateCacheService = null)
    {
      _backendClient = backendClient ?? throw new ArgumentNullException(nameof(backendClient));
      _stateCacheService = stateCacheService;

      // Initialize backend URL from client
      if (backendClient is BackendClient client)
      {
        BackendUrl = client.BaseAddress?.ToString() ?? "http://localhost:8001";
      }
    }

    /// <summary>
    /// Initializes the system store.
    /// </summary>
    public async Task InitializeAsync()
    {
      try
      {
        // Check backend connection
        await CheckBackendConnectionAsync();

        IsInitialized = true;
        LastUpdated = DateTime.UtcNow;
      }
      catch (Exception ex)
      {
        ErrorMessage = $"Failed to initialize system store: {ex.Message}";
        AddError(ex.Message);
      }
    }

    /// <summary>
    /// Checks backend connection status.
    /// </summary>
    public Task CheckBackendConnectionAsync()
    {
      try
      {
        // Try a simple health check or lightweight endpoint
        // For now, we'll use the circuit breaker state as an indicator
        IsBackendConnected = _backendClient.IsConnected;
        LastBackendCheck = DateTime.UtcNow;

        LastUpdated = DateTime.UtcNow;
      }
      catch (Exception ex)
      {
        IsBackendConnected = false;
        AddError($"Backend connection check failed: {ex.Message}");
      }

      return Task.CompletedTask;
    }

    /// <summary>
    /// Adds an error to the recent errors list.
    /// </summary>
    public void AddError(string error)
    {
      if (string.IsNullOrEmpty(error))
        return;

      RecentErrors.Insert(0, $"[{DateTime.UtcNow:HH:mm:ss}] {error}");

      // Keep only last 50 errors
      while (RecentErrors.Count > 50)
      {
        RecentErrors.RemoveAt(RecentErrors.Count - 1);
      }

      ErrorCount = RecentErrors.Count;
      ErrorMessage = error;
      LastUpdated = DateTime.UtcNow;
    }

    /// <summary>
    /// Clears all errors.
    /// </summary>
    public void ClearErrors()
    {
      RecentErrors.Clear();
      ErrorCount = 0;
      ErrorMessage = null;
      LastUpdated = DateTime.UtcNow;
    }

    /// <summary>
    /// Updates backend connection status.
    /// </summary>
    public void UpdateBackendConnection(bool isConnected)
    {
      IsBackendConnected = isConnected;
      LastBackendCheck = DateTime.UtcNow;
      LastUpdated = DateTime.UtcNow;
      
      if (isConnected)
      {
        ConsecutiveFailures = 0;
      }
    }

    /// <summary>
    /// Records a successful backend operation with latency measurement.
    /// </summary>
    /// <param name="latencyMilliseconds">The measured latency in milliseconds.</param>
    public void RecordSuccess(double latencyMilliseconds)
    {
      LatencyMs = latencyMilliseconds;
      ConsecutiveFailures = 0;
      IsBackendConnected = true;
      LastBackendCheck = DateTime.UtcNow;
      LastUpdated = DateTime.UtcNow;
    }

    /// <summary>
    /// Records a backend failure.
    /// </summary>
    public void RecordFailure(string? error = null)
    {
      ConsecutiveFailures++;
      if (!string.IsNullOrEmpty(error))
      {
        AddError(error);
      }
      LastUpdated = DateTime.UtcNow;
    }

    /// <summary>
    /// Clears all system state.
    /// </summary>
    public void Clear()
    {
      RecentErrors.Clear();
      ErrorCount = 0;
      ErrorMessage = null;
      LastUpdated = null;
      LatencyMs = null;
      ConsecutiveFailures = 0;
    }
  }
}