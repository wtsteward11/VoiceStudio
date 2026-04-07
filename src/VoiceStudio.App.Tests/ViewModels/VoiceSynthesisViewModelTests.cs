using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Input;
using VoiceStudio.App.Services;
using VoiceStudio.App.Views.Panels;
using VoiceStudio.Core.Models;
using VoiceStudio.Core.Services;

namespace VoiceStudio.App.Tests.ViewModels
{
  /// <summary>
  /// Comprehensive unit tests for VoiceSynthesisViewModel.
  /// Tests cover synthesis operations, profile loading, audio playback, and error handling.
  /// </summary>
  [TestClass]
  public class VoiceSynthesisViewModelTests
  {
    private Mock<IBackendClient> _mockBackendClient = null!;
    private Mock<IVoiceSynthesisService> _mockVoiceSynthesisService = null!;
    private Mock<IEnginesClient> _mockEnginesClient = null!;
    private Mock<IQualityPipelineService> _mockQualityPipelineService = null!;
    private Mock<IEnsembleService> _mockEnsembleService = null!;
    private Mock<ITextAnalysisService> _mockTextAnalysisService = null!;
    private Mock<IQualityHistoryService> _mockQualityHistoryService = null!;
    private Mock<IProfilesClient> _mockProfilesClient = null!;
    private Mock<IAudioPlayerService> _mockAudioPlayer = null!;
    private Mock<IToastNotificationService> _mockToast = null!;
    private VoiceSynthesisViewModel _sut = null!;

    [TestInitialize]
    public void Setup()
    {
      _mockBackendClient = new Mock<IBackendClient>();
      _mockVoiceSynthesisService = new Mock<IVoiceSynthesisService>();
      _mockEnginesClient = new Mock<IEnginesClient>();
      _mockQualityPipelineService = new Mock<IQualityPipelineService>();
      _mockEnsembleService = new Mock<IEnsembleService>();
      _mockTextAnalysisService = new Mock<ITextAnalysisService>();
      _mockQualityHistoryService = new Mock<IQualityHistoryService>();
      _mockProfilesClient = new Mock<IProfilesClient>();
      _mockAudioPlayer = new Mock<IAudioPlayerService>();
      _mockToast = new Mock<IToastNotificationService>();

      // Setup default mock behavior
      _mockProfilesClient
          .Setup(x => x.GetProfilesAsync(It.IsAny<CancellationToken>()))
          .ReturnsAsync(new List<VoiceProfile>());
      _mockEnginesClient
          .Setup(x => x.GetEnginesAsync(It.IsAny<CancellationToken>()))
          .ReturnsAsync(new List<string>());
      _mockQualityPipelineService
          .Setup(x => x.ListQualityPipelinePresetsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
          .ReturnsAsync(new List<string>());

      _sut = new VoiceSynthesisViewModel(
          _mockVoiceSynthesisService.Object,
          _mockEnginesClient.Object,
          _mockQualityPipelineService.Object,
          _mockEnsembleService.Object,
          _mockTextAnalysisService.Object,
          _mockQualityHistoryService.Object,
          _mockProfilesClient.Object,
          _mockAudioPlayer.Object,
          _mockToast.Object
      );
    }

    [TestCleanup]
    public void Cleanup()
    {
      _sut?.Dispose();
    }

    #region Constructor Tests

    [TestMethod]
    public void Constructor_WithValidDependencies_CreatesInstance()
    {
      // Assert
      Assert.IsNotNull(_sut);
      Assert.IsNotNull(_sut.SynthesizeCommand);
      Assert.IsNotNull(_sut.LoadProfilesCommand);
      Assert.IsNotNull(_sut.PlayAudioCommand);
      Assert.IsNotNull(_sut.StopAudioCommand);
    }

    [TestMethod]
    [ExpectedException(typeof(ArgumentNullException))]
    public void Constructor_WithNullProfilesClient_ThrowsArgumentNullException()
    {
      // Act
      _ = new VoiceSynthesisViewModel(_mockVoiceSynthesisService.Object, _mockEnginesClient.Object, _mockQualityPipelineService.Object, _mockEnsembleService.Object, _mockTextAnalysisService.Object, _mockQualityHistoryService.Object, null!, _mockAudioPlayer.Object, _mockToast.Object);
    }

    [TestMethod]
    [ExpectedException(typeof(ArgumentNullException))]
    public void Constructor_WithNullAudioPlayer_ThrowsArgumentNullException()
    {
      // Act
      _ = new VoiceSynthesisViewModel(_mockVoiceSynthesisService.Object, _mockEnginesClient.Object, _mockQualityPipelineService.Object, _mockEnsembleService.Object, _mockTextAnalysisService.Object, _mockQualityHistoryService.Object, _mockProfilesClient.Object, null!, _mockToast.Object);
    }

    #endregion

    #region Panel Properties Tests

    [TestMethod]
    public void PanelId_ReturnsCorrectValue()
    {
      Assert.AreEqual(VoiceStudio.Core.Panels.PanelIds.VoiceSynthesis, _sut.PanelId);
    }

    [TestMethod]
    public void Region_ReturnsCenterRegion()
    {
      Assert.AreEqual(VoiceStudio.Core.Panels.PanelRegion.Center, _sut.Region);
    }

    #endregion

    #region CanSynthesize Tests

    [TestMethod]
    public void CanSynthesize_WithNoProfile_ReturnsFalse()
    {
      // Arrange
      _sut.SelectedProfile = null;
      _sut.Text = "Test text";

      // Assert
      Assert.IsFalse(_sut.CanSynthesize);
    }

    [TestMethod]
    public void CanSynthesize_WithNoText_ReturnsFalse()
    {
      // Arrange
      _sut.SelectedProfile = new VoiceProfile { Id = "test", Name = "Test Profile" };
      _sut.Text = "";

      // Assert
      Assert.IsFalse(_sut.CanSynthesize);
    }

    [TestMethod]
    public void CanSynthesize_WithWhitespaceText_ReturnsFalse()
    {
      // Arrange
      _sut.SelectedProfile = new VoiceProfile { Id = "test", Name = "Test Profile" };
      _sut.Text = "   ";

      // Assert
      Assert.IsFalse(_sut.CanSynthesize);
    }

    [TestMethod]
    public void CanSynthesize_WhenLoading_ReturnsFalse()
    {
      // Arrange
      _sut.SelectedProfile = new VoiceProfile { Id = "test", Name = "Test Profile" };
      _sut.Text = "Test text";
      _sut.IsLoading = true;

      // Assert
      Assert.IsFalse(_sut.CanSynthesize);
    }

    [TestMethod]
    public void CanSynthesize_WithValidState_ReturnsTrue()
    {
      // Arrange
      _sut.SelectedProfile = new VoiceProfile { Id = "test", Name = "Test Profile" };
      _sut.Text = "Test text";
      _sut.IsLoading = false;

      // Assert
      Assert.IsTrue(_sut.CanSynthesize);
    }

    #endregion

    #region IsEmotionSupported Tests

    [TestMethod]
    public void IsEmotionSupported_WithProfile_ReturnsTrue_AnyEngine()
    {
      _sut.SelectedProfile = new VoiceProfile { Id = "p1", Name = "P" };
      _sut.SelectedEngine = "piper";
      Assert.IsTrue(_sut.IsEmotionSupported);
    }

    [TestMethod]
    public void IsEmotionSupported_WithoutProfile_ReturnsFalse()
    {
      _sut.SelectedProfile = null;
      _sut.SelectedEngine = "xtts";
      Assert.IsFalse(_sut.IsEmotionSupported);
    }

    [TestMethod]
    public void CanonicalEmotionPresets_MatchesGap050Set()
    {
      CollectionAssert.AreEquivalent(
        new[] { "neutral", "warm", "energetic", "calm" },
        _sut.CanonicalEmotionPresets.ToList());
    }

    #endregion

    #region Quality Metrics Display Tests

    [TestMethod]
    public void MosScore_WhenNoMetrics_ReturnsNA()
    {
      _sut.QualityMetrics = null;
      Assert.AreEqual("N/A", _sut.MosScore);
    }

    [TestMethod]
    public void MosScore_WhenHasValue_ReturnsFormattedScore()
    {
      _sut.QualityMetrics = new QualityMetrics { MosScore = 4.25 };
      Assert.AreEqual("4.25/5.0", _sut.MosScore);
    }

    [TestMethod]
    public void Similarity_WhenNoMetrics_ReturnsNA()
    {
      _sut.QualityMetrics = null;
      Assert.AreEqual("N/A", _sut.Similarity);
    }

    [TestMethod]
    public void Similarity_WhenHasValue_ReturnsFormattedPercentage()
    {
      _sut.QualityMetrics = new QualityMetrics { Similarity = 0.85 };
      Assert.AreEqual("85.0%", _sut.Similarity);
    }

    [TestMethod]
    public void Naturalness_WhenNoMetrics_ReturnsNA()
    {
      _sut.QualityMetrics = null;
      Assert.AreEqual("N/A", _sut.Naturalness);
    }

    [TestMethod]
    public void OverallQuality_WhenNoMetrics_ReturnsNA()
    {
      _sut.QualityMetrics = null;
      Assert.AreEqual("N/A", _sut.OverallQuality);
    }

    #endregion

    #region Engine Selection Tests

    [TestMethod]
    public void SelectedEngine_DefaultValue_IsXtts()
    {
      Assert.AreEqual("xtts", _sut.SelectedEngine);
    }

    [TestMethod]
    public void SelectedEngineChanged_DoesNotClearEmotion_WhenProfileSelected()
    {
      _sut.SelectedProfile = new VoiceProfile { Id = "p1", Name = "P" };
      _sut.SelectedEngine = "chatterbox";
      _sut.Emotion = "warm";

      _sut.SelectedEngine = "piper";

      Assert.AreEqual("warm", _sut.Emotion);
    }

    [TestMethod]
    public void SelectedProfileCleared_ClearsEmotion()
    {
      _sut.SelectedProfile = new VoiceProfile { Id = "p1", Name = "P" };
      _sut.Emotion = "calm";

      _sut.SelectedProfile = null;

      Assert.IsNull(_sut.Emotion);
    }

    [TestMethod]
    public async Task SynthesizeCommand_Success_UsesSingleCombinedCapabilityWarningToast()
    {
      _sut.SelectedProfile = new VoiceProfile { Id = "p1", Name = "P" };
      _sut.Text = "Hello";
      _sut.Emotion = "warm";

      _mockVoiceSynthesisService
        .Setup(x => x.SynthesizeVoiceAsync(It.IsAny<VoiceSynthesisRequest>(), It.IsAny<CancellationToken>()))
        .ReturnsAsync(new VoiceSynthesisResponse
        {
          AudioId = "a1",
          AudioUrl = "/api/audio/a1",
          Duration = 1.2,
          QualityScore = 0.91,
          SsmlHandling = new SsmlHandlingDiagnostics
          {
            Action = "stripped_warned",
            Warnings = new List<string> { "SSML note" },
          },
          ProsodyHandling = new ProsodyHandlingDiagnosticsDto
          {
            Warnings = new List<string> { "Prosody note" },
          },
          EmotionPresetApplyFailureMessage = "Preset stage skipped.",
        });

      var cmd = (IAsyncRelayCommand)_sut.SynthesizeCommand;
      await cmd.ExecuteAsync(default);

      _mockToast.Verify(x => x.ShowWarning(It.IsAny<string>(), It.IsAny<string?>()), Times.Once);
      _mockToast.Verify(x => x.ShowSuccess(It.IsAny<string>(), It.IsAny<string?>()), Times.Once);
    }

    #endregion

    #region Multi-Engine Ensemble Tests

    [TestMethod]
    public void ToggleEngineSelection_AddsEngine_WhenNotSelected()
    {
      // Arrange
      _sut.SelectedEngines.Clear();

      // Act
      _sut.ToggleEngineSelection("xtts_v2");

      // Assert
      Assert.IsTrue(_sut.SelectedEngines.Contains("xtts_v2"));
    }

    [TestMethod]
    public void ToggleEngineSelection_RemovesEngine_WhenAlreadySelected()
    {
      // Arrange
      _sut.SelectedEngines.Clear();
      _sut.SelectedEngines.Add("xtts_v2");

      // Act
      _sut.ToggleEngineSelection("xtts_v2");

      // Assert
      Assert.IsFalse(_sut.SelectedEngines.Contains("xtts_v2"));
    }

    [TestMethod]
    public void ToggleEngineSelection_LimitsToFiveEngines()
    {
      // Arrange
      _sut.SelectedEngines.Clear();
      _sut.SelectedEngines.Add("engine1");
      _sut.SelectedEngines.Add("engine2");
      _sut.SelectedEngines.Add("engine3");
      _sut.SelectedEngines.Add("engine4");
      _sut.SelectedEngines.Add("engine5");

      // Act
      _sut.ToggleEngineSelection("engine6");

      // Assert
      Assert.AreEqual(5, _sut.SelectedEngines.Count);
      Assert.IsFalse(_sut.SelectedEngines.Contains("engine6"));
    }

    [TestMethod]
    public void IsEngineSelected_ReturnsTrue_WhenEngineInList()
    {
      _sut.SelectedEngines.Clear();
      _sut.SelectedEngines.Add("xtts_v2");

      Assert.IsTrue(_sut.IsEngineSelected("xtts_v2"));
    }

    [TestMethod]
    public void IsEngineSelected_ReturnsFalse_WhenEngineNotInList()
    {
      _sut.SelectedEngines.Clear();

      Assert.IsFalse(_sut.IsEngineSelected("xtts_v2"));
    }

    #endregion

    #region Default Values Tests

    [TestMethod]
    public void DefaultValues_AreCorrect()
    {
      Assert.AreEqual("en", _sut.Language);
      Assert.AreEqual(string.Empty, _sut.Text);
      Assert.IsFalse(_sut.IsLoading);
      Assert.IsFalse(_sut.HasError);
      Assert.IsFalse(_sut.EnhanceQuality);
      Assert.IsFalse(_sut.UseMultiEngineEnsemble);
      Assert.AreEqual("voting", _sut.EnsembleSelectionMode);
    }

    #endregion

    #region Dispose Tests

    [TestMethod]
    public void Dispose_ClearsProfiles()
    {
      // Arrange
      _sut.Profiles.Add(new VoiceProfile { Id = "test", Name = "Test" });

      // Act
      _sut.Dispose();

      // Assert
      Assert.AreEqual(0, _sut.Profiles.Count);
    }

    [TestMethod]
    public void Dispose_CanBeCalledMultipleTimes()
    {
      // Act & Assert - should not throw
      _sut.Dispose();
      _sut.Dispose();
    }

    #endregion
  }
}
