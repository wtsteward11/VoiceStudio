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
using VoiceStudio.App.Views.Panels;
using VoiceStudio.Core.Models;
using VoiceStudio.Core.Panels;
using VoiceStudio.Core.Services;

namespace VoiceStudio.App.Tests.ViewModels
{
  /// <summary>
  /// Seam-aware tests for ABTestingViewModel.
  /// Instantiates ABTestingViewModel with mocked IABTestService, IProfilesClient, IAudioPlayerService.
  /// See docs/governance/TEST_CLASSIFICATION.md; Pass 08 W8-C2 §8.8.
  /// </summary>
  [TestClass]
  [TestCategory("SeamAware")]
  public class ABTestingViewModelSeamTests
  {
    private Mock<IABTestService> _mockAbTestService = null!;
    private Mock<IProfilesClient> _mockProfilesClient = null!;
    private Mock<IAudioPlayerService> _mockAudioPlayer = null!;
    private IViewModelContext _context = null!;
    private DispatcherQueueController? _dispatcherController;

    [TestInitialize]
    public void Setup()
    {
      _mockAbTestService = new Mock<IABTestService>();
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

    /// <summary>
    /// Invariant: constructor must not call any client methods before activation (ADR-047).
    /// </summary>
    [TestMethod]
    public void Constructor_DoesNotCallClient_BeforeActivation()
    {
      _ = new ABTestingViewModel(
          _context,
          _mockAbTestService.Object,
          _mockProfilesClient.Object,
          _mockAudioPlayer.Object);
      _mockAbTestService.Verify(
          x => x.RunABTestAsync(It.IsAny<ABTestRequest>(), It.IsAny<CancellationToken>()),
          Times.Never);
      _mockProfilesClient.Verify(x => x.GetProfilesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [TestMethod]
    public void Constructor_WithSeamClients_CreatesInstance()
    {
      var vm = new ABTestingViewModel(
          _context,
          _mockAbTestService.Object,
          _mockProfilesClient.Object,
          _mockAudioPlayer.Object);
      Assert.IsNotNull(vm);
      Assert.AreEqual(PanelIds.ABTesting, vm.PanelId);
      Assert.IsNotNull(vm.RunTestCommand);
      Assert.IsNotNull(vm.PlaySampleACommand);
      Assert.IsNotNull(vm.PlaySampleBCommand);
    }

    [TestMethod]
    [ExpectedException(typeof(ArgumentNullException))]
    public void Constructor_WithNullABTestService_Throws()
    {
      _ = new ABTestingViewModel(
          _context,
          null!,
          _mockProfilesClient.Object,
          _mockAudioPlayer.Object);
    }

    [TestMethod]
    [ExpectedException(typeof(ArgumentNullException))]
    public void Constructor_WithNullProfilesClient_Throws()
    {
      _ = new ABTestingViewModel(
          _context,
          _mockAbTestService.Object,
          null!,
          _mockAudioPlayer.Object);
    }

    [TestMethod]
    [ExpectedException(typeof(ArgumentNullException))]
    public void Constructor_WithNullAudioPlayer_Throws()
    {
      _ = new ABTestingViewModel(
          _context,
          _mockAbTestService.Object,
          _mockProfilesClient.Object,
          null!);
    }

    [TestMethod]
    public async Task InitializeAsync_CallsIProfilesClient_GetProfilesAsync()
    {
      var vm = new ABTestingViewModel(
          _context,
          _mockAbTestService.Object,
          _mockProfilesClient.Object,
          _mockAudioPlayer.Object);
      await vm.InitializeAsync(CancellationToken.None);
      _mockProfilesClient.Verify(
          x => x.GetProfilesAsync(It.IsAny<CancellationToken>()),
          Times.Once);
    }

    [TestMethod]
    public async Task RunTestAsync_PopulatesTestResults_AndSetsStatusMessage_OnSuccess()
    {
      var profile = new VoiceProfile { Id = "p1", Name = "One" };
      _mockProfilesClient
          .Setup(x => x.GetProfilesAsync(It.IsAny<CancellationToken>()))
          .ReturnsAsync(new List<VoiceProfile> { profile });

      var response = new ABTestResponse
      {
        TestId = "t1",
        SampleA = new ABTestResult { SampleLabel = "A", AudioId = "a1" },
        SampleB = new ABTestResult { SampleLabel = "B", AudioId = "b1" },
      };
      _mockAbTestService
          .Setup(x => x.RunABTestAsync(It.IsAny<ABTestRequest>(), It.IsAny<CancellationToken>()))
          .ReturnsAsync(response);

      var vm = new ABTestingViewModel(
          _context,
          _mockAbTestService.Object,
          _mockProfilesClient.Object,
          _mockAudioPlayer.Object);
      await vm.InitializeAsync(CancellationToken.None);
      vm.SelectedProfile = profile;

      await vm.RunTestCommand.ExecuteAsync(null);

      Assert.IsNotNull(vm.TestResults);
      Assert.AreEqual("t1", vm.TestResults!.TestId);
      Assert.IsFalse(string.IsNullOrWhiteSpace(vm.StatusMessage));
      _mockAbTestService.Verify(
          x => x.RunABTestAsync(It.IsAny<ABTestRequest>(), It.IsAny<CancellationToken>()),
          Times.Once);
    }

    [TestMethod]
    public void RunTestCommandContract_AlignsWithExplicitRunAffordance()
    {
      var profile = new VoiceProfile { Id = "p1", Name = "One" };
      _mockProfilesClient
          .Setup(x => x.GetProfilesAsync(It.IsAny<CancellationToken>()))
          .ReturnsAsync(new List<VoiceProfile> { profile });

      var vm = new ABTestingViewModel(
          _context,
          _mockAbTestService.Object,
          _mockProfilesClient.Object,
          _mockAudioPlayer.Object);

      Assert.IsFalse(vm.RunTestCommand.CanExecute(null));

      vm.SelectedProfile = profile;
      Assert.IsTrue(vm.RunTestCommand.CanExecute(null));

      vm.TestText = "   ";
      Assert.IsFalse(vm.RunTestCommand.CanExecute(null));
    }
  }
}
