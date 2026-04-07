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
  /// Verifies request shaping (no caller mutation), 404 handling (BackendNotFoundException), error mapping,
  /// and GAP-050 canonical preset delegation to apply-extended (no local preset math).
  /// </summary>
  [TestClass]
  public class VoiceSynthesisServiceTests
  {
    private Mock<IBackendClient> _mockBackend = null!;
    private Mock<IEmotionControlClient> _mockEmotion = null!;
    private VoiceSynthesisService _sut = null!;

    [TestInitialize]
    public void Setup()
    {
      _mockBackend = new Mock<IBackendClient>();
      _mockEmotion = new Mock<IEmotionControlClient>();
      _sut = new VoiceSynthesisService(_mockBackend.Object, _mockEmotion.Object);
    }

    [TestMethod]
    public void Constructor_WithNullBackend_ThrowsArgumentNullException()
    {
      Assert.ThrowsException<ArgumentNullException>(() =>
        new VoiceSynthesisService(null!, _mockEmotion.Object));
    }

    [TestMethod]
    public void Constructor_WithNullEmotionClient_ThrowsArgumentNullException()
    {
      Assert.ThrowsException<ArgumentNullException>(() =>
        new VoiceSynthesisService(_mockBackend.Object, null!));
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
      _mockEmotion.Verify(
        x => x.ApplyEmotionAsync(It.IsAny<EmotionApplyExtendedRequest>(), It.IsAny<CancellationToken>()),
        Times.Never);
    }

    [TestMethod]
    public async Task SynthesizeVoiceAsync_CanonicalPreset_StripsEngineEmotion_AndCallsApplyExtended()
    {
      var request = new VoiceSynthesisRequest
      {
        Engine = "piper",
        ProfileId = "p1",
        Text = "Hi",
        Emotion = "Warm",
      };

      _mockBackend
        .Setup(x => x.SynthesizeVoiceAsync(It.IsAny<VoiceSynthesisRequest>(), It.IsAny<CancellationToken>()))
        .ReturnsAsync(new VoiceSynthesisResponse
        {
          AudioId = "base1",
          AudioUrl = "/api/audio/base1",
          Duration = 1,
          QualityScore = 0.5,
        });

      _mockEmotion
        .Setup(x => x.ApplyEmotionAsync(It.IsAny<EmotionApplyExtendedRequest>(), It.IsAny<CancellationToken>()))
        .ReturnsAsync(new EmotionApplyExtendedResponse
        {
          AudioId = "out1",
          AudioUrl = "",
          ProsodyHandling = new ProsodyHandlingDiagnosticsDto { Warnings = new System.Collections.Generic.List<string> { "note" } },
          EmotionMappingSource = "canonical_preset",
        });

      var result = await _sut.SynthesizeVoiceAsync(request, CancellationToken.None);

      _mockBackend.Verify(
        x => x.SynthesizeVoiceAsync(
          It.Is<VoiceSynthesisRequest>(r => r.Emotion == null),
          It.IsAny<CancellationToken>()),
        Times.Once);

      _mockEmotion.Verify(
        x => x.ApplyEmotionAsync(
          It.Is<EmotionApplyExtendedRequest>(r =>
            r.AudioId == "base1"
            && string.Equals(r.PrimaryEmotion, "warm", StringComparison.Ordinal)
            && r.PrimaryIntensity == 100f),
          It.IsAny<CancellationToken>()),
        Times.Once);

      Assert.AreEqual("out1", result.AudioId);
      Assert.AreEqual("/api/audio/out1", result.AudioUrl);
      Assert.IsNotNull(result.ProsodyHandling);
      Assert.AreEqual("canonical_preset", result.EmotionMappingSource);
      Assert.IsNull(result.EmotionPresetApplyFailureMessage);
    }

    [TestMethod]
    public async Task SynthesizeVoiceAsync_NonCanonicalEmotion_PassesThrough_DoesNotCallApplyExtended()
    {
      _mockBackend
        .Setup(x => x.SynthesizeVoiceAsync(It.IsAny<VoiceSynthesisRequest>(), It.IsAny<CancellationToken>()))
        .ReturnsAsync(new VoiceSynthesisResponse { AudioId = "a1", AudioUrl = "/api/audio/a1" });

      _ = await _sut.SynthesizeVoiceAsync(
        new VoiceSynthesisRequest
        {
          Engine = "xtts",
          ProfileId = "p1",
          Text = "Hi",
          Emotion = "happy",
        },
        CancellationToken.None);

      _mockBackend.Verify(
        x => x.SynthesizeVoiceAsync(
          It.Is<VoiceSynthesisRequest>(r => r.Emotion == "happy"),
          It.IsAny<CancellationToken>()),
        Times.Once);
      _mockEmotion.Verify(
        x => x.ApplyEmotionAsync(It.IsAny<EmotionApplyExtendedRequest>(), It.IsAny<CancellationToken>()),
        Times.Never);
    }

    [TestMethod]
    public async Task SynthesizeVoiceAsync_ApplyExtendedReturnsNull_SetsFailureMessage_KeepsBaseAudio()
    {
      _mockBackend
        .Setup(x => x.SynthesizeVoiceAsync(It.IsAny<VoiceSynthesisRequest>(), It.IsAny<CancellationToken>()))
        .ReturnsAsync(new VoiceSynthesisResponse
        {
          AudioId = "base1",
          AudioUrl = "/api/audio/base1",
        });

      _mockEmotion
        .Setup(x => x.ApplyEmotionAsync(It.IsAny<EmotionApplyExtendedRequest>(), It.IsAny<CancellationToken>()))
        .ReturnsAsync((EmotionApplyExtendedResponse?)null);

      var result = await _sut.SynthesizeVoiceAsync(
        new VoiceSynthesisRequest
        {
          Engine = "xtts",
          ProfileId = "p1",
          Text = "Hi",
          Emotion = "calm",
        },
        CancellationToken.None);

      Assert.AreEqual("base1", result.AudioId);
      Assert.AreEqual("/api/audio/base1", result.AudioUrl);
      Assert.IsFalse(string.IsNullOrEmpty(result.EmotionPresetApplyFailureMessage));
    }

    [TestMethod]
    public async Task SynthesizeVoiceAsync_BackendNotFoundException_PropagatesTyped()
    {
      _mockBackend
        .Setup(x => x.SynthesizeVoiceAsync(It.IsAny<VoiceSynthesisRequest>(), It.IsAny<CancellationToken>()))
        .ThrowsAsync(new BackendNotFoundException("Profile not found"));

      var ex = await Assert.ThrowsExceptionAsync<BackendNotFoundException>(async () =>
        await _sut.SynthesizeVoiceAsync(
          new VoiceSynthesisRequest { Engine = "xtts", ProfileId = "p1", Text = "Hi" },
          CancellationToken.None));

      StringAssert.Contains(ex.Message, "Profile");
    }

    [TestMethod]
    public async Task SynthesizeVoiceAsync_HttpRequestException_MapsToBackendUnavailableWithoutRawSocketText()
    {
      _mockBackend
        .Setup(x => x.SynthesizeVoiceAsync(It.IsAny<VoiceSynthesisRequest>(), It.IsAny<CancellationToken>()))
        .ThrowsAsync(new HttpRequestException("Connection refused"));

      var ex = await Assert.ThrowsExceptionAsync<BackendUnavailableException>(async () =>
        await _sut.SynthesizeVoiceAsync(
          new VoiceSynthesisRequest { Engine = "xtts", ProfileId = "p1", Text = "Hi" },
          CancellationToken.None));

      Assert.IsTrue(ex.Message.Contains("backend", StringComparison.OrdinalIgnoreCase));
      Assert.IsFalse(ex.Message.Contains("Connection refused", StringComparison.Ordinal));
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
