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
  /// Seam-aware tests for SonographyVisualizationViewModel.
  /// Instantiates ViewModel with mocked ISonographyClient, IProjectsClient, IProjectAudioClient.
  /// Supports "SonographyVisualizationViewModel migrated to ISonographyClient" claims.
  /// See docs/governance/TEST_CLASSIFICATION.md.
  /// </summary>
  [TestClass]
  [TestCategory("SeamAware")]
  public class SonographyVisualizationViewModelSeamTests
  {
    private Mock<ISonographyClient> _mockSonography = null!;
    private Mock<IProjectsClient> _mockProjects = null!;
    private Mock<IProjectAudioClient> _mockProjectAudio = null!;
    private IViewModelContext _context = null!;
    private DispatcherQueueController? _dispatcherController;

    [TestInitialize]
    public void Setup()
    {
      TestAppServicesHelper.EnsureInitialized();
      _mockSonography = new Mock<ISonographyClient>();
      _mockProjects = new Mock<IProjectsClient>();
      _mockProjectAudio = new Mock<IProjectAudioClient>();
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
      _ = new SonographyVisualizationViewModel(_context, _mockSonography.Object, _mockProjects.Object, _mockProjectAudio.Object);
      _mockSonography.Verify(x => x.GetPerspectivesAsync(It.IsAny<CancellationToken>()), Times.Never);
      _mockSonography.Verify(x => x.GetColorSchemesAsync(It.IsAny<CancellationToken>()), Times.Never);
      _mockProjects.Verify(x => x.GetProjectsAsync(It.IsAny<CancellationToken>()), Times.Never);
      _mockProjectAudio.Verify(x => x.ListProjectAudioAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [TestMethod]
    public void Constructor_WithClients_CreatesInstance()
    {
      var vm = new SonographyVisualizationViewModel(_context, _mockSonography.Object, _mockProjects.Object, _mockProjectAudio.Object);
      Assert.IsNotNull(vm);
      Assert.AreEqual(PanelIds.Sonography, vm.PanelId);
      Assert.IsNotNull(vm.GenerateSonographyCommand);
    }

    [TestMethod]
    [ExpectedException(typeof(ArgumentNullException))]
    public void Constructor_WithNullSonographyClient_Throws()
    {
      _ = new SonographyVisualizationViewModel(_context, null!, _mockProjects.Object, _mockProjectAudio.Object);
    }
  }
}
