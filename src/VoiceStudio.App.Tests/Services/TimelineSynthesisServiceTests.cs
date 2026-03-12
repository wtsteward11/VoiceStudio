using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using VoiceStudio.App.Services;
using VoiceStudio.Core.Models;
using VoiceStudio.Core.Services;

namespace VoiceStudio.App.Tests.Services
{
  /// <summary>
  /// Unit tests for TimelineSynthesisService.
  /// Verifies synthesis delegation, soft save failure, and cancellation.
  /// </summary>
  [TestClass]
  public class TimelineSynthesisServiceTests
  {
    private Mock<IBackendClient> _mockBackend = null!;
    private Mock<IProjectAudioClient> _mockProjectAudio = null!;
    private TimelineSynthesisService _sut = null!;

    [TestInitialize]
    public void Setup()
    {
      _mockBackend = new Mock<IBackendClient>();
      _mockProjectAudio = new Mock<IProjectAudioClient>();
      _sut = new TimelineSynthesisService(_mockBackend.Object, _mockProjectAudio.Object);
    }

    [TestMethod]
    public void Constructor_WithNullBackend_ThrowsArgumentNullException()
    {
      Assert.ThrowsException<ArgumentNullException>(() =>
        new TimelineSynthesisService(null!, _mockProjectAudio.Object));
    }

    [TestMethod]
    public void Constructor_WithNullProjectAudio_ThrowsArgumentNullException()
    {
      Assert.ThrowsException<ArgumentNullException>(() =>
        new TimelineSynthesisService(_mockBackend.Object, null!));
    }

    [TestMethod]
    public async Task SynthesizeAndSaveAsync_HappyPath_ReturnsResultAndSaves()
    {
      var response = new VoiceSynthesisResponse
      {
        AudioId = "audio-1",
        AudioUrl = "http://localhost:8000/audio/audio-1.wav",
        Duration = 2.5,
        QualityScore = 4.2
      };
      _mockBackend
        .Setup(x => x.SynthesizeVoiceAsync(It.IsAny<VoiceSynthesisRequest>(), It.IsAny<CancellationToken>()))
        .ReturnsAsync(response);

      var result = await _sut.SynthesizeAndSaveAsync(
        "xtts",
        "profile-1",
        "Hello world",
        true,
        "proj-1",
        null,
        CancellationToken.None);

      Assert.IsNotNull(result);
      Assert.AreEqual("audio-1", result.AudioId);
      Assert.AreEqual("http://localhost:8000/audio/audio-1.wav", result.AudioUrl);
      Assert.AreEqual(4.2, result.QualityScore);
      Assert.AreEqual(2.5, result.Duration);
      Assert.IsNotNull(result.SavedFilename);
      Assert.IsTrue(result.SavedFilename!.EndsWith(".wav"));

      _mockBackend.Verify(
        x => x.SynthesizeVoiceAsync(
          It.Is<VoiceSynthesisRequest>(r =>
            r.Engine == "xtts" &&
            r.ProfileId == "profile-1" &&
            r.Text == "Hello world" &&
            r.EnhanceQuality),
          It.IsAny<CancellationToken>()),
        Times.Once);

      _mockProjectAudio.Verify(
        x => x.SaveAudioToProjectAsync("proj-1", "audio-1", It.IsAny<string>(), It.IsAny<CancellationToken>()),
        Times.Once);
    }

    [TestMethod]
    public async Task SynthesizeAndSaveAsync_SoftSaveFailure_ReturnsSavedFilenameNull_NoThrow()
    {
      var response = new VoiceSynthesisResponse
      {
        AudioId = "audio-1",
        AudioUrl = "http://localhost:8000/audio/audio-1.wav",
        Duration = 1.0,
        QualityScore = 3.5
      };
      _mockBackend
        .Setup(x => x.SynthesizeVoiceAsync(It.IsAny<VoiceSynthesisRequest>(), It.IsAny<CancellationToken>()))
        .ReturnsAsync(response);
      _mockProjectAudio
        .Setup(x => x.SaveAudioToProjectAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
        .ThrowsAsync(new InvalidOperationException("Save failed"));

      var result = await _sut.SynthesizeAndSaveAsync(
        "xtts",
        "profile-1",
        "Test",
        false,
        "proj-1",
        null,
        CancellationToken.None);

      Assert.IsNotNull(result);
      Assert.AreEqual("audio-1", result.AudioId);
      Assert.IsNull(result.SavedFilename);
    }

    [TestMethod]
    public async Task SynthesizeAndSaveAsync_BackendThrowsOperationCanceled_PropagatesException()
    {
      _mockBackend
        .Setup(x => x.SynthesizeVoiceAsync(It.IsAny<VoiceSynthesisRequest>(), It.IsAny<CancellationToken>()))
        .ThrowsAsync(new OperationCanceledException());

      await Assert.ThrowsExceptionAsync<OperationCanceledException>(async () =>
        await _sut.SynthesizeAndSaveAsync(
          "xtts",
          "profile-1",
          "Test",
          false,
          null,
          null,
          CancellationToken.None));
    }

    [TestMethod]
    public async Task SynthesizeAndSaveAsync_NoProjectId_SkipsSave()
    {
      var response = new VoiceSynthesisResponse
      {
        AudioId = "audio-1",
        AudioUrl = "http://localhost:8000/audio/audio-1.wav",
        Duration = 1.0,
        QualityScore = 4.0
      };
      _mockBackend
        .Setup(x => x.SynthesizeVoiceAsync(It.IsAny<VoiceSynthesisRequest>(), It.IsAny<CancellationToken>()))
        .ReturnsAsync(response);

      var result = await _sut.SynthesizeAndSaveAsync(
        "xtts",
        "profile-1",
        "Test",
        false,
        null,
        null,
        CancellationToken.None);

      Assert.IsNotNull(result);
      Assert.IsNull(result.SavedFilename);
      _mockProjectAudio.Verify(
        x => x.SaveAudioToProjectAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
        Times.Never);
    }

    [TestMethod]
    public async Task SynthesizeAndSaveAsync_ReportsProgress()
    {
      var response = new VoiceSynthesisResponse
      {
        AudioId = "audio-1",
        AudioUrl = "http://localhost:8000/audio/audio-1.wav",
        Duration = 1.0,
        QualityScore = 4.0
      };
      _mockBackend
        .Setup(x => x.SynthesizeVoiceAsync(It.IsAny<VoiceSynthesisRequest>(), It.IsAny<CancellationToken>()))
        .ReturnsAsync(response);

      var reported = new List<int>();
      var progress = new SyncProgress<int>(p => reported.Add(p));

      await _sut.SynthesizeAndSaveAsync(
        "xtts",
        "profile-1",
        "Test",
        false,
        null,
        progress,
        CancellationToken.None);

      Assert.IsTrue(reported.Count >= 2, $"Expected at least 2 progress reports, got {reported.Count}: [{string.Join(", ", reported)}]");
    }

    private sealed class SyncProgress<T> : IProgress<T>
    {
      private readonly Action<T> _handler;
      public SyncProgress(Action<T> handler) => _handler = handler;
      public void Report(T value) => _handler(value);
    }
  }
}
