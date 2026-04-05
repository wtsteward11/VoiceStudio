using VoiceStudio.Core.Transcription;

namespace VoiceStudio.Core.Services;

/// <summary>
/// Canonical resolver: transcript segment id + transcription → at most one clip + timeline seek position (GAP-045).
/// </summary>
public interface ITranscriptSegmentTargetResolver
{
  TranscriptSegmentTargetResolution Resolve(
      string? transcriptionId,
      string? segmentId,
      double segmentStartSeconds,
      double segmentEndSeconds);
}
