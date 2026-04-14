using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.UI.Dispatching;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using System;
using System.Threading;
using VoiceStudio.App.Services;
using VoiceStudio.App.Tests.Fixtures;
using VoiceStudio.App.ViewModels;
using VoiceStudio.App.Views.Panels;
using VoiceStudio.Core.Models;
using VoiceStudio.Core.Panels;
using VoiceStudio.Core.Services;

namespace VoiceStudio.App.Tests.ViewModels
{
  /// <summary>
  /// Seam-aware tests for ImageGenViewModel.
  /// Instantiates ViewModel with mocked IImageGenClient.
  /// Supports "ImageGenViewModel migrated to IImageGenClient" claims.
  /// See docs/governance/TEST_CLASSIFICATION.md.
  /// </summary>
  [TestClass]
  [TestCategory("SeamAware")]
  public class ImageGenViewModelSeamTests
  {
    private Mock<IImageGenClient> _mockClient = null!;
    private IViewModelContext _context = null!;
    private DispatcherQueueController? _dispatcherController;

    [TestInitialize]
    public void Setup()
    {
      TestAppServicesHelper.EnsureInitialized();
      _mockClient = new Mock<IImageGenClient>();
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
      _ = new ImageGenViewModel(_context, _mockClient.Object);
      _mockClient.Verify(
        x => x.GenerateAsync(It.IsAny<ImageGenerateRequest>(), It.IsAny<CancellationToken>()),
        Times.Never);
      _mockClient.Verify(
        x => x.UpscaleAsync(It.IsAny<ImageUpscaleRequest>(), It.IsAny<CancellationToken>()),
        Times.Never);
    }

    [TestMethod]
    public void Constructor_WithClient_CreatesInstance()
    {
      var vm = new ImageGenViewModel(_context, _mockClient.Object);
      Assert.IsNotNull(vm);
      Assert.AreEqual(PanelIds.ImageGen, vm.PanelId);
      Assert.IsNotNull(vm.GenerateCommand);
      Assert.IsNotNull(vm.UpscaleCommand);
    }

    [TestMethod]
    [ExpectedException(typeof(ArgumentNullException))]
    public void Constructor_WithNullClient_Throws()
    {
      _ = new ImageGenViewModel(_context, null!);
    }
  }
}
