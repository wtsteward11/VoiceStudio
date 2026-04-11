using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.UI.Dispatching;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using VoiceStudio.App.Services;
using VoiceStudio.App.Tests.Fixtures;
using VoiceStudio.App.ViewModels;
using VoiceStudio.App.Views.Panels;
using VoiceStudio.Core.Models;
using VoiceStudio.Core.Panels;
using VoiceStudio.Core.Services;

namespace VoiceStudio.App.Tests.ViewModels
{
  /// <summary>
  /// Seam-aware tests for QualityBenchmarkViewModel.
  /// Instantiates ViewModel with mocked IQualityControlClient, IProfilesClient.
  /// Supports "QualityBenchmarkViewModel migrated to IQualityControlClient" claims.
  /// See docs/governance/TEST_CLASSIFICATION.md.
  /// </summary>
  [TestClass]
  [TestCategory("SeamAware")]
  public class QualityBenchmarkViewModelSeamTests
  {
    private Mock<IQualityControlClient> _mockQualityClient = null!;
    private Mock<IProfilesClient> _mockProfilesClient = null!;
    private Mock<IEnginesClient> _mockEnginesClient = null!;
    private Mock<IAudioPlayerService> _mockAudioPlayer = null!;
    private Mock<IVoiceSynthesisService> _mockSynthesis = null!;
    private BackendClientConfig _backendConfig = null!;
    private IViewModelContext _context = null!;
    private DispatcherQueueController? _dispatcherController;

    [TestInitialize]
    public void Setup()
    {
      TestAppServicesHelper.EnsureInitialized();
      _mockQualityClient = new Mock<IQualityControlClient>();
      _mockProfilesClient = new Mock<IProfilesClient>();
      _mockEnginesClient = new Mock<IEnginesClient>();
      _mockAudioPlayer = new Mock<IAudioPlayerService>();
      _mockSynthesis = new Mock<IVoiceSynthesisService>();
      _backendConfig = new BackendClientConfig { BaseUrl = BackendClientConfig.DefaultHttpBaseUrl };
      _dispatcherController = DispatcherQueueController.CreateOnDedicatedThread();
      var dispatcher = _dispatcherController.DispatcherQueue;
      _context = new ViewModelContext(NullLogger.Instance, dispatcher);

      _mockProfilesClient.Setup(x => x.GetProfilesAsync(It.IsAny<CancellationToken>()))
        .ReturnsAsync(new List<VoiceProfile>());

      _mockEnginesClient.Setup(x => x.GetEnginesAsync(It.IsAny<CancellationToken>()))
        .ReturnsAsync(new List<string> { "xtts", "piper" });
    }

    [TestCleanup]
    public void Cleanup()
    {
      _dispatcherController?.ShutdownQueueAsync().AsTask().GetAwaiter().GetResult();
    }

    private QualityBenchmarkViewModel CreateVm()
    {
      return new QualityBenchmarkViewModel(
          _context,
          _mockQualityClient.Object,
          _mockProfilesClient.Object,
          _mockEnginesClient.Object,
          _mockAudioPlayer.Object,
          _mockSynthesis.Object,
          _backendConfig);
    }

    /// <summary>
    /// Invariant: constructor must not call any client methods before activation.
    /// Prevents constructor fire-and-forget regression (RETAINED_ASYNC_RULE, ADR-047).
    /// </summary>
    [TestMethod]
    public void Constructor_DoesNotCallClient_BeforeActivation()
    {
      _ = CreateVm();
      _mockQualityClient.Verify(x => x.RunBenchmarkAsync(It.IsAny<BenchmarkRequest>(), It.IsAny<CancellationToken>()), Times.Never);
      _mockProfilesClient.Verify(x => x.GetProfilesAsync(It.IsAny<CancellationToken>()), Times.Never);
      _mockEnginesClient.Verify(x => x.GetEnginesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [TestMethod]
    public void Constructor_WithClients_CreatesInstance()
    {
      var vm = CreateVm();
      Assert.IsNotNull(vm);
      Assert.AreEqual(PanelIds.QualityBenchmark, vm.PanelId);
      Assert.IsNotNull(vm.RunBenchmarkCommand);
      Assert.IsNotNull(vm.RunComparisonCommand);
    }

    [TestMethod]
    [ExpectedException(typeof(ArgumentNullException))]
    public void Constructor_WithNullQualityClient_Throws()
    {
      _ = new QualityBenchmarkViewModel(
          _context,
          null!,
          _mockProfilesClient.Object,
          _mockEnginesClient.Object,
          _mockAudioPlayer.Object,
          _mockSynthesis.Object,
          _backendConfig);
    }

    [TestMethod]
    [ExpectedException(typeof(ArgumentNullException))]
    public void Constructor_WithNullProfilesClient_Throws()
    {
      _ = new QualityBenchmarkViewModel(
          _context,
          _mockQualityClient.Object,
          null!,
          _mockEnginesClient.Object,
          _mockAudioPlayer.Object,
          _mockSynthesis.Object,
          _backendConfig);
    }

    [TestMethod]
    [ExpectedException(typeof(ArgumentNullException))]
    public void Constructor_WithNullEnginesClient_Throws()
    {
      _ = new QualityBenchmarkViewModel(
          _context,
          _mockQualityClient.Object,
          _mockProfilesClient.Object,
          null!,
          _mockAudioPlayer.Object,
          _mockSynthesis.Object,
          _backendConfig);
    }

    [TestMethod]
    [ExpectedException(typeof(ArgumentNullException))]
    public void Constructor_WithNullAudioPlayer_Throws()
    {
      _ = new QualityBenchmarkViewModel(
          _context,
          _mockQualityClient.Object,
          _mockProfilesClient.Object,
          _mockEnginesClient.Object,
          null!,
          _mockSynthesis.Object,
          _backendConfig);
    }

    [TestMethod]
    [ExpectedException(typeof(ArgumentNullException))]
    public void Constructor_WithNullSynthesisService_Throws()
    {
      _ = new QualityBenchmarkViewModel(
          _context,
          _mockQualityClient.Object,
          _mockProfilesClient.Object,
          _mockEnginesClient.Object,
          _mockAudioPlayer.Object,
          null!,
          _backendConfig);
    }

    [TestMethod]
    [ExpectedException(typeof(ArgumentNullException))]
    public void Constructor_WithNullBackendConfig_Throws()
    {
      _ = new QualityBenchmarkViewModel(
          _context,
          _mockQualityClient.Object,
          _mockProfilesClient.Object,
          _mockEnginesClient.Object,
          _mockAudioPlayer.Object,
          _mockSynthesis.Object,
          null!);
    }

    /// <summary>Product trust Pass 01 slice 4: Quality Benchmark surface discloses partial / not workflow-pass-closed.</summary>
    [TestMethod]
    public void SurfaceMaturityFootnote_DisclosesPartialAndWorkflowHonesty()
    {
      var vm = CreateVm();
      var text = vm.SurfaceMaturityFootnote;
      StringAssert.Contains(text, "partial", StringComparison.OrdinalIgnoreCase);
      StringAssert.Contains(text, "workflow", StringComparison.OrdinalIgnoreCase);
    }

    [TestMethod]
    public async Task InitializeAsync_LoadsAvailableEngines_FromEnginesClient()
    {
      _mockEnginesClient
          .Setup(x => x.GetEnginesAsync(It.IsAny<CancellationToken>()))
          .ReturnsAsync(new List<string> { "xtts", "piper" });

      var vm = CreateVm();
      await vm.InitializeAsync(CancellationToken.None);

      Assert.AreEqual(2, vm.ComparisonEngineOptions.Count);
      Assert.AreEqual("piper", vm.ComparisonEngineOptions[0].EngineId);
      Assert.AreEqual("xtts", vm.ComparisonEngineOptions[1].EngineId);
    }

    [TestMethod]
    public async Task RunComparison_AllEnginesSucceed_PopulatesComparisonSlots()
    {
      var profile = new VoiceProfile { Id = "p1", Name = "Profile One", Language = "en" };
      _mockProfilesClient
          .Setup(x => x.GetProfilesAsync(It.IsAny<CancellationToken>()))
          .ReturnsAsync(new List<VoiceProfile> { profile });

      _mockSynthesis
          .Setup(x => x.SynthesizeVoiceAsync(It.IsAny<VoiceSynthesisRequest>(), It.IsAny<CancellationToken>()))
          .ReturnsAsync((VoiceSynthesisRequest req, CancellationToken _) =>
              new VoiceSynthesisResponse
              {
                AudioId = $"audio-{req.Engine}",
                QualityMetrics = new QualityMetrics { MosScore = 4.2, Similarity = 0.91 },
              });

      var vm = CreateVm();
      await vm.InitializeAsync(CancellationToken.None);
      vm.SelectedProfile = profile;
      vm.TestText = "Hello benchmark.";

      await vm.RunComparisonCommand.ExecuteAsync(null);

      Assert.AreEqual(2, vm.ComparisonSlots.Count);
      Assert.IsTrue(vm.ComparisonSlots[0].IsSuccess);
      Assert.IsTrue(vm.ComparisonSlots[1].IsSuccess);
      Assert.AreEqual("audio-piper", vm.ComparisonSlots[0].AudioId);
      Assert.AreEqual("audio-xtts", vm.ComparisonSlots[1].AudioId);
    }

    [TestMethod]
    public async Task RunComparison_OneEngineFails_OtherSlotStillPresent()
    {
      var profile = new VoiceProfile { Id = "p1", Name = "Profile One", Language = "en" };
      _mockProfilesClient
          .Setup(x => x.GetProfilesAsync(It.IsAny<CancellationToken>()))
          .ReturnsAsync(new List<VoiceProfile> { profile });

      _mockSynthesis
          .Setup(x => x.SynthesizeVoiceAsync(It.IsAny<VoiceSynthesisRequest>(), It.IsAny<CancellationToken>()))
          .ReturnsAsync((VoiceSynthesisRequest req, CancellationToken _) =>
          {
            if (string.Equals(req.Engine, "xtts", StringComparison.OrdinalIgnoreCase))
            {
              throw new InvalidOperationException("synth failed");
            }

            return new VoiceSynthesisResponse
            {
              AudioId = "ok-audio",
              QualityMetrics = new QualityMetrics { MosScore = 3.0, Similarity = 0.5 },
            };
          });

      var vm = CreateVm();
      await vm.InitializeAsync(CancellationToken.None);
      vm.SelectedProfile = profile;
      vm.TestText = "Hello benchmark.";

      await vm.RunComparisonCommand.ExecuteAsync(null);

      Assert.AreEqual(2, vm.ComparisonSlots.Count);
      var failed = vm.ComparisonSlots.First(s => s.EngineId == "xtts");
      var ok = vm.ComparisonSlots.First(s => s.EngineId == "piper");
      Assert.IsFalse(failed.IsSuccess);
      Assert.IsFalse(string.IsNullOrEmpty(failed.Error));
      Assert.IsTrue(ok.IsSuccess);
    }

    [TestMethod]
    public async Task PlaySlot_InvokesAudioPlayerService_WithAudioId()
    {
      var profile = new VoiceProfile { Id = "p1", Name = "Profile One", Language = "en" };
      _mockProfilesClient
          .Setup(x => x.GetProfilesAsync(It.IsAny<CancellationToken>()))
          .ReturnsAsync(new List<VoiceProfile> { profile });

      _mockSynthesis
          .Setup(x => x.SynthesizeVoiceAsync(It.IsAny<VoiceSynthesisRequest>(), It.IsAny<CancellationToken>()))
          .ReturnsAsync((VoiceSynthesisRequest req, CancellationToken _) =>
              new VoiceSynthesisResponse
              {
                AudioId = $"audio-{req.Engine}",
                QualityMetrics = new QualityMetrics(),
              });

      var vm = CreateVm();
      await vm.InitializeAsync(CancellationToken.None);
      vm.SelectedProfile = profile;
      vm.TestText = "Play test.";

      await vm.RunComparisonCommand.ExecuteAsync(null);

      var slot = vm.ComparisonSlots[0];
      await slot.PlaySlotCommand.ExecuteAsync(null);

      _mockAudioPlayer.Verify(
          x => x.PlayBackendAudioIdAsync(slot.AudioId!, BackendClientConfig.DefaultHttpBaseUrl, null),
          Times.Once);
    }

    [TestMethod]
    public async Task SetPreferred_MarksOneSlot_ClearsOthers()
    {
      var profile = new VoiceProfile { Id = "p1", Name = "Profile One", Language = "en" };
      _mockProfilesClient
          .Setup(x => x.GetProfilesAsync(It.IsAny<CancellationToken>()))
          .ReturnsAsync(new List<VoiceProfile> { profile });

      _mockSynthesis
          .Setup(x => x.SynthesizeVoiceAsync(It.IsAny<VoiceSynthesisRequest>(), It.IsAny<CancellationToken>()))
          .ReturnsAsync((VoiceSynthesisRequest req, CancellationToken _) =>
              new VoiceSynthesisResponse { AudioId = $"a-{req.Engine}", QualityMetrics = new QualityMetrics() });

      var vm = CreateVm();
      await vm.InitializeAsync(CancellationToken.None);
      vm.SelectedProfile = profile;
      await vm.RunComparisonCommand.ExecuteAsync(null);

      var first = vm.ComparisonSlots[0];
      var second = vm.ComparisonSlots[1];

      vm.SetPreferredEngineCommand.Execute(second);
      Assert.IsFalse(first.IsPreferred);
      Assert.IsTrue(second.IsPreferred);
      Assert.AreEqual(second.EngineId, vm.PreferredEngineId);

      vm.SetPreferredEngineCommand.Execute(first);
      Assert.IsTrue(first.IsPreferred);
      Assert.IsFalse(second.IsPreferred);
    }

    [TestMethod]
    public void SubjectiveScore_Update_Reflects_OnSlot()
    {
      var slot = new ComparisonSlot(_mockAudioPlayer.Object, () => BackendClientConfig.DefaultHttpBaseUrl, "e1");
      Assert.AreEqual(0d, slot.SubjectiveScore);
      slot.SubjectiveScore = 4;
      Assert.AreEqual(4d, slot.SubjectiveScore);
    }

    [TestMethod]
    public async Task RunBenchmarkAsync_UpdatesBenchmarkResults_WhenClientReturnsResults()
    {
      var profile = new VoiceProfile { Id = "p1", Name = "Profile One" };
      _mockProfilesClient
          .Setup(x => x.GetProfilesAsync(It.IsAny<CancellationToken>()))
          .ReturnsAsync(new List<VoiceProfile> { profile });

      var benchResult = new BenchmarkResult { Engine = "xtts", Success = true };
      var response = new BenchmarkResponse { Results = new List<BenchmarkResult> { benchResult } };
      _mockQualityClient
          .Setup(x => x.RunBenchmarkAsync(It.IsAny<BenchmarkRequest>(), It.IsAny<CancellationToken>()))
          .ReturnsAsync(response);

      var vm = CreateVm();
      await vm.InitializeAsync(CancellationToken.None);
      vm.SelectedProfile = profile;

      await vm.RunBenchmarkCommand.ExecuteAsync(null);

      Assert.IsTrue(vm.HasResults);
      Assert.AreEqual(1, vm.BenchmarkResults.Count);
      Assert.AreEqual("xtts", vm.BenchmarkResults[0].Engine);
      _mockQualityClient.Verify(
          x => x.RunBenchmarkAsync(It.IsAny<BenchmarkRequest>(), It.IsAny<CancellationToken>()),
          Times.Once);
    }

    [TestMethod]
    public async Task NextStepGuidance_AfterBenchmark_PresentAndNonEmpty()
    {
      var profile = new VoiceProfile { Id = "p1", Name = "Profile One" };
      _mockProfilesClient
          .Setup(x => x.GetProfilesAsync(It.IsAny<CancellationToken>()))
          .ReturnsAsync(new List<VoiceProfile> { profile });

      var response = new BenchmarkResponse
      {
        Results = new List<BenchmarkResult>
        {
          new BenchmarkResult { Engine = "xtts", Success = true }
        }
      };
      _mockQualityClient
          .Setup(x => x.RunBenchmarkAsync(It.IsAny<BenchmarkRequest>(), It.IsAny<CancellationToken>()))
          .ReturnsAsync(response);

      var vm = CreateVm();
      await vm.InitializeAsync(CancellationToken.None);
      vm.SelectedProfile = profile;

      await vm.RunBenchmarkCommand.ExecuteAsync(null);

      Assert.IsFalse(string.IsNullOrWhiteSpace(vm.NextStepHint));
    }

    /// <summary>
    /// Pins success toast contract: view listens for <see cref="QualityBenchmarkViewModel.StatusMessage"/> (inherited from BaseViewModel).
    /// </summary>
    [TestMethod]
    public async Task SuccessNotificationContract_UsesObservableVmProperty()
    {
      var profile = new VoiceProfile { Id = "p1", Name = "Profile One" };
      _mockProfilesClient
          .Setup(x => x.GetProfilesAsync(It.IsAny<CancellationToken>()))
          .ReturnsAsync(new List<VoiceProfile> { profile });

      var response = new BenchmarkResponse
      {
        Results = new List<BenchmarkResult>
        {
          new BenchmarkResult { Engine = "chatterbox", Success = true }
        }
      };
      _mockQualityClient
          .Setup(x => x.RunBenchmarkAsync(It.IsAny<BenchmarkRequest>(), It.IsAny<CancellationToken>()))
          .ReturnsAsync(response);

      var vm = CreateVm();
      await vm.InitializeAsync(CancellationToken.None);
      vm.SelectedProfile = profile;

      var raised = new List<string?>();
      vm.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

      await vm.RunBenchmarkCommand.ExecuteAsync(null);

      Assert.IsTrue(raised.Contains(nameof(QualityBenchmarkViewModel.StatusMessage)));
      Assert.IsFalse(string.IsNullOrEmpty(vm.StatusMessage));
    }
  }
}
