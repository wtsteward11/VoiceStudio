// VoiceStudio — GAP-045: deterministic transcript segment → timeline target resolution.

namespace VoiceStudio.Core.Transcription;

/// <summary>
/// Result of resolving a transcript segment against <see cref="Models.Project"/> clip linkage (GAP-033 substrate).
/// </summary>
public enum TranscriptSegmentTargetResolutionKind
{
  Resolved,
  Unlinked,
  AmbiguousMultipleClips,
  NoTimelineProject,
  InvalidInput,
}

/// <summary>
/// Frozen time basis for <see cref="TimelineSeekSeconds"/>: source-audio seconds aligned to clip-local [0, <see cref="Models.AudioClip.Duration"/>)
/// plus <see cref="Models.AudioClip.StartTime"/> (timeline seconds, double) on the timeline (same basis as linkage overlap in GAP-033).
/// </summary>
public sealed class TranscriptSegmentTargetResolution
{
  public TranscriptSegmentTargetResolutionKind Kind { get; init; }

  /// <summary>Primary clip when <see cref="Kind"/> is <see cref="TranscriptSegmentTargetResolutionKind.Resolved"/>.</summary>
  public string? ClipId { get; init; }

  /// <summary>Track containing <see cref="ClipId"/> when resolved (GAP-046 apply path).</summary>
  public string? TrackId { get; init; }

  public string? TranscriptionId { get; init; }

  public double SourceAudioStartSeconds { get; init; }

  public double SourceAudioEndSeconds { get; init; }

  /// <summary>Absolute timeline seconds for transport seek: clip.StartTime (seconds) + segment start in source-audio space.</summary>
  public double TimelineSeekSeconds { get; init; }

  public string? Reason { get; init; }

  public static TranscriptSegmentTargetResolution Resolved(
      string trackId,
      string clipId,
      string transcriptionId,
      double sourceStart,
      double sourceEnd,
      double timelineSeekSeconds) =>
      new()
      {
        Kind = TranscriptSegmentTargetResolutionKind.Resolved,
        TrackId = trackId,
        ClipId = clipId,
        TranscriptionId = transcriptionId,
        SourceAudioStartSeconds = sourceStart,
        SourceAudioEndSeconds = sourceEnd,
        TimelineSeekSeconds = timelineSeekSeconds,
        Reason = null,
      };

  public static TranscriptSegmentTargetResolution Failure(
      TranscriptSegmentTargetResolutionKind kind,
      string? transcriptionId,
      double sourceStart,
      double sourceEnd,
      string reason) =>
      new()
      {
        Kind = kind,
        ClipId = null,
        TrackId = null,
        TranscriptionId = transcriptionId,
        SourceAudioStartSeconds = sourceStart,
        SourceAudioEndSeconds = sourceEnd,
        TimelineSeekSeconds = 0,
        Reason = reason,
      };
}
