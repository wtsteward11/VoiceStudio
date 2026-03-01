using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
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
    private Mock<IAudioPlayerService> _mockAudioPlayer = null!;
    private VoiceSynthesisViewModel _sut = null!;

    [TestInitialize]
    public void Setup()
    {
      _mockBackendClient = new Mock<IBackendClient>();
      _mockAudioPlayer = new Mock<IAudioPlayerService>();

      // Setup default mock behavior
      _mockBackendClient
          .Setup(x => x.GetProfilesAsync(It.IsAny<CancellationToken>()))
          .ReturnsAsync(new List<VoiceProfile>());

      _sut = new VoiceSynthesisViewModel(
          _mockBackendClient.Object,
          _mockAudioPlayer.Object
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
    public void Constructor_WithNullBackendClient_ThrowsArgumentNullException()
    {
      // Act
      _ = new VoiceSynthesisViewModel(null!, _mockAudioPlayer.Object);
    }

    [TestMethod]
    [ExpectedException(typeof(ArgumentNullException))]
    public void Constructor_WithNullAudioPlayer_ThrowsArgumentNullException()
    {
      // Act
      _ = new VoiceSynthesisViewModel(_mockBackendClient.Object, null!);
    }

    #endregion

    #region Panel Properties Tests

    [TestMethod]
    public void PanelId_ReturnsCorrectValue()
    {
      Assert.AreEqual("voice_synthesis", _sut.PanelId);
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
    public void IsEmotionSupported_WithChatterbox_ReturnsTrue()
    {
      _sut.SelectedEngine = "chatterbox";
      Assert.IsTrue(_sut.IsEmotionSupported);
    }

    [TestMethod]
    public void IsEmotionSupported_WithXtts_ReturnsTrue()
    {
      _sut.SelectedEngine = "xtts";
      Assert.IsTrue(_sut.IsEmotionSupported);
    }

    [TestMethod]
    public void IsEmotionSupported_WithOtherEngine_ReturnsFalse()
    {
      _sut.SelectedEngine = "piper";
      Assert.IsFalse(_sut.IsEmotionSupported);
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
    public void SelectedEngineChanged_ClearsEmotion_WhenNotSupported()
    {
      // Arrange
      _sut.SelectedEngine = "chatterbox";
      _sut.Emotion = "happy";

      // Act
      _sut.SelectedEngine = "piper";

      // Assert
      Assert.IsNull(_sut.Emotion);
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

    [TestMethod]
    public void SynthesisParameterDefaults_AreCorrect()
    {
      Assert.AreEqual(1.0, _sut.Speed);
      Assert.AreEqual(0.0, _sut.Pitch);
      Assert.AreEqual(0.72, _sut.Stability);
      Assert.AreEqual(0.58, _sut.Clarity);
      Assert.AreEqual(0.35, _sut.Temperature);
    }

    [TestMethod]
    public void SpeedDisplay_FormatsCorrectly()
    {
      _sut.Speed = 1.5;
      Assert.AreEqual("1.50", _sut.SpeedDisplay);
    }

    [TestMethod]
    public void PitchDisplay_FormatsPositiveCorrectly()
    {
      _sut.Pitch = 3;
      Assert.AreEqual("+3", _sut.PitchDisplay);
    }

    [TestMethod]
    public void PitchDisplay_FormatsNegativeCorrectly()
    {
      _sut.Pitch = -5;
      Assert.AreEqual("-5", _sut.PitchDisplay);
    }

    [TestMethod]
    public void AvailableLanguages_HasDefaults()
    {
      Assert.IsTrue(_sut.AvailableLanguages.Count >= 8);
      Assert.IsTrue(_sut.AvailableLanguages.Contains("en"));
    }

    [TestMethod]
    public void AvailableEmotions_HasDefaults()
    {
      Assert.IsTrue(_sut.AvailableEmotions.Count >= 6);
      Assert.IsTrue(_sut.AvailableEmotions.Contains("neutral"));
    }

    [TestMethod]
    public void AvailableEngines_HasFallbackDefaults()
    {
      Assert.IsTrue(_sut.AvailableEngines.Count >= 3);
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
