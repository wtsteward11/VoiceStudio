using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using System;
using System.Threading;
using System.Threading.Tasks;
using VoiceStudio.App.Services;
using VoiceStudio.App.Tests.Fixtures;
using VoiceStudio.App.ViewModels;
using VoiceStudio.App.Views.Panels;
using VoiceStudio.Core.Services;

namespace VoiceStudio.App.Tests.ViewModels
{
  /// <summary>
  /// Unit tests for DiagnosticsViewModel.
  /// Tests cover panel properties and initial state.
  /// </summary>
  [TestClass]
  public class DiagnosticsViewModelTests
  {
    private MockViewModelContext _mockContext = null!;
    private Mock<IBackendClient> _mockBackendClient = null!;
    private DiagnosticsViewModel _sut = null!;

    [TestInitialize]
    public void Setup()
    {
      TestAppServicesHelper.EnsureInitialized();
      _mockContext = new MockViewModelContext();
      _mockBackendClient = new Mock<IBackendClient>();

      _sut = new DiagnosticsViewModel(_mockContext, _mockBackendClient.Object);
    }

    [TestCleanup]
    public void Cleanup()
    {
      _sut?.Dispose();
    }

    #region Panel Properties Tests

    [TestMethod]
    public void PanelId_ReturnsDiagnostics()
    {
      Assert.AreEqual("diagnostics", _sut.PanelId);
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
      _ = new DiagnosticsViewModel(_mockContext, null!);
    }

    #endregion

    #region Initial State Tests

    [TestMethod]
    public void IsLoading_InitiallyFalse()
    {
      Assert.IsFalse(_sut.IsLoading);
    }

    [TestMethod]
    public void ErrorMessage_InitiallyNull()
    {
      Assert.IsNull(_sut.ErrorMessage);
    }

    #endregion

    // Note: GPU, Job Queue, and Feature Status data loading is implemented
    // in DiagnosticsView.xaml.cs code-behind, not the ViewModel.
    // View-level tests would require UI testing frameworks.
  }
}
