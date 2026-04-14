using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.UI.Dispatching;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using System;
using System.Threading;
using VoiceStudio.App.Services;
using VoiceStudio.App.Tests.Fixtures;
using VoiceStudio.App.ViewModels;
using VoiceStudio.Core.Panels;
using VoiceStudio.Core.Services;

namespace VoiceStudio.App.Tests.ViewModels
{
  /// <summary>
  /// Seam-aware tests for SSMLControlViewModel.
  /// Instantiates ViewModel with mocked ISSMLClient, IAudioPlayerService.
  /// Supports "SSMLControlViewModel migrated to ISSMLClient" claims.
  /// See docs/governance/TEST_CLASSIFICATION.md.
  /// </summary>
  [TestClass]
  [TestCategory("SeamAware")]
  public class SSMLControlViewModelSeamTests
  {
    private Mock<ISSMLClient> _mockSSMLClient = null!;
    private Mock<IAudioPlayerService> _mockAudioPlayer = null!;
    private IViewModelContext _context = null!;
    private DispatcherQueueController? _dispatcherController;

    [TestInitialize]
    public void Setup()
    {
      TestAppServicesHelper.EnsureInitialized();
      _mockSSMLClient = new Mock<ISSMLClient>();
      _mockAudioPlayer = new Mock<IAudioPlayerService>();
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
      _ = new SSMLControlViewModel(_context, _mockSSMLClient.Object, _mockAudioPlayer.Object, dialogService: null);
      _mockSSMLClient.Verify(x => x.GetDocumentsAsync(It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never);
      _mockSSMLClient.Verify(x => x.ValidateAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [TestMethod]
    public void Constructor_WithClients_CreatesInstance()
    {
      var vm = new SSMLControlViewModel(_context, _mockSSMLClient.Object, _mockAudioPlayer.Object, dialogService: null);
      Assert.IsNotNull(vm);
      Assert.AreEqual(PanelIds.SSMLControl, vm.PanelId);
    }

    [TestMethod]
    [ExpectedException(typeof(ArgumentNullException))]
    public void Constructor_WithNullSSMLClient_Throws()
    {
      _ = new SSMLControlViewModel(_context, null!, _mockAudioPlayer.Object, dialogService: null);
    }

    [TestMethod]
    [ExpectedException(typeof(ArgumentNullException))]
    public void Constructor_WithNullAudioPlayer_Throws()
    {
      _ = new SSMLControlViewModel(_context, _mockSSMLClient.Object, null!, dialogService: null);
    }
  }
}
