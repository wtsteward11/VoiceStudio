using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using VoiceStudio.Core.Models;
using VoiceStudio.Core.Services;

namespace VoiceStudio.App.Services
{
  /// <summary>
  /// Profile quality analytics policy lives here. Owns defaults, thresholds, and result normalization.
  /// Implements profile quality analytics by delegating to BackendClient with canonical policy.
  /// </summary>
  public sealed class ProfileQualityInsightsService : IProfileQualityInsightsService
  {
    private readonly IBackendClient _backendClient;

    /// <summary>Canonical limit for quality history queries.</summary>
    public const int DefaultQualityHistoryLimit = 50;

    /// <summary>Canonical default time range for trends (7d, 30d, 90d, 1y, all).</summary>
    public const string DefaultTimeRange = "30d";

    /// <summary>Canonical time period for baseline calculation (days).</summary>
    public const int DefaultTimePeriodDays = 30;

    /// <summary>Degradation threshold percent (warning).</summary>
    public const double DegradationThresholdPercent = 10.0;

    /// <summary>Critical threshold percent for severe degradation.</summary>
    public const double CriticalThresholdPercent = 25.0;

    public ProfileQualityInsightsService(IBackendClient backendClient)
    {
      _backendClient = backendClient ?? throw new ArgumentNullException(nameof(backendClient));
    }

    public async Task<List<QualityHistoryEntry>> LoadQualityHistoryAsync(
        string profileId,
        CancellationToken cancellationToken = default)
    {
      var raw = await _backendClient.GetQualityHistoryAsync(
          profileId,
          DefaultQualityHistoryLimit,
          startDate: null,
          endDate: null,
          cancellationToken).ConfigureAwait(false);
      return NormalizeHistory(raw);
    }

    public async Task<QualityTrends> LoadQualityTrendsAsync(
        string profileId,
        string? timeRange = null,
        CancellationToken cancellationToken = default)
    {
      var range = string.IsNullOrEmpty(timeRange) ? DefaultTimeRange : timeRange;
      return await _backendClient.GetQualityTrendsAsync(
          profileId,
          range,
          cancellationToken).ConfigureAwait(false);
    }

    public async Task<QualityBaseline?> LoadQualityBaselineAsync(
        string profileId,
        CancellationToken cancellationToken = default)
    {
      return await _backendClient.GetQualityBaselineAsync(
          profileId,
          DefaultTimePeriodDays,
          cancellationToken).ConfigureAwait(false);
    }

    public async Task<QualityDegradationResponse?> GetQualityDegradationAsync(
        string profileId,
        int timeWindowDays = 7,
        CancellationToken cancellationToken = default)
    {
      var raw = await _backendClient.GetQualityDegradationAsync(
          profileId,
          timeWindowDays,
          DegradationThresholdPercent,
          CriticalThresholdPercent,
          cancellationToken).ConfigureAwait(false);
      return NormalizeDegradation(raw);
    }

    private static List<QualityHistoryEntry> NormalizeHistory(List<QualityHistoryEntry>? raw)
    {
      return raw ?? new List<QualityHistoryEntry>();
    }

    private static QualityDegradationResponse? NormalizeDegradation(QualityDegradationResponse? raw)
    {
      if (raw == null)
        return null;
      if (raw.Alerts == null)
        raw.Alerts = new List<QualityDegradationAlert>();
      return raw;
    }
  }
}
