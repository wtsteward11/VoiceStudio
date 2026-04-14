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
  /// Seam-aware tests for AssistantViewModel.
  /// Instantiates ViewModel with mocked IAssistantClient, IProjectsClient.
  /// Supports "AssistantViewModel migrated to IAssistantClient" claims.
  /// See docs/governance/TEST_CLASSIFICATION.md.
  /// </summary>
  [TestClass]
  [TestCategory("SeamAware")]
  public class AssistantViewModelSeamTests
  {
    private Mock<IAssistantClient> _mockAssistantClient = null!;
    private Mock<IProjectsClient> _mockProjectsClient = null!;
    private IViewModelContext _context = null!;
    private DispatcherQueueController? _dispatcherController;

    [TestInitialize]
    public void Setup()
    {
      TestAppServicesHelper.EnsureInitialized();
      _mockAssistantClient = new Mock<IAssistantClient>();
      _mockProjectsClient = new Mock<IProjectsClient>();
      _dispatcherController = DispatcherQueueController.CreateOnDedicatedThread();
      var dispatcher = _dispatcherController.DispatcherQueue;
      _context = new ViewModelContext(NullLogger.Instance, dispatcher);

      _mockProjectsClient
        .Setup(x => x.GetProjectsAsync(It.IsAny<CancellationToken>()))
        .ReturnsAsync(new List<Project>());
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
      _ = new AssistantViewModel(_context, _mockAssistantClient.Object, _mockProjectsClient.Object);
      _mockAssistantClient.Verify(x => x.GetConversationsAsync(It.IsAny<CancellationToken>()), Times.Never);
      _mockProjectsClient.Verify(x => x.GetProjectsAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [TestMethod]
    public void Constructor_WithClients_CreatesInstance()
    {
      var vm = new AssistantViewModel(_context, _mockAssistantClient.Object, _mockProjectsClient.Object);
      Assert.IsNotNull(vm);
      Assert.AreEqual("assistant", vm.PanelId);
      Assert.IsNotNull(vm.SendMessageCommand);
      Assert.IsNotNull(vm.LoadConversationsCommand);
      Assert.IsNotNull(vm.LoadProjectsCommand);
    }

    [TestMethod]
    [ExpectedException(typeof(ArgumentNullException))]
    public void Constructor_WithNullAssistantClient_Throws()
    {
      _ = new AssistantViewModel(_context, null!, _mockProjectsClient.Object);
    }

    [TestMethod]
    [ExpectedException(typeof(ArgumentNullException))]
    public void Constructor_WithNullProjectsClient_Throws()
    {
      _ = new AssistantViewModel(_context, _mockAssistantClient.Object, null!);
    }

    [TestMethod]
    public void ViewModel_ImplementsIPanelLifecycle()
    {
      var vm = new AssistantViewModel(_context, _mockAssistantClient.Object, _mockProjectsClient.Object);
      Assert.IsTrue(vm is IPanelLifecycle);
    }
  }
}
