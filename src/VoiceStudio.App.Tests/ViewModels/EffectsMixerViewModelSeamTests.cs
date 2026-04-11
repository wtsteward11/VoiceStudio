using Microsoft.UI.Dispatching;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using VoiceStudio.App.Core.Models;
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
    /// GAP-039 AC3: IsEffectChainBypassed defaults to false.
    /// </summary>
    [TestMethod]
    public void IsEffectChainBypassed_DefaultsFalse()
    {
      var vm = new EffectsMixerViewModel(_mockEffectsMeterClient.Object, _mockEffectChainClient.Object, _mockMixerStateClient.Object);
      Assert.IsFalse(vm.IsEffectChainBypassed);
    }

    /// <summary>
    /// GAP-039 AC3: ApplyEffectChainCommand invokes ProcessAudioWithChainAsync with bypassChain matching IsEffectChainBypassed=true.
    /// </summary>
    [TestMethod]
    public async Task ApplyEffectChain_WhenBypassed_SendsBypassFlag()
    {
      _mockEffectChainClient
          .Setup(x => x.GetEffectChainsAsync("proj-1", It.IsAny<CancellationToken>()))
          .ReturnsAsync(new List<EffectChain> { new EffectChain { Id = "c1", Name = "C1", ProjectId = "proj-1" } });
      _mockMixerStateClient
          .Setup(x => x.GetMixerStateAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
          .ReturnsAsync((MixerState?)null);
      _mockMixerStateClient
          .Setup(x => x.GetMixerPresetsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
          .ReturnsAsync(new List<MixerPreset>());

      _mockEffectChainClient
          .Setup(x => x.ProcessAudioWithChainAsync("proj-1", "c1", "audio-1", null, true, false, It.IsAny<CancellationToken>()))
          .ReturnsAsync(new EffectProcessResponse { Success = true, OutputAudioId = "audio-1", Message = "bypass" })
          .Verifiable();

      var vm = new EffectsMixerViewModel(_mockEffectsMeterClient.Object, _mockEffectChainClient.Object, _mockMixerStateClient.Object);
      vm.SelectedProjectId = "proj-1";
      await Task.Delay(200);
      vm.SelectedAudioId = "audio-1";
      vm.IsEffectChainBypassed = true;
      vm.ApplyEffectChainCommand.Execute("c1");
      await Task.Delay(300);

      _mockEffectChainClient.Verify(
          x => x.ProcessAudioWithChainAsync("proj-1", "c1", "audio-1", null, true, false, It.IsAny<CancellationToken>()),
          Times.Once);
    }

    /// <summary>
    /// GAP-039 AC3: PreviewEffectChainCommand invokes ProcessAudioWithChainAsync with preview=true.
    /// </summary>
    [TestMethod]
    public async Task PreviewEffectChain_SendsPreviewFlag()
    {
      _mockEffectChainClient
          .Setup(x => x.GetEffectChainsAsync("proj-1", It.IsAny<CancellationToken>()))
          .ReturnsAsync(new List<EffectChain> { new EffectChain { Id = "c1", Name = "C1", ProjectId = "proj-1" } });
      _mockMixerStateClient
          .Setup(x => x.GetMixerStateAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
          .ReturnsAsync((MixerState?)null);
      _mockMixerStateClient
          .Setup(x => x.GetMixerPresetsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
          .ReturnsAsync(new List<MixerPreset>());

      _mockEffectChainClient
          .Setup(x => x.ProcessAudioWithChainAsync("proj-1", "c1", "audio-1", null, false, true, It.IsAny<CancellationToken>()))
          .ReturnsAsync(new EffectProcessResponse { Success = true, OutputAudioId = "out-1", Message = "ok [preview]" })
          .Verifiable();

      var vm = new EffectsMixerViewModel(_mockEffectsMeterClient.Object, _mockEffectChainClient.Object, _mockMixerStateClient.Object);
      vm.SelectedProjectId = "proj-1";
      await Task.Delay(200);
      vm.SelectedAudioId = "audio-1";
      vm.PreviewEffectChainCommand.Execute("c1");
      await Task.Delay(300);

      _mockEffectChainClient.Verify(
          x => x.ProcessAudioWithChainAsync("proj-1", "c1", "audio-1", null, false, true, It.IsAny<CancellationToken>()),
          Times.Once);
    }

    /// <summary>
    /// GAP-039 AC3: ApplyEffectChainCommand with bypass OFF sends bypassChain=false.
    /// </summary>
    [TestMethod]
    public async Task ApplyEffectChain_WhenNotBypassed_SendsBypassFalse()
    {
      _mockEffectChainClient
          .Setup(x => x.GetEffectChainsAsync("proj-1", It.IsAny<CancellationToken>()))
          .ReturnsAsync(new List<EffectChain> { new EffectChain { Id = "c1", Name = "C1", ProjectId = "proj-1" } });
      _mockMixerStateClient
          .Setup(x => x.GetMixerStateAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
          .ReturnsAsync((MixerState?)null);
      _mockMixerStateClient
          .Setup(x => x.GetMixerPresetsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
          .ReturnsAsync(new List<MixerPreset>());

      _mockEffectChainClient
          .Setup(x => x.ProcessAudioWithChainAsync("proj-1", "c1", "audio-1", null, false, false, It.IsAny<CancellationToken>()))
          .ReturnsAsync(new EffectProcessResponse { Success = true, OutputAudioId = "out-1", Message = "applied" })
          .Verifiable();

      var vm = new EffectsMixerViewModel(_mockEffectsMeterClient.Object, _mockEffectChainClient.Object, _mockMixerStateClient.Object);
      vm.SelectedProjectId = "proj-1";
      await Task.Delay(200);
      vm.SelectedAudioId = "audio-1";
      Assert.IsFalse(vm.IsEffectChainBypassed);
      vm.ApplyEffectChainCommand.Execute("c1");
      await Task.Delay(300);

      _mockEffectChainClient.Verify(
          x => x.ProcessAudioWithChainAsync("proj-1", "c1", "audio-1", null, false, false, It.IsAny<CancellationToken>()),
          Times.Once);
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

    /// <summary>GAP-048: Studio Sound creates a transient chain and processes selected audio.</summary>
    [TestMethod]
    public async Task StudioSound_CreatesChainAndProcesses_WhenAudioSelected()
    {
      _mockEffectChainClient
          .Setup(x => x.GetEffectChainsAsync("proj-1", It.IsAny<CancellationToken>()))
          .ReturnsAsync(new List<EffectChain>());
      _mockMixerStateClient
          .Setup(x => x.GetMixerStateAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
          .ReturnsAsync((MixerState?)null);
      _mockMixerStateClient
          .Setup(x => x.GetMixerPresetsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
          .ReturnsAsync(new List<MixerPreset>());

      _mockEffectChainClient
          .Setup(x => x.CreateEffectChainAsync("proj-1", It.IsAny<EffectChain>(), It.IsAny<CancellationToken>()))
          .ReturnsAsync((string _, EffectChain c, CancellationToken _) => new EffectChain
          {
            Id = "ss-chain",
            Name = c.Name,
            ProjectId = "proj-1",
            Effects = c.Effects
          });

      _mockEffectChainClient
          .Setup(x => x.ProcessAudioWithChainAsync("proj-1", "ss-chain", "audio-1", null, false, false, It.IsAny<CancellationToken>()))
          .ReturnsAsync(new EffectProcessResponse { Success = true, OutputAudioId = "out-ss" })
          .Verifiable();

      _mockEffectChainClient
          .Setup(x => x.DeleteEffectChainAsync("proj-1", "ss-chain", It.IsAny<CancellationToken>()))
          .ReturnsAsync(true)
          .Verifiable();

      var vm = new EffectsMixerViewModel(_mockEffectsMeterClient.Object, _mockEffectChainClient.Object, _mockMixerStateClient.Object);
      vm.SelectedProjectId = "proj-1";
      await Task.Delay(200);
      vm.SelectedAudioId = "audio-1";

      vm.RunStudioSoundCommand.Execute(null);
      await Task.Delay(500);

      _mockEffectChainClient.Verify(
          x => x.ProcessAudioWithChainAsync("proj-1", "ss-chain", "audio-1", null, false, false, It.IsAny<CancellationToken>()),
          Times.Once);
      _mockEffectChainClient.Verify(
          x => x.DeleteEffectChainAsync("proj-1", "ss-chain", It.IsAny<CancellationToken>()),
          Times.Once);
      Assert.AreEqual("out-ss", vm.StudioSoundOutputAudioId);
    }

    /// <summary>GAP-048: Processing failure leaves output id null and does not crash.</summary>
    [TestMethod]
    public async Task StudioSound_FailsHonestly_WhenProcessThrows()
    {
      _mockEffectChainClient
          .Setup(x => x.GetEffectChainsAsync("proj-1", It.IsAny<CancellationToken>()))
          .ReturnsAsync(new List<EffectChain>());
      _mockMixerStateClient
          .Setup(x => x.GetMixerStateAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
          .ReturnsAsync((MixerState?)null);
      _mockMixerStateClient
          .Setup(x => x.GetMixerPresetsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
          .ReturnsAsync(new List<MixerPreset>());

      _mockEffectChainClient
          .Setup(x => x.CreateEffectChainAsync("proj-1", It.IsAny<EffectChain>(), It.IsAny<CancellationToken>()))
          .ReturnsAsync((string _, EffectChain c, CancellationToken _) => new EffectChain
          {
            Id = "ss-chain",
            Name = c.Name,
            ProjectId = "proj-1",
            Effects = c.Effects
          });

      _mockEffectChainClient
          .Setup(x => x.ProcessAudioWithChainAsync("proj-1", "ss-chain", "audio-1", null, false, false, It.IsAny<CancellationToken>()))
          .ThrowsAsync(new InvalidOperationException("process failed"));

      _mockEffectChainClient
          .Setup(x => x.DeleteEffectChainAsync("proj-1", "ss-chain", It.IsAny<CancellationToken>()))
          .ReturnsAsync(true);

      var vm = new EffectsMixerViewModel(_mockEffectsMeterClient.Object, _mockEffectChainClient.Object, _mockMixerStateClient.Object);
      vm.SelectedProjectId = "proj-1";
      await Task.Delay(200);
      vm.SelectedAudioId = "audio-1";

      vm.RunStudioSoundCommand.Execute(null);
      await Task.Delay(500);

      Assert.IsNull(vm.StudioSoundOutputAudioId);
    }

    /// <summary>GAP-048: Transient chain contains denoise → compressor → normalize.</summary>
    [TestMethod]
    public async Task StudioSound_ChainHasThreeEffects_DenoiseCompressorNormalize()
    {
      EffectChain? captured = null;
      _mockEffectChainClient
          .Setup(x => x.GetEffectChainsAsync("proj-1", It.IsAny<CancellationToken>()))
          .ReturnsAsync(new List<EffectChain>());
      _mockMixerStateClient
          .Setup(x => x.GetMixerStateAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
          .ReturnsAsync((MixerState?)null);
      _mockMixerStateClient
          .Setup(x => x.GetMixerPresetsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
          .ReturnsAsync(new List<MixerPreset>());

      _mockEffectChainClient
          .Setup(x => x.CreateEffectChainAsync("proj-1", It.IsAny<EffectChain>(), It.IsAny<CancellationToken>()))
          .Callback<string, EffectChain, CancellationToken>((_, chain, _) => captured = chain)
          .ReturnsAsync((string _, EffectChain c, CancellationToken _) => new EffectChain
          {
            Id = "ss-chain",
            Name = c.Name,
            ProjectId = "proj-1",
            Effects = c.Effects
          });

      _mockEffectChainClient
          .Setup(x => x.ProcessAudioWithChainAsync("proj-1", "ss-chain", "audio-1", null, false, false, It.IsAny<CancellationToken>()))
          .ReturnsAsync(new EffectProcessResponse { Success = true, OutputAudioId = "out-ss" });

      _mockEffectChainClient
          .Setup(x => x.DeleteEffectChainAsync("proj-1", "ss-chain", It.IsAny<CancellationToken>()))
          .ReturnsAsync(true);

      var vm = new EffectsMixerViewModel(_mockEffectsMeterClient.Object, _mockEffectChainClient.Object, _mockMixerStateClient.Object);
      vm.SelectedProjectId = "proj-1";
      await Task.Delay(200);
      vm.SelectedAudioId = "audio-1";

      vm.RunStudioSoundCommand.Execute(null);
      await Task.Delay(500);

      Assert.IsNotNull(captured);
      Assert.AreEqual(3, captured!.Effects.Count);
      var types = captured.Effects.OrderBy(e => e.Order).Select(e => e.Type).ToList();
      CollectionAssert.AreEqual(new[] { "denoise", "compressor", "normalize" }, types);
    }

    [TestMethod]
    public void StudioSound_CannotRun_WhenNoAudioSelected()
    {
      var vm = new EffectsMixerViewModel(_mockEffectsMeterClient.Object, _mockEffectChainClient.Object, _mockMixerStateClient.Object);
      vm.SelectedProjectId = "proj-1";
      Assert.IsFalse(vm.CanRunStudioSound);
    }

    [TestMethod]
    public void StudioSound_CannotRun_WhenAlreadyRunning()
    {
      var vm = new EffectsMixerViewModel(_mockEffectsMeterClient.Object, _mockEffectChainClient.Object, _mockMixerStateClient.Object);
      vm.SelectedProjectId = "proj-1";
      vm.SelectedAudioId = "audio-1";
      vm.IsStudioSoundRunning = true;
      Assert.IsFalse(vm.CanRunStudioSound);
    }
  }
}

