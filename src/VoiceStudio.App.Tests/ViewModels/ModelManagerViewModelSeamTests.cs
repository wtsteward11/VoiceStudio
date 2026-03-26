using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.UI.Dispatching;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using System;
using System.Threading;
using VoiceStudio.App.Tests.Fixtures;
using VoiceStudio.App.ViewModels;
using VoiceStudio.App.Views.Panels;
using VoiceStudio.Core.Panels;
using VoiceStudio.Core.Services;

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
      _dispatcherController?.ShutdownQueueAsync().AsTask().GetAwaiter().GetResult();
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
  }
}
