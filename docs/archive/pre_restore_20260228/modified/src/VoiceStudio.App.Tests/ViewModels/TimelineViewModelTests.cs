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
    private Mock<IBackendClient> _mockBackendClient = null!;
    private Mock<IAudioPlayerService> _mockAudioPlayer = null!;
    private Mock<MultiSelectService> _mockMultiSelectService = null!;
    private TimelineViewModel _sut = null!;

    [TestInitialize]
    public void Setup()
    {
      _mockBackendClient = new Mock<IBackendClient>();
      _mockAudioPlayer = new Mock<IAudioPlayerService>();
      _mockMultiSelectService = new Mock<MultiSelectService>();

      // Setup default mock behavior
      _mockBackendClient
          .Setup(x => x.GetProjectsAsync(It.IsAny<CancellationToken>()))
          .ReturnsAsync(new List<Project>());

      _mockBackendClient
          .Setup(x => x.GetProfilesAsync(It.IsAny<CancellationToken>()))
          .ReturnsAsync(new List<VoiceProfile>());

      _sut = new TimelineViewModel(
          _mockBackendClient.Object, 
          _mockAudioPlayer.Object,
          _mockMultiSelectService.Object);
    }

    [TestCleanup]
    public void Cleanup()
    {
      // TimelineViewModel doesn't implement IDisposable
    }

    #region Panel Properties Tests

    [TestMethod]
    public void PanelId_ReturnsTimeline()
    {
      Assert.AreEqual("timeline", _sut.PanelId);
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
    public void Constructor_WithNullBackendClient_ThrowsArgumentNullException()
    {
      _ = new TimelineViewModel(null!, _mockAudioPlayer.Object, _mockMultiSelectService.Object);
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

    #region Phase 2 Tests

    [TestMethod]
    public void IsLoopEnabled_DefaultIsFalse()
    {
      Assert.IsFalse(_sut.IsLoopEnabled);
    }

    [TestMethod]
    public void IsLoopEnabled_CanBeToggled()
    {
      _sut.IsLoopEnabled = true;
      Assert.IsTrue(_sut.IsLoopEnabled);
    }

    [TestMethod]
    public void IsRecording_DefaultIsFalse()
    {
      Assert.IsFalse(_sut.IsRecording);
    }

    [TestMethod]
    public void TimelineZoom_DefaultIsOne()
    {
      Assert.AreEqual(1.0, _sut.TimelineZoom);
    }

    [TestMethod]
    public void TimelineZoom_CanBeChanged()
    {
      _sut.TimelineZoom = 2.5;
      Assert.AreEqual(2.5, _sut.TimelineZoom);
    }

    #endregion
  }
}
