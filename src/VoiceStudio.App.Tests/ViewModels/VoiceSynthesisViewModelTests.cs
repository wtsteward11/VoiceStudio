using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Input;
using VoiceStudio.App.Services;
using VoiceStudio.App.Views.Panels;
using VoiceStudio.Core.Exceptions;
using VoiceStudio.Core.Models;
using VoiceStudio.Core.Panels;
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
      Assert.IsNotNull(_sut.RetryPlaybackCommand);
      Assert.IsNotNull(_sut.CopyPlaybackErrorCommand);
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
    public void SelectedProfile_SwitchToDifferentProfileId_ClearsEmotion_Gap050()
    {
      _sut.SelectedProfile = new VoiceProfile { Id = "p1", Name = "A" };
      _sut.Emotion = "warm";

      _sut.SelectedProfile = new VoiceProfile { Id = "p2", Name = "B" };

      Assert.IsNull(_sut.Emotion);
    }

    [TestMethod]
    public void Emotion_SetToNonCanonical_NormalizesToNull_Gap050()
    {
      _sut.SelectedProfile = new VoiceProfile { Id = "p1", Name = "P" };
      _sut.Emotion = "not-a-canonical-preset";

      Assert.IsNull(_sut.Emotion);
    }

    [TestMethod]
    public async Task RestoreStateAsync_InvalidEmotionKey_NormalizesToNull_AfterProfilesLoaded_Gap050()
    {
      _mockProfilesClient
          .Setup(x => x.GetProfilesAsync(It.IsAny<CancellationToken>()))
          .ReturnsAsync(new List<VoiceProfile> { new() { Id = "p1", Name = "P1" } });
      _mockEnginesClient
          .Setup(x => x.GetEnginesAsync(It.IsAny<CancellationToken>()))
          .ReturnsAsync(new List<string> { "xtts" });

      var loadCmd = (IAsyncRelayCommand)_sut.LoadProfilesCommand;
      await loadCmd.ExecuteAsync(default);

      var state = new PanelStateData
      {
        PanelId = PanelIds.VoiceSynthesis,
        SelectedItemId = "p1",
        CustomData = new Dictionary<string, object>
        {
          ["VoiceSynthesis_EmotionPreset"] = "totally_invalid",
          ["VoiceSynthesis_SelectedEngine"] = "xtts",
        },
      };

      await _sut.RestoreStateAsync(state, default);

      Assert.AreEqual("p1", _sut.SelectedProfile?.Id);
      Assert.IsNull(_sut.Emotion);
    }

    [TestMethod]
    public async Task RestoreStateAsync_ValidCanonicalEmotion_Restored_AfterProfilesLoaded_Gap050()
    {
      _mockProfilesClient
          .Setup(x => x.GetProfilesAsync(It.IsAny<CancellationToken>()))
          .ReturnsAsync(new List<VoiceProfile> { new() { Id = "p1", Name = "P1" } });
      _mockEnginesClient
          .Setup(x => x.GetEnginesAsync(It.IsAny<CancellationToken>()))
          .ReturnsAsync(new List<string> { "xtts", "piper" });

      var loadCmd = (IAsyncRelayCommand)_sut.LoadProfilesCommand;
      await loadCmd.ExecuteAsync(default);

      var state = new PanelStateData
      {
        PanelId = PanelIds.VoiceSynthesis,
        SelectedItemId = "p1",
        CustomData = new Dictionary<string, object>
        {
          ["VoiceSynthesis_EmotionPreset"] = "CALM",
          ["VoiceSynthesis_SelectedEngine"] = "piper",
        },
      };

      await _sut.RestoreStateAsync(state, default);

      Assert.AreEqual("p1", _sut.SelectedProfile?.Id);
      Assert.AreEqual("calm", _sut.Emotion);
      Assert.AreEqual("piper", _sut.SelectedEngine);
    }

    [TestMethod]
    public async Task SynthesizeCommand_SecondRun_AfterWarning_ShowsWarningAgain_NoStaleError_Gap050()
    {
      _sut.SelectedProfile = new VoiceProfile { Id = "p1", Name = "P" };
      _sut.Text = "Hello";

      var response = new VoiceSynthesisResponse
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
      };

      _mockVoiceSynthesisService
          .Setup(x => x.SynthesizeVoiceAsync(It.IsAny<VoiceSynthesisRequest>(), It.IsAny<CancellationToken>()))
          .ReturnsAsync(response);

      var cmd = (IAsyncRelayCommand)_sut.SynthesizeCommand;
      await cmd.ExecuteAsync(default);
      Assert.IsFalse(_sut.HasError);
      await cmd.ExecuteAsync(default);
      Assert.IsFalse(_sut.HasError);

      _mockToast.Verify(x => x.ShowWarning(It.IsAny<string>(), It.IsAny<string?>()), Times.Exactly(2));
    }

    [TestMethod]
    public async Task SynthesizeCommand_AfterFailureThenSuccess_ClearsErrorState_Gap050()
    {
      _sut.SelectedProfile = new VoiceProfile { Id = "p1", Name = "P" };
      _sut.Text = "Hello";

      var success = new VoiceSynthesisResponse
      {
        AudioId = "a1",
        AudioUrl = "/api/audio/a1",
        Duration = 1.0,
        QualityScore = 0.9,
      };

      var call = 0;
      _mockVoiceSynthesisService
          .Setup(x => x.SynthesizeVoiceAsync(It.IsAny<VoiceSynthesisRequest>(), It.IsAny<CancellationToken>()))
          .Returns(() =>
          {
            call++;
            if (call == 1)
            {
              return Task.FromException<VoiceSynthesisResponse>(new HttpRequestException("network fail"));
            }

            return Task.FromResult(success);
          });

      var cmd = (IAsyncRelayCommand)_sut.SynthesizeCommand;
      await cmd.ExecuteAsync(default);
      Assert.IsTrue(_sut.HasError);
      Assert.IsFalse(string.IsNullOrEmpty(_sut.ErrorMessage));

      await cmd.ExecuteAsync(default);
      Assert.IsFalse(_sut.HasError);
      Assert.IsNull(_sut.ErrorMessage);
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

    [TestMethod]
    public void PlayAudio_BeforeSynthesis_IsDisabled()
    {
      Assert.IsFalse(_sut.CanPlayAudio);
      Assert.AreNotEqual(SynthesisWorkflowState.AudioReady, _sut.WorkflowState);
    }

    [TestMethod]
    public async Task PlayAudio_AfterSuccessfulSynthesis_IsEnabled()
    {
      _sut.SelectedProfile = new VoiceProfile { Id = "p1", Name = "P" };
      _sut.Text = "Hello";
      _mockVoiceSynthesisService
          .Setup(x => x.SynthesizeVoiceAsync(It.IsAny<VoiceSynthesisRequest>(), It.IsAny<CancellationToken>()))
          .ReturnsAsync(new VoiceSynthesisResponse
          {
            AudioId = "a1",
            AudioUrl = "/api/audio/a1",
            Duration = 1.0,
            QualityScore = 0.9,
          });

      var cmd = (IAsyncRelayCommand)_sut.SynthesizeCommand;
      await cmd.ExecuteAsync(default);

      Assert.IsTrue(_sut.CanPlayAudio);
      Assert.AreEqual(SynthesisWorkflowState.AudioReady, _sut.WorkflowState);
    }

    [TestMethod]
    public async Task SynthesizeAsync_Success_StoresAudioIdAndUrl()
    {
      _sut.SelectedProfile = new VoiceProfile { Id = "p1", Name = "P" };
      _sut.Text = "Hello";
      _mockVoiceSynthesisService
          .Setup(x => x.SynthesizeVoiceAsync(It.IsAny<VoiceSynthesisRequest>(), It.IsAny<CancellationToken>()))
          .ReturnsAsync(new VoiceSynthesisResponse
          {
            AudioId = "a1",
            AudioUrl = "/api/audio/a1",
            Duration = 1.0,
            QualityScore = 0.9,
          });

      var cmd = (IAsyncRelayCommand)_sut.SynthesizeCommand;
      await cmd.ExecuteAsync(default);

      Assert.AreEqual("a1", _sut.LastSynthesizedAudioId);
      Assert.IsFalse(string.IsNullOrEmpty(_sut.LastSynthesizedAudioUrl));
    }

    [TestMethod]
    public async Task SynthesizeAsync_Success_AudioIdOnly_SetsAudioReadyAndCanPlay()
    {
      _sut.SelectedProfile = new VoiceProfile { Id = "p1", Name = "P" };
      _sut.Text = "Hello";
      _mockVoiceSynthesisService
          .Setup(x => x.SynthesizeVoiceAsync(It.IsAny<VoiceSynthesisRequest>(), It.IsAny<CancellationToken>()))
          .ReturnsAsync(new VoiceSynthesisResponse
          {
            AudioId = "audio-by-id-only",
            AudioUrl = null,
            Duration = 1.0,
            QualityScore = 0.9,
          });

      var cmd = (IAsyncRelayCommand)_sut.SynthesizeCommand;
      await cmd.ExecuteAsync(default);

      Assert.AreEqual(SynthesisWorkflowState.AudioReady, _sut.WorkflowState);
      Assert.IsTrue(_sut.CanPlayAudio);
      Assert.AreEqual("audio-by-id-only", _sut.LastSynthesizedAudioId);
    }

    [TestMethod]
    public async Task PlayAudioCommand_AfterSynthesis_CallsAudioPlayerService()
    {
      _sut.SelectedProfile = new VoiceProfile { Id = "p1", Name = "P" };
      _sut.Text = "Hello";
      _mockVoiceSynthesisService
          .Setup(x => x.SynthesizeVoiceAsync(It.IsAny<VoiceSynthesisRequest>(), It.IsAny<CancellationToken>()))
          .ReturnsAsync(new VoiceSynthesisResponse
          {
            AudioId = "a1",
            AudioUrl = "/api/audio/a1",
            Duration = 1.0,
            QualityScore = 0.9,
          });
      _mockVoiceSynthesisService
          .Setup(x => x.GetAudioStreamAsync("a1", It.IsAny<CancellationToken>()))
          .ReturnsAsync(new MemoryStream(new byte[] { 1, 2, 3, 4 }));
      _mockAudioPlayer
          .Setup(x => x.PlayFileAsync(It.IsAny<string>(), It.IsAny<Action?>()))
          .Returns(Task.CompletedTask);

      var syn = (IAsyncRelayCommand)_sut.SynthesizeCommand;
      await syn.ExecuteAsync(default);

      var play = (IAsyncRelayCommand)_sut.PlayAudioCommand;
      await play.ExecuteAsync(default);

      _mockAudioPlayer.Verify(
          x => x.PlayFileAsync(It.IsAny<string>(), It.IsAny<Action?>()),
          Times.Once);
    }

    [TestMethod]
    public async Task SynthesizeAsync_ConsentRequiredError_ProducesActionableMessage()
    {
      _sut.SelectedProfile = new VoiceProfile { Id = "p1", Name = "P" };
      _sut.Text = "Hello";
      _mockVoiceSynthesisService
          .Setup(x => x.SynthesizeVoiceAsync(It.IsAny<VoiceSynthesisRequest>(), It.IsAny<CancellationToken>()))
          .ThrowsAsync(new ConsentRequiredException("Consent required for this voice profile."));

      var cmd = (IAsyncRelayCommand)_sut.SynthesizeCommand;
      await cmd.ExecuteAsync(default);

      Assert.IsTrue(_sut.HasError);
      Assert.IsTrue(_sut.IsConsentRequired);
      Assert.AreEqual("p1", _sut.ConsentRequiredProfileId);
      Assert.IsFalse(string.IsNullOrWhiteSpace(_sut.ConsentRequiredMessage));
      Assert.IsFalse(_sut.ShowGenericSynthesisError, "Generic error bar must stay hidden while consent callout is primary.");
      Assert.IsTrue(
          _sut.ErrorMessage?.Contains("consent", StringComparison.OrdinalIgnoreCase) == true,
          "ErrorMessage should mention consent.");
    }

    [TestMethod]
    public async Task SynthesizeAsync_GenericException_SurfacesNonEmptyErrorMessage()
    {
      _sut.SelectedProfile = new VoiceProfile { Id = "p1", Name = "P" };
      _sut.Text = "Hello";
      _mockVoiceSynthesisService
          .Setup(x => x.SynthesizeVoiceAsync(It.IsAny<VoiceSynthesisRequest>(), It.IsAny<CancellationToken>()))
          .ThrowsAsync(new HttpRequestException("Network error"));

      var cmd = (IAsyncRelayCommand)_sut.SynthesizeCommand;
      await cmd.ExecuteAsync(default);

      Assert.IsTrue(_sut.HasError);
      Assert.IsFalse(string.IsNullOrWhiteSpace(_sut.ErrorMessage));
    }

    [TestMethod]
    public void CanSynthesize_WithPiperEngineAndValidProfile_ReturnsTrue()
    {
      _sut.SelectedEngine = "piper";
      _sut.SelectedProfile = new VoiceProfile { Id = "p1", Name = "P" };
      _sut.Text = "non-empty";
      _sut.IsLoading = false;

      Assert.IsTrue(_sut.CanSynthesize);
    }

    [TestMethod]
    public async Task SynthesizeAsync_AuthorizationFailed403_DoesNotSetConsentState()
    {
      _sut.SelectedProfile = new VoiceProfile { Id = "p1", Name = "P" };
      _sut.Text = "Hello";
      _mockVoiceSynthesisService
          .Setup(x => x.SynthesizeVoiceAsync(It.IsAny<VoiceSynthesisRequest>(), It.IsAny<CancellationToken>()))
          .ThrowsAsync(new BackendException("Policy denied", 403, "AUTHORIZATION_FAILED", false));

      var cmd = (IAsyncRelayCommand)_sut.SynthesizeCommand;
      await cmd.ExecuteAsync(default);

      Assert.IsTrue(_sut.HasError);
      Assert.IsFalse(_sut.IsConsentRequired);
      Assert.IsTrue(_sut.ShowGenericSynthesisError);
    }

    [TestMethod]
    public void RetrySynthesisCommand_CannotExecute_WhenConsentNotRequired()
    {
      Assert.IsFalse(_sut.RetrySynthesisCommand.CanExecute(null));
    }

    [TestMethod]
    public void OpenProfileConsentCommand_CannotExecute_WhenConsentNotRequired()
    {
      Assert.IsFalse(_sut.OpenProfileConsentCommand.CanExecute(null));
    }

    [TestMethod]
    public void ClearError_ClearsConsentRequiredState()
    {
      _sut.HasError = true;
      _sut.ErrorMessage = "err";
      _sut.IsConsentRequired = true;
      _sut.ConsentRequiredProfileId = "p1";
      _sut.ConsentRequiredMessage = "detail";

      if (_sut.ClearErrorCommand.CanExecute(null))
        _sut.ClearErrorCommand.Execute(null);

      Assert.IsFalse(_sut.IsConsentRequired);
    }

    [TestMethod]
    public async Task SelectedProfileChanged_ClearsConsentRequiredState_AfterConsentFailure()
    {
      _sut.Profiles = new System.Collections.ObjectModel.ObservableCollection<VoiceProfile>
      {
        new VoiceProfile { Id = "a", Name = "A" },
        new VoiceProfile { Id = "b", Name = "B" },
      };
      _sut.Text = "Hello";
      _sut.SelectedProfile = _sut.Profiles[0];
      _mockVoiceSynthesisService
          .Setup(x => x.SynthesizeVoiceAsync(It.IsAny<VoiceSynthesisRequest>(), It.IsAny<CancellationToken>()))
          .ThrowsAsync(new ConsentRequiredException("Need consent"));
      await ((IAsyncRelayCommand)_sut.SynthesizeCommand).ExecuteAsync(default);
      Assert.IsTrue(_sut.IsConsentRequired);

      _sut.SelectedProfile = _sut.Profiles[1];

      Assert.IsFalse(_sut.IsConsentRequired);
    }

    [TestMethod]
    public async Task RetrySynthesis_AfterConsent_Failure_Then_Success_ClearsConsentState()
    {
      _sut.SelectedProfile = new VoiceProfile { Id = "p1", Name = "P" };
      _sut.Text = "Hello";
      _mockVoiceSynthesisService
          .SetupSequence(x => x.SynthesizeVoiceAsync(It.IsAny<VoiceSynthesisRequest>(), It.IsAny<CancellationToken>()))
          .ThrowsAsync(new ConsentRequiredException("c"))
          .ReturnsAsync(new VoiceSynthesisResponse
          {
            AudioId = "a1",
            AudioUrl = "/a",
            Duration = 1.0,
            QualityScore = 0.9,
          });
      var syn = (IAsyncRelayCommand)_sut.SynthesizeCommand;
      await syn.ExecuteAsync(default);
      Assert.IsTrue(_sut.IsConsentRequired);
      var retry = (IAsyncRelayCommand)_sut.RetrySynthesisCommand;
      Assert.IsTrue(retry.CanExecute(null));
      await retry.ExecuteAsync(default);
      Assert.IsFalse(_sut.IsConsentRequired);
      Assert.AreEqual(SynthesisWorkflowState.AudioReady, _sut.WorkflowState);
    }

    [TestMethod]
    public async Task SynthesizeAsync_WhenNotConsented_DoesNotEnablePlay()
    {
      _sut.SelectedProfile = new VoiceProfile { Id = "p1", Name = "P" };
      _sut.Text = "Hello";
      _mockVoiceSynthesisService
          .Setup(x => x.SynthesizeVoiceAsync(It.IsAny<VoiceSynthesisRequest>(), It.IsAny<CancellationToken>()))
          .ThrowsAsync(new ConsentRequiredException("Consent required"));

      var cmd = (IAsyncRelayCommand)_sut.SynthesizeCommand;
      await cmd.ExecuteAsync(default);

      Assert.AreNotEqual(SynthesisWorkflowState.AudioReady, _sut.WorkflowState);
      Assert.IsFalse(_sut.CanPlayAudio);
    }

    [TestMethod]
    public async Task SynthesizeAsync_Success_WithAudioIdAndUrl_SetsSynthesisResult()
    {
      await RunSuccessfulSynthesisAsync("audio-result-1", "/api/audio/audio-result-1");

      Assert.IsTrue(_sut.HasSynthesisResult);
      Assert.IsTrue(_sut.CanCopyAudioId);
      Assert.IsTrue(_sut.CanCopyAudioReference);
    }

    [TestMethod]
    public async Task SynthesisResultSummary_AfterSuccess_IncludesAudioIdAndReference()
    {
      await RunSuccessfulSynthesisAsync("audio-summary-1", "/api/audio/audio-summary-1");

      StringAssert.Contains(_sut.SynthesisResultSummary, "audio-summary-1");
      StringAssert.Contains(_sut.SynthesisResultSummary, "/api/audio/audio-summary-1");
    }

    [TestMethod]
    public void CopyAudioIdCommand_BeforeSynthesis_IsDisabled()
    {
      Assert.IsFalse(_sut.CopyAudioIdCommand.CanExecute(null));
      Assert.IsFalse(_sut.CanCopyAudioId);
    }

    [TestMethod]
    public async Task CopyAudioIdCommand_AfterAudioIdResult_IsEnabled()
    {
      await RunSuccessfulSynthesisAsync("copy-id-1", "/api/audio/copy-id-1");

      Assert.IsTrue(_sut.CopyAudioIdCommand.CanExecute(null));
      Assert.IsTrue(_sut.CanCopyAudioId);
    }

    [TestMethod]
    public async Task CopyAudioReferenceCommand_WhenAudioUrlExists_IsEnabled()
    {
      await RunSuccessfulSynthesisAsync("copy-ref-1", "/api/audio/copy-ref-1");

      Assert.IsTrue(_sut.CopyAudioReferenceCommand.CanExecute(null));
      Assert.IsTrue(_sut.CanCopyAudioReference);
    }

    [TestMethod]
    public async Task SynthesizeAsync_AudioIdOnlyResult_EnablesCopyIdAndPlay()
    {
      await RunSuccessfulSynthesisAsync("audio-id-only-result", string.Empty);

      Assert.IsTrue(_sut.HasSynthesisResult);
      Assert.IsTrue(_sut.CanCopyAudioId);
      Assert.IsFalse(_sut.CanCopyAudioReference);
      Assert.IsTrue(_sut.CanPlayAudio);
    }

    [TestMethod]
    public async Task SynthesizeAsync_UrlOnlyResult_EnablesCopyReferenceAndPlay()
    {
      await RunSuccessfulSynthesisAsync(string.Empty, "/api/audio/url-only-result");

      Assert.IsTrue(_sut.HasSynthesisResult);
      Assert.IsFalse(_sut.CanCopyAudioId);
      Assert.IsTrue(_sut.CanCopyAudioReference);
      Assert.IsTrue(_sut.CanPlayAudio);
    }

    [TestMethod]
    public async Task SynthesizeAsync_ErrorAfterPriorSuccess_ClearsStaleResultAffordances()
    {
      _sut.SelectedProfile = new VoiceProfile { Id = "p1", Name = "P" };
      _sut.Text = "Hello";
      _mockVoiceSynthesisService
          .SetupSequence(x => x.SynthesizeVoiceAsync(It.IsAny<VoiceSynthesisRequest>(), It.IsAny<CancellationToken>()))
          .ReturnsAsync(new VoiceSynthesisResponse
          {
            AudioId = "stale-audio",
            AudioUrl = "/api/audio/stale-audio",
            Duration = 1.0,
            QualityScore = 0.9,
          })
          .ThrowsAsync(new HttpRequestException("network fail"));

      await ((IAsyncRelayCommand)_sut.SynthesizeCommand).ExecuteAsync(default);
      Assert.IsTrue(_sut.HasSynthesisResult);

      await ((IAsyncRelayCommand)_sut.SynthesizeCommand).ExecuteAsync(default);

      Assert.IsTrue(_sut.HasError);
      Assert.IsFalse(_sut.HasSynthesisResult);
      Assert.IsFalse(_sut.CanCopyAudioId);
      Assert.IsFalse(_sut.CanCopyAudioReference);
      Assert.IsFalse(_sut.OpenOutputLocationCommand.CanExecute(null));
    }

    [DataTestMethod]
    [DataRow("https://localhost/api/audio/a1")]
    [DataRow("http://localhost/api/audio/a1")]
    [DataRow("/api/audio/a1")]
    public async Task OpenOutputLocationCommand_ForHttpOrApiReference_IsDisabled(string audioReference)
    {
      await RunSuccessfulSynthesisAsync("a1", audioReference);

      Assert.IsFalse(_sut.CanOpenOutputLocation);
      Assert.IsFalse(_sut.OpenOutputLocationCommand.CanExecute(null));
    }

    [TestMethod]
    public async Task OpenOutputLocationCommand_ForExistingLocalFile_IsEnabled()
    {
      var path = Path.GetTempFileName();
      try
      {
        await RunSuccessfulSynthesisAsync("local-file-result", path);

        Assert.IsTrue(_sut.CanOpenOutputLocation);
        Assert.IsTrue(_sut.OpenOutputLocationCommand.CanExecute(null));
      }
      finally
      {
        File.Delete(path);
      }
    }

    private async Task RunSuccessfulSynthesisAsync(string audioId, string audioUrl)
    {
      _sut.SelectedProfile = new VoiceProfile { Id = "p1", Name = "P" };
      _sut.Text = "Hello";
      _mockVoiceSynthesisService
          .Setup(x => x.SynthesizeVoiceAsync(It.IsAny<VoiceSynthesisRequest>(), It.IsAny<CancellationToken>()))
          .ReturnsAsync(new VoiceSynthesisResponse
          {
            AudioId = audioId,
            AudioUrl = audioUrl,
            Duration = 1.0,
            QualityScore = 0.9,
          });

      await ((IAsyncRelayCommand)_sut.SynthesizeCommand).ExecuteAsync(default);
    }

    #endregion

    #region Playback diagnostics tests

    private void MockAudioIdPlaybackPipelineThrowsInvalidOp()
    {
      _mockVoiceSynthesisService
          .Setup(x => x.GetAudioStreamAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
          .ReturnsAsync(new MemoryStream(new byte[] { 0x01, 0x02, 0x03 }));

      _mockAudioPlayer
          .Setup(x => x.PlayFileAsync(It.IsAny<string>(), It.IsAny<Action?>()))
          .ThrowsAsync(new InvalidOperationException("Audio device unavailable"));
    }

    [TestMethod]
    public async Task PlaybackFailure_SetsIsPlaybackErrorTrue()
    {
      await RunSuccessfulSynthesisAsync("play-err-1", "/api/audio/play-err-1");
      MockAudioIdPlaybackPipelineThrowsInvalidOp();

      await _sut.PlayAudioCommand.ExecuteAsync(null);

      Assert.IsTrue(_sut.IsPlaybackError);
    }

    [TestMethod]
    public async Task PlaybackFailure_StoresNonEmptyPlaybackErrorMessage()
    {
      await RunSuccessfulSynthesisAsync("play-err-2", "/api/audio/play-err-2");
      MockAudioIdPlaybackPipelineThrowsInvalidOp();

      await _sut.PlayAudioCommand.ExecuteAsync(null);

      Assert.IsFalse(string.IsNullOrWhiteSpace(_sut.PlaybackErrorMessage));
    }

    [TestMethod]
    public async Task PlaybackFailure_PreservesLastSynthesizedAudioIdAndUrl()
    {
      await RunSuccessfulSynthesisAsync("preserved-id", "/api/audio/preserved-id");
      MockAudioIdPlaybackPipelineThrowsInvalidOp();

      await _sut.PlayAudioCommand.ExecuteAsync(null);

      Assert.AreEqual("preserved-id", _sut.LastSynthesizedAudioId);
      StringAssert.Contains(_sut.LastSynthesizedAudioUrl, "preserved-id");
    }

    [TestMethod]
    public async Task PlaybackFailure_PreservesHasSynthesisResultTrue()
    {
      await RunSuccessfulSynthesisAsync("h-sr-1", "/api/audio/h-sr-1");
      MockAudioIdPlaybackPipelineThrowsInvalidOp();

      await _sut.PlayAudioCommand.ExecuteAsync(null);

      Assert.IsTrue(_sut.HasSynthesisResult);
    }

    [TestMethod]
    public void RetryPlaybackCommand_BeforeSynthesis_IsDisabled()
    {
      Assert.IsFalse(_sut.RetryPlaybackCommand.CanExecute(null));
    }

    [TestMethod]
    public async Task RetryPlaybackCommand_AfterSuccessfulSynthesis_IsEnabled()
    {
      await RunSuccessfulSynthesisAsync("retry-en-1", "/api/audio/retry-en-1");
      _mockVoiceSynthesisService
          .Setup(x => x.GetAudioStreamAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
          .ReturnsAsync(new MemoryStream(new byte[] { 0x01 }));
      _mockAudioPlayer
          .Setup(x => x.PlayFileAsync(It.IsAny<string>(), It.IsAny<Action?>()))
          .Returns(Task.CompletedTask);

      Assert.IsTrue(_sut.RetryPlaybackCommand.CanExecute(null));
    }

    [TestMethod]
    public async Task RetryPlaybackCommand_InvokesAudioPlayerAgainAfterPriorFailure()
    {
      await RunSuccessfulSynthesisAsync("invoke-2x", "/api/audio/invoke-2x");
      _mockVoiceSynthesisService
          .Setup(x => x.GetAudioStreamAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
          .ReturnsAsync(new MemoryStream(new byte[] { 0x01 }));
      _mockAudioPlayer
          .SetupSequence(x => x.PlayFileAsync(It.IsAny<string>(), It.IsAny<Action?>()))
          .ThrowsAsync(new InvalidOperationException("first fail"))
          .Returns(Task.CompletedTask);

      await _sut.PlayAudioCommand.ExecuteAsync(null);
      await _sut.RetryPlaybackCommand.ExecuteAsync(null);

      _mockAudioPlayer.Verify(x => x.PlayFileAsync(It.IsAny<string>(), It.IsAny<Action?>()), Times.Exactly(2));
    }

    [TestMethod]
    public async Task SuccessfulRetry_ClearsPlaybackError()
    {
      await RunSuccessfulSynthesisAsync("clear-pb-1", "/api/audio/clear-pb-1");
      _mockVoiceSynthesisService
          .Setup(x => x.GetAudioStreamAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
          .ReturnsAsync(new MemoryStream(new byte[] { 0x01 }));
      _mockAudioPlayer
          .SetupSequence(x => x.PlayFileAsync(It.IsAny<string>(), It.IsAny<Action?>()))
          .ThrowsAsync(new InvalidOperationException("first fail"))
          .Returns(Task.CompletedTask);

      await _sut.PlayAudioCommand.ExecuteAsync(null);
      Assert.IsTrue(_sut.IsPlaybackError);

      await _sut.RetryPlaybackCommand.ExecuteAsync(null);
      Assert.IsFalse(_sut.IsPlaybackError);
    }

    [TestMethod]
    public async Task SynthesisConsentError_DoesNotSetIsPlaybackError()
    {
      _sut.SelectedProfile = new VoiceProfile { Id = "p1", Name = "P" };
      _sut.Text = "Hello";
      _mockVoiceSynthesisService
          .Setup(x => x.SynthesizeVoiceAsync(It.IsAny<VoiceSynthesisRequest>(), It.IsAny<CancellationToken>()))
          .ThrowsAsync(new ConsentRequiredException("Consent required for this voice profile."));

      await ((IAsyncRelayCommand)_sut.SynthesizeCommand).ExecuteAsync(default);

      Assert.IsTrue(_sut.IsConsentRequired);
      Assert.IsFalse(_sut.IsPlaybackError);
    }

    [TestMethod]
    public void CopyPlaybackErrorCommand_DisabledWhenNoPlaybackError()
    {
      Assert.IsFalse(_sut.CopyPlaybackErrorCommand.CanExecute(null));
    }

    [TestMethod]
    public async Task CopyPlaybackErrorCommand_EnabledAfterPlaybackError()
    {
      await RunSuccessfulSynthesisAsync("cpy-pb-1", "/api/audio/cpy-pb-1");
      MockAudioIdPlaybackPipelineThrowsInvalidOp();
      await _sut.PlayAudioCommand.ExecuteAsync(null);

      Assert.IsTrue(_sut.CopyPlaybackErrorCommand.CanExecute(null));
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
