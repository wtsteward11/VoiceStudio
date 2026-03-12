using System.Threading;
using System.Threading.Tasks;
using VoiceStudio.Core.Models;

namespace VoiceStudio.App.Services
{
  /// <summary>
  /// Service for profile preview playback. Caches preview audio and quality metrics.
  /// Used by ProfilesViewModel to play voice profile previews.
  /// </summary>
  public interface IProfilePreviewService
  {
    /// <summary>
    /// Gets or creates a preview for the profile, plays it, and returns when playback completes or fails.
    /// </summary>
    /// <param name="profileId">Profile ID</param>
    /// <param name="profile">Profile (for language, emotion)</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>Preview result with audio URL, quality metrics, and score; null on failure</returns>
    Task<PreviewResult?> GetOrCreatePreviewAsync(string profileId, VoiceProfile profile, CancellationToken ct);

    /// <summary>
    /// Stops the current preview playback.
    /// </summary>
    void StopPreview();

    /// <summary>
    /// Gets cached quality metrics and score for a profile, if previously previewed.
    /// </summary>
    /// <param name="profileId">Profile ID</param>
    /// <param name="qualityMetrics">Cached quality metrics, or null</param>
    /// <param name="qualityScore">Cached quality score, or null</param>
    /// <returns>True if cache had an entry for this profile</returns>
    bool TryGetCachedQuality(string profileId, out QualityMetrics? qualityMetrics, out double? qualityScore);
  }

  /// <summary>
  /// Result of a profile preview (playback completed or failed).
  /// </summary>
  public sealed record PreviewResult(
    string? AudioUrl,
    QualityMetrics? QualityMetrics,
    double? QualityScore);
}
