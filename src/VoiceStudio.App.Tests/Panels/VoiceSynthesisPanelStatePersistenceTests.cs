using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Input;
using VoiceStudio.App.Services;
using VoiceStudio.App.Views.Panels;
using VoiceStudio.Core.Models;
using VoiceStudio.Core.Panels;
using VoiceStudio.Core.Services;

namespace VoiceStudio.App.Tests.Panels;

/// <summary>
/// GAP-050: <see cref="IPanelStatePersistable"/> round-trip for Voice Synthesis emotion + engine keys.
/// </summary>
[TestClass]
public class VoiceSynthesisPanelStatePersistenceTests
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

    _mockProfilesClient
        .Setup(x => x.GetProfilesAsync(It.IsAny<CancellationToken>()))
        .ReturnsAsync(new List<VoiceProfile> { new() { Id = "p1", Name = "P1" } });
    _mockEnginesClient
        .Setup(x => x.GetEnginesAsync(It.IsAny<CancellationToken>()))
        .ReturnsAsync(new List<string> { "xtts", "piper" });
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
  public void Cleanup() => _sut.Dispose();

  [TestMethod]
  public void GetCurrentState_IncludesCanonicalEmotionAndEngine_Gap050()
  {
    _sut.SelectedProfile = new VoiceProfile { Id = "p1", Name = "P1" };
    _sut.SelectedEngine = "piper";
    _sut.Emotion = "warm";

    var state = _sut.GetCurrentState();
    Assert.IsNotNull(state);
    Assert.AreEqual("p1", state!.SelectedItemId);
    Assert.IsTrue(state.CustomData!.ContainsKey("VoiceSynthesis_EmotionPreset"));
    Assert.AreEqual("warm", state.CustomData["VoiceSynthesis_EmotionPreset"]);
    Assert.IsTrue(state.CustomData.ContainsKey("VoiceSynthesis_SelectedEngine"));
    Assert.AreEqual("piper", state.CustomData["VoiceSynthesis_SelectedEngine"]);
  }

  [TestMethod]
  public async Task RoundTrip_GetCurrentState_ThenRestore_RestoresEmotionAndEngine_Gap050()
  {
    var loadCmd = (IAsyncRelayCommand)_sut.LoadProfilesCommand;
    await loadCmd.ExecuteAsync(default);

    _sut.SelectedProfile = _sut.Profiles[0];
    _sut.SelectedEngine = "piper";
    _sut.Emotion = "calm";

    var saved = _sut.GetCurrentState();
    Assert.IsNotNull(saved);

    _sut.Emotion = "energetic";
    _sut.SelectedEngine = "xtts";

    await _sut.RestoreStateAsync(saved!, default);

    Assert.AreEqual("calm", _sut.Emotion);
    Assert.AreEqual("piper", _sut.SelectedEngine);
    Assert.AreEqual("p1", _sut.SelectedProfile?.Id);
  }
}
