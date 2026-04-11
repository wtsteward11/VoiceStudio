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
/// GAP-056: STS transformed-output disclosure on <see cref="SpeechToSpeechViewModel"/>.
/// </summary>
[TestClass]
[TestCategory("SeamAware")]
public sealed class SpeechToSpeechDisclosureSeamTests
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
  public async Task ConvertAsync_SetsOutputDisclosureText_OnSuccess()
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
          DisclosureText = "test disclosure",
        });

    var cmd = (IAsyncRelayCommand)_sut.ConvertCommand;
    await cmd.ExecuteAsync(default).ConfigureAwait(false);

    Assert.AreEqual("test disclosure", _sut.OutputDisclosureText);
  }

  [TestMethod]
  public async Task ConvertAsync_SetsOutputIsTransformed_OnSuccess()
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
          DisclosureText = "x",
        });

    var cmd = (IAsyncRelayCommand)_sut.ConvertCommand;
    await cmd.ExecuteAsync(default).ConfigureAwait(false);

    Assert.IsTrue(_sut.OutputIsTransformed);
  }

  [TestMethod]
  public async Task ConvertAsync_ClearsDisclosureText_AtStartOfNewConversion()
  {
    await _sut.RefreshAsync().ConfigureAwait(false);
    _sut.SourceAudioId = "audio-1";
    _sut.SelectedTargetProfile = _sut.Profiles[0];
    _sut.ConsentAcknowledged = true;

    var hang = new TaskCompletionSource<SpeechToSpeechResponse>();
    _mockSpeech
        .Setup(s => s.ConvertSpeechAsync(It.IsAny<SpeechToSpeechRequest>(), It.IsAny<CancellationToken>()))
        .Returns(hang.Task);

    _sut.OutputDisclosureText = "old";

    var cmd = (IAsyncRelayCommand)_sut.ConvertCommand;
    var pending = cmd.ExecuteAsync(default);

    Assert.IsNull(_sut.OutputDisclosureText);

    hang.SetResult(new SpeechToSpeechResponse
    {
      AudioId = "out1",
      AudioUrl = "/api/audio/out1",
      Duration = 1.0,
      EngineUsed = "rvc",
      IsTransformed = true,
      DisclosureText = "new disclosure",
    });

    await pending.ConfigureAwait(false);

    Assert.AreEqual("new disclosure", _sut.OutputDisclosureText);
  }

  [TestMethod]
  public void ConvertAsync_HasOutputDisclosure_TrueWhenDisclosureTextSet()
  {
    _sut.OutputDisclosureText = "x";
    Assert.IsTrue(_sut.HasOutputDisclosure);

    _sut.OutputDisclosureText = null;
    Assert.IsFalse(_sut.HasOutputDisclosure);
  }
}
