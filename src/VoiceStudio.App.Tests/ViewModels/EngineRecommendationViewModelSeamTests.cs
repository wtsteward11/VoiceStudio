using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.UI.Dispatching;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using System;
using System.Threading;
using VoiceStudio.App.Tests.Fixtures;
using VoiceStudio.App.ViewModels;
using VoiceStudio.App.Views.Panels;
using VoiceStudio.Core.Models;
using VoiceStudio.Core.Panels;
using VoiceStudio.Core.Services;

namespace VoiceStudio.App.Tests.ViewModels
{
  /// <summary>
  /// Seam-aware tests for EngineRecommendationViewModel.
  /// Instantiates ViewModel with mocked IQualityControlClient.
  /// Supports "EngineRecommendationViewModel migrated to IQualityControlClient" claims.
  /// See docs/governance/TEST_CLASSIFICATION.md.
  /// </summary>
  [TestClass]
  [TestCategory("SeamAware")]
  public class EngineRecommendationViewModelSeamTests
  {
    private Mock<IQualityControlClient> _mockQualityClient = null!;
    private IViewModelContext _context = null!;
    private DispatcherQueueController? _dispatcherController;

    [TestInitialize]
    public void Setup()
    {
      TestAppServicesHelper.EnsureInitialized();
      _mockQualityClient = new Mock<IQualityControlClient>();
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
      _ = new EngineRecommendationViewModel(_context, _mockQualityClient.Object);
      _mockQualityClient.Verify(x => x.GetEngineRecommendationAsync(It.IsAny<EngineRecommendationRequest>(), It.IsAny<CancellationToken>()), Times.Never);
      _mockQualityClient.Verify(x => x.GetQualityPresetsAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [TestMethod]
    public void Constructor_WithClients_CreatesInstance()
    {
      var vm = new EngineRecommendationViewModel(_context, _mockQualityClient.Object);
      Assert.IsNotNull(vm);
      Assert.AreEqual("engine_recommendation", vm.PanelId);
    }

    [TestMethod]
    [ExpectedException(typeof(ArgumentNullException))]
    public void Constructor_WithNullQualityClient_Throws()
    {
      _ = new EngineRecommendationViewModel(_context, null!);
    }
  }
}
