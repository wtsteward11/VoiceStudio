using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using VoiceStudio.Core.Models;
using VoiceStudio.Core.Services;

namespace VoiceStudio.App.Tests.ViewModels
{
  /// <summary>
  /// Transport-mock tests for transcription backend contract.
  /// Tests IBackendClient directly; does not instantiate TranscribeViewModel with ITranscriptionClient.
  /// See docs/governance/TEST_CLASSIFICATION.md. Cannot support "TranscribeViewModel migration complete" claims.
  /// </summary>
  [TestClass]
  [TestCategory("TransportMock")]
  public class TranscribeViewModelTests
  {
    private Mock<IBackendClient> _mockBackendClient = null!;

    [TestInitialize]
    public void Setup()
    {
      _mockBackendClient = new Mock<IBackendClient>();
    }

    #region Transcription Operations Tests

    [TestMethod]
    public async Task TranscribeAudioAsync_ReturnsTranscriptionResponse()
    {
      // Arrange
      var expectedResponse = new TranscriptionResponse
      {
        Id = "transcription-123",
        Text = "Hello, this is a test transcription.",
        Language = "en"
      };

      _mockBackendClient
          .Setup(x => x.TranscribeAudioAsync(It.IsAny<TranscriptionRequest>(), null, It.IsAny<CancellationToken>()))
          .ReturnsAsync(expectedResponse);

      var request = new TranscriptionRequest
      {
        AudioId = "audio-456",
        Engine = "whisper",
        Language = "en"
      };

      // Act
      var result = await _mockBackendClient.Object.TranscribeAudioAsync(request, null, CancellationToken.None);

      // Assert
      Assert.IsNotNull(result);
      Assert.AreEqual("transcription-123", result.Id);
      Assert.AreEqual("Hello, this is a test transcription.", result.Text);
      Assert.AreEqual("en", result.Language);
    }

    [TestMethod]
    public async Task TranscribeAudioAsync_WithProjectId_IncludesProjectId()
    {
      // Arrange
      string? capturedProjectId = null;
      _mockBackendClient
          .Setup(x => x.TranscribeAudioAsync(It.IsAny<TranscriptionRequest>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
          .Callback<TranscriptionRequest, string?, CancellationToken>((req, projId, ct) => capturedProjectId = projId)
          .ReturnsAsync(new TranscriptionResponse { Id = "t-1" });

      var request = new TranscriptionRequest { AudioId = "a-1" };

      // Act
      await _mockBackendClient.Object.TranscribeAudioAsync(request, "project-789", CancellationToken.None);

      // Assert
      Assert.AreEqual("project-789", capturedProjectId);
    }

    #endregion

    #region List Transcriptions Tests

    [TestMethod]
    public async Task ListTranscriptionsAsync_ReturnsListOfTranscriptions()
    {
      // Arrange
      var expectedList = new List<TranscriptionResponse>
            {
                new TranscriptionResponse { Id = "t-1", Text = "First transcription" },
                new TranscriptionResponse { Id = "t-2", Text = "Second transcription" },
                new TranscriptionResponse { Id = "t-3", Text = "Third transcription" }
            };

      _mockBackendClient
          .Setup(x => x.ListTranscriptionsAsync(null, null, It.IsAny<CancellationToken>()))
          .ReturnsAsync(expectedList);

      // Act
      var result = await _mockBackendClient.Object.ListTranscriptionsAsync(null, null, CancellationToken.None);

      // Assert
      Assert.AreEqual(3, result.Count);
      Assert.AreEqual("t-1", result[0].Id);
    }

    [TestMethod]
    public async Task ListTranscriptionsAsync_FiltersByAudioId()
    {
      // Arrange
      var filteredList = new List<TranscriptionResponse>
            {
                new TranscriptionResponse { Id = "t-audio", AudioId = "audio-specific" }
            };

      _mockBackendClient
          .Setup(x => x.ListTranscriptionsAsync("audio-specific", null, It.IsAny<CancellationToken>()))
          .ReturnsAsync(filteredList);

      // Act
      var result = await _mockBackendClient.Object.ListTranscriptionsAsync("audio-specific", null, CancellationToken.None);

      // Assert
      Assert.AreEqual(1, result.Count);
      Assert.AreEqual("audio-specific", result[0].AudioId);
    }

    #endregion

    #region Get Transcription Tests

    [TestMethod]
    public async Task GetTranscriptionAsync_ReturnsSingleTranscription()
    {
      // Arrange
      var expectedTranscription = new TranscriptionResponse
      {
        Id = "transcription-get",
        Text = "Detailed transcription text with timestamps.",
        Language = "en",
        Duration = 5.5
      };

      _mockBackendClient
          .Setup(x => x.GetTranscriptionAsync("transcription-get", It.IsAny<CancellationToken>()))
          .ReturnsAsync(expectedTranscription);

      // Act
      var result = await _mockBackendClient.Object.GetTranscriptionAsync("transcription-get", CancellationToken.None);

      // Assert
      Assert.IsNotNull(result);
      Assert.AreEqual("transcription-get", result.Id);
      Assert.AreEqual(5.5, result.Duration);
    }

    #endregion

    #region Delete Transcription Tests

    [TestMethod]
    public async Task DeleteTranscriptionAsync_ReturnsTrue_OnSuccess()
    {
      // Arrange
      _mockBackendClient
          .Setup(x => x.DeleteTranscriptionAsync("transcription-delete", It.IsAny<CancellationToken>()))
          .ReturnsAsync(true);

      // Act
      var result = await _mockBackendClient.Object.DeleteTranscriptionAsync("transcription-delete", CancellationToken.None);

      // Assert
      Assert.IsTrue(result);
    }

    [TestMethod]
    public async Task DeleteTranscriptionAsync_ReturnsFalse_WhenNotFound()
    {
      // Arrange
      _mockBackendClient
          .Setup(x => x.DeleteTranscriptionAsync("nonexistent", It.IsAny<CancellationToken>()))
          .ReturnsAsync(false);

      // Act
      var result = await _mockBackendClient.Object.DeleteTranscriptionAsync("nonexistent", CancellationToken.None);

      // Assert
      Assert.IsFalse(result);
    }

    #endregion

    #region Supported Languages Tests

    [TestMethod]
    public async Task GetSupportedLanguagesAsync_ReturnsListOfLanguages()
    {
      // Arrange
      var expectedLanguages = new List<SupportedLanguage>
            {
                new SupportedLanguage { Code = "en", Name = "English" },
                new SupportedLanguage { Code = "es", Name = "Spanish" },
                new SupportedLanguage { Code = "fr", Name = "French" },
                new SupportedLanguage { Code = "de", Name = "German" }
            };

      _mockBackendClient
          .Setup(x => x.GetSupportedLanguagesAsync(It.IsAny<CancellationToken>()))
          .ReturnsAsync(expectedLanguages);

      // Act
      var result = await _mockBackendClient.Object.GetSupportedLanguagesAsync(CancellationToken.None);

      // Assert
      Assert.AreEqual(4, result.Count);
      Assert.AreEqual("en", result[0].Code);
      Assert.AreEqual("English", result[0].Name);
    }

    #endregion

    #region TranscriptionRequest Properties Tests

    [TestMethod]
    public void TranscriptionRequest_DefaultValues()
    {
      var request = new TranscriptionRequest();

      Assert.AreEqual(string.Empty, request.AudioId);
      Assert.AreEqual("whisper", request.Engine);
      Assert.IsFalse(request.WordTimestamps);
      Assert.IsFalse(request.Diarization);
      Assert.IsFalse(request.UseVad);
    }

    [TestMethod]
    public void TranscriptionRequest_WithOptions()
    {
      var request = new TranscriptionRequest
      {
        AudioId = "audio-123",
        Engine = "whisperx",
        Language = "es",
        WordTimestamps = true,
        Diarization = true,
        UseVad = true
      };

      Assert.AreEqual("audio-123", request.AudioId);
      Assert.AreEqual("whisperx", request.Engine);
      Assert.AreEqual("es", request.Language);
      Assert.IsTrue(request.WordTimestamps);
      Assert.IsTrue(request.Diarization);
      Assert.IsTrue(request.UseVad);
    }

    #endregion

    #region TranscriptionResponse Properties Tests

    [TestMethod]
    public void TranscriptionResponse_DefaultValues()
    {
      var response = new TranscriptionResponse();

      Assert.AreEqual(string.Empty, response.Id);
      Assert.AreEqual(string.Empty, response.Text);
      Assert.AreEqual(string.Empty, response.Language);
      Assert.AreEqual(0.0, response.Duration);
    }

    [TestMethod]
    public void TranscriptionResponse_WithWordTimestamps()
    {
      var response = new TranscriptionResponse
      {
        Id = "t-words",
        Text = "Hello world",
        WordTimestamps = new List<WordTimestamp>
                {
                    new WordTimestamp { Word = "Hello", Start = 0.0, End = 0.5, Confidence = 0.98 },
                    new WordTimestamp { Word = "world", Start = 0.6, End = 1.0, Confidence = 0.95 }
                }
      };

      Assert.AreEqual(2, response.WordTimestamps.Count);
      Assert.AreEqual("Hello", response.WordTimestamps[0].Word);
      Assert.AreEqual(0.0, response.WordTimestamps[0].Start);
      Assert.AreEqual(0.5, response.WordTimestamps[0].End);
      Assert.AreEqual(0.98, response.WordTimestamps[0].Confidence);
    }

    #endregion

    #region WordTimestamp Tests

    [TestMethod]
    public void WordTimestamp_DefaultValues()
    {
      var word = new WordTimestamp();

      Assert.AreEqual(string.Empty, word.Word);
      Assert.AreEqual(0.0, word.Start);
      Assert.AreEqual(0.0, word.End);
      Assert.IsNull(word.Confidence);
    }

    [TestMethod]
    public void WordTimestamp_WithAllProperties()
    {
      var word = new WordTimestamp
      {
        Word = "example",
        Start = 1.5,
        End = 2.0,
        Confidence = 0.92
      };

      Assert.AreEqual("example", word.Word);
      Assert.AreEqual(1.5, word.Start);
      Assert.AreEqual(2.0, word.End);
      Assert.AreEqual(0.92, word.Confidence);
    }

    #endregion
  }
}
