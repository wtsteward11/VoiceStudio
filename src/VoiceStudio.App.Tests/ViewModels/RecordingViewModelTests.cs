using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.UI.Dispatching;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using System;
using System.Threading;
using System.Threading.Tasks;
using VoiceStudio.App.Services;
using VoiceStudio.App.Tests.Fixtures;
using VoiceStudio.App.ViewModels;
using VoiceStudio.Core.Panels;
using VoiceStudio.Core.Services;

namespace VoiceStudio.App.Tests.ViewModels
{
  /// <summary>
  /// Unit tests for RecordingViewModel.
  /// Tests cover panel properties and initial state.
  /// </summary>
  [TestClass]
  public class RecordingViewModelTests
  {
    private IViewModelContext _context = null!;
    private Mock<IRecordingClient> _mockRecordingClient = null!;
    private Mock<IProjectAudioClient> _mockProjectAudioClient = null!;
    private Mock<IAudioPlayerService> _mockAudioPlayer = null!;
    private DispatcherQueueController? _dispatcherController;
    private RecordingViewModel _sut = null!;

    [TestInitialize]
    public void Setup()
    {
      TestAppServicesHelper.EnsureInitialized();
      _dispatcherController = DispatcherQueueController.CreateOnDedicatedThread();
      var dispatcher = _dispatcherController.DispatcherQueue;
      _context = new ViewModelContext(NullLogger.Instance, dispatcher);
      _mockRecordingClient = new Mock<IRecordingClient>();
      _mockProjectAudioClient = new Mock<IProjectAudioClient>();
      _mockAudioPlayer = new Mock<IAudioPlayerService>();
      _mockAudioPlayer.Setup(x => x.PlayBackendAudioIdAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Action>()))
          .Returns(Task.CompletedTask);
      _mockAudioPlayer.Setup(x => x.PlayFileAsync(It.IsAny<string>(), It.IsAny<Action>()))
          .Returns(Task.CompletedTask);
      _mockAudioPlayer.Setup(x => x.PlayUrlAsync(It.IsAny<string>(), It.IsAny<Action>()))
          .Returns(Task.CompletedTask);

      _sut = new RecordingViewModel(_context, _mockRecordingClient.Object, _mockProjectAudioClient.Object, _mockAudioPlayer.Object);
    }

    [TestCleanup]
    public void Cleanup()
    {
      _sut?.Dispose();
      DispatcherQueueTestHelpers.ShutdownSyncBounded(_dispatcherController);
    }

    #region Panel Properties Tests

    [TestMethod]
    public void PanelId_ReturnsRecording()
    {
      Assert.AreEqual(PanelIds.Recording, _sut.PanelId);
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
      Assert.IsNotNull(_sut.StartRecordingCommand);
      Assert.IsNotNull(_sut.StopRecordingCommand);
      Assert.IsNotNull(_sut.PlayCommand);
    }

    [TestMethod]
    [ExpectedException(typeof(ArgumentNullException))]
    public void Constructor_WithNullRecordingClient_ThrowsArgumentNullException()
    {
      _ = new RecordingViewModel(_context, null!, _mockProjectAudioClient.Object, _mockAudioPlayer.Object);
    }

    [TestMethod]
    [ExpectedException(typeof(ArgumentNullException))]
    public void Constructor_WithNullProjectAudioClient_ThrowsArgumentNullException()
    {
      _ = new RecordingViewModel(_context, _mockRecordingClient.Object, null!, _mockAudioPlayer.Object);
    }

    /// <summary>
    /// Verifies Dispose can be called multiple times without throwing.
    /// </summary>
    [TestMethod]
    public void Dispose_CanBeCalledMultipleTimes()
    {
      var vm = new RecordingViewModel(_context, _mockRecordingClient.Object, _mockProjectAudioClient.Object, _mockAudioPlayer.Object);
      vm.Dispose();
      vm.Dispose();
    }

    #endregion

    #region Initial State Tests

    [TestMethod]
    public void IsRecording_InitiallyFalse()
    {
      Assert.IsFalse(_sut.IsRecording);
    }

    [TestMethod]
    public void RecordingDuration_InitiallyZero()
    {
      Assert.AreEqual(TimeSpan.Zero, _sut.RecordingDuration);
    }

    [TestMethod]
    public void IsLoading_InitiallyFalse()
    {
      Assert.IsFalse(_sut.IsLoading);
    }

    #endregion

    #region Command State Tests

    [TestMethod]
    public void StartRecordingCommand_WhenNotRecording_CanExecute()
    {
      _sut.IsRecording = false;
      Assert.IsTrue(_sut.StartRecordingCommand.CanExecute(null));
    }

    [TestMethod]
    public void StopRecordingCommand_Exists()
    {
      Assert.IsNotNull(_sut.StopRecordingCommand);
    }

    #endregion
  }
}
