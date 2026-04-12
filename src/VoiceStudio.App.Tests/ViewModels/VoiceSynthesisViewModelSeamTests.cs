using CommunityToolkit.Mvvm.Input;
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

namespace VoiceStudio.App.Tests.ViewModels;

/// <summary>
/// GAP-049 seam tests: long-form synthesis path via <see cref="IVoiceSynthesisService.SynthesizeLongFormAsync"/>.
/// </summary>
[TestClass]
[TestCategory("SeamAware")]
public sealed class VoiceSynthesisViewModelSeamTests
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

  private static LongFormSynthesisResponse OkLongFormResponse() =>
      new()
      {
        AudioId = "lf1",
        AudioUrl = "/api/audio/lf1",
        Duration = 1.0,
        QualityScore = 0.9,
        ChunksTotal = 2,
        ChunksSucceeded = 2,
        PartialFailure = false,
      };

  [TestMethod]
  public async Task LongForm_CallsSynthesizeLongFormAsync_WhenUseLongFormTrue()
  {
    _sut.UseLongForm = true;
    _sut.SelectedProfile = new VoiceProfile { Id = "p1", Name = "P" };
    _sut.Text = "Hello long form.";

    _mockVoiceSynthesisService
        .Setup(x => x.SynthesizeLongFormAsync(It.IsAny<LongFormSynthesisRequest>(), It.IsAny<CancellationToken>()))
        .ReturnsAsync(OkLongFormResponse());

    var cmd = (IAsyncRelayCommand)_sut.SynthesizeCommand;
    await cmd.ExecuteAsync(default);

    _mockVoiceSynthesisService.Verify(
        x => x.SynthesizeLongFormAsync(It.IsAny<LongFormSynthesisRequest>(), It.IsAny<CancellationToken>()),
        Times.Once);
    _mockVoiceSynthesisService.Verify(
        x => x.SynthesizeVoiceAsync(It.IsAny<VoiceSynthesisRequest>(), It.IsAny<CancellationToken>()),
        Times.Never);
  }

  [TestMethod]
  public async Task LongForm_SetsIsLongFormRunning_DuringExecution()
  {
    _sut.UseLongForm = true;
    _sut.SelectedProfile = new VoiceProfile { Id = "p1", Name = "P" };
    _sut.Text = "Block me.";

    var unblock = new TaskCompletionSource<bool>();

    _mockVoiceSynthesisService
        .Setup(x => x.SynthesizeLongFormAsync(It.IsAny<LongFormSynthesisRequest>(), It.IsAny<CancellationToken>()))
        .Returns(async (LongFormSynthesisRequest _, CancellationToken _) =>
        {
          await unblock.Task;
          return OkLongFormResponse();
        });

    var cmd = (IAsyncRelayCommand)_sut.SynthesizeCommand;
    var run = cmd.ExecuteAsync(default);

    for (var i = 0; i < 200 && !_sut.IsLongFormRunning; i++)
    {
      await Task.Delay(10);
    }

    Assert.IsTrue(_sut.IsLongFormRunning, "Expected IsLongFormRunning true while long-form call is in flight.");
    Assert.IsFalse(string.IsNullOrEmpty(_sut.LongFormProgressText));

    unblock.SetResult(true);
    await run;

    Assert.IsFalse(_sut.IsLongFormRunning);
    Assert.AreEqual(string.Empty, _sut.LongFormProgressText);
  }

  [TestMethod]
  public async Task LongForm_ShowsPartialFailureWarning_WhenPartialFailure()
  {
    _sut.UseLongForm = true;
    _sut.SelectedProfile = new VoiceProfile { Id = "p1", Name = "P" };
    _sut.Text = "Hello.";

    _mockVoiceSynthesisService
        .Setup(x => x.SynthesizeLongFormAsync(It.IsAny<LongFormSynthesisRequest>(), It.IsAny<CancellationToken>()))
        .ReturnsAsync(new LongFormSynthesisResponse
        {
          AudioId = "lf1",
          AudioUrl = "/api/audio/lf1",
          Duration = 1.0,
          QualityScore = 0.8,
          ChunksTotal = 2,
          ChunksSucceeded = 1,
          PartialFailure = true,
          FailedChunks = new List<LongFormChunkResultDto>
          {
            new() { ChunkIndex = 0, Error = "chunk fail" },
          },
        });

    var cmd = (IAsyncRelayCommand)_sut.SynthesizeCommand;
    await cmd.ExecuteAsync(default);

    _mockToast.Verify(
        x => x.ShowWarning(It.Is<string>(s => s.Contains("chunks failed", StringComparison.OrdinalIgnoreCase)), It.IsAny<string?>()),
        Times.Once);
  }

  [TestMethod]
  public void LongForm_CannotRun_WhenIsLongFormRunning()
  {
    _sut.SelectedProfile = new VoiceProfile { Id = "p1", Name = "P" };
    _sut.Text = "Hi";
    _sut.IsLongFormRunning = true;

    Assert.IsFalse(_sut.CanSynthesize);
  }

  [TestMethod]
  public async Task LongForm_ClearsProgressText_AfterCompletion()
  {
    _sut.UseLongForm = true;
    _sut.SelectedProfile = new VoiceProfile { Id = "p1", Name = "P" };
    _sut.Text = "Done.";

    _mockVoiceSynthesisService
        .Setup(x => x.SynthesizeLongFormAsync(It.IsAny<LongFormSynthesisRequest>(), It.IsAny<CancellationToken>()))
        .ReturnsAsync(OkLongFormResponse());

    var cmd = (IAsyncRelayCommand)_sut.SynthesizeCommand;
    await cmd.ExecuteAsync(default);

    Assert.AreEqual(string.Empty, _sut.LongFormProgressText);
  }

  [TestMethod]
  public void AdvancedSynthesisControlsExpanded_TogglesForDisclosureState()
  {
    Assert.IsFalse(_sut.IsAdvancedSynthesisControlsExpanded);
    _sut.IsAdvancedSynthesisControlsExpanded = true;
    Assert.IsTrue(_sut.IsAdvancedSynthesisControlsExpanded);
  }
}
