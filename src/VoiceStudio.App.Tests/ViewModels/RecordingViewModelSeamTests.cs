using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.UI.Dispatching;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using System;
using System.Threading;
using VoiceStudio.App.Core.Models;
using VoiceStudio.App.Services;
using VoiceStudio.App.Tests.Fixtures;
using VoiceStudio.App.Utilities;
using VoiceStudio.App.ViewModels;
using VoiceStudio.Core.Events;
using VoiceStudio.Core.Models;
using VoiceStudio.Core.Panels;
using VoiceStudio.Core.Services;

namespace VoiceStudio.App.Tests.ViewModels
{
  /// <summary>
  /// Seam-aware tests for RecordingViewModel.
  /// Instantiates ViewModel with mocked IRecordingClient, IAudioPlayerService.
  /// Supports "RecordingViewModel migrated to IRecordingClient" claims.
  /// See docs/governance/TEST_CLASSIFICATION.md.
  /// </summary>
  [TestClass]
  [TestCategory("SeamAware")]
  public class RecordingViewModelSeamTests
  {
    private Mock<IRecordingClient> _mockRecordingClient = null!;
    private Mock<IProjectAudioClient> _mockProjectAudioClient = null!;
    private Mock<IAudioPlayerService> _mockAudioPlayer = null!;
    private IViewModelContext _context = null!;
    private DispatcherQueueController? _dispatcherController;

    [TestInitialize]
    public void Setup()
    {
      TestAppServicesHelper.EnsureInitialized();
      _mockRecordingClient = new Mock<IRecordingClient>();
      _mockProjectAudioClient = new Mock<IProjectAudioClient>();
      _mockAudioPlayer = new Mock<IAudioPlayerService>();
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
      _ = new RecordingViewModel(_context, _mockRecordingClient.Object, _mockProjectAudioClient.Object, _mockAudioPlayer.Object);
      _mockRecordingClient.Verify(x => x.GetRecordingDevicesAsync(It.IsAny<CancellationToken>()), Times.Never);
      _mockRecordingClient.Verify(x => x.UploadAudioFileAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
      _mockProjectAudioClient.Verify(
          x => x.SaveAudioToProjectAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()),
          Times.Never);
    }

    [TestMethod]
    public void Constructor_WithClients_CreatesInstance()
    {
      var vm = new RecordingViewModel(_context, _mockRecordingClient.Object, _mockProjectAudioClient.Object, _mockAudioPlayer.Object);
      Assert.IsNotNull(vm);
      Assert.AreEqual(PanelIds.Recording, vm.PanelId);
    }

    [TestMethod]
    [ExpectedException(typeof(ArgumentNullException))]
    public void Constructor_WithNullRecordingClient_Throws()
    {
      _ = new RecordingViewModel(_context, null!, _mockProjectAudioClient.Object, _mockAudioPlayer.Object);
    }

    [TestMethod]
    [ExpectedException(typeof(ArgumentNullException))]
    public void Constructor_WithNullAudioPlayer_Throws()
    {
      _ = new RecordingViewModel(_context, _mockRecordingClient.Object, _mockProjectAudioClient.Object, null!);
    }

    /// <summary>
    /// Pass 05 C4: recorded clip playback uses the same base URL resolver as Script Editor (no ad hoc localhost string in VM).
    /// </summary>
    [TestMethod]
    public async Task PlayCommand_PassesBackendPlaybackResolvedBaseUrl_ToAudioPlayer()
    {
      TestAppServicesHelper.EnsureInitialized();
      _mockRecordingClient
          .Setup(x => x.GetRecordingDevicesAsync(It.IsAny<CancellationToken>()))
          .ReturnsAsync(new RecordingDevicesResponse { Devices = System.Array.Empty<RecordingDevice>() });
      string? capturedUrl = null;
      _mockAudioPlayer
          .Setup(x => x.PlayBackendAudioIdAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Action?>()))
          .Callback<string, string, Action?>((_, url, _) => capturedUrl = url)
          .Returns(Task.CompletedTask);

      var vm = new RecordingViewModel(_context, _mockRecordingClient.Object, _mockProjectAudioClient.Object, _mockAudioPlayer.Object);
      vm.RecordedAudioId = "rec-audio-1";
      await vm.PlayCommand.ExecuteAsync(null);

      var expected = BackendPlaybackBaseUrl.Resolve(null);
      Assert.AreEqual(expected, capturedUrl);
      _mockAudioPlayer.Verify(
          x => x.PlayBackendAudioIdAsync("rec-audio-1", expected, It.IsAny<Action?>()),
          Times.Once);
    }

    /// <summary>
    /// Pass 05 Option C: after library upload success, active project triggers exactly one project save with filename hint.
    /// </summary>
    [TestMethod]
    public async Task ApplyPostLibraryUploadSuccessAsync_WithProject_CallsSaveAudioToProjectOnce()
    {
      _mockProjectAudioClient
          .Setup(x => x.SaveAudioToProjectAsync("proj-1", "lib-audio-1", "take.wav", It.IsAny<CancellationToken>()))
          .ReturnsAsync(new ProjectAudioFile { Filename = "take.wav" });

      var vm = new RecordingViewModel(_context, _mockRecordingClient.Object, _mockProjectAudioClient.Object, _mockAudioPlayer.Object);
      vm.ProjectId = "proj-1";
      var upload = new AudioUploadResponse { Id = "lib-audio-1", Path = "http://backend/audio/lib-audio-1" };

      await vm.ApplyPostLibraryUploadSuccessAsync(upload, @"C:\temp\take.wav", CancellationToken.None);

      Assert.AreEqual("lib-audio-1", vm.RecordedAudioId);
      Assert.AreEqual(upload.Path, vm.RecordedAudioUrl);
      _mockProjectAudioClient.Verify(
          x => x.SaveAudioToProjectAsync("proj-1", "lib-audio-1", "take.wav", It.IsAny<CancellationToken>()),
          Times.Once);
    }

    /// <summary>
    /// Pass 05 Option C: no active project means library upload success does not call project persistence.
    /// </summary>
    [TestMethod]
    public async Task ApplyPostLibraryUploadSuccessAsync_WithoutProject_DoesNotCallSaveAudioToProject()
    {
      var vm = new RecordingViewModel(_context, _mockRecordingClient.Object, _mockProjectAudioClient.Object, _mockAudioPlayer.Object);
      vm.ProjectId = null;
      var upload = new AudioUploadResponse { Id = "lib-2", Path = "http://backend/x" };

      await vm.ApplyPostLibraryUploadSuccessAsync(upload, @"D:\rec\clip.flac", CancellationToken.None);

      _mockProjectAudioClient.Verify(
          x => x.SaveAudioToProjectAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()),
          Times.Never);
    }

    /// <summary>
    /// Pass 05 Option C: project save failure must not break post-upload flow (logged inside <see cref="RecordingToProjectPersistence"/>).
    /// </summary>
    [TestMethod]
    public async Task ApplyPostLibraryUploadSuccessAsync_WhenSaveThrows_CompletesWithoutThrowing()
    {
      _mockProjectAudioClient
          .Setup(x => x.SaveAudioToProjectAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
          .ThrowsAsync(new InvalidOperationException("save failed"));

      var vm = new RecordingViewModel(_context, _mockRecordingClient.Object, _mockProjectAudioClient.Object, _mockAudioPlayer.Object);
      vm.ProjectId = "p1";
      var upload = new AudioUploadResponse { Id = "a1", Path = "http://x" };

      await vm.ApplyPostLibraryUploadSuccessAsync(upload, @"C:\x\y.wav", CancellationToken.None);

      Assert.AreEqual("a1", vm.RecordedAudioId);
    }

    /// <summary>GAP-027: library focus + transcribe prefill contract — <see cref="PanelIds.Recording"/> source id.</summary>
    [TestMethod]
    public async Task ApplyPostLibraryUploadSuccessAsync_PublishesAssetAdded_WithRecordingPanelSource()
    {
      TestAppServicesHelper.EnsureInitialized();
      AssetAddedEvent? published = null;
      var agg = AppServices.GetService<IEventAggregator>();
      Assert.IsNotNull(agg);
      using var _ = agg.Subscribe<AssetAddedEvent>(e => published = e);

      var vm = new RecordingViewModel(_context, _mockRecordingClient.Object, _mockProjectAudioClient.Object, _mockAudioPlayer.Object);
      var upload = new AudioUploadResponse { Id = "lib-asset-99", Path = "http://backend/audio/lib-asset-99" };

      await vm.ApplyPostLibraryUploadSuccessAsync(upload, @"C:\take.wav", CancellationToken.None);

      Assert.IsNotNull(published);
      Assert.AreEqual(PanelIds.Recording, published.SourcePanelId);
      Assert.AreEqual("lib-asset-99", published.AssetId);
      Assert.AreEqual("audio", published.AssetType);
    }
  }
}
