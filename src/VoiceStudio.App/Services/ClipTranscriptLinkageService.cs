using System;
using System.Collections.Generic;
using System.Linq;
using VoiceStudio.Core.Models;
using VoiceStudio.Core.Services;

namespace VoiceStudio.App.Services
{
  /// <summary>
  /// Mutates <see cref="Project.ClipTranscriptLinks"/> in memory; shell save persists JSON.
  /// </summary>
  public sealed class ClipTranscriptLinkageService : IClipTranscriptLinkageService
  {
    public IReadOnlyList<ClipTranscriptLink> GetLinksForClip(Project? project, string clipId)
    {
      if (project?.ClipTranscriptLinks == null || string.IsNullOrEmpty(clipId))
        return Array.Empty<ClipTranscriptLink>();
      return project.ClipTranscriptLinks.Where(l => string.Equals(l.ClipId, clipId, StringComparison.Ordinal)).ToList();
    }

    public IReadOnlyList<ClipTranscriptLink> GetLinksForTranscription(Project? project, string transcriptionId)
    {
      if (project?.ClipTranscriptLinks == null || string.IsNullOrEmpty(transcriptionId))
        return Array.Empty<ClipTranscriptLink>();
      return project.ClipTranscriptLinks
          .Where(l => string.Equals(l.TranscriptionId, transcriptionId, StringComparison.Ordinal)).ToList();
    }

    public void AddOrUpdateLink(Project? project, ClipTranscriptLink link)
    {
      if (project == null || string.IsNullOrEmpty(link.ClipId))
        return;
      project.ClipTranscriptLinks ??= new List<ClipTranscriptLink>();
      project.ClipTranscriptLinks.RemoveAll(x => string.Equals(x.ClipId, link.ClipId, StringComparison.Ordinal));
      project.ClipTranscriptLinks.Add(link);
    }

    public void RemoveLinksByClipId(Project? project, string clipId)
    {
      if (project?.ClipTranscriptLinks == null || string.IsNullOrEmpty(clipId))
        return;
      project.ClipTranscriptLinks.RemoveAll(x => string.Equals(x.ClipId, clipId, StringComparison.Ordinal));
    }

    public void RemoveLinksByTranscriptionId(Project? project, string transcriptionId)
    {
      if (project?.ClipTranscriptLinks == null || string.IsNullOrEmpty(transcriptionId))
        return;
      project.ClipTranscriptLinks.RemoveAll(x =>
          string.Equals(x.TranscriptionId, transcriptionId, StringComparison.Ordinal));
    }

    public void CopyTranscriptLinksToNewClip(Project? project, string sourceClipId, string targetClipId)
    {
      if (project == null || string.IsNullOrEmpty(sourceClipId) || string.IsNullOrEmpty(targetClipId))
        return;
      if (string.Equals(sourceClipId, targetClipId, StringComparison.Ordinal))
        return;
      var src = GetLinksForClip(project, sourceClipId);
      if (src.Count == 0)
        return;
      foreach (var l in src)
      {
        AddOrUpdateLink(project, new ClipTranscriptLink
        {
          ClipId = targetClipId,
          TranscriptionId = l.TranscriptionId,
          AudioId = l.AudioId,
          SegmentIds = new List<string>(l.SegmentIds),
        });
        break;
      }
    }

    public IReadOnlyList<string> ResolveSegmentIdsForClip(Project? project, string clipId)
    {
      var links = GetLinksForClip(project, clipId);
      if (links.Count == 0)
        return Array.Empty<string>();
      return links[0].SegmentIds;
    }

    public void UpsertLinksForTranscription(
        Project? project,
        string transcriptionId,
        string audioId,
        IReadOnlyList<TranscriptionSegmentLinkInput> segments)
    {
      if (project?.Tracks == null || string.IsNullOrEmpty(transcriptionId) || string.IsNullOrEmpty(audioId))
        return;

      foreach (var track in project.Tracks)
      {
        if (track.Clips == null)
          continue;
        foreach (var clip in track.Clips)
        {
          if (!string.Equals(clip.AudioId, audioId, StringComparison.Ordinal))
            continue;
          var clipLen = clip.Duration.TotalSeconds;
          if (clipLen <= 0)
            continue;
          var ids = segments
              .Where(s => s.End > 0 && s.Start < clipLen && !string.IsNullOrWhiteSpace(s.Id))
              .Select(s => s.Id)
              .Distinct(StringComparer.Ordinal)
              .ToList();
          AddOrUpdateLink(project, new ClipTranscriptLink
          {
            ClipId = clip.Id,
            TranscriptionId = transcriptionId,
            AudioId = audioId,
            SegmentIds = ids,
          });
        }
      }
    }
  }
}
