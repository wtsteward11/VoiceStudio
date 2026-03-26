using System.Threading;
using System.Threading.Tasks;
using VoiceStudio.Core.Models;

namespace VoiceStudio.Core.Services
{
  /// <summary>
  /// Client for Analytics API (/api/analytics/summary, metrics, categories, statistical).
  /// Use instead of IBackendClient for AnalyticsDashboard panel.
  /// </summary>
  public interface IAnalyticsDashboardClient
  {
    Task<AnalyticsDashboardSummary?> GetSummaryAsync(CancellationToken cancellationToken = default);

    Task<AnalyticsDashboardMetric[]?> GetMetricsAsync(
      string category,
      string interval,
      CancellationToken cancellationToken = default);

    Task<string[]?> GetCategoriesAsync(CancellationToken cancellationToken = default);

    Task<AnalyticsDashboardStatisticalResponse?> GetStatisticalAnalysisAsync(
      string category,
      string interval,
      CancellationToken cancellationToken = default);
  }
}
