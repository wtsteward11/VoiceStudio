using System;
using System.Threading;
using System.Threading.Tasks;
using VoiceStudio.Core.Models;
using VoiceStudio.Core.Services;

namespace VoiceStudio.App.Services
{
  /// <summary>
  /// Client for /api/engine/telemetry and /api/v1/diagnostics/traces. PR-6: extracted from BackendClient.
  /// </summary>
  public sealed class TelemetryClient : ITelemetryClient
  {
    private readonly BackendClientHttpPipeline _pipeline;

    /// <summary>
    /// For DI: use BackendHttpContext.Pipeline. Tests use this ctor with mock pipeline.
    /// </summary>
    internal TelemetryClient(BackendClientHttpPipeline pipeline)
    {
      _pipeline = pipeline ?? throw new ArgumentNullException(nameof(pipeline));
    }

    /// <inheritdoc />
    public Task<Telemetry?> GetTelemetryAsync(CancellationToken cancellationToken = default)
      => _pipeline.GetAsync<Telemetry>("/api/engine/telemetry", cancellationToken);

    /// <inheritdoc />
    public Task<TraceListResponse?> GetTracesAsync(CancellationToken cancellationToken = default)
      => _pipeline.GetAsync<TraceListResponse>("/api/v1/diagnostics/traces", cancellationToken);
  }
}
