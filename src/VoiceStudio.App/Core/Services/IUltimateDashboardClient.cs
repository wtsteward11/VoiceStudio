using System.Threading;
using System.Threading.Tasks;
using VoiceStudio.Core.Models;

namespace VoiceStudio.Core.Services
{
  /// <summary>
  /// Client for /api/ultimate-dashboard.
  /// Thin pass-through to IBackendClient.
  /// </summary>
  public interface IUltimateDashboardClient
  {
    /// <summary>
    /// Gets the dashboard data (summary, quick stats, recent activities, alerts).
    /// </summary>
    Task<UltimateDashboardData?> GetDashboardAsync(CancellationToken cancellationToken = default);
  }
}
