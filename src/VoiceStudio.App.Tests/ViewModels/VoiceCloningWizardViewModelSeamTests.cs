using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.UI.Dispatching;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using VoiceStudio.App.Tests.Fixtures;
using VoiceStudio.App.ViewModels;
using VoiceStudio.Core.Panels;
using VoiceStudio.Core.Services;

namespace VoiceStudio.App.Tests.ViewModels
{
  /// <summary>
  /// Seam-aware tests for VoiceCloningWizardViewModel.
  /// Instantiates ViewModel with mocked IVoiceCloningWizardClient.
  /// Supports "VoiceCloningWizardViewModel migrated to IVoiceCloningWizardClient" claims.
  /// See docs/governance/TEST_CLASSIFICATION.md.
  /// </summary>
  [TestClass]
  [TestCategory("SeamAware")]
  public class VoiceCloningWizardViewModelSeamTests
  {
    private Mock<IVoiceCloningWizardClient> _mockWizardClient = null!;
    private IViewModelContext _context = null!;
    private DispatcherQueueController? _dispatcherController;

    [TestInitialize]
    public void Setup()
    {
      _mockWizardClient = new Mock<IVoiceCloningWizardClient>();
      _dispatcherController = DispatcherQueueController.CreateOnDedicatedThread();
      var dispatcher = _dispatcherController.DispatcherQueue;
      _context = new ViewModelContext(NullLogger.Instance, dispatcher);

      _mockWizardClient
          .Setup(x => x.GetEnginesAsync(It.IsAny<CancellationToken>()))
          .ReturnsAsync(new List<string> { "xtts", "chatterbox" });
    }

    [TestCleanup]
    public void Cleanup()
    {
      DispatcherQueueTestHelpers.ShutdownSyncBounded(_dispatcherController);
    }

    [TestMethod]
    public void Constructor_WithIVoiceCloningWizardClient_CreatesInstance()
    {
      var vm = new VoiceCloningWizardViewModel(_context, _mockWizardClient.Object);
      Assert.IsNotNull(vm);
      Assert.AreEqual(PanelIds.VoiceCloningWizard, vm.PanelId);
      Assert.IsNotNull(vm.BrowseAudioCommand);
      Assert.IsNotNull(vm.ValidateAudioCommand);
      Assert.IsNotNull(vm.NextStepCommand);
      Assert.IsNotNull(vm.PreviousStepCommand);
      Assert.IsNotNull(vm.StartProcessingCommand);
      Assert.IsNotNull(vm.FinalizeWizardCommand);
    }

    [TestMethod]
    [ExpectedException(typeof(ArgumentNullException))]
    public void Constructor_WithNullWizardClient_Throws()
    {
      _ = new VoiceCloningWizardViewModel(_context, null!);
    }

    /// <summary>
    /// Verifies LoadEnginesAsync is called from InitializeAsync (Loaded), not constructor (ADR-047).
    /// </summary>
    [TestMethod]
    public async Task InitializeAsync_CallsLoadEngines_CallsIVoiceCloningWizardClient_GetEnginesAsync()
    {
      var vm = new VoiceCloningWizardViewModel(_context, _mockWizardClient.Object);
      _mockWizardClient.Invocations.Clear();

      vm.InitializeAsync();
      await Task.Delay(200);

      _mockWizardClient.Verify(
          x => x.GetEnginesAsync(It.IsAny<CancellationToken>()),
          Times.AtLeastOnce);
    }

    /// <summary>
    /// Invariant: constructor must not call any client methods before activation.
    /// Prevents constructor fire-and-forget regression (RETAINED_ASYNC_RULE, ADR-047).
    /// </summary>
    [TestMethod]
    public void Constructor_DoesNotCallClient_BeforeActivation()
    {
      _ = new VoiceCloningWizardViewModel(_context, _mockWizardClient.Object);
      _mockWizardClient.Verify(x => x.GetEnginesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }
  }
}
