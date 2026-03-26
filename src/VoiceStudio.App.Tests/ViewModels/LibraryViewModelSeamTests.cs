using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.UI.Dispatching;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using VoiceStudio.App.Services;
using VoiceStudio.App.Tests.Fixtures;
using VoiceStudio.App.ViewModels;
using VoiceStudio.Core.Panels;
using VoiceStudio.Core.Services;

namespace VoiceStudio.App.Tests.ViewModels
{
  /// <summary>
  /// Seam-aware tests for LibraryViewModel.
  /// Instantiates ViewModel with mocked ILibraryClient.
  /// Supports "LibraryViewModel migrated to ILibraryClient" claims.
  /// See docs/governance/TEST_CLASSIFICATION.md.
  /// </summary>
  [TestClass]
  [TestCategory("SeamAware")]
  public class LibraryViewModelSeamTests
  {
    private Mock<ILibraryClient> _mockLibraryClient = null!;
    private Mock<IDialogService> _mockDialogService = null!;
    private IViewModelContext _context = null!;
    private DispatcherQueueController? _dispatcherController;

    [TestInitialize]
    public void Setup()
    {
      TestAppServicesHelper.EnsureInitialized();
      _mockLibraryClient = new Mock<ILibraryClient>();
      _mockDialogService = new Mock<IDialogService>();
      _dispatcherController = DispatcherQueueController.CreateOnDedicatedThread();
      var dispatcher = _dispatcherController.DispatcherQueue;
      _context = new ViewModelContext(NullLogger.Instance, dispatcher);

      _mockLibraryClient
          .Setup(x => x.GetLibraryFoldersAsync(It.IsAny<string?>(), It.IsAny<System.Threading.CancellationToken>()))
          .ReturnsAsync(new LibraryFoldersResponse { Folders = Array.Empty<LibraryFolder>() });
      _mockLibraryClient
          .Setup(x => x.SearchAssetsAsync(It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<System.Threading.CancellationToken>()))
          .ReturnsAsync(new AssetSearchResponse { Assets = Array.Empty<LibraryAsset>(), Total = 0 });
      _mockLibraryClient
          .Setup(x => x.GetAssetTypesAsync(It.IsAny<System.Threading.CancellationToken>()))
          .ReturnsAsync(new AssetTypesResponse { Types = Array.Empty<AssetTypeInfo>() });
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
      _ = new LibraryViewModel(_context, _mockLibraryClient.Object, _mockDialogService.Object);
      _mockLibraryClient.Verify(x => x.GetLibraryFoldersAsync(It.IsAny<string?>(), It.IsAny<System.Threading.CancellationToken>()), Times.Never);
      _mockLibraryClient.Verify(x => x.SearchAssetsAsync(It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<System.Threading.CancellationToken>()), Times.Never);
      _mockLibraryClient.Verify(x => x.GetAssetTypesAsync(It.IsAny<System.Threading.CancellationToken>()), Times.Never);
    }

    [TestMethod]
    public void Constructor_WithILibraryClient_CreatesInstance()
    {
      var vm = new LibraryViewModel(_context, _mockLibraryClient.Object, _mockDialogService.Object);
      Assert.IsNotNull(vm);
      Assert.AreEqual(PanelIds.Library, vm.PanelId);
      Assert.IsNotNull(vm.LoadFoldersCommand);
      Assert.IsNotNull(vm.LoadAssetsCommand);
      Assert.IsNotNull(vm.SearchAssetsCommand);
      Assert.IsNotNull(vm.CreateFolderCommand);
    }

    [TestMethod]
    [ExpectedException(typeof(ArgumentNullException))]
    public void Constructor_WithNullLibraryClient_Throws()
    {
      _ = new LibraryViewModel(_context, null!, _mockDialogService.Object);
    }

    [TestMethod]
    public async Task LoadFoldersCommand_CallsILibraryClient_GetLibraryFoldersAsync()
    {
      var vm = new LibraryViewModel(_context, _mockLibraryClient.Object, _mockDialogService.Object);
      await vm.LoadFoldersCommand.ExecuteAsync(null);
      _mockLibraryClient.Verify(
          x => x.GetLibraryFoldersAsync(It.IsAny<string?>(), It.IsAny<System.Threading.CancellationToken>()),
          Times.AtLeastOnce);
    }

    /// <summary>Product trust Pass 01 slice 2: Library panel discloses import vs drag-drop→project until A4 §12.</summary>
    [TestMethod]
    public void ImportDragDropScopeFootnote_DisclosesImportVsDragDropAndDeferredParity()
    {
      var vm = new LibraryViewModel(_context, _mockLibraryClient.Object, _mockDialogService.Object);
      var text = vm.ImportDragDropScopeFootnote;
      StringAssert.Contains(text, "import", StringComparison.OrdinalIgnoreCase);
      StringAssert.Contains(text, "drag", StringComparison.OrdinalIgnoreCase);
      StringAssert.Contains(text, "project", StringComparison.OrdinalIgnoreCase);
    }
  }
}
