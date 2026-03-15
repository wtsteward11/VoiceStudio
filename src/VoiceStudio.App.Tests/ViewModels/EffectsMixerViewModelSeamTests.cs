using Microsoft.UI.Dispatching;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using System;
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
  /// Instantiates ViewModel with mocked IBackendClient and IEffectsMeterClient.
  /// Supports EffectsMixer seam migration (Slice 1: IEffectsMeterClient for meters).
  /// See docs/governance/TEST_CLASSIFICATION.md.
  /// </summary>
  [TestClass]
  [TestCategory("SeamAware")]
  public class EffectsMixerViewModelSeamTests
  {
    private Mock<IBackendClient> _mockBackendClient = null!;
    private Mock<IEffectsMeterClient> _mockEffectsMeterClient = null!;
    private DispatcherQueueController? _dispatcherController;

    [TestInitialize]
    public void Setup()
    {
      TestAppServicesHelper.EnsureInitialized();
      _mockBackendClient = new Mock<IBackendClient>();
      _mockEffectsMeterClient = new Mock<IEffectsMeterClient>();
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
      _ = new EffectsMixerViewModel(_mockBackendClient.Object, _mockEffectsMeterClient.Object);
      _mockEffectsMeterClient.Verify(x => x.GetAudioMetersAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
      _mockBackendClient.Verify(x => x.GetEffectChainsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [TestMethod]
    public void Constructor_WithClients_CreatesInstance()
    {
      var vm = new EffectsMixerViewModel(_mockBackendClient.Object, _mockEffectsMeterClient.Object);
      Assert.IsNotNull(vm);
      Assert.AreEqual("effectsmixer", vm.PanelId);
      Assert.IsNotNull(vm.LoadMetersCommand);
      Assert.IsNotNull(vm.LoadEffectChainsCommand);
    }

    [TestMethod]
    [ExpectedException(typeof(ArgumentNullException))]
    public void Constructor_WithNullBackendClient_Throws()
    {
      _ = new EffectsMixerViewModel(null!, _mockEffectsMeterClient.Object);
    }

    [TestMethod]
    [ExpectedException(typeof(ArgumentNullException))]
    public void Constructor_WithNullEffectsMeterClient_Throws()
    {
      _ = new EffectsMixerViewModel(_mockBackendClient.Object, null!);
    }

    [TestMethod]
    public void ViewModel_ImplementsIPanelLifecycle()
    {
      var vm = new EffectsMixerViewModel(_mockBackendClient.Object, _mockEffectsMeterClient.Object);
      Assert.IsTrue(vm is IPanelLifecycle);
    }
  }
}
