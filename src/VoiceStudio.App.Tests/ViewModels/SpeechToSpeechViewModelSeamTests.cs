using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using VoiceStudio.App.Services;
using VoiceStudio.App.Views.Panels;
using VoiceStudio.Core.Models;
using VoiceStudio.Core.Services;

namespace VoiceStudio.App.Tests.ViewModels;

/// <summary>
/// GAP-051 seam tests: speech-to-speech path via <see cref="ISpeechToSpeechService.ConvertSpeechAsync"/>.
/// </summary>
[TestClass]
[TestCategory("SeamAware")]
public sealed class SpeechToSpeechViewModelSeamTests
{
  private Mock<IViewModelContext> _mockContext = null!;
  private Mock<ISpeechToSpeechService> _mockSpeech = null!;
  private Mock<IProfilesClient> _mockProfiles = null!;
  private SpeechToSpeechViewModel _sut = null!;

  [TestInitialize]
  public void Setup()
  {
    _mockContext = new Mock<IViewModelContext>();
    _mockSpeech = new Mock<ISpeechToSpeechService>();
    _mockProfiles = new Mock<IProfilesClient>();

    var dispatcher = new Mock<IDispatcher>();
    dispatcher
        .Setup(d => d.TryEnqueue(It.IsAny<System.Action>()))
        .Returns<System.Action>(a =>
        {
          a();
          return true;
        });

    _mockContext.Setup(c => c.Dispatcher).Returns(dispatcher.Object);
    _mockContext.Setup(c => c.Logger).Returns(NullLogger.Instance);

    _mockProfiles
        .Setup(p => p.GetProfilesAsync(It.IsAny<CancellationToken>()))
        .ReturnsAsync(new List<VoiceProfile> { new() { Id = "p1", Name = "Profile1" } });

    _sut = new SpeechToSpeechViewModel(
        _mockContext.Object,
        _mockSpeech.Object,
        _mockProfiles.Object);
  }

  [TestMethod]
  public async Task ConvertAsync_CallsConvertSpeechAsync_WithShapedRequest()
  {
    await _sut.RefreshAsync().ConfigureAwait(false);
    _sut.SourceAudioId = "audio-1";
    _sut.SelectedTargetProfile = _sut.Profiles[0];
    _sut.ConsentAcknowledged = true;

    _mockSpeech
        .Setup(s => s.ConvertSpeechAsync(It.IsAny<SpeechToSpeechRequest>(), It.IsAny<CancellationToken>()))
        .ReturnsAsync(new SpeechToSpeechResponse
        {
          AudioId = "out1",
          AudioUrl = "/api/audio/out1",
          Duration = 2.5,
          EngineUsed = "rvc",
        });

    var cmd = (IAsyncRelayCommand)_sut.ConvertCommand;
    await cmd.ExecuteAsync(default).ConfigureAwait(false);

    _mockSpeech.Verify(
        s => s.ConvertSpeechAsync(
            It.Is<SpeechToSpeechRequest>(r =>
                r.SourceAudioId == "audio-1"
                && r.TargetVoiceProfileId == "p1"),
            It.IsAny<CancellationToken>()),
        Times.Once);
  }

  [TestMethod]
  public async Task ConvertAsync_SetsOutputFields_OnSuccess()
  {
    await _sut.RefreshAsync().ConfigureAwait(false);
    _sut.SourceAudioId = "a";
    _sut.SelectedTargetProfile = _sut.Profiles[0];
    _sut.ConsentAcknowledged = true;

    _mockSpeech
        .Setup(s => s.ConvertSpeechAsync(It.IsAny<SpeechToSpeechRequest>(), It.IsAny<CancellationToken>()))
        .ReturnsAsync(new SpeechToSpeechResponse
        {
          AudioId = "oid",
          AudioUrl = "/api/audio/oid",
          Duration = 1.2,
          EngineUsed = "rvc",
        });

    var cmd = (IAsyncRelayCommand)_sut.ConvertCommand;
    await cmd.ExecuteAsync(default).ConfigureAwait(false);

    Assert.AreEqual("oid", _sut.OutputAudioId);
    Assert.AreEqual("/api/audio/oid", _sut.OutputAudioUrl);
    StringAssert.Contains(_sut.StatusText, "Done");
  }

  [TestMethod]
  public async Task ConvertAsync_SetsErrorMessage_OnFailure()
  {
    await _sut.RefreshAsync().ConfigureAwait(false);
    _sut.SourceAudioId = "a";
    _sut.SelectedTargetProfile = _sut.Profiles[0];
    _sut.ConsentAcknowledged = true;

    _mockSpeech
        .Setup(s => s.ConvertSpeechAsync(It.IsAny<SpeechToSpeechRequest>(), It.IsAny<CancellationToken>()))
        .ThrowsAsync(new System.InvalidOperationException("backend down"));

    var cmd = (IAsyncRelayCommand)_sut.ConvertCommand;
    await cmd.ExecuteAsync(default).ConfigureAwait(false);

    Assert.IsFalse(string.IsNullOrEmpty(_sut.ErrorMessage));
    StringAssert.Contains(_sut.StatusText, "failed");
  }
}
