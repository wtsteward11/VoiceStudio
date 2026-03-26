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
using VoiceStudio.Core.Panels;
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

    /// <summary>
    /// Invariant: constructor must not call any client methods before activation.
    /// Prevents constructor fire-and-forget regression (RETAINED_ASYNC_RULE, ADR-047).
    /// </summary>
    [TestMethod]
    public void Constructor_DoesNotCallClient_BeforeActivation()
    {
      _ = new ProfileComparisonViewModel(
          _context,
          _mockSynthesisService.Object,
          _mockProfilesClient.Object,
          _mockAudioPlayer.Object);
      _mockProfilesClient.Verify(x => x.GetProfilesAsync(It.IsAny<CancellationToken>()), Times.Never);
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
      Assert.AreEqual(PanelIds.ProfileComparison, vm.PanelId);
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

    [TestMethod]
    public async Task CompareProfilesAsync_UsesFrozenEngineId_OnBothSynthesisRequests()
    {
      var profileA = new VoiceProfile { Id = "profile-a", Name = "A", Language = "en" };
      var profileB = new VoiceProfile { Id = "profile-b", Name = "B", Language = "en" };
      _mockProfilesClient
          .Setup(x => x.GetProfilesAsync(It.IsAny<CancellationToken>()))
          .ReturnsAsync(new List<VoiceProfile> { profileA, profileB });

      var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
      var captured = new List<VoiceSynthesisRequest>();
      var response = new VoiceSynthesisResponse
      {
        AudioUrl = "https://example/audio.wav",
        QualityScore = 4.0,
        QualityMetrics = new QualityMetrics()
      };
      _mockSynthesisService
          .Setup(x => x.SynthesizeVoiceAsync(It.IsAny<VoiceSynthesisRequest>(), It.IsAny<CancellationToken>()))
          .Returns<VoiceSynthesisRequest, CancellationToken>((r, _) =>
          {
            captured.Add(r);
            if (captured.Count >= 2)
              tcs.TrySetResult();
            return Task.FromResult(response);
          });

      var vm = new ProfileComparisonViewModel(
          _context,
          _mockSynthesisService.Object,
          _mockProfilesClient.Object,
          _mockAudioPlayer.Object);
      await vm.InitializeAsync(CancellationToken.None);
      vm.ComparisonEngineId = "chatterbox";
      vm.PreviewText = "Side by side line.";
      vm.SelectedProfileA = profileA;
      vm.SelectedProfileB = profileB;

      var completed = await Task.WhenAny(tcs.Task, Task.Delay(TimeSpan.FromSeconds(10)));
      Assert.IsTrue(ReferenceEquals(completed, tcs.Task), "Timed out waiting for dual synthesis (selection auto-compare).");

      Assert.AreEqual(2, captured.Count);
      Assert.AreEqual("chatterbox", captured[0].Engine);
      Assert.AreEqual("chatterbox", captured[1].Engine);
    }

    [TestMethod]
    public async Task ComparisonData_Populated_AfterSuccessfulDualSynthesis()
    {
      var profileA = new VoiceProfile { Id = "id-a", Name = "Alpha", Language = "en" };
      var profileB = new VoiceProfile { Id = "id-b", Name = "Beta", Language = "en" };
      _mockProfilesClient
          .Setup(x => x.GetProfilesAsync(It.IsAny<CancellationToken>()))
          .ReturnsAsync(new List<VoiceProfile> { profileA, profileB });

      var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
      var completedCount = 0;
      _mockSynthesisService
          .Setup(x => x.SynthesizeVoiceAsync(It.IsAny<VoiceSynthesisRequest>(), It.IsAny<CancellationToken>()))
          .Returns<VoiceSynthesisRequest, CancellationToken>((r, _) =>
          {
            VoiceSynthesisResponse resp;
            if (r.ProfileId == "id-a")
            {
              resp = new VoiceSynthesisResponse
              {
                AudioUrl = "https://a.wav",
                QualityScore = 4.2,
                QualityMetrics = new QualityMetrics { MosScore = 4.1 }
              };
            }
            else
            {
              resp = new VoiceSynthesisResponse
              {
                AudioUrl = "https://b.wav",
                QualityScore = 3.8,
                QualityMetrics = new QualityMetrics { MosScore = 3.9 }
              };
            }

            if (Interlocked.Increment(ref completedCount) == 2)
              tcs.TrySetResult();
            return Task.FromResult(resp);
          });

      var vm = new ProfileComparisonViewModel(
          _context,
          _mockSynthesisService.Object,
          _mockProfilesClient.Object,
          _mockAudioPlayer.Object);
      await vm.InitializeAsync(CancellationToken.None);
      vm.PreviewText = "Check metrics.";
      vm.SelectedProfileA = profileA;
      vm.SelectedProfileB = profileB;

      var completed = await Task.WhenAny(tcs.Task, Task.Delay(TimeSpan.FromSeconds(10)));
      Assert.IsTrue(ReferenceEquals(completed, tcs.Task), "Timed out waiting for dual synthesis (selection auto-compare).");

      Assert.IsNotNull(vm.ComparisonData);
      Assert.AreSame(profileA, vm.ComparisonData.ProfileA);
      Assert.AreSame(profileB, vm.ComparisonData.ProfileB);
      Assert.AreEqual("https://a.wav", vm.ComparisonData.AudioUrlA);
      Assert.AreEqual("https://b.wav", vm.ComparisonData.AudioUrlB);
      Assert.AreEqual(4.2, vm.ComparisonData.QualityScoreA, 0.001);
      Assert.AreEqual(3.8, vm.ComparisonData.QualityScoreB, 0.001);
    }

    [TestMethod]
    public void CompareCommandContract_AlignsWithExplicitCompareAffordance()
    {
      var profileA = new VoiceProfile { Id = "a", Name = "A" };
      var profileB = new VoiceProfile { Id = "b", Name = "B" };
      var vm = new ProfileComparisonViewModel(
          _context,
          _mockSynthesisService.Object,
          _mockProfilesClient.Object,
          _mockAudioPlayer.Object);
      vm.SelectedProfileA = profileA;
      vm.SelectedProfileB = profileB;
      vm.PreviewText = "   ";
      Assert.IsFalse(vm.CompareProfilesCommand.CanExecute(null));
      vm.PreviewText = "Valid preview";
      Assert.IsTrue(vm.CompareProfilesCommand.CanExecute(null));
    }
  }
}
