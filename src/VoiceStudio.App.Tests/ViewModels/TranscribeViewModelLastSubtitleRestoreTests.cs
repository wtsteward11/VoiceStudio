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
using VoiceStudio.Core.Services;

namespace VoiceStudio.App.Tests.ViewModels
{
  /// <summary>
  /// GAP-045 last-subtitle restore seam tests.
  /// Validates rehydrate behavior when persisted subtitle transcription id is present.
  /// </summary>
  [TestClass]
  [TestCategory("SeamAware")]
  public class TranscribeViewModelLastSubtitleRestoreTests
  {
    private Mock<ITranscriptionClient> _mockTranscriptionClient = null!;
    private Mock<IProjectAudioClient> _mockProjectAudioClient = null!;
    private Mock<IProjectRepository> _mockProjectRepository = null!;
    private IViewModelContext _context = null!;
    private DispatcherQueueController? _dispatcherController;

    [TestInitialize]
    public void Setup()
    {
      TestAppServicesHelper.EnsureInitialized();
      _mockTranscriptionClient = new Mock<ITranscriptionClient>();
      _mockProjectAudioClient = new Mock<IProjectAudioClient>();
      _mockProjectRepository = new Mock<IProjectRepository>();

      _dispatcherController = DispatcherQueueController.CreateOnDedicatedThread();
      var dispatcher = _dispatcherController.DispatcherQueue;
      _context = new ViewModelContext(NullLogger.Instance, dispatcher);

      _mockTranscriptionClient
          .Setup(x => x.GetSupportedLanguagesAsync(It.IsAny<CancellationToken>()))
          .ReturnsAsync(new List<SupportedLanguage>());
      _mockTranscriptionClient
          .Setup(x => x.GetTranscriptionEnginesAsync(It.IsAny<CancellationToken>()))
          .ReturnsAsync(new List<TranscriptionEngine>());
      _mockTranscriptionClient
          .Setup(x => x.ListTranscriptionsAsync(It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
          .ReturnsAsync(new List<TranscriptionResponse>());

      _mockProjectAudioClient
          .Setup(x => x.SaveAudioToProjectAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
          .ReturnsAsync(new ProjectAudioFile { Filename = "stub.wav" });

      _mockProjectRepository
          .Setup(x => x.GetLastSubtitleTranscriptionIdAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
          .ReturnsAsync((string?)null);
      _mockProjectRepository
          .Setup(x => x.SaveLastSubtitleTranscriptionIdAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
          .Returns(Task.CompletedTask);
    }

    [TestCleanup]
    public void Cleanup()
    {
      _dispatcherController?.ShutdownQueueAsync().AsTask().GetAwaiter().GetResult();
    }

    private TranscribeViewModel CreateSut() =>
        new TranscribeViewModel(_context, _mockTranscriptionClient.Object, _mockProjectAudioClient.Object, _mockProjectRepository.Object);

    private Task PumpDispatcherQueueAsync()
    {
      var tcs = new TaskCompletionSource<bool>();
      _context.Dispatcher.TryEnqueue(() => tcs.TrySetResult(true));
      return tcs.Task;
    }

    private async Task WaitForSelectionAsync(TranscribeViewModel vm, Func<TranscriptionResponse?, bool> predicate, int maxWaitMs = 2500)
    {
      var deadline = DateTime.UtcNow.AddMilliseconds(maxWaitMs);
      while (DateTime.UtcNow < deadline)
      {
        if (predicate(vm.SelectedTranscription))
          return;
        await Task.Delay(25).ConfigureAwait(false);
        await PumpDispatcherQueueAsync().ConfigureAwait(false);
      }

      Assert.Fail("Timed out waiting for SelectedTranscription to match expected condition.");
    }

    [TestMethod]
    public async Task RunBackendRehydrate_WhenNoInMemorySelection_RestoresStoredLastSubtitleId()
    {
      const string projectId = "proj-restore";
      const string audioId = "aud-restore";
      const string storedId = "tid-stored";

      var trStored = new TranscriptionResponse
      {
        Id = storedId,
        Text = "stored",
        Created = DateTime.UtcNow.AddMinutes(-2),
        Segments = new List<TranscriptionSegment>(),
      };
      var trOther = new TranscriptionResponse
      {
        Id = "tid-other",
        Text = "other",
        Created = DateTime.UtcNow,
        Segments = new List<TranscriptionSegment>(),
      };

      _mockProjectRepository
          .Setup(x => x.GetLastSubtitleTranscriptionIdAsync(projectId, It.IsAny<CancellationToken>()))
          .ReturnsAsync(storedId);
      _mockTranscriptionClient
          .Setup(x => x.ListTranscriptionsAsync(audioId, projectId, It.IsAny<CancellationToken>()))
          .ReturnsAsync(new List<TranscriptionResponse> { trOther, trStored });

      var vm = CreateSut();
      await vm.InitializeAsync(CancellationToken.None);
      vm.SelectedProjectId = projectId;
      vm.SelectedAudioId = audioId;

      await WaitForSelectionAsync(vm, tr => tr?.Id == storedId);

      Assert.AreEqual(storedId, vm.SelectedTranscription?.Id);
      _mockProjectRepository.Verify(
          x => x.GetLastSubtitleTranscriptionIdAsync(projectId, It.IsAny<CancellationToken>()),
          Times.AtLeastOnce);
    }

    [TestMethod]
    public async Task RunBackendRehydrate_WhenInMemorySelectionPresent_DoesNotOverrideWithStored()
    {
      const string projectId = "proj-keep";
      const string storedId = "tid-stored";

      var trA = new TranscriptionResponse
      {
        Id = "tid-a",
        Text = "first",
        Created = DateTime.UtcNow,
        Segments = new List<TranscriptionSegment>(),
      };
      var trStored = new TranscriptionResponse
      {
        Id = storedId,
        Text = "stored",
        Created = DateTime.UtcNow.AddMinutes(-1),
        Segments = new List<TranscriptionSegment>(),
      };

      _mockProjectRepository
          .Setup(x => x.GetLastSubtitleTranscriptionIdAsync(projectId, It.IsAny<CancellationToken>()))
          .ReturnsAsync(storedId);
      _mockTranscriptionClient
          .Setup(x => x.ListTranscriptionsAsync("aud-1", projectId, It.IsAny<CancellationToken>()))
          .ReturnsAsync(new List<TranscriptionResponse> { trA });
      _mockTranscriptionClient
          .Setup(x => x.ListTranscriptionsAsync("aud-2", projectId, It.IsAny<CancellationToken>()))
          .ReturnsAsync(new List<TranscriptionResponse> { trStored, trA });

      var vm = CreateSut();
      await vm.InitializeAsync(CancellationToken.None);
      vm.SelectedProjectId = projectId;
      vm.SelectedAudioId = "aud-1";

      await WaitForSelectionAsync(vm, tr => tr?.Id == "tid-a");

      vm.SelectedAudioId = "aud-2";
      await WaitForSelectionAsync(vm, tr => tr?.Id == "tid-a");

      Assert.AreEqual("tid-a", vm.SelectedTranscription?.Id);
      _mockProjectRepository.Verify(
          x => x.GetLastSubtitleTranscriptionIdAsync(projectId, It.IsAny<CancellationToken>()),
          Times.Once);
    }

    [TestMethod]
    public async Task RunBackendRehydrate_WhenStoredIdNotInList_PicksFirstAndLogsRestoreMessage()
    {
      const string projectId = "proj-missing";
      const string audioId = "aud-missing";

      var trOnly = new TranscriptionResponse
      {
        Id = "tid-only",
        Text = "only",
        Created = DateTime.UtcNow,
        Segments = new List<TranscriptionSegment>(),
      };

      _mockProjectRepository
          .Setup(x => x.GetLastSubtitleTranscriptionIdAsync(projectId, It.IsAny<CancellationToken>()))
          .ReturnsAsync("tid-missing");
      _mockTranscriptionClient
          .Setup(x => x.ListTranscriptionsAsync(audioId, projectId, It.IsAny<CancellationToken>()))
          .ReturnsAsync(new List<TranscriptionResponse> { trOnly });

      var vm = CreateSut();
      await vm.InitializeAsync(CancellationToken.None);
      vm.SelectedProjectId = projectId;
      vm.SelectedAudioId = audioId;

      await WaitForSelectionAsync(vm, tr => tr?.Id == "tid-only");

      Assert.AreEqual("tid-only", vm.SelectedTranscription?.Id);
      Assert.IsFalse(string.IsNullOrWhiteSpace(vm.TranscriptOperatorMessage));
      StringAssert.Contains(
          vm.TranscriptOperatorMessage,
          "Last subtitle transcription no longer exists",
          StringComparison.OrdinalIgnoreCase);
    }

    [TestMethod]
    public async Task RunBackendRehydrate_WhenStoredIdIsNullOrEmpty_NoBehaviorChange()
    {
      const string projectId = "proj-null";
      const string audioId = "aud-null";

      var trOnly = new TranscriptionResponse
      {
        Id = "tid-first",
        Text = "first",
        Created = DateTime.UtcNow,
        Segments = new List<TranscriptionSegment>(),
      };

      _mockProjectRepository
          .Setup(x => x.GetLastSubtitleTranscriptionIdAsync(projectId, It.IsAny<CancellationToken>()))
          .ReturnsAsync((string?)null);
      _mockTranscriptionClient
          .Setup(x => x.ListTranscriptionsAsync(audioId, projectId, It.IsAny<CancellationToken>()))
          .ReturnsAsync(new List<TranscriptionResponse> { trOnly });

      var vm = CreateSut();
      await vm.InitializeAsync(CancellationToken.None);
      vm.SelectedProjectId = projectId;
      vm.SelectedAudioId = audioId;

      await WaitForSelectionAsync(vm, tr => tr?.Id == "tid-first");

      Assert.AreEqual("tid-first", vm.SelectedTranscription?.Id);
      Assert.IsTrue(string.IsNullOrWhiteSpace(vm.TranscriptOperatorMessage));
      _mockProjectRepository.Verify(
          x => x.GetLastSubtitleTranscriptionIdAsync(projectId, It.IsAny<CancellationToken>()),
          Times.AtLeastOnce);
    }

    /// <summary>GAP-045 lifecycle: switching project clears in-memory selection so restore reads stored id for the new project.</summary>
    [TestMethod]
    public async Task Rehydrate_WhenProjectIdChanges_ReadsRepositoryRestore_NotPriorInMemoryTranscription()
    {
      const string audioId = "aud-lc";
      var trOld = new TranscriptionResponse
      {
        Id = "tid-old-proj",
        Text = "old",
        Created = DateTime.UtcNow,
        Segments = new List<TranscriptionSegment>(),
      };
      var trNewer = new TranscriptionResponse
      {
        Id = "tid-newer",
        Text = "newer",
        Created = DateTime.UtcNow.AddMinutes(1),
        Segments = new List<TranscriptionSegment>(),
      };
      var trFromRepo = new TranscriptionResponse
      {
        Id = "tid-from-repo",
        Text = "repo",
        Created = DateTime.UtcNow.AddMinutes(-1),
        Segments = new List<TranscriptionSegment>(),
      };

      _mockTranscriptionClient
          .Setup(x => x.ListTranscriptionsAsync(audioId, "proj-a", It.IsAny<CancellationToken>()))
          .ReturnsAsync(new List<TranscriptionResponse> { trOld });
      _mockTranscriptionClient
          .Setup(x => x.ListTranscriptionsAsync(audioId, "proj-b", It.IsAny<CancellationToken>()))
          .ReturnsAsync(new List<TranscriptionResponse> { trNewer, trFromRepo });

      _mockProjectRepository
          .Setup(x => x.GetLastSubtitleTranscriptionIdAsync("proj-b", It.IsAny<CancellationToken>()))
          .ReturnsAsync("tid-from-repo");

      var vm = CreateSut();
      await vm.InitializeAsync(CancellationToken.None);
      vm.SelectedProjectId = "proj-a";
      vm.SelectedAudioId = audioId;
      await WaitForSelectionAsync(vm, tr => tr?.Id == "tid-old-proj");

      vm.SelectedProjectId = "proj-b";
      await WaitForSelectionAsync(vm, tr => tr?.Id == "tid-from-repo");

      _mockProjectRepository.Verify(
          x => x.GetLastSubtitleTranscriptionIdAsync("proj-b", It.IsAny<CancellationToken>()),
          Times.AtLeastOnce);
      Assert.AreEqual("tid-from-repo", vm.SelectedTranscription?.Id);
    }
  }
}
