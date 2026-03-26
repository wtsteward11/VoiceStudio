using System.Threading;
using System.Threading.Tasks;
using VoiceStudio.Core.Models;

namespace VoiceStudio.Core.Services
{
  /// <summary>
  /// Client for SLO Dashboard API (/api/v1/diagnostics/slo).
  /// Use instead of IBackendClient for SLODashboard panel.
  /// </summary>
  public interface ISLODashboardClient
  {
    Task<SloDataResponse?> GetSloDataAsync(CancellationToken cancellationToken = default);
  }
}
