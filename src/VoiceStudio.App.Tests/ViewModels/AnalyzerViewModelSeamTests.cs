using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using System;
using System.Threading;
using VoiceStudio.App.Services;
using VoiceStudio.App.Tests.Fixtures;
using VoiceStudio.App.Views.Panels;
using VoiceStudio.Core.Panels;
using VoiceStudio.Core.Services;

namespace VoiceStudio.App.Tests.ViewModels
{
  /// <summary>
  /// Seam-aware tests for AnalyzerViewModel.
  /// Instantiates ViewModel with mocked IAnalyzerClient, IAudioVisualizationService.
  /// Supports "AnalyzerViewModel migrated to IAnalyzerClient" claims.
  /// See docs/governance/TEST_CLASSIFICATION.md.
  /// </summary>
  [TestClass]
  [TestCategory("SeamAware")]
  public class AnalyzerViewModelSeamTests
  {
    private Mock<IAnalyzerClient> _mockAnalyzerClient = null!;
    private Mock<IAudioVisualizationService> _mockAudioVisualization = null!;

    [TestInitialize]
    public void Setup()
    {
      TestAppServicesHelper.EnsureInitialized();
      _mockAnalyzerClient = new Mock<IAnalyzerClient>();
      _mockAudioVisualization = new Mock<IAudioVisualizationService>();
    }

    /// <summary>
    /// Invariant: constructor must not call any client methods before activation.
    /// Prevents constructor fire-and-forget regression (RETAINED_ASYNC_RULE, ADR-047).
    /// </summary>
    [TestMethod]
    public void Constructor_DoesNotCallClient_BeforeActivation()
    {
      _ = new AnalyzerViewModel(_mockAnalyzerClient.Object, _mockAudioVisualization.Object);
      _mockAnalyzerClient.Verify(x => x.UploadAudioFileAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
      _mockAnalyzerClient.Verify(x => x.GetRadarDataAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [TestMethod]
    public void Constructor_WithClients_CreatesInstance()
    {
      var vm = new AnalyzerViewModel(_mockAnalyzerClient.Object, _mockAudioVisualization.Object);
      Assert.IsNotNull(vm);
      Assert.AreEqual(PanelIds.Analyzer, vm.PanelId);
    }

    [TestMethod]
    [ExpectedException(typeof(ArgumentNullException))]
    public void Constructor_WithNullAnalyzerClient_Throws()
    {
      _ = new AnalyzerViewModel(null!, _mockAudioVisualization.Object);
    }

    [TestMethod]
    [ExpectedException(typeof(ArgumentNullException))]
    public void Constructor_WithNullAudioVisualizationService_Throws()
    {
      _ = new AnalyzerViewModel(_mockAnalyzerClient.Object, null!);
    }
  }
}
