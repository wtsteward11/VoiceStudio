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
  /// Seam-aware tests for ImageSearchViewModel.
  /// Instantiates ViewModel with mocked IImageSearchClient.
  /// Supports "ImageSearchViewModel migrated to IImageSearchClient" claims.
  /// </summary>
  [TestClass]
  [TestCategory("SeamAware")]
  public class ImageSearchViewModelSeamTests
  {
    private Mock<IImageSearchClient> _mockClient = null!;
    private IViewModelContext _context = null!;
    private DispatcherQueueController? _dispatcherController;

    [TestInitialize]
    public void Setup()
    {
      TestAppServicesHelper.EnsureInitialized();
      _mockClient = new Mock<IImageSearchClient>();
      _dispatcherController = DispatcherQueueController.CreateOnDedicatedThread();
      var dispatcher = _dispatcherController.DispatcherQueue;
      _context = new ViewModelContext(NullLogger.Instance, dispatcher);

      _mockClient
        .Setup(x => x.GetSourcesAsync(It.IsAny<CancellationToken>()))
        .ReturnsAsync((ImageSourceInfo[]?)null);
      _mockClient
        .Setup(x => x.GetCategoriesAsync(It.IsAny<CancellationToken>()))
        .ReturnsAsync((string[]?)null);
      _mockClient
        .Setup(x => x.GetColorsAsync(It.IsAny<CancellationToken>()))
        .ReturnsAsync((string[]?)null);
    }

    [TestCleanup]
    public void Cleanup()
    {
      _dispatcherController?.ShutdownQueueAsync().AsTask().GetAwaiter().GetResult();
    }

    [TestMethod]
    public void Constructor_WithIImageSearchClient_CreatesInstance()
    {
      var vm = new ImageSearchViewModel(_context, _mockClient.Object);
      Assert.IsNotNull(vm);
      Assert.AreEqual("image-search", vm.PanelId);
      Assert.IsNotNull(vm.SearchCommand);
      Assert.IsNotNull(vm.LoadSourcesCommand);
      Assert.IsNotNull(vm.LoadCategoriesCommand);
      Assert.IsNotNull(vm.LoadColorsCommand);
      Assert.IsNotNull(vm.RefreshCommand);
      Assert.IsNotNull(vm.ClearHistoryCommand);
    }

    [TestMethod]
    [ExpectedException(typeof(ArgumentNullException))]
    public void Constructor_WithNullClient_Throws()
    {
      _ = new ImageSearchViewModel(_context, null!);
    }

    [TestMethod]
    public void ViewModel_ImplementsIPanelLifecycle()
    {
      var vm = new ImageSearchViewModel(_context, _mockClient.Object);
      Assert.IsTrue(vm is IPanelLifecycle);
    }

    [TestMethod]
    public async Task OnActivatedAsync_LoadsSourcesCategoriesColors()
    {
      var vm = new ImageSearchViewModel(_context, _mockClient.Object);
      await vm.OnActivatedAsync(CancellationToken.None);
      _mockClient.Verify(x => x.GetSourcesAsync(It.IsAny<CancellationToken>()), Times.Once);
      _mockClient.Verify(x => x.GetCategoriesAsync(It.IsAny<CancellationToken>()), Times.Once);
      _mockClient.Verify(x => x.GetColorsAsync(It.IsAny<CancellationToken>()), Times.Once);
    }
  }
}
