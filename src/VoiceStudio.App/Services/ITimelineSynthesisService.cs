using System;
using System.Threading;
using System.Threading.Tasks;

namespace VoiceStudio.App.Services
{
  /// <summary>
  /// Result of timeline synthesis and optional project save.
  /// </summary>
  public record SynthesisResult(
    string AudioId,
    string AudioUrl,
    double? QualityScore,
    double? Duration,
    string? SavedFilename);

  /// <summary>
  /// Service for synthesizing voice and optionally saving to project.
  /// Owns request construction, filename sanitization, and save orchestration.
  /// </summary>
  public interface ITimelineSynthesisService
  {
    /// <summary>
    /// Synthesize text to audio and optionally save to project.
    /// </summary>
    /// <param name="engine">Engine ID (e.g. xtts).</param>
    /// <param name="profileId">Voice profile ID.</param>
    /// <param name="text">Text to synthesize.</param>
    /// <param name="enhanceQuality">Whether to enhance quality.</param>
    /// <param name="projectId">Project ID to save to; null to skip save.</param>
    /// <param name="progress">Progress reporter (0-100).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Synthesis result; SavedFilename is null if save was skipped or failed.</returns>
    Task<SynthesisResult> SynthesizeAndSaveAsync(
      string engine,
      string profileId,
      string text,
      bool enhanceQuality,
      string? projectId,
      IProgress<int>? progress,
      CancellationToken cancellationToken = default);
  }
}
