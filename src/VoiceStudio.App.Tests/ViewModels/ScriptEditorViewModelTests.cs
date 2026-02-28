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
using VoiceStudio.Core.Services;

namespace VoiceStudio.App.Tests.ViewModels
{
  /// <summary>
  /// Unit tests for ScriptEditorViewModel.
  /// Tests cover panel properties and initial state.
  /// </summary>
  [TestClass]
  public class ScriptEditorViewModelTests
  {
    private IViewModelContext _context = null!;
    private Mock<IBackendClient> _mockBackendClient = null!;
    private DispatcherQueueController? _dispatcherController;
    private ScriptEditorViewModel _sut = null!;

    [TestInitialize]
    public void Setup()
    {
      TestAppServicesHelper.EnsureInitialized();
      _dispatcherController = DispatcherQueueController.CreateOnDedicatedThread();
      var dispatcher = _dispatcherController.DispatcherQueue;
      _context = new ViewModelContext(NullLogger.Instance, dispatcher);
      _mockBackendClient = new Mock<IBackendClient>();

      _sut = new ScriptEditorViewModel(_context, _mockBackendClient.Object);
    }

    [TestCleanup]
    public void Cleanup()
    {
      _sut?.Dispose();
      _dispatcherController?.ShutdownQueueAsync().AsTask().GetAwaiter().GetResult();
    }

    #region Panel Properties Tests

    [TestMethod]
    public void PanelId_ReturnsScriptEditor()
    {
      Assert.AreEqual("script-editor", _sut.PanelId);
    }

    [TestMethod]
    public void DisplayName_ReturnsLocalizedName()
    {
      Assert.IsNotNull(_sut.DisplayName);
      Assert.IsTrue(_sut.DisplayName.Length > 0);
    }

    #endregion

    #region Constructor Tests

    [TestMethod]
    public void Constructor_WithValidDependencies_CreatesInstance()
    {
      Assert.IsNotNull(_sut);
    }

    [TestMethod]
    [ExpectedException(typeof(ArgumentNullException))]
    public void Constructor_WithNullBackendClient_ThrowsArgumentNullException()
    {
      _ = new ScriptEditorViewModel(_context, null!);
    }

    #endregion

    #region Initial State Tests

    [TestMethod]
    public void Scripts_InitiallyEmpty()
    {
      Assert.IsNotNull(_sut.Scripts);
      Assert.AreEqual(0, _sut.Scripts.Count);
    }

    [TestMethod]
    public void SelectedScript_InitiallyNull()
    {
      Assert.IsNull(_sut.SelectedScript);
    }

    [TestMethod]
    public void IsLoading_InitiallyFalse()
    {
      Assert.IsFalse(_sut.IsLoading);
    }

    #endregion

    #region Command Existence Tests

    [TestMethod]
    public void DeleteScriptCommand_IsNotNull()
    {
      Assert.IsNotNull(_sut.DeleteScriptCommand);
    }

    #endregion
  }
}
