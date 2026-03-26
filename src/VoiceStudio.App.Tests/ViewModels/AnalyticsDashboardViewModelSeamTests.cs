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
  /// Seam-aware tests for AnalyticsDashboardViewModel.
  /// Instantiates ViewModel with mocked IAnalyticsDashboardClient.
  /// Supports "AnalyticsDashboardViewModel migrated to IAnalyticsDashboardClient" claims.
  /// See docs/governance/TEST_CLASSIFICATION.md.
  /// </summary>
  [TestClass]
  [TestCategory("SeamAware")]
  public class AnalyticsDashboardViewModelSeamTests
  {
    private Mock<IAnalyticsDashboardClient> _mockAnalyticsClient = null!;
    private IViewModelContext _context = null!;
    private DispatcherQueueController? _dispatcherController;

    [TestInitialize]
    public void Setup()
    {
      TestAppServicesHelper.EnsureInitialized();
      _mockAnalyticsClient = new Mock<IAnalyticsDashboardClient>();
      _dispatcherController = DispatcherQueueController.CreateOnDedicatedThread();
      var dispatcher = _dispatcherController.DispatcherQueue;
      _context = new ViewModelContext(NullLogger.Instance, dispatcher);
    }

    [TestCleanup]
    public void Cleanup()
    {
      _dispatcherController?.ShutdownQueueAsync().AsTask().GetAwaiter().GetResult();
    }

    /// <summary>
    /// Invariant: constructor must not call any client methods before activation.
    /// Prevents constructor fire-and-forget regression (RETAINED_ASYNC_RULE, ADR-047).
    /// </summary>
    [TestMethod]
    public void Constructor_DoesNotCallClient_BeforeActivation()
    {
      _ = new AnalyticsDashboardViewModel(_context, _mockAnalyticsClient.Object);

      _mockAnalyticsClient.Verify(x => x.GetSummaryAsync(It.IsAny<CancellationToken>()), Times.Never);
      _mockAnalyticsClient.Verify(x => x.GetCategoriesAsync(It.IsAny<CancellationToken>()), Times.Never);
      _mockAnalyticsClient.Verify(x => x.GetMetricsAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
      _mockAnalyticsClient.Verify(x => x.GetStatisticalAnalysisAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [TestMethod]
    public void Constructor_WithClient_CreatesInstance()
    {
      var vm = new AnalyticsDashboardViewModel(_context, _mockAnalyticsClient.Object);

      Assert.IsNotNull(vm);
      Assert.AreEqual("analytics-dashboard", vm.PanelId);
      Assert.IsNotNull(vm.LoadSummaryCommand);
      Assert.IsNotNull(vm.LoadCategoryMetricsCommand);
      Assert.IsNotNull(vm.LoadCategoriesCommand);
      Assert.IsNotNull(vm.LoadStatisticalAnalysisCommand);
      Assert.IsNotNull(vm.RefreshCommand);
    }

    [TestMethod]
    [ExpectedException(typeof(ArgumentNullException))]
    public void Constructor_WithNullAnalyticsClient_Throws()
    {
      _ = new AnalyticsDashboardViewModel(_context, null!);
    }

    [TestMethod]
    public void ViewModel_ImplementsIPanelLifecycle()
    {
      var vm = new AnalyticsDashboardViewModel(_context, _mockAnalyticsClient.Object);
      Assert.IsTrue(vm is IPanelLifecycle);
    }
  }
}
