using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
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

/// <summary>Generated-audio clip inserted: <see cref="GeneratedAudioClipInsertedEvent"/> reloads tracks for the active project.</summary>
[TestClass]
public sealed class TimelineViewModelGeneratedAudioInsertedTests
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

    _mockTrackService.Setup(t => t.GetTracksAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
        .ReturnsAsync(new List<AudioTrack>
        {
          new()
          {
            Id = "tr-1",
            Name = "T",
            ProjectId = "p-insert",
            Clips = new List<AudioClip>(),
          },
        });

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
  public async Task GeneratedAudioClipInsertedEvent_RefreshesTracksForMatchingProject()
  {
    var project = new Project { Id = "p-insert", Name = "P", Tracks = new List<AudioTrack>() };
    _sut.SelectedProject = project;

    _mockTrackService.Invocations.Clear();

    var bus = AppServices.TryGetEventAggregator();
    Assert.IsNotNull(bus);
    bus!.Publish(
        new GeneratedAudioClipInsertedEvent(PanelIds.VoiceSynthesis, "p-insert", "tr-1", "c-new", "aud-1"));

    await Task.Delay(400).ConfigureAwait(false);

    _mockTrackService.Verify(t => t.GetTracksAsync("p-insert", It.IsAny<CancellationToken>()), Times.AtLeastOnce);
  }

  [TestMethod]
  public async Task GeneratedAudioClipInsertedEvent_IgnoresEventForDifferentProject()
  {
    var project = new Project { Id = "p-insert", Name = "P", Tracks = new List<AudioTrack>() };
    _sut.SelectedProject = project;

    _mockTrackService.Invocations.Clear();

    var bus = AppServices.TryGetEventAggregator();
    bus!.Publish(
        new GeneratedAudioClipInsertedEvent(PanelIds.VoiceSynthesis, "other-project", "tr-1", "c-new", "aud-1"));

    await Task.Delay(200).ConfigureAwait(false);

    _mockTrackService.Verify(t => t.GetTracksAsync("other-project", It.IsAny<CancellationToken>()), Times.Never);
  }

  [TestMethod]
  public async Task GeneratedAudioClipInsertedEvent_IgnoresEventWhenNoProjectSelected()
  {
    _mockTrackService.Invocations.Clear();

    var bus = AppServices.TryGetEventAggregator();
    bus!.Publish(
        new GeneratedAudioClipInsertedEvent(PanelIds.VoiceSynthesis, "p-insert", "tr-1", "c-new", "aud-1"));

    await Task.Delay(200).ConfigureAwait(false);

    _mockTrackService.Verify(t => t.GetTracksAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
  }

  [TestMethod]
  public async Task GeneratedAudioClipInsertedEvent_DoesNotThrow_OnNullOptionalFields()
  {
    var project = new Project { Id = "p-insert", Name = "P", Tracks = new List<AudioTrack>() };
    _sut.SelectedProject = project;

    _mockTrackService.Invocations.Clear();

    var bus = AppServices.TryGetEventAggregator();
    bus!.Publish(
        new GeneratedAudioClipInsertedEvent(
            PanelIds.VoiceSynthesis,
            "p-insert",
            "tr-1",
            "c-new",
            "aud-1",
            audioReference: null,
            profileId: null,
            engine: null));

    await Task.Delay(400).ConfigureAwait(false);

    _mockTrackService.Verify(t => t.GetTracksAsync("p-insert", It.IsAny<CancellationToken>()), Times.AtLeastOnce);
  }

  [TestMethod]
  public void TimelineViewModel_SubscribesToGeneratedAudioClipInsertedEvent()
  {
    var mockAgg = new Mock<IEventAggregator>(MockBehavior.Loose);

    using var vm = new TimelineViewModel(
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
        eventAggregator: mockAgg.Object);

    mockAgg.Verify(x => x.Subscribe(It.IsAny<Action<GeneratedAudioClipInsertedEvent>>()), Times.Once);

    _ = vm.OnDeactivatedAsync(CancellationToken.None);
  }
}
