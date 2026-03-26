using System.Threading;
using System.Threading.Tasks;
using VoiceStudio.App.Services;
using VoiceStudio.Core.Models;

namespace VoiceStudio.Core.Services
{
  /// <summary>
  /// Client for diagnostics API. Use instead of IBackendClient for health, telemetry, traces.
  /// </summary>
  public interface IDiagnosticsClient
  {
    Task<bool> CheckHealthAsync(CancellationToken cancellationToken = default);
    bool IsConnected { get; }
    Task<Telemetry?> GetTelemetryAsync(CancellationToken cancellationToken = default);
    Task<TraceListResponse?> GetTracesAsync(CancellationToken cancellationToken = default);
    string GetConnectionStatus();
  }
}
