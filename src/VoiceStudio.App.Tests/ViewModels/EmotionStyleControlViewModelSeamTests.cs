using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.UI.Dispatching;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using System;
using System.Threading;
using VoiceStudio.App.Tests.Fixtures;
using VoiceStudio.App.ViewModels;
using VoiceStudio.Core.Panels;
using VoiceStudio.Core.Services;

namespace VoiceStudio.App.Tests.ViewModels
{
  /// <summary>
  /// Seam-aware tests for EmotionStyleControlViewModel.
  /// Instantiates ViewModel with mocked IEmotionStyleClient.
  /// Supports "EmotionStyleControlViewModel migrated to IEmotionStyleClient" claims.
  /// See docs/governance/TEST_CLASSIFICATION.md.
  /// </summary>
  [TestClass]
  [TestCategory("SeamAware")]
  public class EmotionStyleControlViewModelSeamTests
  {
    private Mock<IEmotionStyleClient> _mockEmotionStyleClient = null!;
    private IViewModelContext _context = null!;
    private DispatcherQueueController? _dispatcherController;

    [TestInitialize]
    public void Setup()
    {
      TestAppServicesHelper.EnsureInitialized();
      _mockEmotionStyleClient = new Mock<IEmotionStyleClient>();
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
      _ = new EmotionStyleControlViewModel(_context, _mockEmotionStyleClient.Object);
      _mockEmotionStyleClient.Verify(x => x.GetEmotionPresetsAsync(It.IsAny<CancellationToken>()), Times.Never);
      _mockEmotionStyleClient.Verify(x => x.GetStylePresetsAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [TestMethod]
    public void Constructor_WithClients_CreatesInstance()
    {
      var vm = new EmotionStyleControlViewModel(_context, _mockEmotionStyleClient.Object);
      Assert.IsNotNull(vm);
      Assert.AreEqual(PanelIds.EmotionStyle, vm.PanelId);
    }

    [TestMethod]
    [ExpectedException(typeof(ArgumentNullException))]
    public void Constructor_WithNullEmotionStyleClient_Throws()
    {
      _ = new EmotionStyleControlViewModel(_context, null!);
    }
  }
}
