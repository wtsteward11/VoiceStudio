using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using VoiceStudio.App.Tests.Fixtures;
using VoiceStudio.App.Services;
using VoiceStudio.App.ViewModels;
using VoiceStudio.Core.Services;

namespace VoiceStudio.App.Tests.ViewModels
{
  [TestClass]
  public class SSMLControlViewModelTests
  {
    private Mock<IBackendClient> _mockBackend = null!;
    private Mock<IAudioPlayerService> _mockAudioPlayer = null!;
    private IViewModelContext _context = null!;
    private SSMLControlViewModel _sut = null!;

    [TestInitialize]
    public void Setup()
    {
      _mockBackend = new Mock<IBackendClient>();
      _mockAudioPlayer = new Mock<IAudioPlayerService>();
      _context = new MockViewModelContext();
      _sut = new SSMLControlViewModel(_context, _mockBackend.Object, _mockAudioPlayer.Object);
    }

    [TestMethod]
    public async Task PreviewSSMLAsync_OnSuccess_InvokesPlayBackendAudioIdAsync()
    {
      _sut.SsmlContent = "<speak><p>Hello</p></speak>";
      var response = new SSMLControlViewModel.SSMLPreviewResponse
      {
        AudioId = "test-audio-123",
        Duration = 1.5,
        Message = "OK"
      };

      _mockBackend
          .Setup(x => x.SendRequestAsync<object, SSMLControlViewModel.SSMLPreviewResponse>(
              "/api/ssml/preview",
              It.IsAny<object?>(),
              It.IsAny<System.Net.Http.HttpMethod>(),
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
  }
}
