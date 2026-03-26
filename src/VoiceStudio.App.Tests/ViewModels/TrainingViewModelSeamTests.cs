using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.UI.Dispatching;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using VoiceStudio.App.Services;
using VoiceStudio.App.ViewModels;
using VoiceStudio.App.Views.Panels;
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
      _dispatcherController?.ShutdownQueueAsync().AsTask().GetAwaiter().GetResult();
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
  }
}
