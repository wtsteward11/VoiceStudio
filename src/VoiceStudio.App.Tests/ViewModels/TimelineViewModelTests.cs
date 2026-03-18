using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using VoiceStudio.App.Services;
using VoiceStudio.App.Services.UndoableActions;
using VoiceStudio.App.Views.Panels;
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

      await _sut.AddClipToTrackCommand.ExecuteAsync(null);

      await Task.Delay(300);

      _mockClipService.Verify(
          x => x.CreateClipAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<AudioClip>(), It.IsAny<CancellationToken>()),
          Times.Once);
    }

    #endregion
  }
}
