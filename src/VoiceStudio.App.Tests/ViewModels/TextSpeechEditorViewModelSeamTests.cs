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
using VoiceStudio.Core.Models;
using VoiceStudio.App.ViewModels;
using VoiceStudio.Core.Panels;
using VoiceStudio.Core.Services;

namespace VoiceStudio.App.Tests.ViewModels
{
  /// <summary>
  /// Seam-aware tests for TextSpeechEditorViewModel.
  /// Instantiates ViewModel with mocked ITextSpeechEditorClient, IProjectsClient, IProfilesClient, IAudioPlayerService.
  /// Supports "TextSpeechEditorViewModel migrated to ITextSpeechEditorClient" claims.
  /// See docs/governance/TEST_CLASSIFICATION.md.
  /// </summary>
  [TestClass]
  [TestCategory("SeamAware")]
  public class TextSpeechEditorViewModelSeamTests
  {
    private Mock<ITextSpeechEditorClient> _mockTextSpeechEditorClient = null!;
    private Mock<IProjectsClient> _mockProjectsClient = null!;
    private Mock<IProfilesClient> _mockProfilesClient = null!;
    private Mock<IAudioPlayerService> _mockAudioPlayer = null!;
    private IViewModelContext _context = null!;
    private DispatcherQueueController? _dispatcherController;

    [TestInitialize]
    public void Setup()
    {
      TestAppServicesHelper.EnsureInitialized();
      _mockTextSpeechEditorClient = new Mock<ITextSpeechEditorClient>();
      _mockProjectsClient = new Mock<IProjectsClient>();
      _mockProfilesClient = new Mock<IProfilesClient>();
      _mockAudioPlayer = new Mock<IAudioPlayerService>();
      _dispatcherController = DispatcherQueueController.CreateOnDedicatedThread();
      var dispatcher = _dispatcherController.DispatcherQueue;
      _context = new ViewModelContext(NullLogger.Instance, dispatcher);

      _mockTextSpeechEditorClient
        .Setup(x => x.GetSessionsAsync(It.IsAny<CancellationToken>()))
        .ReturnsAsync(Array.Empty<VoiceStudio.App.Services.EditorSession>());
      _mockProjectsClient
        .Setup(x => x.GetProjectsAsync(It.IsAny<CancellationToken>()))
        .ReturnsAsync(new List<Project>());
      _mockProfilesClient
        .Setup(x => x.GetProfilesAsync(It.IsAny<CancellationToken>()))
        .ReturnsAsync(new List<VoiceProfile>());
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
      _ = new TextSpeechEditorViewModel(
        _context,
        _mockTextSpeechEditorClient.Object,
        _mockProjectsClient.Object,
        _mockProfilesClient.Object,
        _mockAudioPlayer.Object);

      _mockTextSpeechEditorClient.Verify(x => x.GetSessionsAsync(It.IsAny<CancellationToken>()), Times.Never);
      _mockTextSpeechEditorClient.Verify(x => x.GetEnginesAsync(It.IsAny<CancellationToken>()), Times.Never);
      _mockProjectsClient.Verify(x => x.GetProjectsAsync(It.IsAny<CancellationToken>()), Times.Never);
      _mockProfilesClient.Verify(x => x.GetProfilesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [TestMethod]
    public void Constructor_WithClients_CreatesInstance()
    {
      var vm = new TextSpeechEditorViewModel(
        _context,
        _mockTextSpeechEditorClient.Object,
        _mockProjectsClient.Object,
        _mockProfilesClient.Object,
        _mockAudioPlayer.Object);

      Assert.IsNotNull(vm);
      Assert.AreEqual(PanelIds.TextSpeechEditor, vm.PanelId);
      Assert.IsNotNull(vm.LoadSessionsCommand);
      Assert.IsNotNull(vm.CreateSessionCommand);
    }

    [TestMethod]
    [ExpectedException(typeof(ArgumentNullException))]
    public void Constructor_WithNullTextSpeechEditorClient_Throws()
    {
      _ = new TextSpeechEditorViewModel(
        _context,
        null!,
        _mockProjectsClient.Object,
        _mockProfilesClient.Object,
        _mockAudioPlayer.Object);
    }
  }
}
