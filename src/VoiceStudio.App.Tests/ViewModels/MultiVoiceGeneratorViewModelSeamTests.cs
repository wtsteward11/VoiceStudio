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
using VoiceStudio.Core.Panels;
using VoiceStudio.Core.Services;

namespace VoiceStudio.App.Tests.ViewModels
{
  /// <summary>
  /// Seam-aware tests for MultiVoiceGeneratorViewModel.
  /// Instantiates ViewModel with mocked IMultiVoiceGeneratorClient.
  /// Supports "MultiVoiceGeneratorViewModel migrated to IMultiVoiceGeneratorClient" claims.
  /// See docs/governance/TEST_CLASSIFICATION.md.
  /// </summary>
  [TestClass]
  [TestCategory("SeamAware")]
  public class MultiVoiceGeneratorViewModelSeamTests
  {
    private Mock<IMultiVoiceGeneratorClient> _mockClient = null!;
    private IViewModelContext _context = null!;
    private DispatcherQueueController? _dispatcherController;

    [TestInitialize]
    public void Setup()
    {
      TestAppServicesHelper.EnsureInitialized();
      _mockClient = new Mock<IMultiVoiceGeneratorClient>();
      _dispatcherController = DispatcherQueueController.CreateOnDedicatedThread();
      var dispatcher = _dispatcherController.DispatcherQueue;
      _context = new ViewModelContext(NullLogger.Instance, dispatcher);

      _mockClient
          .Setup(x => x.GetEnginesAsync(It.IsAny<CancellationToken>()))
          .ReturnsAsync(new List<string> { "xtts", "piper" });
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
      _ = new MultiVoiceGeneratorViewModel(_context, _mockClient.Object);
      _mockClient.Verify(x => x.GetEnginesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [TestMethod]
    public void Constructor_WithIMultiVoiceGeneratorClient_CreatesInstance()
    {
      var vm = new MultiVoiceGeneratorViewModel(_context, _mockClient.Object);
      Assert.IsNotNull(vm);
      Assert.AreEqual(PanelIds.MultiVoiceGenerator, vm.PanelId);
      Assert.IsNotNull(vm.AddToQueueCommand);
      Assert.IsNotNull(vm.ImportCSVCommand);
      Assert.IsNotNull(vm.ExportCSVCommand);
      Assert.IsNotNull(vm.StartGenerationCommand);
      Assert.IsNotNull(vm.RefreshCommand);
    }

    [TestMethod]
    [ExpectedException(typeof(ArgumentNullException))]
    public void Constructor_WithNullClient_Throws()
    {
      _ = new MultiVoiceGeneratorViewModel(_context, null!);
    }

    [TestMethod]
    public async Task RefreshCommand_CallsIMultiVoiceGeneratorClient_GetEnginesAsync()
    {
      var vm = new MultiVoiceGeneratorViewModel(_context, _mockClient.Object);
      await vm.RefreshCommand.ExecuteAsync(null);
      _mockClient.Verify(
          x => x.GetEnginesAsync(It.IsAny<CancellationToken>()),
          Times.AtLeastOnce);
    }

    [TestMethod]
    public async Task StartGenerationCommand_WhenQueueAndNameSet_CallsGenerateAsync()
    {
      _mockClient
          .Setup(x => x.GenerateAsync(It.IsAny<MultiVoiceGenerateRequest>(), It.IsAny<CancellationToken>()))
          .ReturnsAsync(new MultiVoiceGenerateResponse { JobId = "job-1", Status = "pending" });

      var vm = new MultiVoiceGeneratorViewModel(_context, _mockClient.Object);
      vm.NewItemProfileId = "profile-1";
      vm.NewItemText = "Hello";
      await vm.AddToQueueCommand.ExecuteAsync(null);
      vm.CurrentJobName = "Test Job";
      await vm.StartGenerationCommand.ExecuteAsync(null);

      _mockClient.Verify(
          x => x.GenerateAsync(It.Is<MultiVoiceGenerateRequest>(r => r.Name == "Test Job" && r.Items.Count == 1), It.IsAny<CancellationToken>()),
          Times.Once);
    }

    [TestMethod]
    public async Task LoadJobStatusCommand_WhenJobIdSet_CallsGetJobStatusAsync()
    {
      _mockClient
          .Setup(x => x.GetJobStatusAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
          .ReturnsAsync(new MultiVoiceJobStatusResponse { JobId = "job-1", Status = "completed", Progress = 1f });

      var vm = new MultiVoiceGeneratorViewModel(_context, _mockClient.Object);
      vm.CurrentJobId = "job-1";
      await vm.LoadJobStatusCommand.ExecuteAsync(null);

      _mockClient.Verify(
          x => x.GetJobStatusAsync("job-1", It.IsAny<CancellationToken>()),
          Times.Once);
    }

    [TestMethod]
    public async Task LoadResultsCommand_WhenJobIdSet_CallsGetResultsAsync()
    {
      _mockClient
          .Setup(x => x.GetResultsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
          .ReturnsAsync(new MultiVoiceResultsResponse { JobId = "job-1", Items = new List<MultiVoiceResultItem>() });

      var vm = new MultiVoiceGeneratorViewModel(_context, _mockClient.Object);
      vm.CurrentJobId = "job-1";
      await vm.LoadResultsCommand.ExecuteAsync(null);

      _mockClient.Verify(
          x => x.GetResultsAsync("job-1", It.IsAny<CancellationToken>()),
          Times.Once);
    }
  }
}
