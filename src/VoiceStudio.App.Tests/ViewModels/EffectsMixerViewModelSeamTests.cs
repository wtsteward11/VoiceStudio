using Microsoft.UI.Dispatching;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using VoiceStudio.App.Services;
using VoiceStudio.App.Tests.Fixtures;
using VoiceStudio.App.Views.Panels;
using VoiceStudio.Core.Models;
using VoiceStudio.Core.Panels;
using VoiceStudio.Core.Services;

namespace VoiceStudio.App.Tests.ViewModels
{
  /// <summary>
  /// Seam-aware tests for EffectsMixerViewModel.
  /// Instantiates ViewModel with mocked IEffectsMeterClient, IEffectChainClient, IMixerStateClient.
  /// Supports EffectsMixer seam migration (Slice 1–3: meters, effect chains, mixer state).
  /// See docs/governance/TEST_CLASSIFICATION.md.
  /// </summary>
  [TestClass]
  [TestCategory("SeamAware")]
  public class EffectsMixerViewModelSeamTests
  {
    private Mock<IEffectsMeterClient> _mockEffectsMeterClient = null!;
    private Mock<IEffectChainClient> _mockEffectChainClient = null!;
    private Mock<IMixerStateClient> _mockMixerStateClient = null!;
    private DispatcherQueueController? _dispatcherController;

    [TestInitialize]
    public void Setup()
    {
      TestAppServicesHelper.EnsureInitialized();
      _mockEffectsMeterClient = new Mock<IEffectsMeterClient>();
      _mockEffectChainClient = new Mock<IEffectChainClient>();
      _mockMixerStateClient = new Mock<IMixerStateClient>();
      _dispatcherController = DispatcherQueueController.CreateOnDedicatedThread();

      _mockEffectsMeterClient
          .Setup(x => x.GetAudioMetersAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
          .ReturnsAsync(new AudioMeters());
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
      _ = new EffectsMixerViewModel(_mockEffectsMeterClient.Object, _mockEffectChainClient.Object, _mockMixerStateClient.Object);
      _mockEffectsMeterClient.Verify(x => x.GetAudioMetersAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
      _mockEffectChainClient.Verify(x => x.GetEffectChainsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [TestMethod]
    public void Constructor_WithClients_CreatesInstance()
    {
      var vm = new EffectsMixerViewModel(_mockEffectsMeterClient.Object, _mockEffectChainClient.Object, _mockMixerStateClient.Object);
      Assert.IsNotNull(vm);
      Assert.AreEqual(PanelIds.EffectsMixer, vm.PanelId);
      Assert.IsNotNull(vm.LoadMetersCommand);
      Assert.IsNotNull(vm.LoadEffectChainsCommand);
    }

    [TestMethod]
    [ExpectedException(typeof(ArgumentNullException))]
    public void Constructor_WithNullEffectsMeterClient_Throws()
    {
      _ = new EffectsMixerViewModel(null!, _mockEffectChainClient.Object, _mockMixerStateClient.Object);
    }

    [TestMethod]
    [ExpectedException(typeof(ArgumentNullException))]
    public void Constructor_WithNullEffectChainClient_Throws()
    {
      _ = new EffectsMixerViewModel(_mockEffectsMeterClient.Object, null!, _mockMixerStateClient.Object);
    }

    [TestMethod]
    [ExpectedException(typeof(ArgumentNullException))]
    public void Constructor_WithNullMixerStateClient_Throws()
    {
      _ = new EffectsMixerViewModel(_mockEffectsMeterClient.Object, _mockEffectChainClient.Object, null!);
    }

    [TestMethod]
    public void ViewModel_ImplementsIPanelLifecycle()
    {
      var vm = new EffectsMixerViewModel(_mockEffectsMeterClient.Object, _mockEffectChainClient.Object, _mockMixerStateClient.Object);
      Assert.IsTrue(vm is IPanelLifecycle);
    }

    /// <summary>
    /// Verifies Dispose cleans up without throwing. Prevents subscription leak.
    /// </summary>
    [TestMethod]
    public void Dispose_DoesNotThrow()
    {
      var vm = new EffectsMixerViewModel(_mockEffectsMeterClient.Object, _mockEffectChainClient.Object, _mockMixerStateClient.Object);
      vm.Dispose();
      vm.Dispose(); // Idempotent
    }

    /// <summary>
    /// Pass 02 C6: SelectedProjectId set to null clears stale state (effect chains, mixer presets, etc).
    /// Prevents data from previous project leaking into cleared state.
    /// </summary>
    [TestMethod]
    public async Task SelectedProjectId_SetToNull_ClearsStaleState()
    {
      _mockEffectChainClient
          .Setup(x => x.GetEffectChainsAsync("proj-1", It.IsAny<CancellationToken>()))
          .ReturnsAsync(new List<EffectChain> { new EffectChain { Id = "c1", Name = "Chain 1", ProjectId = "proj-1" } });
      _mockMixerStateClient
          .Setup(x => x.GetMixerStateAsync("proj-1", It.IsAny<CancellationToken>()))
          .ReturnsAsync((MixerState?)null);
      _mockMixerStateClient
          .Setup(x => x.GetMixerPresetsAsync("proj-1", It.IsAny<CancellationToken>()))
          .ReturnsAsync(new List<MixerPreset> { new MixerPreset { Id = "p1", Name = "Preset 1", ProjectId = "proj-1" } });

      var vm = new EffectsMixerViewModel(_mockEffectsMeterClient.Object, _mockEffectChainClient.Object, _mockMixerStateClient.Object);
      vm.SelectedProjectId = "proj-1";
      await Task.Delay(150);

      Assert.IsTrue(vm.EffectChains.Count > 0 || vm.MixerPresets.Count > 0, "Load should have populated at least one collection");

      vm.SelectedProjectId = null;
      Assert.AreEqual(0, vm.EffectChains.Count);
      Assert.AreEqual(0, vm.MixerPresets.Count);
      Assert.IsNull(vm.SelectedEffectChain);
      Assert.IsNull(vm.MixerState);
    }
  }
}
