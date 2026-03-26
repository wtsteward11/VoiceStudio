using System.Threading;
using System.Threading.Tasks;
using VoiceStudio.App.Services;
using VoiceStudio.Core.Models;

namespace VoiceStudio.Core.Services
{
  /// <summary>
  /// Client for backend telemetry and diagnostics traces. PR-6: extracted from IBackendClient.
  /// </summary>
  public interface ITelemetryClient
  {
    /// <summary>
    /// Gets engine telemetry (GPU/VRAM, etc.) via GET /api/engine/telemetry.
    /// </summary>
    Task<Telemetry?> GetTelemetryAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets diagnostics traces via GET /api/v1/diagnostics/traces.
    /// </summary>
    Task<TraceListResponse?> GetTracesAsync(CancellationToken cancellationToken = default);
  }
}
