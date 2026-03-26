using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.UI.Dispatching;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using System;
using System.Collections.Generic;
using System.Threading;
using VoiceStudio.App.Services;
using VoiceStudio.App.Tests.Fixtures;
using VoiceStudio.App.ViewModels;
using VoiceStudio.Core.Models;
using VoiceStudio.Core.Panels;
using VoiceStudio.Core.Services;

namespace VoiceStudio.App.Tests.ViewModels
{
  /// <summary>
  /// Seam-aware tests for AIMixingMasteringViewModel.
  /// Instantiates ViewModel with mocked IAIMixingClient.
  /// Supports "AIMixingMasteringViewModel migrated to IAIMixingClient" claims.
  /// See docs/governance/TEST_CLASSIFICATION.md.
  /// </summary>
  [TestClass]
  [TestCategory("SeamAware")]
  public class AIMixingMasteringViewModelSeamTests
  {
    private Mock<IAIMixingClient> _mockClient = null!;
    private IViewModelContext _context = null!;
    private DispatcherQueueController? _dispatcherController;

    [TestInitialize]
    public void Setup()
    {
      TestAppServicesHelper.EnsureInitialized();
      _mockClient = new Mock<IAIMixingClient>();
      _dispatcherController = DispatcherQueueController.CreateOnDedicatedThread();
      _context = new ViewModelContext(NullLogger.Instance, _dispatcherController.DispatcherQueue);

      _mockClient
          .Setup(x => x.AnalyzeMixAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
          .ReturnsAsync(new MixAnalysisResponse { Suggestions = new System.Collections.Generic.List<MixSuggestionData>() });
      _mockClient
          .Setup(x => x.ApplyMixAsync(It.IsAny<MixApplyRequest>(), It.IsAny<CancellationToken>()))
          .ReturnsAsync(new MixApplyResponse { Applied = 1, Message = "OK" });
      _mockClient
          .Setup(x => x.AnalyzeMasteringAsync(It.IsAny<MasteringAnalysisRequest>(), It.IsAny<CancellationToken>()))
          .ReturnsAsync(new MasteringAnalysisResponse { ProjectId = "p1", CurrentLoudness = -16f, TargetLoudness = -16f });
      _mockClient
          .Setup(x => x.ApplyMasteringAsync(It.IsAny<MasteringApplyRequest>(), It.IsAny<CancellationToken>()))
          .ReturnsAsync(new MasteringApplyResponse { OutputAudioId = "a1", OutputAudioUrl = "url", FinalLoudness = -16f });
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
      _ = new AIMixingMasteringViewModel(_context, _mockClient.Object);
      _mockClient.Verify(x => x.AnalyzeMixAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
      _mockClient.Verify(x => x.ApplyMixAsync(It.IsAny<MixApplyRequest>(), It.IsAny<CancellationToken>()), Times.Never);
      _mockClient.Verify(x => x.AnalyzeMasteringAsync(It.IsAny<MasteringAnalysisRequest>(), It.IsAny<CancellationToken>()), Times.Never);
      _mockClient.Verify(x => x.ApplyMasteringAsync(It.IsAny<MasteringApplyRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [TestMethod]
    public void Constructor_WithClient_CreatesInstance()
    {
      var vm = new AIMixingMasteringViewModel(_context, _mockClient.Object);
      Assert.IsNotNull(vm);
      Assert.IsNotNull(vm.Suggestions);
      Assert.AreEqual(PanelIds.AIMixingMastering, vm.PanelId);
    }

    [TestMethod]
    [ExpectedException(typeof(ArgumentNullException))]
    public void Constructor_WithNullClient_Throws()
    {
      _ = new AIMixingMasteringViewModel(_context, null!);
    }
  }
}
