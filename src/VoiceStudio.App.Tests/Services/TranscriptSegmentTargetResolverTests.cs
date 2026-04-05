using System;
using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using VoiceStudio.App.Services;
using VoiceStudio.Core.Models;
using VoiceStudio.Core.Services;
using VoiceStudio.Core.Transcription;

namespace VoiceStudio.App.Tests.Services;

[TestClass]
public sealed class TranscriptSegmentTargetResolverTests
{
  private static Project MakeProject(string clipId, string audioId, double clipStartSec, double clipDurSec)
  {
    var clip = new AudioClip
    {
      Id = clipId,
      AudioId = audioId,
      StartTime = clipStartSec,
      Duration = TimeSpan.FromSeconds(clipDurSec),
    };
    var track = new AudioTrack { Id = "t1", Clips = new List<AudioClip> { clip } };
    return new Project { Id = "p1", Name = "P1", Tracks = new List<AudioTrack> { track }, ClipTranscriptLinks = new List<ClipTranscriptLink>() };
  }

  [TestMethod]
  public void Resolve_singleClip_returnsTimelineSeekWithStartOffset()
  {
    var gate = new TimelineSelectedProjectGate();
    var project = MakeProject("c1", "a1", 10, 30);
    gate.SetSelectedProject(project);
    var linkage = new ClipTranscriptLinkageService();
    linkage.AddOrUpdateLink(project, new ClipTranscriptLink
    {
      ClipId = "c1",
      TranscriptionId = "tr1",
      AudioId = "a1",
      SegmentIds = new List<string> { "s1" },
    });
    ITranscriptSegmentTargetResolver r = new TranscriptSegmentTargetResolver(gate, linkage);

    var res = r.Resolve("tr1", "s1", 2, 5);

    Assert.AreEqual(TranscriptSegmentTargetResolutionKind.Resolved, res.Kind);
    Assert.AreEqual("c1", res.ClipId);
    Assert.AreEqual(12.0, res.TimelineSeekSeconds, 0.001);
  }

  [TestMethod]
  public void Resolve_noProject_noTimelineProject()
  {
    var gate = new TimelineSelectedProjectGate();
    var linkage = new ClipTranscriptLinkageService();
    ITranscriptSegmentTargetResolver r = new TranscriptSegmentTargetResolver(gate, linkage);

    var res = r.Resolve("tr1", "s1", 0, 1);

    Assert.AreEqual(TranscriptSegmentTargetResolutionKind.NoTimelineProject, res.Kind);
  }

  [TestMethod]
  public void Resolve_twoClipsSameSegment_ambiguous()
  {
    var gate = new TimelineSelectedProjectGate();
    var project = new Project
    {
      Id = "p1",
      Name = "P1",
      Tracks = new List<AudioTrack>
      {
        new()
        {
          Id = "t1",
          Clips = new List<AudioClip>
          {
            new() { Id = "c1", AudioId = "a1", StartTime = 0, Duration = TimeSpan.FromSeconds(20) },
            new() { Id = "c2", AudioId = "a1", StartTime = 25, Duration = TimeSpan.FromSeconds(20) },
          },
        },
      },
      ClipTranscriptLinks = new List<ClipTranscriptLink>(),
    };
    gate.SetSelectedProject(project);
    var linkage = new ClipTranscriptLinkageService();
    linkage.AddOrUpdateLink(project, new ClipTranscriptLink
    {
      ClipId = "c1",
      TranscriptionId = "tr1",
      AudioId = "a1",
      SegmentIds = new List<string> { "s1" },
    });
    linkage.AddOrUpdateLink(project, new ClipTranscriptLink
    {
      ClipId = "c2",
      TranscriptionId = "tr1",
      AudioId = "a1",
      SegmentIds = new List<string> { "s1" },
    });
    ITranscriptSegmentTargetResolver r = new TranscriptSegmentTargetResolver(gate, linkage);

    var res = r.Resolve("tr1", "s1", 1, 2);

    Assert.AreEqual(TranscriptSegmentTargetResolutionKind.AmbiguousMultipleClips, res.Kind);
  }

  [TestMethod]
  public void Resolve_missingLink_unlinked()
  {
    var gate = new TimelineSelectedProjectGate();
    var project = MakeProject("c1", "a1", 0, 20);
    gate.SetSelectedProject(project);
    var linkage = new ClipTranscriptLinkageService();
    ITranscriptSegmentTargetResolver r = new TranscriptSegmentTargetResolver(gate, linkage);

    var res = r.Resolve("tr1", "s1", 1, 2);

    Assert.AreEqual(TranscriptSegmentTargetResolutionKind.Unlinked, res.Kind);
  }
}
