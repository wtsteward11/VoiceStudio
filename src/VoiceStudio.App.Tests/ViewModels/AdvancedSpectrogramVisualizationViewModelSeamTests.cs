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
  /// Seam-aware tests for AdvancedSpectrogramVisualizationViewModel.
  /// Instantiates ViewModel with mocked IAdvancedSpectrogramClient, IProjectAudioClient, IProjectsClient.
  /// Supports "AdvancedSpectrogramVisualizationViewModel migrated to IAdvancedSpectrogramClient" claims.
  /// See docs/governance/TEST_CLASSIFICATION.md.
  /// </summary>
  [TestClass]
  [TestCategory("SeamAware")]
  public class AdvancedSpectrogramVisualizationViewModelSeamTests
  {
    private Mock<IAdvancedSpectrogramClient> _mockSpectrogramClient = null!;
    private Mock<IProjectAudioClient> _mockProjectAudioClient = null!;
    private Mock<IProjectsClient> _mockProjectsClient = null!;
    private IViewModelContext _context = null!;
    private DispatcherQueueController? _dispatcherController;

    [TestInitialize]
    public void Setup()
    {
      TestAppServicesHelper.EnsureInitialized();
      _mockSpectrogramClient = new Mock<IAdvancedSpectrogramClient>();
      _mockProjectAudioClient = new Mock<IProjectAudioClient>();
      _mockProjectsClient = new Mock<IProjectsClient>();
      _dispatcherController = DispatcherQueueController.CreateOnDedicatedThread();
      var dispatcher = _dispatcherController.DispatcherQueue;
      _context = new ViewModelContext(NullLogger.Instance, dispatcher);

      _mockProjectsClient
        .Setup(x => x.GetProjectsAsync(It.IsAny<CancellationToken>()))
        .ReturnsAsync(new List<Project>());
      _mockProjectAudioClient
        .Setup(x => x.ListProjectAudioAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
        .ReturnsAsync(new List<ProjectAudioFile>());
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
      _ = new AdvancedSpectrogramVisualizationViewModel(
        _context,
        _mockSpectrogramClient.Object,
        _mockProjectAudioClient.Object,
        _mockProjectsClient.Object);

      _mockSpectrogramClient.Verify(x => x.GetViewTypesAsync(It.IsAny<CancellationToken>()), Times.Never);
      _mockSpectrogramClient.Verify(x => x.GenerateSpectrogramAsync(It.IsAny<AdvancedSpectrogramGenerateRequest>(), It.IsAny<CancellationToken>()), Times.Never);
      _mockSpectrogramClient.Verify(x => x.CompareSpectrogramsAsync(It.IsAny<AdvancedSpectrogramCompareRequest>(), It.IsAny<CancellationToken>()), Times.Never);
      _mockProjectAudioClient.Verify(x => x.ListProjectAudioAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
      _mockProjectsClient.Verify(x => x.GetProjectsAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [TestMethod]
    public void Constructor_WithClients_CreatesInstance()
    {
      var vm = new AdvancedSpectrogramVisualizationViewModel(
        _context,
        _mockSpectrogramClient.Object,
        _mockProjectAudioClient.Object,
        _mockProjectsClient.Object);

      Assert.IsNotNull(vm);
      Assert.AreEqual("advanced-spectrogram-visualization", vm.PanelId);
      Assert.IsNotNull(vm.LoadViewTypesCommand);
      Assert.IsNotNull(vm.GenerateSpectrogramCommand);
      Assert.IsNotNull(vm.CompareSpectrogramsCommand);
      Assert.IsNotNull(vm.LoadAudioFilesCommand);
      Assert.IsNotNull(vm.RefreshCommand);
    }

    [TestMethod]
    [ExpectedException(typeof(ArgumentNullException))]
    public void Constructor_WithNullSpectrogramClient_Throws()
    {
      _ = new AdvancedSpectrogramVisualizationViewModel(
        _context,
        null!,
        _mockProjectAudioClient.Object,
        _mockProjectsClient.Object);
    }

    [TestMethod]
    public void ViewModel_ImplementsIPanelLifecycle()
    {
      var vm = new AdvancedSpectrogramVisualizationViewModel(
        _context,
        _mockSpectrogramClient.Object,
        _mockProjectAudioClient.Object,
        _mockProjectsClient.Object);
      Assert.IsTrue(vm is IPanelLifecycle);
    }
  }
}
