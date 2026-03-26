using System;
using System.Threading;
using System.Threading.Tasks;
using VoiceStudio.Core.Models;
using VoiceStudio.Core.Services;

namespace VoiceStudio.App.Services
{
  /// <summary>
  /// Client for profile health and quality metrics.
  /// Thin pass-through to IBackendClient.
  /// </summary>
  public sealed class ProfileHealthClient : IProfileHealthClient
  {
    private readonly IBackendClient _backend;

    public ProfileHealthClient(IBackendClient backend)
    {
      _backend = backend ?? throw new ArgumentNullException(nameof(backend));
    }

    /// <inheritdoc />
    public Task<QualityDegradationResponse?> GetQualityDegradationAsync(
      string profileId,
      int timeWindowDays = 7,
      double degradationThresholdPercent = 10.0,
      double criticalThresholdPercent = 25.0,
      CancellationToken cancellationToken = default)
      => _backend.GetQualityDegradationAsync(
        profileId,
        timeWindowDays,
        degradationThresholdPercent,
        criticalThresholdPercent,
        cancellationToken);

    /// <inheritdoc />
    public Task<QualityBaseline?> GetQualityBaselineAsync(
      string profileId,
      int timePeriodDays = 30,
      CancellationToken cancellationToken = default)
      => _backend.GetQualityBaselineAsync(profileId, timePeriodDays, cancellationToken);

    /// <inheritdoc />
    public Task<QualityTrends> GetQualityTrendsAsync(
      string profileId,
      string timeRange = "30d",
      CancellationToken cancellationToken = default)
      => _backend.GetQualityTrendsAsync(profileId, timeRange, cancellationToken);
  }
}
