using System.Threading;
using System.Threading.Tasks;
using VoiceStudio.Core.Models;
using VoiceStudio.Core.Services;
using VoiceStudio.App.Utilities;

namespace VoiceStudio.App.Services
{
  /// <summary>
  /// Client for diagnostics API. Composes IConnectionStatusClient, IHealthVersionClient, ITelemetryClient. PR-8: no IBackendClient.
  /// </summary>
  public sealed class DiagnosticsClient : IDiagnosticsClient
  {
    private readonly IConnectionStatusClient _connectionStatus;
    private readonly IHealthVersionClient _healthVersion;
    private readonly ITelemetryClient _telemetry;

    public DiagnosticsClient(IConnectionStatusClient connectionStatus, IHealthVersionClient healthVersion, ITelemetryClient telemetry)
    {
      _connectionStatus = connectionStatus ?? throw new System.ArgumentNullException(nameof(connectionStatus));
      _healthVersion = healthVersion ?? throw new System.ArgumentNullException(nameof(healthVersion));
      _telemetry = telemetry ?? throw new System.ArgumentNullException(nameof(telemetry));
    }

    /// <inheritdoc />
    public Task<bool> CheckHealthAsync(CancellationToken cancellationToken = default)
      => _healthVersion.CheckHealthAsync(cancellationToken);

    /// <inheritdoc />
    public bool IsConnected => _connectionStatus.IsConnected;

    /// <inheritdoc />
    public Task<Telemetry?> GetTelemetryAsync(CancellationToken cancellationToken = default)
      => _telemetry.GetTelemetryAsync(cancellationToken);

    /// <inheritdoc />
    public Task<TraceListResponse?> GetTracesAsync(CancellationToken cancellationToken = default)
      => _telemetry.GetTracesAsync(cancellationToken);

    /// <inheritdoc />
    public string GetConnectionStatus()
    {
      if (!IsConnected)
        return "Offline";

      var circuitState = _connectionStatus.CircuitState;
      return circuitState switch
      {
        CircuitState.Open => "Circuit Open (Temporarily Unavailable)",
        CircuitState.HalfOpen => "Testing Connection...",
        CircuitState.Closed => "Connected",
        _ => "Connected"
      };
    }
  }
}
