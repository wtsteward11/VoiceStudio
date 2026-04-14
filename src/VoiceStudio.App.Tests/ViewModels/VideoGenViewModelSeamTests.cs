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
using VoiceStudio.Core.Models;
using VoiceStudio.Core.Panels;
using VoiceStudio.Core.Services;

namespace VoiceStudio.App.Tests.ViewModels
{
  /// <summary>
  /// Seam-aware tests for VideoGenViewModel.
  /// Instantiates ViewModel with mocked IVideoGenClient.
  /// Supports "VideoGenViewModel migrated to IVideoGenClient" claims.
  /// See docs/governance/TEST_CLASSIFICATION.md.
  /// </summary>
  [TestClass]
  [TestCategory("SeamAware")]
  public class VideoGenViewModelSeamTests
  {
    private Mock<IVideoGenClient> _mockVideoGenClient = null!;
    private IViewModelContext _context = null!;
    private DispatcherQueueController? _dispatcherController;

    [TestInitialize]
    public void Setup()
    {
      TestAppServicesHelper.EnsureInitialized();
      _mockVideoGenClient = new Mock<IVideoGenClient>();
      _dispatcherController = DispatcherQueueController.CreateOnDedicatedThread();
      var dispatcher = _dispatcherController.DispatcherQueue;
      _context = new ViewModelContext(NullLogger.Instance, dispatcher);

      _mockVideoGenClient
          .Setup(x => x.ListVideoEnginesAsync(It.IsAny<CancellationToken>()))
          .ReturnsAsync(new List<string> { "svd", "deforum" });
      _mockVideoGenClient
          .Setup(x => x.GenerateVideoAsync(It.IsAny<VideoGenerateRequest>(), It.IsAny<CancellationToken>()))
          .ReturnsAsync(new VideoGenerateResponse { VideoId = "v1", VideoUrl = "https://example.com/v.mp4", Width = 512, Height = 512, Fps = 24, Duration = 5 });
      _mockVideoGenClient
          .Setup(x => x.UpscaleVideoAsync(It.IsAny<VideoUpscaleRequest>(), It.IsAny<CancellationToken>()))
          .ReturnsAsync(new VideoUpscaleResponse { VideoId = "v2", VideoUrl = "https://example.com/v2.mp4", Width = 1024, Height = 1024 });
      _mockVideoGenClient
          .Setup(x => x.GetVideoQualityMetricsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
          .ReturnsAsync(new VideoQualityMetricsResponse { Clarity = 85, Compression = 70 });
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
      _ = new VideoGenViewModel(_context, _mockVideoGenClient.Object);

      _mockVideoGenClient.Verify(
          x => x.ListVideoEnginesAsync(It.IsAny<CancellationToken>()),
          Times.Never);
      _mockVideoGenClient.Verify(
          x => x.GenerateVideoAsync(It.IsAny<VideoGenerateRequest>(), It.IsAny<CancellationToken>()),
          Times.Never);
      _mockVideoGenClient.Verify(
          x => x.GetVideoQualityMetricsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()),
          Times.Never);
    }

    [TestMethod]
    public void Constructor_WithClient_CreatesInstance()
    {
      var vm = new VideoGenViewModel(_context, _mockVideoGenClient.Object);

      Assert.IsNotNull(vm);
      Assert.AreEqual(PanelIds.VideoGen, vm.PanelId);
      Assert.IsNotNull(vm.Engines);
      Assert.IsNotNull(vm.GeneratedVideos);
      Assert.IsNotNull(vm.QualityPresets);
      Assert.IsNotNull(vm.GenerateCommand);
      Assert.IsNotNull(vm.UpscaleCommand);
    }

    [TestMethod]
    [ExpectedException(typeof(ArgumentNullException))]
    public void Constructor_WithNullVideoGenClient_Throws()
    {
      _ = new VideoGenViewModel(_context, null!);
    }

    [TestMethod]
    public void ViewModel_ImplementsIPanelLifecycle()
    {
      var vm = new VideoGenViewModel(_context, _mockVideoGenClient.Object);
      Assert.IsTrue(vm is IPanelLifecycle);
    }

    [TestMethod]
    public async Task OnActivatedAsync_CallsIVideoGenClient_ListVideoEnginesAsync()
    {
      var vm = new VideoGenViewModel(_context, _mockVideoGenClient.Object);

      await vm.OnActivatedAsync(CancellationToken.None);

      _mockVideoGenClient.Verify(
          x => x.ListVideoEnginesAsync(It.IsAny<CancellationToken>()),
          Times.Once);
    }
  }
}
