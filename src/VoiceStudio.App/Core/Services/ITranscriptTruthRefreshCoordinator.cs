using System.Threading;
using System.Threading.Tasks;
using VoiceStudio.Core.Models;

namespace VoiceStudio.App.Core.Services;

/// <summary>
/// GAP-045 Option B: canonical operator-triggered transcript refresh after clip regeneration invalidated linkage.
/// </summary>
public interface ITranscriptTruthRefreshCoordinator
{
  /// <summary>
  /// Re-transcribes the clip's current <see cref="AudioClip.AudioId"/> and rebuilds <see cref="Project.ClipTranscriptLinks"/> for that clip.
  /// Fail-closed unless the clip is <see cref="TranscriptTruthState.StaleAfterClipRegeneration"/>.
  /// Returns null on success; otherwise an operator-safe message (no raw exceptions).
  /// </summary>
  Task<string?> TryRefreshStaleTranscriptForClipAsync(
      Project project,
      string trackId,
      string clipId,
      string engine,
      string? language,
      bool wordTimestamps,
      bool diarization,
      bool useVad,
      string sourcePanelId,
      string? projectId,
      CancellationToken cancellationToken = default);
}
