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
  /// Seam-aware tests for MixAssistantViewModel.
  /// Instantiates ViewModel with mocked IMixAssistantClient.
  /// Supports "MixAssistantViewModel migrated to IMixAssistantClient" claims.
  /// See docs/governance/TEST_CLASSIFICATION.md.
  /// </summary>
  [TestClass]
  [TestCategory("SeamAware")]
  public class MixAssistantViewModelSeamTests
  {
    private Mock<IMixAssistantClient> _mockMixClient = null!;
    private Mock<IProjectsClient> _mockProjectsClient = null!;
    private IViewModelContext _context = null!;
    private DispatcherQueueController? _dispatcherController;

    [TestInitialize]
    public void Setup()
    {
      TestAppServicesHelper.EnsureInitialized();
      _mockMixClient = new Mock<IMixAssistantClient>();
      _mockProjectsClient = new Mock<IProjectsClient>();
      _dispatcherController = DispatcherQueueController.CreateOnDedicatedThread();
      var dispatcher = _dispatcherController.DispatcherQueue;
      _context = new ViewModelContext(NullLogger.Instance, dispatcher);

      _mockProjectsClient
          .Setup(x => x.GetProjectsAsync(It.IsAny<CancellationToken>()))
          .ReturnsAsync(new List<Project>());
      _mockMixClient
          .Setup(x => x.GetSuggestionsAsync(It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
          .ReturnsAsync(Array.Empty<MixSuggestion>());
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
      _ = new MixAssistantViewModel(_context, _mockMixClient.Object, _mockProjectsClient.Object);
      _mockMixClient.Verify(x => x.GetSuggestionsAsync(It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never);
      _mockProjectsClient.Verify(x => x.GetProjectsAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [TestMethod]
    public void Constructor_WithIMixAssistantClient_CreatesInstance()
    {
      var vm = new MixAssistantViewModel(_context, _mockMixClient.Object, _mockProjectsClient.Object);
      Assert.IsNotNull(vm);
      Assert.AreEqual("mix-assistant", vm.PanelId);
      Assert.IsNotNull(vm.AnalyzeMixCommand);
      Assert.IsNotNull(vm.ApplySuggestionCommand);
      Assert.IsNotNull(vm.LoadSuggestionsCommand);
      Assert.IsNotNull(vm.LoadProjectsCommand);
    }

    [TestMethod]
    [ExpectedException(typeof(ArgumentNullException))]
    public void Constructor_WithNullMixClient_Throws()
    {
      _ = new MixAssistantViewModel(_context, null!, _mockProjectsClient.Object);
    }

    [TestMethod]
    [ExpectedException(typeof(ArgumentNullException))]
    public void Constructor_WithNullProjectsClient_Throws()
    {
      _ = new MixAssistantViewModel(_context, _mockMixClient.Object, null!);
    }

    [TestMethod]
    public void ViewModel_ImplementsIPanelLifecycle()
    {
      var vm = new MixAssistantViewModel(_context, _mockMixClient.Object, _mockProjectsClient.Object);
      Assert.IsTrue(vm is IPanelLifecycle);
    }
  }
}
