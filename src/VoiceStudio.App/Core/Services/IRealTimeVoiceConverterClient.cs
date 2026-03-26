using System.Threading;
using System.Threading.Tasks;
using VoiceStudio.App.Services;

namespace VoiceStudio.Core.Services
{
  /// <summary>
  /// Client for real-time voice converter API (/api/realtime-converter).
  /// Use instead of IBackendClient for session start/stop/pause/resume, latency, quality metrics.
  /// Exposes WebSocketService for fallback when IWebSocketClientFactory is null.
  /// </summary>
  public interface IRealTimeVoiceConverterClient
  {
    /// <summary>
    /// WebSocket service for real-time updates. Used when IWebSocketClientFactory is null.
    /// </summary>
    IWebSocketService? WebSocketService { get; }

    Task<RealtimeLatencyInfo?> GetLatencyAsync(string sessionId, CancellationToken cancellationToken = default);
    Task<RealtimeQualityMetrics?> GetQualityMetricsAsync(string sessionId, CancellationToken cancellationToken = default);
    Task<ConverterStartResponse?> StartSessionAsync(string sourceProfileId, string targetProfileId, CancellationToken cancellationToken = default);
    Task StopSessionAsync(string sessionId, CancellationToken cancellationToken = default);
    Task PauseSessionAsync(string sessionId, CancellationToken cancellationToken = default);
    Task ResumeSessionAsync(string sessionId, CancellationToken cancellationToken = default);
    Task<ConverterSessionListResponse?> GetSessionsAsync(CancellationToken cancellationToken = default);
    Task<ConverterSession?> GetSessionAsync(string sessionId, CancellationToken cancellationToken = default);
    Task DeleteSessionAsync(string sessionId, CancellationToken cancellationToken = default);
  }
}
