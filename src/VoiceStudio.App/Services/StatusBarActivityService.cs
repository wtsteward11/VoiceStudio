using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using VoiceStudio.Core.Services;

namespace VoiceStudio.App.Services
{
  /// <summary>
  /// Service for monitoring and reporting status bar activity indicators.
  /// Implements IDEA 19: Status Bar Activity Indicators.
  /// </summary>
  public class StatusBarActivityService
  {
    private readonly IHealthVersionClient _healthVersionClient;
    private readonly OperationQueueService? _operationQueueService;
    private bool _isMonitoring;

    public event EventHandler<ActivityStatusChangedEventArgs>? ActivityStatusChanged;

    public StatusBarActivityService(IHealthVersionClient healthVersionClient, OperationQueueService? operationQueueService = null)
    {
      _healthVersionClient = healthVersionClient ?? throw new ArgumentNullException(nameof(healthVersionClient));
      _operationQueueService = operationQueueService;
    }

    /// <summary>
    /// Gets the current processing status.
    /// </summary>
    public ProcessingStatus ProcessingStatus { get; private set; } = ProcessingStatus.Idle;

    /// <summary>
    /// Gets the current network status.
    /// </summary>
    public NetworkStatus NetworkStatus { get; private set; } = NetworkStatus.Connected;

    /// <summary>
    /// Gets the current engine status.
    /// </summary>
    public EngineStatus EngineStatus { get; private set; } = EngineStatus.Ready;

    /// <summary>
    /// Gets the number of active processing jobs.
    /// </summary>
    public int ActiveJobCount { get; private set; }

    /// <summary>
    /// Gets the number of queued operations.
    /// </summary>
    public int QueuedOperationCount { get; private set; }

    /// <summary>
    /// Starts monitoring activity status.
    /// </summary>
    public void StartMonitoring()
    {
      if (_isMonitoring)
        return;

      _isMonitoring = true;
      _ = Task.Run(MonitorLoop);
    }

    /// <summary>
    /// Stops monitoring activity status.
    /// </summary>
    public void StopMonitoring()
    {
      _isMonitoring = false;
    }

    /// <summary>
    /// Updates processing status.
    /// </summary>
    public void UpdateProcessingStatus(ProcessingStatus status, int activeJobCount = 0)
    {
      if (ProcessingStatus != status || ActiveJobCount != activeJobCount)
      {
        ProcessingStatus = status;
        ActiveJobCount = activeJobCount;
        OnActivityStatusChanged();
      }
    }

    /// <summary>
    /// Updates network status.
    /// </summary>
    public void UpdateNetworkStatus(NetworkStatus status)
    {
      if (NetworkStatus != status)
      {
        NetworkStatus = status;
        OnActivityStatusChanged();
      }
    }

    /// <summary>
    /// Updates engine status.
    /// </summary>
    public void UpdateEngineStatus(EngineStatus status)
    {
      if (EngineStatus != status)
      {
        EngineStatus = status;
        OnActivityStatusChanged();
      }
    }

    private async Task MonitorLoop()
    {
      while (_isMonitoring)
      {
        try
        {
          // Check network status
          var isConnected = await _healthVersionClient.CheckHealthAsync(System.Threading.CancellationToken.None);
          UpdateNetworkStatus(isConnected ? NetworkStatus.Connected : NetworkStatus.Disconnected);

          // Check queued operations
          if (_operationQueueService != null)
          {
            QueuedOperationCount = _operationQueueService.QueueCount;
          }

          // Update processing status based on queue
          if (QueuedOperationCount > 0 || ActiveJobCount > 0)
          {
            UpdateProcessingStatus(ProcessingStatus.Processing, ActiveJobCount);
          }
          else
          {
            UpdateProcessingStatus(ProcessingStatus.Idle);
          }

          // Check engine status (simplified - could be enhanced with actual engine state)
          // For now, assume ready if connected
          UpdateEngineStatus(isConnected ? EngineStatus.Ready : EngineStatus.Offline);

          await Task.Delay(TimeSpan.FromSeconds(2));
        }
        catch
        {
          // Silently continue monitoring
          await Task.Delay(TimeSpan.FromSeconds(5));
        }
      }
    }

    private void OnActivityStatusChanged()
    {
      ActivityStatusChanged?.Invoke(this, new ActivityStatusChangedEventArgs
      {
        ProcessingStatus = ProcessingStatus,
        NetworkStatus = NetworkStatus,
        EngineStatus = EngineStatus,
        ActiveJobCount = ActiveJobCount,
        QueuedOperationCount = QueuedOperationCount
      });
    }
  }

  /// <summary>
  /// Processing status enum.
  /// </summary>
  public enum ProcessingStatus
  {
    Idle = 0,
    Processing = 1,
    Paused = 2,
    Error = 3
  }

  /// <summary>
  /// Network status enum.
  /// </summary>
  public enum NetworkStatus
  {
    Connected = 0,
    Disconnected = 1,
    Reconnecting = 2,
    Error = 3
  }

  /// <summary>
  /// Engine status enum.
  /// </summary>
  public enum EngineStatus
  {
    Ready = 0,
    Busy = 1,
    Starting = 2,
    Offline = 3,
    Error = 4
  }

  /// <summary>
  /// Event arguments for activity status changes.
  /// </summary>
  public class ActivityStatusChangedEventArgs : EventArgs
  {
    public ProcessingStatus ProcessingStatus { get; set; }
    public NetworkStatus NetworkStatus { get; set; }
    public EngineStatus EngineStatus { get; set; }
    public int ActiveJobCount { get; set; }
    public int QueuedOperationCount { get; set; }
  }
}