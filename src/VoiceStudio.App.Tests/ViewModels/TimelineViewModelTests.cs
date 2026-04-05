using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using VoiceStudio.App.Services;
using VoiceStudio.App.Services.UndoableActions;
using VoiceStudio.App.Tests.Fixtures;
using VoiceStudio.App.UseCases;
using VoiceStudio.App.Views.Panels;
using VoiceStudio.Core.Events;
using VoiceStudio.Core.Models;
using VoiceStudio.Core.Panels;
using VoiceStudio.Core.Services;

namespace VoiceStudio.App.Tests.ViewModels
{
  /// <summary>
  /// Unit tests for TimelineViewModel.
  /// Tests cover track/clip operations, playback, synthesis, and zoom controls.
  /// </summary>
  [TestClass]
  public class TimelineViewModelTests
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
    private TimelineViewModel _sut = null!;

    [TestInitialize]
    public void Setup()
    {
      _mockSynthesisService = new Mock<ITimelineSynthesisService>();
      _mockClipService = new Mock<ITimelineClipService>();
      _mockTrackService = new Mock<ITimelineTrackService>();
      _mockTranscriptionService = new Mock<ITimelineTranscriptionService>();
      _mockProjectAudioClient = new Mock<IProjectAudioClient>();
      _mockAudioVisualizationService = new Mock<IAudioVisualizationService>();
      _mockProjectsClient = new Mock<IProjectsClient>();
      _mockProjectsClient
          .Setup(x => x.GetProjectsAsync(It.IsAny<CancellationToken>()))
          .ReturnsAsync(new List<Project>());
      _mockProfilesClient = new Mock<IProfilesClient>();
      _mockProfilesClient
          .Setup(x => x.GetProfilesAsync(It.IsAny<CancellationToken>()))
          .ReturnsAsync(new List<VoiceProfile>());
      _mockAudioPlayer = new Mock<IAudioPlayerService>();
      _mockMultiSelectService = new Mock<MultiSelectService>();
      _mockDialogService = new Mock<IDialogService>();
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
          _mockDialogService.Object);
    }

    [TestCleanup]
    public void Cleanup()
    {
      _ = _sut?.OnDeactivatedAsync(CancellationToken.None);
    }

    /// <summary>
    /// Verifies OnDeactivatedAsync disposes EventAggregator tokens without throwing.
    /// Prevents subscription leak (GAP-W3).
    /// </summary>
    [TestMethod]
    public async Task OnDeactivatedAsync_DisposesTokens_DoesNotThrow()
    {
      await _sut.OnDeactivatedAsync(CancellationToken.None);
      await _sut.OnDeactivatedAsync(CancellationToken.None); // Idempotent
    }

    #region Panel Properties Tests

    [TestMethod]
    public void PanelId_ReturnsTimeline()
    {
      Assert.AreEqual(PanelIds.Timeline, _sut.PanelId);
    }

    [TestMethod]
    public void DisplayName_ReturnsLocalizedName()
    {
      Assert.IsNotNull(_sut.DisplayName);
      Assert.IsTrue(_sut.DisplayName.Length > 0);
    }

    #endregion

    #region Constructor Tests

    [TestMethod]
    public void Constructor_WithValidDependencies_CreatesInstance()
    {
      Assert.IsNotNull(_sut);
      Assert.IsNotNull(_sut.AddTrackCommand);
      Assert.IsNotNull(_sut.SynthesizeCommand);
      Assert.IsNotNull(_sut.PlayAudioCommand);
      Assert.IsNotNull(_sut.StopAudioCommand);
      Assert.IsNotNull(_sut.OpenRecordingFromTimelineCommand);
      Assert.IsNotNull(_sut.ZoomInCommand);
      Assert.IsNotNull(_sut.ZoomOutCommand);
    }

    [TestMethod]
    [ExpectedException(typeof(ArgumentNullException))]
    public void Constructor_WithNullSynthesisService_ThrowsArgumentNullException()
    {
      _ = new TimelineViewModel(null!, _mockClipService!.Object, _mockTrackService!.Object, _mockTranscriptionService!.Object, _mockProjectAudioClient!.Object, _mockAudioVisualizationService!.Object, _mockProjectsClient!.Object, _mockProfilesClient!.Object, _mockAudioPlayer.Object, _mockMultiSelectService.Object, _mockDialogService.Object);
    }

    #endregion

    #region Initial State Tests

    [TestMethod]
    public void Projects_InitiallyEmpty()
    {
      Assert.IsNotNull(_sut.Projects);
      Assert.AreEqual(0, _sut.Projects.Count);
    }

    [TestMethod]
    public void Tracks_InitiallyEmpty()
    {
      Assert.IsNotNull(_sut.Tracks);
      Assert.AreEqual(0, _sut.Tracks.Count);
    }

    [TestMethod]
    public void SelectedProject_InitiallyNull()
    {
      Assert.IsNull(_sut.SelectedProject);
    }

    [TestMethod]
    public void SelectedTrack_InitiallyNull()
    {
      Assert.IsNull(_sut.SelectedTrack);
    }

    [TestMethod]
    public void IsPlaying_InitiallyFalse()
    {
      Assert.IsFalse(_sut.IsPlaying);
    }

    [TestMethod]
    public void TimelineZoom_InitiallyOne()
    {
      Assert.AreEqual(1.0, _sut.TimelineZoom);
    }

    #endregion

    #region Waveform viewport (GAP-038 slice 2)

    [TestMethod]
    public void WaveformViewport_Zoom2_SlicesDisplaySamplesAndMapsPlayback()
    {
      _mockAudioPlayer.Setup(x => x.Duration).Returns(10);
      _sut.WaveformSamples = Enumerable.Range(0, 100).Select(i => (float)i).ToList();
      _sut.TimelineZoom = 2;
      _sut.CurrentPlaybackPosition = 5;
      Assert.AreEqual(50, _sut.WaveformDisplaySamples.Count);
      Assert.AreEqual(25f, _sut.WaveformDisplaySamples[0]);
      Assert.AreEqual(0.5, _sut.WaveformVisualizerPlaybackNormalized, 1e-6);
    }

    [TestMethod]
    public void WaveformViewport_DurationZero_FullPassThroughAndPlaybackHidden()
    {
      _mockAudioPlayer.Setup(x => x.Duration).Returns(0);
      _sut.WaveformSamples = Enumerable.Range(0, 20).Select(i => (float)i).ToList();
      _sut.TimelineZoom = 2;
      _sut.CurrentPlaybackPosition = 3;
      Assert.AreEqual(20, _sut.WaveformDisplaySamples.Count);
      Assert.AreEqual(-1, _sut.WaveformVisualizerPlaybackNormalized);
    }

    #endregion

    #region Engine Settings Tests

    [TestMethod]
    public void SelectedEngine_DefaultIsXtts()
    {
      Assert.AreEqual("xtts", _sut.SelectedEngine);
    }

    [TestMethod]
    public void SynthesisText_InitiallyEmpty()
    {
      Assert.AreEqual(string.Empty, _sut.SynthesisText);
    }

    [TestMethod]
    public void EnhanceQuality_InitiallyFalse()
    {
      Assert.IsFalse(_sut.EnhanceQuality);
    }

    #endregion

    #region Property Change Tests

    [TestMethod]
    public void SynthesisText_WhenChanged_RaisesPropertyChanged()
    {
      var propertyChanged = false;
      _sut.PropertyChanged += (s, e) =>
      {
        if (e.PropertyName == nameof(_sut.SynthesisText))
          propertyChanged = true;
      };

      _sut.SynthesisText = "Hello world";

      Assert.IsTrue(propertyChanged);
      Assert.AreEqual("Hello world", _sut.SynthesisText);
    }

    [TestMethod]
    public void TimelineZoom_WhenChanged_RaisesPropertyChanged()
    {
      var propertyChanged = false;
      _sut.PropertyChanged += (s, e) =>
      {
        if (e.PropertyName == nameof(_sut.TimelineZoom))
          propertyChanged = true;
      };

      _sut.TimelineZoom = 2.0;

      Assert.IsTrue(propertyChanged);
      Assert.AreEqual(2.0, _sut.TimelineZoom);
    }

    #endregion

    #region Playback State Tests

    [TestMethod]
    public void CanPlayAudio_InitiallyFalse()
    {
      Assert.IsFalse(_sut.CanPlayAudio);
    }

    [TestMethod]
    public void CurrentPlaybackPosition_InitiallyZero()
    {
      Assert.AreEqual(0.0, _sut.CurrentPlaybackPosition);
    }

    #endregion

    #region Quality Score Tests

    [TestMethod]
    public void LastQualityScore_InitiallyNull()
    {
      Assert.IsNull(_sut.LastQualityScore);
    }

    [TestMethod]
    public void LastSynthesizedAudioId_InitiallyNull()
    {
      Assert.IsNull(_sut.LastSynthesizedAudioId);
    }

    #endregion

    #region Command Existence Tests

    [TestMethod]
    public void LoadProjectsCommand_IsNotNull()
    {
      Assert.IsNotNull(_sut.LoadProjectsCommand);
    }

    [TestMethod]
    public void CreateProjectCommand_IsNotNull()
    {
      Assert.IsNotNull(_sut.CreateProjectCommand);
    }

    [TestMethod]
    public void DeleteProjectCommand_IsNotNull()
    {
      Assert.IsNotNull(_sut.DeleteProjectCommand);
    }

    [TestMethod]
    public void LoadProfilesCommand_IsNotNull()
    {
      Assert.IsNotNull(_sut.LoadProfilesCommand);
    }

    [TestMethod]
    public void AddClipToTrackCommand_IsNotNull()
    {
      Assert.IsNotNull(_sut.AddClipToTrackCommand);
    }

    [TestMethod]
    public void DeleteSelectedClipsCommand_IsNotNull()
    {
      Assert.IsNotNull(_sut.DeleteSelectedClipsCommand);
    }

    #endregion

    #region Bounded Request Tests

    /// <summary>
    /// Bounded-request proof: Timeline load cycle makes exactly 1 call each to GetProjectsAsync,
    /// GetProfilesAsync, GetTracksAsync, ListProjectAudioAsync, and zero calls to GetWaveformDataAsync during load.
    /// </summary>
    [TestMethod]
    [TestCategory("BoundedRequest")]
    public async Task TimelineLoadCycle_BoundedRequestCounts_ExactlyOnePerStableRead_ZeroWaveformDuringLoad()
    {
      var project = new Project { Id = "proj-1", Name = "Test Project" };
      var track = new AudioTrack { Id = "t1", Name = "Track 1", ProjectId = "proj-1" };

      _mockProjectsClient.Reset();
      _mockProfilesClient.Reset();
      _mockTrackService.Reset();
      _mockProjectAudioClient.Reset();
      _mockAudioVisualizationService.Reset();

      _mockProjectsClient.Setup(x => x.GetProjectsAsync(It.IsAny<CancellationToken>()))
          .ReturnsAsync(new List<Project> { project });
      _mockProfilesClient.Setup(x => x.GetProfilesAsync(It.IsAny<CancellationToken>()))
          .ReturnsAsync(new List<VoiceProfile>());
      _mockTrackService.Setup(x => x.GetTracksAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
          .ReturnsAsync(new List<AudioTrack> { track });
      _mockProjectAudioClient.Setup(x => x.ListProjectAudioAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
          .ReturnsAsync(new List<ProjectAudioFile>());

      await _sut.LoadProjectsCommand.ExecuteAsync(null);
      await _sut.LoadProfilesCommand.ExecuteAsync(null);

      _sut.SelectedProject = project;

      await Task.Delay(200);

      _mockProjectsClient.Verify(x => x.GetProjectsAsync(It.IsAny<CancellationToken>()), Times.Once);
      _mockProfilesClient.Verify(x => x.GetProfilesAsync(It.IsAny<CancellationToken>()), Times.Once);
      _mockTrackService.Verify(x => x.GetTracksAsync("proj-1", It.IsAny<CancellationToken>()), Times.Once);
      _mockProjectAudioClient.Verify(x => x.ListProjectAudioAsync("proj-1", It.IsAny<CancellationToken>()), Times.Once);
      _mockAudioVisualizationService.Verify(
          x => x.GetWaveformDataAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
          Times.Never);
    }

    /// <summary>
    /// Bounded-request proof: One AddClipToTrack execution results in exactly 1 call to CreateClipAsync.
    /// </summary>
    [TestMethod]
    [TestCategory("BoundedRequest")]
    public async Task AddClipToTrack_OneExecution_ExactlyOneCreateClipAsync()
    {
      var project = new Project { Id = "proj-1", Name = "Test Project" };
      var track = new AudioTrack { Id = "t1", Name = "Track 1", ProjectId = "proj-1" };
      var clip = new AudioClip { Id = "c1", Name = "Clip 1", ProfileId = "p1", AudioId = "a1", Duration = TimeSpan.FromSeconds(1), StartTime = 0 };
      var savedFile = new ProjectAudioFile { Filename = "c1.wav", Url = "http://localhost/audio.wav" };

      _mockClipService.Reset();
      _mockProjectAudioClient.Reset();

      _mockClipService.Setup(x => x.CreateClipAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<AudioClip>(), It.IsAny<CancellationToken>()))
          .ReturnsAsync(clip);
      _mockProjectAudioClient.Setup(x => x.SaveAudioToProjectAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
          .ReturnsAsync(savedFile);

      _sut.Projects.Add(project);
      _sut.Tracks.Add(track);
      _sut.SelectedProject = project;
      _sut.SelectedTrack = track;
      _sut.LastSynthesizedAudioId = "a1";
      _sut.LastSynthesizedAudioUrl = "http://localhost/audio.wav";
      _sut.LastSynthesizedDuration = 1.0;
      _sut.SelectedProfileId = "p1";
      _sut.SynthesisText = "Test text";

      await _sut.AddClipToTrackCommand.ExecuteAsync(null);

      await Task.Delay(300);

      _mockClipService.Verify(
          x => x.CreateClipAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<AudioClip>(), It.IsAny<CancellationToken>()),
          Times.Once);
    }

    /// <summary>
    /// Pass 01: CreateClipAsync receives clip with ProfileId when adding via AddClipToTrackCommand.
    /// </summary>
    [TestMethod]
    [TestCategory("WorkflowCoherence")]
    public async Task AddClipToTrack_PassesProfileIdToCreateClipAsync()
    {
      var project = new Project { Id = "proj-1", Name = "Test Project" };
      var track = new AudioTrack { Id = "t1", Name = "Track 1", ProjectId = "proj-1" };
      var returnedClip = new AudioClip { Id = "c1", Name = "Clip 1", ProfileId = "profile-123", AudioId = "a1", Duration = TimeSpan.FromSeconds(1), StartTime = 0 };

      _mockClipService.Setup(x => x.CreateClipAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<AudioClip>(), It.IsAny<CancellationToken>()))
          .ReturnsAsync(returnedClip);

      _sut.Projects.Add(project);
      _sut.Tracks.Add(track);
      _sut.SelectedProject = project;
      _sut.SelectedTrack = track;
      _sut.LastSynthesizedAudioId = "a1";
      _sut.LastSynthesizedAudioUrl = "http://localhost/audio.wav";
      _sut.LastSynthesizedDuration = 1.0;
      _sut.SelectedProfileId = "profile-123";
      _sut.SynthesisText = "Hello";

      await _sut.AddClipToTrackCommand.ExecuteAsync(null);

      await Task.Delay(300);

      _mockClipService.Verify(
          x => x.CreateClipAsync("proj-1", "t1", It.Is<AudioClip>(c => c.ProfileId == "profile-123"), It.IsAny<CancellationToken>()),
          Times.Once);
    }

    #endregion

    #region Pass 06 backup restore coherence

    [TestMethod]
    [TestCategory("SeamAware")]
    public async Task ApplyBackupRestoredAsync_WhenPreviousProjectMissing_ClearsSelectedProject()
    {
      var oldProject = new Project { Id = "proj-old", Name = "Old" };
      _sut.Projects.Add(oldProject);
      _sut.SelectedProject = oldProject;

      _mockProjectsClient.Reset();
      _mockProjectsClient
          .Setup(x => x.GetProjectsAsync(It.IsAny<CancellationToken>()))
          .ReturnsAsync(new List<Project> { new Project { Id = "proj-new", Name = "New" } });

      await _sut.ApplyBackupRestoredAsync(
          new BackupRestoredEvent(PanelIds.BackupRestore, restoreProjects: true, restoreProfiles: false, restoreSettings: false, restoreModels: false),
          CancellationToken.None);

      Assert.IsNull(_sut.SelectedProject);
      _mockProjectsClient.Verify(x => x.GetProjectsAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [TestMethod]
    [TestCategory("SeamAware")]
    public async Task ApplyBackupRestoredAsync_WhenPreviousProjectStillPresent_PreservesSelectionById()
    {
      var oldProject = new Project { Id = "proj-1", Name = "Same" };
      _sut.Projects.Add(oldProject);
      _sut.SelectedProject = oldProject;

      var refreshed = new Project { Id = "proj-1", Name = "Same Updated" };
      _mockProjectsClient.Reset();
      _mockProjectsClient
          .Setup(x => x.GetProjectsAsync(It.IsAny<CancellationToken>()))
          .ReturnsAsync(new List<Project> { refreshed });

      await _sut.ApplyBackupRestoredAsync(
          new BackupRestoredEvent(PanelIds.BackupRestore, true, false, false, false),
          CancellationToken.None);

      Assert.IsNotNull(_sut.SelectedProject);
      Assert.AreEqual("proj-1", _sut.SelectedProject.Id);
      Assert.AreEqual("Same Updated", _sut.SelectedProject.Name);
    }

    [TestMethod]
    [TestCategory("SeamAware")]
    public async Task ApplyBackupRestoredAsync_WhenRestoreProjectsFalse_DoesNotReloadProjects_RefreshesProfilesWhenRequested()
    {
      _mockProjectsClient.Reset();
      _mockProfilesClient.Reset();
      _mockProjectsClient
          .Setup(x => x.GetProjectsAsync(It.IsAny<CancellationToken>()))
          .ReturnsAsync(new List<Project>());
      _mockProfilesClient
          .Setup(x => x.GetProfilesAsync(It.IsAny<CancellationToken>()))
          .ReturnsAsync(new List<VoiceProfile>());

      await _sut.ApplyBackupRestoredAsync(
          new BackupRestoredEvent(PanelIds.BackupRestore, restoreProjects: false, restoreProfiles: true, restoreSettings: false, restoreModels: false),
          CancellationToken.None);

      _mockProjectsClient.Verify(x => x.GetProjectsAsync(It.IsAny<CancellationToken>()), Times.Never);
      _mockProfilesClient.Verify(x => x.GetProfilesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    #endregion

    #region Transport authority Slice 1 (GOV-VOICESTUDIO-TRANSPORT-AUTHORITY-01)

    /// <summary>
    /// Per-track M/S/R and volume: disabled in XAML with tooltips; no VM contract — see Slice 1 proof honesty note.
    /// </summary>
    [TestMethod]
    [TestCategory("SeamAware")]
    public async Task OpenRecordingFromTimelineCommand_PublishesNavigateToEvent_ToRecordingPanel()
    {
      TestAppServicesHelper.EnsureInitialized();
      var dq = TestAppServicesHelper.GetDispatcher();
      Assert.IsNotNull(dq);

      var tcs = new TaskCompletionSource<(NavigateToEvent? Captured, Exception? Error)>(
          TaskCreationOptions.RunContinuationsAsynchronously);

      dq.TryEnqueue(() =>
      {
        try
        {
          NavigateToEvent? captured = null;
          var agg = AppServices.TryGetEventAggregator();
          Assert.IsNotNull(agg, "EventAggregator required for timeline Record navigation proof");

          using (agg.Subscribe<NavigateToEvent>(e => captured = e))
          {
            var mockPlayer = new Mock<IAudioPlayerService>();
            mockPlayer.SetupProperty(a => a.IsLooping, false);
            var vm = new TimelineViewModel(
                _mockSynthesisService.Object,
                _mockClipService.Object,
                _mockTrackService.Object,
                _mockTranscriptionService.Object,
                _mockProjectAudioClient.Object,
                _mockAudioVisualizationService.Object,
                _mockProjectsClient.Object,
                _mockProfilesClient.Object,
                mockPlayer.Object,
                _mockMultiSelectService.Object,
                _mockDialogService.Object);

            vm.OpenRecordingFromTimelineCommand.Execute(null);
            tcs.TrySetResult((captured, null));
            _ = vm.OnDeactivatedAsync(CancellationToken.None);
          }
        }
        catch (Exception ex)
        {
          tcs.TrySetResult((null, ex));
        }
      });

      var (capturedEvt, err) = await tcs.Task.ConfigureAwait(false);
      if (err != null)
        throw err;

      Assert.IsNotNull(capturedEvt, "NavigateToEvent should be published");
      Assert.AreEqual(PanelIds.Recording, capturedEvt.TargetPanelId);
      Assert.AreEqual(PanelIds.Timeline, capturedEvt.SourcePanelId);
    }

    [TestMethod]
    public void IsTimelineLoopEnabled_Constructor_SyncsFromAudioPlayer()
    {
      var mockPlayer = new Mock<IAudioPlayerService>();
      mockPlayer.SetupProperty(a => a.IsLooping, true);

      var sut = new TimelineViewModel(
          _mockSynthesisService.Object,
          _mockClipService.Object,
          _mockTrackService.Object,
          _mockTranscriptionService.Object,
          _mockProjectAudioClient.Object,
          _mockAudioVisualizationService.Object,
          _mockProjectsClient.Object,
          _mockProfilesClient.Object,
          mockPlayer.Object,
          _mockMultiSelectService.Object,
          _mockDialogService.Object);

      Assert.IsTrue(sut.IsTimelineLoopEnabled);
      _ = sut.OnDeactivatedAsync(CancellationToken.None);
    }

    [TestMethod]
    public void IsTimelineLoopEnabled_WhenChanged_PropagatesToAudioPlayer()
    {
      var mockPlayer = new Mock<IAudioPlayerService>();
      mockPlayer.SetupProperty(a => a.IsLooping, false);

      var sut = new TimelineViewModel(
          _mockSynthesisService.Object,
          _mockClipService.Object,
          _mockTrackService.Object,
          _mockTranscriptionService.Object,
          _mockProjectAudioClient.Object,
          _mockAudioVisualizationService.Object,
          _mockProjectsClient.Object,
          _mockProfilesClient.Object,
          mockPlayer.Object,
          _mockMultiSelectService.Object,
          _mockDialogService.Object);

      Assert.IsFalse(mockPlayer.Object.IsLooping);
      sut.IsTimelineLoopEnabled = true;
      Assert.IsTrue(mockPlayer.Object.IsLooping);
      sut.IsTimelineLoopEnabled = false;
      Assert.IsFalse(mockPlayer.Object.IsLooping);
      _ = sut.OnDeactivatedAsync(CancellationToken.None);
    }

    [TestMethod]
    public void TransportTimeDisplay_FormatsCurrentPlaybackPosition_Deterministically()
    {
      _sut.CurrentPlaybackPosition = 61.5;
      Assert.AreEqual("01:01.500", _sut.TransportTimeDisplay);
      Assert.AreEqual(6150.0, _sut.PlayheadPosition, 0.001);
    }

    [TestMethod]
    public void CurrentPlaybackPosition_WhenChanged_RaisesTransportTimeDisplayPropertyChanged()
    {
      var names = new List<string>();
      _sut.PropertyChanged += (_, e) =>
      {
        if (e.PropertyName != null)
          names.Add(e.PropertyName);
      };

      _sut.CurrentPlaybackPosition = 5.0;

      Assert.IsTrue(names.Contains(nameof(_sut.TransportTimeDisplay)));
    }

    /// <summary>
    /// GAP-031: Mute/solo persistence calls project track API and timeline PUT when use case is injected.
    /// </summary>
    [TestMethod]
    public async Task PersistTrackMixStateAsync_CallsTrackServiceAndTimelineUseCase_WhenProjectSelected()
    {
      var mockTimeline = new Mock<ITimelineUseCase>();
      _mockTrackService
          .Setup(x => x.UpdateTrackAsync(
              It.IsAny<string>(),
              It.IsAny<string>(),
              It.IsAny<string?>(),
              It.IsAny<string?>(),
              It.IsAny<bool?>(),
              It.IsAny<bool?>(),
              It.IsAny<CancellationToken>()))
          .ReturnsAsync(new AudioTrack { Id = "tr1" });

      var sut = new TimelineViewModel(
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
          timelineUseCase: mockTimeline.Object);

      sut.SelectedProject = new Project { Id = "proj-x" };
      var track = new AudioTrack { Id = "tr1", IsMuted = true, IsSolo = false };

      await sut.PersistTrackMixStateAsync(track);

      _mockTrackService.Verify(
          x => x.UpdateTrackAsync(
              "proj-x",
              "tr1",
              null,
              null,
              true,
              false,
              It.IsAny<CancellationToken>()),
          Times.Once);
      mockTimeline.Verify(
          t => t.UpdateTimelineTrackAsync("tr1", true, false, It.IsAny<CancellationToken>()),
          Times.Once);

      _ = sut.OnDeactivatedAsync(CancellationToken.None);
    }

    /// <summary>
    /// Transport Authority Slice 2: timeline Play and global toggle must resume when paused, not restart download/play pipeline.
    /// </summary>
    [TestMethod]
    public async Task PlayAudioCommand_WhenPlayerPaused_CallsResumeOnly()
    {
      _mockAudioPlayer.SetupGet(a => a.IsPaused).Returns(true);
      _mockAudioPlayer.SetupGet(a => a.IsPlaying).Returns(false);
      _sut.CanPlayAudio = true;

      await _sut.PlayAudioCommand.ExecuteAsync(null);

      _mockAudioPlayer.Verify(a => a.Resume(), Times.Once);
      _mockAudioPlayer.Verify(
          a => a.PlayFileAsync(It.IsAny<string>(), It.IsAny<Action?>()),
          Times.Never);
    }

    /// <summary>
    /// <see cref="ITimelineTransportController.PlayAsync"/> must mirror Play command resume semantics for orchestrator parity.
    /// </summary>
    [TestMethod]
    public void TimelineTransportController_PlayAsync_WhenPaused_CallsResume()
    {
      var mockPlayer = new Mock<IAudioPlayerService>();
      mockPlayer.SetupProperty(a => a.IsLooping, false);
      mockPlayer.SetupGet(a => a.IsPaused).Returns(true);
      mockPlayer.SetupGet(a => a.IsPlaying).Returns(false);

      var sut = new TimelineViewModel(
          _mockSynthesisService.Object,
          _mockClipService.Object,
          _mockTrackService.Object,
          _mockTranscriptionService.Object,
          _mockProjectAudioClient.Object,
          _mockAudioVisualizationService.Object,
          _mockProjectsClient.Object,
          _mockProfilesClient.Object,
          mockPlayer.Object,
          _mockMultiSelectService.Object,
          _mockDialogService.Object);

      sut.CanPlayAudio = true;

      var controller = (ITimelineTransportController)sut;
      var task = controller.PlayAsync();
      Assert.IsTrue(task.IsCompletedSuccessfully);

      mockPlayer.Verify(a => a.Resume(), Times.Once);
      _ = sut.OnDeactivatedAsync(CancellationToken.None);
    }

    /// <summary>
    /// Transport Authority Slice 3: seek must call <see cref="IAudioPlayerService.Seek"/> and set <see cref="TimelineViewModel.CurrentPlaybackPosition"/> (pixels / (100 * zoom)).
    /// </summary>
    [TestMethod]
    public void SeekToPositionCommand_CallsPlayerSeek_AndSetsCurrentPlaybackPosition()
    {
      _mockAudioPlayer.SetupGet(a => a.Duration).Returns(120.0);
      _sut.TimelineZoom = 1.0;

      _sut.SeekToPositionCommand.Execute(5000.0);

      _mockAudioPlayer.Verify(a => a.Seek(50.0), Times.Once);
      Assert.AreEqual(50.0, _sut.CurrentPlaybackPosition, 0.001);
    }

    /// <summary>
    /// Transport Authority Slice 3: stop resets playback position to zero for deterministic transport/playhead truth.
    /// </summary>
    [TestMethod]
    public void StopAudioCommand_ResetsCurrentPlaybackPosition_AndTransportTimeDisplay()
    {
      _mockAudioPlayer.SetupGet(a => a.IsPlaying).Returns(false);
      _sut.IsPlaying = true;
      _sut.CurrentPlaybackPosition = 42.5;

      _sut.StopAudioCommand.Execute(null);

      Assert.IsFalse(_sut.IsPlaying);
      Assert.AreEqual(0.0, _sut.CurrentPlaybackPosition, 0.001);
      Assert.AreEqual("00:00.000", _sut.TransportTimeDisplay);
    }

    /// <summary>
    /// Transport Authority Slice 3: after stop, playhead chrome hides when not playing, previewing, or player-playing.
    /// </summary>
    [TestMethod]
    public void StopAudioCommand_WhenStopped_IsPlayheadVisible_IsFalse()
    {
      _mockAudioPlayer.SetupGet(a => a.IsPlaying).Returns(false);
      _sut.IsPlaying = true;
      _sut.IsPreviewing = false;

      _sut.StopAudioCommand.Execute(null);

      Assert.IsFalse(_sut.IsPlayheadVisible);
    }

    /// <summary>
    /// Transport Authority Slice 3: preview flag drives playhead visibility and pulsing without corrupting main position source.
    /// </summary>
    [TestMethod]
    public void IsPreviewing_TogglesPlayheadVisibility_AndPlayheadPulsing()
    {
      _mockAudioPlayer.SetupGet(a => a.IsPlaying).Returns(false);
      _sut.IsPlaying = false;

      _sut.IsPreviewing = true;
      Assert.IsTrue(_sut.IsPlayheadVisible);
      Assert.IsTrue(_sut.PlayheadPulsing);

      _sut.IsPreviewing = false;
      Assert.IsFalse(_sut.IsPlayheadVisible);
      Assert.IsFalse(_sut.PlayheadPulsing);
    }

    /// <summary>
    /// Transport Authority Slice 3: <see cref="TimelineViewModel.OnCurrentPlaybackPositionChanged"/> propagates to dependent properties.
    /// </summary>
    [TestMethod]
    public void CurrentPlaybackPosition_WhenChanged_RaisesPlayheadPosition_IsPlayheadVisible_TransportTimeDisplay()
    {
      var names = new List<string>();
      _sut.PropertyChanged += (_, e) =>
      {
        if (e.PropertyName != null)
          names.Add(e.PropertyName);
      };

      _sut.CurrentPlaybackPosition = 3.25;

      Assert.IsTrue(names.Contains(nameof(_sut.PlayheadPosition)));
      Assert.IsTrue(names.Contains(nameof(_sut.IsPlayheadVisible)));
      Assert.IsTrue(names.Contains(nameof(_sut.TransportTimeDisplay)));
    }

    #endregion
  }
}
