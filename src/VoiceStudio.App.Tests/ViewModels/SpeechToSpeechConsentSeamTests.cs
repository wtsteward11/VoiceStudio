using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using VoiceStudio.App.Services;
using VoiceStudio.App.Views.Panels;
using VoiceStudio.Core.Exceptions;
using VoiceStudio.Core.Models;
using VoiceStudio.Core.Services;

namespace VoiceStudio.App.Tests.ViewModels;

/// <summary>
/// GAP-055: STS consent gate on <see cref="SpeechToSpeechViewModel"/>.
/// </summary>
[TestClass]
[TestCategory("SeamAware")]
public sealed class SpeechToSpeechConsentSeamTests
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
  public void ConvertCommand_CannotExecute_WhenConsentNotAcknowledged()
  {
    _sut.SourceAudioId = "audio-1";
    _sut.SelectedTargetProfile = new VoiceProfile { Id = "p1", Name = "P" };
    _sut.ConsentAcknowledged = false;

    var cmd = (IAsyncRelayCommand)_sut.ConvertCommand;
    Assert.IsFalse(cmd.CanExecute(null));
  }

  [TestMethod]
  public void ConvertCommand_CanExecute_WhenConsentAcknowledged()
  {
    _sut.SourceAudioId = "audio-1";
    _sut.SelectedTargetProfile = new VoiceProfile { Id = "p1", Name = "P" };
    _sut.ConsentAcknowledged = true;

    var cmd = (IAsyncRelayCommand)_sut.ConvertCommand;
    Assert.IsTrue(cmd.CanExecute(null));
  }

  [TestMethod]
  public async Task ConvertAsync_PassesConsentAcknowledgedToService()
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
            It.Is<SpeechToSpeechRequest>(r => r.ConsentAcknowledged),
            It.IsAny<CancellationToken>()),
        Times.Once);
  }

  [TestMethod]
  public async Task ConvertAsync_OnConsentBlockedError_ShowsActionableStatus()
  {
    await _sut.RefreshAsync().ConfigureAwait(false);
    _sut.SourceAudioId = "a";
    _sut.SelectedTargetProfile = _sut.Profiles[0];
    _sut.ConsentAcknowledged = true;

    _mockSpeech
        .Setup(s => s.ConvertSpeechAsync(It.IsAny<SpeechToSpeechRequest>(), It.IsAny<CancellationToken>()))
        .ThrowsAsync(new BackendException(
            "You must acknowledge that you have permission to transform this voice before conversion proceeds.",
            statusCode: 400,
            errorCode: "CONSENT_REQUIRED",
            isRetryable: false));

    var cmd = (IAsyncRelayCommand)_sut.ConvertCommand;
    await cmd.ExecuteAsync(default).ConfigureAwait(false);

    StringAssert.Contains(_sut.ErrorMessage ?? "", "permission");
    StringAssert.Contains(_sut.StatusText, "failed");
  }
}
