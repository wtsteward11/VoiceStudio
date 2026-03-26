using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using VoiceStudio.Core.Services;
using VoiceStudio.App.Services;

namespace VoiceStudio.App.Services
{
  /// <summary>
  /// Client for /api/realtime-converter. Thin pass-through to IBackendClient.
  /// </summary>
  public sealed class RealTimeVoiceConverterClient : IRealTimeVoiceConverterClient
  {
    private readonly IBackendClient _backend;

    public RealTimeVoiceConverterClient(IBackendClient backend)
    {
      _backend = backend ?? throw new ArgumentNullException(nameof(backend));
    }

    /// <inheritdoc />
    public IWebSocketService? WebSocketService => _backend.WebSocketService;

    /// <inheritdoc />
    public Task<RealtimeLatencyInfo?> GetLatencyAsync(string sessionId, CancellationToken cancellationToken = default)
      => _backend.SendRequestAsync<object, RealtimeLatencyInfo>(
          $"/api/realtime-converter/{Uri.EscapeDataString(sessionId)}/latency",
          null,
          HttpMethod.Get,
          cancellationToken);

    /// <inheritdoc />
    public Task<RealtimeQualityMetrics?> GetQualityMetricsAsync(string sessionId, CancellationToken cancellationToken = default)
      => _backend.SendRequestAsync<object, RealtimeQualityMetrics>(
          $"/api/realtime-converter/{Uri.EscapeDataString(sessionId)}/quality",
          null,
          HttpMethod.Get,
          cancellationToken);

    /// <inheritdoc />
    public Task<ConverterStartResponse?> StartSessionAsync(string sourceProfileId, string targetProfileId, CancellationToken cancellationToken = default)
      => _backend.SendRequestAsync<object, ConverterStartResponse>(
          "/api/realtime-converter/start",
          new { source_profile_id = sourceProfileId, target_profile_id = targetProfileId },
          HttpMethod.Post,
          cancellationToken);

    /// <inheritdoc />
    public Task StopSessionAsync(string sessionId, CancellationToken cancellationToken = default)
      => _backend.SendRequestAsync<object, object>(
          $"/api/realtime-converter/{Uri.EscapeDataString(sessionId)}/stop",
          null,
          HttpMethod.Post,
          cancellationToken);

    /// <inheritdoc />
    public Task PauseSessionAsync(string sessionId, CancellationToken cancellationToken = default)
      => _backend.SendRequestAsync<object, object>(
          $"/api/realtime-converter/{Uri.EscapeDataString(sessionId)}/pause",
          null,
          HttpMethod.Post,
          cancellationToken);

    /// <inheritdoc />
    public Task ResumeSessionAsync(string sessionId, CancellationToken cancellationToken = default)
      => _backend.SendRequestAsync<object, object>(
          $"/api/realtime-converter/{Uri.EscapeDataString(sessionId)}/resume",
          null,
          HttpMethod.Post,
          cancellationToken);

    /// <inheritdoc />
    public Task<ConverterSessionListResponse?> GetSessionsAsync(CancellationToken cancellationToken = default)
      => _backend.SendRequestAsync<object, ConverterSessionListResponse>(
          "/api/realtime-converter",
          null,
          HttpMethod.Get,
          cancellationToken);

    /// <inheritdoc />
    public Task<ConverterSession?> GetSessionAsync(string sessionId, CancellationToken cancellationToken = default)
      => _backend.SendRequestAsync<object, ConverterSession>(
          $"/api/realtime-converter/{Uri.EscapeDataString(sessionId)}",
          null,
          HttpMethod.Get,
          cancellationToken);

    /// <inheritdoc />
    public Task DeleteSessionAsync(string sessionId, CancellationToken cancellationToken = default)
      => _backend.SendRequestAsync<object, object>(
          $"/api/realtime-converter/{Uri.EscapeDataString(sessionId)}",
          null,
          HttpMethod.Delete,
          cancellationToken);
  }
}
