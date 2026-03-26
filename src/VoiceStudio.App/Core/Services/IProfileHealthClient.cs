using System.Threading;
using System.Threading.Tasks;
using VoiceStudio.Core.Models;

namespace VoiceStudio.Core.Services
{
  /// <summary>
  /// Client for profile health and quality metrics.
  /// Use instead of IBackendClient for profile health dashboard.
  /// </summary>
  public interface IProfileHealthClient
  {
    /// <summary>
    /// Gets quality degradation for a profile.
    /// </summary>
    Task<QualityDegradationResponse?> GetQualityDegradationAsync(
      string profileId,
      int timeWindowDays = 7,
      double degradationThresholdPercent = 10.0,
      double criticalThresholdPercent = 25.0,
      CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets quality baseline for a profile.
    /// </summary>
    Task<QualityBaseline?> GetQualityBaselineAsync(
      string profileId,
      int timePeriodDays = 30,
      CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets quality trends for a profile.
    /// </summary>
    Task<QualityTrends> GetQualityTrendsAsync(
      string profileId,
      string timeRange = "30d",
      CancellationToken cancellationToken = default);
  }
}
