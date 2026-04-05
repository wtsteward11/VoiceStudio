using System.Collections.Generic;
using VoiceStudio.Core.Models;

namespace VoiceStudio.Core.Services
{
  /// <summary>
  /// Mutates <see cref="Project.ClipTranscriptLinks"/> for transcript–clip authority (GAP-033).
  /// Callers pass the in-memory <see cref="Project"/> (typically timeline <c>SelectedProject</c>).
  /// </summary>
  public interface IClipTranscriptLinkageService
  {
    IReadOnlyList<ClipTranscriptLink> GetLinksForClip(Project? project, string clipId);

    IReadOnlyList<ClipTranscriptLink> GetLinksForTranscription(Project? project, string transcriptionId);

    void AddOrUpdateLink(Project? project, ClipTranscriptLink link);

    void RemoveLinksByClipId(Project? project, string clipId);

    void RemoveLinksByTranscriptionId(Project? project, string transcriptionId);

    /// <summary>GAP-040: After split, copy linkage from source clip id to target clip id (same segments; shallow copy of segment id list).</summary>
    void CopyTranscriptLinksToNewClip(Project? project, string sourceClipId, string targetClipId);

    IReadOnlyList<string> ResolveSegmentIdsForClip(Project? project, string clipId);

    /// <summary>
    /// For each clip whose <see cref="AudioClip.AudioId"/> matches, replace or insert a link for this transcription and overlapping segment ids.
    /// </summary>
    void UpsertLinksForTranscription(
        Project? project,
        string transcriptionId,
        string audioId,
        IReadOnlyList<TranscriptionSegmentLinkInput> segments);
  }
}
