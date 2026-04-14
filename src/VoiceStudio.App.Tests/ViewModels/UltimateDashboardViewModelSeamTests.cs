using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.UI.Dispatching;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using System;
using System.Threading;
using VoiceStudio.App.Services;
using VoiceStudio.App.Tests.Fixtures;
using VoiceStudio.App.ViewModels;
using VoiceStudio.Core.Models;
using VoiceStudio.Core.Panels;
using VoiceStudio.Core.Services;

namespace VoiceStudio.App.Tests.ViewModels
{
  /// <summary>
  /// Seam-aware tests for UltimateDashboardViewModel.
  /// Instantiates ViewModel with mocked IUltimateDashboardClient.
  /// </summary>
  [TestClass]
  [TestCategory("SeamAware")]
  public class UltimateDashboardViewModelSeamTests
  {
    private Mock<IUltimateDashboardClient> _mockClient = null!;
    private IViewModelContext _context = null!;
    private DispatcherQueueController? _dispatcherController;

    [TestInitialize]
    public void Setup()
    {
      TestAppServicesHelper.EnsureInitialized();
      _mockClient = new Mock<IUltimateDashboardClient>();
      _dispatcherController = DispatcherQueueController.CreateOnDedicatedThread();
      var dispatcher = _dispatcherController.DispatcherQueue;
      _context = new ViewModelContext(NullLogger.Instance, dispatcher);

      _mockClient
        .Setup(x => x.GetDashboardAsync(It.IsAny<CancellationToken>()))
        .ReturnsAsync((UltimateDashboardData?)null);
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
      _ = new UltimateDashboardViewModel(_context, _mockClient.Object);
      _mockClient.Verify(x => x.GetDashboardAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [TestMethod]
    public void Constructor_WithIUltimateDashboardClient_CreatesInstance()
    {
      var vm = new UltimateDashboardViewModel(_context, _mockClient.Object);
      Assert.IsNotNull(vm);
      Assert.AreEqual(PanelIds.UltimateDashboard, vm.PanelId);
      Assert.IsNotNull(vm.LoadDashboardCommand);
      Assert.IsNotNull(vm.RefreshCommand);
    }

    [TestMethod]
    [ExpectedException(typeof(ArgumentNullException))]
    public void Constructor_WithNullClient_Throws()
    {
      _ = new UltimateDashboardViewModel(_context, null!);
    }

    [TestMethod]
    public void ViewModel_ImplementsIPanelLifecycle()
    {
      var vm = new UltimateDashboardViewModel(_context, _mockClient.Object);
      Assert.IsTrue(vm is IPanelLifecycle);
    }
  }
}
