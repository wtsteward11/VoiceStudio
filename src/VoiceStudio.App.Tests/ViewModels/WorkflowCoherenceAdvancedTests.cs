using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using VoiceStudio.App.Services;
using VoiceStudio.App.Services.UndoableActions;
using VoiceStudio.App.Tests.Fixtures;
using VoiceStudio.App.Views.Panels;
using VoiceStudio.Core.Events;
using VoiceStudio.Core.Models;
using VoiceStudio.Core.Panels;
using VoiceStudio.Core.Services;

namespace VoiceStudio.App.Tests.ViewModels
{
  /// <summary>
  /// GOV-VOICESTUDIO-WORKFLOW-COHERENCE-ADVANCED-01 Slice 1 — deterministic proof for
  /// Profile → VoiceSynthesis selection and Synthesis/Timeline event → clip insertion + selection.
  /// </summary>
  [TestClass]
  [TestCategory("SeamAware")]
  public class WorkflowCoherenceAdvancedTests
  {
    /// <summary>SelectedProject triggers async <c>LoadTracksForProject</c>, which replaces <c>Tracks</c>; tests must mock <c>GetTracksAsync</c> and wait for this window.</summary>
    private const int HandoffLoadTracksDelayMs = 500;

    [TestMethod]
    public async Task ProfileSelectedEvent_UpdatesVoiceSynthesisSelectedProfile_WhenProfileInList()
    {
      TestAppServicesHelper.EnsureInitialized();
      var dq = TestAppServicesHelper.GetDispatcher();
      Assert.IsNotNull(dq);

      var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
      dq.TryEnqueue(async () =>
      {
        try
        {
          var mockVoice = new Mock<IVoiceSynthesisService>();
          var mockEngines = new Mock<IEnginesClient>();
          mockEngines.Setup(x => x.GetEnginesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new List<string>());
          var mockQp = new Mock<IQualityPipelineService>();
          mockQp.Setup(x => x.ListQualityPipelinePresetsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string>());
          var mockEns = new Mock<IEnsembleService>();
          var mockTxt = new Mock<ITextAnalysisService>();
          var mockQh = new Mock<IQualityHistoryService>();
          var mockProfilesClient = new Mock<IProfilesClient>();
          mockProfilesClient.Setup(x => x.GetProfilesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<VoiceProfile>
            {
              new() { Id = "prof-a", Name = "Alpha" },
              new() { Id = "prof-b", Name = "Beta" }
            });
          var mockAudio = new Mock<IAudioPlayerService>();

          var sut = new VoiceSynthesisViewModel(
            mockVoice.Object,
            mockEngines.Object,
            mockQp.Object,
            mockEns.Object,
            mockTxt.Object,
            mockQh.Object,
            mockProfilesClient.Object,
            mockAudio.Object);

          await sut.LoadProfilesCommand.ExecuteAsync(null);
          await sut.OnActivatedAsync(CancellationToken.None);

          var agg = AppServices.TryGetEventAggregator();
          Assert.IsNotNull(agg);
          agg.Publish(new ProfileSelectedEvent(PanelIds.Profiles, "prof-b", "Beta"));

          await Task.Delay(400);
          Assert.AreEqual("prof-b", sut.SelectedProfile?.Id, "Synthesis target should follow ProfileSelectedEvent");

          await sut.OnDeactivatedAsync(CancellationToken.None);
          sut.Dispose();
          tcs.TrySetResult(true);
        }
        catch (Exception ex)
        {
          tcs.TrySetException(ex);
        }
      });

      await tcs.Task;
    }

    [TestMethod]
    public async Task AddToTimelineEvent_AddsClipWithProfileId_AndSelectsClip()
    {
      TestAppServicesHelper.EnsureInitialized();
      var dq = TestAppServicesHelper.GetDispatcher();
      Assert.IsNotNull(dq);

      var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
      dq.TryEnqueue(async () =>
      {
        try
        {
          var mockSynthesis = new Mock<ITimelineSynthesisService>();
          var mockClip = new Mock<ITimelineClipService>();
          mockClip.Setup(x => x.CreateClipAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<AudioClip>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((string _, string _, AudioClip c, CancellationToken _) => c);
          var mockTrack = new Mock<ITimelineTrackService>();
          var project = new Project { Id = "proj-1", Name = "P1" };
          var track = new AudioTrack { Id = "track-1", Name = "T1", Clips = new List<AudioClip>() };
          mockTrack.Setup(x => x.GetTracksAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<AudioTrack> { track });
          var mockTr = new Mock<ITimelineTranscriptionService>();
          var mockProjAudio = new Mock<IProjectAudioClient>();
          var mockViz = new Mock<IAudioVisualizationService>();
          var mockProjects = new Mock<IProjectsClient>();
          mockProjects.Setup(x => x.GetProjectsAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new List<Project>());
          var mockProfiles = new Mock<IProfilesClient>();
          mockProfiles.Setup(x => x.GetProfilesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new List<VoiceProfile>());
          var mockAudioPlayer = new Mock<IAudioPlayerService>();
          var mockDialog = new Mock<IDialogService>();
          mockDialog.Setup(x => x.ShowConfirmationAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(true);

          var multi = AppServices.GetRequiredService<MultiSelectService>();

          var sut = new TimelineViewModel(
            mockSynthesis.Object,
            mockClip.Object,
            mockTrack.Object,
            mockTr.Object,
            mockProjAudio.Object,
            mockViz.Object,
            mockProjects.Object,
            mockProfiles.Object,
            mockAudioPlayer.Object,
            multi,
            mockDialog.Object);

          sut.SelectedProject = project;
          await Task.Delay(HandoffLoadTracksDelayMs);
          Assert.AreEqual(1, sut.Tracks.Count, "LoadTracksForProject should populate Tracks from mocked backend");

          var agg = AppServices.TryGetEventAggregator();
          Assert.IsNotNull(agg);
          agg.Publish(new AddToTimelineEvent(
            PanelIds.VoiceSynthesis,
            audioId: "audio-xyz",
            audioPath: "http://localhost:8000/api/audio/file/audio-xyz",
            duration: TimeSpan.FromSeconds(2.5),
            clipName: "Synthesis - Test",
            targetTrackIndex: null,
            insertPosition: null,
            profileId: "prof-lineage"));

          await Task.Delay(200);

          Assert.AreEqual(1, track.Clips.Count, "One clip should be added");
          var clip = track.Clips[0];
          Assert.AreEqual("audio-xyz", clip.AudioId);
          Assert.AreEqual("prof-lineage", clip.ProfileId);
          Assert.IsTrue(sut.IsClipSelected(clip.Id), "New clip should be selected for discoverability");

          await sut.OnDeactivatedAsync(CancellationToken.None);
          tcs.TrySetResult(true);
        }
        catch (Exception ex)
        {
          tcs.TrySetException(ex);
        }
      });

      await tcs.Task;
    }

    [TestMethod]
    public async Task SynthesisCompletedEvent_DoesNotInsertClip_Gap025ExplicitHandoffOnly()
    {
      TestAppServicesHelper.EnsureInitialized();
      var dq = TestAppServicesHelper.GetDispatcher();
      Assert.IsNotNull(dq);

      var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
      dq.TryEnqueue(async () =>
      {
        try
        {
          var mockSynthesis = new Mock<ITimelineSynthesisService>();
          var mockClip = new Mock<ITimelineClipService>();
          mockClip.Setup(x => x.CreateClipAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<AudioClip>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((string _, string _, AudioClip c, CancellationToken _) => c);
          var mockTrack = new Mock<ITimelineTrackService>();
          var track = new AudioTrack { Id = "track-2", Name = "T2", Clips = new List<AudioClip>() };
          mockTrack.Setup(x => x.GetTracksAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<AudioTrack> { track });
          var mockTr = new Mock<ITimelineTranscriptionService>();
          var mockProjAudio = new Mock<IProjectAudioClient>();
          var mockViz = new Mock<IAudioVisualizationService>();
          var mockProjects = new Mock<IProjectsClient>();
          mockProjects.Setup(x => x.GetProjectsAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new List<Project>());
          var mockProfiles = new Mock<IProfilesClient>();
          mockProfiles.Setup(x => x.GetProfilesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new List<VoiceProfile>());
          var mockAudioPlayer = new Mock<IAudioPlayerService>();
          var mockDialog = new Mock<IDialogService>();
          mockDialog.Setup(x => x.ShowConfirmationAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(true);

          var multi = AppServices.GetRequiredService<MultiSelectService>();

          var sut = new TimelineViewModel(
            mockSynthesis.Object,
            mockClip.Object,
            mockTrack.Object,
            mockTr.Object,
            mockProjAudio.Object,
            mockViz.Object,
            mockProjects.Object,
            mockProfiles.Object,
            mockAudioPlayer.Object,
            multi,
            mockDialog.Object);

          var project = new Project { Id = "proj-2", Name = "P2" };
          sut.SelectedProject = project;
          await Task.Delay(HandoffLoadTracksDelayMs);

          var agg = AppServices.TryGetEventAggregator();
          Assert.IsNotNull(agg);
          agg.Publish(new SynthesisCompletedEvent(
            PanelIds.VoiceSynthesis,
            audioId: "auto-1",
            audioPath: "http://localhost:8000/api/audio/file/auto-1",
            duration: TimeSpan.FromSeconds(1.2),
            text: "hi",
            voiceName: "VN",
            engineName: "xtts",
            profileId: "prof-auto"));

          await Task.Delay(200);

          Assert.AreEqual(0, track.Clips.Count, "GAP-025: synthesis completion must not add timeline clips without explicit AddToTimeline");

          await sut.OnDeactivatedAsync(CancellationToken.None);
          tcs.TrySetResult(true);
        }
        catch (Exception ex)
        {
          tcs.TrySetException(ex);
        }
      });

      await tcs.Task;
    }

    [TestMethod]
    public async Task AddToTimelineEvent_UsesInsertPosition_ForClipStartTime()
    {
      TestAppServicesHelper.EnsureInitialized();
      var dq = TestAppServicesHelper.GetDispatcher();
      Assert.IsNotNull(dq);

      var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
      dq.TryEnqueue(async () =>
      {
        try
        {
          var track = new AudioTrack { Id = "tr-ip", Name = "A", Clips = new List<AudioClip>() };
          var (sut, _) = CreateTimelineSutForHandoffTests(track);
          sut.CurrentPlaybackPosition = 99.0;
          sut.SelectedProject = new Project { Id = "proj-ip", Name = "PIP" };
          await Task.Delay(HandoffLoadTracksDelayMs);

          var agg = AppServices.TryGetEventAggregator();
          Assert.IsNotNull(agg);
          agg.Publish(new AddToTimelineEvent(
            PanelIds.VoiceSynthesis,
            audioId: "a1",
            audioPath: "http://localhost:8000/api/audio/file/a1",
            duration: TimeSpan.FromSeconds(1),
            clipName: "AtInsert",
            targetTrackIndex: null,
            insertPosition: TimeSpan.FromSeconds(12.5),
            profileId: "prof-x"));

          await Task.Delay(200);

          Assert.AreEqual(1, track.Clips.Count);
          Assert.AreEqual(12.5, track.Clips[0].StartTime, 1e-6);

          await sut.OnDeactivatedAsync(CancellationToken.None);
          tcs.TrySetResult(true);
        }
        catch (Exception ex)
        {
          tcs.TrySetException(ex);
        }
      });

      await tcs.Task;
    }

    [TestMethod]
    public async Task AddToTimelineEvent_UsesPlayhead_WhenInsertPositionAbsent()
    {
      TestAppServicesHelper.EnsureInitialized();
      var dq = TestAppServicesHelper.GetDispatcher();
      Assert.IsNotNull(dq);

      var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
      dq.TryEnqueue(async () =>
      {
        try
        {
          var track = new AudioTrack { Id = "tr-ph", Name = "A", Clips = new List<AudioClip>() };
          var (sut, _) = CreateTimelineSutForHandoffTests(track);
          sut.CurrentPlaybackPosition = 4.25;
          sut.SelectedProject = new Project { Id = "proj-ph", Name = "PPH" };
          await Task.Delay(HandoffLoadTracksDelayMs);

          var agg = AppServices.TryGetEventAggregator();
          Assert.IsNotNull(agg);
          agg.Publish(new AddToTimelineEvent(
            PanelIds.VoiceSynthesis,
            audioId: "a2",
            audioPath: "http://localhost:8000/api/audio/file/a2",
            duration: TimeSpan.FromSeconds(1),
            clipName: "AtPlayhead",
            targetTrackIndex: null,
            insertPosition: null,
            profileId: "prof-x"));

          await Task.Delay(200);

          Assert.AreEqual(1, track.Clips.Count);
          Assert.AreEqual(4.25, track.Clips[0].StartTime, 1e-6);

          await sut.OnDeactivatedAsync(CancellationToken.None);
          tcs.TrySetResult(true);
        }
        catch (Exception ex)
        {
          tcs.TrySetException(ex);
        }
      });

      await tcs.Task;
    }

    [TestMethod]
    public async Task AddToTimelineEvent_AppendsAfterLastClip_WhenPlayheadInvalid()
    {
      TestAppServicesHelper.EnsureInitialized();
      var dq = TestAppServicesHelper.GetDispatcher();
      Assert.IsNotNull(dq);

      var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
      dq.TryEnqueue(async () =>
      {
        try
        {
          var track = new AudioTrack { Id = "tr-ap", Name = "A", Clips = new List<AudioClip>() };
          track.Clips.Add(new AudioClip
          {
            Id = "existing",
            Name = "E",
            StartTime = 0,
            Duration = TimeSpan.FromSeconds(5),
            AudioId = "old",
            AudioUrl = "",
            ProfileId = "prof-x"
          });
          var (sut, _) = CreateTimelineSutForHandoffTests(track);
          sut.CurrentPlaybackPosition = -1.0;
          sut.SelectedProject = new Project { Id = "proj-ap", Name = "PAP" };
          await Task.Delay(HandoffLoadTracksDelayMs);

          var agg = AppServices.TryGetEventAggregator();
          Assert.IsNotNull(agg);
          agg.Publish(new AddToTimelineEvent(
            PanelIds.VoiceSynthesis,
            audioId: "a3",
            audioPath: "http://localhost:8000/api/audio/file/a3",
            duration: TimeSpan.FromSeconds(1),
            clipName: "Appended",
            targetTrackIndex: null,
            insertPosition: null,
            profileId: "prof-x"));

          await Task.Delay(200);

          Assert.AreEqual(2, track.Clips.Count);
          Assert.AreEqual(5.0, track.Clips[1].StartTime, 1e-6);

          await sut.OnDeactivatedAsync(CancellationToken.None);
          tcs.TrySetResult(true);
        }
        catch (Exception ex)
        {
          tcs.TrySetException(ex);
        }
      });

      await tcs.Task;
    }

    [TestMethod]
    public async Task AddToTimelineEvent_UsesTargetTrackIndex_OverSelectedTrack()
    {
      TestAppServicesHelper.EnsureInitialized();
      var dq = TestAppServicesHelper.GetDispatcher();
      Assert.IsNotNull(dq);

      var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
      dq.TryEnqueue(async () =>
      {
        try
        {
          var t0 = new AudioTrack { Id = "tr-a", Name = "A", Clips = new List<AudioClip>() };
          var t1 = new AudioTrack { Id = "tr-b", Name = "B", Clips = new List<AudioClip>() };
          var (sut, _) = CreateTimelineSutForHandoffTests(t0, t1);
          sut.SelectedProject = new Project { Id = "proj-tt", Name = "PTT" };
          await Task.Delay(HandoffLoadTracksDelayMs);
          Assert.AreEqual(2, sut.Tracks.Count);
          Assert.AreSame(t0, sut.SelectedTrack, "LoadTracks selects first track by default");

          var agg = AppServices.TryGetEventAggregator();
          Assert.IsNotNull(agg);
          agg.Publish(new AddToTimelineEvent(
            PanelIds.VoiceSynthesis,
            audioId: "a4",
            audioPath: "http://localhost:8000/api/audio/file/a4",
            duration: TimeSpan.FromSeconds(1),
            clipName: "OnTrackB",
            targetTrackIndex: 1,
            insertPosition: null,
            profileId: "prof-x"));

          await Task.Delay(200);

          Assert.AreEqual(0, t0.Clips.Count);
          Assert.AreEqual(1, t1.Clips.Count);
          Assert.AreEqual("a4", t1.Clips[0].AudioId);

          await sut.OnDeactivatedAsync(CancellationToken.None);
          tcs.TrySetResult(true);
        }
        catch (Exception ex)
        {
          tcs.TrySetException(ex);
        }
      });

      await tcs.Task;
    }

    [TestMethod]
    public async Task AddToTimelineEvent_NoProject_FailClosed_NoClipInserted()
    {
      TestAppServicesHelper.EnsureInitialized();
      var dq = TestAppServicesHelper.GetDispatcher();
      Assert.IsNotNull(dq);

      var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
      dq.TryEnqueue(async () =>
      {
        try
        {
          var (sut, preload) = CreateTimelineSutForHandoffTests();
          var track = preload[0];
          sut.SelectedProject = null;
          sut.Tracks.Add(track);
          sut.SelectedTrack = track;

          var agg = AppServices.TryGetEventAggregator();
          Assert.IsNotNull(agg);
          agg.Publish(new AddToTimelineEvent(
            PanelIds.VoiceSynthesis,
            audioId: "a5",
            audioPath: "http://localhost:8000/api/audio/file/a5",
            duration: TimeSpan.FromSeconds(1),
            clipName: "NoProj",
            targetTrackIndex: null,
            insertPosition: null,
            profileId: "prof-x"));

          await Task.Delay(200);

          Assert.AreEqual(0, track.Clips.Count);

          await sut.OnDeactivatedAsync(CancellationToken.None);
          tcs.TrySetResult(true);
        }
        catch (Exception ex)
        {
          tcs.TrySetException(ex);
        }
      });

      await tcs.Task;
    }

    [TestMethod]
    public async Task AddToTimelineEvent_NoProfile_FailClosed_NoClipInserted()
    {
      TestAppServicesHelper.EnsureInitialized();
      var dq = TestAppServicesHelper.GetDispatcher();
      Assert.IsNotNull(dq);

      var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
      dq.TryEnqueue(async () =>
      {
        try
        {
          var track = new AudioTrack { Id = "tr-np", Name = "A", Clips = new List<AudioClip>() };
          var (sut, _) = CreateTimelineSutForHandoffTests(track);
          sut.SelectedProject = new Project { Id = "proj-np", Name = "PNP" };
          await Task.Delay(HandoffLoadTracksDelayMs);

          var agg = AppServices.TryGetEventAggregator();
          Assert.IsNotNull(agg);
          agg.Publish(new AddToTimelineEvent(
            PanelIds.VoiceSynthesis,
            audioId: "a6",
            audioPath: "http://localhost:8000/api/audio/file/a6",
            duration: TimeSpan.FromSeconds(1),
            clipName: "NoProf",
            targetTrackIndex: null,
            insertPosition: null,
            profileId: null));

          await Task.Delay(200);

          Assert.AreEqual(0, track.Clips.Count);

          await sut.OnDeactivatedAsync(CancellationToken.None);
          tcs.TrySetResult(true);
        }
        catch (Exception ex)
        {
          tcs.TrySetException(ex);
        }
      });

      await tcs.Task;
    }

    /// <summary>GAP-027: duplicate <see cref="AddToTimelineEvent"/> at same track/start must not stack clips.</summary>
    [TestMethod]
    public async Task AddToTimelineEvent_DuplicateSameAudioSameStart_DoesNotInsertSecondClip()
    {
      TestAppServicesHelper.EnsureInitialized();
      var dq = TestAppServicesHelper.GetDispatcher();
      Assert.IsNotNull(dq);

      var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
      dq.TryEnqueue(async () =>
      {
        try
        {
          var track = new AudioTrack { Id = "tr-dup", Name = "A", Clips = new List<AudioClip>() };
          var (sut, _) = CreateTimelineSutForHandoffTests(track);
          sut.SelectedProject = new Project { Id = "proj-dup", Name = "PD" };
          await Task.Delay(HandoffLoadTracksDelayMs);

          var agg = AppServices.TryGetEventAggregator();
          Assert.IsNotNull(agg);
          var evt = new AddToTimelineEvent(
            PanelIds.Library,
            audioId: "a-dup",
            audioPath: "http://localhost:8000/api/audio/file/a-dup",
            duration: TimeSpan.FromSeconds(2),
            clipName: "Dup",
            targetTrackIndex: 0,
            insertPosition: TimeSpan.FromSeconds(1),
            profileId: "prof-dup");
          agg.Publish(evt);
          await Task.Delay(200);
          Assert.AreEqual(1, track.Clips.Count, "first handoff should insert one clip");
          agg.Publish(evt);
          await Task.Delay(200);
          Assert.AreEqual(1, track.Clips.Count, "duplicate handoff should not insert");

          await sut.OnDeactivatedAsync(CancellationToken.None);
          tcs.TrySetResult(true);
        }
        catch (Exception ex)
        {
          tcs.TrySetException(ex);
        }
      });

      await tcs.Task;
    }

    private static (TimelineViewModel sut, IReadOnlyList<AudioTrack> backendTracks) CreateTimelineSutForHandoffTests(
      params AudioTrack[] backendTracks)
    {
      var mockSynthesis = new Mock<ITimelineSynthesisService>();
      var mockClip = new Mock<ITimelineClipService>();
      mockClip.Setup(x => x.CreateClipAsync(
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<AudioClip>(),
            It.IsAny<CancellationToken>()))
        .ReturnsAsync((string _, string _, AudioClip c, CancellationToken _) => c);
      var mockTrack = new Mock<ITimelineTrackService>();
      var tracksForApi = backendTracks.Length > 0
          ? backendTracks.ToList()
          : new List<AudioTrack>
            {
              new()
              {
                Id = "tr-default",
                Name = "T1",
                Clips = new List<AudioClip>()
              }
            };
      mockTrack.Setup(x => x.GetTracksAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
        .ReturnsAsync(tracksForApi);
      var mockTr = new Mock<ITimelineTranscriptionService>();
      var mockProjAudio = new Mock<IProjectAudioClient>();
      var mockViz = new Mock<IAudioVisualizationService>();
      var mockProjects = new Mock<IProjectsClient>();
      mockProjects.Setup(x => x.GetProjectsAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new List<Project>());
      var mockProfiles = new Mock<IProfilesClient>();
      mockProfiles.Setup(x => x.GetProfilesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new List<VoiceProfile>());
      var mockAudioPlayer = new Mock<IAudioPlayerService>();
      var mockDialog = new Mock<IDialogService>();
      mockDialog.Setup(x => x.ShowConfirmationAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
        .ReturnsAsync(true);

      var multi = AppServices.GetRequiredService<MultiSelectService>();

      var sut = new TimelineViewModel(
        mockSynthesis.Object,
        mockClip.Object,
        mockTrack.Object,
        mockTr.Object,
        mockProjAudio.Object,
        mockViz.Object,
        mockProjects.Object,
        mockProfiles.Object,
        mockAudioPlayer.Object,
        multi,
        mockDialog.Object);

      return (sut, tracksForApi);
    }
  }
}
