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
using VoiceStudio.Core.Events;
using VoiceStudio.Core.Models;
using VoiceStudio.Core.Panels;
using VoiceStudio.Core.Services;

namespace VoiceStudio.App.Tests.ViewModels
{
  /// <summary>
  /// GAP-030 acceptance tests: QualityDashboardViewModel refreshes on batch JobCompletedEvent.
  /// </summary>
  [TestClass]
  [TestCategory("SeamAware")]
  [TestCategory("GAP-030")]
  public class QualityDashboardGap030Tests
  {
    private Mock<IQualityControlClient> _mockQualityClient = null!;
    private Mock<IEventAggregator> _mockEventAggregator = null!;
    private Mock<ISubscriptionToken> _mockToken = null!;
    private IViewModelContext _context = null!;
    private DispatcherQueueController? _dispatcherController;
    private Action<JobCompletedEvent>? _capturedHandler;

    [TestInitialize]
    public void Setup()
    {
      TestAppServicesHelper.EnsureInitialized();
      _mockQualityClient = new Mock<IQualityControlClient>();
      _mockEventAggregator = new Mock<IEventAggregator>();
      _mockToken = new Mock<ISubscriptionToken>();
      _mockToken.Setup(t => t.IsActive).Returns(true);
      _dispatcherController = DispatcherQueueController.CreateOnDedicatedThread();
      var dispatcher = _dispatcherController.DispatcherQueue;
      _context = new ViewModelContext(NullLogger.Instance, dispatcher);

      _mockQualityClient.Setup(x => x.GetQualityDashboardAsync(
          It.IsAny<string?>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
        .ReturnsAsync(new QualityDashboard());

      _mockQualityClient.Setup(x => x.GetQualityPresetsAsync(It.IsAny<CancellationToken>()))
        .ReturnsAsync(new System.Collections.Generic.Dictionary<string, QualityPresetInfo>());

      _mockEventAggregator
        .Setup(e => e.Subscribe(It.IsAny<Action<JobCompletedEvent>>()))
        .Callback<Action<JobCompletedEvent>>(handler => _capturedHandler = handler)
        .Returns(_mockToken.Object);
    }

    [TestCleanup]
    public void Cleanup()
    {
      _dispatcherController?.ShutdownQueueAsync().AsTask().GetAwaiter().GetResult();
    }

    [TestMethod]
    public async Task InitializeAsync_SubscribesToJobCompletedEvent()
    {
      var vm = new QualityDashboardViewModel(_context, _mockQualityClient.Object, _mockEventAggregator.Object);
      await vm.InitializeAsync();

      _mockEventAggregator.Verify(
          e => e.Subscribe(It.IsAny<Action<JobCompletedEvent>>()),
          Times.Once,
          "InitializeAsync must subscribe to JobCompletedEvent");
    }

    [TestMethod]
    public async Task OnBatchJobCompleted_Success_RefreshesOverview()
    {
      var vm = new QualityDashboardViewModel(_context, _mockQualityClient.Object, _mockEventAggregator.Object);
      await vm.InitializeAsync();

      _mockQualityClient.Invocations.Clear();

      Assert.IsNotNull(_capturedHandler, "Handler must be captured via Subscribe");

      var evt = JobCompletedEvent.Succeeded("BatchProcessing", "job-1", "batch", result: "audio-1");
      _capturedHandler(evt);

      // Allow async LoadOverviewAsync to start
      await Task.Delay(200);

      _mockQualityClient.Verify(
          x => x.GetQualityDashboardAsync(It.IsAny<string?>(), It.IsAny<int>(), It.IsAny<CancellationToken>()),
          Times.AtLeastOnce,
          "Successful batch event must trigger dashboard refresh");
    }

    [TestMethod]
    public async Task OnBatchJobCompleted_Failed_DoesNotRefresh()
    {
      var vm = new QualityDashboardViewModel(_context, _mockQualityClient.Object, _mockEventAggregator.Object);
      await vm.InitializeAsync();

      _mockQualityClient.Invocations.Clear();

      Assert.IsNotNull(_capturedHandler, "Handler must be captured via Subscribe");

      var evt = JobCompletedEvent.Failed("BatchProcessing", "job-1", "batch", "Some error");
      _capturedHandler(evt);

      await Task.Delay(200);

      _mockQualityClient.Verify(
          x => x.GetQualityDashboardAsync(It.IsAny<string?>(), It.IsAny<int>(), It.IsAny<CancellationToken>()),
          Times.Never,
          "Failed batch event must NOT trigger dashboard refresh");
    }

    [TestMethod]
    public async Task OnNonBatchJobCompleted_DoesNotRefresh()
    {
      var vm = new QualityDashboardViewModel(_context, _mockQualityClient.Object, _mockEventAggregator.Object);
      await vm.InitializeAsync();

      _mockQualityClient.Invocations.Clear();

      Assert.IsNotNull(_capturedHandler, "Handler must be captured via Subscribe");

      var evt = JobCompletedEvent.Succeeded("Training", "job-2", "training", result: "model-1");
      _capturedHandler(evt);

      await Task.Delay(200);

      _mockQualityClient.Verify(
          x => x.GetQualityDashboardAsync(It.IsAny<string?>(), It.IsAny<int>(), It.IsAny<CancellationToken>()),
          Times.Never,
          "Non-batch event must NOT trigger dashboard refresh");
    }

    [TestMethod]
    public void Constructor_WithoutEventAggregator_DoesNotThrow()
    {
      var vm = new QualityDashboardViewModel(_context, _mockQualityClient.Object, eventAggregator: null);
      Assert.IsNotNull(vm);
    }
  }
}
