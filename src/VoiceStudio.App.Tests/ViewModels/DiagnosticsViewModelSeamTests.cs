using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.UI.Dispatching;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using System;
using System.Threading;
using VoiceStudio.App.Services;
using VoiceStudio.App.Tests.Fixtures;
using VoiceStudio.App.ViewModels;
using VoiceStudio.App.Views.Panels;
using VoiceStudio.Core.Panels;
using VoiceStudio.Core.Services;

namespace VoiceStudio.App.Tests.ViewModels
{
  /// <summary>
  /// Seam-aware tests for DiagnosticsViewModel.
  /// Instantiates ViewModel with mocked IDiagnosticsClient.
  /// Supports "DiagnosticsViewModel migrated to IDiagnosticsClient" claims.
  /// See docs/governance/TEST_CLASSIFICATION.md.
  /// </summary>
  [TestClass]
  [TestCategory("SeamAware")]
  public class DiagnosticsViewModelSeamTests
  {
    private Mock<IDiagnosticsClient> _mockDiagnosticsClient = null!;
    private IViewModelContext _context = null!;
    private DispatcherQueueController? _dispatcherController;

    [TestInitialize]
    public void Setup()
    {
      TestAppServicesHelper.EnsureInitialized();
      _mockDiagnosticsClient = new Mock<IDiagnosticsClient>();
      _dispatcherController = DispatcherQueueController.CreateOnDedicatedThread();
      var dispatcher = _dispatcherController.DispatcherQueue;
      _context = new ViewModelContext(NullLogger.Instance, dispatcher);

      _mockDiagnosticsClient.Setup(x => x.CheckHealthAsync(It.IsAny<CancellationToken>())).ReturnsAsync(true);
      _mockDiagnosticsClient.Setup(x => x.GetTelemetryAsync(It.IsAny<CancellationToken>())).ReturnsAsync((VoiceStudio.Core.Models.Telemetry?)null);
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
      _ = new DiagnosticsViewModel(_context, _mockDiagnosticsClient.Object);
      _mockDiagnosticsClient.Verify(x => x.CheckHealthAsync(It.IsAny<CancellationToken>()), Times.Never);
      _mockDiagnosticsClient.Verify(x => x.GetTelemetryAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [TestMethod]
    public void Constructor_WithClient_CreatesInstance()
    {
      var vm = new DiagnosticsViewModel(_context, _mockDiagnosticsClient.Object);
      Assert.IsNotNull(vm);
      Assert.AreEqual(PanelIds.Diagnostics, vm.PanelId);
    }

    [TestMethod]
    [ExpectedException(typeof(ArgumentNullException))]
    public void Constructor_WithNullDiagnosticsClient_Throws()
    {
      _ = new DiagnosticsViewModel(_context, null!);
    }
  }
}
