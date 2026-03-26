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
  /// Seam-aware tests for SceneBuilderViewModel.
  /// Instantiates ViewModel with mocked ISceneBuilderClient.
  /// Supports "SceneBuilderViewModel migrated to ISceneBuilderClient" claims.
  /// </summary>
  [TestClass]
  [TestCategory("SeamAware")]
  public class SceneBuilderViewModelSeamTests
  {
    private Mock<ISceneBuilderClient> _mockClient = null!;
    private IViewModelContext _context = null!;
    private DispatcherQueueController? _dispatcherController;

    [TestInitialize]
    public void Setup()
    {
      TestAppServicesHelper.EnsureInitialized();
      _mockClient = new Mock<ISceneBuilderClient>();
      _dispatcherController = DispatcherQueueController.CreateOnDedicatedThread();
      var dispatcher = _dispatcherController.DispatcherQueue;
      _context = new ViewModelContext(NullLogger.Instance, dispatcher);

      _mockClient
          .Setup(x => x.GetScenesAsync(It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
          .ReturnsAsync(Array.Empty<Scene>());
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
      _ = new SceneBuilderViewModel(_context, _mockClient.Object);
      _mockClient.Verify(x => x.GetScenesAsync(It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [TestMethod]
    public void Constructor_WithISceneBuilderClient_CreatesInstance()
    {
      var vm = new SceneBuilderViewModel(_context, _mockClient.Object);
      Assert.IsNotNull(vm);
      Assert.AreEqual(PanelIds.SceneBuilder, vm.PanelId);
      Assert.IsNotNull(vm.LoadScenesCommand);
      Assert.IsNotNull(vm.CreateSceneCommand);
      Assert.IsNotNull(vm.UpdateSceneCommand);
      Assert.IsNotNull(vm.DeleteSceneCommand);
      Assert.IsNotNull(vm.ApplySceneCommand);
      Assert.IsNotNull(vm.RefreshCommand);
    }

    [TestMethod]
    [ExpectedException(typeof(ArgumentNullException))]
    public void Constructor_WithNullClient_Throws()
    {
      _ = new SceneBuilderViewModel(_context, null!);
    }

    [TestMethod]
    public async Task LoadScenesCommand_CallsISceneBuilderClient_GetScenesAsync()
    {
      var vm = new SceneBuilderViewModel(_context, _mockClient.Object);
      await vm.LoadScenesCommand.ExecuteAsync(null);
      _mockClient.Verify(
          x => x.GetScenesAsync(It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()),
          Times.AtLeastOnce);
    }

    /// <summary>
    /// Lifecycle: OnActivatedAsync owns the load (awaits LoadScenesAsync); not fire-and-forget.
    /// </summary>
    [TestMethod]
    public async Task OnActivatedAsync_OwnsLoad_AwaitsCompletion()
    {
      var tcs = new TaskCompletionSource<Scene[]>();
      _mockClient
          .Setup(x => x.GetScenesAsync(It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
          .Returns(() => tcs.Task);
      var vm = new SceneBuilderViewModel(_context, _mockClient.Object);
      var activated = vm.OnActivatedAsync(CancellationToken.None);
      await Task.Delay(50);
      Assert.IsFalse(activated.IsCompleted);
      tcs.SetResult(Array.Empty<Scene>());
      await activated;
      _mockClient.Verify(
          x => x.GetScenesAsync(It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()),
          Times.Once);
    }

    /// <summary>
    /// Lifecycle: OnSelectedProjectIdChanged triggers LoadScenesAsync with cancellation of prior load.
    /// </summary>
    [TestMethod]
    public async Task OnSelectedProjectIdChanged_TriggersLoadScenesAsync()
    {
      var vm = new SceneBuilderViewModel(_context, _mockClient.Object);
      vm.SelectedProjectId = "proj1";
      await Task.Delay(100);
      vm.SelectedProjectId = "proj2";
      await Task.Delay(100);
      _mockClient.Verify(
          x => x.GetScenesAsync(It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()),
          Times.AtLeast(2));
    }

    /// <summary>
    /// Lifecycle: OnSearchQueryChanged debounces and eventually calls GetScenesAsync.
    /// </summary>
    [TestMethod]
    public async Task OnSearchQueryChanged_DebouncesAndCallsGetScenesAsync()
    {
      var vm = new SceneBuilderViewModel(_context, _mockClient.Object);
      vm.SearchQuery = "test";
      await Task.Delay(400);
      _mockClient.Verify(
          x => x.GetScenesAsync(It.IsAny<string?>(), It.Is<string?>(s => s == "test"), It.IsAny<CancellationToken>()),
          Times.AtLeastOnce);
    }

    /// <summary>
    /// Lifecycle: Dispose cleans up without throwing.
    /// </summary>
    [TestMethod]
    public void Dispose_DisposesResources_NoThrow()
    {
      var vm = new SceneBuilderViewModel(_context, _mockClient.Object);
      vm.Dispose();
    }

    /// <summary>
    /// Lifecycle: When cancelled, LoadScenesAsync does not overwrite Scenes with stale data.
    /// Uses mock that throws OperationCanceledException to simulate cancellation.
    /// </summary>
    [TestMethod]
    public async Task LoadScenesAsync_WhenClientThrowsCancelled_DoesNotOverwriteScenes()
    {
      var mockClient = new Mock<ISceneBuilderClient>();
      mockClient
          .Setup(x => x.GetScenesAsync(It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
          .ThrowsAsync(new OperationCanceledException());
      var vm = new SceneBuilderViewModel(_context, mockClient.Object);
      var initialScene = new Scene { Id = "s1", Name = "Initial", ProjectId = "p1" };
      vm.Scenes.Add(new SceneItem(initialScene));
      await vm.LoadScenesCommand.ExecuteAsync(null);
      Assert.AreEqual(1, vm.Scenes.Count);
      Assert.AreEqual("s1", vm.Scenes[0].Id);
    }

    /// <summary>
    /// Lifecycle: Rapid project change does not apply stale scene results.
    /// Staleness guard discards result when project changed after request started.
    /// </summary>
    [TestMethod]
    public async Task RapidProjectChange_DoesNotApplyStaleResults()
    {
      var p1Scenes = new[] { new Scene { Id = "s1", Name = "FromP1", ProjectId = "p1" } };
      var p2Scenes = new[] { new Scene { Id = "s2", Name = "FromP2", ProjectId = "p2" } };
      var mockClient = new Mock<ISceneBuilderClient>();
      mockClient
          .Setup(x => x.GetScenesAsync("p1", It.IsAny<string?>(), It.IsAny<CancellationToken>()))
          .Returns(async () =>
          {
            await Task.Delay(150);
            return p1Scenes;
          });
      mockClient
          .Setup(x => x.GetScenesAsync("p2", It.IsAny<string?>(), It.IsAny<CancellationToken>()))
          .ReturnsAsync(p2Scenes);
      var vm = new SceneBuilderViewModel(_context, mockClient.Object);
      vm.SelectedProjectId = "p1";
      await Task.Delay(20);
      vm.SelectedProjectId = "p2";
      await Task.Delay(200);
      Assert.AreEqual(1, vm.Scenes.Count);
      Assert.AreEqual("s2", vm.Scenes[0].Id);
    }

    /// <summary>
    /// Lifecycle: Dispose prevents delayed debounce from mutating state.
    /// After dispose, timer tick should not run LoadScenesAsync or should not mutate.
    /// </summary>
    [TestMethod]
    public async Task Dispose_PreventsDebounceFromMutatingState()
    {
      var mockClient = new Mock<ISceneBuilderClient>();
      mockClient
          .Setup(x => x.GetScenesAsync(It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
          .ReturnsAsync(new[] { new Scene { Id = "s1", Name = "FromDebounce", ProjectId = "p1" } });
      var vm = new SceneBuilderViewModel(_context, mockClient.Object);
      vm.SelectedProjectId = "p1";
      vm.SearchQuery = "x";
      vm.Dispose();
      await Task.Delay(400);
      mockClient.Verify(
          x => x.GetScenesAsync(It.IsAny<string?>(), It.Is<string?>(s => s == "x"), It.IsAny<CancellationToken>()),
          Times.Never);
    }
  }
}
