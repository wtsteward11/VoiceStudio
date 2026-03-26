using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using VoiceStudio.App.Services;
using VoiceStudio.Core.Exceptions;
using VoiceStudio.Core.Models;
using VoiceStudio.Core.Services;

namespace VoiceStudio.App.Tests.Services
{
  /// <summary>
  /// Unit tests for VoiceSynthesisService.
  /// Verifies request shaping (no caller mutation), 404 handling (BackendNotFoundException), and error mapping.
  /// </summary>
  [TestClass]
  public class VoiceSynthesisServiceTests
  {
    private Mock<IBackendClient> _mockBackend = null!;
    private VoiceSynthesisService _sut = null!;

    [TestInitialize]
    public void Setup()
    {
      _mockBackend = new Mock<IBackendClient>();
      _sut = new VoiceSynthesisService(_mockBackend.Object);
    }

    [TestMethod]
    public void Constructor_WithNullBackend_ThrowsArgumentNullException()
    {
      Assert.ThrowsException<ArgumentNullException>(() =>
        new VoiceSynthesisService(null!));
    }

    [TestMethod]
    public async Task SynthesizeVoiceAsync_WithEmptyEngine_DoesNotMutateCallerRequest()
    {
      var request = new VoiceSynthesisRequest
      {
        Engine = "",
        ProfileId = "profile-1",
        Text = "Hello",
        Language = "en"
      };
      var originalEngine = request.Engine;

      _mockBackend
        .Setup(x => x.SynthesizeVoiceAsync(It.IsAny<VoiceSynthesisRequest>(), It.IsAny<CancellationToken>()))
        .ReturnsAsync(new VoiceSynthesisResponse { AudioId = "a1", AudioUrl = "/api/audio/a1" });

      _ = await _sut.SynthesizeVoiceAsync(request, CancellationToken.None);

      Assert.AreEqual("", request.Engine, "Caller request must not be mutated");
      _mockBackend.Verify(
        x => x.SynthesizeVoiceAsync(
          It.Is<VoiceSynthesisRequest>(r => r.Engine == "xtts" && r.Text == "Hello"),
          It.IsAny<CancellationToken>()),
        Times.Once);
    }

    [TestMethod]
    public async Task SynthesizeVoiceAsync_BackendNotFoundException_MapsToInvalidOperationException()
    {
      _mockBackend
        .Setup(x => x.SynthesizeVoiceAsync(It.IsAny<VoiceSynthesisRequest>(), It.IsAny<CancellationToken>()))
        .ThrowsAsync(new BackendNotFoundException("Profile not found"));

      var ex = await Assert.ThrowsExceptionAsync<InvalidOperationException>(async () =>
        await _sut.SynthesizeVoiceAsync(
          new VoiceSynthesisRequest { Engine = "xtts", ProfileId = "p1", Text = "Hi" },
          CancellationToken.None));

      Assert.IsTrue(ex.Message.Contains("Profile or engine not found", StringComparison.OrdinalIgnoreCase));
      Assert.IsNotNull(ex.InnerException);
      Assert.IsInstanceOfType(ex.InnerException, typeof(BackendNotFoundException));
    }

    [TestMethod]
    public async Task SynthesizeVoiceAsync_HttpRequestException_MapsToInvalidOperationException()
    {
      _mockBackend
        .Setup(x => x.SynthesizeVoiceAsync(It.IsAny<VoiceSynthesisRequest>(), It.IsAny<CancellationToken>()))
        .ThrowsAsync(new HttpRequestException("Connection refused"));

      var ex = await Assert.ThrowsExceptionAsync<InvalidOperationException>(async () =>
        await _sut.SynthesizeVoiceAsync(
          new VoiceSynthesisRequest { Engine = "xtts", ProfileId = "p1", Text = "Hi" },
          CancellationToken.None));

      Assert.IsTrue(ex.Message.Contains("Backend is unavailable", StringComparison.OrdinalIgnoreCase));
    }

    [TestMethod]
    public async Task SynthesizeVoiceAsync_OperationCanceledException_Propagates()
    {
      _mockBackend
        .Setup(x => x.SynthesizeVoiceAsync(It.IsAny<VoiceSynthesisRequest>(), It.IsAny<CancellationToken>()))
        .ThrowsAsync(new OperationCanceledException());

      await Assert.ThrowsExceptionAsync<OperationCanceledException>(async () =>
        await _sut.SynthesizeVoiceAsync(
          new VoiceSynthesisRequest { Engine = "xtts", ProfileId = "p1", Text = "Hi" },
          CancellationToken.None));
    }

    [TestMethod]
    public async Task SynthesizeVoiceAsync_EmptyText_ThrowsArgumentException()
    {
      await Assert.ThrowsExceptionAsync<ArgumentException>(async () =>
        await _sut.SynthesizeVoiceAsync(
          new VoiceSynthesisRequest { Engine = "xtts", ProfileId = "p1", Text = "" },
          CancellationToken.None));

      _mockBackend.Verify(
        x => x.SynthesizeVoiceAsync(It.IsAny<VoiceSynthesisRequest>(), It.IsAny<CancellationToken>()),
        Times.Never);
    }
  }
}
