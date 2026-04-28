using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
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
    private Mock<IGeneratedAudioLibraryService> _mockLibraryService = null!;
    private Mock<IGeneratedAudioTimelineService> _mockTimelineService = null!;
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
      _mockLibraryService = new Mock<IGeneratedAudioLibraryService>();
      _mockTimelineService = new Mock<IGeneratedAudioTimelineService>();
      _mockLibraryService
          .Setup(s => s.SaveAsync(It.IsAny<GeneratedAudioSaveRequest>(), It.IsAny<CancellationToken>()))
          .ReturnsAsync(new GeneratedAudioSaveResult(
              true,
              null,
              GeneratedAudioSaveKind.LibraryBacked,
              null,
              null,
              null,
              null,
              null));

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

      _mockTimelineService
          .Setup(t => t.AddGeneratedClipAsync(It.IsAny<GeneratedAudioTimelineRequest>(), It.IsAny<CancellationToken>()))
          .ReturnsAsync(new GeneratedAudioTimelineResult(
              true,
              GeneratedAudioTimelineKind.ExactAppend,
              null,
              "proj-x",
              "tr-x",
              "clip-x"));

      _sut = new VoiceSynthesisViewModel(
          _mockVoiceSynthesisService.Object,
          _mockEnginesClient.Object,
          _mockQualityPipelineService.Object,
          _mockEnsembleService.Object,
          _mockTextAnalysisService.Object,
          _mockQualityHistoryService.Object,
          _mockProfilesClient.Object,
          _mockAudioPlayer.Object,
          _mockToast.Object,
          _mockLibraryService.Object,
          _mockTimelineService.Object
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
      Assert.IsNotNull(_sut.RestoreRecentResultCommand);
      Assert.IsNotNull(_sut.RemoveRecentResultCommand);
      Assert.IsNotNull(_sut.ClearRecentResultsCommand);
      Assert.IsNotNull(_sut.StopAudioCommand);
      Assert.IsNotNull(_sut.AddGeneratedAudioToTimelineCommand);
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

    #region Profile engine compatibility

    [TestMethod]
    public void ProfileEngineCompatibility_KnownCompatible_AllowsSynthesize()
    {
      _sut.IsLoading = false;
      _sut.IsLongFormRunning = false;
      _sut.SelectedProfile = new VoiceProfile
      {
        Id = "p1",
        Name = "Narrator",
        Tags = new List<string> { "vs:engines:xtts" }
      };
      _sut.SelectedEngine = "xtts";
      _sut.Text = "Hello";

      Assert.IsTrue(_sut.IsProfileEngineCompatibilityKnown);
      Assert.IsTrue(_sut.IsSelectedProfileEngineCompatible);
      Assert.AreEqual(ProfileEngineCompatibilityStatus.Compatible, _sut.SelectedProfileEngineCompatibilityStatus);
      Assert.IsTrue(_sut.CanSynthesize);
    }

    [TestMethod]
    public void ProfileEngineCompatibility_KnownIncompatible_BlocksSynthesize()
    {
      _sut.IsLoading = false;
      _sut.IsLongFormRunning = false;
      _sut.SelectedProfile = new VoiceProfile
      {
        Id = "p1",
        Name = "Narrator",
        Tags = new List<string> { "vs:engines:piper" }
      };
      _sut.SelectedEngine = "xtts";
      _sut.Text = "Hello";

      Assert.IsTrue(_sut.IsProfileEngineCompatibilityKnown);
      Assert.IsFalse(_sut.IsSelectedProfileEngineCompatible);
      Assert.AreEqual(ProfileEngineCompatibilityStatus.Incompatible, _sut.SelectedProfileEngineCompatibilityStatus);
      Assert.IsFalse(_sut.CanSynthesize);
    }

    [TestMethod]
    public void ProfileEngineCompatibility_IncompatibleMessage_ContainsProfileNameAndEngine()
    {
      _sut.SelectedProfile = new VoiceProfile
      {
        Id = "p1",
        Name = "Narrator",
        Tags = new List<string> { "vs:engines:piper" }
      };
      _sut.SelectedEngine = "xtts";

      StringAssert.Contains(_sut.ProfileEngineCompatibilityMessage, "Narrator");
      StringAssert.Contains(_sut.ProfileEngineCompatibilityMessage, "xtts");
    }

    [TestMethod]
    public void ProfileEngineCompatibility_EngineChange_RecomputesAndAllowsSynthesize()
    {
      _sut.IsLoading = false;
      _sut.IsLongFormRunning = false;
      _sut.SelectedProfile = new VoiceProfile
      {
        Id = "p1",
        Name = "Narrator",
        Tags = new List<string> { "vs:engines:piper" }
      };
      _sut.SelectedEngine = "xtts";
      _sut.Text = "Hello";
      Assert.IsFalse(_sut.CanSynthesize);

      _sut.SelectedEngine = "piper";

      Assert.IsTrue(_sut.IsSelectedProfileEngineCompatible);
      Assert.IsTrue(_sut.CanSynthesize);
    }

    [TestMethod]
    public void ProfileEngineCompatibility_ProfileChange_Recomputes()
    {
      _sut.IsLoading = false;
      _sut.SelectedEngine = "xtts";
      _sut.Text = "Hello";

      _sut.SelectedProfile = new VoiceProfile
      {
        Id = "p1",
        Name = "A",
        Tags = new List<string> { "vs:engines:piper" }
      };
      Assert.IsFalse(_sut.IsSelectedProfileEngineCompatible);

      _sut.SelectedProfile = new VoiceProfile
      {
        Id = "p2",
        Name = "B",
        Tags = new List<string> { "vs:engines:xtts" }
      };
      Assert.IsTrue(_sut.IsSelectedProfileEngineCompatible);
    }

    [TestMethod]
    public void ProfileEngineCompatibility_UnknownTag_DoesNotBlockSynthesize()
    {
      _sut.IsLoading = false;
      _sut.IsLongFormRunning = false;
      _sut.SelectedProfile = new VoiceProfile
      {
        Id = "p1",
        Name = "Plain",
        Tags = new List<string> { "audiobook" }
      };
      _sut.SelectedEngine = "anything";
      _sut.Text = "Hello";

      Assert.IsFalse(_sut.IsProfileEngineCompatibilityKnown);
      Assert.IsTrue(_sut.IsSelectedProfileEngineCompatible);
      Assert.IsTrue(_sut.CanSynthesize);
    }

    [TestMethod]
    public async Task ProfileEngineCompatibility_DoesNotClearRecentResults_WhenBecomingIncompatible()
    {
      await RunSuccessfulSynthesisAsync("keep-recent", "/api/audio/keep-recent");
      Assert.IsFalse(string.IsNullOrEmpty(_sut.LastSynthesizedAudioId));
      var recentCount = _sut.RecentSynthesisResults.Count;

      _sut.SelectedProfile = new VoiceProfile
      {
        Id = "p2",
        Name = "Restricted",
        Tags = new List<string> { "vs:engines:piper" }
      };
      _sut.SelectedEngine = "xtts";

      Assert.AreEqual("keep-recent", _sut.LastSynthesizedAudioId);
      Assert.AreEqual(recentCount, _sut.RecentSynthesisResults.Count);
    }

    [TestMethod]
    public async Task ProfileEngineCompatibility_ConsentRequired_RemainsIndependentOfCompatibility()
    {
      _sut.SelectedProfile = new VoiceProfile
      {
        Id = "p1",
        Name = "NeedsConsent",
        Tags = new List<string> { "vs:engines:xtts" }
      };
      _sut.SelectedEngine = "xtts";
      _sut.Text = "Hello";
      _mockVoiceSynthesisService
          .Setup(x => x.SynthesizeVoiceAsync(It.IsAny<VoiceSynthesisRequest>(), It.IsAny<CancellationToken>()))
          .ThrowsAsync(new ConsentRequiredException("Consent required for this voice profile."));

      await ((IAsyncRelayCommand)_sut.SynthesizeCommand).ExecuteAsync(default);

      Assert.IsTrue(_sut.IsConsentRequired);
      Assert.IsTrue(_sut.IsProfileEngineCompatibilityKnown);
      Assert.IsTrue(_sut.IsSelectedProfileEngineCompatible);

      _sut.SelectedEngine = "piper";

      Assert.IsTrue(_sut.IsConsentRequired);
      Assert.IsTrue(_sut.IsProfileEngineCompatibilityKnown);
      Assert.IsFalse(_sut.IsSelectedProfileEngineCompatible);
    }

    [TestMethod]
    public void ProfileEngineCompatibility_SelectFirstCompatible_SelectsFirstMatchingInProfilesOrder()
    {
      _sut.Profiles.Clear();
      var firstCompat = new VoiceProfile { Id = "c1", Name = "C1", Tags = new List<string> { "vs:engines:xtts" } };
      var secondCompat = new VoiceProfile { Id = "c2", Name = "C2", Tags = new List<string> { "vs:engines:xtts" } };
      _sut.Profiles.Add(firstCompat);
      _sut.Profiles.Add(secondCompat);

      _sut.SelectedEngine = "xtts";
      _sut.SelectedProfile = secondCompat;
      _sut.Text = "Hello";

      Assert.IsTrue(_sut.SelectFirstCompatibleProfileCommand.CanExecute(null));
      _sut.SelectFirstCompatibleProfileCommand.Execute(null);

      Assert.AreSame(firstCompat, _sut.SelectedProfile);
    }

    [TestMethod]
    public void ProfileEngineCompatibility_NoCompatibleProfiles_CommandCannotExecute()
    {
      _sut.Profiles.Clear();
      _sut.Profiles.Add(new VoiceProfile { Id = "a", Name = "A", Tags = new List<string> { "vs:engines:piper" } });
      _sut.Profiles.Add(new VoiceProfile { Id = "b", Name = "B", Tags = new List<string> { "vs:engines:piper" } });

      _sut.SelectedEngine = "xtts";
      _sut.SelectedProfile = _sut.Profiles[0];
      _sut.Text = "Hello";

      Assert.IsFalse(_sut.HasCompatibleProfilesForSelectedEngine);
      Assert.IsFalse(_sut.SelectFirstCompatibleProfileCommand.CanExecute(null));
    }

    [TestMethod]
    public void ProfilePicker_Default_IncludesUnrestrictedProfiles_AndExcludesKnownIncompatible()
    {
      _sut.Profiles.Clear();
      var unrestricted = new VoiceProfile { Id = "u", Name = "Free", Tags = new List<string> { "audiobook" } };
      var bad = new VoiceProfile { Id = "b", Name = "Bad", Tags = new List<string> { "vs:engines:piper" } };
      var good = new VoiceProfile { Id = "g", Name = "Good", Tags = new List<string> { "vs:engines:xtts" } };
      _sut.Profiles.Add(unrestricted);
      _sut.Profiles.Add(bad);
      _sut.Profiles.Add(good);

      _sut.SelectedEngine = "xtts";
      _sut.ShowCompatibleProfilesOnly = false;

      CollectionAssert.AreEqual(
          new[] { unrestricted, good },
          _sut.ProfilePickerProfiles.ToList());
      Assert.AreEqual(2, _sut.ProfilePickerProfiles.Count);
      Assert.IsTrue(_sut.ProfilePickerSummary.Contains("incompatible"));
      Assert.AreEqual(1, _sut.IncompatibleProfileCount);
      Assert.AreEqual(1, _sut.CompatibleProfileCount);
      Assert.AreEqual(1, _sut.UnrestrictedProfileCount);
    }

    [TestMethod]
    public void ProfilePicker_CompatibleOnly_ShowsOnlyMatchingAllowList_HidesUnrestricted()
    {
      _sut.Profiles.Clear();
      var unrestricted = new VoiceProfile { Id = "u", Name = "Free", Tags = new List<string>() };
      var good = new VoiceProfile { Id = "g", Name = "Good", Tags = new List<string> { "vs:engines:xtts" } };
      _sut.Profiles.Add(unrestricted);
      _sut.Profiles.Add(good);
      _sut.SelectedEngine = "xtts";
      _sut.ShowCompatibleProfilesOnly = true;

      CollectionAssert.AreEqual(new[] { good }, _sut.ProfilePickerProfiles.ToList());
      StringAssert.Contains(_sut.ProfilePickerSummary, "Compatible only");
    }

    [TestMethod]
    public void ProfilePicker_EngineChange_RecomputesPickerAndCounts()
    {
      _sut.Profiles.Clear();
      _sut.Profiles.Add(new VoiceProfile { Id = "g", Name = "G", Tags = new List<string> { "vs:engines:piper" } });
      _sut.SelectedEngine = "xtts";
      _sut.ShowCompatibleProfilesOnly = false;

      Assert.AreEqual(0, _sut.ProfilePickerProfiles.Count);
      Assert.AreEqual(1, _sut.IncompatibleProfileCount);

      _sut.SelectedEngine = "piper";
      Assert.AreEqual(1, _sut.ProfilePickerProfiles.Count);
      Assert.AreEqual(1, _sut.CompatibleProfileCount);
    }

    [TestMethod]
    public void ProfilePicker_WhenSelectedWasIncompatible_AlignsToFirstVisible()
    {
      _sut.Profiles.Clear();
      var bad = new VoiceProfile { Id = "b", Name = "Bad", Tags = new List<string> { "vs:engines:piper" } };
      var good = new VoiceProfile { Id = "g", Name = "Good", Tags = new List<string> { "vs:engines:xtts" } };
      _sut.Profiles.Add(bad);
      _sut.Profiles.Add(good);
      _sut.SelectedEngine = "xtts";
      _sut.SelectedProfile = bad;
      _sut.Text = "hi";

      Assert.AreSame(good, _sut.SelectedProfile);
    }

    [TestMethod]
    public void ProfilePicker_DoesNotMutateProfileTags()
    {
      _sut.Profiles.Clear();
      var p = new VoiceProfile { Id = "p", Name = "P", Tags = new List<string> { "vs:engines:xtts", "k=v" } };
      _sut.Profiles.Add(p);
      _sut.SelectedEngine = "xtts";
      _sut.ShowCompatibleProfilesOnly = true;
      _ = _sut.ProfilePickerSummary;

      Assert.AreEqual(2, p.Tags.Count);
      Assert.AreEqual("vs:engines:xtts", p.Tags[0]);
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

    #region Recent results tests

    private async Task RunMultipleSynthesesAsync(int count)
    {
      for (var i = 0; i < count; i++)
        await RunSuccessfulSynthesisAsync($"id-{i}", $"/api/audio/{i}");
    }

    [TestMethod]
    public async Task SuccessfulSynthesis_AddsOneRecentResult()
    {
      await RunSuccessfulSynthesisAsync("r1", "/api/audio/r1");

      Assert.AreEqual(1, _sut.RecentSynthesisResults.Count);
    }

    [TestMethod]
    public async Task FailedSynthesis_DoesNotAddRecentResult()
    {
      _sut.SelectedProfile = new VoiceProfile { Id = "p1", Name = "P" };
      _sut.Text = "Hello";
      _mockVoiceSynthesisService
          .Setup(x => x.SynthesizeVoiceAsync(It.IsAny<VoiceSynthesisRequest>(), It.IsAny<CancellationToken>()))
          .ThrowsAsync(new InvalidOperationException("Synthesis failed"));

      await ((IAsyncRelayCommand)_sut.SynthesizeCommand).ExecuteAsync(default);

      Assert.AreEqual(0, _sut.RecentSynthesisResults.Count);
    }

    [TestMethod]
    public async Task RecentResults_AreNewestFirst()
    {
      await RunSuccessfulSynthesisAsync("first", "/api/audio/first");
      await RunSuccessfulSynthesisAsync("second", "/api/audio/second");

      Assert.AreEqual("second", _sut.RecentSynthesisResults[0].AudioId);
      Assert.AreEqual("first", _sut.RecentSynthesisResults[1].AudioId);
    }

    [TestMethod]
    public async Task RecentResults_TrimToFive()
    {
      await RunMultipleSynthesesAsync(6);

      Assert.AreEqual(5, _sut.RecentSynthesisResults.Count);
      Assert.AreEqual("id-5", _sut.RecentSynthesisResults[0].AudioId);
      Assert.AreEqual("id-1", _sut.RecentSynthesisResults[4].AudioId);
    }

    [TestMethod]
    public async Task RecentResult_CapturesAudioIdAndReference()
    {
      await RunSuccessfulSynthesisAsync("cap-id", "/api/audio/cap-ref");

      Assert.AreEqual("cap-id", _sut.RecentSynthesisResults[0].AudioId);
      Assert.AreEqual("/api/audio/cap-ref", _sut.RecentSynthesisResults[0].AudioReference);
    }

    [TestMethod]
    public async Task RecentResult_CapturesProfileAndEngine()
    {
      await RunSuccessfulSynthesisAsync("pe-1", "/api/audio/pe-1");

      Assert.AreEqual("P", _sut.RecentSynthesisResults[0].ProfileName);
      Assert.AreEqual("p1", _sut.RecentSynthesisResults[0].ProfileId);
      Assert.AreEqual("xtts", _sut.RecentSynthesisResults[0].Engine);
    }

    [TestMethod]
    public async Task RestoreRecentResult_SetsActiveAudioIdAndUrl()
    {
      await RunSuccessfulSynthesisAsync("a", "/api/audio/a");
      await RunSuccessfulSynthesisAsync("b", "/api/audio/b");
      var older = _sut.RecentSynthesisResults[1];

      _sut.RestoreRecentResultCommand.Execute(older);

      Assert.AreEqual("a", _sut.LastSynthesizedAudioId);
      Assert.AreEqual("/api/audio/a", _sut.LastSynthesizedAudioUrl);
    }

    [TestMethod]
    public async Task RestoreRecentResult_SetsAudioReadyAndCanPlay()
    {
      await RunSuccessfulSynthesisAsync("ar-1", "/api/audio/ar-1");
      _sut.WorkflowState = SynthesisWorkflowState.Idle;

      _sut.RestoreRecentResultCommand.Execute(_sut.RecentSynthesisResults[0]);

      Assert.AreEqual(SynthesisWorkflowState.AudioReady, _sut.WorkflowState);
      Assert.IsTrue(_sut.CanPlayAudio);
    }

    [TestMethod]
    public async Task RestoreRecentResult_ClearsPlaybackError()
    {
      await RunSuccessfulSynthesisAsync("pb-1", "/api/audio/pb-1");
      MockAudioIdPlaybackPipelineThrowsInvalidOp();
      await _sut.PlayAudioCommand.ExecuteAsync(null);
      Assert.IsTrue(_sut.IsPlaybackError);

      _sut.RestoreRecentResultCommand.Execute(_sut.RecentSynthesisResults[0]);

      Assert.IsFalse(_sut.IsPlaybackError);
    }

    [TestMethod]
    public void RestoreCommand_DisabledForNull()
    {
      Assert.IsFalse(_sut.RestoreRecentResultCommand.CanExecute(null));
    }

    #endregion

    #region Recent results persistence tests

    private const string RecentResultsCustomKey = "VoiceSynthesis_RecentResults";

    private static PanelStateData BuildRecentResultsPanelState(
        params (string? audioId, string? audioRef, double dur, string? engine)[] items)
    {
      var list = new List<Dictionary<string, object?>>();
      var utc = DateTime.UtcNow.ToString("O");
      foreach (var i in items)
      {
        list.Add(new Dictionary<string, object?>
        {
          ["AudioId"] = i.audioId,
          ["AudioReference"] = i.audioRef,
          ["DurationSeconds"] = i.dur,
          ["QualityScore"] = 0.9,
          ["ProfileId"] = "p1",
          ["ProfileName"] = "P",
          ["Engine"] = i.engine,
          ["CreatedAtUtc"] = utc,
        });
      }
      return new PanelStateData
      {
        PanelId = PanelIds.VoiceSynthesis,
        CustomData = new Dictionary<string, object>
        {
          [RecentResultsCustomKey] = JsonSerializer.Serialize(list),
        },
      };
    }

    private static PanelStateData BuildRecentResultsPanelStateRawJson(string json)
    {
      return new PanelStateData
      {
        PanelId = PanelIds.VoiceSynthesis,
        CustomData = new Dictionary<string, object>
        {
          [RecentResultsCustomKey] = json,
        },
      };
    }

    [TestMethod]
    public async Task GetCurrentState_PersistsRecentResults_WhenPresent()
    {
      await RunSuccessfulSynthesisAsync("persist-1", "/api/audio/persist-1");
      var state = _sut.GetCurrentState();
      Assert.IsNotNull(state);
      Assert.IsNotNull(state!.CustomData);
      Assert.IsTrue(state.CustomData.ContainsKey(RecentResultsCustomKey));
      var json = state.CustomData[RecentResultsCustomKey] as string;
      Assert.IsFalse(string.IsNullOrEmpty(json));
      Assert.IsTrue(json!.Contains("persist-1", StringComparison.Ordinal));
    }

    [TestMethod]
    public void GetCurrentState_OmitsRecentResults_WhenEmpty()
    {
      var state = _sut.GetCurrentState();
      Assert.IsNotNull(state);
      Assert.IsNotNull(state!.CustomData);
      Assert.IsFalse(state.CustomData.ContainsKey(RecentResultsCustomKey));
    }

    [TestMethod]
    public async Task RestoreStateAsync_RestoresPersistedRecentResults()
    {
      var panelState = BuildRecentResultsPanelState(
          ("a", "/api/a", 1.0, "xtts"),
          ("b", "/api/b", 2.0, "piper"));
      await _sut.RestoreStateAsync(panelState, default);

      Assert.AreEqual(2, _sut.RecentSynthesisResults.Count);
      Assert.AreEqual("a", _sut.RecentSynthesisResults[0].AudioId);
      Assert.AreEqual("b", _sut.RecentSynthesisResults[1].AudioId);
    }

    [TestMethod]
    public async Task RestoreStateAsync_CapsRestoredResultsToFive()
    {
      var panelState = BuildRecentResultsPanelState(
          ("0", "/api/0", 0, "a"),
          ("1", "/api/1", 0, "a"),
          ("2", "/api/2", 0, "a"),
          ("3", "/api/3", 0, "a"),
          ("4", "/api/4", 0, "a"),
          ("5", "/api/5", 0, "a"),
          ("6", "/api/6", 0, "a"));
      await _sut.RestoreStateAsync(panelState, default);

      Assert.AreEqual(5, _sut.RecentSynthesisResults.Count);
      Assert.AreEqual("0", _sut.RecentSynthesisResults[0].AudioId);
    }

    [TestMethod]
    public async Task RestoreStateAsync_SkipsInvalidRows_NoAudioIdOrReference()
    {
      var list = new List<Dictionary<string, object?>>
      {
        new()
        {
          ["AudioId"] = "",
          ["AudioReference"] = null,
          ["DurationSeconds"] = 0.0,
          ["QualityScore"] = 0.0,
        },
        new()
        {
          ["AudioId"] = "ok",
          ["AudioReference"] = "/ref/ok",
          ["DurationSeconds"] = 1.0,
          ["QualityScore"] = 0.5,
          ["ProfileId"] = "p1",
          ["ProfileName"] = "P",
          ["Engine"] = "e",
          ["CreatedAtUtc"] = DateTime.UtcNow.ToString("O"),
        },
      };
      var raw = JsonSerializer.Serialize(list);
      await _sut.RestoreStateAsync(BuildRecentResultsPanelStateRawJson(raw), default);

      Assert.AreEqual(1, _sut.RecentSynthesisResults.Count);
      Assert.AreEqual("ok", _sut.RecentSynthesisResults[0].AudioId);
    }

    [TestMethod]
    public async Task RestoreStateAsync_MalformedJson_DoesNotThrow()
    {
      var panelState = BuildRecentResultsPanelStateRawJson("not valid json {{{");
      await _sut.RestoreStateAsync(panelState, default);
      Assert.AreEqual(0, _sut.RecentSynthesisResults.Count);
    }

    [TestMethod]
    public async Task RestoreStateAsync_MissingRecentResultsKey_DoesNotThrow()
    {
      var panelState = new PanelStateData
      {
        PanelId = PanelIds.VoiceSynthesis,
        CustomData = new Dictionary<string, object> { { "OtherKey", 1 } },
      };
      await _sut.RestoreStateAsync(panelState, default);
      Assert.AreEqual(0, _sut.RecentSynthesisResults.Count);
    }

    [TestMethod]
    public async Task RestoredRecentResult_CanBeRestoredAsActive()
    {
      await _sut.RestoreStateAsync(
          BuildRecentResultsPanelState(
              ("rid", "/api/rid", 1, "xtts"),
              ("other", "/api/o", 1, "xtts")),
          default);
      var pick = _sut.RecentSynthesisResults[1];
      _sut.RestoreRecentResultCommand.Execute(pick);

      Assert.AreEqual("other", _sut.LastSynthesizedAudioId);
    }

    [TestMethod]
    public async Task RestoredRecentResult_SetsAudioReadyAndCanPlay_FromPersistence()
    {
      await _sut.RestoreStateAsync(
          BuildRecentResultsPanelState(
              ("ra", "/api/ra", 1, "xtts")),
          default);
      _sut.WorkflowState = SynthesisWorkflowState.Idle;

      _sut.RestoreRecentResultCommand.Execute(_sut.RecentSynthesisResults[0]);

      Assert.AreEqual(SynthesisWorkflowState.AudioReady, _sut.WorkflowState);
      Assert.IsTrue(_sut.CanPlayAudio);
    }

    [TestMethod]
    public async Task PlaybackErrorState_NotPersisted()
    {
      await RunSuccessfulSynthesisAsync("pe-err", "/api/pe-err");
      _sut.PlaybackErrorMessage = "E";
      _sut.IsPlaybackError = true;

      var state = _sut.GetCurrentState();
      var json = state?.CustomData?[RecentResultsCustomKey] as string;
      Assert.IsFalse(string.IsNullOrEmpty(json));
      using var doc = JsonDocument.Parse(json!);
      var el0 = doc.RootElement[0];
      Assert.IsFalse(el0.TryGetProperty("IsPlaybackError", out _));
      Assert.IsFalse(el0.TryGetProperty("isPlaybackError", out _));
      Assert.IsTrue(el0.TryGetProperty("AudioId", out var aid));
      Assert.AreEqual("pe-err", aid.GetString());
    }

    #endregion

    #region Recent results management tests

    [TestMethod]
    public async Task RemoveRecentResult_RemovesOneItem_NewestFirst()
    {
      await RunSuccessfulSynthesisAsync("rm-a", "/api/audio/rm-a");
      await RunSuccessfulSynthesisAsync("rm-b", "/api/audio/rm-b");
      var remove = _sut.RecentSynthesisResults[1];

      _sut.RemoveRecentResultCommand.Execute(remove);

      Assert.AreEqual(1, _sut.RecentSynthesisResults.Count);
      Assert.AreEqual("rm-b", _sut.RecentSynthesisResults[0].AudioId);
    }

    [TestMethod]
    public void RemoveRecentResult_UnknownItem_IsNoOp()
    {
      var orphan = new VoiceSynthesisRecentResult
      {
        AudioId = "not-in-list",
        AudioReference = "/x",
      };
      Assert.AreEqual(0, _sut.RecentSynthesisResults.Count);
      _sut.RemoveRecentResultCommand.Execute(orphan);
      Assert.AreEqual(0, _sut.RecentSynthesisResults.Count);
    }

    [TestMethod]
    public async Task RemoveRecentResult_PreservesActiveLastSynthesized_WhenRemovingOther()
    {
      await RunSuccessfulSynthesisAsync("keep-active", "/api/audio/keep-active");
      await RunSuccessfulSynthesisAsync("newer", "/api/audio/newer");
      Assert.AreEqual("newer", _sut.LastSynthesizedAudioId);

      var older = _sut.RecentSynthesisResults[1];
      _sut.RemoveRecentResultCommand.Execute(older);

      Assert.AreEqual("newer", _sut.LastSynthesizedAudioId);
      Assert.AreEqual("/api/audio/newer", _sut.LastSynthesizedAudioUrl);
    }

    [TestMethod]
    public async Task ClearRecentResults_RemovesAllEntries()
    {
      await RunSuccessfulSynthesisAsync("c1", "/api/c1");
      await RunSuccessfulSynthesisAsync("c2", "/api/c2");

      _sut.ClearRecentResultsCommand.Execute(null);

      Assert.AreEqual(0, _sut.RecentSynthesisResults.Count);
      Assert.IsFalse(_sut.HasRecentSynthesisResults);
    }

    [TestMethod]
    public async Task ClearRecentResults_PreservesActiveOutputAndPlaybackError()
    {
      await RunSuccessfulSynthesisAsync("active-pb", "/api/audio/active-pb");
      await RunSuccessfulSynthesisAsync("second", "/api/audio/second");
      MockAudioIdPlaybackPipelineThrowsInvalidOp();
      await _sut.PlayAudioCommand.ExecuteAsync(null);
      Assert.IsTrue(_sut.IsPlaybackError);
      Assert.AreEqual("second", _sut.LastSynthesizedAudioId);

      _sut.ClearRecentResultsCommand.Execute(null);

      Assert.AreEqual("second", _sut.LastSynthesizedAudioId);
      Assert.AreEqual("/api/audio/second", _sut.LastSynthesizedAudioUrl);
      Assert.IsTrue(_sut.IsPlaybackError);
      Assert.IsTrue(_sut.HasSynthesisResult);
    }

    [TestMethod]
    public void ClearRecentResults_CanExecute_FalseWhenListEmpty()
    {
      Assert.IsFalse(_sut.ClearRecentResultsCommand.CanExecute(null));
    }

    [TestMethod]
    public async Task ClearRecentResults_CanExecute_TrueWhenListNonEmpty()
    {
      await RunSuccessfulSynthesisAsync("ce-1", "/api/ce-1");
      Assert.IsTrue(_sut.ClearRecentResultsCommand.CanExecute(null));
    }

    [TestMethod]
    public async Task GetCurrentState_AfterRemove_ExcludesRemovedFromPersistence()
    {
      await RunSuccessfulSynthesisAsync("p-a", "/api/p-a");
      await RunSuccessfulSynthesisAsync("p-b", "/api/p-b");
      _sut.RemoveRecentResultCommand.Execute(_sut.RecentSynthesisResults[1]);

      var state = _sut.GetCurrentState();
      var json = state?.CustomData?[RecentResultsCustomKey] as string;
      Assert.IsFalse(string.IsNullOrEmpty(json));
      using var doc = JsonDocument.Parse(json!);
      Assert.AreEqual(1, doc.RootElement.GetArrayLength());
      Assert.AreEqual("p-b", doc.RootElement[0].GetProperty("AudioId").GetString());
    }

    [TestMethod]
    public async Task GetCurrentState_AfterClear_OmitsRecentResultsKey()
    {
      await RunSuccessfulSynthesisAsync("oc-1", "/api/oc-1");
      _sut.ClearRecentResultsCommand.Execute(null);

      var state = _sut.GetCurrentState();
      Assert.IsNotNull(state?.CustomData);
      Assert.IsFalse(state!.CustomData!.ContainsKey(RecentResultsCustomKey));
    }

    [TestMethod]
    public async Task RestoreState_AfterClearRoundTrip_KeepsRecentsEmpty()
    {
      await RunSuccessfulSynthesisAsync("rt-1", "/api/rt-1");
      _sut.ClearRecentResultsCommand.Execute(null);
      var state = _sut.GetCurrentState();

      await _sut.RestoreStateAsync(state!, default);

      Assert.IsFalse(_sut.HasRecentSynthesisResults);
      Assert.AreEqual(0, _sut.RecentSynthesisResults.Count);
    }

    [TestMethod]
    public async Task RecentResultsManagement_RefreshesHasAndClearCanExecute()
    {
      Assert.IsFalse(_sut.HasRecentSynthesisResults);
      Assert.IsFalse(_sut.ClearRecentResultsCommand.CanExecute(null));

      await RunSuccessfulSynthesisAsync("rf-1", "/api/rf-1");
      Assert.IsTrue(_sut.HasRecentSynthesisResults);
      Assert.IsTrue(_sut.ClearRecentResultsCommand.CanExecute(null));

      _sut.ClearRecentResultsCommand.Execute(null);
      Assert.IsFalse(_sut.HasRecentSynthesisResults);
      Assert.IsFalse(_sut.ClearRecentResultsCommand.CanExecute(null));
    }

    [TestMethod]
    public void RemoveRecentResultCommand_CanExecute_FalseForNull()
    {
      Assert.IsFalse(_sut.RemoveRecentResultCommand.CanExecute(null));
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

    #region Library output tests

    [TestMethod]
    public void LibraryOutput_CanAdd_IsFalse_BeforeSynthesis()
    {
      Assert.IsFalse(_sut.CanAddGeneratedAudioToLibrary);
      Assert.IsFalse(_sut.AddGeneratedAudioToLibraryCommand.CanExecute(null));
    }

    [TestMethod]
    public async Task LibraryOutput_CanAdd_IsTrue_AfterSynthesis_WithAudioId()
    {
      await RunSuccessfulSynthesisAsync("lib-id-1", "/api/audio/lib-id-1");

      Assert.IsTrue(_sut.CanAddGeneratedAudioToLibrary);
      Assert.IsTrue(_sut.AddGeneratedAudioToLibraryCommand.CanExecute(null));
    }

    [TestMethod]
    public async Task LibraryOutput_CanAdd_IsTrue_AfterSynthesis_WithAudioReferenceOnly()
    {
      _sut.SelectedProfile = new VoiceProfile { Id = "p1", Name = "P" };
      _sut.Text = "Hello";
      _mockVoiceSynthesisService
          .Setup(x => x.SynthesizeVoiceAsync(It.IsAny<VoiceSynthesisRequest>(), It.IsAny<CancellationToken>()))
          .ReturnsAsync(new VoiceSynthesisResponse
          {
            AudioId = string.Empty,
            AudioUrl = "/api/audio/ref-only",
            Duration = 1.0,
            QualityScore = 0.8,
          });

      await ((IAsyncRelayCommand)_sut.SynthesizeCommand).ExecuteAsync(default);

      Assert.IsTrue(_sut.HasSynthesisResult);
      Assert.IsTrue(_sut.CanAddGeneratedAudioToLibrary);
    }

    [TestMethod]
    public async Task LibraryOutput_SaveAsync_ReceivesExpectedMetadata()
    {
      await RunSuccessfulSynthesisAsync("meta-id", "/api/audio/meta-id");
      _sut.SelectedEngine = "piper";

      await _sut.AddGeneratedAudioToLibraryCommand.ExecuteAsync(default);

      _mockLibraryService.Verify(
          s => s.SaveAsync(
              It.Is<GeneratedAudioSaveRequest>(r =>
                  r.SourcePanelId == PanelIds.VoiceSynthesis &&
                  r.AudioId == "meta-id" &&
                  r.AudioReference == "/api/audio/meta-id" &&
                  r.Engine == "piper" &&
                  r.ProfileId == "p1" &&
                  r.ProfileName == "P"),
              It.IsAny<CancellationToken>()),
          Times.Once);
    }

    [TestMethod]
    public async Task LibraryOutput_SaveSuccess_SetsSavedState()
    {
      await RunSuccessfulSynthesisAsync("saved-1", "/api/audio/saved-1");

      await _sut.AddGeneratedAudioToLibraryCommand.ExecuteAsync(default);

      Assert.IsTrue(_sut.IsGeneratedAudioSaved);
      StringAssert.Contains(_sut.GeneratedAudioSaveStatus, "Saved", StringComparison.OrdinalIgnoreCase);
    }

    [TestMethod]
    public async Task LibraryOutput_EventNotified_StatusDoesNotClaimProjectLibrary()
    {
      _mockLibraryService
          .Setup(s => s.SaveAsync(It.IsAny<GeneratedAudioSaveRequest>(), It.IsAny<CancellationToken>()))
          .ReturnsAsync(new GeneratedAudioSaveResult(
              true,
              null,
              GeneratedAudioSaveKind.EventNotified,
              null,
              null,
              null,
              "Library notified; project-backed save requires a local generated audio file.",
              null));

      await RunSuccessfulSynthesisAsync("ev1", "/api/audio/ev1");
      await _sut.AddGeneratedAudioToLibraryCommand.ExecuteAsync(default);

      Assert.IsTrue(_sut.IsGeneratedAudioSaved);
      StringAssert.Contains(_sut.GeneratedAudioSaveStatus, "Library", StringComparison.OrdinalIgnoreCase);
      Assert.IsFalse(
          _sut.GeneratedAudioSaveStatus.Contains("project library", StringComparison.OrdinalIgnoreCase));
    }

    [TestMethod]
    public async Task LibraryOutput_ProjectBacked_StatusMentionsProjectLibrary()
    {
      _mockLibraryService
          .Setup(s => s.SaveAsync(It.IsAny<GeneratedAudioSaveRequest>(), It.IsAny<CancellationToken>()))
          .ReturnsAsync(new GeneratedAudioSaveResult(
              true,
              null,
              GeneratedAudioSaveKind.ProjectBacked,
              "a1",
              "play1",
              "proj1",
              null,
              @"C:\temp\f.wav"));

      await RunSuccessfulSynthesisAsync("pb1", "/api/audio/pb1");
      await _sut.AddGeneratedAudioToLibraryCommand.ExecuteAsync(default);

      StringAssert.Contains(_sut.GeneratedAudioSaveStatus, "project", StringComparison.OrdinalIgnoreCase);
    }

    [TestMethod]
    public async Task LibraryOutput_SecondSynthesis_ResetsUnsaved()
    {
      await RunSuccessfulSynthesisAsync("first", "/api/audio/first");
      await _sut.AddGeneratedAudioToLibraryCommand.ExecuteAsync(default);
      Assert.IsTrue(_sut.IsGeneratedAudioSaved);

      await RunSuccessfulSynthesisAsync("second", "/api/audio/second");

      Assert.IsFalse(_sut.IsGeneratedAudioSaved);
    }

    [TestMethod]
    public async Task LibraryOutput_SaveFailure_DoesNotMarkSaved()
    {
      _mockLibraryService
          .Setup(s => s.SaveAsync(It.IsAny<GeneratedAudioSaveRequest>(), It.IsAny<CancellationToken>()))
          .ReturnsAsync(new GeneratedAudioSaveResult(false, "svc error"));

      await RunSuccessfulSynthesisAsync("fail-id", "/api/audio/fail-id");
      await _sut.AddGeneratedAudioToLibraryCommand.ExecuteAsync(default);

      Assert.IsFalse(_sut.IsGeneratedAudioSaved);
    }

    [TestMethod]
    public async Task LibraryOutput_SaveFailure_PreservesAudioAndPlayCapability()
    {
      _mockLibraryService
          .Setup(s => s.SaveAsync(It.IsAny<GeneratedAudioSaveRequest>(), It.IsAny<CancellationToken>()))
          .ReturnsAsync(new GeneratedAudioSaveResult(false, "svc error"));

      await RunSuccessfulSynthesisAsync("preserve-id", "/api/audio/preserve-id");
      var idBefore = _sut.LastSynthesizedAudioId;

      await _sut.AddGeneratedAudioToLibraryCommand.ExecuteAsync(default);

      Assert.AreEqual(idBefore, _sut.LastSynthesizedAudioId);
      Assert.IsTrue(_sut.CanPlayAudio);
    }

    [TestMethod]
    public async Task LibraryOutput_SaveSuccess_MarksMatchingRecentResult()
    {
      await RunSuccessfulSynthesisAsync("recent-mark", "/api/audio/recent-mark");
      Assert.AreEqual(1, _sut.RecentSynthesisResults.Count);

      await _sut.AddGeneratedAudioToLibraryCommand.ExecuteAsync(default);

      Assert.IsTrue(_sut.RecentSynthesisResults[0].IsSavedToLibrary);
    }

    [TestMethod]
    public async Task LibraryOutput_RestoreSavedRecent_RestoresVmSavedState()
    {
      await RunSuccessfulSynthesisAsync("restore-saved", "/api/audio/rs");
      await _sut.AddGeneratedAudioToLibraryCommand.ExecuteAsync(default);
      await RunSuccessfulSynthesisAsync("other", "/api/audio/other");

      var savedRow = _sut.RecentSynthesisResults.First(r => r.AudioId == "restore-saved");
      _sut.RestoreRecentResultCommand.Execute(savedRow);

      Assert.IsTrue(_sut.IsGeneratedAudioSaved);
      StringAssert.Contains(_sut.GeneratedAudioSaveStatus, "Previously", StringComparison.OrdinalIgnoreCase);
    }

    [TestMethod]
    public async Task LibraryOutput_Persistence_RoundTripsSavedFlag()
    {
      await RunSuccessfulSynthesisAsync("persist-saved", "/api/audio/ps");
      await _sut.AddGeneratedAudioToLibraryCommand.ExecuteAsync(default);

      var state = _sut.GetCurrentState();
      Assert.IsNotNull(state?.CustomData);
      var json = state!.CustomData![RecentResultsCustomKey] as string;
      Assert.IsFalse(string.IsNullOrEmpty(json));
      StringAssert.Contains(json!, "persist-saved");
      StringAssert.Contains(json!, "IsSavedToLibrary");

      var sut2 = new VoiceSynthesisViewModel(
          _mockVoiceSynthesisService.Object,
          _mockEnginesClient.Object,
          _mockQualityPipelineService.Object,
          _mockEnsembleService.Object,
          _mockTextAnalysisService.Object,
          _mockQualityHistoryService.Object,
          _mockProfilesClient.Object,
          _mockAudioPlayer.Object,
          _mockToast.Object,
          _mockLibraryService.Object,
          _mockTimelineService.Object);
      await sut2.RestoreStateAsync(state!, default);

      Assert.IsTrue(sut2.RecentSynthesisResults[0].IsSavedToLibrary);
      sut2.Dispose();
    }

    [TestMethod]
    public async Task LibraryOutput_ConsentFailure_DoesNotEnableAddToLibrary()
    {
      _sut.SelectedProfile = new VoiceProfile { Id = "p1", Name = "P" };
      _sut.Text = "Hello";
      _mockVoiceSynthesisService
          .Setup(x => x.SynthesizeVoiceAsync(It.IsAny<VoiceSynthesisRequest>(), It.IsAny<CancellationToken>()))
          .ThrowsAsync(new ConsentRequiredException("need consent"));

      await ((IAsyncRelayCommand)_sut.SynthesizeCommand).ExecuteAsync(default);

      Assert.IsFalse(_sut.HasSynthesisResult);
      Assert.IsFalse(_sut.CanAddGeneratedAudioToLibrary);
    }

    [TestMethod]
    public void LibraryOutput_UsesMocksOnly_NoRealBackendOrAudioDevice()
    {
      Assert.IsNotNull(_mockLibraryService);
      Assert.IsNotNull(_mockVoiceSynthesisService);
    }

    [TestMethod]
    public async Task LibraryOutput_WithoutLibraryService_CannotAdd()
    {
      var vm = new VoiceSynthesisViewModel(
          _mockVoiceSynthesisService.Object,
          _mockEnginesClient.Object,
          _mockQualityPipelineService.Object,
          _mockEnsembleService.Object,
          _mockTextAnalysisService.Object,
          _mockQualityHistoryService.Object,
          _mockProfilesClient.Object,
          _mockAudioPlayer.Object,
          _mockToast.Object,
          generatedAudioLibraryService: null,
          generatedAudioTimelineService: null);
      try
      {
        await RunSuccessfulSynthesisOn(vm, "no-lib", "/api/audio/no-lib");

        Assert.IsFalse(vm.CanAddGeneratedAudioToLibrary);
      }
      finally
      {
        vm.Dispose();
      }
    }

    private async Task RunSuccessfulSynthesisOn(VoiceSynthesisViewModel vm, string audioId, string audioUrl)
    {
      vm.SelectedProfile = new VoiceProfile { Id = "p1", Name = "P" };
      vm.Text = "Hello";
      _mockVoiceSynthesisService
          .Setup(x => x.SynthesizeVoiceAsync(It.IsAny<VoiceSynthesisRequest>(), It.IsAny<CancellationToken>()))
          .ReturnsAsync(new VoiceSynthesisResponse
          {
            AudioId = audioId,
            AudioUrl = audioUrl,
            Duration = 1.0,
            QualityScore = 0.9,
          });

      await ((IAsyncRelayCommand)vm.SynthesizeCommand).ExecuteAsync(default);
    }

    #endregion

    #region Timeline output tests

    [TestMethod]
    public void TimelineOutput_BeforeSynthesis_CannotAddToTimeline()
    {
      _sut.SelectedProfile = new VoiceProfile { Id = "p1", Name = "P" };
      Assert.IsFalse(_sut.CanAddGeneratedAudioToTimeline);
    }

    [TestMethod]
    public async Task TimelineOutput_AfterSynthesis_CanAddWhenTimelineServicePresent()
    {
      await RunSuccessfulSynthesisAsync("tl-1", "/api/audio/tl-1");
      Assert.IsTrue(_sut.CanAddGeneratedAudioToTimeline);
    }

    [TestMethod]
    public async Task TimelineOutput_WithoutTimelineService_CannotAdd()
    {
      var vm = new VoiceSynthesisViewModel(
          _mockVoiceSynthesisService.Object,
          _mockEnginesClient.Object,
          _mockQualityPipelineService.Object,
          _mockEnsembleService.Object,
          _mockTextAnalysisService.Object,
          _mockQualityHistoryService.Object,
          _mockProfilesClient.Object,
          _mockAudioPlayer.Object,
          _mockToast.Object,
          _mockLibraryService.Object,
          generatedAudioTimelineService: null);
      try
      {
        await RunSuccessfulSynthesisOn(vm, "no-tl", "/api/audio/no-tl");
        Assert.IsFalse(vm.CanAddGeneratedAudioToTimeline);
      }
      finally
      {
        vm.Dispose();
      }
    }

    [TestMethod]
    public async Task TimelineOutput_Success_CallsServiceAndMarksAdded()
    {
      await RunSuccessfulSynthesisAsync("tl-ok", "/api/audio/tl-ok");

      await ((IAsyncRelayCommand)_sut.AddGeneratedAudioToTimelineCommand).ExecuteAsync(default);

      _mockTimelineService.Verify(
          t => t.AddGeneratedClipAsync(
              It.Is<GeneratedAudioTimelineRequest>(r =>
                  r.AudioId == "tl-ok" &&
                  r.Engine == _sut.SelectedEngine &&
                  r.ProfileId == "p1"),
              It.IsAny<CancellationToken>()),
          Times.Once);
      Assert.IsTrue(_sut.IsGeneratedAudioAddedToTimeline);
      Assert.IsFalse(_sut.CanAddGeneratedAudioToTimeline);
    }

    [TestMethod]
    public async Task TimelineOutput_Unavailable_SetsActionableStatus()
    {
      _mockTimelineService
          .Setup(t => t.AddGeneratedClipAsync(It.IsAny<GeneratedAudioTimelineRequest>(), It.IsAny<CancellationToken>()))
          .ReturnsAsync(new GeneratedAudioTimelineResult(
              false,
              GeneratedAudioTimelineKind.Unavailable,
              "No active project.",
              null,
              null,
              null));

      await RunSuccessfulSynthesisAsync("tl-u", "/api/audio/tl-u");
      await ((IAsyncRelayCommand)_sut.AddGeneratedAudioToTimelineCommand).ExecuteAsync(default);

      Assert.IsFalse(_sut.IsGeneratedAudioAddedToTimeline);
      StringAssert.Contains(_sut.GeneratedAudioTimelineStatus, "project");
    }

    [TestMethod]
    public async Task TimelineOutput_PlacementUnavailable_DoesNotMarkAdded()
    {
      _mockTimelineService
          .Setup(t => t.AddGeneratedClipAsync(It.IsAny<GeneratedAudioTimelineRequest>(), It.IsAny<CancellationToken>()))
          .ReturnsAsync(new GeneratedAudioTimelineResult(
              false,
              GeneratedAudioTimelineKind.PlacementUnavailable,
              "Cannot determine a safe start time for this track.",
              "proj-1",
              "tr-1",
              null,
              null));

      await RunSuccessfulSynthesisAsync("tl-pu", "/api/audio/tl-pu");
      await ((IAsyncRelayCommand)_sut.AddGeneratedAudioToTimelineCommand).ExecuteAsync(default);

      Assert.IsFalse(_sut.IsGeneratedAudioAddedToTimeline);
      StringAssert.Contains(_sut.GeneratedAudioTimelineStatus, "safe");
    }

    [TestMethod]
    public async Task TimelineOutput_PlacementUnavailable_PreservesGeneratedResultAndCanPlay()
    {
      _mockTimelineService
          .Setup(t => t.AddGeneratedClipAsync(It.IsAny<GeneratedAudioTimelineRequest>(), It.IsAny<CancellationToken>()))
          .ReturnsAsync(new GeneratedAudioTimelineResult(
              false,
              GeneratedAudioTimelineKind.PlacementUnavailable,
              "placement blocked",
              null,
              null,
              null,
              null));

      await RunSuccessfulSynthesisAsync("tl-pu2", "/api/audio/tl-pu2");
      await ((IAsyncRelayCommand)_sut.AddGeneratedAudioToTimelineCommand).ExecuteAsync(default);

      Assert.AreEqual("tl-pu2", _sut.LastSynthesizedAudioId);
      Assert.IsTrue(_sut.CanPlayAudio);
    }

    [TestMethod]
    public async Task TimelineOutput_DefaultEmptyTrack_SetsEmptyTrackStatus()
    {
      _mockTimelineService
          .Setup(t => t.AddGeneratedClipAsync(It.IsAny<GeneratedAudioTimelineRequest>(), It.IsAny<CancellationToken>()))
          .ReturnsAsync(new GeneratedAudioTimelineResult(
              true,
              GeneratedAudioTimelineKind.DefaultAtZeroBecauseTrackEmpty,
              null,
              "proj-x",
              "tr-x",
              "clip-z",
              0));

      await RunSuccessfulSynthesisAsync("tl-e", "/api/audio/tl-e");
      await ((IAsyncRelayCommand)_sut.AddGeneratedAudioToTimelineCommand).ExecuteAsync(default);

      Assert.IsTrue(_sut.IsGeneratedAudioAddedToTimeline);
      StringAssert.Contains(_sut.GeneratedAudioTimelineStatus, "empty");
    }

    [TestMethod]
    public async Task TimelineOutput_Failure_DoesNotMarkAdded()
    {
      _mockTimelineService
          .Setup(t => t.AddGeneratedClipAsync(It.IsAny<GeneratedAudioTimelineRequest>(), It.IsAny<CancellationToken>()))
          .ReturnsAsync(new GeneratedAudioTimelineResult(
              false,
              GeneratedAudioTimelineKind.Failed,
              "network",
              null,
              null,
              null));

      await RunSuccessfulSynthesisAsync("tl-f", "/api/audio/tl-f");
      await ((IAsyncRelayCommand)_sut.AddGeneratedAudioToTimelineCommand).ExecuteAsync(default);

      Assert.IsFalse(_sut.IsGeneratedAudioAddedToTimeline);
    }

    [TestMethod]
    public async Task TimelineOutput_Failure_PreservesGeneratedResultAndCanPlay()
    {
      _mockTimelineService
          .Setup(t => t.AddGeneratedClipAsync(It.IsAny<GeneratedAudioTimelineRequest>(), It.IsAny<CancellationToken>()))
          .ReturnsAsync(new GeneratedAudioTimelineResult(
              false,
              GeneratedAudioTimelineKind.Failed,
              "x",
              null,
              null,
              null));

      await RunSuccessfulSynthesisAsync("tl-k", "/api/audio/tl-k");
      await ((IAsyncRelayCommand)_sut.AddGeneratedAudioToTimelineCommand).ExecuteAsync(default);

      Assert.AreEqual("tl-k", _sut.LastSynthesizedAudioId);
      Assert.IsTrue(_sut.CanPlayAudio);
    }

    [TestMethod]
    public async Task TimelineOutput_Success_MarksMatchingRecentResult()
    {
      await RunSuccessfulSynthesisAsync("tl-recent", "/api/audio/tl-recent");
      await ((IAsyncRelayCommand)_sut.AddGeneratedAudioToTimelineCommand).ExecuteAsync(default);

      Assert.AreEqual(1, _sut.RecentSynthesisResults.Count);
      Assert.IsTrue(_sut.RecentSynthesisResults[0].IsAddedToTimeline);
    }

    [TestMethod]
    public async Task TimelineOutput_RestoreRecent_RestoresTimelineAddedState()
    {
      await RunSuccessfulSynthesisAsync("tl-rs", "/api/audio/tl-rs");
      await ((IAsyncRelayCommand)_sut.AddGeneratedAudioToTimelineCommand).ExecuteAsync(default);

      await RunSuccessfulSynthesisAsync("other", "/api/audio/other");

      var row = _sut.RecentSynthesisResults.First(r => r.AudioId == "tl-rs");
      _sut.RestoreRecentResultCommand.Execute(row);

      Assert.IsTrue(_sut.IsGeneratedAudioAddedToTimeline);
      StringAssert.Contains(_sut.GeneratedAudioTimelineStatus, "timeline");
    }

    [TestMethod]
    public async Task TimelineOutput_Persistence_RoundTripsTimelineFlag()
    {
      await RunSuccessfulSynthesisAsync("tl-persist", "/api/audio/tl-persist");
      await ((IAsyncRelayCommand)_sut.AddGeneratedAudioToTimelineCommand).ExecuteAsync(default);

      var state = _sut.GetCurrentState();
      Assert.IsNotNull(state?.CustomData);
      var json = state!.CustomData![RecentResultsCustomKey] as string;
      Assert.IsFalse(string.IsNullOrEmpty(json));
      StringAssert.Contains(json!, "IsAddedToTimeline");

      var sut2 = new VoiceSynthesisViewModel(
          _mockVoiceSynthesisService.Object,
          _mockEnginesClient.Object,
          _mockQualityPipelineService.Object,
          _mockEnsembleService.Object,
          _mockTextAnalysisService.Object,
          _mockQualityHistoryService.Object,
          _mockProfilesClient.Object,
          _mockAudioPlayer.Object,
          _mockToast.Object,
          _mockLibraryService.Object,
          _mockTimelineService.Object);
      await sut2.RestoreStateAsync(state!, default);

      Assert.IsTrue(sut2.RecentSynthesisResults[0].IsAddedToTimeline);
      sut2.Dispose();
    }

    [TestMethod]
    public async Task TimelineOutput_LibrarySaveNotCleared_OnTimelineFailure()
    {
      await RunSuccessfulSynthesisAsync("both", "/api/audio/both");
      await ((IAsyncRelayCommand)_sut.AddGeneratedAudioToLibraryCommand).ExecuteAsync(default);
      Assert.IsTrue(_sut.IsGeneratedAudioSaved);

      _mockTimelineService
          .Setup(t => t.AddGeneratedClipAsync(It.IsAny<GeneratedAudioTimelineRequest>(), It.IsAny<CancellationToken>()))
          .ReturnsAsync(new GeneratedAudioTimelineResult(
              false,
              GeneratedAudioTimelineKind.Failed,
              "bad",
              null,
              null,
              null));

      await ((IAsyncRelayCommand)_sut.AddGeneratedAudioToTimelineCommand).ExecuteAsync(default);

      Assert.IsTrue(_sut.IsGeneratedAudioSaved);
    }

    [TestMethod]
    public async Task TimelineOutput_ConsentFailure_DoesNotEnableTimelineAdd()
    {
      _sut.SelectedProfile = new VoiceProfile { Id = "p1", Name = "P" };
      _sut.Text = "Hello";
      _mockVoiceSynthesisService
          .Setup(x => x.SynthesizeVoiceAsync(It.IsAny<VoiceSynthesisRequest>(), It.IsAny<CancellationToken>()))
          .ThrowsAsync(new ConsentRequiredException("need consent"));

      await ((IAsyncRelayCommand)_sut.SynthesizeCommand).ExecuteAsync(default);

      Assert.IsFalse(_sut.CanAddGeneratedAudioToTimeline);
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
