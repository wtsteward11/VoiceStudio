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
using VoiceStudio.Core.Events;
using VoiceStudio.Core.Models;
using VoiceStudio.Core.Panels;
using VoiceStudio.Core.Services;

namespace VoiceStudio.App.Tests.ViewModels
{
  /// <summary>
  /// Seam-aware tests for TranscribeViewModel.
  /// Instantiates TranscribeViewModel with mocked ITranscriptionClient.
  /// Supports "TranscribeViewModel migrated to ITranscriptionClient" claims.
  /// See docs/governance/TEST_CLASSIFICATION.md.
  /// </summary>
  [TestClass]
  [TestCategory("SeamAware")]
  public class TranscribeViewModelSeamTests
  {
    private Mock<ITranscriptionClient> _mockTranscriptionClient = null!;
    private Mock<IProjectAudioClient> _mockProjectAudioClient = null!;
    private IViewModelContext _context = null!;
    private DispatcherQueueController? _dispatcherController;

    [TestInitialize]
    public void Setup()
    {
      TestAppServicesHelper.EnsureInitialized();
      _mockTranscriptionClient = new Mock<ITranscriptionClient>();
      _dispatcherController = DispatcherQueueController.CreateOnDedicatedThread();
      var dispatcher = _dispatcherController.DispatcherQueue;
      _context = new ViewModelContext(NullLogger.Instance, dispatcher);

      _mockTranscriptionClient
          .Setup(x => x.GetSupportedLanguagesAsync(It.IsAny<CancellationToken>()))
          .ReturnsAsync(new List<SupportedLanguage>());
      _mockTranscriptionClient
          .Setup(x => x.GetTranscriptionEnginesAsync(It.IsAny<CancellationToken>()))
          .ReturnsAsync(new List<TranscriptionEngine>());
      _mockProjectAudioClient = new Mock<IProjectAudioClient>();
      _mockProjectAudioClient
          .Setup(x => x.SaveAudioToProjectAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
          .ReturnsAsync(new ProjectAudioFile { Filename = "stub.wav" });
      _mockTranscriptionClient
          .Setup(x => x.ListTranscriptionsAsync(It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
          .ReturnsAsync(new List<TranscriptionResponse>());
    }

    private TranscribeViewModel CreateSut() =>
        new TranscribeViewModel(_context, _mockTranscriptionClient.Object, _mockProjectAudioClient.Object);

    [TestCleanup]
    public void Cleanup()
    {
      _dispatcherController?.ShutdownQueueAsync().AsTask().GetAwaiter().GetResult();
    }

    private Task PumpDispatcherQueueAsync()
    {
      var tcs = new TaskCompletionSource<bool>();
      _context.Dispatcher.TryEnqueue(() => tcs.TrySetResult(true));
      return tcs.Task;
    }

    /// <summary>
    /// Invariant: constructor must not call any client methods before activation/InitializeAsync.
    /// Prevents constructor fire-and-forget regression (RETAINED_ASYNC_RULE, ADR-047).
    /// </summary>
    [TestMethod]
    public void Constructor_DoesNotCallClient_BeforeActivation()
    {
      _ = CreateSut();
      _mockTranscriptionClient.Verify(x => x.GetTranscriptionEnginesAsync(It.IsAny<CancellationToken>()), Times.Never);
      _mockTranscriptionClient.Verify(x => x.GetSupportedLanguagesAsync(It.IsAny<CancellationToken>()), Times.Never);
      _mockProjectAudioClient.Verify(
          x => x.SaveAudioToProjectAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()),
          Times.Never);
    }

    [TestMethod]
    public void Constructor_WithITranscriptionClient_CreatesInstance()
    {
      var vm = CreateSut();
      Assert.IsNotNull(vm);
      Assert.AreEqual(PanelIds.Transcribe, vm.PanelId);
      Assert.IsNotNull(vm.TranscribeCommand);
      Assert.IsNotNull(vm.LoadTranscriptionsCommand);
    }

    [TestMethod]
    [ExpectedException(typeof(ArgumentNullException))]
    public void Constructor_WithNullTranscriptionClient_Throws()
    {
      _ = new TranscribeViewModel(_context, null!, _mockProjectAudioClient.Object);
    }

    [TestMethod]
    [ExpectedException(typeof(ArgumentNullException))]
    public void Constructor_WithNullProjectAudioClient_Throws()
    {
      _ = new TranscribeViewModel(_context, _mockTranscriptionClient.Object, null!);
    }

    [TestMethod]
    public async Task InitializeAsync_CallsITranscriptionClient_GetTranscriptionEnginesAsync()
    {
      var vm = CreateSut();
      await vm.InitializeAsync(CancellationToken.None);
      _mockTranscriptionClient.Verify(
          x => x.GetTranscriptionEnginesAsync(It.IsAny<CancellationToken>()),
          Times.Once);
    }

    [TestMethod]
    public async Task InitializeAsync_CallsITranscriptionClient_GetSupportedLanguagesAsync()
    {
      var vm = CreateSut();
      await vm.InitializeAsync(CancellationToken.None);
      _mockTranscriptionClient.Verify(
          x => x.GetSupportedLanguagesAsync(It.IsAny<CancellationToken>()),
          Times.Once);
    }

    /// <summary>
    /// Pass 05 C4: empty audio id must not call list API and must surface ErrorMessage.
    /// </summary>
    [TestMethod]
    public async Task LoadTranscriptionsCommand_WhenAudioIdEmpty_SetsErrorMessage_DoesNotCallList()
    {
      _mockTranscriptionClient
          .Setup(x => x.GetSupportedLanguagesAsync(It.IsAny<CancellationToken>()))
          .ReturnsAsync(new List<SupportedLanguage>());
      _mockTranscriptionClient
          .Setup(x => x.GetTranscriptionEnginesAsync(It.IsAny<CancellationToken>()))
          .ReturnsAsync(new List<TranscriptionEngine>());
      var vm = CreateSut();
      await vm.InitializeAsync(CancellationToken.None);
      vm.SelectedAudioId = "   ";
      await vm.LoadTranscriptionsCommand.ExecuteAsync(null);
      Assert.IsFalse(string.IsNullOrEmpty(vm.ErrorMessage));
      _mockTranscriptionClient.Verify(
          x => x.ListTranscriptionsAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
          Times.Never);
    }

    /// <summary>
    /// Pass 05 C2: recording upload success publishes AssetAddedEvent; Transcribe prefills when empty.
    /// </summary>
    [TestMethod]
    public async Task AssetAdded_RecordingPanel_WhenSelectedAudioIdEmpty_SetsSelectedAudioId()
    {
      var vm = CreateSut();
      await vm.OnActivatedAsync(CancellationToken.None);
      var agg = AppServices.TryGetEventAggregator();
      Assert.IsNotNull(agg);
      agg.Publish(new AssetAddedEvent("recording-panel", "backend-audio-99", "audio", @"C:\temp\rec.wav"));
      await PumpDispatcherQueueAsync();
      Assert.AreEqual("backend-audio-99", vm.SelectedAudioId);
    }

    /// <summary>
    /// Pass 05 C2: do not overwrite user-entered audio id.
    /// </summary>
    [TestMethod]
    public async Task AssetAdded_RecordingPanel_WhenSelectedAudioIdAlreadySet_DoesNotOverwrite()
    {
      var vm = CreateSut();
      await vm.OnActivatedAsync(CancellationToken.None);
      vm.SelectedAudioId = "user-chosen-id";
      var agg = AppServices.TryGetEventAggregator();
      Assert.IsNotNull(agg);
      agg.Publish(new AssetAddedEvent("recording-panel", "new-from-recording", "audio", null));
      await PumpDispatcherQueueAsync();
      Assert.AreEqual("user-chosen-id", vm.SelectedAudioId);
    }

    /// <summary>
    /// Pass 05 C2: only recording-panel and import-workflow sources participate.
    /// </summary>
    [TestMethod]
    public async Task AssetAdded_UnknownSource_DoesNotSetSelectedAudioId()
    {
      var vm = CreateSut();
      await vm.OnActivatedAsync(CancellationToken.None);
      var agg = AppServices.TryGetEventAggregator();
      Assert.IsNotNull(agg);
      agg.Publish(new AssetAddedEvent("voice-synthesis-panel", "x", "audio", null));
      await PumpDispatcherQueueAsync();
      Assert.IsTrue(string.IsNullOrEmpty(vm.SelectedAudioId));
    }

    /// <summary>
    /// Pass 05 C2: single-file import workflow uses import-workflow source id.
    /// </summary>
    [TestMethod]
    public async Task AssetAdded_ImportWorkflow_WhenEmpty_SetsSelectedAudioId()
    {
      var vm = CreateSut();
      await vm.OnActivatedAsync(CancellationToken.None);
      var agg = AppServices.TryGetEventAggregator();
      Assert.IsNotNull(agg);
      agg.Publish(new AssetAddedEvent("import-workflow", "playback-id-42", "audio", @"C:\in.wav"));
      await PumpDispatcherQueueAsync();
      Assert.AreEqual("playback-id-42", vm.SelectedAudioId);
    }

    /// <summary>
    /// Pass 05 C2: after deactivate, AssetAdded must not update (subscriptions released).
    /// </summary>
    [TestMethod]
    public async Task OnDeactivatedAsync_AfterAssetAddedSubscription_Unsubscribes()
    {
      var vm = CreateSut();
      await vm.OnActivatedAsync(CancellationToken.None);
      await vm.OnDeactivatedAsync(CancellationToken.None);
      var agg = AppServices.TryGetEventAggregator();
      Assert.IsNotNull(agg);
      agg.Publish(new AssetAddedEvent("recording-panel", "should-not-apply", "audio", null));
      await PumpDispatcherQueueAsync();
      Assert.IsTrue(string.IsNullOrEmpty(vm.SelectedAudioId));
    }

    /// <summary>
    /// Pass 05 C2: Dispose releases subscriptions (same as deactivate for event cleanup).
    /// </summary>
    [TestMethod]
    public async Task Dispose_AfterDispose_AssetAddedDoesNotUpdateSelectedAudioId()
    {
      var vm = CreateSut();
      await vm.OnActivatedAsync(CancellationToken.None);
      vm.Dispose();
      var agg = AppServices.TryGetEventAggregator();
      Assert.IsNotNull(agg);
      agg.Publish(new AssetAddedEvent("recording-panel", "post-dispose", "audio", null));
      await PumpDispatcherQueueAsync();
      Assert.IsTrue(string.IsNullOrEmpty(vm.SelectedAudioId));
    }

    /// <summary>
    /// Pass 05 C3 Option B: after successful transcribe, bindable semantics hint explains library vs project audio.
    /// </summary>
    [TestMethod]
    public async Task TranscribeAsync_WhenSucceeds_SetsAudioPersistenceSemanticsHint()
    {
      var tr = new TranscriptionResponse
      {
        Id = "tr-c3-1",
        AudioId = "audio-c3-1",
        Text = "hello",
        Segments = new List<TranscriptionSegment>(),
        Duration = 1.0,
        Created = DateTime.UtcNow
      };
      _mockTranscriptionClient
          .Setup(x => x.TranscribeAudioAsync(It.IsAny<TranscriptionRequest>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
          .ReturnsAsync(tr);
      _mockTranscriptionClient
          .Setup(x => x.ListTranscriptionsAsync(It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
          .ReturnsAsync(new List<TranscriptionResponse> { tr });

      var vm = CreateSut();
      await vm.InitializeAsync(CancellationToken.None);
      vm.SelectedAudioId = "audio-c3-1";
      await vm.TranscribeCommand.ExecuteAsync(null);

      Assert.IsFalse(string.IsNullOrWhiteSpace(vm.AudioPersistenceSemanticsHint));
      StringAssert.Contains(vm.AudioPersistenceSemanticsHint, "library", StringComparison.OrdinalIgnoreCase);
      _mockProjectAudioClient.Verify(
          x => x.SaveAudioToProjectAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()),
          Times.Never);
    }

    /// <summary>
    /// Pass 05 Option A1: active project + successful transcribe invokes one project-audio save for the library source id.
    /// </summary>
    [TestMethod]
    public async Task TranscribeAsync_WhenActiveProjectSet_CallsSaveOnceWithLibraryAudioId()
    {
      var tr = new TranscriptionResponse
      {
        Id = "tr-a1-1",
        AudioId = "lib-audio-77",
        Text = "hello",
        Segments = new List<TranscriptionSegment>(),
        Duration = 1.0,
        Created = DateTime.UtcNow
      };
      _mockTranscriptionClient
          .Setup(x => x.TranscribeAudioAsync(It.IsAny<TranscriptionRequest>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
          .ReturnsAsync(tr);
      _mockTranscriptionClient
          .Setup(x => x.ListTranscriptionsAsync(It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
          .ReturnsAsync(new List<TranscriptionResponse> { tr });

      var vm = CreateSut();
      await vm.InitializeAsync(CancellationToken.None);
      vm.SelectedProjectId = "proj-a1";
      vm.SelectedAudioId = "lib-audio-77";
      await vm.TranscribeCommand.ExecuteAsync(null);

      _mockProjectAudioClient.Verify(
          x => x.SaveAudioToProjectAsync("proj-a1", "lib-audio-77", null, It.IsAny<CancellationToken>()),
          Times.Once);
      StringAssert.Contains(vm.AudioPersistenceSemanticsHint, "project audio", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Pass 05 Option A1: save failure is non-blocking; transcribe result remains; hint is honest.
    /// </summary>
    [TestMethod]
    public async Task TranscribeAsync_WhenSaveFails_TranscriptionStillSucceeds_SetsFailedHint()
    {
      var tr = new TranscriptionResponse
      {
        Id = "tr-a1-fail",
        AudioId = "lib-audio-fail",
        Text = "still here",
        Segments = new List<TranscriptionSegment>(),
        Duration = 0.5,
        Created = DateTime.UtcNow
      };
      _mockTranscriptionClient
          .Setup(x => x.TranscribeAudioAsync(It.IsAny<TranscriptionRequest>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
          .ReturnsAsync(tr);
      _mockTranscriptionClient
          .Setup(x => x.ListTranscriptionsAsync(It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
          .ReturnsAsync(new List<TranscriptionResponse> { tr });
      _mockProjectAudioClient
          .Setup(x => x.SaveAudioToProjectAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
          .ThrowsAsync(new InvalidOperationException("save failed"));

      var vm = CreateSut();
      await vm.InitializeAsync(CancellationToken.None);
      vm.SelectedProjectId = "proj-x";
      vm.SelectedAudioId = "lib-audio-fail";
      await vm.TranscribeCommand.ExecuteAsync(null);

      Assert.AreEqual("still here", vm.TranscriptionText);
      Assert.IsFalse(string.IsNullOrWhiteSpace(vm.AudioPersistenceSemanticsHint));
      StringAssert.Contains(vm.AudioPersistenceSemanticsHint, "could not", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Pass 05 C3 Option B: Send to Timeline updates overlay-only semantics hint.
    /// </summary>
    [TestMethod]
    public void SendToTimeline_WhenInvoked_SetsOverlaySemanticsHint()
    {
      var vm = CreateSut();
      vm.SelectedTranscription = new TranscriptionResponse
      {
        Id = "tid-overlay-1",
        Text = "line"
      };

      vm.SendToTimelineCommand.Execute(null);

      Assert.IsFalse(string.IsNullOrWhiteSpace(vm.AudioPersistenceSemanticsHint));
      StringAssert.Contains(vm.AudioPersistenceSemanticsHint, "overlay", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Product trust Pass 01 slice 1: persistent footnote discloses drag/drop batch scope vs Option A transcribe path.
    /// </summary>
    [TestMethod]
    public void PersistenceScopeFootnote_DisclosesDragDropAndProjectCopyScope()
    {
      var vm = CreateSut();

      Assert.IsFalse(string.IsNullOrWhiteSpace(vm.PersistenceScopeFootnote));
      StringAssert.Contains(vm.PersistenceScopeFootnote, "drag", StringComparison.OrdinalIgnoreCase);
      StringAssert.Contains(vm.PersistenceScopeFootnote, "project", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>GAP-045 reload lane: project+audio scope schedules backend list; export uses same DTO as persistence lane.</summary>
    [TestMethod]
    public async Task Rehydrate_WhenProjectAndAudioSet_LoadsFromListTranscriptions_ExportMatchesPlainText()
    {
      var tr = new TranscriptionResponse
      {
        Id = "tr-rehydrate-1",
        Text = string.Empty,
        Created = DateTime.UtcNow,
        Segments = new List<TranscriptionSegment>
        {
          new() { Id = "seg-1", Start = 0, End = 1, Text = "hello" },
        },
      };
      _mockTranscriptionClient
          .Setup(x => x.ListTranscriptionsAsync("aud-rehydrate", "proj-rehydrate", It.IsAny<CancellationToken>()))
          .ReturnsAsync(new List<TranscriptionResponse> { tr });

      var vm = CreateSut();
      await vm.InitializeAsync(CancellationToken.None);
      vm.SelectedProjectId = "proj-rehydrate";
      vm.SelectedAudioId = "aud-rehydrate";
      for (var i = 0; i < 40; i++)
      {
        await Task.Delay(25).ConfigureAwait(false);
        await PumpDispatcherQueueAsync().ConfigureAwait(false);
        if (vm.SelectedTranscription != null && vm.SelectedTranscription.Segments.Count > 0)
          break;
      }

      Assert.IsNotNull(vm.SelectedTranscription);
      Assert.AreEqual(string.Empty, vm.SelectedTranscription.Text ?? string.Empty);
      Assert.AreEqual(1, vm.SelectedTranscription.Segments.Count);
      var exported = TranscriptionExportFormatter.BuildPlainText(vm.SelectedTranscription);
      StringAssert.Contains(exported, "hello", StringComparison.Ordinal);
      _mockTranscriptionClient.Verify(
          x => x.ListTranscriptionsAsync("aud-rehydrate", "proj-rehydrate", It.IsAny<CancellationToken>()),
          Times.AtLeastOnce);
    }

    /// <summary>GAP-045 reload lane: explicit load surfaces diagnostic when prior selection id is absent from backend list.</summary>
    [TestMethod]
    public async Task LoadTranscriptions_WhenPriorSelectionNotReturned_SetsOperatorDiagnostic()
    {
      var trA = new TranscriptionResponse
      {
        Id = "id-a",
        Text = "first",
        Created = DateTime.UtcNow,
        Segments = new List<TranscriptionSegment>(),
      };
      var trB = new TranscriptionResponse
      {
        Id = "id-b",
        Text = "only-b",
        Created = DateTime.UtcNow,
        Segments = new List<TranscriptionSegment>(),
      };

      _mockTranscriptionClient
          .Setup(x => x.ListTranscriptionsAsync("ax", "px", It.IsAny<CancellationToken>()))
          .ReturnsAsync(new List<TranscriptionResponse> { trA });

      var vm = CreateSut();
      await vm.InitializeAsync(CancellationToken.None);
      vm.SelectedProjectId = "px";
      vm.SelectedAudioId = "ax";
      for (var i = 0; i < 40; i++)
      {
        await Task.Delay(25).ConfigureAwait(false);
        await PumpDispatcherQueueAsync().ConfigureAwait(false);
        if (vm.SelectedTranscription != null)
          break;
      }

      Assert.IsNotNull(vm.SelectedTranscription);
      Assert.AreEqual("id-a", vm.SelectedTranscription.Id);

      _mockTranscriptionClient
          .Setup(x => x.ListTranscriptionsAsync("ax", "px", It.IsAny<CancellationToken>()))
          .ReturnsAsync(new List<TranscriptionResponse> { trB });

      await vm.LoadTranscriptionsCommand.ExecuteAsync(null);

      Assert.IsFalse(string.IsNullOrWhiteSpace(vm.TranscriptOperatorMessage));
      StringAssert.Contains(vm.TranscriptOperatorMessage, "not in the backend list", StringComparison.OrdinalIgnoreCase);
      Assert.AreEqual("id-b", vm.SelectedTranscription?.Id);
    }

    /// <summary>
    /// GAP-047 seam: list reload must replace the selected row with backend truth, not retain stale local-only segment text
    /// (models post-apply authoritative transcript after filler cleanup Apply).
    /// </summary>
    [TestMethod]
    public async Task RehydrateAfterAppliedCleanup_UsesAuthoritativeText_NotDraftState()
    {
      const string tid = "tr-clean-1";
      var staleLocal = new TranscriptionResponse
      {
        Id = tid,
        Text = string.Empty,
        Created = DateTime.UtcNow,
        Segments = new List<TranscriptionSegment>
        {
          new() { Id = "seg-1", Start = 0, End = 1, Text = "stale draft-only view" },
        },
      };
      var authoritative = new TranscriptionResponse
      {
        Id = tid,
        Text = string.Empty,
        Created = DateTime.UtcNow,
        Segments = new List<TranscriptionSegment>
        {
          new() { Id = "seg-1", Start = 0, End = 1, Text = "authoritative after apply cleanup" },
        },
      };

      _mockTranscriptionClient
          .Setup(x => x.ListTranscriptionsAsync("aud-c", "proj-c", It.IsAny<CancellationToken>()))
          .ReturnsAsync(new List<TranscriptionResponse> { authoritative });

      var vm = CreateSut();
      await vm.InitializeAsync(CancellationToken.None);
      vm.SelectedProjectId = "proj-c";
      vm.SelectedAudioId = "aud-c";
      vm.Transcriptions.Clear();
      vm.Transcriptions.Add(staleLocal);
      vm.SelectedTranscription = staleLocal;

      await vm.LoadTranscriptionsCommand.ExecuteAsync(null);

      for (var i = 0; i < 40; i++)
      {
        await Task.Delay(25).ConfigureAwait(false);
        await PumpDispatcherQueueAsync().ConfigureAwait(false);
        if (vm.SelectedTranscription?.Segments is { Count: > 0 } segs
            && string.Equals(segs[0].Text, "authoritative after apply cleanup", StringComparison.Ordinal))
        {
          break;
        }
      }

      Assert.IsNotNull(vm.SelectedTranscription);
      Assert.AreEqual(1, vm.SelectedTranscription.Segments.Count);
      Assert.AreEqual("authoritative after apply cleanup", vm.SelectedTranscription.Segments[0].Text);
    }

    /// <summary>
    /// GAP-047 range parity: list reload replaces stale multi-segment local row with backend authoritative merged shape.
    /// </summary>
    [TestMethod]
    public async Task RangeApply_Rehydrate_UsesAuthoritativeBackendTruth()
    {
      const string tid = "tr-range-rehydrate-1";
      var staleLocal = new TranscriptionResponse
      {
        Id = tid,
        Text = string.Empty,
        Created = DateTime.UtcNow,
        Segments = new List<TranscriptionSegment>
        {
          new() { Id = "r1", Start = 0, End = 1, Text = "stale range a" },
          new() { Id = "r2", Start = 1, End = 2, Text = "stale range b" },
          new() { Id = "r3", Start = 2, End = 3, Text = "stale range c" },
        },
      };
      var authoritative = new TranscriptionResponse
      {
        Id = tid,
        Text = string.Empty,
        Created = DateTime.UtcNow,
        Segments = new List<TranscriptionSegment>
        {
          new() { Id = "r1", Start = 0, End = 1, Text = "authoritative merged range" },
          new() { Id = "r2", Start = 1, End = 2, Text = string.Empty },
          new() { Id = "r3", Start = 2, End = 3, Text = string.Empty },
        },
      };

      _mockTranscriptionClient
          .Setup(x => x.ListTranscriptionsAsync("aud-r", "proj-r", It.IsAny<CancellationToken>()))
          .ReturnsAsync(new List<TranscriptionResponse> { authoritative });

      var vm = CreateSut();
      await vm.InitializeAsync(CancellationToken.None);
      vm.SelectedProjectId = "proj-r";
      vm.SelectedAudioId = "aud-r";
      vm.Transcriptions.Clear();
      vm.Transcriptions.Add(staleLocal);
      vm.SelectedTranscription = staleLocal;

      await vm.LoadTranscriptionsCommand.ExecuteAsync(null);

      for (var i = 0; i < 40; i++)
      {
        await Task.Delay(25).ConfigureAwait(false);
        await PumpDispatcherQueueAsync().ConfigureAwait(false);
        if (vm.SelectedTranscription?.Segments is { Count: >= 3 } segs
            && string.Equals(segs[0].Text, "authoritative merged range", StringComparison.Ordinal))
        {
          break;
        }
      }

      Assert.IsNotNull(vm.SelectedTranscription);
      Assert.AreEqual(3, vm.SelectedTranscription.Segments.Count);
      Assert.AreEqual("authoritative merged range", vm.SelectedTranscription.Segments[0].Text);
      Assert.AreEqual(string.Empty, vm.SelectedTranscription.Segments[1].Text);
      Assert.AreEqual(string.Empty, vm.SelectedTranscription.Segments[2].Text);
    }
  }
}
