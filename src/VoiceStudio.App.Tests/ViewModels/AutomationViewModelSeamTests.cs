using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.UI.Dispatching;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using VoiceStudio.App.Services;
using VoiceStudio.App.Tests.Fixtures;
using VoiceStudio.App.ViewModels;
using VoiceStudio.Core.Panels;
using VoiceStudio.Core.Services;

namespace VoiceStudio.App.Tests.ViewModels
{
  /// <summary>
  /// Seam-aware tests for AutomationViewModel.
  /// Instantiates ViewModel with mocked IAutomationClient.
  /// Supports "AutomationViewModel migrated to IAutomationClient" claims.
  /// </summary>
  [TestClass]
  [TestCategory("SeamAware")]
  public class AutomationViewModelSeamTests
  {
    private Mock<IAutomationClient> _mockClient = null!;
    private IViewModelContext _context = null!;
    private DispatcherQueueController? _dispatcherController;

    [TestInitialize]
    public void Setup()
    {
      TestAppServicesHelper.EnsureInitialized();
      _mockClient = new Mock<IAutomationClient>();
      _dispatcherController = DispatcherQueueController.CreateOnDedicatedThread();
      var dispatcher = _dispatcherController.DispatcherQueue;
      _context = new ViewModelContext(NullLogger.Instance, dispatcher);

      _mockClient
          .Setup(x => x.GetTracksAsync(It.IsAny<CancellationToken>()))
          .ReturnsAsync(Array.Empty<AutomationTrackInfo>());
      _mockClient
          .Setup(x => x.GetCurvesAsync(It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
          .ReturnsAsync(Array.Empty<AutomationCurve>());
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
      _ = new AutomationViewModel(_context, _mockClient.Object);
      _mockClient.Verify(x => x.GetTracksAsync(It.IsAny<CancellationToken>()), Times.Never);
      _mockClient.Verify(
          x => x.GetCurvesAsync(It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()),
          Times.Never);
    }

    [TestMethod]
    public void Constructor_WithIAutomationClient_CreatesInstance()
    {
      var vm = new AutomationViewModel(_context, _mockClient.Object);
      Assert.IsNotNull(vm);
      Assert.AreEqual(PanelIds.Automation, vm.PanelId);
      Assert.IsNotNull(vm.LoadCurvesCommand);
      Assert.IsNotNull(vm.CreateCurveCommand);
      Assert.IsNotNull(vm.DeleteCurveCommand);
    }

    [TestMethod]
    [ExpectedException(typeof(ArgumentNullException))]
    public void Constructor_WithNullClient_Throws()
    {
      _ = new AutomationViewModel(_context, null!);
    }

    [TestMethod]
    public async Task LoadCurvesCommand_CallsIAutomationClient_GetCurvesAsync()
    {
      var vm = new AutomationViewModel(_context, _mockClient.Object);
      await vm.LoadCurvesCommand.ExecuteAsync(null);
      _mockClient.Verify(
          x => x.GetCurvesAsync(It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()),
          Times.AtLeastOnce);
    }
  }
}
