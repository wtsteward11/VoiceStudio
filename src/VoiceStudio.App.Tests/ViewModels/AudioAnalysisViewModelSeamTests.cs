using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.UI.Dispatching;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using System;
using System.Threading;
using VoiceStudio.App.Services;
using VoiceStudio.App.Tests.Fixtures;
using VoiceStudio.App.ViewModels;
using VoiceStudio.Core.Panels;
using VoiceStudio.Core.Services;

namespace VoiceStudio.App.Tests.ViewModels
{
  /// <summary>
  /// Seam-aware tests for AudioAnalysisViewModel.
  /// Instantiates ViewModel with mocked IAudioAnalysisClient.
  /// Supports "AudioAnalysisViewModel migrated to IAudioAnalysisClient" claims.
  /// See docs/governance/TEST_CLASSIFICATION.md.
  /// </summary>
  [TestClass]
  [TestCategory("SeamAware")]
  public class AudioAnalysisViewModelSeamTests
  {
    private Mock<IAudioAnalysisClient> _mockAudioAnalysisClient = null!;
    private IViewModelContext _context = null!;
    private DispatcherQueueController? _dispatcherController;

    [TestInitialize]
    public void Setup()
    {
      TestAppServicesHelper.EnsureInitialized();
      _mockAudioAnalysisClient = new Mock<IAudioAnalysisClient>();
      _dispatcherController = DispatcherQueueController.CreateOnDedicatedThread();
      var dispatcher = _dispatcherController.DispatcherQueue;
      _context = new ViewModelContext(NullLogger.Instance, dispatcher);
    }

    [TestCleanup]
    public void Cleanup()
    {
      DispatcherQueueTestHelpers.ShutdownSyncBounded(_dispatcherController);
    }

    /// <summary>
    /// Invariant: constructor must not call any client methods before activation.
    /// Prevents constructor fire-and-forget regression (RETAINED_ASYNC_RULE, ADR-047).
    /// </summary>
    [TestMethod]
    public void Constructor_DoesNotCallClient_BeforeActivation()
    {
      _ = new AudioAnalysisViewModel(_context, _mockAudioAnalysisClient.Object);

      _mockAudioAnalysisClient.Verify(
        x => x.GetAnalysisAsync(It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()),
        Times.Never);
      _mockAudioAnalysisClient.Verify(
        x => x.QueueAnalysisAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()),
        Times.Never);
      _mockAudioAnalysisClient.Verify(
        x => x.CompareAudioAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
        Times.Never);
    }

    [TestMethod]
    public void Constructor_WithClient_CreatesInstance()
    {
      var vm = new AudioAnalysisViewModel(_context, _mockAudioAnalysisClient.Object);

      Assert.IsNotNull(vm);
      Assert.AreEqual(PanelIds.AudioAnalysis, vm.PanelId);
      Assert.IsNotNull(vm.LoadAnalysisCommand);
      Assert.IsNotNull(vm.AnalyzeAudioCommand);
      Assert.IsNotNull(vm.CompareAudioCommand);
      Assert.IsNotNull(vm.RefreshCommand);
    }

    [TestMethod]
    [ExpectedException(typeof(ArgumentNullException))]
    public void Constructor_WithNullAudioAnalysisClient_Throws()
    {
      _ = new AudioAnalysisViewModel(_context, null!);
    }

    [TestMethod]
    public void ViewModel_ImplementsIPanelLifecycle()
    {
      var vm = new AudioAnalysisViewModel(_context, _mockAudioAnalysisClient.Object);
      Assert.IsTrue(vm is IPanelLifecycle);
    }
  }
}
