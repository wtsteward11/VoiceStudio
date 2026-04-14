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
using VoiceStudio.Core.Services;

namespace VoiceStudio.App.Tests.ViewModels
{
  /// <summary>
  /// Lifecycle tests for LibraryViewModel: event subscription, activation/deactivation, disposal.
  /// Excluded from fast seam shards; run in dedicated Lifecycle shard.
  /// See docs/governance/TEST_CLASSIFICATION.md.
  /// </summary>
  [TestClass]
  [TestCategory("Lifecycle")]
  public class LibraryViewModelLifecycleTests
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
          .Setup(x => x.GetLibraryFoldersAsync(It.IsAny<string?>(), It.IsAny<CancellationToken>()))
          .ReturnsAsync(new LibraryFoldersResponse { Folders = Array.Empty<LibraryFolder>() });
      _mockLibraryClient
          .Setup(x => x.SearchAssetsAsync(It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
          .ReturnsAsync(new AssetSearchResponse { Assets = Array.Empty<LibraryAsset>(), Total = 0 });
      _mockLibraryClient
          .Setup(x => x.GetAssetTypesAsync(It.IsAny<CancellationToken>()))
          .ReturnsAsync(new AssetTypesResponse { Types = Array.Empty<AssetTypeInfo>() });
    }

    [TestCleanup]
    public void Cleanup()
    {
      DispatcherQueueTestHelpers.ShutdownSyncBounded(_dispatcherController);
    }

    /// <summary>
    /// Proves that after deactivate/reactivate, Library still receives AssetAddedEvent and refreshes.
    /// Prevents re-subscription bug from quietly returning.
    /// </summary>
    [TestMethod]
    public async Task OnActivatedAsync_AfterDeactivate_ResubscribesToAssetAddedEvent()
    {
      var searchCalled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
      _mockLibraryClient
          .Setup(x => x.SearchAssetsAsync(It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
          .Returns(async () =>
          {
            searchCalled.TrySetResult();
            return new AssetSearchResponse { Assets = Array.Empty<LibraryAsset>(), Total = 0 };
          });

      var vm = new LibraryViewModel(_context, _mockLibraryClient.Object, _mockDialogService.Object);
      var eventAggregator = AppServices.TryGetEventAggregator();
      Assert.IsNotNull(eventAggregator, "EventAggregator required for this test");

      await vm.OnActivatedAsync(CancellationToken.None);
      await vm.OnDeactivatedAsync(CancellationToken.None);

      _mockLibraryClient.Invocations.Clear();
      searchCalled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
      _mockLibraryClient
          .Setup(x => x.SearchAssetsAsync(It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
          .Returns(async () =>
          {
            searchCalled.TrySetResult();
            return new AssetSearchResponse { Assets = Array.Empty<LibraryAsset>(), Total = 0 };
          });

      await vm.OnActivatedAsync(CancellationToken.None);

      eventAggregator.Publish(new AssetAddedEvent("test-panel", "asset-123", "audio", "test.wav"));

      await searchCalled.Task;

      _mockLibraryClient.Verify(
          x => x.SearchAssetsAsync(It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()),
          Times.AtLeastOnce,
          "AssetAddedEvent after reactivate should trigger asset refresh");
    }

    /// <summary>
    /// Proves that EnsureEventSubscriptions is called before loads, so events during load are not missed.
    /// Uses deterministic SearchAssetsAsync signal instead of Task.Delay.
    /// </summary>
    [TestMethod]
    public async Task OnActivatedAsync_SubscribesBeforeLoads_ReceivesEventsDuringLoad()
    {
      var searchCalled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
      _mockLibraryClient
          .Setup(x => x.GetAssetTypesAsync(It.IsAny<CancellationToken>()))
          .Returns(async () =>
          {
            await Task.Delay(80);
            return new AssetTypesResponse { Types = Array.Empty<AssetTypeInfo>() };
          });
      _mockLibraryClient
          .Setup(x => x.GetLibraryFoldersAsync(It.IsAny<string?>(), It.IsAny<CancellationToken>()))
          .ReturnsAsync(new LibraryFoldersResponse { Folders = Array.Empty<LibraryFolder>() });
      _mockLibraryClient
          .Setup(x => x.SearchAssetsAsync(It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
          .Returns(async () =>
          {
            searchCalled.TrySetResult();
            return new AssetSearchResponse { Assets = Array.Empty<LibraryAsset>(), Total = 0 };
          });

      var vm = new LibraryViewModel(_context, _mockLibraryClient.Object, _mockDialogService.Object);
      var eventAggregator = AppServices.TryGetEventAggregator();
      Assert.IsNotNull(eventAggregator);

      _mockLibraryClient.Invocations.Clear();

      var activateTask = vm.OnActivatedAsync(CancellationToken.None);
      await Task.Delay(20);
      eventAggregator.Publish(new AssetAddedEvent("test", "asset-1", "audio", "test.wav"));
      await activateTask;

      await searchCalled.Task;

      _mockLibraryClient.Verify(
          x => x.SearchAssetsAsync(It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()),
          Times.AtLeastOnce,
          "Event published during activation load should trigger refresh");
    }

    /// <summary>
    /// Proves that disposed LibraryViewModel does not throw when SelectionChanged is invoked.
    /// Handler must be unsubscribed in Dispose to avoid invoking logic on disposed instance.
    /// </summary>
    [TestMethod]
    public void Disposed_DoesNotThrow_WhenSelectionChangedInvoked()
    {
      var vm = new LibraryViewModel(_context, _mockLibraryClient.Object, _mockDialogService.Object);
      var multiSelect = AppServices.GetService<MultiSelectService>();
      Assert.IsNotNull(multiSelect);

      var state = multiSelect.GetState("library");
      state.Add("asset-1");

      vm.Dispose();
      multiSelect.OnSelectionChanged("library", state);
    }

    /// <summary>
    /// Proves that after deactivation, event handlers no longer fire (no refresh on AssetAddedEvent).
    /// </summary>
    [TestMethod]
    public async Task OnDeactivatedAsync_Unsubscribes_NoRefreshOnEventAfterDeactivate()
    {
      var vm = new LibraryViewModel(_context, _mockLibraryClient.Object, _mockDialogService.Object);
      var eventAggregator = AppServices.TryGetEventAggregator();
      Assert.IsNotNull(eventAggregator);

      await vm.OnActivatedAsync(CancellationToken.None);
      _mockLibraryClient.Invocations.Clear();

      await vm.OnDeactivatedAsync(CancellationToken.None);

      eventAggregator.Publish(new AssetAddedEvent("test", "asset-1", "audio", "test.wav"));
      // Allow async handlers to run; we expect none (SearchAssetsAsync should never be called)
      await Task.Delay(50);

      _mockLibraryClient.Verify(
          x => x.SearchAssetsAsync(It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()),
          Times.Never,
          "AssetAddedEvent after deactivate should NOT trigger refresh");
    }
  }
}
