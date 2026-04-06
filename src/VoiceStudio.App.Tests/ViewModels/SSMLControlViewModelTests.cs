using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using VoiceStudio.App.Tests.Fixtures;
using VoiceStudio.App.Services;
using VoiceStudio.App.ViewModels;
using VoiceStudio.Core.Models;
using VoiceStudio.Core.Services;

namespace VoiceStudio.App.Tests.ViewModels
{
  [TestClass]
  public class SSMLControlViewModelTests
  {
    private Mock<ISSMLClient> _mockSsmlClient = null!;
    private Mock<IAudioPlayerService> _mockAudioPlayer = null!;
    private IViewModelContext _context = null!;
    private SSMLControlViewModel _sut = null!;

    [TestInitialize]
    public void Setup()
    {
      _mockSsmlClient = new Mock<ISSMLClient>();
      _mockAudioPlayer = new Mock<IAudioPlayerService>();
      _context = new MockViewModelContext();
      _sut = new SSMLControlViewModel(_context, _mockSsmlClient.Object, _mockAudioPlayer.Object);
    }

    [TestMethod]
    public async Task PreviewSSMLAsync_OnSuccess_InvokesPlayBackendAudioIdAsync()
    {
      _sut.SsmlContent = "<speak><p>Hello</p></speak>";
      var response = new SSMLPreviewResult
      {
        AudioId = "test-audio-123",
        Duration = 1.5,
        Message = "OK"
      };

      _mockSsmlClient
          .Setup(x => x.PreviewAsync(
              It.IsAny<string>(),
              It.IsAny<string?>(),
              It.IsAny<string?>(),
              It.IsAny<CancellationToken>()))
          .ReturnsAsync(response);

      await _sut.PreviewCommand.ExecuteAsync(null);

      _mockAudioPlayer.Verify(
          x => x.PlayBackendAudioIdAsync(
              "test-audio-123",
              It.Is<string>(s => s.Contains("localhost") || s.Contains("http")),
              It.IsAny<Action>()),
          Times.Once);
    }

    [TestMethod]
    public async Task PreviewSSMLAsync_StrippedWarned_StillInvokesPlayAndPassesSsmlHandling()
    {
      _sut.SsmlContent = "<speak><p>Hello</p></speak>";
      var response = new SSMLPreviewResult
      {
        AudioId = "audio-ssml",
        Duration = 1.0,
        Message = "OK",
        SsmlHandling = new SsmlHandlingDiagnostics
        {
          Action = "stripped_warned",
          Warnings = new List<string> { "tag removed" }
        }
      };

      _mockSsmlClient
          .Setup(x => x.PreviewAsync(
              It.IsAny<string>(),
              It.IsAny<string?>(),
              It.IsAny<string?>(),
              It.IsAny<CancellationToken>()))
          .ReturnsAsync(response);

      await _sut.PreviewCommand.ExecuteAsync(null);

      _mockAudioPlayer.Verify(
          x => x.PlayBackendAudioIdAsync("audio-ssml", It.IsAny<string>(), It.IsAny<Action>()),
          Times.Once);
    }
  }
}
