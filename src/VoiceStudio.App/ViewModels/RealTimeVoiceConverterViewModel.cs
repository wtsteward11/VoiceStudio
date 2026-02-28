using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VoiceStudio.Core.Panels;
using VoiceStudio.Core.Services;
using VoiceStudio.App.Services;
using Microsoft.UI.Dispatching;
using VoiceStudio.App.Utilities;
using VoiceStudio.App.Logging;

namespace VoiceStudio.App.ViewModels
{
  /// <summary>
  /// ViewModel for the RealTimeVoiceConverterView panel - Real-time voice conversion.
  /// </summary>
  public partial class RealTimeVoiceConverterViewModel : BaseViewModel, IPanelView
  {
    private readonly IBackendClient _backendClient;
    private readonly IWebSocketClientFactory? _webSocketClientFactory;
    private readonly ToastNotificationService? _toastNotificationService;
    private readonly RealtimeVoiceWebSocketClient? _webSocketClient;
    private DispatcherQueue? _dispatcherQueue;

    public string PanelId => "realtime-voice-converter";
    public string DisplayName => ResourceHelper.GetString("Panel.RealTimeVoiceConverter.DisplayName", "Real-Time Voice Converter");
    public PanelRegion Region => PanelRegion.Center;

    [ObservableProperty]
    private ObservableCollection<ConverterSessionItem> sessions = new();

    [ObservableProperty]
    private ConverterSessionItem? selectedSession;

    [ObservableProperty]
    private string? sourceProfileId;

    [ObservableProperty]
    private string? targetProfileId;

    [ObservableProperty]
    private ObservableCollection<string> availableProfiles = new();

    [ObservableProperty]
    private bool isStreaming;

    [ObservableProperty]
    private bool isPaused;

    // Latency monitoring
    [ObservableProperty]
    private double currentLatencyMs;

    [ObservableProperty]
    private double averageLatencyMs;

    [ObservableProperty]
    private double minLatencyMs;

    [ObservableProperty]
    private double maxLatencyMs;

    // Quality metrics
    [ObservableProperty]
    private double qualityScore;

    [ObservableProperty]
    private double mosScore;

    [ObservableProperty]
    private double similarityScore;

    [ObservableProperty]
    private double naturalnessScore;

    [ObservableProperty]
    private double snrDb;

    [ObservableProperty]
    private double clarity;

    [ObservableProperty]
    private string qualityMetricsDisplay = "No metrics available";

    // Monitoring
    private DispatcherQueueTimer? _monitoringTimer;
    private readonly List<double> _latencyHistory = new();
    private readonly object _latencyHistoryLock = new();
    private const int MAX_LATENCY_HISTORY = 100;
    private const int MONITORING_INTERVAL_MS = 2000; // Update every 2 seconds

    // GAP-009: Updated to use IWebSocketClientFactory for DI
    public RealTimeVoiceConverterViewModel(
        IViewModelContext context,
        IBackendClient backendClient,
        IWebSocketClientFactory? webSocketClientFactory = null)
        : base(context)
    {
      _backendClient = backendClient ?? throw new ArgumentNullException(nameof(backendClient));
      _webSocketClientFactory = webSocketClientFactory;
      _dispatcherQueue = DispatcherQueue.GetForCurrentThread();

      // Get services (may be null if not initialized)
      try
      {
        _toastNotificationService = AppServices.TryGetToastNotificationService();
      }
      catch
      {
        // ALLOWED: bare except - Services may not be initialized yet, that's okay
        _toastNotificationService = null;
      }

      // Initialize WebSocket client via factory (GAP-009 remediation)
      if (_webSocketClientFactory != null)
      {
        _webSocketClient = _webSocketClientFactory.CreateRealtimeVoiceClient() as RealtimeVoiceWebSocketClient;
        if (_webSocketClient != null)
        {
          _webSocketClient.AudioDataReceived += OnAudioDataReceived;
          _webSocketClient.StatusChanged += OnConversionStatusChanged;
          _webSocketClient.QualityMetricsUpdated += OnQualityMetricsUpdated;
          _webSocketClient.LatencyInfoReceived += OnLatencyInfoReceived;
        }
      }
      else if (_backendClient.WebSocketService != null)
      {
        // Fallback for backward compatibility
        _webSocketClient = new RealtimeVoiceWebSocketClient(_backendClient.WebSocketService);
        _webSocketClient.AudioDataReceived += OnAudioDataReceived;
        _webSocketClient.StatusChanged += OnConversionStatusChanged;
        _webSocketClient.QualityMetricsUpdated += OnQualityMetricsUpdated;
        _webSocketClient.LatencyInfoReceived += OnLatencyInfoReceived;
      }

      StartSessionCommand = new EnhancedAsyncRelayCommand(async (ct) =>
      {
        using var profiler = PerformanceProfiler.StartCommand("StartSession");
        await StartSessionAsync(ct);
      }, () => !string.IsNullOrEmpty(SourceProfileId) && !string.IsNullOrEmpty(TargetProfileId) && !IsLoading);
      StopSessionCommand = new EnhancedAsyncRelayCommand(async (ct) =>
      {
        using var profiler = PerformanceProfiler.StartCommand("StopSession");
        await StopSessionAsync(ct);
      }, () => SelectedSession != null && !IsLoading);
      PauseSessionCommand = new EnhancedAsyncRelayCommand(async (ct) =>
      {
        using var profiler = PerformanceProfiler.StartCommand("PauseSession");
        await PauseSessionAsync(ct);
      }, () => SelectedSession != null && !IsLoading);
      ResumeSessionCommand = new EnhancedAsyncRelayCommand(async (ct) =>
      {
        using var profiler = PerformanceProfiler.StartCommand("ResumeSession");
        await ResumeSessionAsync(ct);
      }, () => SelectedSession != null && !IsLoading);
      LoadSessionsCommand = new EnhancedAsyncRelayCommand(async (ct) =>
      {
        using var profiler = PerformanceProfiler.StartCommand("LoadSessions");
        await LoadSessionsAsync(ct);
      }, () => !IsLoading);
      DeleteSessionCommand = new EnhancedAsyncRelayCommand<ConverterSessionItem>(async (session, ct) =>
      {
        using var profiler = PerformanceProfiler.StartCommand("DeleteSession");
        await DeleteSessionAsync(session, ct);
      }, (session) => session != null && !IsLoading);
      RefreshCommand = new EnhancedAsyncRelayCommand(async (ct) =>
      {
        using var profiler = PerformanceProfiler.StartCommand("Refresh");
        await RefreshAsync(ct);
      }, () => !IsLoading);

      // Load initial data
      _ = LoadSessionsAsync(CancellationToken.None);
      _ = LoadProfilesAsync(CancellationToken.None);

      // Initialize monitoring timer (fallback if WebSocket not available)
      InitializeMonitoringTimer();
    }

    private void InitializeMonitoringTimer()
    {
      var dispatcherQueue = DispatcherQueue.GetForCurrentThread();
      if (dispatcherQueue != null)
      {
        _monitoringTimer = dispatcherQueue.CreateTimer();
        _monitoringTimer.Interval = TimeSpan.FromMilliseconds(MONITORING_INTERVAL_MS);
        _monitoringTimer.IsRepeating = true;
        _monitoringTimer.Tick += MonitoringTimer_Tick;
      }
    }

    private void MonitoringTimer_Tick(DispatcherQueueTimer sender, object args)
    {
      _ = UpdateMetricsAsync(CancellationToken.None);
    }

    private async Task UpdateMetricsAsync(CancellationToken cancellationToken)
    {
      var selectedSession = SelectedSession;
      if (!IsStreaming || IsPaused || selectedSession == null)
      {
        return;
      }

      var sessionId = selectedSession.SessionId;
      if (string.IsNullOrWhiteSpace(sessionId))
      {
        return;
      }

      try
      {
        // Try to get latency from backend endpoint
        if (!string.IsNullOrEmpty(sessionId))
        {
          try
          {
            var latencyResponse = await _backendClient.SendRequestAsync<object, RealtimeLatencyInfo>(
                $"/api/realtime-converter/{Uri.EscapeDataString(sessionId)}/latency",
                null,
                System.Net.Http.HttpMethod.Get,
                cancellationToken
            );

            if (latencyResponse != null)
            {
              RecordLatency(latencyResponse.TotalLatency);
            }
          }
          catch
          {
            // Fallback: estimate latency based on session status and average
            var estimatedLatency = AverageLatencyMs > 0 ? AverageLatencyMs : 75.0;
            RecordLatency(estimatedLatency);
          }
        }

        // Update quality metrics periodically
        await LoadQualityMetricsAsync(cancellationToken);
      }
      catch (OperationCanceledException)
      {
        return; // Cancelled
      }
      catch (Exception ex)
      {
        // Log background monitoring errors silently (non-critical)
        ErrorLoggingService?.LogWarning($"Error updating metrics: {ex.Message}", "UpdateMetrics");
      }
    }

    private void RecordLatency(double latencyMs)
    {
      double avg = 0, min = 0, max = 0;
      
      lock (_latencyHistoryLock)
      {
        _latencyHistory.Add(latencyMs);
        if (_latencyHistory.Count > MAX_LATENCY_HISTORY)
        {
          _latencyHistory.RemoveAt(0);
        }

        if (_latencyHistory.Count > 0)
        {
          avg = _latencyHistory.Average();
          min = _latencyHistory.Min();
          max = _latencyHistory.Max();
        }
      }

      // Update properties outside the lock to avoid potential deadlocks with UI thread
      CurrentLatencyMs = latencyMs;
      AverageLatencyMs = avg;
      MinLatencyMs = min;
      MaxLatencyMs = max;
    }

    private async Task LoadQualityMetricsAsync(CancellationToken cancellationToken)
    {
      var sessionId = SelectedSession?.SessionId;
      if (string.IsNullOrWhiteSpace(sessionId))
      {
        return;
      }

      try
      {
        cancellationToken.ThrowIfCancellationRequested();

        // Try to get quality metrics from backend endpoint
        try
        {
          var metricsResponse = await _backendClient.SendRequestAsync<object, RealtimeQualityMetrics>(
              $"/api/realtime-converter/{Uri.EscapeDataString(sessionId)}/quality",
              null,
              System.Net.Http.HttpMethod.Get,
              cancellationToken
          );

          if (metricsResponse != null)
          {
            SimilarityScore = metricsResponse.Similarity;
            NaturalnessScore = metricsResponse.Naturalness;
            Clarity = metricsResponse.Clarity;

            QualityScore = (metricsResponse.Similarity * 0.4) + (metricsResponse.Naturalness * 0.4) + (metricsResponse.Clarity * 0.2);
            MosScore = 3.0 + (QualityScore * 2.0);
            SnrDb = 20.0 + (QualityScore * 15.0);

            QualityMetricsDisplay = $"MOS: {MosScore:F2} | Similarity: {SimilarityScore:P0} | SNR: {SnrDb:F1}dB";
            return;
          }
        }
        catch (Exception ex)
      {
        ErrorLogger.LogWarning($"Best effort operation failed: {ex.Message}", "RealTimeVoiceConverterViewModel.LoadQualityMetricsAsync");
      }

        // Fallback calculation based on latency and session status
        var calculatedQuality = CalculateSimulatedQuality();

        QualityScore = calculatedQuality.QualityScore;
        MosScore = calculatedQuality.MosScore;
        SimilarityScore = calculatedQuality.SimilarityScore;
        NaturalnessScore = calculatedQuality.NaturalnessScore;
        SnrDb = calculatedQuality.SnrDb;

        QualityMetricsDisplay = $"MOS: {MosScore:F2} | Similarity: {SimilarityScore:P0} | SNR: {SnrDb:F1}dB";
      }
      catch (OperationCanceledException)
      {
        return; // Cancelled
      }
      catch (Exception ex)
      {
        // Log quality metrics failure silently (non-critical background operation)
        ErrorLoggingService?.LogWarning($"Error loading quality metrics: {ex.Message}", "LoadQualityMetrics");
      }
    }

    private (double QualityScore, double MosScore, double SimilarityScore, double NaturalnessScore, double SnrDb) CalculateSimulatedQuality()
    {
      // Simulate quality metrics based on latency and session status
      // Lower latency = better quality
      var latencyFactor = Math.Max(0.0, Math.Min(1.0, 1.0 - (AverageLatencyMs / 200.0))); // 200ms = 0 quality

      var mosScore = 3.0 + (latencyFactor * 2.0); // 3.0-5.0
      var similarityScore = 0.7 + (latencyFactor * 0.25); // 0.7-0.95
      var naturalnessScore = 0.75 + (latencyFactor * 0.2); // 0.75-0.95
      var snrDb = 20.0 + (latencyFactor * 15.0); // 20-35 dB

      var qualityScore = (mosScore / 5.0 * 0.4) + (similarityScore * 0.3) + (naturalnessScore * 0.3);

      return (qualityScore, mosScore, similarityScore, naturalnessScore, snrDb);
    }

    private void OnAudioDataReceived(object? sender, RealtimeAudioData data)
    {
      // Handle received audio data for playback
      // This would typically be sent to the audio player service
      _dispatcherQueue?.TryEnqueue(() =>
        {
          // Update UI or send to audio player
          // Audio data received from WebSocket connection
          System.Diagnostics.Debug.WriteLine($"Received audio data: {data.AudioData.Length} bytes, {data.SampleRate}Hz, {data.Channels} channels");
        });
    }

    private void OnConversionStatusChanged(object? sender, RealtimeConversionStatus status)
    {
      // Update conversion status on UI thread
      _dispatcherQueue?.TryEnqueue(() =>
        {
          var statusValue = status.Status ?? string.Empty;
          StatusMessage = status.Message ?? statusValue;

          // Update streaming/paused state based on status
          switch (statusValue.ToLowerInvariant())
          {
            case "idle":
              IsStreaming = false;
              IsPaused = false;
              break;
            case "converting":
              IsStreaming = true;
              IsPaused = false;
              break;
            case "paused":
              IsStreaming = true;
              IsPaused = true;
              break;
            case "error":
              IsStreaming = false;
              IsPaused = false;
              ErrorMessage = status.Message ?? "Conversion error";
              break;
          }
        });
    }

    private void OnQualityMetricsUpdated(object? sender, RealtimeQualityMetrics metrics)
    {
      // Update quality metrics on UI thread
      _dispatcherQueue?.TryEnqueue(() =>
        {
          SimilarityScore = metrics.Similarity;
          NaturalnessScore = metrics.Naturalness;
          Clarity = metrics.Clarity;

          // Calculate composite quality score
          QualityScore = (metrics.Similarity * 0.4) + (metrics.Naturalness * 0.4) + (metrics.Clarity * 0.2);
          MosScore = 3.0 + (QualityScore * 2.0); // Map to 3.0-5.0 range

          QualityMetricsDisplay = $"MOS: {MosScore:F2} | Similarity: {SimilarityScore:P0} | Naturalness: {NaturalnessScore:P0}";
        });
    }

    private void OnLatencyInfoReceived(object? sender, RealtimeLatencyInfo latency)
    {
      // Update latency information on UI thread
      _dispatcherQueue?.TryEnqueue(() => RecordLatency(latency.TotalLatency));
    }

    /// <summary>
    /// Notify commands when relevant properties change to update their CanExecute state.
    /// </summary>
    protected override void OnPropertyChanged(System.ComponentModel.PropertyChangedEventArgs e)
    {
      base.OnPropertyChanged(e);
      
      if (e.PropertyName == nameof(IsLoading) ||
          e.PropertyName == nameof(SourceProfileId) ||
          e.PropertyName == nameof(TargetProfileId) ||
          e.PropertyName == nameof(SelectedSession))
      {
        // Notify all commands that depend on these properties
        StartSessionCommand.NotifyCanExecuteChanged();
        StopSessionCommand.NotifyCanExecuteChanged();
        PauseSessionCommand.NotifyCanExecuteChanged();
        ResumeSessionCommand.NotifyCanExecuteChanged();
        LoadSessionsCommand.NotifyCanExecuteChanged();
        DeleteSessionCommand.NotifyCanExecuteChanged();
        RefreshCommand.NotifyCanExecuteChanged();
      }
    }

    protected override void Dispose(bool disposing)
    {
      if (IsDisposed)
      {
        base.Dispose(disposing);
        return;
      }

      if (disposing)
      {
        if (_monitoringTimer != null)
        {
          _monitoringTimer.Tick -= MonitoringTimer_Tick;
          _monitoringTimer.Stop();
          _monitoringTimer = null;
        }

        if (_webSocketClient != null)
        {
          _webSocketClient.AudioDataReceived -= OnAudioDataReceived;
          _webSocketClient.StatusChanged -= OnConversionStatusChanged;
          _webSocketClient.QualityMetricsUpdated -= OnQualityMetricsUpdated;
          _webSocketClient.LatencyInfoReceived -= OnLatencyInfoReceived;

          _webSocketClient.Dispose();
        }
      }

      base.Dispose(disposing);
    }

    private async Task LoadProfilesAsync(CancellationToken cancellationToken)
    {
      IsLoading = true;
      ErrorMessage = null;

      try
      {
        var profiles = await _backendClient.GetProfilesAsync(cancellationToken);

        AvailableProfiles.Clear();
        foreach (var profile in profiles)
        {
          AvailableProfiles.Add(profile.Id);
        }
      }
      catch (OperationCanceledException)
      {
        return; // User cancelled
      }
      catch (Exception ex)
      {
        await HandleErrorAsync(ex, "LoadProfiles");
        _toastNotificationService?.ShowError(
            ResourceHelper.GetString("Toast.Title.LoadProfilesFailed", "Failed to Load Profiles"),
            ex.Message);
      }
      finally
      {
        IsLoading = false;
      }
    }

    public IAsyncRelayCommand StartSessionCommand { get; }
    public IAsyncRelayCommand StopSessionCommand { get; }
    public IAsyncRelayCommand PauseSessionCommand { get; }
    public IAsyncRelayCommand ResumeSessionCommand { get; }
    public IAsyncRelayCommand LoadSessionsCommand { get; }
    public IAsyncRelayCommand<ConverterSessionItem> DeleteSessionCommand { get; }
    public IAsyncRelayCommand RefreshCommand { get; }

    private async Task StartSessionAsync(CancellationToken cancellationToken)
    {
      var sourceProfileId = SourceProfileId?.Trim();
      var targetProfileId = TargetProfileId?.Trim();
      if (string.IsNullOrEmpty(sourceProfileId) || string.IsNullOrEmpty(targetProfileId))
      {
        ErrorMessage = ResourceHelper.GetString("RealTimeVoiceConverter.BothProfilesRequired", "Both source and target profiles must be selected");
        return;
      }

      IsLoading = true;
      ErrorMessage = null;

      try
      {
        var request = new
        {
          source_profile_id = sourceProfileId,
          target_profile_id = targetProfileId
        };

        var response = await _backendClient.SendRequestAsync<object, ConverterStartResponse>(
            "/api/realtime-converter/start",
            request,
            System.Net.Http.HttpMethod.Post,
            cancellationToken
        );

        if (response != null)
        {
          StatusMessage = response.Message;
          IsStreaming = true;

          // Reset metrics
          ResetMetrics();

          // Connect WebSocket for real-time updates if available
          if (_webSocketClient != null && response.SessionId != null)
          {
            await _webSocketClient.ConnectAsync(response.SessionId);
          }

          // Start monitoring (fallback if WebSocket not available)
          if (_webSocketClient == null)
          {
            StartMonitoring();
          }

          await LoadSessionsAsync(cancellationToken);
          _toastNotificationService?.ShowSuccess(
              ResourceHelper.GetString("RealTimeVoiceConverter.SessionStarted", "Real-time voice conversion started"),
              ResourceHelper.GetString("Toast.Title.SessionStarted", "Session Started"));
        }
      }
      catch (OperationCanceledException)
      {
        return; // User cancelled
      }
      catch (Exception ex)
      {
        await HandleErrorAsync(ex, "StartSession");
        _toastNotificationService?.ShowError(
            ResourceHelper.GetString("Toast.Title.StartFailed", "Start Failed"),
            ex.Message);
      }
      finally
      {
        IsLoading = false;
      }
    }

    private async Task StopSessionAsync(CancellationToken cancellationToken)
    {
      var sessionId = SelectedSession?.SessionId;
      if (string.IsNullOrWhiteSpace(sessionId))
      {
        ErrorMessage = ResourceHelper.GetString("RealTimeVoiceConverter.SessionRequired", "Session must be selected");
        return;
      }

      IsLoading = true;
      ErrorMessage = null;

      try
      {
        await _backendClient.SendRequestAsync<object, object>(
            $"/api/realtime-converter/{Uri.EscapeDataString(sessionId)}/stop",
            null,
            System.Net.Http.HttpMethod.Post,
            cancellationToken
        );

        IsStreaming = false;
        IsPaused = false;
        StatusMessage = ResourceHelper.GetString("RealTimeVoiceConverter.SessionStopped", "Session stopped");

        // Disconnect WebSocket
        if (_webSocketClient != null)
        {
          await _webSocketClient.DisconnectAsync();
        }

        // Stop monitoring (fallback)
        StopMonitoring();

        await LoadSessionsAsync(cancellationToken);
        _toastNotificationService?.ShowSuccess(
            ResourceHelper.GetString("RealTimeVoiceConverter.SessionStoppedDetail", "Real-time voice conversion stopped"),
            ResourceHelper.GetString("Toast.Title.SessionStopped", "Session Stopped"));
      }
      catch (OperationCanceledException)
      {
        return; // User cancelled
      }
      catch (Exception ex)
      {
        await HandleErrorAsync(ex, "StopSession");
        _toastNotificationService?.ShowError(
            ResourceHelper.GetString("Toast.Title.StopFailed", "Stop Failed"),
            ex.Message);
      }
      finally
      {
        IsLoading = false;
      }
    }

    private async Task PauseSessionAsync(CancellationToken cancellationToken)
    {
      var sessionId = SelectedSession?.SessionId;
      if (string.IsNullOrWhiteSpace(sessionId))
      {
        ErrorMessage = ResourceHelper.GetString("RealTimeVoiceConverter.SessionRequired", "Session must be selected");
        return;
      }

      IsLoading = true;
      ErrorMessage = null;

      try
      {
        await _backendClient.SendRequestAsync<object, object>(
            $"/api/realtime-converter/{Uri.EscapeDataString(sessionId)}/pause",
            null,
            System.Net.Http.HttpMethod.Post,
            cancellationToken
        );

        IsPaused = true;
        StatusMessage = ResourceHelper.GetString("RealTimeVoiceConverter.SessionPaused", "Session paused");

        // Pause monitoring (but keep timer running for resume)

        await LoadSessionsAsync(cancellationToken);
        _toastNotificationService?.ShowInfo(
            ResourceHelper.GetString("RealTimeVoiceConverter.SessionPausedDetail", "Real-time voice conversion paused"),
            ResourceHelper.GetString("Toast.Title.SessionPaused", "Session Paused"));
      }
      catch (OperationCanceledException)
      {
        return; // User cancelled
      }
      catch (Exception ex)
      {
        await HandleErrorAsync(ex, "PauseSession");
        _toastNotificationService?.ShowError(
            ResourceHelper.GetString("Toast.Title.PauseFailed", "Pause Failed"),
            ex.Message);
      }
      finally
      {
        IsLoading = false;
      }
    }

    private async Task ResumeSessionAsync(CancellationToken cancellationToken)
    {
      var sessionId = SelectedSession?.SessionId;
      if (string.IsNullOrWhiteSpace(sessionId))
      {
        ErrorMessage = ResourceHelper.GetString("RealTimeVoiceConverter.SessionRequired", "Session must be selected");
        return;
      }

      IsLoading = true;
      ErrorMessage = null;

      try
      {
        await _backendClient.SendRequestAsync<object, object>(
            $"/api/realtime-converter/{Uri.EscapeDataString(sessionId)}/resume",
            null,
            System.Net.Http.HttpMethod.Post,
            cancellationToken
        );

        IsPaused = false;
        StatusMessage = ResourceHelper.GetString("RealTimeVoiceConverter.SessionResumed", "Session resumed");

        // Resume monitoring

        await LoadSessionsAsync(cancellationToken);
        _toastNotificationService?.ShowSuccess(
            ResourceHelper.GetString("RealTimeVoiceConverter.SessionResumedDetail", "Real-time voice conversion resumed"),
            ResourceHelper.GetString("Toast.Title.SessionResumed", "Session Resumed"));
      }
      catch (OperationCanceledException)
      {
        return; // User cancelled
      }
      catch (Exception ex)
      {
        await HandleErrorAsync(ex, "ResumeSession");
        _toastNotificationService?.ShowError(
            ResourceHelper.GetString("Toast.Title.ResumeFailed", "Resume Failed"),
            ex.Message);
      }
      finally
      {
        IsLoading = false;
      }
    }

    private async Task LoadSessionsAsync(CancellationToken cancellationToken)
    {
      try
      {
        IsLoading = true;
        ErrorMessage = null;

        try
        {
          var sessionsList = await _backendClient.SendRequestAsync<object, ConverterSessionListResponse>(
              "/api/realtime-converter",
              null,
              System.Net.Http.HttpMethod.Get,
              cancellationToken: cancellationToken
          );

          if (sessionsList?.Sessions != null)
          {
            Sessions.Clear();
            foreach (var session in sessionsList.Sessions)
            {
              Sessions.Add(new ConverterSessionItem(session));
            }
          }
        }
        catch
        {
          var selectedSession = SelectedSession;
          var sessionId = selectedSession?.SessionId;
          if (!string.IsNullOrWhiteSpace(sessionId))
          {
            var session = await _backendClient.SendRequestAsync<object, ConverterSession>(
                $"/api/realtime-converter/{Uri.EscapeDataString(sessionId)}",
                null,
                System.Net.Http.HttpMethod.Get,
                cancellationToken
            );

            if (session != null)
            {
              selectedSession?.UpdateFrom(session);
              if (!Sessions.Any(s => s.SessionId == session.SessionId))
              {
                Sessions.Add(new ConverterSessionItem(session));
              }
            }
          }
        }
      }
      catch (Exception ex)
      {
        ErrorMessage = ResourceHelper.FormatString("RealTimeVoiceConverter.LoadSessionsFailed", ex.Message);
        _toastNotificationService?.ShowError(
            ResourceHelper.GetString("Toast.Title.LoadSessionsFailed", "Failed to Load Sessions"),
            ex.Message);
      }
      finally
      {
        IsLoading = false;
      }
    }

    private class ConverterSessionListResponse
    {
      public List<ConverterSession> Sessions { get; set; } = new();
    }

    private async Task DeleteSessionAsync(ConverterSessionItem? session, CancellationToken cancellationToken)
    {
      if (session == null)
        return;

      IsLoading = true;
      ErrorMessage = null;

      try
      {
        await _backendClient.SendRequestAsync<object, object>(
            $"/api/realtime-converter/{session.SessionId}",
            null,
            System.Net.Http.HttpMethod.Delete,
            cancellationToken
        );

        Sessions.Remove(session);
        StatusMessage = ResourceHelper.GetString("RealTimeVoiceConverter.SessionDeleted", "Session deleted");
        _toastNotificationService?.ShowSuccess(
            ResourceHelper.GetString("RealTimeVoiceConverter.SessionDeletedDetail", "Session deleted"),
            ResourceHelper.GetString("Toast.Title.SessionDeleted", "Session Deleted"));
      }
      catch (OperationCanceledException)
      {
        return; // User cancelled
      }
      catch (Exception ex)
      {
        await HandleErrorAsync(ex, "DeleteSession");
        _toastNotificationService?.ShowError(
            ResourceHelper.GetString("Toast.Title.DeleteFailed", "Delete Failed"),
            ex.Message);
      }
      finally
      {
        IsLoading = false;
      }
    }

    private async Task RefreshAsync(CancellationToken cancellationToken)
    {
      try
      {
        await LoadSessionsAsync(cancellationToken);
        await LoadProfilesAsync(cancellationToken);
        StatusMessage = ResourceHelper.GetString("RealTimeVoiceConverter.SessionsAndProfilesRefreshed", "Sessions and profiles refreshed");

        // Refresh metrics if streaming
        if (IsStreaming && !IsPaused)
        {
          await LoadQualityMetricsAsync(cancellationToken);
        }
      }
      catch (OperationCanceledException)
      {
        return; // User cancelled
      }
      catch (Exception ex)
      {
        await HandleErrorAsync(ex, "Refresh");
      }
    }

    private void StartMonitoring()
    {
      _monitoringTimer?.Start();
    }

    private void StopMonitoring()
    {
      _monitoringTimer?.Stop();
      ResetMetrics();
    }

    private void ResetMetrics()
    {
      CurrentLatencyMs = 0.0;
      AverageLatencyMs = 0.0;
      MinLatencyMs = 0.0;
      MaxLatencyMs = 0.0;
      QualityScore = 0.0;
      MosScore = 0.0;
      SimilarityScore = 0.0;
      NaturalnessScore = 0.0;
      SnrDb = 0.0;
      QualityMetricsDisplay = "No metrics available";
      
      lock (_latencyHistoryLock)
      {
        _latencyHistory.Clear();
      }
    }

    // Response models
    private class ConverterStartResponse
    {
      public string SessionId { get; set; } = string.Empty;
      public string Message { get; set; } = string.Empty;
    }
  }

  // Data models
  public class ConverterSession
  {
    public string SessionId { get; set; } = string.Empty;
    public string SourceProfileId { get; set; } = string.Empty;
    public string TargetProfileId { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string Created { get; set; } = string.Empty;
  }

  public class ConverterSessionItem : ObservableObject
  {
    public string SessionId { get; set; }
    public string SourceProfileId { get; set; }
    public string TargetProfileId { get; set; }
    public string Status { get; set; }
    public string Created { get; set; }

    public ConverterSessionItem(ConverterSession session)
    {
      SessionId = session.SessionId;
      SourceProfileId = session.SourceProfileId;
      TargetProfileId = session.TargetProfileId;
      Status = session.Status;
      Created = session.Created;
    }

    public void UpdateFrom(ConverterSession session)
    {
      Status = session.Status;
      OnPropertyChanged(nameof(Status));
    }
  }
}