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
  /// Seam-aware tests for StyleTransferViewModel.
  /// Instantiates ViewModel with mocked IStyleTransferClient, IProjectAudioClient, IProjectsClient, IProfilesClient.
  /// Supports "StyleTransferViewModel migrated to IStyleTransferClient + IProjectAudioClient" claims.
  /// See docs/governance/TEST_CLASSIFICATION.md.
  /// </summary>
  [TestClass]
  [TestCategory("SeamAware")]
  public class StyleTransferViewModelSeamTests
  {
    private Mock<IStyleTransferClient> _mockStyleTransferClient = null!;
    private Mock<IProjectAudioClient> _mockProjectAudioClient = null!;
    private Mock<IProjectsClient> _mockProjectsClient = null!;
    private Mock<IProfilesClient> _mockProfilesClient = null!;
    private IViewModelContext _context = null!;
    private DispatcherQueueController? _dispatcherController;

    [TestInitialize]
    public void Setup()
    {
      TestAppServicesHelper.EnsureInitialized();
      _mockStyleTransferClient = new Mock<IStyleTransferClient>();
      _mockProjectAudioClient = new Mock<IProjectAudioClient>();
      _mockProjectsClient = new Mock<IProjectsClient>();
      _mockProfilesClient = new Mock<IProfilesClient>();
      _dispatcherController = DispatcherQueueController.CreateOnDedicatedThread();
      var dispatcher = _dispatcherController.DispatcherQueue;
      _context = new ViewModelContext(NullLogger.Instance, dispatcher);

      _mockStyleTransferClient
        .Setup(x => x.GetPresetsAsync(It.IsAny<CancellationToken>()))
        .ReturnsAsync(Array.Empty<StyleTransferPresetResponse>());
      _mockStyleTransferClient
        .Setup(x => x.GetJobsAsync(It.IsAny<CancellationToken>()))
        .ReturnsAsync(Array.Empty<StyleTransferJobResponse>());
      _mockProfilesClient
        .Setup(x => x.GetProfilesAsync(It.IsAny<CancellationToken>()))
        .ReturnsAsync(new List<VoiceProfile>());
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
      _ = new StyleTransferViewModel(
        _context,
        _mockStyleTransferClient.Object,
        _mockProjectAudioClient.Object,
        _mockProjectsClient.Object,
        _mockProfilesClient.Object);

      _mockStyleTransferClient.Verify(x => x.GetPresetsAsync(It.IsAny<CancellationToken>()), Times.Never);
      _mockStyleTransferClient.Verify(x => x.GetJobsAsync(It.IsAny<CancellationToken>()), Times.Never);
      _mockProjectAudioClient.Verify(x => x.ListProjectAudioAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
      _mockProjectsClient.Verify(x => x.GetProjectsAsync(It.IsAny<CancellationToken>()), Times.Never);
      _mockProfilesClient.Verify(x => x.GetProfilesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [TestMethod]
    public void Constructor_WithClients_CreatesInstance()
    {
      var vm = new StyleTransferViewModel(
        _context,
        _mockStyleTransferClient.Object,
        _mockProjectAudioClient.Object,
        _mockProjectsClient.Object,
        _mockProfilesClient.Object);

      Assert.IsNotNull(vm);
      Assert.AreEqual("style-transfer", vm.PanelId);
      Assert.IsNotNull(vm.LoadAudioFilesCommand);
      Assert.IsNotNull(vm.LoadVoiceProfilesCommand);
      Assert.IsNotNull(vm.LoadPresetsCommand);
      Assert.IsNotNull(vm.CreateTransferCommand);
      Assert.IsNotNull(vm.LoadJobsCommand);
      Assert.IsNotNull(vm.DeleteJobCommand);
    }

    [TestMethod]
    [ExpectedException(typeof(ArgumentNullException))]
    public void Constructor_WithNullStyleTransferClient_Throws()
    {
      _ = new StyleTransferViewModel(
        _context,
        null!,
        _mockProjectAudioClient.Object,
        _mockProjectsClient.Object,
        _mockProfilesClient.Object);
    }

    [TestMethod]
    [ExpectedException(typeof(ArgumentNullException))]
    public void Constructor_WithNullProjectAudioClient_Throws()
    {
      _ = new StyleTransferViewModel(
        _context,
        _mockStyleTransferClient.Object,
        null!,
        _mockProjectsClient.Object,
        _mockProfilesClient.Object);
    }
  }
}
