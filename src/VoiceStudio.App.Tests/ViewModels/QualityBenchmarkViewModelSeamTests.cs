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
    private IViewModelContext _context = null!;
    private DispatcherQueueController? _dispatcherController;

    [TestInitialize]
    public void Setup()
    {
      TestAppServicesHelper.EnsureInitialized();
      _mockQualityClient = new Mock<IQualityControlClient>();
      _mockProfilesClient = new Mock<IProfilesClient>();
      _dispatcherController = DispatcherQueueController.CreateOnDedicatedThread();
      var dispatcher = _dispatcherController.DispatcherQueue;
      _context = new ViewModelContext(NullLogger.Instance, dispatcher);

      _mockProfilesClient.Setup(x => x.GetProfilesAsync(It.IsAny<CancellationToken>()))
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
      _ = new QualityBenchmarkViewModel(_context, _mockQualityClient.Object, _mockProfilesClient.Object);
      _mockQualityClient.Verify(x => x.RunBenchmarkAsync(It.IsAny<BenchmarkRequest>(), It.IsAny<CancellationToken>()), Times.Never);
      _mockProfilesClient.Verify(x => x.GetProfilesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [TestMethod]
    public void Constructor_WithClients_CreatesInstance()
    {
      var vm = new QualityBenchmarkViewModel(_context, _mockQualityClient.Object, _mockProfilesClient.Object);
      Assert.IsNotNull(vm);
      Assert.AreEqual(PanelIds.QualityBenchmark, vm.PanelId);
      Assert.IsNotNull(vm.RunBenchmarkCommand);
    }

    [TestMethod]
    [ExpectedException(typeof(ArgumentNullException))]
    public void Constructor_WithNullQualityClient_Throws()
    {
      _ = new QualityBenchmarkViewModel(_context, null!, _mockProfilesClient.Object);
    }

    [TestMethod]
    [ExpectedException(typeof(ArgumentNullException))]
    public void Constructor_WithNullProfilesClient_Throws()
    {
      _ = new QualityBenchmarkViewModel(_context, _mockQualityClient.Object, null!);
    }

    /// <summary>Product trust Pass 01 slice 4: Quality Benchmark surface discloses partial / not workflow-pass-closed.</summary>
    [TestMethod]
    public void SurfaceMaturityFootnote_DisclosesPartialAndWorkflowHonesty()
    {
      var vm = new QualityBenchmarkViewModel(_context, _mockQualityClient.Object, _mockProfilesClient.Object);
      var text = vm.SurfaceMaturityFootnote;
      StringAssert.Contains(text, "partial", StringComparison.OrdinalIgnoreCase);
      StringAssert.Contains(text, "workflow", StringComparison.OrdinalIgnoreCase);
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

      var vm = new QualityBenchmarkViewModel(_context, _mockQualityClient.Object, _mockProfilesClient.Object);
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

      var vm = new QualityBenchmarkViewModel(_context, _mockQualityClient.Object, _mockProfilesClient.Object);
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

      var vm = new QualityBenchmarkViewModel(_context, _mockQualityClient.Object, _mockProfilesClient.Object);
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
