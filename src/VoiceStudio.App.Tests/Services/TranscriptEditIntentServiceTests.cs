using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using VoiceStudio.App.Services;
using VoiceStudio.Core.Models;
using VoiceStudio.Core.Services;
using VoiceStudio.Core.Transcription;

namespace VoiceStudio.App.Tests.Services;

[TestClass]
public sealed class TranscriptEditIntentServiceTests
{
  [TestMethod]
  public void TryRecordIntent_Regenerate_recordsExecutableIntent_WithProjectAndTrack()
  {
    var gate = new TimelineSelectedProjectGate();
    var project = new Project
    {
      Id = "p1",
      Tracks = new List<AudioTrack>
      {
        new()
        {
          Id = "t1",
          Clips = new List<AudioClip>
          {
            new()
            {
              Id = "c1",
              AudioId = "a1",
              StartTime = 5,
              Duration = TimeSpan.FromSeconds(60),
            },
          },
        },
      },
      ClipTranscriptLinks = new List<ClipTranscriptLink>
      {
        new()
        {
          ClipId = "c1",
          TranscriptionId = "tr1",
          AudioId = "a1",
          SegmentIds = new List<string> { "s9" },
        },
      },
    };
    gate.SetSelectedProject(project);
    var linkage = new ClipTranscriptLinkageService();
    var resolver = new TranscriptSegmentTargetResolver(gate, linkage);
    ITranscriptEditIntentService svc = new TranscriptEditIntentService(resolver, gate);

    var ok = svc.TryRecordIntent(TranscriptEditIntentKind.RegenerateRange, "tr1", "s9", 1, 4, out var err);

    Assert.IsTrue(ok);
    Assert.IsNull(err);
    Assert.IsNotNull(svc.Current);
    Assert.AreEqual(TranscriptEditIntentKind.RegenerateRange, svc.Current!.Kind);
    Assert.IsTrue(svc.Current.DownstreamExecutable);
    Assert.IsNull(svc.Current.ExecutionBlockedReason);
    Assert.AreEqual("p1", svc.Current.ProjectId);
    Assert.AreEqual("t1", svc.Current.TargetTrackId);
    Assert.AreEqual("c1", svc.Current.TargetClipId);
    Assert.AreEqual(6.0, svc.Current.TimelineSeekSeconds, 0.001);
  }

  [TestMethod]
  public void TryRecordIntent_ReplaceRange_IsExecutable_WithReplacementText()
  {
    var gate = new TimelineSelectedProjectGate();
    var project = new Project
    {
      Id = "p1",
      Tracks = new List<AudioTrack>
      {
        new()
        {
          Id = "t1",
          Clips = new List<AudioClip>
          {
            new()
            {
              Id = "c1",
              AudioId = "a1",
              StartTime = 5,
              Duration = TimeSpan.FromSeconds(60),
            },
          },
        },
      },
      ClipTranscriptLinks = new List<ClipTranscriptLink>
      {
        new()
        {
          ClipId = "c1",
          TranscriptionId = "tr1",
          AudioId = "a1",
          SegmentIds = new List<string> { "s9" },
        },
      },
    };
    gate.SetSelectedProject(project);
    var linkage = new ClipTranscriptLinkageService();
    var resolver = new TranscriptSegmentTargetResolver(gate, linkage);
    ITranscriptEditIntentService svc = new TranscriptEditIntentService(resolver, gate);

    var ok = svc.TryRecordIntent(TranscriptEditIntentKind.ReplaceRange, "tr1", "s9", 1, 4, out var err, "replaced text");

    Assert.IsTrue(ok);
    Assert.IsNull(err);
    Assert.IsNotNull(svc.Current);
    Assert.AreEqual(TranscriptEditIntentKind.ReplaceRange, svc.Current!.Kind);
    Assert.IsTrue(svc.Current.DownstreamExecutable);
    Assert.IsNull(svc.Current.ExecutionBlockedReason);
    Assert.AreEqual("replaced text", svc.Current.ReplacementText);
    Assert.AreEqual(6.0, svc.Current.TimelineSeekSeconds, 0.001);
  }

  [TestMethod]
  public void TryRecordIntent_ReplaceRange_EmptyReplacement_fails()
  {
    var gate = new TimelineSelectedProjectGate();
    gate.SetSelectedProject(new Project
    {
      Id = "p1",
      Tracks = new List<AudioTrack>
      {
        new()
        {
          Id = "t1",
          Clips = new List<AudioClip>
          {
            new()
            {
              Id = "c1",
              AudioId = "a1",
              StartTime = 5,
              Duration = TimeSpan.FromSeconds(60),
            },
          },
        },
      },
      ClipTranscriptLinks = new List<ClipTranscriptLink>
      {
        new()
        {
          ClipId = "c1",
          TranscriptionId = "tr1",
          AudioId = "a1",
          SegmentIds = new List<string> { "s9" },
        },
      },
    });
    var linkage = new ClipTranscriptLinkageService();
    var resolver = new TranscriptSegmentTargetResolver(gate, linkage);
    ITranscriptEditIntentService svc = new TranscriptEditIntentService(resolver, gate);

    var ok = svc.TryRecordIntent(TranscriptEditIntentKind.ReplaceRange, "tr1", "s9", 1, 4, out var err, "   ");

    Assert.IsFalse(ok);
    Assert.IsNotNull(err);
    Assert.IsNull(svc.Current);
  }

  [TestMethod]
  public void TryRecordIntent_unlinked_fails()
  {
    var gate = new TimelineSelectedProjectGate();
    gate.SetSelectedProject(new Project
    {
      Id = "p1",
      Tracks = new List<AudioTrack>
      {
        new() { Id = "t1", Clips = new List<AudioClip>() },
      },
    });
    var linkage = new ClipTranscriptLinkageService();
    var resolver = new TranscriptSegmentTargetResolver(gate, linkage);
    ITranscriptEditIntentService svc = new TranscriptEditIntentService(resolver, gate);

    var ok = svc.TryRecordIntent(TranscriptEditIntentKind.RemoveRange, "tr1", "s1", 0, 1, out var err);

    Assert.IsFalse(ok);
    Assert.IsNotNull(err);
    Assert.IsNull(svc.Current);
  }
}
