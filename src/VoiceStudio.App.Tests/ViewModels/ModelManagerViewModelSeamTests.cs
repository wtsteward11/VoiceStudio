using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.UI.Dispatching;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using System;
using System.Threading;
using System.Threading.Tasks;
using VoiceStudio.App.Tests.Fixtures;
using VoiceStudio.App.ViewModels;
using VoiceStudio.App.Views.Panels;
using VoiceStudio.Core.Models;
using VoiceStudio.Core.Panels;
using VoiceStudio.Core.Services;
using JobDto = VoiceStudio.App.Services.Job;

namespace VoiceStudio.App.Tests.ViewModels
{
  /// <summary>
  /// Seam-aware tests for ModelManagerViewModel.
  /// Instantiates ViewModel with mocked IModelManagerClient.
  /// Supports "ModelManagerViewModel migrated to IModelManagerClient" claims.
  /// See docs/governance/TEST_CLASSIFICATION.md.
  /// </summary>
  [TestClass]
  [TestCategory("SeamAware")]
  public class ModelManagerViewModelSeamTests
  {
    private Mock<IModelManagerClient> _mockModelManagerClient = null!;
    private IViewModelContext _context = null!;
    private DispatcherQueueController? _dispatcherController;

    [TestInitialize]
    public void Setup()
    {
      TestAppServicesHelper.EnsureInitialized();
      _mockModelManagerClient = new Mock<IModelManagerClient>();
      _dispatcherController = DispatcherQueueController.CreateOnDedicatedThread();
      var dispatcher = _dispatcherController.DispatcherQueue;
      _context = new ViewModelContext(NullLogger.Instance, dispatcher);
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
      _ = new ModelManagerViewModel(_context, _mockModelManagerClient.Object);
      _mockModelManagerClient.Verify(x => x.GetModelsAsync(It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never);
      _mockModelManagerClient.Verify(x => x.GetStorageStatsAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [TestMethod]
    public void Constructor_WithClients_CreatesInstance()
    {
      var vm = new ModelManagerViewModel(_context, _mockModelManagerClient.Object);
      Assert.IsNotNull(vm);
      Assert.AreEqual(PanelIds.ModelManager, vm.PanelId);
    }

    [TestMethod]
    [ExpectedException(typeof(ArgumentNullException))]
    public void Constructor_WithNullModelManagerClient_Throws()
    {
      _ = new ModelManagerViewModel(_context, null!);
    }

    [TestMethod]
    public async Task StartModelDownloadCommand_InvokesModelManagerClient_StartModelDownloadAsync()
    {
      var jobs = new Mock<IJobProgressApiClient>();
      _mockModelManagerClient
          .Setup(x => x.StartModelDownloadAsync(It.IsAny<ModelDownloadStartRequest>(), It.IsAny<CancellationToken>()))
          .ReturnsAsync(new ModelDownloadStartResponse { JobId = "job-1" });
      jobs.Setup(x => x.GetJobAsync("job-1", It.IsAny<CancellationToken>()))
          .ReturnsAsync((JobDto?)new JobDto { Id = "job-1", Status = "completed", Progress = 1.0 });

      var vm = new ModelManagerViewModel(_context, _mockModelManagerClient.Object, jobs.Object)
      {
        DownloadUrl = "https://example.com/m.zip",
        DownloadModelName = "m1",
        DownloadVersion = "1.0",
        DownloadTargetEngine = "xtts_v2",
      };

      await vm.StartModelDownloadCommand.ExecuteAsync(null);

      _mockModelManagerClient.Verify(
          x => x.StartModelDownloadAsync(
              It.Is<ModelDownloadStartRequest>(r => r.Url.Contains("example.com", StringComparison.Ordinal) && r.Engine == "xtts_v2"),
              It.IsAny<CancellationToken>()),
          Times.Once);
    }
  }
}
