using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.UI.Dispatching;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using System;
using System.Threading;
using System.Threading.Tasks;
using VoiceStudio.App.Services;
using VoiceStudio.App.Tests.Fixtures;
using VoiceStudio.App.ViewModels;
using VoiceStudio.Core.Models;
using VoiceStudio.Core.Panels;
using VoiceStudio.Core.Services;

namespace VoiceStudio.App.Tests.ViewModels
{
  /// <summary>
  /// Seam-aware tests for UpscalingViewModel.
  /// Instantiates ViewModel with mocked IUpscalingClient.
  /// Supports "UpscalingViewModel migrated to IUpscalingClient" claims.
  /// See docs/governance/TEST_CLASSIFICATION.md.
  /// </summary>
  [TestClass]
  [TestCategory("SeamAware")]
  public class UpscalingViewModelSeamTests
  {
    private Mock<IUpscalingClient> _mockUpscalingClient = null!;
    private IViewModelContext _context = null!;
    private DispatcherQueueController? _dispatcherController;

    [TestInitialize]
    public void Setup()
    {
      TestAppServicesHelper.EnsureInitialized();
      _mockUpscalingClient = new Mock<IUpscalingClient>();
      _dispatcherController = DispatcherQueueController.CreateOnDedicatedThread();
      var dispatcher = _dispatcherController.DispatcherQueue;
      _context = new ViewModelContext(NullLogger.Instance, dispatcher);

      _mockUpscalingClient
        .Setup(x => x.GetEnginesAsync(It.IsAny<CancellationToken>()))
        .ReturnsAsync(Array.Empty<UpscalingEngineResponse>());
      _mockUpscalingClient
        .Setup(x => x.GetJobsAsync(It.IsAny<CancellationToken>()))
        .ReturnsAsync(Array.Empty<UpscalingJobResponse>());
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
      _ = new UpscalingViewModel(_context, _mockUpscalingClient.Object);
      _mockUpscalingClient.Verify(x => x.GetEnginesAsync(It.IsAny<CancellationToken>()), Times.Never);
      _mockUpscalingClient.Verify(x => x.GetJobsAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [TestMethod]
    public void Constructor_WithClients_CreatesInstance()
    {
      var vm = new UpscalingViewModel(_context, _mockUpscalingClient.Object);
      Assert.IsNotNull(vm);
      Assert.AreEqual(PanelIds.Upscaling, vm.PanelId);
      Assert.IsNotNull(vm.LoadEnginesCommand);
      Assert.IsNotNull(vm.UpscaleCommand);
      Assert.IsNotNull(vm.LoadJobsCommand);
      Assert.IsNotNull(vm.DeleteJobCommand);
      Assert.IsNotNull(vm.RefreshCommand);
    }

    [TestMethod]
    [ExpectedException(typeof(ArgumentNullException))]
    public void Constructor_WithNullUpscalingClient_Throws()
    {
      _ = new UpscalingViewModel(_context, null!);
    }

    [TestMethod]
    public void ViewModel_ImplementsIPanelLifecycle()
    {
      var vm = new UpscalingViewModel(_context, _mockUpscalingClient.Object);
      Assert.IsTrue(vm is IPanelLifecycle);
    }
  }
}
