using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.UI.Dispatching;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using VoiceStudio.App.Services;
using VoiceStudio.App.ViewModels;
using VoiceStudio.Core.Models;
using VoiceStudio.Core.Services;

namespace VoiceStudio.App.Tests.ViewModels
{
  /// <summary>
  /// Seam-aware tests for ProfileComparisonViewModel.
  /// Instantiates ProfileComparisonViewModel with mocked IVoiceSynthesisService and IProfilesClient.
  /// Supports "ProfileComparisonViewModel migrated to IVoiceSynthesisService + IProfilesClient" claims.
  /// See docs/governance/TEST_CLASSIFICATION.md.
  /// </summary>
  [TestClass]
  [TestCategory("SeamAware")]
  public class ProfileComparisonViewModelSeamTests
  {
    private Mock<IVoiceSynthesisService> _mockSynthesisService = null!;
    private Mock<IProfilesClient> _mockProfilesClient = null!;
    private Mock<IAudioPlayerService> _mockAudioPlayer = null!;
    private IViewModelContext _context = null!;
    private DispatcherQueueController? _dispatcherController;

    [TestInitialize]
    public void Setup()
    {
      _mockSynthesisService = new Mock<IVoiceSynthesisService>();
      _mockProfilesClient = new Mock<IProfilesClient>();
      _mockAudioPlayer = new Mock<IAudioPlayerService>();
      _dispatcherController = DispatcherQueueController.CreateOnDedicatedThread();
      var dispatcher = _dispatcherController.DispatcherQueue;
      _context = new ViewModelContext(NullLogger.Instance, dispatcher);

      _mockProfilesClient
          .Setup(x => x.GetProfilesAsync(It.IsAny<CancellationToken>()))
          .ReturnsAsync(new List<VoiceProfile>());
    }

    [TestCleanup]
    public void Cleanup()
    {
      _dispatcherController?.ShutdownQueueAsync().AsTask().GetAwaiter().GetResult();
    }

    [TestMethod]
    public void Constructor_WithSeamClients_CreatesInstance()
    {
      var vm = new ProfileComparisonViewModel(
          _context,
          _mockSynthesisService.Object,
          _mockProfilesClient.Object,
          _mockAudioPlayer.Object);
      Assert.IsNotNull(vm);
      Assert.AreEqual("profile-comparison", vm.PanelId);
      Assert.IsNotNull(vm.LoadProfilesCommand);
      Assert.IsNotNull(vm.CompareProfilesCommand);
    }

    [TestMethod]
    [ExpectedException(typeof(ArgumentNullException))]
    public void Constructor_WithNullVoiceSynthesisService_Throws()
    {
      _ = new ProfileComparisonViewModel(
          _context,
          null!,
          _mockProfilesClient.Object,
          _mockAudioPlayer.Object);
    }

    [TestMethod]
    [ExpectedException(typeof(ArgumentNullException))]
    public void Constructor_WithNullProfilesClient_Throws()
    {
      _ = new ProfileComparisonViewModel(
          _context,
          _mockSynthesisService.Object,
          null!,
          _mockAudioPlayer.Object);
    }

    [TestMethod]
    public async Task InitializeAsync_CallsIProfilesClient_GetProfilesAsync()
    {
      var vm = new ProfileComparisonViewModel(
          _context,
          _mockSynthesisService.Object,
          _mockProfilesClient.Object,
          _mockAudioPlayer.Object);
      await vm.InitializeAsync(CancellationToken.None);
      _mockProfilesClient.Verify(
          x => x.GetProfilesAsync(It.IsAny<CancellationToken>()),
          Times.Once);
    }
  }
}
