using System;
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
  /// Unit tests for ProfileEnhancementService.
  /// Verifies request building, backend delegation, and cancellation.
  /// </summary>
  [TestClass]
  public class ProfileEnhancementServiceTests
  {
    private Mock<IBackendClient> _mockBackend = null!;
    private ProfileEnhancementService _sut = null!;

    [TestInitialize]
    public void Setup()
    {
      _mockBackend = new Mock<IBackendClient>();
      _sut = new ProfileEnhancementService(_mockBackend.Object);
    }

    [TestMethod]
    public void Constructor_WithNullBackend_ThrowsArgumentNullException()
    {
      Assert.ThrowsException<ArgumentNullException>(() =>
        new ProfileEnhancementService(null!));
    }

    [TestMethod]
    public async Task EnhanceAsync_DelegatesToBackend_ReturnsResponse()
    {
      var expected = new ReferenceAudioPreprocessResponse
      {
        ProcessedAudioId = "proc-1",
        ProcessedAudioUrl = "http://localhost:8000/audio/proc-1.wav",
        QualityImprovement = 0.15
      };
      _mockBackend
        .Setup(x => x.SendRequestAsync<ReferenceAudioPreprocessRequest, ReferenceAudioPreprocessResponse>(
          It.IsAny<string>(),
          It.IsAny<ReferenceAudioPreprocessRequest>(),
          It.IsAny<CancellationToken>()))
        .ReturnsAsync(expected);

      var result = await _sut.EnhanceAsync("profile-1", true, true, 1.0, 5);

      Assert.IsNotNull(result);
      Assert.AreEqual("proc-1", result.ProcessedAudioId);
      Assert.AreEqual(0.15, result.QualityImprovement);
      _mockBackend.Verify(
        x => x.SendRequestAsync<ReferenceAudioPreprocessRequest, ReferenceAudioPreprocessResponse>(
          "/api/profiles/profile-1/preprocess-reference",
          It.Is<ReferenceAudioPreprocessRequest>(r =>
            r.ProfileId == "profile-1" &&
            r.AutoEnhance &&
            r.SelectOptimalSegments &&
            r.MinSegmentDuration == 1.0 &&
            r.MaxSegments == 5),
          It.IsAny<CancellationToken>()),
        Times.Once);
    }

    [TestMethod]
    public async Task EnhanceAsync_BackendReturnsNull_ReturnsNull()
    {
      _mockBackend
        .Setup(x => x.SendRequestAsync<ReferenceAudioPreprocessRequest, ReferenceAudioPreprocessResponse>(
          It.IsAny<string>(),
          It.IsAny<ReferenceAudioPreprocessRequest>(),
          It.IsAny<CancellationToken>()))
        .ReturnsAsync((ReferenceAudioPreprocessResponse?)null);

      var result = await _sut.EnhanceAsync("profile-1", false, false, 2.0, 3);

      Assert.IsNull(result);
    }

    [TestMethod]
    public async Task EnhanceAsync_CancellationRequested_PropagatesToBackend()
    {
      using var cts = new CancellationTokenSource();
      cts.Cancel();
      _mockBackend
        .Setup(x => x.SendRequestAsync<ReferenceAudioPreprocessRequest, ReferenceAudioPreprocessResponse>(
          It.IsAny<string>(),
          It.IsAny<ReferenceAudioPreprocessRequest>(),
          It.IsAny<CancellationToken>()))
        .ThrowsAsync(new OperationCanceledException());

      await Assert.ThrowsExceptionAsync<OperationCanceledException>(() =>
        _sut.EnhanceAsync("profile-1", true, false, 1.0, 5, cts.Token));
    }
  }
}
