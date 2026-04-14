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
  /// Seam-aware tests for DeepfakeCreatorViewModel.
  /// Instantiates ViewModel with mocked IDeepfakeCreatorClient.
  /// Supports "DeepfakeCreatorViewModel migrated to IDeepfakeCreatorClient" claims.
  /// See docs/governance/TEST_CLASSIFICATION.md.
  /// </summary>
  [TestClass]
  [TestCategory("SeamAware")]
  public class DeepfakeCreatorViewModelSeamTests
  {
    private Mock<IDeepfakeCreatorClient> _mockDeepfakeClient = null!;
    private IViewModelContext _context = null!;
    private DispatcherQueueController? _dispatcherController;

    [TestInitialize]
    public void Setup()
    {
      TestAppServicesHelper.EnsureInitialized();
      _mockDeepfakeClient = new Mock<IDeepfakeCreatorClient>();
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
      _ = new DeepfakeCreatorViewModel(_context, _mockDeepfakeClient.Object);

      _mockDeepfakeClient.Verify(x => x.GetEnginesAsync(It.IsAny<CancellationToken>()), Times.Never);
      _mockDeepfakeClient.Verify(x => x.GetJobsAsync(It.IsAny<CancellationToken>()), Times.Never);
      _mockDeepfakeClient.Verify(x => x.DeleteJobAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
      _mockDeepfakeClient.Verify(
        x => x.CreateDeepfakeAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<DeepfakeCreateRequest>(), It.IsAny<IProgress<double>>(), It.IsAny<CancellationToken>()),
        Times.Never);
    }

    [TestMethod]
    public void Constructor_WithClient_CreatesInstance()
    {
      var vm = new DeepfakeCreatorViewModel(_context, _mockDeepfakeClient.Object);

      Assert.IsNotNull(vm);
      Assert.AreEqual(PanelIds.DeepfakeCreator, vm.PanelId);
      Assert.IsNotNull(vm.LoadEnginesCommand);
      Assert.IsNotNull(vm.CreateDeepfakeCommand);
      Assert.IsNotNull(vm.LoadJobsCommand);
      Assert.IsNotNull(vm.DeleteJobCommand);
      Assert.IsNotNull(vm.RefreshCommand);
    }

    [TestMethod]
    [ExpectedException(typeof(ArgumentNullException))]
    public void Constructor_WithNullDeepfakeClient_Throws()
    {
      _ = new DeepfakeCreatorViewModel(_context, null!);
    }

    [TestMethod]
    public void ViewModel_ImplementsIPanelLifecycle()
    {
      var vm = new DeepfakeCreatorViewModel(_context, _mockDeepfakeClient.Object);
      Assert.IsTrue(vm is IPanelLifecycle);
    }
  }
}
