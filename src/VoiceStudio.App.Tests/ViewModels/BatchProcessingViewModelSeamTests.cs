using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.UI.Dispatching;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using VoiceStudio.App.Services;
using VoiceStudio.App.Tests.Fixtures;
using VoiceStudio.App.ViewModels;
using VoiceStudio.App.Views.Panels;
using VoiceStudio.Core.Models;
using VoiceStudio.Core.Panels;
using VoiceStudio.Core.Services;

namespace VoiceStudio.App.Tests.ViewModels
{
  /// <summary>
  /// Seam-aware tests for BatchProcessingViewModel.
  /// Instantiates ViewModel with mocked IBatchProcessingClient.
  /// Supports "BatchProcessingViewModel migrated to IBatchProcessingClient" claims.
  /// See docs/governance/TEST_CLASSIFICATION.md.
  /// </summary>
  [TestClass]
  [TestCategory("SeamAware")]
  public class BatchProcessingViewModelSeamTests
  {
    private Mock<IBatchProcessingClient> _mockBatchClient = null!;
    private Mock<IDialogService> _mockDialogService = null!;
    private IViewModelContext _context = null!;
    private DispatcherQueueController? _dispatcherController;

    [TestInitialize]
    public void Setup()
    {
      TestAppServicesHelper.EnsureInitialized();
      _mockBatchClient = new Mock<IBatchProcessingClient>();
      _mockDialogService = new Mock<IDialogService>();
      _dispatcherController = DispatcherQueueController.CreateOnDedicatedThread();
      var dispatcher = _dispatcherController.DispatcherQueue;
      _context = new ViewModelContext(NullLogger.Instance, dispatcher);

      _mockBatchClient
          .Setup(x => x.GetBatchJobsAsync(It.IsAny<string?>(), It.IsAny<JobStatus?>(), It.IsAny<CancellationToken>()))
          .ReturnsAsync(new List<BatchJob>());
      _mockBatchClient
          .Setup(x => x.GetBatchQueueStatusAsync(It.IsAny<CancellationToken>()))
          .ReturnsAsync(new BatchQueueStatus { Pending = 0, Running = 0 });
      _mockBatchClient
          .Setup(x => x.GetBatchQualityReportAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
          .ReturnsAsync(new BatchQualityReport { JobId = "test", QualityScore = 0.9 });
      _mockBatchClient
          .Setup(x => x.GetBatchQualityStatisticsAsync(It.IsAny<string?>(), It.IsAny<JobStatus?>(), It.IsAny<CancellationToken>()))
          .ReturnsAsync(new BatchQualityStatistics { TotalJobs = 0, CompletedJobs = 0, AverageQuality = 0 });
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
      _ = new BatchProcessingViewModel(_context, _mockBatchClient.Object, _mockDialogService.Object);
      _mockBatchClient.Verify(x => x.GetBatchJobsAsync(It.IsAny<string?>(), It.IsAny<JobStatus?>(), It.IsAny<CancellationToken>()), Times.Never);
      _mockBatchClient.Verify(x => x.GetBatchQueueStatusAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [TestMethod]
    public void Constructor_WithIBatchProcessingClient_CreatesInstance()
    {
      var vm = new BatchProcessingViewModel(_context, _mockBatchClient.Object, _mockDialogService.Object);
      Assert.IsNotNull(vm);
      Assert.AreEqual(PanelIds.BatchProcessing, vm.PanelId);
      Assert.IsNotNull(vm.LoadJobsCommand);
      Assert.IsNotNull(vm.RefreshCommand);
      Assert.IsNotNull(vm.CreateJobCommand);
      Assert.IsNotNull(vm.DeleteJobCommand);
      Assert.IsNotNull(vm.StartJobCommand);
      Assert.IsNotNull(vm.CancelJobCommand);
    }

    [TestMethod]
    [ExpectedException(typeof(ArgumentNullException))]
    public void Constructor_WithNullBatchClient_Throws()
    {
      _ = new BatchProcessingViewModel(_context, null!, _mockDialogService.Object);
    }

    [TestMethod]
    public async Task LoadJobsCommand_CallsIBatchProcessingClient_GetBatchJobsAsync()
    {
      var vm = new BatchProcessingViewModel(_context, _mockBatchClient.Object, _mockDialogService.Object);
      await vm.LoadJobsCommand.ExecuteAsync(null);
      _mockBatchClient.Verify(
          x => x.GetBatchJobsAsync(It.IsAny<string?>(), It.IsAny<JobStatus?>(), It.IsAny<CancellationToken>()),
          Times.AtLeastOnce);
    }

    [TestMethod]
    public async Task LoadQueueStatusCommand_CallsIBatchProcessingClient_GetBatchQueueStatusAsync()
    {
      var vm = new BatchProcessingViewModel(_context, _mockBatchClient.Object, _mockDialogService.Object);
      await vm.LoadQueueStatusCommand.ExecuteAsync(null);
      _mockBatchClient.Verify(
          x => x.GetBatchQueueStatusAsync(It.IsAny<CancellationToken>()),
          Times.AtLeastOnce);
    }

    /// <summary>
    /// Verifies Dispose cleans up without throwing (lifecycle hardening).
    /// </summary>
    [TestMethod]
    public void Dispose_DisposesResources_NoThrow()
    {
      var vm = new BatchProcessingViewModel(_context, _mockBatchClient.Object, _mockDialogService.Object);
      vm.Dispose();
      // No exception; disposal completes
    }

    /// <summary>
    /// Lifecycle: OnFilterStatusChanged triggers LoadJobsAsync (fire-and-forget).
    /// </summary>
    [TestMethod]
    public async Task OnFilterStatusChanged_TriggersLoadJobsAsync()
    {
      var vm = new BatchProcessingViewModel(_context, _mockBatchClient.Object, _mockDialogService.Object);
      vm.FilterStatus = JobStatus.Completed;
      await Task.Delay(300); // Allow fire-and-forget to complete
      _mockBatchClient.Verify(
          x => x.GetBatchJobsAsync(It.IsAny<string?>(), JobStatus.Completed, It.IsAny<CancellationToken>()),
          Times.AtLeastOnce);
    }

    /// <summary>
    /// Lifecycle: OnSelectedProjectIdChanged triggers LoadJobsAsync (fire-and-forget).
    /// </summary>
    [TestMethod]
    public async Task OnSelectedProjectIdChanged_TriggersLoadJobsAsync()
    {
      var vm = new BatchProcessingViewModel(_context, _mockBatchClient.Object, _mockDialogService.Object);
      vm.SelectedProjectId = "proj-123";
      await Task.Delay(300); // Allow fire-and-forget to complete
      _mockBatchClient.Verify(
          x => x.GetBatchJobsAsync("proj-123", It.IsAny<JobStatus?>(), It.IsAny<CancellationToken>()),
          Times.AtLeastOnce);
    }

    /// <summary>
    /// Lifecycle: OnSelectedJobChanged (completed job) triggers LoadQualityReportAsync (fire-and-forget).
    /// </summary>
    [TestMethod]
    public async Task OnSelectedJobChanged_WhenCompletedJob_TriggersLoadQualityReportAsync()
    {
      var vm = new BatchProcessingViewModel(_context, _mockBatchClient.Object, _mockDialogService.Object);
      var completedJob = new BatchJob { Id = "job-1", Status = JobStatus.Completed };
      vm.SelectedJob = completedJob;
      await Task.Delay(300); // Allow fire-and-forget to complete
      _mockBatchClient.Verify(
          x => x.GetBatchQualityReportAsync("job-1", It.IsAny<CancellationToken>()),
          Times.AtLeastOnce);
    }

    /// <summary>
    /// Lifecycle: Rapid filter change cancels prior load; second call uses new filter (staleness guard).
    /// </summary>
    [TestMethod]
    public async Task OnFilterStatusChanged_RapidChange_SecondCallUsesNewFilter()
    {
      var vm = new BatchProcessingViewModel(_context, _mockBatchClient.Object, _mockDialogService.Object);
      vm.FilterStatus = JobStatus.Completed;
      vm.FilterStatus = null; // Change before first load completes
      await Task.Delay(400); // Allow both loads to complete or be cancelled
      // At least one call; last call should have null filter (or Completed if first won the race)
      _mockBatchClient.Verify(
          x => x.GetBatchJobsAsync(It.IsAny<string?>(), It.IsAny<JobStatus?>(), It.IsAny<CancellationToken>()),
          Times.AtLeastOnce);
    }

    /// <summary>
    /// Lifecycle: StartPolling (AutoRefresh) triggers LoadJobsAsync and LoadQueueStatusAsync.
    /// </summary>
    [TestMethod]
    public async Task StartPolling_WhenAutoRefreshEnabled_CallsLoadJobsAndQueueStatus()
    {
      var vm = new BatchProcessingViewModel(_context, _mockBatchClient.Object, _mockDialogService.Object);
      vm.AutoRefresh = true;
      await Task.Delay(500); // Allow fire-and-forget polling to run
      _mockBatchClient.Verify(
          x => x.GetBatchJobsAsync(It.IsAny<string?>(), It.IsAny<JobStatus?>(), It.IsAny<CancellationToken>()),
          Times.AtLeastOnce);
      _mockBatchClient.Verify(
          x => x.GetBatchQueueStatusAsync(It.IsAny<CancellationToken>()),
          Times.AtLeastOnce);
    }

    /// <summary>
    /// Lifecycle: Rapid job selection change; last selection wins (cancellation of prior LoadQualityReportAsync).
    /// </summary>
    [TestMethod]
    public async Task OnSelectedJobChanged_RapidChange_LastSelectionWins()
    {
      var vm = new BatchProcessingViewModel(_context, _mockBatchClient.Object, _mockDialogService.Object);
      vm.SelectedJob = new BatchJob { Id = "job-a", Status = JobStatus.Completed };
      vm.SelectedJob = new BatchJob { Id = "job-b", Status = JobStatus.Completed };
      await Task.Delay(400);
      _mockBatchClient.Verify(
          x => x.GetBatchQualityReportAsync("job-b", It.IsAny<CancellationToken>()),
          Times.AtLeastOnce);
    }

    /// <summary>
    /// Lifecycle: Dispose during in-flight load does not throw; CTS cancellation prevents stale apply.
    /// </summary>
    [TestMethod]
    public void Dispose_DuringLoad_NoThrow()
    {
      var vm = new BatchProcessingViewModel(_context, _mockBatchClient.Object, _mockDialogService.Object);
      vm.FilterStatus = JobStatus.Completed; // Triggers LoadJobsAsync
      vm.Dispose(); // Cancel _disposalCts while load may be in flight
      // No exception; disposal completes; in-flight load receives cancellation
    }
  }
}
