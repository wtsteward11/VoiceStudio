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
  /// Seam-aware tests for QualityDashboardViewModel.
  /// Instantiates ViewModel with mocked IQualityControlClient.
  /// Supports "QualityDashboardViewModel migrated to IQualityControlClient" claims.
  /// See docs/governance/TEST_CLASSIFICATION.md.
  /// </summary>
  [TestClass]
  [TestCategory("SeamAware")]
  public class QualityDashboardViewModelSeamTests
  {
    private Mock<IQualityControlClient> _mockQualityClient = null!;
    private IViewModelContext _context = null!;
    private DispatcherQueueController? _dispatcherController;

    [TestInitialize]
    public void Setup()
    {
      TestAppServicesHelper.EnsureInitialized();
      _mockQualityClient = new Mock<IQualityControlClient>();
      _dispatcherController = DispatcherQueueController.CreateOnDedicatedThread();
      var dispatcher = _dispatcherController.DispatcherQueue;
      _context = new ViewModelContext(NullLogger.Instance, dispatcher);

      _mockQualityClient.Setup(x => x.GetQualityDashboardAsync(It.IsAny<string?>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
        .ReturnsAsync(new QualityDashboard());
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
      _ = new QualityDashboardViewModel(_context, _mockQualityClient.Object);
      _mockQualityClient.Verify(x => x.GetQualityDashboardAsync(It.IsAny<string?>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [TestMethod]
    public void Constructor_WithClients_CreatesInstance()
    {
      var vm = new QualityDashboardViewModel(_context, _mockQualityClient.Object);
      Assert.IsNotNull(vm);
      Assert.AreEqual(PanelIds.QualityDashboard, vm.PanelId);
    }

    [TestMethod]
    [ExpectedException(typeof(ArgumentNullException))]
    public void Constructor_WithNullQualityClient_Throws()
    {
      _ = new QualityDashboardViewModel(_context, null!);
    }
  }
}
