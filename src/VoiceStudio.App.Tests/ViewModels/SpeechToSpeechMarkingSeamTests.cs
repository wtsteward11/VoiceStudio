using System;
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
/// GAP-056 slice 2: durable marking badge on <see cref="SpeechToSpeechViewModel"/>.
/// </summary>
[TestClass]
[TestCategory("SeamAware")]
public sealed class SpeechToSpeechMarkingSeamTests
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
        .ReturnsAsync(new System.Collections.Generic.List<VoiceProfile> { new() { Id = "p1", Name = "Profile1" } });

    _sut = new SpeechToSpeechViewModel(
        _mockContext.Object,
        _mockSpeech.Object,
        _mockProfiles.Object);
  }

  [TestMethod]
  public async Task ConvertAsync_SetsOutputMarkingVerified_WhenMarkingReturnsTransformed()
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
          IsTransformed = true,
          DisclosureText = "d",
        });

    _mockSpeech
        .Setup(s => s.GetMarkingAsync("out1", It.IsAny<CancellationToken>()))
        .ReturnsAsync(new StsMarkingStatus
        {
          AudioId = "out1",
          IsTransformed = true,
          TransformationType = "speech_to_speech",
        });

    var cmd = (IAsyncRelayCommand)_sut.ConvertCommand;
    await cmd.ExecuteAsync(default).ConfigureAwait(false);

    Assert.IsTrue(_sut.OutputMarkingVerified);
    Assert.AreEqual("speech_to_speech", _sut.OutputMarkingType);
  }

  [TestMethod]
  public async Task ConvertAsync_LeavesOutputMarkingVerifiedFalse_WhenMarkingReturnsNotTransformed()
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
          Duration = 1.0,
          EngineUsed = "rvc",
          IsTransformed = true,
          DisclosureText = "d",
        });

    _mockSpeech
        .Setup(s => s.GetMarkingAsync("out1", It.IsAny<CancellationToken>()))
        .ReturnsAsync(new StsMarkingStatus { AudioId = "out1", IsTransformed = false });

    var cmd = (IAsyncRelayCommand)_sut.ConvertCommand;
    await cmd.ExecuteAsync(default).ConfigureAwait(false);

    Assert.IsFalse(_sut.OutputMarkingVerified);
  }

  [TestMethod]
  public async Task ConvertAsync_LeavesOutputMarkingVerifiedFalse_WhenMarkingThrows()
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
          Duration = 1.0,
          EngineUsed = "rvc",
          IsTransformed = true,
          DisclosureText = "d",
        });

    _mockSpeech
        .Setup(s => s.GetMarkingAsync("out1", It.IsAny<CancellationToken>()))
        .ThrowsAsync(new InvalidOperationException("network"));

    var cmd = (IAsyncRelayCommand)_sut.ConvertCommand;
    await cmd.ExecuteAsync(default).ConfigureAwait(false);

    Assert.IsFalse(_sut.OutputMarkingVerified);
  }

  [TestMethod]
  public async Task ConvertAsync_ClearsOutputMarkingVerified_AtStartOfNewConversion()
  {
    await _sut.RefreshAsync().ConfigureAwait(false);
    _sut.SourceAudioId = "audio-1";
    _sut.SelectedTargetProfile = _sut.Profiles[0];
    _sut.ConsentAcknowledged = true;

    var hang = new TaskCompletionSource<SpeechToSpeechResponse>();
    _mockSpeech
        .Setup(s => s.ConvertSpeechAsync(It.IsAny<SpeechToSpeechRequest>(), It.IsAny<CancellationToken>()))
        .Returns(hang.Task);

    _sut.OutputMarkingVerified = true;

    _mockSpeech
        .Setup(s => s.GetMarkingAsync("out1", It.IsAny<CancellationToken>()))
        .ReturnsAsync(new StsMarkingStatus { AudioId = "out1", IsTransformed = true, TransformationType = "speech_to_speech" });

    var cmd = (IAsyncRelayCommand)_sut.ConvertCommand;
    var pending = cmd.ExecuteAsync(default);

    Assert.IsFalse(_sut.OutputMarkingVerified);

    hang.SetResult(new SpeechToSpeechResponse
    {
      AudioId = "out1",
      AudioUrl = "/api/audio/out1",
      Duration = 1.0,
      EngineUsed = "rvc",
      IsTransformed = true,
      DisclosureText = "d",
    });

    await pending.ConfigureAwait(false);

    Assert.IsTrue(_sut.OutputMarkingVerified);
  }
}
