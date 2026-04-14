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
  /// Seam-aware tests for VoiceMorphViewModel.
  /// Instantiates ViewModel with mocked IVoiceMorphClient, IProjectAudioClient, IProjectsClient, IProfilesClient.
  /// Supports "VoiceMorphViewModel migrated to IVoiceMorphClient" claims.
  /// See docs/governance/TEST_CLASSIFICATION.md.
  /// </summary>
  [TestClass]
  [TestCategory("SeamAware")]
  public class VoiceMorphViewModelSeamTests
  {
    private Mock<IVoiceMorphClient> _mockVoiceMorphClient = null!;
    private Mock<IProjectAudioClient> _mockProjectAudioClient = null!;
    private Mock<IProjectsClient> _mockProjectsClient = null!;
    private Mock<IProfilesClient> _mockProfilesClient = null!;
    private IViewModelContext _context = null!;
    private DispatcherQueueController? _dispatcherController;

    [TestInitialize]
    public void Setup()
    {
      TestAppServicesHelper.EnsureInitialized();
      _mockVoiceMorphClient = new Mock<IVoiceMorphClient>();
      _mockProjectAudioClient = new Mock<IProjectAudioClient>();
      _mockProjectsClient = new Mock<IProjectsClient>();
      _mockProfilesClient = new Mock<IProfilesClient>();
      _dispatcherController = DispatcherQueueController.CreateOnDedicatedThread();
      var dispatcher = _dispatcherController.DispatcherQueue;
      _context = new ViewModelContext(NullLogger.Instance, dispatcher);

      _mockVoiceMorphClient
        .Setup(x => x.GetConfigsAsync(It.IsAny<CancellationToken>()))
        .ReturnsAsync((VoiceMorphConfig[]?)null);
      _mockProjectsClient
        .Setup(x => x.GetProjectsAsync(It.IsAny<CancellationToken>()))
        .ReturnsAsync(new List<Project>());
      _mockProjectAudioClient
        .Setup(x => x.ListProjectAudioAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
        .ReturnsAsync(new List<ProjectAudioFile>());
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
      _ = new VoiceMorphViewModel(_context, _mockVoiceMorphClient.Object, _mockProjectAudioClient.Object, _mockProjectsClient.Object, _mockProfilesClient.Object);
      _mockVoiceMorphClient.Verify(x => x.GetConfigsAsync(It.IsAny<CancellationToken>()), Times.Never);
      _mockProjectsClient.Verify(x => x.GetProjectsAsync(It.IsAny<CancellationToken>()), Times.Never);
      _mockProjectAudioClient.Verify(x => x.ListProjectAudioAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
      _mockProfilesClient.Verify(x => x.GetProfilesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [TestMethod]
    public void Constructor_WithClients_CreatesInstance()
    {
      var vm = new VoiceMorphViewModel(_context, _mockVoiceMorphClient.Object, _mockProjectAudioClient.Object, _mockProjectsClient.Object, _mockProfilesClient.Object);
      Assert.IsNotNull(vm);
      Assert.AreEqual(PanelIds.VoiceMorph, vm.PanelId);
      Assert.IsNotNull(vm.LoadConfigsCommand);
      Assert.IsNotNull(vm.CreateConfigCommand);
      Assert.IsNotNull(vm.ApplyMorphCommand);
      Assert.IsNotNull(vm.RefreshCommand);
    }

    [TestMethod]
    [ExpectedException(typeof(ArgumentNullException))]
    public void Constructor_WithNullVoiceMorphClient_Throws()
    {
      _ = new VoiceMorphViewModel(_context, null!, _mockProjectAudioClient.Object, _mockProjectsClient.Object, _mockProfilesClient.Object);
    }

    [TestMethod]
    [ExpectedException(typeof(ArgumentNullException))]
    public void Constructor_WithNullProjectAudioClient_Throws()
    {
      _ = new VoiceMorphViewModel(_context, _mockVoiceMorphClient.Object, null!, _mockProjectsClient.Object, _mockProfilesClient.Object);
    }

    [TestMethod]
    public void ViewModel_ImplementsIPanelLifecycle()
    {
      var vm = new VoiceMorphViewModel(_context, _mockVoiceMorphClient.Object, _mockProjectAudioClient.Object, _mockProjectsClient.Object, _mockProfilesClient.Object);
      Assert.IsTrue(vm is IPanelLifecycle);
    }

    [TestMethod]
    public async Task OnActivatedAsync_LoadsConfigsAndAudioAndProfiles()
    {
      var vm = new VoiceMorphViewModel(_context, _mockVoiceMorphClient.Object, _mockProjectAudioClient.Object, _mockProjectsClient.Object, _mockProfilesClient.Object);
      await vm.OnActivatedAsync(CancellationToken.None);
      _mockVoiceMorphClient.Verify(x => x.GetConfigsAsync(It.IsAny<CancellationToken>()), Times.Once);
      _mockProjectsClient.Verify(x => x.GetProjectsAsync(It.IsAny<CancellationToken>()), Times.Once);
      _mockProfilesClient.Verify(x => x.GetProfilesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }
  }
}
