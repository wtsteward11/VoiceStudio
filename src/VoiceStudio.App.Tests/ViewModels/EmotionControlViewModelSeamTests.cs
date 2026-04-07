using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.UI.Dispatching;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using System;
using System.Collections.Generic;
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
  /// Seam-aware tests for EmotionControlViewModel.
  /// Instantiates ViewModel with mocked IEmotionControlClient.
  /// Supports "EmotionControlViewModel migrated to IEmotionControlClient" claims.
  /// See docs/governance/TEST_CLASSIFICATION.md.
  /// </summary>
  [TestClass]
  [TestCategory("SeamAware")]
  public class EmotionControlViewModelSeamTests
  {
    private Mock<IEmotionControlClient> _mockEmotionControlClient = null!;
    private IViewModelContext _context = null!;
    private DispatcherQueueController? _dispatcherController;

    [TestInitialize]
    public void Setup()
    {
      TestAppServicesHelper.EnsureInitialized();
      _mockEmotionControlClient = new Mock<IEmotionControlClient>();
      _dispatcherController = DispatcherQueueController.CreateOnDedicatedThread();
      var dispatcher = _dispatcherController.DispatcherQueue;
      _context = new ViewModelContext(NullLogger.Instance, dispatcher);
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
      _ = new EmotionControlViewModel(_context, _mockEmotionControlClient.Object, dialogService: null);
      _mockEmotionControlClient.Verify(x => x.GetPresetsAsync(It.IsAny<CancellationToken>()), Times.Never);
      _mockEmotionControlClient.Verify(x => x.GetAvailableEmotionsAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [TestMethod]
    public void Constructor_WithClients_CreatesInstance()
    {
      var vm = new EmotionControlViewModel(_context, _mockEmotionControlClient.Object, dialogService: null);
      Assert.IsNotNull(vm);
      Assert.AreEqual(PanelIds.EmotionControl, vm.PanelId);
    }

    [TestMethod]
    [ExpectedException(typeof(ArgumentNullException))]
    public void Constructor_WithNullEmotionControlClient_Throws()
    {
      _ = new EmotionControlViewModel(_context, null!, dialogService: null);
    }

    /// <summary>GAP-050: apply-extended response diagnostics surface in status (seam stays IEmotionControlClient).</summary>
    [TestMethod]
    public async Task ApplyEmotionCommand_WhenClientReturnsProsodyHandling_IncludesActionInStatus()
    {
      _mockEmotionControlClient
        .Setup(x => x.ApplyEmotionAsync(It.IsAny<EmotionApplyExtendedRequest>(), It.IsAny<CancellationToken>()))
        .ReturnsAsync(new EmotionApplyExtendedResponse
        {
          AudioId = "out1",
          AudioUrl = "/api/voice/audio/out1",
          EmotionMappingSource = "canonical_preset",
          ProsodyHandling = new ProsodyHandlingDiagnosticsDto
          {
            Action = "applied",
            AppliedOperations = new List<string> { "pitch_shift" },
            Warnings = new List<string>(),
          },
        });

      var vm = new EmotionControlViewModel(_context, _mockEmotionControlClient.Object, dialogService: null);
      vm.TargetAudioId = "a1";
      vm.SelectedPrimaryEmotion = "warm";
      await vm.ApplyEmotionCommand.ExecuteAsync(CancellationToken.None);
      Assert.IsNotNull(vm.StatusMessage);
      StringAssert.Contains(vm.StatusMessage, "(applied)");
    }
  }
}
