using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.UI.Dispatching;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using System;
using System.Threading;
using System.Threading.Tasks;
using VoiceStudio.App.ViewModels;
using VoiceStudio.App.Tests.Fixtures;
using VoiceStudio.App.Services;
using VoiceStudio.Core.Panels;
using VoiceStudio.Core.Services;

namespace VoiceStudio.App.Tests.ViewModels
{
  /// <summary>
  /// Seam-aware tests for APIKeyManagerViewModel.
  /// Instantiates ViewModel with mocked IAPIKeyManagerClient.
  /// Supports "APIKeyManagerViewModel migrated to IAPIKeyManagerClient" claims.
  /// </summary>
  [TestClass]
  [TestCategory("SeamAware")]
  public class APIKeyManagerViewModelSeamTests
  {
    private Mock<IAPIKeyManagerClient> _mockClient = null!;
    private IViewModelContext _context = null!;
    private DispatcherQueueController? _dispatcherController;

    [TestInitialize]
    public void Setup()
    {
      TestAppServicesHelper.EnsureInitialized();
      _mockClient = new Mock<IAPIKeyManagerClient>();
      _dispatcherController = DispatcherQueueController.CreateOnDedicatedThread();
      var dispatcher = _dispatcherController.DispatcherQueue;
      _context = new ViewModelContext(NullLogger.Instance, dispatcher);

      _mockClient
          .Setup(x => x.GetKeysAsync(It.IsAny<CancellationToken>()))
          .ReturnsAsync(Array.Empty<APIKeyResponse>());
      _mockClient
          .Setup(x => x.GetSupportedServicesAsync(It.IsAny<CancellationToken>()))
          .ReturnsAsync(Array.Empty<string>());
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
      _ = new APIKeyManagerViewModel(_context, _mockClient.Object);
      _mockClient.Verify(x => x.GetKeysAsync(It.IsAny<CancellationToken>()), Times.Never);
      _mockClient.Verify(x => x.GetSupportedServicesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [TestMethod]
    public void Constructor_WithIAPIKeyManagerClient_CreatesInstance()
    {
      var vm = new APIKeyManagerViewModel(_context, _mockClient.Object);
      Assert.IsNotNull(vm);
      Assert.AreEqual(PanelIds.APIKeyManager, vm.PanelId);
      Assert.IsNotNull(vm.LoadKeysCommand);
      Assert.IsNotNull(vm.CreateKeyCommand);
      Assert.IsNotNull(vm.RefreshCommand);
    }

    [TestMethod]
    [ExpectedException(typeof(ArgumentNullException))]
    public void Constructor_WithNullClient_Throws()
    {
      _ = new APIKeyManagerViewModel(_context, null!);
    }

    [TestMethod]
    public async Task LoadKeysCommand_CallsIAPIKeyManagerClient_GetKeysAsync()
    {
      var vm = new APIKeyManagerViewModel(_context, _mockClient.Object);
      await vm.LoadKeysCommand.ExecuteAsync(null);
      _mockClient.Verify(
          x => x.GetKeysAsync(It.IsAny<CancellationToken>()),
          Times.AtLeastOnce);
    }
  }
}
