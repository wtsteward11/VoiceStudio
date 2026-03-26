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
  /// Seam-aware tests for EmbeddingExplorerViewModel.
  /// Instantiates ViewModel with mocked IEmbeddingExplorerClient, IProjectsClient, IProjectAudioClient, IProfilesClient.
  /// Supports "EmbeddingExplorerViewModel migrated to IEmbeddingExplorerClient" claims.
  /// See docs/governance/TEST_CLASSIFICATION.md.
  /// </summary>
  [TestClass]
  [TestCategory("SeamAware")]
  public class EmbeddingExplorerViewModelSeamTests
  {
    private Mock<IEmbeddingExplorerClient> _mockEmbeddingClient = null!;
    private Mock<IProjectsClient> _mockProjectsClient = null!;
    private Mock<IProjectAudioClient> _mockProjectAudioClient = null!;
    private Mock<IProfilesClient> _mockProfilesClient = null!;
    private IViewModelContext _context = null!;
    private DispatcherQueueController? _dispatcherController;

    [TestInitialize]
    public void Setup()
    {
      TestAppServicesHelper.EnsureInitialized();
      _mockEmbeddingClient = new Mock<IEmbeddingExplorerClient>();
      _mockProjectsClient = new Mock<IProjectsClient>();
      _mockProjectAudioClient = new Mock<IProjectAudioClient>();
      _mockProfilesClient = new Mock<IProfilesClient>();
      _dispatcherController = DispatcherQueueController.CreateOnDedicatedThread();
      var dispatcher = _dispatcherController.DispatcherQueue;
      _context = new ViewModelContext(NullLogger.Instance, dispatcher);

      _mockProjectsClient.Setup(x => x.GetProjectsAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new List<Project>());
      _mockProjectAudioClient.Setup(x => x.ListProjectAudioAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(new List<ProjectAudioFile>());
      _mockProfilesClient.Setup(x => x.GetProfilesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new List<VoiceProfile>());
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
      _ = new EmbeddingExplorerViewModel(_context, _mockEmbeddingClient.Object, _mockProjectsClient.Object, _mockProjectAudioClient.Object, _mockProfilesClient.Object);
      _mockEmbeddingClient.Verify(x => x.GetEmbeddingsAsync(It.IsAny<CancellationToken>()), Times.Never);
      _mockProjectsClient.Verify(x => x.GetProjectsAsync(It.IsAny<CancellationToken>()), Times.Never);
      _mockProjectAudioClient.Verify(x => x.ListProjectAudioAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
      _mockProfilesClient.Verify(x => x.GetProfilesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [TestMethod]
    public void Constructor_WithClients_CreatesInstance()
    {
      var vm = new EmbeddingExplorerViewModel(_context, _mockEmbeddingClient.Object, _mockProjectsClient.Object, _mockProjectAudioClient.Object, _mockProfilesClient.Object);
      Assert.IsNotNull(vm);
      Assert.AreEqual(PanelIds.EmbeddingExplorer, vm.PanelId);
    }

    [TestMethod]
    [ExpectedException(typeof(ArgumentNullException))]
    public void Constructor_WithNullEmbeddingClient_Throws()
    {
      _ = new EmbeddingExplorerViewModel(_context, null!, _mockProjectsClient.Object, _mockProjectAudioClient.Object, _mockProfilesClient.Object);
    }
  }
}
