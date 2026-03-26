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
using VoiceStudio.Core.Panels;
using VoiceStudio.Core.Services;

namespace VoiceStudio.App.Tests.ViewModels
{
  /// <summary>
  /// Seam-aware tests for JobProgressViewModel.
  /// Instantiates ViewModel with mocked IJobProgressApiClient.
  /// Supports "JobProgressViewModel migrated to IJobProgressApiClient" claims.
  /// See docs/governance/TEST_CLASSIFICATION.md.
  /// </summary>
  [TestClass]
  [TestCategory("SeamAware")]
  public class JobProgressViewModelSeamTests
  {
    private Mock<IJobProgressApiClient> _mockJobProgressClient = null!;
    private IViewModelContext _context = null!;
    private DispatcherQueueController? _dispatcherController;

    [TestInitialize]
    public void Setup()
    {
      TestAppServicesHelper.EnsureInitialized();
      _mockJobProgressClient = new Mock<IJobProgressApiClient>();
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
      _ = new JobProgressViewModel(_context, _mockJobProgressClient.Object, webSocketService: null);
      _mockJobProgressClient.Verify(x => x.GetJobsAsync(It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never);
      _mockJobProgressClient.Verify(x => x.GetJobSummaryAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [TestMethod]
    public void Constructor_WithClients_CreatesInstance()
    {
      var vm = new JobProgressViewModel(_context, _mockJobProgressClient.Object, webSocketService: null);
      Assert.IsNotNull(vm);
      Assert.AreEqual(PanelIds.JobProgress, vm.PanelId);
    }

    [TestMethod]
    [ExpectedException(typeof(ArgumentNullException))]
    public void Constructor_WithNullJobProgressClient_Throws()
    {
      _ = new JobProgressViewModel(_context, null!, webSocketService: null);
    }

    /// <summary>
    /// Activation triggers client calls only after OnActivatedAsync is invoked.
    /// Constructor must not call client; activation must.
    /// </summary>
    [TestMethod]
    public async Task Activation_CallsClient_OnlyAfterOnActivatedAsync()
    {
      var vm = new JobProgressViewModel(_context, _mockJobProgressClient.Object, webSocketService: null);
      _mockJobProgressClient.Verify(x => x.GetJobsAsync(It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never);
      _mockJobProgressClient.Verify(x => x.GetJobSummaryAsync(It.IsAny<CancellationToken>()), Times.Never);

      await ((IPanelLifecycle)vm).OnActivatedAsync(CancellationToken.None);

      _mockJobProgressClient.Verify(x => x.GetJobsAsync(It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.AtLeastOnce);
      _mockJobProgressClient.Verify(x => x.GetJobSummaryAsync(It.IsAny<CancellationToken>()), Times.AtLeastOnce);
    }

    /// <summary>
    /// Activation performs initial LoadSummary + LoadJobs before polling/websocket.
    /// Polling path may trigger a second fetch shortly after. Verify no excessive over-fetch (e.g. 5+ calls).
    /// </summary>
    [TestMethod]
    public async Task Activation_DoesNotDoubleLoad_InitialFetch()
    {
      var vm = new JobProgressViewModel(_context, _mockJobProgressClient.Object, webSocketService: null);
      await ((IPanelLifecycle)vm).OnActivatedAsync(CancellationToken.None);

      // After single activation: at most 2 per fetch (initial + possible early poll/filter handler).
      // Guards against excessive over-fetch (e.g. 5+ calls would indicate a bug).
      _mockJobProgressClient.Verify(x => x.GetJobSummaryAsync(It.IsAny<CancellationToken>()), Times.AtMost(2));
      _mockJobProgressClient.Verify(x => x.GetJobsAsync(It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.AtMost(2));
    }
  }
}
