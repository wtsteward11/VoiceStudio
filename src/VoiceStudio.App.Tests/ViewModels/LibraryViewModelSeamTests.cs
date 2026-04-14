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
using VoiceStudio.Core.Events;
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
      DispatcherQueueTestHelpers.ShutdownSyncBounded(_dispatcherController);
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

    /// <summary>GAP-027: recording-origin <see cref="AssetAddedEvent"/> selects asset after reload.</summary>
    [TestMethod]
    public async Task AssetAdded_FromRecordingPanel_SelectsAssetAfterReload()
    {
      var assetId = "rec-lib-1";
      _mockLibraryClient
          .SetupSequence(x => x.SearchAssetsAsync(It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
          .ReturnsAsync(new AssetSearchResponse { Assets = Array.Empty<LibraryAsset>(), Total = 0 })
          .ReturnsAsync(new AssetSearchResponse
          {
            Assets = new[]
            {
              new LibraryAsset
              {
                Id = assetId,
                Name = "Take",
                Type = "audio",
                Path = "http://localhost:8000/api/audio/file/x",
                AudioId = assetId,
                Duration = 2.5
              }
            },
            Total = 1
          });

      var vm = new LibraryViewModel(_context, _mockLibraryClient.Object, _mockDialogService.Object);
      await vm.OnActivatedAsync(CancellationToken.None);
      var agg = AppServices.TryGetEventAggregator();
      Assert.IsNotNull(agg);
      agg.Publish(new AssetAddedEvent(PanelIds.Recording, assetId, "audio", @"C:\take.wav"));
      await Task.Delay(500);
      Assert.AreEqual(assetId, vm.SelectedAsset?.Id);
    }

    /// <summary>GAP-027: non-recording <see cref="AssetAddedEvent"/> does not force selection.</summary>
    [TestMethod]
    public async Task AssetAdded_FromImportWorkflow_DoesNotForceSelection()
    {
      var uploadedId = "imp-only-1";
      _mockLibraryClient
          .Setup(x => x.SearchAssetsAsync(It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
          .ReturnsAsync(new AssetSearchResponse
          {
            Assets = new[] { new LibraryAsset { Id = uploadedId, Name = "I", Type = "audio", Path = "p.wav" } },
            Total = 1
          });

      var vm = new LibraryViewModel(_context, _mockLibraryClient.Object, _mockDialogService.Object);
      await vm.OnActivatedAsync(CancellationToken.None);
      var agg = AppServices.TryGetEventAggregator();
      agg!.Publish(new AssetAddedEvent("import-workflow", uploadedId, "audio", "x.wav"));
      await Task.Delay(400);
      Assert.IsNull(vm.SelectedAsset);
    }

    /// <summary>GAP-027: explicit operator command publishes <see cref="AddToTimelineEvent"/>.</summary>
    [TestMethod]
    public void AddAssetToTimelineCommand_PublishesHandoffEvent()
    {
      TestAppServicesHelper.EnsureInitialized();
      AddToTimelineEvent? cap = null;
      var agg = AppServices.GetService<IEventAggregator>();
      Assert.IsNotNull(agg);
      using var _ = agg.Subscribe<AddToTimelineEvent>(e => cap = e);
      var vm = new LibraryViewModel(_context, _mockLibraryClient.Object, _mockDialogService.Object);
      var asset = new LibraryAsset
      {
        Id = "la-1",
        Name = "Clip",
        Type = "audio",
        Path = "http://localhost:8000/api/audio/file/aid1",
        AudioId = "aid1",
        Duration = 3.0
      };
      vm.AddAssetToTimelineCommand.Execute(asset);
      Assert.IsNotNull(cap);
      Assert.AreEqual("aid1", cap.AudioId);
      Assert.AreEqual(PanelIds.Library, cap.SourcePanelId);
    }

    /// <summary>GAP-032: Library builds cross-panel drag payload with playback id + core3 metadata.</summary>
    [TestMethod]
    public void BuildCrossPanelDragPayload_PrefersAudioId_AndEmbedsMetadata()
    {
      var vm = new LibraryViewModel(_context, _mockLibraryClient.Object, _mockDialogService.Object);
      var asset = new LibraryAsset
      {
        Id = "lib-row",
        AudioId = "play-1",
        Name = "Clip.wav",
        Type = "audio",
        Path = @"C:\media\Clip.wav",
        Duration = 4.2
      };
      var p = vm.BuildCrossPanelDragPayload(asset);
      Assert.AreEqual(DragPayloadType.Asset, p.PayloadType);
      Assert.AreEqual(PanelIds.Library, p.SourcePanelId);
      Assert.AreEqual("play-1", p.Items[0].Id);
      Assert.IsNotNull(p.Items[0].Metadata);
      Assert.AreEqual("audio", p.Items[0].Metadata!["AssetType"].ToString());
      Assert.AreEqual(@"C:\media\Clip.wav", p.Items[0].Metadata["FilePath"].ToString());
      Assert.AreEqual("lib-row", p.Items[0].Metadata["LibraryAssetId"].ToString());
      Assert.AreEqual(4.2, (double)p.Items[0].Metadata["DurationSeconds"]);
    }
  }
}
