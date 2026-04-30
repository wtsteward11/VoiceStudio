using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using VoiceStudio.App.Core.Models;
using VoiceStudio.App.Core.Services;
using VoiceStudio.App.Services;
using VoiceStudio.App.Services.UndoableActions;
using VoiceStudio.Core.Events;
using JobDto = VoiceStudio.App.Services.Job;
using VoiceStudio.Core.Models;
using VoiceStudio.Core.Panels;
using VoiceStudio.Core.Services;
using VoiceStudio.Core.Transcription;

namespace VoiceStudio.App.Tests.Services;

[TestClass]
public sealed class TranscriptSegmentRegenerationCoordinatorTests
{
  private static Project BuildProject(string projectId, string trackId, string clipId, string profileId = "prof-1")
  {
    var clip = new AudioClip
    {
      Id = clipId,
      Name = "c",
      ProfileId = profileId,
      AudioId = "audio-old",
      AudioUrl = "/old",
      Duration = TimeSpan.FromSeconds(2),
      StartTime = 0,
    };
    var track = new AudioTrack
    {
      Id = trackId,
      Name = "t",
      ProjectId = projectId,
      Clips = new List<AudioClip> { clip },
    };
    return new Project
    {
      Id = projectId,
      Name = "p",
      Tracks = new List<AudioTrack> { track },
    };
  }


  private static Mock<IDialogueServiceClient> DialogueRegenerateOk(
      string audioId = "audio-new",
      double duration = 4.2)
  {
    var d = new Mock<IDialogueServiceClient>();
    d.Setup(x => x.RegenerateSegmentAsync(
            It.IsAny<string>(),
            It.IsAny<RegenerateDialogueSegmentRequest>(),
            It.IsAny<CancellationToken>()))
        .Returns<string, RegenerateDialogueSegmentRequest, CancellationToken>((segId, req, _) =>
            Task.FromResult(new RegenerateDialogueSegmentResponse
            {
              AudioId = audioId,
              GeneratedAudioId = audioId,
              LibraryAssetId = "lib-x",
              TimelineClipId = "tl-x",
              RoutedEngine = "piper",
              Duration = duration,
              TranscriptId = req.TranscriptId,
              SegmentId = segId,
              Status = "regenerated",
              ProjectId = req.ProjectId,
              SessionId = "",
            }));
    return d;
  }

  private static Mock<IDialogueServiceClient> DialogueRegenerateThrows(Exception ex)
  {
    var d = new Mock<IDialogueServiceClient>();
    d.Setup(x => x.RegenerateSegmentAsync(
            It.IsAny<string>(),
            It.IsAny<RegenerateDialogueSegmentRequest>(),
            It.IsAny<CancellationToken>()))
        .ThrowsAsync(ex);
    return d;
  }

  [TestMethod]
  public async Task TryExecuteAsync_Success_UpdatesClip_RemovesLinks_PublishesEvent_RegistersUndo()
  {
    var project = BuildProject("p1", "t1", "c1");
    var gate = new Mock<ITimelineSelectedProjectGate>();
    gate.SetupGet(g => g.SelectedProject).Returns(project);

    var resolver = new Mock<ITranscriptSegmentTargetResolver>();
    resolver
        .Setup(r => r.Resolve("tr1", "seg1", It.IsAny<double>(), It.IsAny<double>()))
        .Returns(TranscriptSegmentTargetResolution.Resolved("t1", "c1", "tr1", 0, 1, 0));
    var dialogue = DialogueRegenerateOk();


    var backend = new Mock<IBackendClient>();
    backend
        .Setup(b => b.UpdateClipAsync("p1", "t1", "c1", null, null, "audio-new", "/api/voice/audio/audio-new", 4.2, null, null, null, null, It.IsAny<CancellationToken>()))
        .ReturnsAsync(new AudioClip { Id = "c1", AudioId = "audio-new" });

    var savedLink = new ClipTranscriptLink
    {
      ClipId = "c1",
      TranscriptionId = "tr1",
      AudioId = "audio-old",
      SegmentIds = new List<string> { "seg1" },
    };
    var linkage = new Mock<IClipTranscriptLinkageService>();
    linkage.Setup(l => l.GetLinksForClip(project, "c1")).Returns(new List<ClipTranscriptLink> { savedLink });

    var dirty = new Mock<IProjectSessionDirtyState>();
    var events = new Mock<IEventAggregator>();
    ClipAudioArtifactReplacedEvent? published = null;
    events.Setup(e => e.Publish(It.IsAny<ClipAudioArtifactReplacedEvent>()))
        .Callback<object>(ev => published = (ClipAudioArtifactReplacedEvent)ev);

    var undo = new UndoRedoService();
    var sut = new TranscriptSegmentRegenerationCoordinator(
        dialogue.Object,
        backend.Object,
        linkage.Object,
        gate.Object,
        resolver.Object,
        dirty.Object,
        undo,
        events.Object,
        null);

    var transcription = new TranscriptionResponse { Id = "tr1" };
    var segment = new TranscriptionSegment { Id = "seg1", Start = 0, End = 1 };
    var msg = await sut.TryExecuteAsync(transcription, segment, PanelIds.Transcribe, null, CancellationToken.None);

    Assert.IsNull(msg);
    backend.Verify(
        b => b.UpdateClipAsync("p1", "t1", "c1", null, null, "audio-new", "/api/voice/audio/audio-new", 4.2, null, null, null, null, It.IsAny<CancellationToken>()),
        Times.Once);
    linkage.Verify(l => l.RemoveLinksByClipId(project, "c1"), Times.Once);
    dirty.Verify(d => d.MarkProjectDirty("transcript_segment_regenerate"), Times.Once);
    Assert.IsNotNull(published);
    Assert.AreEqual("audio-new", published!.AudioId);
    Assert.AreEqual("/api/voice/audio/audio-new", published.AudioUrl);
    Assert.AreEqual(4.2, published.DurationSeconds, 0.001);
    Assert.AreEqual(TranscriptTruthState.StaleAfterClipRegeneration, project.Tracks[0].Clips[0].TranscriptTruth);
    events.Verify(e => e.Publish(It.IsAny<TranscriptTruthStateChangedEvent>()), Times.Once);
    Assert.AreEqual(1, undo.UndoCount);
    Assert.AreEqual("Regenerate transcript segment (clip audio)", undo.NextUndoActionName);
  }

  [TestMethod]
  public async Task TryExecuteAsync_JobFailed_DoesNotUpdateClip()
  {
    var project = BuildProject("p1", "t1", "c1");
    var gate = new Mock<ITimelineSelectedProjectGate>();
    gate.SetupGet(g => g.SelectedProject).Returns(project);
    var resolver = new Mock<ITranscriptSegmentTargetResolver>();
    resolver
        .Setup(r => r.Resolve("tr1", "seg1", It.IsAny<double>(), It.IsAny<double>()))
        .Returns(TranscriptSegmentTargetResolution.Resolved("t1", "c1", "tr1", 0, 1, 0));
    var dialogue = DialogueRegenerateThrows(new InvalidOperationException("synthesis blew up"));


    var backend = new Mock<IBackendClient>();
    var linkage = new Mock<IClipTranscriptLinkageService>();
    linkage.Setup(l => l.GetLinksForClip(It.IsAny<Project?>(), It.IsAny<string>())).Returns(Array.Empty<ClipTranscriptLink>());

    var sut = new TranscriptSegmentRegenerationCoordinator(
        dialogue.Object,
        backend.Object,
        linkage.Object,
        gate.Object,
        resolver.Object,
        null,
        null,
        null,
        null);

    var msg = await sut.TryExecuteAsync(
        new TranscriptionResponse { Id = "tr1" },
        new TranscriptionSegment { Id = "seg1", Start = 0, End = 1 },
        PanelIds.Transcribe,
        null,
        CancellationToken.None);

    Assert.AreEqual("Regeneration failed: synthesis blew up", msg);
    backend.Verify(
        b => b.UpdateClipAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<double?>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<double?>(), It.IsAny<double?>(), It.IsAny<double?>(), It.IsAny<double?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()),
        Times.Never);
  }

  [TestMethod]
  public async Task TryExecuteAsync_WithReplacementText_PersistsTranscriptionThroughClient()
  {
    var project = BuildProject("p1", "t1", "c1");
    var gate = new Mock<ITimelineSelectedProjectGate>();
    gate.SetupGet(g => g.SelectedProject).Returns(project);

    var resolver = new Mock<ITranscriptSegmentTargetResolver>();
    resolver
        .Setup(r => r.Resolve("tr1", "seg1", It.IsAny<double>(), It.IsAny<double>()))
        .Returns(TranscriptSegmentTargetResolution.Resolved("t1", "c1", "tr1", 0, 1, 0));
    var dialogue = DialogueRegenerateOk();


    var backend = new Mock<IBackendClient>();
    backend
        .Setup(b => b.UpdateClipAsync("p1", "t1", "c1", null, null, "audio-new", "/api/voice/audio/audio-new", 4.2, null, null, null, null, It.IsAny<CancellationToken>()))
        .ReturnsAsync(new AudioClip { Id = "c1", AudioId = "audio-new" });

    var linkage = new Mock<IClipTranscriptLinkageService>();
    linkage.Setup(l => l.GetLinksForClip(project, "c1")).Returns(Array.Empty<ClipTranscriptLink>());

    var txClient = new Mock<ITranscriptionClient>();
    txClient
        .Setup(t => t.UpdateTranscriptionTextAsync(
            "tr1",
            "new words",
            It.Is<List<TranscriptionSegment>>(segments =>
                segments.Count == 1
                && segments[0].Id == "seg1"
                && segments[0].Text == "new words"),
            It.IsAny<CancellationToken>()))
        .ReturnsAsync(new TranscriptionResponse
        {
          Id = "tr1",
          Text = "new words",
          Segments = new List<TranscriptionSegment>
          {
            new() { Id = "seg1", Start = 0, End = 1, Text = "new words" },
          },
        });

    var sut = new TranscriptSegmentRegenerationCoordinator(
        dialogue.Object,
        backend.Object,
        linkage.Object,
        gate.Object,
        resolver.Object,
        null,
        null,
        null,
        null,
        txClient.Object);

    var transcription = new TranscriptionResponse
    {
      Id = "tr1",
      Text = "original",
      Segments = new List<TranscriptionSegment>
      {
        new() { Id = "seg1", Start = 0, End = 1, Text = "original" },
      },
    };
    var segment = new TranscriptionSegment { Id = "seg1", Start = 0, End = 1, Text = "original" };

    var msg = await sut.TryExecuteAsync(
        transcription,
        segment,
        PanelIds.Transcribe,
        "new words",
        CancellationToken.None);

    Assert.IsNull(msg);
    txClient.VerifyAll();
    Assert.AreEqual("new words", transcription.Text);
    Assert.AreEqual("new words", transcription.Segments![0].Text);
  }

  /// <summary>GAP-047: registered undo restores pre-apply transcript via <see cref="ITranscriptionClient"/> and publishes coherence navigation.</summary>
  [TestMethod]
  public async Task TryExecuteAsync_WithReplacementText_UndoRestoresTranscriptSnapshotAndCoherenceNavigate()
  {
    var project = BuildProject("p1", "t1", "c1");
    var gate = new Mock<ITimelineSelectedProjectGate>();
    gate.SetupGet(g => g.SelectedProject).Returns(project);

    var resolver = new Mock<ITranscriptSegmentTargetResolver>();
    resolver
        .Setup(r => r.Resolve("tr1", "seg1", It.IsAny<double>(), It.IsAny<double>()))
        .Returns(TranscriptSegmentTargetResolution.Resolved("t1", "c1", "tr1", 0, 1, 0));
    var dialogue = DialogueRegenerateOk();


    var backend = new Mock<IBackendClient>();
    backend
        .Setup(b => b.UpdateClipAsync("p1", "t1", "c1", null, null, "audio-new", "/api/voice/audio/audio-new", 4.2, null, null, null, null, It.IsAny<CancellationToken>()))
        .ReturnsAsync(new AudioClip { Id = "c1", AudioId = "audio-new" });
    backend
        .Setup(b => b.UpdateClipAsync("p1", "t1", "c1", null, null, "audio-old", "/old", 2, null, null, null, null, It.IsAny<CancellationToken>()))
        .ReturnsAsync(new AudioClip { Id = "c1", AudioId = "audio-old" });

    var linkage = new Mock<IClipTranscriptLinkageService>();
    linkage.Setup(l => l.GetLinksForClip(project, "c1")).Returns(Array.Empty<ClipTranscriptLink>());

    var txClient = new Mock<ITranscriptionClient>();
    txClient
        .Setup(t => t.UpdateTranscriptionTextAsync(
            "tr1",
            "new words",
            It.IsAny<List<TranscriptionSegment>>(),
            It.IsAny<CancellationToken>()))
        .ReturnsAsync(new TranscriptionResponse
        {
          Id = "tr1",
          Text = "new words",
          Segments = new List<TranscriptionSegment>
          {
            new() { Id = "seg1", Start = 0, End = 1, Text = "new words" },
          },
        });
    txClient
        .Setup(t => t.UpdateTranscriptionTextAsync(
            "tr1",
            "original",
            It.Is<List<TranscriptionSegment>>(l => l.Count == 1 && l[0].Text == "original"),
            It.IsAny<CancellationToken>()))
        .ReturnsAsync(new TranscriptionResponse
        {
          Id = "tr1",
          Text = "original",
          Segments = new List<TranscriptionSegment>
          {
            new() { Id = "seg1", Start = 0, End = 1, Text = "original" },
          },
        });

    var events = new Mock<IEventAggregator>();
    events.Setup(e => e.Publish(It.IsAny<ClipAudioArtifactReplacedEvent>()));
    events.Setup(e => e.Publish(It.IsAny<TranscriptTruthStateChangedEvent>()));
    NavigateToEvent? coherenceNav = null;
    events
        .Setup(e => e.Publish(It.IsAny<NavigateToEvent>()))
        .Callback<NavigateToEvent>(ev => coherenceNav = ev);

    var undo = new UndoRedoService();
    var sut = new TranscriptSegmentRegenerationCoordinator(
        dialogue.Object,
        backend.Object,
        linkage.Object,
        gate.Object,
        resolver.Object,
        null,
        undo,
        events.Object,
        null,
        txClient.Object);

    var transcription = new TranscriptionResponse
    {
      Id = "tr1",
      Text = "original",
      Segments = new List<TranscriptionSegment>
      {
        new() { Id = "seg1", Start = 0, End = 1, Text = "original" },
      },
    };
    var segment = new TranscriptionSegment { Id = "seg1", Start = 0, End = 1, Text = "original" };

    var msg = await sut.TryExecuteAsync(
        transcription,
        segment,
        PanelIds.Transcribe,
        "new words",
        CancellationToken.None);

    Assert.IsNull(msg);
    Assert.AreEqual("new words", transcription.Segments![0].Text);

    Assert.IsTrue(undo.Undo());
    Assert.AreEqual("original", transcription.Segments[0].Text);
    txClient.Verify(
        t => t.UpdateTranscriptionTextAsync(
            "tr1",
            It.IsAny<string>(),
            It.IsAny<List<TranscriptionSegment>>(),
            It.IsAny<CancellationToken>()),
        Times.Exactly(2));
    Assert.IsNotNull(coherenceNav);
    Assert.IsTrue(coherenceNav!.Parameters!.TryGetValue("action", out var a));
    Assert.AreEqual("coherentReloadAfterSegmentApply", a?.ToString());
  }

  [TestMethod]
  public async Task Apply_WithTranscriptPersistFailure_RestoresPreApplyClipState()
  {
    var project = BuildProject("p1", "t1", "c1");
    var gate = new Mock<ITimelineSelectedProjectGate>();
    gate.SetupGet(g => g.SelectedProject).Returns(project);

    var resolver = new Mock<ITranscriptSegmentTargetResolver>();
    resolver
        .Setup(r => r.Resolve("tr1", "seg1", It.IsAny<double>(), It.IsAny<double>()))
        .Returns(TranscriptSegmentTargetResolution.Resolved("t1", "c1", "tr1", 0, 1, 0));
    var dialogue = DialogueRegenerateOk();


    var backend = new Mock<IBackendClient>();
    backend
        .Setup(b => b.UpdateClipAsync("p1", "t1", "c1", null, null, "audio-new", "/api/voice/audio/audio-new", 4.2, null, null, null, null, It.IsAny<CancellationToken>()))
        .ReturnsAsync(new AudioClip { Id = "c1", AudioId = "audio-new" });
    backend
        .Setup(b => b.UpdateClipAsync("p1", "t1", "c1", null, null, "audio-old", "/old", 2, null, null, null, null, It.IsAny<CancellationToken>()))
        .ReturnsAsync(new AudioClip { Id = "c1", AudioId = "audio-old" });

    var linkage = new Mock<IClipTranscriptLinkageService>();
    linkage.Setup(l => l.GetLinksForClip(project, "c1")).Returns(Array.Empty<ClipTranscriptLink>());

    var txClient = new Mock<ITranscriptionClient>();
    txClient
        .Setup(t => t.UpdateTranscriptionTextAsync(
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<List<TranscriptionSegment>>(),
            It.IsAny<CancellationToken>()))
        .ThrowsAsync(new InvalidOperationException("persist unavailable"));

    var sut = new TranscriptSegmentRegenerationCoordinator(
        dialogue.Object,
        backend.Object,
        linkage.Object,
        gate.Object,
        resolver.Object,
        null,
        null,
        null,
        null,
        txClient.Object);

    var msg = await sut.TryExecuteAsync(
        new TranscriptionResponse
        {
          Id = "tr1",
          Text = "original",
          Segments = new List<TranscriptionSegment>
          {
            new() { Id = "seg1", Start = 0, End = 1, Text = "original" },
          },
        },
        new TranscriptionSegment { Id = "seg1", Start = 0, End = 1, Text = "original" },
        PanelIds.Transcribe,
        "new words",
        CancellationToken.None);

    StringAssert.Contains(msg ?? string.Empty, "transcript persistence failed");
    backend.Verify(
        b => b.UpdateClipAsync("p1", "t1", "c1", null, null, "audio-new", "/api/voice/audio/audio-new", 4.2, null, null, null, null, It.IsAny<CancellationToken>()),
        Times.Once);
    backend.Verify(
        b => b.UpdateClipAsync("p1", "t1", "c1", null, null, "audio-old", "/old", 2, null, null, null, null, It.IsAny<CancellationToken>()),
        Times.Once);
    linkage.Verify(l => l.RemoveLinksByClipId(project, "c1"), Times.Never);
    Assert.AreEqual("audio-old", project.Tracks[0].Clips[0].AudioId);
    Assert.AreEqual("/old", project.Tracks[0].Clips[0].AudioUrl);
    Assert.AreEqual(2, project.Tracks[0].Clips[0].Duration.TotalSeconds, 0.001);
  }

  [TestMethod]
  public async Task Apply_WithTranscriptPersistFailure_DoesNotRegisterUndoAction()
  {
    var project = BuildProject("p1", "t1", "c1");
    var gate = new Mock<ITimelineSelectedProjectGate>();
    gate.SetupGet(g => g.SelectedProject).Returns(project);
    var resolver = new Mock<ITranscriptSegmentTargetResolver>();
    resolver
        .Setup(r => r.Resolve("tr1", "seg1", It.IsAny<double>(), It.IsAny<double>()))
        .Returns(TranscriptSegmentTargetResolution.Resolved("t1", "c1", "tr1", 0, 1, 0));
    var dialogue = DialogueRegenerateOk();

    var backend = new Mock<IBackendClient>();
    backend
        .Setup(b => b.UpdateClipAsync("p1", "t1", "c1", null, null, "audio-new", "/api/voice/audio/audio-new", 4.2, null, null, null, null, It.IsAny<CancellationToken>()))
        .ReturnsAsync(new AudioClip { Id = "c1", AudioId = "audio-new" });
    backend
        .Setup(b => b.UpdateClipAsync("p1", "t1", "c1", null, null, "audio-old", "/old", 2, null, null, null, null, It.IsAny<CancellationToken>()))
        .ReturnsAsync(new AudioClip { Id = "c1", AudioId = "audio-old" });
    var linkage = new Mock<IClipTranscriptLinkageService>();
    linkage.Setup(l => l.GetLinksForClip(project, "c1")).Returns(Array.Empty<ClipTranscriptLink>());
    var txClient = new Mock<ITranscriptionClient>();
    txClient
        .Setup(t => t.UpdateTranscriptionTextAsync(
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<List<TranscriptionSegment>>(),
            It.IsAny<CancellationToken>()))
        .ThrowsAsync(new InvalidOperationException("persist unavailable"));
    var undo = new UndoRedoService();
    var sut = new TranscriptSegmentRegenerationCoordinator(
        dialogue.Object,
        backend.Object,
        linkage.Object,
        gate.Object,
        resolver.Object,
        null,
        undo,
        null,
        null,
        txClient.Object);

    _ = await sut.TryExecuteAsync(
        new TranscriptionResponse
        {
          Id = "tr1",
          Text = "original",
          Segments = new List<TranscriptionSegment> { new() { Id = "seg1", Start = 0, End = 1, Text = "original" } },
        },
        new TranscriptionSegment { Id = "seg1", Start = 0, End = 1, Text = "original" },
        PanelIds.Transcribe,
        "new words",
        CancellationToken.None);

    Assert.AreEqual(0, undo.UndoCount);
  }

  [TestMethod]
  public async Task Apply_WithTranscriptPersistFailure_DoesNotPublishSuccessEvents()
  {
    var project = BuildProject("p1", "t1", "c1");
    var gate = new Mock<ITimelineSelectedProjectGate>();
    gate.SetupGet(g => g.SelectedProject).Returns(project);
    var resolver = new Mock<ITranscriptSegmentTargetResolver>();
    resolver
        .Setup(r => r.Resolve("tr1", "seg1", It.IsAny<double>(), It.IsAny<double>()))
        .Returns(TranscriptSegmentTargetResolution.Resolved("t1", "c1", "tr1", 0, 1, 0));
    var dialogue = DialogueRegenerateOk();

    var backend = new Mock<IBackendClient>();
    backend
        .Setup(b => b.UpdateClipAsync("p1", "t1", "c1", null, null, "audio-new", "/api/voice/audio/audio-new", 4.2, null, null, null, null, It.IsAny<CancellationToken>()))
        .ReturnsAsync(new AudioClip { Id = "c1", AudioId = "audio-new" });
    backend
        .Setup(b => b.UpdateClipAsync("p1", "t1", "c1", null, null, "audio-old", "/old", 2, null, null, null, null, It.IsAny<CancellationToken>()))
        .ReturnsAsync(new AudioClip { Id = "c1", AudioId = "audio-old" });
    var linkage = new Mock<IClipTranscriptLinkageService>();
    linkage.Setup(l => l.GetLinksForClip(project, "c1")).Returns(Array.Empty<ClipTranscriptLink>());
    var txClient = new Mock<ITranscriptionClient>();
    txClient
        .Setup(t => t.UpdateTranscriptionTextAsync(
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<List<TranscriptionSegment>>(),
            It.IsAny<CancellationToken>()))
        .ThrowsAsync(new InvalidOperationException("persist unavailable"));
    var events = new Mock<IEventAggregator>();
    var sut = new TranscriptSegmentRegenerationCoordinator(
        dialogue.Object,
        backend.Object,
        linkage.Object,
        gate.Object,
        resolver.Object,
        null,
        null,
        events.Object,
        null,
        txClient.Object);

    _ = await sut.TryExecuteAsync(
        new TranscriptionResponse
        {
          Id = "tr1",
          Text = "a",
          Segments = new List<TranscriptionSegment> { new() { Id = "seg1", Start = 0, End = 1, Text = "a" } },
        },
        new TranscriptionSegment { Id = "seg1", Start = 0, End = 1, Text = "a" },
        PanelIds.Transcribe,
        "b",
        CancellationToken.None);

    events.Verify(e => e.Publish(It.IsAny<ClipAudioArtifactReplacedEvent>()), Times.Never);
    events.Verify(e => e.Publish(It.IsAny<TranscriptTruthStateChangedEvent>()), Times.Never);
  }

  [TestMethod]
  public async Task Apply_WithPersistenceMessage_FollowsAtomicFailureContract()
  {
    var project = BuildProject("p1", "t1", "c1");
    var gate = new Mock<ITimelineSelectedProjectGate>();
    gate.SetupGet(g => g.SelectedProject).Returns(project);
    var resolver = new Mock<ITranscriptSegmentTargetResolver>();
    resolver
        .Setup(r => r.Resolve("tr1", "seg1", It.IsAny<double>(), It.IsAny<double>()))
        .Returns(TranscriptSegmentTargetResolution.Resolved("t1", "c1", "tr1", 0, 1, 0));
    var dialogue = DialogueRegenerateOk();

    var backend = new Mock<IBackendClient>();
    backend
        .Setup(b => b.UpdateClipAsync("p1", "t1", "c1", null, null, "audio-new", "/api/voice/audio/audio-new", 4.2, null, null, null, null, It.IsAny<CancellationToken>()))
        .ReturnsAsync(new AudioClip { Id = "c1", AudioId = "audio-new" });
    backend
        .Setup(b => b.UpdateClipAsync("p1", "t1", "c1", null, null, "audio-old", "/old", 2, null, null, null, null, It.IsAny<CancellationToken>()))
        .ReturnsAsync(new AudioClip { Id = "c1", AudioId = "audio-old" });
    var linkage = new Mock<IClipTranscriptLinkageService>();
    linkage.Setup(l => l.GetLinksForClip(project, "c1")).Returns(Array.Empty<ClipTranscriptLink>());
    var txClient = new Mock<ITranscriptionClient>();
    txClient
        .Setup(t => t.UpdateTranscriptionTextAsync(
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<List<TranscriptionSegment>>(),
            It.IsAny<CancellationToken>()))
        .ThrowsAsync(new InvalidOperationException("persist unavailable"));
    var undo = new UndoRedoService();
    var events = new Mock<IEventAggregator>();
    var sut = new TranscriptSegmentRegenerationCoordinator(
        dialogue.Object,
        backend.Object,
        linkage.Object,
        gate.Object,
        resolver.Object,
        null,
        undo,
        events.Object,
        null,
        txClient.Object);

    var msg = await sut.TryExecuteAsync(
        new TranscriptionResponse
        {
          Id = "tr1",
          Text = "original",
          Segments = new List<TranscriptionSegment> { new() { Id = "seg1", Start = 0, End = 1, Text = "original" } },
        },
        new TranscriptionSegment { Id = "seg1", Start = 0, End = 1, Text = "original" },
        PanelIds.Transcribe,
        "new words",
        CancellationToken.None);

    StringAssert.Contains(msg ?? string.Empty, "transcript persistence failed");
    Assert.AreEqual(0, undo.UndoCount);
    linkage.Verify(l => l.RemoveLinksByClipId(project, "c1"), Times.Never);
    events.Verify(e => e.Publish(It.IsAny<ClipAudioArtifactReplacedEvent>()), Times.Never);
    backend.Verify(
        b => b.UpdateClipAsync("p1", "t1", "c1", null, null, "audio-new", "/api/voice/audio/audio-new", 4.2, null, null, null, null, It.IsAny<CancellationToken>()),
        Times.Once);
    backend.Verify(
        b => b.UpdateClipAsync("p1", "t1", "c1", null, null, "audio-old", "/old", 2, null, null, null, null, It.IsAny<CancellationToken>()),
        Times.Once);
  }

  [TestMethod]
  public async Task RangeApply_WithTranscriptPersistFailure_RestoresPreApplyState()
  {
    var project = BuildProject("p1", "t1", "c1");
    var gate = new Mock<ITimelineSelectedProjectGate>();
    gate.SetupGet(g => g.SelectedProject).Returns(project);
    var resolver = new Mock<ITranscriptSegmentTargetResolver>();
    resolver
        .Setup(r => r.Resolve("tr1", "seg1", It.IsAny<double>(), It.IsAny<double>()))
        .Returns(TranscriptSegmentTargetResolution.Resolved("t1", "c1", "tr1", 0, 1, 0));
    var dialogue = DialogueRegenerateOk();

    var backend = new Mock<IBackendClient>();
    backend
        .Setup(b => b.UpdateClipAsync("p1", "t1", "c1", null, null, "audio-new", "/api/voice/audio/audio-new", 4.2, null, null, null, null, It.IsAny<CancellationToken>()))
        .ReturnsAsync(new AudioClip { Id = "c1", AudioId = "audio-new" });
    backend
        .Setup(b => b.UpdateClipAsync("p1", "t1", "c1", null, null, "audio-old", "/old", 2, null, null, null, null, It.IsAny<CancellationToken>()))
        .ReturnsAsync(new AudioClip { Id = "c1", AudioId = "audio-old" });
    var linkage = new Mock<IClipTranscriptLinkageService>();
    linkage.Setup(l => l.GetLinksForClip(project, "c1")).Returns(Array.Empty<ClipTranscriptLink>());
    var txClient = new Mock<ITranscriptionClient>();
    txClient
        .Setup(t => t.UpdateTranscriptionTextAsync(
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<List<TranscriptionSegment>>(),
            It.IsAny<CancellationToken>()))
        .ThrowsAsync(new InvalidOperationException("persist unavailable"));
    var sut = new TranscriptSegmentRegenerationCoordinator(
        dialogue.Object,
        backend.Object,
        linkage.Object,
        gate.Object,
        resolver.Object,
        null,
        null,
        null,
        null,
        txClient.Object);

    var transcription = new TranscriptionResponse
    {
      Id = "tr1",
      Text = string.Empty,
      Segments = new List<TranscriptionSegment>
      {
        new() { Id = "seg1", Start = 0, End = 1, Text = "aa" },
        new() { Id = "seg2", Start = 1, End = 2, Text = "bb" },
      },
    };

    var msg = await sut.TryExecuteAsync(
        transcription,
        transcription.Segments[0],
        PanelIds.Transcribe,
        "merged",
        CancellationToken.None,
        jobProgress: null,
        operationCorrelationId: null,
        rangeEndInclusiveIndex: 1);

    StringAssert.Contains(msg ?? string.Empty, "transcript persistence failed");
    backend.Verify(
        b => b.UpdateClipAsync("p1", "t1", "c1", null, null, "audio-new", "/api/voice/audio/audio-new", 4.2, null, null, null, null, It.IsAny<CancellationToken>()),
        Times.Once);
    backend.Verify(
        b => b.UpdateClipAsync("p1", "t1", "c1", null, null, "audio-old", "/old", 2, null, null, null, null, It.IsAny<CancellationToken>()),
        Times.Once);
    linkage.Verify(l => l.RemoveLinksByClipId(project, "c1"), Times.Never);
    Assert.AreEqual("audio-old", project.Tracks[0].Clips[0].AudioId);
  }

  [TestMethod]
  public async Task TryExecuteAsync_PersistFailure_CompensateClipThrows_AppendsRollbackMessage()
  {
    var project = BuildProject("p1", "t1", "c1");
    var gate = new Mock<ITimelineSelectedProjectGate>();
    gate.SetupGet(g => g.SelectedProject).Returns(project);
    var resolver = new Mock<ITranscriptSegmentTargetResolver>();
    resolver
        .Setup(r => r.Resolve("tr1", "seg1", It.IsAny<double>(), It.IsAny<double>()))
        .Returns(TranscriptSegmentTargetResolution.Resolved("t1", "c1", "tr1", 0, 1, 0));
    var dialogue = DialogueRegenerateOk();

    var backend = new Mock<IBackendClient>();
    backend
        .Setup(b => b.UpdateClipAsync("p1", "t1", "c1", null, null, "audio-new", "/api/voice/audio/audio-new", 4.2, null, null, null, null, It.IsAny<CancellationToken>()))
        .ReturnsAsync(new AudioClip { Id = "c1", AudioId = "audio-new" });
    backend
        .Setup(b => b.UpdateClipAsync("p1", "t1", "c1", null, null, "audio-old", "/old", 2, null, null, null, null, It.IsAny<CancellationToken>()))
        .ThrowsAsync(new InvalidOperationException("rollback failed"));
    var linkage = new Mock<IClipTranscriptLinkageService>();
    linkage.Setup(l => l.GetLinksForClip(project, "c1")).Returns(Array.Empty<ClipTranscriptLink>());
    var txClient = new Mock<ITranscriptionClient>();
    txClient
        .Setup(t => t.UpdateTranscriptionTextAsync(
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<List<TranscriptionSegment>>(),
            It.IsAny<CancellationToken>()))
        .ThrowsAsync(new InvalidOperationException("persist unavailable"));
    var sut = new TranscriptSegmentRegenerationCoordinator(
        dialogue.Object,
        backend.Object,
        linkage.Object,
        gate.Object,
        resolver.Object,
        null,
        null,
        null,
        null,
        txClient.Object);

    var msg = await sut.TryExecuteAsync(
        new TranscriptionResponse
        {
          Id = "tr1",
          Text = "x",
          Segments = new List<TranscriptionSegment> { new() { Id = "seg1", Start = 0, End = 1, Text = "x" } },
        },
        new TranscriptionSegment { Id = "seg1", Start = 0, End = 1, Text = "x" },
        PanelIds.Transcribe,
        "y",
        CancellationToken.None);

    StringAssert.Contains(msg ?? string.Empty, "transcript persistence failed");
    StringAssert.Contains(msg ?? string.Empty, "Clip audio rollback also failed");
    StringAssert.Contains(msg ?? string.Empty, "rollback failed");
  }

  [TestMethod]
  public async Task TryExecuteAsync_NoProject_ReturnsEarly()
  {
    var gate = new Mock<ITimelineSelectedProjectGate>();
    gate.SetupGet(g => g.SelectedProject).Returns((Project?)null);
    var dialogue = DialogueRegenerateOk();
    var resolver = new Mock<ITranscriptSegmentTargetResolver>();

    var backend = new Mock<IBackendClient>();
    var linkage = new Mock<IClipTranscriptLinkageService>();
    linkage.Setup(l => l.GetLinksForClip(It.IsAny<Project?>(), It.IsAny<string>())).Returns(Array.Empty<ClipTranscriptLink>());

    var sut = new TranscriptSegmentRegenerationCoordinator(
        dialogue.Object,
        backend.Object,
        linkage.Object,
        gate.Object,
        resolver.Object);

    var msg = await sut.TryExecuteAsync(
        new TranscriptionResponse { Id = "tr1" },
        new TranscriptionSegment { Id = "seg1", Start = 0, End = 1 },
        PanelIds.Transcribe,
        null,
        CancellationToken.None);

    StringAssert.Contains(msg, "No timeline project is active.");
    dialogue.Verify(
        d => d.RegenerateSegmentAsync(
            It.IsAny<string>(),
            It.IsAny<RegenerateDialogueSegmentRequest>(),
            It.IsAny<CancellationToken>()),
        Times.Never);
    backend.Verify(
        b => b.UpdateClipAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<double?>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<double?>(), It.IsAny<double?>(), It.IsAny<double?>(), It.IsAny<double?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()),
        Times.Never);
  }

  [TestMethod]
  public async Task TryExecuteAsync_ApplyClipThrows_ReturnsError_DoesNotRemoveLinks()
  {
    var project = BuildProject("p1", "t1", "c1");
    var gate = new Mock<ITimelineSelectedProjectGate>();
    gate.SetupGet(g => g.SelectedProject).Returns(project);
    var resolver = new Mock<ITranscriptSegmentTargetResolver>();
    resolver
        .Setup(r => r.Resolve("tr1", "seg1", It.IsAny<double>(), It.IsAny<double>()))
        .Returns(TranscriptSegmentTargetResolution.Resolved("t1", "c1", "tr1", 0, 1, 0));
    var dialogue = DialogueRegenerateOk();


    var backend = new Mock<IBackendClient>();
    backend
        .Setup(b => b.UpdateClipAsync("p1", "t1", "c1", null, null, "audio-new", It.IsAny<string?>(), It.IsAny<double?>(), It.IsAny<double?>(), It.IsAny<double?>(), It.IsAny<double?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
        .ThrowsAsync(new InvalidOperationException("disk full"));

    var linkage = new Mock<IClipTranscriptLinkageService>();
    linkage.Setup(l => l.GetLinksForClip(project, "c1")).Returns(Array.Empty<ClipTranscriptLink>());

    var sut = new TranscriptSegmentRegenerationCoordinator(
        dialogue.Object,
        backend.Object,
        linkage.Object,
        gate.Object,
        resolver.Object);

    var msg = await sut.TryExecuteAsync(
        new TranscriptionResponse { Id = "tr1" },
        new TranscriptionSegment { Id = "seg1", Start = 0, End = 1 },
        PanelIds.Transcribe,
        null,
        CancellationToken.None);

    StringAssert.Contains(msg, "disk full");
    linkage.Verify(l => l.RemoveLinksByClipId(It.IsAny<Project>(), It.IsAny<string>()), Times.Never);
  }

  [TestMethod]
  public async Task TryExecuteAsync_WithProgress_EmitsPendingRunningAndSessionSucceeded()
  {
    var project = BuildProject("p1", "t1", "c1");
    var gate = new Mock<ITimelineSelectedProjectGate>();
    gate.SetupGet(g => g.SelectedProject).Returns(project);
    var resolver = new Mock<ITranscriptSegmentTargetResolver>();
    resolver
        .Setup(r => r.Resolve("tr1", "seg1", It.IsAny<double>(), It.IsAny<double>()))
        .Returns(TranscriptSegmentTargetResolution.Resolved("t1", "c1", "tr1", 0, 1, 0));
    var dialogue = DialogueRegenerateOk();


    var backend = new Mock<IBackendClient>();
    backend
        .Setup(b => b.UpdateClipAsync("p1", "t1", "c1", null, null, "audio-new", "/api/voice/audio/audio-new", 4.2, null, null, null, null, It.IsAny<CancellationToken>()))
        .ReturnsAsync(new AudioClip { Id = "c1", AudioId = "audio-new" });

    var linkage = new Mock<IClipTranscriptLinkageService>();
    linkage.Setup(l => l.GetLinksForClip(project, "c1")).Returns(Array.Empty<ClipTranscriptLink>());

    var reports = new System.Collections.Generic.List<TranscriptRegenerationJobProgressReport>();
    var progress = new Progress<TranscriptRegenerationJobProgressReport>(reports.Add);

    var sut = new TranscriptSegmentRegenerationCoordinator(
        dialogue.Object,
        backend.Object,
        linkage.Object,
        gate.Object,
        resolver.Object);

    var transcription = new TranscriptionResponse { Id = "tr1" };
    var segment = new TranscriptionSegment { Id = "seg1", Start = 0, End = 1 };
    var msg = await sut.TryExecuteAsync(
        transcription,
        segment,
        PanelIds.Transcribe,
        null,
        CancellationToken.None,
        progress,
        "op-1");

    Assert.IsNull(msg);
    Assert.IsTrue(reports.Count >= 2, $"expected progress reports; got {reports.Count}");
    Assert.IsTrue(
        reports.Any(r => string.Equals(r.BackendStatus, "pending", StringComparison.OrdinalIgnoreCase)),
        "expected pending progress");
    var terminalStatus = reports[^1].BackendStatus ?? string.Empty;
    Assert.IsTrue(
        terminalStatus.Contains("session_succeeded", StringComparison.OrdinalIgnoreCase)
        || terminalStatus.Contains("completed", StringComparison.OrdinalIgnoreCase),
        $"Expected terminal progress status to contain 'session_succeeded' or 'completed', got '{terminalStatus}'.");
  }

  [TestMethod]
  public async Task TryExecuteAsync_SecondInvocation_WithNewCorrelationId_StillSucceeds()
  {
    var project = BuildProject("p1", "t1", "c1");
    var gate = new Mock<ITimelineSelectedProjectGate>();
    gate.SetupGet(g => g.SelectedProject).Returns(project);

    var resolver = new Mock<ITranscriptSegmentTargetResolver>();
    resolver
        .Setup(r => r.Resolve("tr1", "seg1", It.IsAny<double>(), It.IsAny<double>()))
        .Returns(TranscriptSegmentTargetResolution.Resolved("t1", "c1", "tr1", 0, 1, 0));
    var dialogue = DialogueRegenerateOk();


    var backend = new Mock<IBackendClient>();
    backend
        .Setup(b => b.UpdateClipAsync("p1", "t1", "c1", null, null, "audio-new", "/api/voice/audio/audio-new", 4.2, null, null, null, null, It.IsAny<CancellationToken>()))
        .ReturnsAsync(new AudioClip { Id = "c1", AudioId = "audio-new" });

    var linkage = new Mock<IClipTranscriptLinkageService>();
    linkage.Setup(l => l.GetLinksForClip(project, "c1")).Returns(Array.Empty<ClipTranscriptLink>());

    var sut = new TranscriptSegmentRegenerationCoordinator(
        dialogue.Object,
        backend.Object,
        linkage.Object,
        gate.Object,
        resolver.Object);

    var transcription = new TranscriptionResponse { Id = "tr1" };
    var segment = new TranscriptionSegment { Id = "seg1", Start = 0, End = 1 };

    var err1 = await sut.TryExecuteAsync(
        transcription,
        segment,
        PanelIds.Transcribe,
        "replace",
        CancellationToken.None,
        null,
        "op-first");
    var err2 = await sut.TryExecuteAsync(
        transcription,
        segment,
        PanelIds.Transcribe,
        "replace",
        CancellationToken.None,
        null,
        "op-second");

    Assert.IsNull(err1);
    Assert.IsNull(err2);
    dialogue.Verify(
        x => x.RegenerateSegmentAsync(
            It.IsAny<string>(),
            It.IsAny<RegenerateDialogueSegmentRequest>(),
            It.IsAny<CancellationToken>()),
        Times.Exactly(2));
  }
}
