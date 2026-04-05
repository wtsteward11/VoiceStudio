using System;
using VoiceStudio.Core.Services;
using VoiceStudio.Core.Transcription;

namespace VoiceStudio.App.Services;

/// <inheritdoc />
public sealed class TranscriptEditIntentService : ITranscriptEditIntentService
{
  private readonly ITranscriptSegmentTargetResolver _resolver;
  private readonly ITimelineSelectedProjectGate _gate;

  public TranscriptEditIntentService(
      ITranscriptSegmentTargetResolver resolver,
      ITimelineSelectedProjectGate gate)
  {
    _resolver = resolver ?? throw new ArgumentNullException(nameof(resolver));
    _gate = gate ?? throw new ArgumentNullException(nameof(gate));
  }

  public TranscriptEditIntent? Current { get; private set; }

  public void Clear() => Current = null;

  public bool TryRecordIntent(
      TranscriptEditIntentKind kind,
      string transcriptionId,
      string segmentId,
      double segmentStartSeconds,
      double segmentEndSeconds,
      out string? errorMessage,
      string? replacementText = null)
  {
    errorMessage = null;
    if (kind == TranscriptEditIntentKind.ReplaceRange)
    {
      var trimmedReplace = (replacementText ?? string.Empty).Trim();
      if (string.IsNullOrEmpty(trimmedReplace))
      {
        errorMessage = "Replacement text is required for replace-range apply.";
        Current = null;
        return false;
      }
    }

    var resolution = _resolver.Resolve(transcriptionId, segmentId, segmentStartSeconds, segmentEndSeconds);
    if (resolution.Kind != TranscriptSegmentTargetResolutionKind.Resolved)
    {
      Current = null;
      errorMessage = resolution.Reason ?? "Target resolution failed.";
      return false;
    }

    var project = _gate.SelectedProject;
    var projectId = string.IsNullOrWhiteSpace(project?.Id) ? null : project!.Id;

    string? blockedReason;
    bool downstreamExecutable;
    switch (kind)
    {
      case TranscriptEditIntentKind.RegenerateRange:
        downstreamExecutable = true;
        blockedReason = null;
        break;
      case TranscriptEditIntentKind.RemoveRange:
        downstreamExecutable = false;
        blockedReason =
            "Remove-range execution is not implemented in this lane; intent is recorded for future pipelines.";
        break;
      case TranscriptEditIntentKind.ReplaceRange:
        downstreamExecutable = true;
        blockedReason = null;
        break;
      default:
        downstreamExecutable = false;
        blockedReason = "Unsupported intent kind.";
        break;
    }

    var trimmedForIntent = kind == TranscriptEditIntentKind.ReplaceRange
        ? (replacementText ?? string.Empty).Trim()
        : null;

    Current = new TranscriptEditIntent
    {
      Kind = kind,
      ProjectId = projectId,
      TranscriptionId = transcriptionId,
      SegmentId = segmentId,
      TargetTrackId = resolution.TrackId,
      TargetClipId = resolution.ClipId,
      SourceAudioStartSeconds = segmentStartSeconds,
      SourceAudioEndSeconds = segmentEndSeconds,
      TimelineSeekSeconds = resolution.TimelineSeekSeconds,
      ReplacementText = trimmedForIntent,
      DownstreamExecutable = downstreamExecutable,
      ExecutionBlockedReason = blockedReason,
    };

    return true;
  }
}
