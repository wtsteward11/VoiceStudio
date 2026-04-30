using System.Threading;
using System.Threading.Tasks;
using VoiceStudio.App.Core.Models;

namespace VoiceStudio.App.Core.Services;

/// <summary>Narrow HTTP client for dialogue timeline segment APIs (v1.2 sync regenerate).</summary>
public interface IDialogueServiceClient
{
  /// <summary>POST /api/dialogue/segments/{segmentId}/regenerate</summary>
  Task<RegenerateDialogueSegmentResponse> RegenerateSegmentAsync(
      string segmentId,
      RegenerateDialogueSegmentRequest request,
      CancellationToken cancellationToken = default);

  /// <summary>POST /api/dialogue/transcripts/{transcriptId}/create-timeline-clips</summary>
  Task<CreateTimelineClipsFromTranscriptResponse> CreateTimelineClipsAsync(
      string transcriptId,
      CreateTimelineClipsFromTranscriptRequest request,
      CancellationToken cancellationToken = default);
}
