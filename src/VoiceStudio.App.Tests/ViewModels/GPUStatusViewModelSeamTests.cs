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
  /// Seam-aware tests for GPUStatusViewModel.
  /// Instantiates ViewModel with mocked IGPUStatusClient.
  /// Supports "GPUStatusViewModel migrated to IGPUStatusClient" claims.
  /// See docs/governance/TEST_CLASSIFICATION.md.
  /// </summary>
  [TestClass]
  [TestCategory("SeamAware")]
  public class GPUStatusViewModelSeamTests
  {
    private Mock<IGPUStatusClient> _mockGpuStatusClient = null!;
    private IViewModelContext _context = null!;
    private DispatcherQueueController? _dispatcherController;

    [TestInitialize]
    public void Setup()
    {
      TestAppServicesHelper.EnsureInitialized();
      _mockGpuStatusClient = new Mock<IGPUStatusClient>();
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
      _ = new GPUStatusViewModel(_context, _mockGpuStatusClient.Object);

      _mockGpuStatusClient.Verify(x => x.GetStatusAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [TestMethod]
    public void Constructor_WithClient_CreatesInstance()
    {
      var vm = new GPUStatusViewModel(_context, _mockGpuStatusClient.Object);

      Assert.IsNotNull(vm);
      Assert.AreEqual(PanelIds.GPUStatus, vm.PanelId);
      Assert.IsNotNull(vm.LoadGPUStatusCommand);
      Assert.IsNotNull(vm.RefreshCommand);
    }

    [TestMethod]
    [ExpectedException(typeof(ArgumentNullException))]
    public void Constructor_WithNullGpuStatusClient_Throws()
    {
      _ = new GPUStatusViewModel(_context, null!);
    }

    [TestMethod]
    public void ViewModel_ImplementsIPanelLifecycle()
    {
      var vm = new GPUStatusViewModel(_context, _mockGpuStatusClient.Object);
      Assert.IsTrue(vm is IPanelLifecycle);
    }
  }
}
