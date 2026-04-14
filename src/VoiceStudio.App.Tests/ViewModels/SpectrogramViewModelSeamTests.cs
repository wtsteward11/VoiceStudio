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
  /// Seam-aware tests for SpectrogramViewModel.
  /// Instantiates ViewModel with mocked ISpectrogramClient.
  /// Supports "SpectrogramViewModel migrated to ISpectrogramClient" claims.
  /// See docs/governance/TEST_CLASSIFICATION.md.
  /// </summary>
  [TestClass]
  [TestCategory("SeamAware")]
  public class SpectrogramViewModelSeamTests
  {
    private Mock<ISpectrogramClient> _mockClient = null!;
    private IViewModelContext _context = null!;
    private DispatcherQueueController? _dispatcherController;

    [TestInitialize]
    public void Setup()
    {
      TestAppServicesHelper.EnsureInitialized();
      _mockClient = new Mock<ISpectrogramClient>();
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
      _ = new SpectrogramViewModel(_context, _mockClient.Object);
      _mockClient.Verify(
        x => x.GetSpectrogramDataAsync(It.IsAny<string>(), It.IsAny<SpectrogramDataRequest>(), It.IsAny<CancellationToken>()),
        Times.Never);
      _mockClient.Verify(
        x => x.GetColorSchemesAsync(It.IsAny<CancellationToken>()),
        Times.Never);
    }

    [TestMethod]
    public void Constructor_WithClient_CreatesInstance()
    {
      var vm = new SpectrogramViewModel(_context, _mockClient.Object);
      Assert.IsNotNull(vm);
      Assert.AreEqual(PanelIds.Spectrogram, vm.PanelId);
      Assert.IsNotNull(vm.LoadSpectrogramCommand);
      Assert.IsNotNull(vm.LoadColorSchemesCommand);
    }

    [TestMethod]
    [ExpectedException(typeof(ArgumentNullException))]
    public void Constructor_WithNullClient_Throws()
    {
      _ = new SpectrogramViewModel(_context, null!);
    }
  }
}
