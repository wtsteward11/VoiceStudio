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
using JobDto = VoiceStudio.App.Services.Job;
using VoiceStudio.Core.Events;
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

    var regen = new Mock<ITranscriptRegenerationClient>();
    regen
        .Setup(x => x.StartRegenerateSegmentAsync(It.IsAny<RegenerateSegmentStartRequest>(), It.IsAny<CancellationToken>()))
        .ReturnsAsync(new RegenerateSegmentJobStartResponse { JobId = "job-a", Status = "pending" });

    var jobs = new Mock<IJobProgressApiClient>();
    var terminalJob = new JobDto
    {
      Id = "job-a",
      Status = "completed",
      ResultId = "audio-new",
      Metadata = new Dictionary<string, object>
      {
        ["audio_url"] = "/new.wav",
        ["duration_seconds"] = 4.2,
      },
    };
    jobs.Setup(j => j.GetJobAsync("job-a", It.IsAny<CancellationToken>())).ReturnsAsync(terminalJob);

    var backend = new Mock<IBackendClient>();
    backend
        .Setup(b => b.UpdateClipAsync("p1", "t1", "c1", null, null, "audio-new", "/new.wav", 4.2, null, null, null, null, It.IsAny<CancellationToken>()))
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
        regen.Object,
        jobs.Object,
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
        b => b.UpdateClipAsync("p1", "t1", "c1", null, null, "audio-new", "/new.wav", 4.2, null, null, null, null, It.IsAny<CancellationToken>()),
        Times.Once);
    linkage.Verify(l => l.RemoveLinksByClipId(project, "c1"), Times.Once);
    dirty.Verify(d => d.MarkProjectDirty("transcript_segment_regenerate"), Times.Once);
    Assert.IsNotNull(published);
    Assert.AreEqual("audio-new", published!.AudioId);
    Assert.AreEqual("/new.wav", published.AudioUrl);
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

    var regen = new Mock<ITranscriptRegenerationClient>();
    regen
        .Setup(x => x.StartRegenerateSegmentAsync(It.IsAny<RegenerateSegmentStartRequest>(), It.IsAny<CancellationToken>()))
        .ReturnsAsync(new RegenerateSegmentJobStartResponse { JobId = "job-f", Status = "pending" });

    var jobs = new Mock<IJobProgressApiClient>();
    jobs.Setup(j => j.GetJobAsync("job-f", It.IsAny<CancellationToken>())).ReturnsAsync(new JobDto
    {
      Id = "job-f",
      Status = "failed",
      ErrorMessage = "synthesis blew up",
    });

    var backend = new Mock<IBackendClient>();
    var linkage = new Mock<IClipTranscriptLinkageService>();
    linkage.Setup(l => l.GetLinksForClip(It.IsAny<Project?>(), It.IsAny<string>())).Returns(Array.Empty<ClipTranscriptLink>());

    var sut = new TranscriptSegmentRegenerationCoordinator(
        regen.Object,
        jobs.Object,
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

    Assert.AreEqual("synthesis blew up", msg);
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

    var regen = new Mock<ITranscriptRegenerationClient>();
    regen
        .Setup(x => x.StartRegenerateSegmentAsync(It.IsAny<RegenerateSegmentStartRequest>(), It.IsAny<CancellationToken>()))
        .ReturnsAsync(new RegenerateSegmentJobStartResponse { JobId = "job-upd", Status = "pending" });

    var jobs = new Mock<IJobProgressApiClient>();
    jobs.Setup(j => j.GetJobAsync("job-upd", It.IsAny<CancellationToken>())).ReturnsAsync(new JobDto
    {
      Id = "job-upd",
      Status = "completed",
      ResultId = "audio-new",
      Metadata = new Dictionary<string, object>
      {
        ["audio_url"] = "/new.wav",
        ["duration_seconds"] = 4.2,
      },
    });

    var backend = new Mock<IBackendClient>();
    backend
        .Setup(b => b.UpdateClipAsync("p1", "t1", "c1", null, null, "audio-new", "/new.wav", 4.2, null, null, null, null, It.IsAny<CancellationToken>()))
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
        regen.Object,
        jobs.Object,
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

  [TestMethod]
  public async Task TryExecuteAsync_PersistFailure_ReturnsWarningAfterClipApply()
  {
    var project = BuildProject("p1", "t1", "c1");
    var gate = new Mock<ITimelineSelectedProjectGate>();
    gate.SetupGet(g => g.SelectedProject).Returns(project);

    var resolver = new Mock<ITranscriptSegmentTargetResolver>();
    resolver
        .Setup(r => r.Resolve("tr1", "seg1", It.IsAny<double>(), It.IsAny<double>()))
        .Returns(TranscriptSegmentTargetResolution.Resolved("t1", "c1", "tr1", 0, 1, 0));

    var regen = new Mock<ITranscriptRegenerationClient>();
    regen
        .Setup(x => x.StartRegenerateSegmentAsync(It.IsAny<RegenerateSegmentStartRequest>(), It.IsAny<CancellationToken>()))
        .ReturnsAsync(new RegenerateSegmentJobStartResponse { JobId = "job-warn", Status = "pending" });

    var jobs = new Mock<IJobProgressApiClient>();
    jobs.Setup(j => j.GetJobAsync("job-warn", It.IsAny<CancellationToken>())).ReturnsAsync(new JobDto
    {
      Id = "job-warn",
      Status = "completed",
      ResultId = "audio-new",
      Metadata = new Dictionary<string, object>
      {
        ["audio_url"] = "/new.wav",
        ["duration_seconds"] = 4.2,
      },
    });

    var backend = new Mock<IBackendClient>();
    backend
        .Setup(b => b.UpdateClipAsync("p1", "t1", "c1", null, null, "audio-new", "/new.wav", 4.2, null, null, null, null, It.IsAny<CancellationToken>()))
        .ReturnsAsync(new AudioClip { Id = "c1", AudioId = "audio-new" });

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
        regen.Object,
        jobs.Object,
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
        b => b.UpdateClipAsync("p1", "t1", "c1", null, null, "audio-new", "/new.wav", 4.2, null, null, null, null, It.IsAny<CancellationToken>()),
        Times.Once);
    linkage.Verify(l => l.RemoveLinksByClipId(project, "c1"), Times.Once);
  }

  [TestMethod]
  public async Task TryExecuteAsync_NoProject_ReturnsEarly()
  {
    var gate = new Mock<ITimelineSelectedProjectGate>();
    gate.SetupGet(g => g.SelectedProject).Returns((Project?)null);
    var regen = new Mock<ITranscriptRegenerationClient>();
    var jobs = new Mock<IJobProgressApiClient>();
    var backend = new Mock<IBackendClient>();
    var linkage = new Mock<IClipTranscriptLinkageService>();
    var resolver = new Mock<ITranscriptSegmentTargetResolver>();

    var sut = new TranscriptSegmentRegenerationCoordinator(
        regen.Object,
        jobs.Object,
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

    StringAssert.Contains(msg, "timeline");
    regen.Verify(x => x.StartRegenerateSegmentAsync(It.IsAny<RegenerateSegmentStartRequest>(), It.IsAny<CancellationToken>()), Times.Never);
  }

  [TestMethod]
  public async Task TryExecuteAsync_ResolverNotResolved_DoesNotStartJob()
  {
    var project = BuildProject("p1", "t1", "c1");
    var gate = new Mock<ITimelineSelectedProjectGate>();
    gate.SetupGet(g => g.SelectedProject).Returns(project);
    var resolver = new Mock<ITranscriptSegmentTargetResolver>();
    resolver
        .Setup(r => r.Resolve("tr1", "seg1", It.IsAny<double>(), It.IsAny<double>()))
        .Returns(TranscriptSegmentTargetResolution.Failure(
            TranscriptSegmentTargetResolutionKind.Unlinked,
            "tr1",
            0,
            1,
            "not linked"));

    var regen = new Mock<ITranscriptRegenerationClient>();
    var jobs = new Mock<IJobProgressApiClient>();
    var backend = new Mock<IBackendClient>();
    var linkage = new Mock<IClipTranscriptLinkageService>();

    var sut = new TranscriptSegmentRegenerationCoordinator(
        regen.Object,
        jobs.Object,
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

    Assert.AreEqual("not linked", msg);
    regen.Verify(x => x.StartRegenerateSegmentAsync(It.IsAny<RegenerateSegmentStartRequest>(), It.IsAny<CancellationToken>()), Times.Never);
  }

  [TestMethod]
  public async Task TryExecuteAsync_ClipMissingOnProject_ReturnsBeforeJob()
  {
    var emptyProject = new Project { Id = "p1", Name = "p", Tracks = new List<AudioTrack>() };
    var gate = new Mock<ITimelineSelectedProjectGate>();
    gate.SetupGet(g => g.SelectedProject).Returns(emptyProject);
    var resolver = new Mock<ITranscriptSegmentTargetResolver>();
    resolver
        .Setup(r => r.Resolve("tr1", "seg1", It.IsAny<double>(), It.IsAny<double>()))
        .Returns(TranscriptSegmentTargetResolution.Resolved("t1", "c1", "tr1", 0, 1, 0));

    var regen = new Mock<ITranscriptRegenerationClient>();
    var sut = new TranscriptSegmentRegenerationCoordinator(
        regen.Object,
        Mock.Of<IJobProgressApiClient>(),
        Mock.Of<IBackendClient>(),
        Mock.Of<IClipTranscriptLinkageService>(),
        gate.Object,
        resolver.Object);

    var msg = await sut.TryExecuteAsync(
        new TranscriptionResponse { Id = "tr1" },
        new TranscriptionSegment { Id = "seg1", Start = 0, End = 1 },
        PanelIds.Transcribe,
        null,
        CancellationToken.None);

    StringAssert.Contains(msg, "linked clip");
    regen.Verify(x => x.StartRegenerateSegmentAsync(It.IsAny<RegenerateSegmentStartRequest>(), It.IsAny<CancellationToken>()), Times.Never);
  }

  [TestMethod]
  public async Task TryExecuteAsync_CompletedJobMissingResultId_ReturnsError()
  {
    var project = BuildProject("p1", "t1", "c1");
    var gate = new Mock<ITimelineSelectedProjectGate>();
    gate.SetupGet(g => g.SelectedProject).Returns(project);
    var resolver = new Mock<ITranscriptSegmentTargetResolver>();
    resolver
        .Setup(r => r.Resolve("tr1", "seg1", It.IsAny<double>(), It.IsAny<double>()))
        .Returns(TranscriptSegmentTargetResolution.Resolved("t1", "c1", "tr1", 0, 1, 0));

    var regen = new Mock<ITranscriptRegenerationClient>();
    regen
        .Setup(x => x.StartRegenerateSegmentAsync(It.IsAny<RegenerateSegmentStartRequest>(), It.IsAny<CancellationToken>()))
        .ReturnsAsync(new RegenerateSegmentJobStartResponse { JobId = "job-z", Status = "pending" });

    var jobs = new Mock<IJobProgressApiClient>();
    jobs.Setup(j => j.GetJobAsync("job-z", It.IsAny<CancellationToken>())).ReturnsAsync(new JobDto
    {
      Id = "job-z",
      Status = "completed",
      ResultId = null,
    });

    var backend = new Mock<IBackendClient>();
    var linkage = new Mock<IClipTranscriptLinkageService>();
    linkage.Setup(l => l.GetLinksForClip(It.IsAny<Project?>(), It.IsAny<string>())).Returns(Array.Empty<ClipTranscriptLink>());

    var sut = new TranscriptSegmentRegenerationCoordinator(
        regen.Object,
        jobs.Object,
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

    StringAssert.Contains(msg, "audio result");
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

    var regen = new Mock<ITranscriptRegenerationClient>();
    regen
        .Setup(x => x.StartRegenerateSegmentAsync(It.IsAny<RegenerateSegmentStartRequest>(), It.IsAny<CancellationToken>()))
        .ReturnsAsync(new RegenerateSegmentJobStartResponse { JobId = "job-x", Status = "pending" });

    var jobs = new Mock<IJobProgressApiClient>();
    jobs.Setup(j => j.GetJobAsync("job-x", It.IsAny<CancellationToken>())).ReturnsAsync(new JobDto
    {
      Id = "job-x",
      Status = "completed",
      ResultId = "audio-new",
      Metadata = new Dictionary<string, object> { ["duration_seconds"] = 1.0 },
    });

    var backend = new Mock<IBackendClient>();
    backend
        .Setup(b => b.UpdateClipAsync("p1", "t1", "c1", null, null, "audio-new", It.IsAny<string?>(), It.IsAny<double?>(), It.IsAny<double?>(), It.IsAny<double?>(), It.IsAny<double?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
        .ThrowsAsync(new InvalidOperationException("disk full"));

    var linkage = new Mock<IClipTranscriptLinkageService>();
    linkage.Setup(l => l.GetLinksForClip(project, "c1")).Returns(Array.Empty<ClipTranscriptLink>());

    var sut = new TranscriptSegmentRegenerationCoordinator(
        regen.Object,
        jobs.Object,
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

    var regen = new Mock<ITranscriptRegenerationClient>();
    regen
        .Setup(x => x.StartRegenerateSegmentAsync(It.IsAny<RegenerateSegmentStartRequest>(), It.IsAny<CancellationToken>()))
        .ReturnsAsync(new RegenerateSegmentJobStartResponse { JobId = "job-p", Status = "pending" });

    var running = new JobDto
    {
      Id = "job-p",
      Status = "running",
      Progress = 0.4,
      CurrentStep = "synth",
    };
    var terminalJob = new JobDto
    {
      Id = "job-p",
      Status = "completed",
      Progress = 1,
      ResultId = "audio-new",
      Metadata = new Dictionary<string, object>
      {
        ["audio_url"] = "/new.wav",
        ["duration_seconds"] = 4.2,
      },
    };
    var call = 0;
    var jobs = new Mock<IJobProgressApiClient>();
    jobs
        .Setup(j => j.GetJobAsync("job-p", It.IsAny<CancellationToken>()))
        .Returns(() =>
        {
          call++;
          return Task.FromResult<JobDto?>(call == 1 ? running : terminalJob);
        });

    var backend = new Mock<IBackendClient>();
    backend
        .Setup(b => b.UpdateClipAsync("p1", "t1", "c1", null, null, "audio-new", "/new.wav", 4.2, null, null, null, null, It.IsAny<CancellationToken>()))
        .ReturnsAsync(new AudioClip { Id = "c1", AudioId = "audio-new" });

    var linkage = new Mock<IClipTranscriptLinkageService>();
    linkage.Setup(l => l.GetLinksForClip(project, "c1")).Returns(Array.Empty<ClipTranscriptLink>());

    var reports = new System.Collections.Generic.List<TranscriptRegenerationJobProgressReport>();
    var progress = new Progress<TranscriptRegenerationJobProgressReport>(reports.Add);

    var sut = new TranscriptSegmentRegenerationCoordinator(
        regen.Object,
        jobs.Object,
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
    Assert.IsTrue(reports.Count >= 3, "expected pending + running + completed + session_succeeded");
    Assert.AreEqual("pending", reports[0].BackendStatus, StringComparer.OrdinalIgnoreCase);
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

    var regen = new Mock<ITranscriptRegenerationClient>();
    var jobIds = new Queue<string>(new[] { "job-op1", "job-op2" });
    regen
        .Setup(x => x.StartRegenerateSegmentAsync(It.IsAny<RegenerateSegmentStartRequest>(), It.IsAny<CancellationToken>()))
        .ReturnsAsync(() => new RegenerateSegmentJobStartResponse
        {
          JobId = jobIds.Dequeue(),
          Status = "pending",
        });

    var jobs = new Mock<IJobProgressApiClient>();
    jobs
        .Setup(j => j.GetJobAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
        .ReturnsAsync((string id, CancellationToken _) => new JobDto
        {
          Id = id,
          Status = "completed",
          ResultId = "audio-new",
          Metadata = new Dictionary<string, object>
          {
            ["audio_url"] = "/new.wav",
            ["duration_seconds"] = 4.2,
          },
        });

    var backend = new Mock<IBackendClient>();
    backend
        .Setup(b => b.UpdateClipAsync("p1", "t1", "c1", null, null, "audio-new", "/new.wav", 4.2, null, null, null, null, It.IsAny<CancellationToken>()))
        .ReturnsAsync(new AudioClip { Id = "c1", AudioId = "audio-new" });

    var linkage = new Mock<IClipTranscriptLinkageService>();
    linkage.Setup(l => l.GetLinksForClip(project, "c1")).Returns(Array.Empty<ClipTranscriptLink>());

    var sut = new TranscriptSegmentRegenerationCoordinator(
        regen.Object,
        jobs.Object,
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
    regen.Verify(
        x => x.StartRegenerateSegmentAsync(It.IsAny<RegenerateSegmentStartRequest>(), It.IsAny<CancellationToken>()),
        Times.Exactly(2));
  }
}
