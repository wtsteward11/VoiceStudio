using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using VoiceStudio.Core.Models;

namespace VoiceStudio.App.Services
{
  /// <summary>
  /// Service for profile quality analytics: history, trends, baseline, degradation.
  /// Profile quality analytics policy (defaults, thresholds, result normalization) lives here.
  /// Used by ProfilesViewModel to load quality insights without embedding backend orchestration.
  /// </summary>
  public interface IProfileQualityInsightsService
  {
    /// <summary>
    /// Loads quality history entries for a profile. Uses service-owned limit default.
    /// </summary>
    Task<List<QualityHistoryEntry>> LoadQualityHistoryAsync(
        string profileId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Loads quality trends for a profile. Uses service-owned time range default.
    /// </summary>
    Task<QualityTrends> LoadQualityTrendsAsync(
        string profileId,
        string? timeRange = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Loads quality baseline for a profile. Uses service-owned time period default.
    /// </summary>
    Task<QualityBaseline?> LoadQualityBaselineAsync(
        string profileId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets quality degradation for a profile. Uses service-owned thresholds.
    /// </summary>
    Task<QualityDegradationResponse?> GetQualityDegradationAsync(
        string profileId,
        int timeWindowDays = 7,
        CancellationToken cancellationToken = default);
  }
}
