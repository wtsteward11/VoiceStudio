using System;
using System.IO;
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
  /// Unit tests for ABTestService.
  /// Verifies RunABTestAsync and GetAudioStreamAsync delegation, cancellation.
  /// </summary>
  [TestClass]
  public class ABTestServiceTests
  {
    private Mock<IBackendClient> _mockBackend = null!;
    private ABTestService _sut = null!;

    [TestInitialize]
    public void Setup()
    {
      _mockBackend = new Mock<IBackendClient>();
      _sut = new ABTestService(_mockBackend.Object);
    }

    [TestMethod]
    public void Constructor_WithNullBackend_ThrowsArgumentNullException()
    {
      Assert.ThrowsException<ArgumentNullException>(() => new ABTestService(null!));
    }

    [TestMethod]
    public async Task RunABTestAsync_HappyPath_ReturnsResponse()
    {
      var request = new ABTestRequest { ProfileId = "profile-1", Text = "Hello" };
      var response = new ABTestResponse
      {
        SampleA = new ABTestResult { SampleLabel = "A", AudioId = "audio-a" },
        SampleB = new ABTestResult { SampleLabel = "B", AudioId = "audio-b" },
        TestId = "test-1"
      };
      _mockBackend
        .Setup(x => x.RunABTestAsync(It.IsAny<ABTestRequest>(), It.IsAny<CancellationToken>()))
        .ReturnsAsync(response);

      var result = await _sut.RunABTestAsync(request, CancellationToken.None);

      Assert.IsNotNull(result);
      Assert.AreEqual("test-1", result.TestId);
      Assert.AreEqual("audio-a", result.SampleA.AudioId);
      Assert.AreEqual("audio-b", result.SampleB.AudioId);
      _mockBackend.Verify(
        x => x.RunABTestAsync(request, It.IsAny<CancellationToken>()),
        Times.Once);
    }

    [TestMethod]
    public async Task RunABTestAsync_Cancellation_Propagates()
    {
      var request = new ABTestRequest { ProfileId = "p", Text = "t" };
      var cts = new CancellationTokenSource();
      cts.Cancel();
      _mockBackend
        .Setup(x => x.RunABTestAsync(It.IsAny<ABTestRequest>(), It.IsAny<CancellationToken>()))
        .ThrowsAsync(new OperationCanceledException());

      await Assert.ThrowsExceptionAsync<OperationCanceledException>(async () =>
        await _sut.RunABTestAsync(request, cts.Token));
    }

    [TestMethod]
    public async Task GetAudioStreamAsync_HappyPath_ReturnsStream()
    {
      var response = new MemoryStream(new byte[] { 1, 2, 3 });
      _mockBackend
        .Setup(x => x.GetAudioStreamAsync("audio-1", It.IsAny<CancellationToken>()))
        .ReturnsAsync(response);

      var result = await _sut.GetAudioStreamAsync("audio-1", CancellationToken.None);

      Assert.IsNotNull(result);
      Assert.AreEqual(3, result.Length);
      _mockBackend.Verify(
        x => x.GetAudioStreamAsync("audio-1", It.IsAny<CancellationToken>()),
        Times.Once);
    }

    [TestMethod]
    public async Task GetAudioStreamAsync_Cancellation_Propagates()
    {
      var cts = new CancellationTokenSource();
      cts.Cancel();
      _mockBackend
        .Setup(x => x.GetAudioStreamAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
        .ThrowsAsync(new OperationCanceledException());

      await Assert.ThrowsExceptionAsync<OperationCanceledException>(async () =>
        await _sut.GetAudioStreamAsync("audio-1", cts.Token));
    }
  }
}
