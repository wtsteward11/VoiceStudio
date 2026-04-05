using VoiceStudio.Core.Transcription;

namespace VoiceStudio.Core.Services;

/// <summary>
/// Session authority for transcript-derived edit intents (GAP-045). Validates against <see cref="ITranscriptSegmentTargetResolver"/> before accepting state.
/// </summary>
public interface ITranscriptEditIntentService
{
  TranscriptEditIntent? Current { get; }

  void Clear();

  /// <summary>
  /// Attempts to record an intent. Fails closed when resolution is not <see cref="TranscriptSegmentTargetResolutionKind.Resolved"/>.
  /// For <see cref="TranscriptEditIntentKind.ReplaceRange"/>, <paramref name="replacementText"/> must be non-empty after trim.
  /// </summary>
  bool TryRecordIntent(
      TranscriptEditIntentKind kind,
      string transcriptionId,
      string segmentId,
      double segmentStartSeconds,
      double segmentEndSeconds,
      out string? errorMessage,
      string? replacementText = null);
}
