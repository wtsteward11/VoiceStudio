using System;
using System.Threading;
using System.Threading.Tasks;
using VoiceStudio.Core.Models;
using VoiceStudio.Core.Services;

namespace VoiceStudio.App.Services
{
  /// <summary>
  /// Client for Analytics API. Thin pass-through to IBackendClient.
  /// </summary>
  public sealed class AnalyticsDashboardClient : IAnalyticsDashboardClient
  {
    private readonly IBackendClient _backend;

    public AnalyticsDashboardClient(IBackendClient backend)
    {
      _backend = backend ?? throw new ArgumentNullException(nameof(backend));
    }

    /// <inheritdoc />
    public Task<AnalyticsDashboardSummary?> GetSummaryAsync(CancellationToken cancellationToken = default)
    {
      return _backend.SendRequestAsync<object, AnalyticsDashboardSummary>(
        "/api/analytics/summary",
        null,
        System.Net.Http.HttpMethod.Get,
        cancellationToken);
    }

    /// <inheritdoc />
    public Task<AnalyticsDashboardMetric[]?> GetMetricsAsync(
      string category,
      string interval,
      CancellationToken cancellationToken = default)
    {
      var url = $"/api/analytics/metrics/{Uri.EscapeDataString(category ?? "")}?interval={Uri.EscapeDataString(interval ?? "")}";
      return _backend.SendRequestAsync<object, AnalyticsDashboardMetric[]>(
        url,
        null,
        System.Net.Http.HttpMethod.Get,
        cancellationToken);
    }

    /// <inheritdoc />
    public Task<string[]?> GetCategoriesAsync(CancellationToken cancellationToken = default)
    {
      return _backend.SendRequestAsync<object, string[]>(
        "/api/analytics/categories",
        null,
        System.Net.Http.HttpMethod.Get,
        cancellationToken);
    }

    /// <inheritdoc />
    public Task<AnalyticsDashboardStatisticalResponse?> GetStatisticalAnalysisAsync(
      string category,
      string interval,
      CancellationToken cancellationToken = default)
    {
      var url = $"/api/analytics/statistical/{Uri.EscapeDataString(category ?? "")}?interval={Uri.EscapeDataString(interval ?? "")}";
      return _backend.SendRequestAsync<object, AnalyticsDashboardStatisticalResponse>(
        url,
        null,
        System.Net.Http.HttpMethod.Get,
        cancellationToken);
    }
  }
}
