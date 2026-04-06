using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using VoiceStudio.App.Services;
using VoiceStudio.App.Tests.Fixtures;
using VoiceStudio.App.ViewModels;
using VoiceStudio.App.Views.Panels;
using VoiceStudio.Core.Events;
using VoiceStudio.Core.Models;
using VoiceStudio.Core.Panels;
using VoiceStudio.Core.Services;

namespace VoiceStudio.App.Tests.ViewModels;

/// <summary>GAP-045 cross-consumer: timeline subtitle track tracks backend transcription id and refetches quietly.</summary>
[TestClass]
public sealed class TimelineViewModelGap045CrossConsumerTests
{
  private Mock<ITimelineSynthesisService> _mockSynthesisService = null!;
  private Mock<ITimelineClipService> _mockClipService = null!;
  private Mock<ITimelineTrackService> _mockTrackService = null!;
  private Mock<ITimelineTranscriptionService> _mockTranscriptionService = null!;
  private Mock<IProjectAudioClient> _mockProjectAudioClient = null!;
  private Mock<IAudioVisualizationService> _mockAudioVisualizationService = null!;
  private Mock<IProjectsClient> _mockProjectsClient = null!;
  private Mock<IProfilesClient> _mockProfilesClient = null!;
  private Mock<IAudioPlayerService> _mockAudioPlayer = null!;
  private Mock<MultiSelectService> _mockMultiSelectService = null!;
  private Mock<IDialogService> _mockDialogService = null!;
  private Mock<IProjectRepository> _mockProjectRepository = null!;
  private TimelineViewModel _sut = null!;

  [TestInitialize]
  public void Setup()
  {
    TestAppServicesHelper.EnsureInitialized();
    _mockSynthesisService = new Mock<ITimelineSynthesisService>();
    _mockClipService = new Mock<ITimelineClipService>();
    _mockTrackService = new Mock<ITimelineTrackService>();
    _mockTranscriptionService = new Mock<ITimelineTranscriptionService>();
    _mockProjectAudioClient = new Mock<IProjectAudioClient>();
    _mockAudioVisualizationService = new Mock<IAudioVisualizationService>();
    _mockProjectsClient = new Mock<IProjectsClient>();
    _mockProjectsClient.Setup(x => x.GetProjectsAsync(It.IsAny<CancellationToken>()))
        .ReturnsAsync(new List<Project>());
    _mockProfilesClient = new Mock<IProfilesClient>();
    _mockProfilesClient.Setup(x => x.GetProfilesAsync(It.IsAny<CancellationToken>()))
        .ReturnsAsync(new List<VoiceProfile>());
    _mockAudioPlayer = new Mock<IAudioPlayerService>();
    _mockMultiSelectService = new Mock<MultiSelectService>();
    _mockDialogService = new Mock<IDialogService>();
    _mockProjectRepository = new Mock<IProjectRepository>();
    _mockProjectRepository
        .Setup(x => x.SaveLastSubtitleTranscriptionIdAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
        .Returns(Task.CompletedTask);
    _mockProjectRepository
        .Setup(x => x.GetLastSubtitleTranscriptionIdAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
        .ReturnsAsync((string?)null);
    _mockDialogService
        .Setup(x => x.ShowConfirmationAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
        .ReturnsAsync(true);

    _sut = new TimelineViewModel(
        _mockSynthesisService.Object,
        _mockClipService.Object,
        _mockTrackService.Object,
        _mockTranscriptionService.Object,
        _mockProjectAudioClient.Object,
        _mockAudioVisualizationService.Object,
        _mockProjectsClient.Object,
        _mockProfilesClient.Object,
        _mockAudioPlayer.Object,
        _mockMultiSelectService.Object,
        _mockDialogService.Object,
        projectRepository: _mockProjectRepository.Object);
  }

  [TestCleanup]
  public void Cleanup()
  {
    _ = _sut?.OnDeactivatedAsync(CancellationToken.None);
  }

  private static async Task PumpNavigateAsync(int iterations = 12)
  {
    var dq = AppServices.GetViewModelContext().Dispatcher;
    for (var i = 0; i < iterations; i++)
    {
      var tcs = new TaskCompletionSource<bool>();
      dq.TryEnqueue(() => tcs.TrySetResult(true));
      await tcs.Task.ConfigureAwait(false);
      await Task.Delay(35).ConfigureAwait(false);
    }
  }

  [TestMethod]
  public async Task LoadTranscriptSegmentsAsync_SetsLoadedSubtitleTranscriptionId()
  {
    const string tid = "tx-backend-1";
    _mockTranscriptionService
        .Setup(x => x.GetTranscriptionAsync(tid, It.IsAny<CancellationToken>()))
        .ReturnsAsync(
            new TranscriptionResponse
            {
              Id = tid,
              AudioId = "a1",
              Segments = new List<TranscriptionSegment>
              {
                new() { Id = "s1", Text = "alpha", Start = 0, End = 1 },
              },
            });

    await _sut.LoadTranscriptSegmentsAsync(tid);

    Assert.AreEqual(tid, _sut.LoadedSubtitleTranscriptionId);
    Assert.AreEqual(1, _sut.TranscriptSegments.Count);
    Assert.AreEqual("alpha", _sut.TranscriptSegments[0].Text);
  }

  [TestMethod]
  public async Task LoadTranscriptSegmentsAsync_SecondFetchQuiet_UpdatesSegmentTextFromBackend()
  {
    const string tid = "tx-backend-1";
    var first = new TranscriptionResponse
    {
      Id = tid,
      AudioId = "a1",
      Segments = new List<TranscriptionSegment>
      {
        new() { Id = "s1", Text = "before", Start = 0, End = 1 },
      },
    };
    var second = new TranscriptionResponse
    {
      Id = tid,
      AudioId = "a1",
      Segments = new List<TranscriptionSegment>
      {
        new() { Id = "s1", Text = "after", Start = 0, End = 1 },
      },
    };
    var call = 0;
    _mockTranscriptionService
        .Setup(x => x.GetTranscriptionAsync(tid, It.IsAny<CancellationToken>()))
        .ReturnsAsync(() => call++ == 0 ? first : second);

    await _sut.LoadTranscriptSegmentsAsync(tid);
    Assert.AreEqual("before", _sut.TranscriptSegments[0].Text);

    await _sut.LoadTranscriptSegmentsAsync(tid, default, quietNotifications: true);

    Assert.AreEqual("after", _sut.TranscriptSegments[0].Text);
    Assert.AreEqual(tid, _sut.LoadedSubtitleTranscriptionId);
  }

  [TestMethod]
  public async Task LoadTranscriptSegmentsAsync_Success_WritesLastSubtitleIdToRepository()
  {
    const string projectId = "proj-save-1";
    const string tid = "tx-save-1";
    var project = new Project { Id = projectId, Name = "Project Save" };
    _sut.Projects.Add(project);
    _mockTrackService
        .Setup(x => x.GetTracksAsync(projectId, It.IsAny<CancellationToken>()))
        .ReturnsAsync(new List<AudioTrack>());
    _sut.SelectedProject = project;

    _mockTranscriptionService
        .Setup(x => x.GetTranscriptionAsync(tid, It.IsAny<CancellationToken>()))
        .ReturnsAsync(
            new TranscriptionResponse
            {
              Id = tid,
              AudioId = "a-save",
              Segments = new List<TranscriptionSegment>
              {
                new() { Id = "seg-save-1", Text = "persist me", Start = 0, End = 1 },
              },
            });

    await _sut.LoadTranscriptSegmentsAsync(tid);

    _mockProjectRepository.Verify(
        x => x.SaveLastSubtitleTranscriptionIdAsync(projectId, tid, It.IsAny<CancellationToken>()),
        Times.Once);
  }

  [TestMethod]
  public async Task LoadTranscriptSegmentsAsync_NullProject_DoesNotWriteToRepository()
  {
    const string tid = "tx-no-project";
    _mockTranscriptionService
        .Setup(x => x.GetTranscriptionAsync(tid, It.IsAny<CancellationToken>()))
        .ReturnsAsync(
            new TranscriptionResponse
            {
              Id = tid,
              AudioId = "a-none",
              Segments = new List<TranscriptionSegment>
              {
                new() { Id = "seg-none-1", Text = "no project", Start = 0, End = 1 },
              },
            });

    await _sut.LoadTranscriptSegmentsAsync(tid);

    _mockProjectRepository.Verify(
        x => x.SaveLastSubtitleTranscriptionIdAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()),
        Times.Never);
  }

  [TestMethod]
  public async Task SelectedProjectChanged_DifferentProject_ClearsSubtitleOverlay()
  {
    var pa = new Project { Id = "proj-a", Name = "A" };
    var pb = new Project { Id = "proj-b", Name = "B" };
    _sut.Projects.Add(pa);
    _sut.Projects.Add(pb);
    var trackA = new AudioTrack { Id = "ta", Name = "T", ProjectId = "proj-a", Clips = new List<AudioClip>() };
    var trackB = new AudioTrack { Id = "tb", Name = "T", ProjectId = "proj-b", Clips = new List<AudioClip>() };
    _mockTrackService
        .Setup(x => x.GetTracksAsync("proj-a", It.IsAny<CancellationToken>()))
        .ReturnsAsync(new List<AudioTrack> { trackA });
    _mockTrackService
        .Setup(x => x.GetTracksAsync("proj-b", It.IsAny<CancellationToken>()))
        .ReturnsAsync(new List<AudioTrack> { trackB });

    _sut.SelectedProject = pa;
    const string tid = "tx-backend-1";
    _mockTranscriptionService
        .Setup(x => x.GetTranscriptionAsync(tid, It.IsAny<CancellationToken>()))
        .ReturnsAsync(
            new TranscriptionResponse
            {
              Id = tid,
              AudioId = "a1",
              Segments = new List<TranscriptionSegment>
              {
                new() { Id = "s1", Text = "hello", Start = 0, End = 1 },
              },
            });

    await _sut.LoadTranscriptSegmentsAsync(tid);

    Assert.AreEqual(tid, _sut.LoadedSubtitleTranscriptionId);
    Assert.AreEqual(1, _sut.TranscriptSegments.Count);

    _sut.SelectedProject = pb;

    Assert.IsNull(_sut.LoadedSubtitleTranscriptionId);
    Assert.AreEqual(0, _sut.TranscriptSegments.Count);
    Assert.IsFalse(_sut.ShowTranscriptTrack);
  }

  [TestMethod]
  public async Task SelectedProjectChanged_NullProject_ClearsSubtitleOverlay()
  {
    var pa = new Project { Id = "proj-a", Name = "A" };
    _sut.Projects.Add(pa);
    var trackA = new AudioTrack { Id = "ta", Name = "T", ProjectId = "proj-a", Clips = new List<AudioClip>() };
    _mockTrackService
        .Setup(x => x.GetTracksAsync("proj-a", It.IsAny<CancellationToken>()))
        .ReturnsAsync(new List<AudioTrack> { trackA });

    _sut.SelectedProject = pa;
    const string tid = "tx-backend-1";
    _mockTranscriptionService
        .Setup(x => x.GetTranscriptionAsync(tid, It.IsAny<CancellationToken>()))
        .ReturnsAsync(
            new TranscriptionResponse
            {
              Id = tid,
              AudioId = "a1",
              Segments = new List<TranscriptionSegment>
              {
                new() { Id = "s1", Text = "hello", Start = 0, End = 1 },
              },
            });

    await _sut.LoadTranscriptSegmentsAsync(tid);
    Assert.AreEqual(tid, _sut.LoadedSubtitleTranscriptionId);

    _sut.SelectedProject = null;

    Assert.IsNull(_sut.LoadedSubtitleTranscriptionId);
    Assert.AreEqual(0, _sut.TranscriptSegments.Count);
  }

  [TestMethod]
  public async Task ClearTranscript_ClearsLoadedSubtitleTranscriptionId()
  {
    const string tid = "tx-backend-1";
    _mockTranscriptionService
        .Setup(x => x.GetTranscriptionAsync(tid, It.IsAny<CancellationToken>()))
        .ReturnsAsync(
            new TranscriptionResponse
            {
              Id = tid,
              AudioId = "a1",
              Segments = new List<TranscriptionSegment>
              {
                new() { Id = "s1", Text = "x", Start = 0, End = 1 },
              },
            });

    await _sut.LoadTranscriptSegmentsAsync(tid);
    _sut.ClearTranscriptCommand.Execute(null);

    Assert.IsNull(_sut.LoadedSubtitleTranscriptionId);
    Assert.AreEqual(0, _sut.TranscriptSegments.Count);
  }

  /// <summary>GAP-047: post-apply coherence quiet-refetch when loaded subtitle id + project match.</summary>
  [TestMethod]
  public async Task PostApply_WhenLoadedSubtitleMatches_QuietRefetchUpdatesTranscriptSegments()
  {
    const string tid = "tx-post-apply-1";
    const string pid = "proj-post-apply-1";
    var proj = new Project { Id = pid, Name = "PostApply" };
    _sut.Projects.Add(proj);
    _mockTrackService
        .Setup(x => x.GetTracksAsync(pid, It.IsAny<CancellationToken>()))
        .ReturnsAsync(new List<AudioTrack> { new() { Id = "trk", Name = "T", ProjectId = pid, Clips = new List<AudioClip>() } });
    _sut.SelectedProject = proj;

    var first = new TranscriptionResponse
    {
      Id = tid,
      AudioId = "a1",
      Segments = new List<TranscriptionSegment>
      {
        new() { Id = "s1", Text = "before", Start = 0, End = 1 },
      },
    };
    var second = new TranscriptionResponse
    {
      Id = tid,
      AudioId = "a1",
      Segments = new List<TranscriptionSegment>
      {
        new() { Id = "s1", Text = "after", Start = 0, End = 1 },
      },
    };
    var call = 0;
    _mockTranscriptionService
        .Setup(x => x.GetTranscriptionAsync(tid, It.IsAny<CancellationToken>()))
        .ReturnsAsync(() => call++ == 0 ? first : second);

    await _sut.LoadTranscriptSegmentsAsync(tid).ConfigureAwait(false);
    Assert.AreEqual("before", _sut.TranscriptSegments[0].Text);

    var agg = AppServices.TryGetEventAggregator();
    Assert.IsNotNull(agg);
    agg!.Publish(
        new NavigateToEvent(
            PanelIds.Transcribe,
            "timeline",
            new Dictionary<string, object>
            {
                { "action", "coherentReloadAfterSegmentApply" },
                { "transcriptionId", tid },
                { "projectId", pid },
            }));

    await PumpNavigateAsync().ConfigureAwait(false);

    Assert.AreEqual("after", _sut.TranscriptSegments[0].Text);
    _mockTranscriptionService.Verify(x => x.GetTranscriptionAsync(tid, It.IsAny<CancellationToken>()), Times.Exactly(2));
  }

  /// <summary>GAP-047: mismatching transcription id must not overwrite overlay (fail-closed).</summary>
  [TestMethod]
  public async Task PostApply_WhenLoadedSubtitleMismatches_DoesNotOverwriteOverlay()
  {
    const string loadedTid = "tx-loaded";
    const string otherTid = "tx-other";
    const string pid = "proj-mismatch-1";
    var proj = new Project { Id = pid, Name = "Mis" };
    _sut.Projects.Add(proj);
    _mockTrackService
        .Setup(x => x.GetTracksAsync(pid, It.IsAny<CancellationToken>()))
        .ReturnsAsync(new List<AudioTrack> { new() { Id = "trk", Name = "T", ProjectId = pid, Clips = new List<AudioClip>() } });
    _sut.SelectedProject = proj;

    _mockTranscriptionService
        .Setup(x => x.GetTranscriptionAsync(loadedTid, It.IsAny<CancellationToken>()))
        .ReturnsAsync(
            new TranscriptionResponse
            {
              Id = loadedTid,
              AudioId = "a1",
              Segments = new List<TranscriptionSegment>
              {
                new() { Id = "s1", Text = "overlay", Start = 0, End = 1 },
              },
            });

    await _sut.LoadTranscriptSegmentsAsync(loadedTid).ConfigureAwait(false);

    var agg = AppServices.TryGetEventAggregator();
    agg!.Publish(
        new NavigateToEvent(
            PanelIds.Transcribe,
            "timeline",
            new Dictionary<string, object>
            {
                { "action", "coherentReloadAfterSegmentApply" },
                { "transcriptionId", otherTid },
                { "projectId", pid },
            }));

    await PumpNavigateAsync().ConfigureAwait(false);

    Assert.AreEqual("overlay", _sut.TranscriptSegments[0].Text);
    _mockTranscriptionService.Verify(x => x.GetTranscriptionAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
  }

  /// <summary>
  /// GAP-047: post-Apply coherence is a no-op when no subtitle overlay is loaded (draft-only / pre-send paths
  /// do not publish this event; handler stays fail-closed).
  /// </summary>
  [TestMethod]
  public async Task DraftCleanup_DoesNotRefreshCrossConsumers()
  {
    const string pid = "proj-no-overlay";
    var proj = new Project { Id = pid, Name = "NoOverlay" };
    _sut.Projects.Add(proj);
    _mockTrackService
        .Setup(x => x.GetTracksAsync(pid, It.IsAny<CancellationToken>()))
        .ReturnsAsync(new List<AudioTrack> { new() { Id = "trk", Name = "T", ProjectId = pid, Clips = new List<AudioClip>() } });
    _sut.SelectedProject = proj;

    var agg = AppServices.TryGetEventAggregator();
    agg!.Publish(
        new NavigateToEvent(
            PanelIds.Transcribe,
            "timeline",
            new Dictionary<string, object>
            {
                { "action", "coherentReloadAfterSegmentApply" },
                { "transcriptionId", "tx-never" },
                { "projectId", pid },
            }));

    await PumpNavigateAsync().ConfigureAwait(false);

    _mockTranscriptionService.Verify(
        x => x.GetTranscriptionAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()),
        Times.Never);
  }

  /// <summary>GAP-047 range parity: one post-apply signal → one quiet refetch; overlay shows merged authoritative segments.</summary>
  [TestMethod]
  public async Task RangeApply_Success_QuietRefetchesTimelineOnce_WhenOwnershipMatches()
  {
    const string tid = "tx-range-parity-1";
    const string pid = "proj-range-parity-1";
    var proj = new Project { Id = pid, Name = "RangeParity" };
    _sut.Projects.Add(proj);
    _mockTrackService
        .Setup(x => x.GetTracksAsync(pid, It.IsAny<CancellationToken>()))
        .ReturnsAsync(new List<AudioTrack> { new() { Id = "trk", Name = "T", ProjectId = pid, Clips = new List<AudioClip>() } });
    _sut.SelectedProject = proj;

    var first = new TranscriptionResponse
    {
      Id = tid,
      AudioId = "a1",
      Segments = new List<TranscriptionSegment>
      {
        new() { Id = "s1", Text = "aa", Start = 0, End = 1 },
        new() { Id = "s2", Text = "bb", Start = 1, End = 2 },
        new() { Id = "s3", Text = "cc", Start = 2, End = 3 },
      },
    };
    var second = new TranscriptionResponse
    {
      Id = tid,
      AudioId = "a1",
      Segments = new List<TranscriptionSegment>
      {
        new() { Id = "s1", Text = "merged range", Start = 0, End = 1 },
        new() { Id = "s2", Text = string.Empty, Start = 1, End = 2 },
        new() { Id = "s3", Text = string.Empty, Start = 2, End = 3 },
      },
    };
    var call = 0;
    _mockTranscriptionService
        .Setup(x => x.GetTranscriptionAsync(tid, It.IsAny<CancellationToken>()))
        .ReturnsAsync(() => call++ == 0 ? first : second);

    await _sut.LoadTranscriptSegmentsAsync(tid).ConfigureAwait(false);
    Assert.AreEqual(3, _sut.TranscriptSegments.Count);
    Assert.AreEqual("aa", _sut.TranscriptSegments[0].Text);

    var agg = AppServices.TryGetEventAggregator();
    Assert.IsNotNull(agg);
    agg!.Publish(
        new NavigateToEvent(
            PanelIds.Transcribe,
            "timeline",
            new Dictionary<string, object>
            {
                { "action", "coherentReloadAfterSegmentApply" },
                { "transcriptionId", tid },
                { "projectId", pid },
            }));

    await PumpNavigateAsync().ConfigureAwait(false);

    Assert.AreEqual("merged range", _sut.TranscriptSegments[0].Text);
    Assert.AreEqual(string.Empty, _sut.TranscriptSegments[1].Text);
    _mockTranscriptionService.Verify(x => x.GetTranscriptionAsync(tid, It.IsAny<CancellationToken>()), Times.Exactly(2));
  }

  /// <summary>GAP-047 range parity: mismatching apply transcription id must not refetch (same fail-closed as single-segment).</summary>
  [TestMethod]
  public async Task RangeApply_Success_NoOpOnTimeline_WhenOwnershipMismatches()
  {
    const string loadedTid = "tx-range-loaded";
    const string otherTid = "tx-range-other";
    const string pid = "proj-range-mismatch";
    var proj = new Project { Id = pid, Name = "RangeMis" };
    _sut.Projects.Add(proj);
    _mockTrackService
        .Setup(x => x.GetTracksAsync(pid, It.IsAny<CancellationToken>()))
        .ReturnsAsync(new List<AudioTrack> { new() { Id = "trk", Name = "T", ProjectId = pid, Clips = new List<AudioClip>() } });
    _sut.SelectedProject = proj;

    _mockTranscriptionService
        .Setup(x => x.GetTranscriptionAsync(loadedTid, It.IsAny<CancellationToken>()))
        .ReturnsAsync(
            new TranscriptionResponse
            {
              Id = loadedTid,
              AudioId = "a1",
              Segments = new List<TranscriptionSegment>
              {
                new() { Id = "s1", Text = "keep", Start = 0, End = 1 },
                new() { Id = "s2", Text = "overlay", Start = 1, End = 2 },
              },
            });

    await _sut.LoadTranscriptSegmentsAsync(loadedTid).ConfigureAwait(false);

    var agg = AppServices.TryGetEventAggregator();
    agg!.Publish(
        new NavigateToEvent(
            PanelIds.Transcribe,
            "timeline",
            new Dictionary<string, object>
            {
                { "action", "coherentReloadAfterSegmentApply" },
                { "transcriptionId", otherTid },
                { "projectId", pid },
            }));

    await PumpNavigateAsync().ConfigureAwait(false);

    Assert.AreEqual("keep", _sut.TranscriptSegments[0].Text);
    _mockTranscriptionService.Verify(x => x.GetTranscriptionAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
  }

  /// <summary>GAP-047 undo coherence: simulated post-undo reload updates overlay when loaded subtitle + project match.</summary>
  [TestMethod]
  public async Task UndoAfterFillerCleanup_UpdatesTimelineOverlay_WhenOwnershipMatches()
  {
    const string tid = "tx-undo-coherence-1";
    const string pid = "proj-undo-coherence-1";
    var proj = new Project { Id = pid, Name = "UndoCoherence" };
    _sut.Projects.Add(proj);
    _mockTrackService
        .Setup(x => x.GetTracksAsync(pid, It.IsAny<CancellationToken>()))
        .ReturnsAsync(new List<AudioTrack> { new() { Id = "trk", Name = "T", ProjectId = pid, Clips = new List<AudioClip>() } });
    _sut.SelectedProject = proj;

    var before = new TranscriptionResponse
    {
      Id = tid,
      AudioId = "a1",
      Segments = new List<TranscriptionSegment>
      {
        new() { Id = "s1", Text = "canonical before undo", Start = 0, End = 1 },
      },
    };
    var afterApply = new TranscriptionResponse
    {
      Id = tid,
      AudioId = "a1",
      Segments = new List<TranscriptionSegment>
      {
        new() { Id = "s1", Text = "after apply cleaned", Start = 0, End = 1 },
      },
    };
    var call = 0;
    _mockTranscriptionService
        .Setup(x => x.GetTranscriptionAsync(tid, It.IsAny<CancellationToken>()))
        .ReturnsAsync(() => call++ switch
        {
          0 => before,
          1 => afterApply,
          _ => before,
        });

    await _sut.LoadTranscriptSegmentsAsync(tid).ConfigureAwait(false);
    Assert.AreEqual("canonical before undo", _sut.TranscriptSegments[0].Text);

    var agg = AppServices.TryGetEventAggregator();
    Assert.IsNotNull(agg);
    agg!.Publish(
        new NavigateToEvent(
            PanelIds.Transcribe,
            "timeline",
            new Dictionary<string, object>
            {
                { "action", "coherentReloadAfterSegmentApply" },
                { "transcriptionId", tid },
                { "projectId", pid },
            }));

    await PumpNavigateAsync().ConfigureAwait(false);
    Assert.AreEqual("after apply cleaned", _sut.TranscriptSegments[0].Text);

    agg.Publish(
        new NavigateToEvent(
            PanelIds.Transcribe,
            "timeline",
            new Dictionary<string, object>
            {
                { "action", "coherentReloadAfterSegmentApply" },
                { "transcriptionId", tid },
                { "projectId", pid },
            }));

    await PumpNavigateAsync().ConfigureAwait(false);
    Assert.AreEqual("canonical before undo", _sut.TranscriptSegments[0].Text);
    _mockTranscriptionService.Verify(x => x.GetTranscriptionAsync(tid, It.IsAny<CancellationToken>()), Times.Exactly(3));
  }

  /// <summary>GAP-047 undo coherence: each reload signal performs exactly one quiet refetch (no duplicate suppression bug).</summary>
  [TestMethod]
  public async Task UndoAfterFillerCleanup_DoesNotDuplicateCoherenceReload()
  {
    const string tid = "tx-undo-dedupe-1";
    const string pid = "proj-undo-dedupe-1";
    var proj = new Project { Id = pid, Name = "UndoDedupe" };
    _sut.Projects.Add(proj);
    _mockTrackService
        .Setup(x => x.GetTracksAsync(pid, It.IsAny<CancellationToken>()))
        .ReturnsAsync(new List<AudioTrack> { new() { Id = "trk", Name = "T", ProjectId = pid, Clips = new List<AudioClip>() } });
    _sut.SelectedProject = proj;

    var first = new TranscriptionResponse
    {
      Id = tid,
      AudioId = "a1",
      Segments = new List<TranscriptionSegment>
      {
        new() { Id = "s1", Text = "v1", Start = 0, End = 1 },
      },
    };
    var second = new TranscriptionResponse
    {
      Id = tid,
      AudioId = "a1",
      Segments = new List<TranscriptionSegment>
      {
        new() { Id = "s1", Text = "v2", Start = 0, End = 1 },
      },
    };
    var call = 0;
    _mockTranscriptionService
        .Setup(x => x.GetTranscriptionAsync(tid, It.IsAny<CancellationToken>()))
        .ReturnsAsync(() => call++ == 0 ? first : second);

    await _sut.LoadTranscriptSegmentsAsync(tid).ConfigureAwait(false);
    var agg = AppServices.TryGetEventAggregator();
    Assert.IsNotNull(agg);

    void PublishCoherence() =>
        agg!.Publish(
            new NavigateToEvent(
                PanelIds.Transcribe,
                "timeline",
                new Dictionary<string, object>
                {
                    { "action", "coherentReloadAfterSegmentApply" },
                    { "transcriptionId", tid },
                    { "projectId", pid },
                }));

    PublishCoherence();
    await PumpNavigateAsync().ConfigureAwait(false);
    PublishCoherence();
    await PumpNavigateAsync().ConfigureAwait(false);

    Assert.AreEqual("v2", _sut.TranscriptSegments[0].Text);
    _mockTranscriptionService.Verify(x => x.GetTranscriptionAsync(tid, It.IsAny<CancellationToken>()), Times.Exactly(3));
  }
}
