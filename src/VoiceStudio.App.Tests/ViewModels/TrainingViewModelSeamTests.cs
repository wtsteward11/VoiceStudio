using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.UI.Dispatching;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using VoiceStudio.App.Core.Models;
using VoiceStudio.App.Services;
using VoiceStudio.App.Tests.Fixtures;
using VoiceStudio.App.ViewModels;
using VoiceStudio.App.Views.Panels;
using VoiceStudio.Core.Events;
using VoiceStudio.Core.Models;
using VoiceStudio.Core.Panels;
using VoiceStudio.Core.Services;

namespace VoiceStudio.App.Tests.ViewModels
{
  /// <summary>
  /// Seam-aware tests for TrainingViewModel.
  /// Instantiates TrainingViewModel with mocked ITrainingClient.
  /// Supports "TrainingViewModel migrated to ITrainingClient" claims.
  /// See docs/governance/TEST_CLASSIFICATION.md.
  /// </summary>
  [TestClass]
  [TestCategory("SeamAware")]
  public class TrainingViewModelSeamTests
  {
    private Mock<ITrainingClient> _mockTrainingClient = null!;
    private IViewModelContext _context = null!;
    private IServiceProvider _provider = null!;
    private DispatcherQueueController? _dispatcherController;

    [TestInitialize]
    public void Setup()
    {
      _mockTrainingClient = new Mock<ITrainingClient>();
      _dispatcherController = DispatcherQueueController.CreateOnDedicatedThread();
      var dispatcher = _dispatcherController.DispatcherQueue;
      _context = new ViewModelContext(NullLogger.Instance, dispatcher);

      var services = new ServiceCollection();
      services.AddSingleton<MultiSelectService>();
      services.AddSingleton<IEventAggregator, EventAggregator>();
      _provider = services.BuildServiceProvider();
      AppServices.Initialize(_provider);

      _mockTrainingClient
          .Setup(x => x.ListDatasetsAsync(It.IsAny<CancellationToken>()))
          .ReturnsAsync(new List<TrainingDataset>());
      _mockTrainingClient
          .Setup(x => x.ListTrainingJobsAsync(It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
          .ReturnsAsync(new List<TrainingStatus>());
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
      _ = new TrainingViewModel(_context, _mockTrainingClient.Object);
      _mockTrainingClient.Verify(x => x.ListDatasetsAsync(It.IsAny<CancellationToken>()), Times.Never);
      _mockTrainingClient.Verify(x => x.ListTrainingJobsAsync(It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [TestMethod]
    public void Constructor_WithITrainingClient_CreatesInstance()
    {
      var vm = new TrainingViewModel(_context, _mockTrainingClient.Object);
      Assert.IsNotNull(vm);
      Assert.AreEqual(PanelIds.Training, vm.PanelId);
      Assert.IsNotNull(vm.LoadDatasetsCommand);
      Assert.IsNotNull(vm.LoadTrainingJobsCommand);
      vm.Dispose();
    }

    [TestMethod]
    [ExpectedException(typeof(ArgumentNullException))]
    public void Constructor_WithNullTrainingClient_Throws()
    {
      _ = new TrainingViewModel(_context, null!);
    }

    [TestMethod]
    public async Task InitializeAsync_CallsITrainingClient_ListDatasetsAsync()
    {
      var vm = new TrainingViewModel(_context, _mockTrainingClient.Object);
      try
      {
        await vm.InitializeAsync(CancellationToken.None);
        _mockTrainingClient.Verify(
            x => x.ListDatasetsAsync(It.IsAny<CancellationToken>()),
            Times.Once);
      }
      finally
      {
        vm.Dispose();
      }
    }

    [TestMethod]
    public async Task InitializeAsync_CallsITrainingClient_ListTrainingJobsAsync()
    {
      var vm = new TrainingViewModel(_context, _mockTrainingClient.Object);
      try
      {
        await vm.InitializeAsync(CancellationToken.None);
        _mockTrainingClient.Verify(
            x => x.ListTrainingJobsAsync(It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()),
            Times.Once);
      }
      finally
      {
        vm.Dispose();
      }
    }

    /// <summary>Product trust Pass 01 slice 3: Training surface discloses partial / not workflow-pass-closed.</summary>
    [TestMethod]
    public void SurfaceMaturityFootnote_DisclosesPartialAndWorkflowHonesty()
    {
      var vm = new TrainingViewModel(_context, _mockTrainingClient.Object);
      try
      {
        var text = vm.SurfaceMaturityFootnote;
        StringAssert.Contains(text, "partial", StringComparison.OrdinalIgnoreCase);
        StringAssert.Contains(text, "workflow", StringComparison.OrdinalIgnoreCase);
      }
      finally
      {
        vm.Dispose();
      }
    }

    /// <summary>GAP-028: polling completion path publishes ProfileCreated + ProfileUpdated.</summary>
    [TestMethod]
    public void Gap028_PollingComplete_WithProfileId_PublishesProfileCreatedAndUpdated()
    {
      var vm = new TrainingViewModel(_context, _mockTrainingClient.Object);
      try
      {
        var createdCount = 0;
        var updatedCount = 0;
        var agg = AppServices.TryGetEventAggregator();
        Assert.IsNotNull(agg);
        using var _ = agg.Subscribe<ProfileCreatedEvent>(_ => createdCount++);
        using var __ = agg.Subscribe<ProfileUpdatedEvent>(_ => updatedCount++);

        var status = new TrainingStatus
        {
          Id = "job-gap028-a",
          Status = "completed",
          ProfileId = "prof-gap028",
          Engine = "xtts",
        };
        vm.SeamTryPublishPollingTrainingCompletion(status);

        Assert.AreEqual(1, createdCount, "ProfileCreatedEvent expected once");
        Assert.AreEqual(1, updatedCount, "ProfileUpdatedEvent expected once");
      }
      finally
      {
        vm.Dispose();
      }
    }

    /// <summary>GAP-028: repeated polling for the same completed job must not republish.</summary>
    [TestMethod]
    public void Gap028_PollingComplete_DuplicatePoll_DoesNotPublishTwice()
    {
      var vm = new TrainingViewModel(_context, _mockTrainingClient.Object);
      try
      {
        var createdCount = 0;
        var updatedCount = 0;
        var agg = AppServices.TryGetEventAggregator();
        Assert.IsNotNull(agg);
        using var _ = agg.Subscribe<ProfileCreatedEvent>(_ => createdCount++);
        using var __ = agg.Subscribe<ProfileUpdatedEvent>(_ => updatedCount++);

        var status = new TrainingStatus
        {
          Id = "job-gap028-dup",
          Status = "completed",
          ProfileId = "prof-dup",
          Engine = "rvc",
        };
        vm.SeamTryPublishPollingTrainingCompletion(status);
        vm.SeamTryPublishPollingTrainingCompletion(status);

        Assert.AreEqual(1, createdCount);
        Assert.AreEqual(1, updatedCount);
      }
      finally
      {
        vm.Dispose();
      }
    }

    /// <summary>GAP-028: non-terminal status does not publish profile events.</summary>
    [TestMethod]
    public void Gap028_PollingRunning_DoesNotPublishProfileEvents()
    {
      var vm = new TrainingViewModel(_context, _mockTrainingClient.Object);
      try
      {
        var eventCount = 0;
        var agg = AppServices.TryGetEventAggregator();
        Assert.IsNotNull(agg);
        using var _ = agg.Subscribe<ProfileCreatedEvent>(_ => eventCount++);
        using var __ = agg.Subscribe<ProfileUpdatedEvent>(_ => eventCount++);

        var status = new TrainingStatus
        {
          Id = "job-run",
          Status = "running",
          ProfileId = "prof-x",
          Engine = "xtts",
        };
        vm.SeamTryPublishPollingTrainingCompletion(status);

        Assert.AreEqual(0, eventCount);
      }
      finally
      {
        vm.Dispose();
      }
    }

    /// <summary>GAP-028: completed without profile id does not publish.</summary>
    [TestMethod]
    public void Gap028_PollingComplete_EmptyProfileId_DoesNotPublish()
    {
      var vm = new TrainingViewModel(_context, _mockTrainingClient.Object);
      try
      {
        var eventCount = 0;
        var agg = AppServices.TryGetEventAggregator();
        Assert.IsNotNull(agg);
        using var _ = agg.Subscribe<ProfileCreatedEvent>(_ => eventCount++);
        using var __ = agg.Subscribe<ProfileUpdatedEvent>(_ => eventCount++);

        var status = new TrainingStatus
        {
          Id = "job-noprof",
          Status = "completed",
          ProfileId = "",
          Engine = "xtts",
        };
        vm.SeamTryPublishPollingTrainingCompletion(status);

        Assert.AreEqual(0, eventCount);
      }
      finally
      {
        vm.Dispose();
      }
    }

    /// <summary>GAP-024: simulation terminal state must not publish profile trained events (polling path).</summary>
    [TestMethod]
    public void Gap024_PollingSimulationComplete_DoesNotPublishProfileEvents()
    {
      var vm = new TrainingViewModel(_context, _mockTrainingClient.Object);
      try
      {
        var eventCount = 0;
        var agg = AppServices.TryGetEventAggregator();
        Assert.IsNotNull(agg);
        using var _ = agg.Subscribe<ProfileCreatedEvent>(_ => eventCount++);
        using var __ = agg.Subscribe<ProfileUpdatedEvent>(_ => eventCount++);

        var status = new TrainingStatus
        {
          Id = "job-sim",
          Status = "simulation_complete",
          ProfileId = "prof-sim",
          Engine = "xtts",
          SimulationMode = true,
        };
        vm.SeamTryPublishPollingTrainingCompletion(status);

        Assert.AreEqual(0, eventCount);
      }
      finally
      {
        vm.Dispose();
      }
    }

    /// <summary>GAP-024: helpers distinguish real completion from simulation.</summary>
    [TestMethod]
    public void Gap024_IsRealTrainingCompletion_ExcludesSimulation()
    {
      Assert.IsFalse(TrainingViewModel.IsRealTrainingCompletion(new TrainingStatus
      {
        Status = "simulation_complete",
        SimulationMode = true,
      }));
      Assert.IsFalse(TrainingViewModel.IsRealTrainingCompletion(new TrainingStatus
      {
        Status = "completed",
        SimulationMode = true,
      }));
      Assert.IsTrue(TrainingViewModel.IsRealTrainingCompletion(new TrainingStatus
      {
        Status = "completed",
        SimulationMode = false,
      }));
    }

    /// <summary>GAP-028: seam helper emits both events (shared path with WebSocket completion).</summary>
    [TestMethod]
    public void Gap028_SeamPublishTrainingCompletedProfileEvents_EmitsCreatedAndUpdated()
    {
      var vm = new TrainingViewModel(_context, _mockTrainingClient.Object);
      try
      {
        var createdCount = 0;
        var updatedCount = 0;
        var agg = AppServices.TryGetEventAggregator();
        Assert.IsNotNull(agg);
        using var _ = agg.Subscribe<ProfileCreatedEvent>(_ => createdCount++);
        using var __ = agg.Subscribe<ProfileUpdatedEvent>(_ => updatedCount++);

        var job = new TrainingStatus
        {
          Id = "job-ws-parity",
          Status = "completed",
          ProfileId = "prof-ws",
          Engine = "coqui",
        };
        vm.SeamPublishTrainingCompletedProfileEvents(job);

        Assert.AreEqual(1, createdCount);
        Assert.AreEqual(1, updatedCount);
      }
      finally
      {
        vm.Dispose();
      }
    }
  }
}
