using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.UI.Dispatching;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using VoiceStudio.App.Services;
using VoiceStudio.App.Tests.Fixtures;
using VoiceStudio.App.ViewModels;
using VoiceStudio.Core.Models;
using VoiceStudio.Core.Panels;
using VoiceStudio.Core.Services;

namespace VoiceStudio.App.Tests.ViewModels
{
  /// <summary>
  /// Seam-aware tests for ScriptEditorViewModel.
  /// Instantiates ViewModel with mocked IScriptEditorClient.
  /// Supports "ScriptEditorViewModel migrated to IScriptEditorClient" claims.
  /// </summary>
  [TestClass]
  [TestCategory("SeamAware")]
  public class ScriptEditorViewModelSeamTests
  {
    private Mock<IScriptEditorClient> _mockClient = null!;
    private IViewModelContext _context = null!;
    private Mock<IDialogService> _mockDialogService = null!;
    private DispatcherQueueController? _dispatcherController;

    [TestInitialize]
    public void Setup()
    {
      TestAppServicesHelper.EnsureInitialized();
      _mockClient = new Mock<IScriptEditorClient>();
      _mockDialogService = new Mock<IDialogService>();
      _mockDialogService
          .Setup(x => x.ShowConfirmationAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
          .ReturnsAsync(true);
      _dispatcherController = DispatcherQueueController.CreateOnDedicatedThread();
      var dispatcher = _dispatcherController.DispatcherQueue;
      _context = new ViewModelContext(NullLogger.Instance, dispatcher);

      _mockClient
          .Setup(x => x.GetScriptsAsync(It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
          .ReturnsAsync(new List<Script>());
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
      _ = new ScriptEditorViewModel(_context, _mockClient.Object, _mockDialogService.Object);
      _mockClient.Verify(
          x => x.GetScriptsAsync(It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()),
          Times.Never);
    }

    [TestMethod]
    public void Constructor_WithIScriptEditorClient_CreatesInstance()
    {
      var vm = new ScriptEditorViewModel(_context, _mockClient.Object, _mockDialogService.Object);
      Assert.IsNotNull(vm);
      Assert.AreEqual(PanelIds.ScriptEditor, vm.PanelId);
      Assert.IsNotNull(vm.LoadScriptsCommand);
      Assert.IsNotNull(vm.CreateScriptCommand);
      Assert.IsNotNull(vm.DeleteScriptCommand);
    }

    [TestMethod]
    [ExpectedException(typeof(ArgumentNullException))]
    public void Constructor_WithNullClient_Throws()
    {
      _ = new ScriptEditorViewModel(_context, null!, _mockDialogService!.Object);
    }

    [TestMethod]
    public async Task LoadScriptsCommand_CallsIScriptEditorClient_GetScriptsAsync()
    {
      var vm = new ScriptEditorViewModel(_context, _mockClient.Object, _mockDialogService.Object);
      await vm.LoadScriptsCommand.ExecuteAsync(null);
      _mockClient.Verify(
          x => x.GetScriptsAsync(It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()),
          Times.AtLeastOnce);
    }
  }
}
