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
using VoiceStudio.Core.Models;
using VoiceStudio.Core.Panels;
using VoiceStudio.Core.Services;

namespace VoiceStudio.App.Tests.ViewModels
{
  /// <summary>
  /// Seam-aware tests for TrainingQualityVisualizationViewModel.
  /// Instantiates ViewModel with mocked ITrainingClient.
  /// Supports "TrainingQualityVisualizationViewModel migrated to ITrainingClient" claims.
  /// See docs/governance/TEST_CLASSIFICATION.md.
  /// </summary>
  [TestClass]
  [TestCategory("SeamAware")]
  public class TrainingQualityVisualizationViewModelSeamTests
  {
    private Mock<ITrainingClient> _mockTrainingClient = null!;
    private IViewModelContext _context = null!;
    private DispatcherQueueController? _dispatcherController;

    [TestInitialize]
    public void Setup()
    {
      TestAppServicesHelper.EnsureInitialized();
      _mockTrainingClient = new Mock<ITrainingClient>();
      _dispatcherController = DispatcherQueueController.CreateOnDedicatedThread();
      var dispatcher = _dispatcherController.DispatcherQueue;
      _context = new ViewModelContext(NullLogger.Instance, dispatcher);

      _mockTrainingClient
          .Setup(x => x.ListTrainingJobsAsync(It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
          .ReturnsAsync(new List<TrainingStatus>());
      _mockTrainingClient
          .Setup(x => x.GetTrainingQualityHistoryAsync(It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()))
          .ReturnsAsync(new List<TrainingQualityMetrics>());
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
      _ = new TrainingQualityVisualizationViewModel(_context, _mockTrainingClient.Object);

      _mockTrainingClient.Verify(
          x => x.ListTrainingJobsAsync(It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()),
          Times.Never);
      _mockTrainingClient.Verify(
          x => x.GetTrainingQualityHistoryAsync(It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()),
          Times.Never);
    }

    [TestMethod]
    public void Constructor_WithClient_CreatesInstance()
    {
      var vm = new TrainingQualityVisualizationViewModel(_context, _mockTrainingClient.Object);

      Assert.IsNotNull(vm);
      Assert.AreEqual("training-quality-visualization", vm.PanelId);
      Assert.IsNotNull(vm.LoadTrainingJobsCommand);
      Assert.IsNotNull(vm.LoadQualityHistoryCommand);
      Assert.IsNotNull(vm.RefreshCommand);
    }

    [TestMethod]
    [ExpectedException(typeof(ArgumentNullException))]
    public void Constructor_WithNullTrainingClient_Throws()
    {
      _ = new TrainingQualityVisualizationViewModel(_context, null!);
    }

    [TestMethod]
    public void ViewModel_ImplementsIPanelLifecycle()
    {
      var vm = new TrainingQualityVisualizationViewModel(_context, _mockTrainingClient.Object);
      Assert.IsTrue(vm is IPanelLifecycle);
    }

    [TestMethod]
    public async Task OnActivatedAsync_CallsITrainingClient_ListTrainingJobsAsync()
    {
      var vm = new TrainingQualityVisualizationViewModel(_context, _mockTrainingClient.Object);

      await vm.OnActivatedAsync(CancellationToken.None);

      _mockTrainingClient.Verify(
          x => x.ListTrainingJobsAsync(It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()),
          Times.Once);
    }

    /// <summary>
    /// Lifecycle: Rapid selection change does not apply stale quality history.
    /// Staleness guard discards result when selection changed after request started.
    /// </summary>
    [TestMethod]
    public async Task RapidSelectionChange_DoesNotApplyStaleResults()
    {
      var jobAMetrics = new List<TrainingQualityMetrics> { new() { Epoch = 999, QualityScore = 0.5 } };
      var jobBMetrics = new List<TrainingQualityMetrics> { new() { Epoch = 1, QualityScore = 0.9 } };
      var mockClient = new Mock<ITrainingClient>();
      mockClient
          .Setup(x => x.ListTrainingJobsAsync(It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
          .ReturnsAsync(new List<TrainingStatus> { new() { Id = "job-a" }, new() { Id = "job-b" } });
      mockClient
          .Setup(x => x.GetTrainingQualityHistoryAsync("job-a", It.IsAny<int?>(), It.IsAny<CancellationToken>()))
          .Returns(async () =>
          {
            await Task.Delay(150);
            return jobAMetrics;
          });
      mockClient
          .Setup(x => x.GetTrainingQualityHistoryAsync("job-b", It.IsAny<int?>(), It.IsAny<CancellationToken>()))
          .ReturnsAsync(jobBMetrics);

      var vm = new TrainingQualityVisualizationViewModel(_context, mockClient.Object);
      await vm.OnActivatedAsync(CancellationToken.None);

      vm.SelectedTrainingJobId = "job-a";
      await Task.Delay(20);
      vm.SelectedTrainingJobId = "job-b";
      await Task.Delay(200);

      Assert.AreEqual(1, vm.QualityHistory.Count);
      Assert.AreEqual(1, vm.QualityHistory[0].Epoch);
      Assert.AreEqual(0.9, vm.QualityHistory[0].QualityScore);
    }

    /// <summary>
    /// Lifecycle: Rapid selection change; last selection wins (cancellation of prior LoadQualityHistoryForSelectionAsync).
    /// </summary>
    [TestMethod]
    public async Task OnSelectedTrainingJobIdChanged_RapidChange_LastSelectionWins()
    {
      var vm = new TrainingQualityVisualizationViewModel(_context, _mockTrainingClient.Object);
      await vm.OnActivatedAsync(CancellationToken.None);

      vm.SelectedTrainingJobId = "job-a";
      vm.SelectedTrainingJobId = "job-b";
      await Task.Delay(400);

      _mockTrainingClient.Verify(
          x => x.GetTrainingQualityHistoryAsync("job-b", It.IsAny<int?>(), It.IsAny<CancellationToken>()),
          Times.AtLeastOnce);
    }

    /// <summary>
    /// Lifecycle: When client throws OperationCanceledException, QualityHistory remains empty; no crash.
    /// </summary>
    [TestMethod]
    public async Task SelectionLoad_WhenClientThrowsCancelled_DoesNotOverwriteQualityHistory()
    {
      var mockClient = new Mock<ITrainingClient>();
      mockClient
          .Setup(x => x.ListTrainingJobsAsync(It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
          .ReturnsAsync(new List<TrainingStatus>());
      mockClient
          .Setup(x => x.GetTrainingQualityHistoryAsync(It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()))
          .ThrowsAsync(new OperationCanceledException());

      var vm = new TrainingQualityVisualizationViewModel(_context, mockClient.Object);
      await vm.OnActivatedAsync(CancellationToken.None);

      vm.SelectedTrainingJobId = "job-x";
      await Task.Delay(200);

      Assert.AreEqual(0, vm.QualityHistory.Count);
      Assert.IsFalse(vm.HasData);
    }
  }
}
