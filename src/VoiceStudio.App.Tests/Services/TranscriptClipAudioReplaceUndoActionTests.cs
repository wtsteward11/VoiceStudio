using System;
using System.Collections.Generic;
using System.Threading;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using VoiceStudio.App.Core.Services;
using VoiceStudio.App.Services;
using VoiceStudio.App.Services.UndoableActions;
using VoiceStudio.Core.Events;
using VoiceStudio.Core.Models;
using VoiceStudio.Core.Panels;
using VoiceStudio.Core.Services;

namespace VoiceStudio.App.Tests.Services;

[TestClass]
public sealed class TranscriptClipAudioReplaceUndoActionTests
{
  private static Project ProjectWithClip(string pid, string tid, string cid, string audioId, string url, double dur)
  {
    var clip = new AudioClip
    {
      Id = cid,
      Name = "n",
      ProfileId = "pr",
      AudioId = audioId,
      AudioUrl = url,
      Duration = TimeSpan.FromSeconds(dur),
      StartTime = 0,
    };
    return new Project
    {
      Id = pid,
      Name = "p",
      Tracks = new List<AudioTrack>
      {
        new()
        {
          Id = tid,
          Name = "t",
          ProjectId = pid,
          Clips = new List<AudioClip> { clip },
        },
      },
    };
  }

  [TestMethod]
  public void Undo_RestoresBackendClip_AddsLinks_PublishesEvent_MarksDirty()
  {
    var project = ProjectWithClip("p1", "t1", "c1", "new-audio", "/new", 3);
    var backend = new Mock<IBackendClient>();
    var linkage = new Mock<IClipTranscriptLinkageService>();
    var dirty = new Mock<IProjectSessionDirtyState>();
    var events = new Mock<IEventAggregator>();
    ClipAudioArtifactReplacedEvent? published = null;
    events.Setup(e => e.Publish(It.IsAny<ClipAudioArtifactReplacedEvent>()))
        .Callback<object>(e => published = (ClipAudioArtifactReplacedEvent)e);

    var saved = new ClipTranscriptLink
    {
      ClipId = "c1",
      TranscriptionId = "tr1",
      AudioId = "old-audio",
      SegmentIds = new List<string> { "s1" },
    };

    var sut = new TranscriptClipAudioReplaceUndoAction(
        backend.Object,
        linkage.Object,
        project,
        "p1",
        "t1",
        "c1",
        "old-audio",
        "/old",
        2.5,
        "new-audio",
        "/new",
        3,
        new List<ClipTranscriptLink> { saved },
        dirty.Object,
        events.Object,
        PanelIds.Transcribe);

    backend
        .Setup(b => b.UpdateClipAsync("p1", "t1", "c1", null, null, "old-audio", "/old", 2.5, null, null, null, null, It.IsAny<CancellationToken>()))
        .ReturnsAsync(new AudioClip { Id = "c1" });

    sut.Undo();

    backend.Verify(
        b => b.UpdateClipAsync("p1", "t1", "c1", null, null, "old-audio", "/old", 2.5, null, null, null, null, It.IsAny<CancellationToken>()),
        Times.Once);
    linkage.Verify(
        l => l.AddOrUpdateLink(project, It.Is<ClipTranscriptLink>(x => x.TranscriptionId == "tr1" && x.ClipId == "c1")),
        Times.Once);
    dirty.Verify(d => d.MarkProjectDirty("transcript_segment_regenerate_undo"), Times.Once);
    Assert.IsNotNull(published);
    Assert.AreEqual("old-audio", published!.AudioId);
    Assert.AreEqual("/old", published.AudioUrl);
    Assert.AreEqual(2.5, published.DurationSeconds, 0.001);
    var clip = project.Tracks![0].Clips![0];
    Assert.AreEqual("old-audio", clip.AudioId);
    Assert.AreEqual("/old", clip.AudioUrl);
    Assert.AreEqual(2.5, clip.Duration.TotalSeconds, 0.001);
  }

  [TestMethod]
  public void Redo_ReappliesNewAudio_RemovesLinks_PublishesEvent_MarksDirty()
  {
    var project = ProjectWithClip("p1", "t1", "c1", "old-audio", "/old", 2.5);
    var backend = new Mock<IBackendClient>();
    var linkage = new Mock<IClipTranscriptLinkageService>();
    var dirty = new Mock<IProjectSessionDirtyState>();
    var events = new Mock<IEventAggregator>();
    ClipAudioArtifactReplacedEvent? published = null;
    events
        .Setup(e => e.Publish(It.IsAny<ClipAudioArtifactReplacedEvent>()))
        .Callback<object>(e => published = (ClipAudioArtifactReplacedEvent)e);

    var sut = new TranscriptClipAudioReplaceUndoAction(
        backend.Object,
        linkage.Object,
        project,
        "p1",
        "t1",
        "c1",
        "old-audio",
        "/old",
        2.5,
        "new-audio",
        "/new",
        3,
        Array.Empty<ClipTranscriptLink>(),
        dirty.Object,
        events.Object,
        PanelIds.Transcribe);

    backend
        .Setup(b => b.UpdateClipAsync("p1", "t1", "c1", null, null, "new-audio", "/new", 3, null, null, null, null, It.IsAny<CancellationToken>()))
        .ReturnsAsync(new AudioClip { Id = "c1" });

    sut.Redo();

    backend.Verify(
        b => b.UpdateClipAsync("p1", "t1", "c1", null, null, "new-audio", "/new", 3, null, null, null, null, It.IsAny<CancellationToken>()),
        Times.Once);
    linkage.Verify(l => l.RemoveLinksByClipId(project, "c1"), Times.Once);
    dirty.Verify(d => d.MarkProjectDirty("transcript_segment_regenerate_redo"), Times.Once);
    Assert.IsNotNull(published);
    Assert.AreEqual("new-audio", published!.AudioId);
    var clip = project.Tracks![0].Clips![0];
    Assert.AreEqual("new-audio", clip.AudioId);
  }

  [TestMethod]
  public void Redo_PublishesEvent_WithNewDuration()
  {
    var project = ProjectWithClip("p1", "t1", "c1", "old-audio", "/old", 2.5);
    var backend = new Mock<IBackendClient>();
    var linkage = new Mock<IClipTranscriptLinkageService>();
    var dirty = new Mock<IProjectSessionDirtyState>();
    var events = new Mock<IEventAggregator>();
    ClipAudioArtifactReplacedEvent? published = null;
    events
        .Setup(e => e.Publish(It.IsAny<ClipAudioArtifactReplacedEvent>()))
        .Callback<object>(e => published = (ClipAudioArtifactReplacedEvent)e);

    var sut = new TranscriptClipAudioReplaceUndoAction(
        backend.Object,
        linkage.Object,
        project,
        "p1",
        "t1",
        "c1",
        "old-audio",
        "/old",
        2.5,
        "new-audio",
        "/new",
        4.25,
        Array.Empty<ClipTranscriptLink>(),
        dirty.Object,
        events.Object,
        PanelIds.Transcribe);

    backend
        .Setup(b => b.UpdateClipAsync("p1", "t1", "c1", null, null, "new-audio", "/new", 4.25, null, null, null, null, It.IsAny<CancellationToken>()))
        .ReturnsAsync(new AudioClip { Id = "c1" });

    sut.Redo();

    Assert.IsNotNull(published);
    Assert.AreEqual("new-audio", published!.AudioId);
    Assert.AreEqual("/new", published.AudioUrl);
    Assert.AreEqual(4.25, published.DurationSeconds, 0.001);
  }

  [TestMethod]
  public void UndoThenRedo_MarksProjectDirtyTwice()
  {
    var project = ProjectWithClip("p1", "t1", "c1", "new-audio", "/new", 3);
    var backend = new Mock<IBackendClient>();
    backend
        .Setup(b => b.UpdateClipAsync("p1", "t1", "c1", null, null, "old-audio", "/old", 2.5, null, null, null, null, It.IsAny<CancellationToken>()))
        .ReturnsAsync(new AudioClip { Id = "c1" });
    backend
        .Setup(b => b.UpdateClipAsync("p1", "t1", "c1", null, null, "new-audio", "/new", 3, null, null, null, null, It.IsAny<CancellationToken>()))
        .ReturnsAsync(new AudioClip { Id = "c1" });

    var linkage = new Mock<IClipTranscriptLinkageService>();
    var dirty = new Mock<IProjectSessionDirtyState>();
    var events = new Mock<IEventAggregator>();

    var sut = new TranscriptClipAudioReplaceUndoAction(
        backend.Object,
        linkage.Object,
        project,
        "p1",
        "t1",
        "c1",
        "old-audio",
        "/old",
        2.5,
        "new-audio",
        "/new",
        3,
        Array.Empty<ClipTranscriptLink>(),
        dirty.Object,
        events.Object,
        PanelIds.Transcribe);

    sut.Undo();
    sut.Redo();

    dirty.Verify(d => d.MarkProjectDirty("transcript_segment_regenerate_undo"), Times.Once);
    dirty.Verify(d => d.MarkProjectDirty("transcript_segment_regenerate_redo"), Times.Once);
  }
}
