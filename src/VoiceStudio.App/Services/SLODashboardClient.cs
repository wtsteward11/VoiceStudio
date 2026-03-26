using System;
using System.Threading;
using System.Threading.Tasks;
using VoiceStudio.Core.Models;
using VoiceStudio.Core.Services;

namespace VoiceStudio.App.Services
{
  /// <summary>
  /// Client for SLO Dashboard API. Thin pass-through to IBackendClient.
  /// </summary>
  public sealed class SLODashboardClient : ISLODashboardClient
  {
    private readonly IBackendClient _backend;

    public SLODashboardClient(IBackendClient backend)
    {
      _backend = backend ?? throw new ArgumentNullException(nameof(backend));
    }

    /// <inheritdoc />
    public Task<SloDataResponse?> GetSloDataAsync(CancellationToken cancellationToken = default)
      => _backend.GetAsync<SloDataResponse>("/api/v1/diagnostics/slo", cancellationToken);
  }
}
