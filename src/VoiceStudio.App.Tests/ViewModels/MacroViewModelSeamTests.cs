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
  /// Seam-aware tests for MacroViewModel.
  /// Instantiates ViewModel with mocked IMacroClient, IDialogService.
  /// Supports "MacroViewModel migrated to IMacroClient" claims.
  /// See docs/governance/TEST_CLASSIFICATION.md.
  /// </summary>
  [TestClass]
  [TestCategory("SeamAware")]
  public class MacroViewModelSeamTests
  {
    private Mock<IMacroClient> _mockMacroClient = null!;
    private Mock<IDialogService> _mockDialogService = null!;
    private IViewModelContext _context = null!;
    private DispatcherQueueController? _dispatcherController;

    [TestInitialize]
    public void Setup()
    {
      TestAppServicesHelper.EnsureInitialized();
      _mockMacroClient = new Mock<IMacroClient>();
      _mockDialogService = new Mock<IDialogService>();
      _dispatcherController = DispatcherQueueController.CreateOnDedicatedThread();
      var dispatcher = _dispatcherController.DispatcherQueue;
      _context = new ViewModelContext(NullLogger.Instance, dispatcher);

      _mockMacroClient
        .Setup(x => x.GetMacrosAsync(It.IsAny<string?>(), It.IsAny<CancellationToken>()))
        .ReturnsAsync(new List<Macro>());
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
      _ = new MacroViewModel(_context, _mockMacroClient.Object, _mockDialogService.Object);
      _mockMacroClient.Verify(x => x.GetMacrosAsync(It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [TestMethod]
    public void Constructor_WithClients_CreatesInstance()
    {
      var vm = new MacroViewModel(_context, _mockMacroClient.Object, _mockDialogService.Object);
      Assert.IsNotNull(vm);
      Assert.AreEqual(PanelIds.Macro, vm.PanelId);
      Assert.IsNotNull(vm.LoadMacrosCommand);
      Assert.IsNotNull(vm.CreateMacroCommand);
    }

    [TestMethod]
    [ExpectedException(typeof(ArgumentNullException))]
    public void Constructor_WithNullMacroClient_Throws()
    {
      _ = new MacroViewModel(_context, null!, _mockDialogService.Object);
    }

    [TestMethod]
    [ExpectedException(typeof(ArgumentNullException))]
    public void Constructor_WithNullDialogService_Throws()
    {
      _ = new MacroViewModel(_context, _mockMacroClient.Object, null!);
    }
  }
}
