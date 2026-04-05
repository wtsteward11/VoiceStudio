using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using VoiceStudio.App.Services;
using VoiceStudio.App.Tests.Fixtures;
using VoiceStudio.App.Views.Panels;
using VoiceStudio.Core.Events;
using VoiceStudio.Core.Models;
using VoiceStudio.Core.Panels;
using VoiceStudio.Core.Services;

namespace VoiceStudio.App.Tests.ViewModels;

/// <summary>GAP-046: <see cref="ClipAudioArtifactReplacedEvent"/> hydrates observable tracks when project matches.</summary>
[TestClass]
public sealed class TimelineViewModelGap046EventTests
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

  [TestMethod]
  public void ClipAudioArtifactReplacedEvent_UpdatesMatchingClipOnTracks()
  {
    var project = new Project
    {
      Id = "p-gap",
      Name = "P",
      Tracks = new List<AudioTrack>(),
    };
    var track = new AudioTrack
    {
      Id = "t1",
      Name = "T",
      ProjectId = "p-gap",
      Clips = new List<AudioClip>
      {
        new()
        {
          Id = "c1",
          Name = "C",
          ProfileId = "pr",
          AudioId = "old",
          AudioUrl = "/o",
          Duration = TimeSpan.FromSeconds(1),
          StartTime = 0,
        },
      },
    };
    project.Tracks = new List<AudioTrack> { track };
    _sut.SelectedProject = project;
    _sut.Tracks.Clear();
    _sut.Tracks.Add(
        new AudioTrack
        {
          Id = "t1",
          Name = "T",
          ProjectId = "p-gap",
          Clips = new List<AudioClip>
          {
            new()
            {
              Id = "c1",
              Name = "C",
              ProfileId = "pr",
              AudioId = "old",
              AudioUrl = "/o",
              Duration = TimeSpan.FromSeconds(1),
              StartTime = 0,
            },
          },
        });

    var bus = AppServices.TryGetEventAggregator();
    Assert.IsNotNull(bus);
    bus!.Publish(
        new ClipAudioArtifactReplacedEvent(PanelIds.Transcribe, "p-gap", "t1", "c1", "new-audio", "/n", 5.5));

    var clip = _sut.Tracks.First(t => t.Id == "t1").Clips!.First(c => c.Id == "c1");
    Assert.AreEqual("new-audio", clip.AudioId);
    Assert.AreEqual("/n", clip.AudioUrl);
    Assert.AreEqual(5.5, clip.Duration.TotalSeconds, 0.001);
  }

  [TestMethod]
  public void ClipAudioArtifactReplacedEvent_IgnoresWrongProject()
  {
    var project = new Project { Id = "p-gap", Name = "P", Tracks = new List<AudioTrack>() };
    var track = new AudioTrack
    {
      Id = "t1",
      ProjectId = "p-gap",
      Clips = new List<AudioClip>
      {
        new() { Id = "c1", AudioId = "old", AudioUrl = "/o", Duration = TimeSpan.FromSeconds(1), StartTime = 0 },
      },
    };
    project.Tracks = new List<AudioTrack> { track };
    _sut.SelectedProject = project;
    _sut.Tracks.Clear();
    _sut.Tracks.Add(
        new AudioTrack
        {
          Id = "t1",
          ProjectId = "p-gap",
          Clips = new List<AudioClip>
          {
            new()
            {
              Id = "c1",
              AudioId = "old",
              AudioUrl = "/o",
              Duration = TimeSpan.FromSeconds(1),
              StartTime = 0,
            },
          },
        });

    var bus = AppServices.TryGetEventAggregator();
    Assert.IsNotNull(bus);
    bus!.Publish(
        new ClipAudioArtifactReplacedEvent(PanelIds.Transcribe, "other-project", "t1", "c1", "x", "/x", 9));

    var clip = _sut.Tracks.First(t => t.Id == "t1").Clips!.First(c => c.Id == "c1");
    Assert.AreEqual("old", clip.AudioId);
  }

  [TestMethod]
  public void ClipAudioArtifactReplacedEvent_SnapTracksOntoSelectedProject_UpdatesProjectModel()
  {
    var project = new Project { Id = "p2", Name = "P", Tracks = new List<AudioTrack>() };
    var track = new AudioTrack
    {
      Id = "t1",
      ProjectId = "p2",
      Clips = new List<AudioClip>
      {
        new()
        {
          Id = "c1",
          AudioId = "a0",
          AudioUrl = "/0",
          Duration = TimeSpan.FromSeconds(1),
          StartTime = 0,
        },
      },
    };
    project.Tracks = new List<AudioTrack> { track };
    _sut.SelectedProject = project;
    _sut.Tracks.Clear();
    _sut.Tracks.Add(
        new AudioTrack
        {
          Id = "t1",
          ProjectId = "p2",
          Clips = new List<AudioClip>
          {
            new()
            {
              Id = "c1",
              AudioId = "a0",
              AudioUrl = "/0",
              Duration = TimeSpan.FromSeconds(1),
              StartTime = 0,
            },
          },
        });

    var bus = AppServices.TryGetEventAggregator();
    bus!.Publish(new ClipAudioArtifactReplacedEvent(PanelIds.Timeline, "p2", "t1", "c1", "a1", "/1", 2));

    var persisted = _sut.SelectedProject!.Tracks!.First(tr => tr.Id == "t1").Clips!.First(c => c.Id == "c1");
    Assert.AreEqual("a1", persisted.AudioId);
  }
}
