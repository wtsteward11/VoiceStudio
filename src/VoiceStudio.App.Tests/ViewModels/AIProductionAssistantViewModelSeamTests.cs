using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.UI.Dispatching;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using System;
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
  /// Seam-aware tests for AIProductionAssistantViewModel.
  /// Instantiates ViewModel with mocked IAIProductionAssistantClient.
  /// Supports "AIProductionAssistantViewModel migrated to IAIProductionAssistantClient" claims.
  /// See docs/governance/TEST_CLASSIFICATION.md.
  /// </summary>
  [TestClass]
  [TestCategory("SeamAware")]
  public class AIProductionAssistantViewModelSeamTests
  {
    private Mock<IAIProductionAssistantClient> _mockClient = null!;
    private IViewModelContext _context = null!;
    private DispatcherQueueController? _dispatcherController;

    [TestInitialize]
    public void Setup()
    {
      TestAppServicesHelper.EnsureInitialized();
      _mockClient = new Mock<IAIProductionAssistantClient>();
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
      _ = new AIProductionAssistantViewModel(_context, _mockClient.Object);
      _mockClient.Verify(x => x.SendQueryAsync(It.IsAny<AIProductionAssistantQueryRequest>(), It.IsAny<CancellationToken>()), Times.Never);
      _mockClient.Verify(x => x.ExecuteActionAsync(It.IsAny<AIProductionAssistantExecuteRequest>(), It.IsAny<CancellationToken>()), Times.Never);
      _mockClient.Verify(x => x.GetContextAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [TestMethod]
    public void Constructor_WithClient_CreatesInstance()
    {
      var vm = new AIProductionAssistantViewModel(_context, _mockClient.Object);
      Assert.IsNotNull(vm);
      Assert.AreEqual(PanelIds.AIProductionAssistant, vm.PanelId);
      Assert.IsNotNull(vm.SendQueryCommand);
      Assert.IsNotNull(vm.ExecuteActionCommand);
      Assert.IsNotNull(vm.LoadContextCommand);
      Assert.IsNotNull(vm.ClearChatCommand);
      Assert.IsNotNull(vm.RefreshCommand);
    }

    [TestMethod]
    [ExpectedException(typeof(ArgumentNullException))]
    public void Constructor_WithNullClient_Throws()
    {
      _ = new AIProductionAssistantViewModel(_context, null!);
    }
  }
}
