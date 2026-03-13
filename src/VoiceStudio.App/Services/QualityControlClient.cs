using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using VoiceStudio.Core.Models;
using VoiceStudio.Core.Services;

namespace VoiceStudio.App.Services
{
  /// <summary>
  /// Client for /api/quality. Thin pass-through to IBackendClient.
  /// </summary>
  public sealed class QualityControlClient : IQualityControlClient
  {
    private readonly IBackendClient _backend;

    public QualityControlClient(IBackendClient backend)
    {
      _backend = backend ?? throw new ArgumentNullException(nameof(backend));
    }

    /// <inheritdoc />
    public Task<Dictionary<string, QualityPresetInfo>> GetQualityPresetsAsync(CancellationToken ct = default)
      => _backend.GetQualityPresetsAsync(ct);

    /// <inheritdoc />
    public Task<QualityDashboard> GetQualityDashboardAsync(string? projectId = null, int days = 30, CancellationToken ct = default)
      => _backend.GetQualityDashboardAsync(projectId, days, ct);

    /// <inheritdoc />
    public Task<QualityAnalysisResponse> AnalyzeQualityAsync(QualityAnalysisRequest request, CancellationToken ct = default)
      => _backend.AnalyzeQualityAsync(request, ct);

    /// <inheritdoc />
    public Task<QualityOptimizationResponse> OptimizeQualityAsync(QualityOptimizationRequest request, CancellationToken ct = default)
      => _backend.OptimizeQualityAsync(request, ct);

    /// <inheritdoc />
    public Task<EngineRecommendationResponse> GetEngineRecommendationAsync(EngineRecommendationRequest request, CancellationToken ct = default)
      => _backend.GetEngineRecommendationAsync(request, ct);

    /// <inheritdoc />
    public Task<QualityConsistencyReport> CheckProjectConsistencyAsync(string projectId, int timePeriodDays = 30, CancellationToken ct = default)
      => _backend.CheckProjectConsistencyAsync(projectId, timePeriodDays, ct);

    /// <inheritdoc />
    public Task<AllProjectsConsistencyResponse> CheckAllProjectsConsistencyAsync(int timePeriodDays = 30, CancellationToken ct = default)
      => _backend.CheckAllProjectsConsistencyAsync(timePeriodDays, ct);

    /// <inheritdoc />
    public Task<QualityTrendsResponse> GetProjectQualityTrendsAsync(string projectId, int timePeriodDays = 30, CancellationToken ct = default)
      => _backend.GetProjectQualityTrendsAsync(projectId, timePeriodDays, ct);

    /// <inheritdoc />
    public Task<bool> SetQualityStandardAsync(string projectId, string standardName, CancellationToken ct = default)
      => _backend.SetQualityStandardAsync(projectId, standardName, ct);

    /// <inheritdoc />
    public Task<QualityHeatmapResponse> GetQualityHeatmapAsync(QualityHeatmapRequest request, CancellationToken ct = default)
      => _backend.GetQualityHeatmapAsync(request, ct);

    /// <inheritdoc />
    public Task<QualityCorrelationResponse> GetQualityCorrelationsAsync(List<Dictionary<string, object>> qualityData, CancellationToken ct = default)
      => _backend.GetQualityCorrelationsAsync(qualityData, ct);

    /// <inheritdoc />
    public Task<QualityAnomalyResponse> DetectQualityAnomaliesAsync(List<Dictionary<string, object>> qualityData, string metric = "mos_score", double thresholdStd = 2.0, CancellationToken ct = default)
      => _backend.DetectQualityAnomaliesAsync(qualityData, metric, thresholdStd, ct);

    /// <inheritdoc />
    public Task<QualityPredictionResponse> PredictQualityAsync(QualityPredictionRequest request, CancellationToken ct = default)
      => _backend.PredictQualityAsync(request, ct);

    /// <inheritdoc />
    public Task<QualityInsightsResponse> GetQualityInsightsAsync(List<Dictionary<string, object>> qualityData, int timePeriodDays = 30, CancellationToken ct = default)
      => _backend.GetQualityInsightsAsync(qualityData, timePeriodDays, ct);

    /// <inheritdoc />
    public Task<BenchmarkResponse> RunBenchmarkAsync(BenchmarkRequest request, CancellationToken ct = default)
      => _backend.RunBenchmarkAsync(request, ct);
  }
}
