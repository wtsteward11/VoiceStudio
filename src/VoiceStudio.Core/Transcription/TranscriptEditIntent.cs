namespace VoiceStudio.Core.Transcription;

/// <summary>
/// Edit intents for transcript-driven tooling. GAP-045 lane: model + routing only; downstream mutation/regeneration may be blocked explicitly.
/// </summary>
public enum TranscriptEditIntentKind
{
  RemoveRange,
  ReplaceRange,
  RegenerateRange,
}

/// <summary>
/// Session-scoped edit intent. Not persisted in this lane unless a future row extends project authority.
/// </summary>
public sealed class TranscriptEditIntent
{
  public TranscriptEditIntentKind Kind { get; init; }
  public string? ProjectId { get; init; }
  public string TranscriptionId { get; init; } = string.Empty;
  public string SegmentId { get; init; } = string.Empty;
  public string? TargetTrackId { get; init; }
  public string? TargetClipId { get; init; }
  public double SourceAudioStartSeconds { get; init; }
  public double SourceAudioEndSeconds { get; init; }
  public double TimelineSeekSeconds { get; init; }

  /// <summary>
  /// For <see cref="TranscriptEditIntentKind.ReplaceRange"/>, operator draft text passed to regen <c>replacement_text</c>.
  /// </summary>
  public string? ReplacementText { get; init; }

  /// <summary>True when a future pipeline may execute this intent without additional user action.</summary>
  public bool DownstreamExecutable { get; init; }

  /// <summary>Set when <see cref="DownstreamExecutable"/> is false.</summary>
  public string? ExecutionBlockedReason { get; init; }
}
