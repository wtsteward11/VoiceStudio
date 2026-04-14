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
  /// Seam-aware tests for QualityOptimizationWizardViewModel.
  /// Instantiates ViewModel with mocked IVoiceSynthesisService, IQualityControlClient, IProfilesClient.
  /// Supports "QualityOptimizationWizardViewModel migrated" claims.
  /// See docs/governance/TEST_CLASSIFICATION.md.
  /// </summary>
  [TestClass]
  [TestCategory("SeamAware")]
  public class QualityOptimizationWizardViewModelSeamTests
  {
    private Mock<IVoiceSynthesisService> _mockVoiceSynthesisService = null!;
    private Mock<IQualityControlClient> _mockQualityClient = null!;
    private Mock<IProfilesClient> _mockProfilesClient = null!;
    private IViewModelContext _context = null!;
    private DispatcherQueueController? _dispatcherController;

    [TestInitialize]
    public void Setup()
    {
      TestAppServicesHelper.EnsureInitialized();
      _mockVoiceSynthesisService = new Mock<IVoiceSynthesisService>();
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
      DispatcherQueueTestHelpers.ShutdownSyncBounded(_dispatcherController);
    }

    /// <summary>
    /// Invariant: constructor must not call any client methods before activation.
    /// Prevents constructor fire-and-forget regression (RETAINED_ASYNC_RULE, ADR-047).
    /// </summary>
    [TestMethod]
    public void Constructor_DoesNotCallClient_BeforeActivation()
    {
      _ = new QualityOptimizationWizardViewModel(
        _context,
        _mockVoiceSynthesisService.Object,
        _mockQualityClient.Object,
        _mockProfilesClient.Object);

      _mockProfilesClient.Verify(x => x.GetProfilesAsync(It.IsAny<CancellationToken>()), Times.Never);
      _mockQualityClient.Verify(x => x.AnalyzeQualityAsync(It.IsAny<QualityAnalysisRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [TestMethod]
    public void Constructor_WithClients_CreatesInstance()
    {
      var vm = new QualityOptimizationWizardViewModel(
        _context,
        _mockVoiceSynthesisService.Object,
        _mockQualityClient.Object,
        _mockProfilesClient.Object);

      Assert.IsNotNull(vm);
      Assert.AreEqual(PanelIds.QualityOptimizer, vm.PanelId);
      Assert.IsNotNull(vm.LoadProfilesCommand);
      Assert.IsNotNull(vm.AnalyzeQualityCommand);
    }

    [TestMethod]
    [ExpectedException(typeof(ArgumentNullException))]
    public void Constructor_WithNullVoiceSynthesisService_Throws()
    {
      _ = new QualityOptimizationWizardViewModel(
        _context,
        null!,
        _mockQualityClient.Object,
        _mockProfilesClient.Object);
    }
  }
}
