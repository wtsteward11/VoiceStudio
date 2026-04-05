using System;
using System.Linq;
using VoiceStudio.Core.Models;
using VoiceStudio.Core.Services;
using VoiceStudio.Core.Transcription;

namespace VoiceStudio.App.Services;

/// <inheritdoc />
public sealed class TranscriptSegmentTargetResolver : ITranscriptSegmentTargetResolver
{
  private readonly ITimelineSelectedProjectGate _projectGate;
  private readonly IClipTranscriptLinkageService _linkage;

  public TranscriptSegmentTargetResolver(
      ITimelineSelectedProjectGate projectGate,
      IClipTranscriptLinkageService linkage)
  {
    _projectGate = projectGate ?? throw new ArgumentNullException(nameof(projectGate));
    _linkage = linkage ?? throw new ArgumentNullException(nameof(linkage));
  }

  /// <inheritdoc />
  public TranscriptSegmentTargetResolution Resolve(
      string? transcriptionId,
      string? segmentId,
      double segmentStartSeconds,
      double segmentEndSeconds)
  {
    if (string.IsNullOrWhiteSpace(transcriptionId) || string.IsNullOrWhiteSpace(segmentId))
    {
      return TranscriptSegmentTargetResolution.Failure(
          TranscriptSegmentTargetResolutionKind.InvalidInput,
          transcriptionId,
          segmentStartSeconds,
          segmentEndSeconds,
          "Transcription id and segment id are required.");
    }

    var project = _projectGate.SelectedProject;
    if (project == null)
    {
      return TranscriptSegmentTargetResolution.Failure(
          TranscriptSegmentTargetResolutionKind.NoTimelineProject,
          transcriptionId,
          segmentStartSeconds,
          segmentEndSeconds,
          "No project is loaded on the Timeline. Select or open a project, then try again.");
    }

    var clipIds = _linkage
        .GetLinksForTranscription(project, transcriptionId)
        .Where(l => l.SegmentIds.Any(s => string.Equals(s, segmentId, StringComparison.Ordinal)))
        .Select(l => l.ClipId)
        .Distinct(StringComparer.Ordinal)
        .ToList();

    if (clipIds.Count == 0)
    {
      return TranscriptSegmentTargetResolution.Failure(
          TranscriptSegmentTargetResolutionKind.Unlinked,
          transcriptionId,
          segmentStartSeconds,
          segmentEndSeconds,
          "This segment is not linked to a timeline clip. Send the transcription to the Timeline or transcribe audio that matches a clip.");
    }

    if (clipIds.Count > 1)
    {
      return TranscriptSegmentTargetResolution.Failure(
          TranscriptSegmentTargetResolutionKind.AmbiguousMultipleClips,
          transcriptionId,
          segmentStartSeconds,
          segmentEndSeconds,
          "Multiple clips link this segment; automatic targeting is blocked. Adjust clips or linkage so one clip owns this segment.");
    }

    var clipId = clipIds[0];
    var clip = FindClip(project, clipId, out var trackId);
    if (clip == null || string.IsNullOrWhiteSpace(trackId))
    {
      return TranscriptSegmentTargetResolution.Failure(
          TranscriptSegmentTargetResolutionKind.Unlinked,
          transcriptionId,
          segmentStartSeconds,
          segmentEndSeconds,
          "The linked clip no longer exists in the project.");
    }

    var timelineSeek = clip.StartTime + segmentStartSeconds;
    return TranscriptSegmentTargetResolution.Resolved(
        trackId,
        clipId,
        transcriptionId,
        segmentStartSeconds,
        segmentEndSeconds,
        timelineSeek);
  }

  private static AudioClip? FindClip(Project project, string clipId, out string? trackId)
  {
    trackId = null;
    foreach (var track in project.Tracks ?? Enumerable.Empty<AudioTrack>())
    {
      foreach (var c in track.Clips ?? Enumerable.Empty<AudioClip>())
      {
        if (string.Equals(c.Id, clipId, StringComparison.Ordinal))
        {
          trackId = track.Id;
          return c;
        }
      }
    }

    return null;
  }
}
