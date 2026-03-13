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
      _dispatcherController?.ShutdownQueueAsync().AsTask().GetAwaiter().GetResult();
    }

    [TestMethod]
    public void Constructor_WithIUltimateDashboardClient_CreatesInstance()
    {
      var vm = new UltimateDashboardViewModel(_context, _mockClient.Object);
      Assert.IsNotNull(vm);
      Assert.AreEqual("ultimate-dashboard", vm.PanelId);
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
