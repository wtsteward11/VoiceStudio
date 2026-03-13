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

    [TestMethod]
    public void Constructor_WithITrainingClient_CreatesInstance()
    {
      var vm = new TrainingViewModel(_context, _mockTrainingClient.Object);
      Assert.IsNotNull(vm);
      Assert.AreEqual("training", vm.PanelId);
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
  }
}
