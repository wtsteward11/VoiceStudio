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
  /// Seam-aware tests for EnsembleSynthesisViewModel.
  /// Instantiates ViewModel with mocked IEnsembleSynthesisClient.
  /// Supports "EnsembleSynthesisViewModel migrated to IEnsembleSynthesisClient" claims.
  /// See docs/governance/TEST_CLASSIFICATION.md.
  /// </summary>
  [TestClass]
  [TestCategory("SeamAware")]
  public class EnsembleSynthesisViewModelSeamTests
  {
    private Mock<IEnsembleSynthesisClient> _mockClient = null!;
    private Mock<IDialogService> _mockDialogService = null!;
    private IViewModelContext _context = null!;
    private DispatcherQueueController? _dispatcherController;

    [TestInitialize]
    public void Setup()
    {
      TestAppServicesHelper.EnsureInitialized();
      _mockClient = new Mock<IEnsembleSynthesisClient>();
      _mockDialogService = new Mock<IDialogService>();
      _dispatcherController = DispatcherQueueController.CreateOnDedicatedThread();
      var dispatcher = _dispatcherController.DispatcherQueue;
      _context = new ViewModelContext(NullLogger.Instance, dispatcher);

      _mockClient
          .Setup(x => x.GetEnginesAsync(It.IsAny<CancellationToken>()))
          .ReturnsAsync(new List<string> { "xtts", "piper" });
      _mockClient
          .Setup(x => x.ListJobsAsync(It.IsAny<string?>(), It.IsAny<CancellationToken>()))
          .ReturnsAsync(Array.Empty<EnsembleJobStatus>());
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
      _ = new EnsembleSynthesisViewModel(_context, _mockClient.Object, _mockDialogService.Object);
      _mockClient.Verify(x => x.GetEnginesAsync(It.IsAny<CancellationToken>()), Times.Never);
      _mockClient.Verify(x => x.ListJobsAsync(It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [TestMethod]
    public void Constructor_WithIEnsembleSynthesisClient_CreatesInstance()
    {
      var vm = new EnsembleSynthesisViewModel(_context, _mockClient.Object, _mockDialogService.Object);
      Assert.IsNotNull(vm);
      Assert.AreEqual(PanelIds.EnsembleSynthesis, vm.PanelId);
      Assert.IsNotNull(vm.AddVoiceCommand);
      Assert.IsNotNull(vm.SynthesizeCommand);
      Assert.IsNotNull(vm.LoadJobsCommand);
      Assert.IsNotNull(vm.RefreshCommand);
    }

    [TestMethod]
    [ExpectedException(typeof(ArgumentNullException))]
    public void Constructor_WithNullClient_Throws()
    {
      _ = new EnsembleSynthesisViewModel(_context, null!, _mockDialogService.Object);
    }

    [TestMethod]
    public async Task RefreshCommand_CallsIEnsembleSynthesisClient_GetEnginesAndListJobs()
    {
      var vm = new EnsembleSynthesisViewModel(_context, _mockClient.Object, _mockDialogService.Object);
      await vm.RefreshCommand.ExecuteAsync(null);
      _mockClient.Verify(x => x.GetEnginesAsync(It.IsAny<CancellationToken>()), Times.AtLeastOnce);
      _mockClient.Verify(x => x.ListJobsAsync(It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.AtLeastOnce);
    }

    [TestMethod]
    public async Task LoadJobsCommand_CallsIEnsembleSynthesisClient_ListJobsAsync()
    {
      var vm = new EnsembleSynthesisViewModel(_context, _mockClient.Object, _mockDialogService.Object);
      await vm.LoadJobsCommand.ExecuteAsync(null);
      _mockClient.Verify(
          x => x.ListJobsAsync(It.IsAny<string?>(), It.IsAny<CancellationToken>()),
          Times.AtLeastOnce);
    }

    [TestMethod]
    public async Task SynthesizeCommand_WhenVoicesSet_CallsCreateSynthesisAsync()
    {
      _mockClient
          .Setup(x => x.CreateSynthesisAsync(It.IsAny<EnsembleSynthesisRequest>(), It.IsAny<CancellationToken>()))
          .ReturnsAsync(new EnsembleSynthesisResponse { JobId = "job-1", Message = "Queued" });

      var vm = new EnsembleSynthesisViewModel(_context, _mockClient.Object, _mockDialogService.Object);
      await vm.AddVoiceCommand.ExecuteAsync(null);
      await vm.SynthesizeCommand.ExecuteAsync(null);

      _mockClient.Verify(
          x => x.CreateSynthesisAsync(It.Is<EnsembleSynthesisRequest>(r => r.Voices.Length == 1), It.IsAny<CancellationToken>()),
          Times.Once);
    }

    [TestMethod]
    public async Task DeleteJobCommand_CallsDeleteJobAsync()
    {
      _mockClient
          .Setup(x => x.ListJobsAsync(It.IsAny<string?>(), It.IsAny<CancellationToken>()))
          .ReturnsAsync(new[] { new EnsembleJobStatus { JobId = "job-1", Status = "completed" } });

      var vm = new EnsembleSynthesisViewModel(_context, _mockClient.Object, _mockDialogService.Object);
      await vm.LoadJobsCommand.ExecuteAsync(null);
      var job = vm.Jobs[0];
      _mockDialogService.Setup(x => x.ShowConfirmationAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
          .ReturnsAsync(true);
      await vm.DeleteJobCommand.ExecuteAsync(job);

      _mockClient.Verify(
          x => x.DeleteJobAsync("job-1", It.IsAny<CancellationToken>()),
          Times.Once);
    }
  }
}
