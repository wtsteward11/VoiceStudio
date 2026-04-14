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
  /// Seam-aware tests for RealTimeVoiceConverterViewModel.
  /// Instantiates ViewModel with mocked IRealTimeVoiceConverterClient, IProfilesClient.
  /// Supports "RealTimeVoiceConverterViewModel migrated to IRealTimeVoiceConverterClient" claims.
  /// See docs/governance/TEST_CLASSIFICATION.md.
  /// </summary>
  [TestClass]
  [TestCategory("SeamAware")]
  public class RealTimeVoiceConverterViewModelSeamTests
  {
    private Mock<IRealTimeVoiceConverterClient> _mockRealtimeClient = null!;
    private Mock<IProfilesClient> _mockProfilesClient = null!;
    private IViewModelContext _context = null!;
    private DispatcherQueueController? _dispatcherController;

    [TestInitialize]
    public void Setup()
    {
      TestAppServicesHelper.EnsureInitialized();
      _mockRealtimeClient = new Mock<IRealTimeVoiceConverterClient>();
      _mockProfilesClient = new Mock<IProfilesClient>();
      _dispatcherController = DispatcherQueueController.CreateOnDedicatedThread();
      var dispatcher = _dispatcherController.DispatcherQueue;
      _context = new ViewModelContext(NullLogger.Instance, dispatcher);

      _mockRealtimeClient.Setup(x => x.GetSessionsAsync(It.IsAny<CancellationToken>()))
        .ReturnsAsync((ConverterSessionListResponse?)null);
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
      _ = new RealTimeVoiceConverterViewModel(_context, _mockRealtimeClient.Object, _mockProfilesClient.Object);
      _mockRealtimeClient.Verify(x => x.GetSessionsAsync(It.IsAny<CancellationToken>()), Times.Never);
      _mockProfilesClient.Verify(x => x.GetProfilesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [TestMethod]
    public void Constructor_WithClients_CreatesInstance()
    {
      var vm = new RealTimeVoiceConverterViewModel(_context, _mockRealtimeClient.Object, _mockProfilesClient.Object);
      Assert.IsNotNull(vm);
      Assert.AreEqual(PanelIds.RealTimeConverter, vm.PanelId);
      Assert.IsNotNull(vm.LoadSessionsCommand);
      Assert.IsNotNull(vm.RefreshCommand);
    }

    [TestMethod]
    [ExpectedException(typeof(ArgumentNullException))]
    public void Constructor_WithNullRealtimeClient_Throws()
    {
      _ = new RealTimeVoiceConverterViewModel(_context, null!, _mockProfilesClient.Object);
    }

    [TestMethod]
    [ExpectedException(typeof(ArgumentNullException))]
    public void Constructor_WithNullProfilesClient_Throws()
    {
      _ = new RealTimeVoiceConverterViewModel(_context, _mockRealtimeClient.Object, null!);
    }
  }
}
