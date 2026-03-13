using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using VoiceStudio.Core.Models;

namespace VoiceStudio.Core.Services
{
  /// <summary>
  /// Client for quality control API (/api/quality).
  /// Use instead of IBackendClient for presets, analysis, optimization, consistency, heatmap, correlations, anomalies, prediction, and insights.
  /// </summary>
  public interface IQualityControlClient
  {
    Task<Dictionary<string, QualityPresetInfo>> GetQualityPresetsAsync(CancellationToken ct = default);
    Task<QualityDashboard> GetQualityDashboardAsync(string? projectId = null, int days = 30, CancellationToken ct = default);
    Task<QualityAnalysisResponse> AnalyzeQualityAsync(QualityAnalysisRequest request, CancellationToken ct = default);
    Task<QualityOptimizationResponse> OptimizeQualityAsync(QualityOptimizationRequest request, CancellationToken ct = default);
    Task<EngineRecommendationResponse> GetEngineRecommendationAsync(EngineRecommendationRequest request, CancellationToken ct = default);
    Task<QualityConsistencyReport> CheckProjectConsistencyAsync(string projectId, int timePeriodDays = 30, CancellationToken ct = default);
    Task<AllProjectsConsistencyResponse> CheckAllProjectsConsistencyAsync(int timePeriodDays = 30, CancellationToken ct = default);
    Task<QualityTrendsResponse> GetProjectQualityTrendsAsync(string projectId, int timePeriodDays = 30, CancellationToken ct = default);
    Task<bool> SetQualityStandardAsync(string projectId, string standardName, CancellationToken ct = default);
    Task<QualityHeatmapResponse> GetQualityHeatmapAsync(QualityHeatmapRequest request, CancellationToken ct = default);
    Task<QualityCorrelationResponse> GetQualityCorrelationsAsync(List<Dictionary<string, object>> qualityData, CancellationToken ct = default);
    Task<QualityAnomalyResponse> DetectQualityAnomaliesAsync(List<Dictionary<string, object>> qualityData, string metric = "mos_score", double thresholdStd = 2.0, CancellationToken ct = default);
    Task<QualityPredictionResponse> PredictQualityAsync(QualityPredictionRequest request, CancellationToken ct = default);
    Task<QualityInsightsResponse> GetQualityInsightsAsync(List<Dictionary<string, object>> qualityData, int timePeriodDays = 30, CancellationToken ct = default);
    Task<BenchmarkResponse> RunBenchmarkAsync(BenchmarkRequest request, CancellationToken ct = default);
  }
}
