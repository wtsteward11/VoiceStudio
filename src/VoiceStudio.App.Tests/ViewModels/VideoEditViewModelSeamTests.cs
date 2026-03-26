using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.UI.Dispatching;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using System;
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
  /// Seam-aware tests for VideoEditViewModel.
  /// Instantiates ViewModel with mocked IVideoEditClient.
  /// Supports "VideoEditViewModel migrated to IVideoEditClient" claims.
  /// See docs/governance/TEST_CLASSIFICATION.md.
  /// </summary>
  [TestClass]
  [TestCategory("SeamAware")]
  public class VideoEditViewModelSeamTests
  {
    private Mock<IVideoEditClient> _mockVideoEditClient = null!;
    private IViewModelContext _context = null!;
    private DispatcherQueueController? _dispatcherController;

    [TestInitialize]
    public void Setup()
    {
      TestAppServicesHelper.EnsureInitialized();
      _mockVideoEditClient = new Mock<IVideoEditClient>();
      _dispatcherController = DispatcherQueueController.CreateOnDedicatedThread();
      var dispatcher = _dispatcherController.DispatcherQueue;
      _context = new ViewModelContext(NullLogger.Instance, dispatcher);

      _mockVideoEditClient
          .Setup(x => x.GetVideoInfoAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
          .ReturnsAsync(new VideoInfo { Duration = 60.0 });
      _mockVideoEditClient
          .Setup(x => x.EditVideoAsync(It.IsAny<VideoEditRequest>(), It.IsAny<CancellationToken>()))
          .ReturnsAsync(new VideoEditResponse { Success = true, OutputPath = "out.mp4", Message = "OK" });
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
      _ = new VideoEditViewModel(_context, _mockVideoEditClient.Object);

      _mockVideoEditClient.Verify(
          x => x.GetVideoInfoAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()),
          Times.Never);
      _mockVideoEditClient.Verify(
          x => x.EditVideoAsync(It.IsAny<VideoEditRequest>(), It.IsAny<CancellationToken>()),
          Times.Never);
    }

    [TestMethod]
    public void Constructor_WithClient_CreatesInstance()
    {
      var vm = new VideoEditViewModel(_context, _mockVideoEditClient.Object);

      Assert.IsNotNull(vm);
      Assert.AreEqual(PanelIds.VideoEdit, vm.PanelId);
      Assert.IsNotNull(vm.Effects);
      Assert.IsNotNull(vm.Transitions);
      Assert.IsNotNull(vm.ExportFormats);
      Assert.IsNotNull(vm.SelectVideoCommand);
      Assert.IsNotNull(vm.TrimCommand);
      Assert.IsNotNull(vm.ExportCommand);
    }

    [TestMethod]
    [ExpectedException(typeof(ArgumentNullException))]
    public void Constructor_WithNullVideoEditClient_Throws()
    {
      _ = new VideoEditViewModel(_context, null!);
    }

    /// <summary>
    /// Lifecycle: Rapid video path change does not apply stale video info.
    /// Staleness guard discards result when path changed after request started.
    /// </summary>
    [TestMethod]
    public async Task RapidPathChange_DoesNotApplyStaleResults()
    {
      var pathAMetrics = new VideoInfo { Duration = 999 };
      var pathBMetrics = new VideoInfo { Duration = 60 };
      var mockClient = new Mock<IVideoEditClient>();
      mockClient
          .Setup(x => x.GetVideoInfoAsync("path-a", It.IsAny<CancellationToken>()))
          .Returns(async () =>
          {
            await Task.Delay(150);
            return pathAMetrics;
          });
      mockClient
          .Setup(x => x.GetVideoInfoAsync("path-b", It.IsAny<CancellationToken>()))
          .ReturnsAsync(pathBMetrics);

      var vm = new VideoEditViewModel(_context, mockClient.Object);

      vm.SelectedVideoPath = "path-a";
      await Task.Delay(20);
      vm.SelectedVideoPath = "path-b";
      await Task.Delay(200);

      Assert.AreEqual(60, vm.VideoDuration);
      Assert.AreNotEqual(999, vm.VideoDuration);
    }

    /// <summary>
    /// Lifecycle: Rapid path change; last selection wins.
    /// </summary>
    [TestMethod]
    public async Task OnSelectedVideoPathChanged_RapidChange_LastSelectionWins()
    {
      var vm = new VideoEditViewModel(_context, _mockVideoEditClient.Object);

      vm.SelectedVideoPath = "path-a";
      vm.SelectedVideoPath = "path-b";
      await Task.Delay(400);

      _mockVideoEditClient.Verify(
          x => x.GetVideoInfoAsync("path-b", It.IsAny<CancellationToken>()),
          Times.AtLeastOnce);
    }
  }
}
