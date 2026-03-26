using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.UI.Dispatching;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using System;
using System.Threading;
using VoiceStudio.App.Services;
using VoiceStudio.App.Tests.Fixtures;
using VoiceStudio.App.ViewModels;
using VoiceStudio.Core.Panels;
using VoiceStudio.Core.Services;

namespace VoiceStudio.App.Tests.ViewModels
{
  /// <summary>
  /// Seam-aware tests for TextHighlightingViewModel.
  /// Instantiates ViewModel with mocked ITextHighlightingClient, IProjectsClient, IProjectAudioClient.
  /// Supports "TextHighlightingViewModel migrated to ITextHighlightingClient" claims.
  /// See docs/governance/TEST_CLASSIFICATION.md.
  /// </summary>
  [TestClass]
  [TestCategory("SeamAware")]
  public class TextHighlightingViewModelSeamTests
  {
    private Mock<ITextHighlightingClient> _mockClient = null!;
    private Mock<IProjectsClient> _mockProjectsClient = null!;
    private Mock<IProjectAudioClient> _mockProjectAudioClient = null!;
    private IViewModelContext _context = null!;
    private DispatcherQueueController? _dispatcherController;

    [TestInitialize]
    public void Setup()
    {
      TestAppServicesHelper.EnsureInitialized();
      _mockClient = new Mock<ITextHighlightingClient>();
      _mockProjectsClient = new Mock<IProjectsClient>();
      _mockProjectAudioClient = new Mock<IProjectAudioClient>();
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
      _ = new TextHighlightingViewModel(_context, _mockClient.Object, _mockProjectsClient.Object, _mockProjectAudioClient.Object);

      _mockClient.Verify(x => x.CreateSessionAsync(It.IsAny<VoiceStudio.Core.Models.TextHighlightingCreateRequest>(), It.IsAny<CancellationToken>()), Times.Never);
      _mockProjectsClient.Verify(x => x.GetProjectsAsync(It.IsAny<CancellationToken>()), Times.Never);
      _mockProjectAudioClient.Verify(x => x.ListProjectAudioAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [TestMethod]
    public void Constructor_WithClient_CreatesInstance()
    {
      var vm = new TextHighlightingViewModel(_context, _mockClient.Object, _mockProjectsClient.Object, _mockProjectAudioClient.Object);

      Assert.IsNotNull(vm);
      Assert.AreEqual(PanelIds.TextHighlighting, vm.PanelId);
      Assert.IsNotNull(vm.LoadAudioFilesCommand);
      Assert.IsNotNull(vm.RefreshCommand);
      Assert.IsNotNull(vm.CreateSessionCommand);
    }

    [TestMethod]
    [ExpectedException(typeof(ArgumentNullException))]
    public void Constructor_WithNullClient_Throws()
    {
      _ = new TextHighlightingViewModel(_context, null!, _mockProjectsClient.Object, _mockProjectAudioClient.Object);
    }

    [TestMethod]
    public void ViewModel_ImplementsIPanelLifecycle()
    {
      var vm = new TextHighlightingViewModel(_context, _mockClient.Object, _mockProjectsClient.Object, _mockProjectAudioClient.Object);
      Assert.IsTrue(vm is IPanelLifecycle);
    }
  }
}
