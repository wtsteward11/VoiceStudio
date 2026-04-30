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
      DispatcherQueueTestHelpers.ShutdownSyncBounded(_dispatcherController);
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
    public void AdvancedTranscribeOptionsExpanded_TogglesForDisclosureState()
    {
      var vm = CreateSut();
      Assert.IsFalse(vm.IsAdvancedTranscribeOptionsExpanded);
      vm.IsAdvancedTranscribeOptionsExpanded = true;
      Assert.IsTrue(vm.IsAdvancedTranscribeOptionsExpanded);
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
    /// Source audio import: playback id from library upload (same as ImportWorkflowService
    /// after GetPlaybackAudioId) must flow into durable transcription job request.
    /// </summary>
    [TestMethod]
    public async Task ImportWorkflow_UploadedAsset_AudioId_FeedsTranscriptionJob()
    {
      TranscriptionJobRequest? captured = null;
      _mockTranscriptionClient
          .Setup(x => x.StartTranscriptionJobAsync(It.IsAny<TranscriptionJobRequest>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
          .Callback<TranscriptionJobRequest, string?, CancellationToken>((req, _, _) => captured = req)
          .ReturnsAsync(
              new TranscriptionJobResponse
              {
                JobId = "j-import",
                Status = "pending",
                Mode = "real",
              });

      var vm = CreateSut();
      await vm.InitializeAsync(CancellationToken.None);

      var agg = AppServices.TryGetEventAggregator();
      Assert.IsNotNull(agg);
      agg.Publish(new AssetAddedEvent("import-workflow", "lib-file-uuid-1", "audio", @"C:\src.wav"));
      await PumpDispatcherQueueAsync();

      Assert.AreEqual("lib-file-uuid-1", vm.SelectedAudioId);

      await vm.StartJobCommand.ExecuteAsync(null);
      await PumpDispatcherQueueAsync().ConfigureAwait(false);

      Assert.IsNotNull(captured);
      Assert.AreEqual("lib-file-uuid-1", captured.AudioId);
      Assert.IsTrue(captured.AsyncMode);
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

    /// <summary>
    /// GAP-047 undo lane: list reload reflects authoritative backend after local segment drift (paired with
    /// <see cref="TranscribeViewModelInlineEditTests.ApplyUndoRehydrate_UsesAuthoritativeBackendTruth"/> coordinator path).
    /// </summary>
    [TestMethod]
    public async Task ApplyUndoRehydrate_UsesAuthoritativeBackendTruth()
    {
      const string tid = "tr-apply-undo-rehyd-seam";
      var staleLocal = new TranscriptionResponse
      {
        Id = tid,
        Text = string.Empty,
        Created = DateTime.UtcNow,
        Segments = new List<TranscriptionSegment>
        {
          new() { Id = "seg-u1", Start = 0, End = 1, Text = "local after apply drift" },
        },
      };
      var authoritative = new TranscriptionResponse
      {
        Id = tid,
        Text = string.Empty,
        Created = DateTime.UtcNow,
        Segments = new List<TranscriptionSegment>
        {
          new() { Id = "seg-u1", Start = 0, End = 1, Text = "post-undo backend truth" },
        },
      };

      _mockTranscriptionClient
          .Setup(x => x.ListTranscriptionsAsync("aud-u", "proj-u", It.IsAny<CancellationToken>()))
          .ReturnsAsync(new List<TranscriptionResponse> { authoritative });

      var vm = CreateSut();
      await vm.InitializeAsync(CancellationToken.None);
      vm.SelectedProjectId = "proj-u";
      vm.SelectedAudioId = "aud-u";
      vm.Transcriptions.Clear();
      vm.Transcriptions.Add(staleLocal);
      vm.SelectedTranscription = staleLocal;

      await vm.LoadTranscriptionsCommand.ExecuteAsync(null);

      for (var i = 0; i < 40; i++)
      {
        await Task.Delay(25).ConfigureAwait(false);
        await PumpDispatcherQueueAsync().ConfigureAwait(false);
        if (vm.SelectedTranscription?.Segments is { Count: > 0 } segs
            && string.Equals(segs[0].Text, "post-undo backend truth", StringComparison.Ordinal))
        {
          break;
        }
      }

      Assert.IsNotNull(vm.SelectedTranscription);
      Assert.AreEqual(1, vm.SelectedTranscription.Segments.Count);
      Assert.AreEqual("post-undo backend truth", vm.SelectedTranscription.Segments[0].Text);
    }

    /// <summary>
    /// GAP-047 persist recovery: list reload replaces local row with backend truth when apply did not persist
    /// (authoritative transcript unchanged on server).
    /// </summary>
    [TestMethod]
    public async Task FailedApply_Rehydrate_UsesAuthoritativeBackendTruth()
    {
      const string tid = "tr-fail-persist-rehyd";
      var staleLocal = new TranscriptionResponse
      {
        Id = tid,
        Text = string.Empty,
        Created = DateTime.UtcNow,
        Segments = new List<TranscriptionSegment>
        {
          new() { Id = "seg-f1", Start = 0, End = 1, Text = "local drift after failed persist" },
        },
      };
      var authoritative = new TranscriptionResponse
      {
        Id = tid,
        Text = string.Empty,
        Created = DateTime.UtcNow,
        Segments = new List<TranscriptionSegment>
        {
          new() { Id = "seg-f1", Start = 0, End = 1, Text = "backend never received edit" },
        },
      };

      _mockTranscriptionClient
          .Setup(x => x.ListTranscriptionsAsync("aud-fp", "proj-fp", It.IsAny<CancellationToken>()))
          .ReturnsAsync(new List<TranscriptionResponse> { authoritative });

      var vm = CreateSut();
      await vm.InitializeAsync(CancellationToken.None);
      vm.SelectedProjectId = "proj-fp";
      vm.SelectedAudioId = "aud-fp";
      vm.Transcriptions.Clear();
      vm.Transcriptions.Add(staleLocal);
      vm.SelectedTranscription = staleLocal;

      await vm.LoadTranscriptionsCommand.ExecuteAsync(null);

      for (var i = 0; i < 40; i++)
      {
        await Task.Delay(25).ConfigureAwait(false);
        await PumpDispatcherQueueAsync().ConfigureAwait(false);
        if (vm.SelectedTranscription?.Segments is { Count: > 0 } segs
            && string.Equals(segs[0].Text, "backend never received edit", StringComparison.Ordinal))
        {
          break;
        }
      }

      Assert.IsNotNull(vm.SelectedTranscription);
      Assert.AreEqual(1, vm.SelectedTranscription.Segments.Count);
      Assert.AreEqual("backend never received edit", vm.SelectedTranscription.Segments[0].Text);
    }

    [TestMethod]
    public async Task StartJobCommand_SendsAudioId()
    {
      TranscriptionJobRequest? captured = null;
      _mockTranscriptionClient
          .Setup(x => x.StartTranscriptionJobAsync(It.IsAny<TranscriptionJobRequest>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
          .Callback<TranscriptionJobRequest, string?, CancellationToken>((req, _, _) => captured = req)
          .ReturnsAsync(
              new TranscriptionJobResponse
              {
                JobId = "j1",
                Status = "completed",
                Mode = "real",
                IsSimulated = false,
                RealTranscriptionPerformed = true,
                Transcript = new TranscriptionResponse
                {
                  Id = "tr-send",
                  Text = "x",
                  Created = DateTime.UtcNow,
                  Segments = new List<TranscriptionSegment>(),
                },
              });

      var vm = CreateSut();
      await vm.InitializeAsync(CancellationToken.None);
      vm.SelectedAudioId = "audio-job-send";
      await vm.StartJobCommand.ExecuteAsync(null);
      await PumpDispatcherQueueAsync().ConfigureAwait(false);

      Assert.IsNotNull(captured);
      Assert.AreEqual("audio-job-send", captured.AudioId);
      Assert.IsTrue(captured.AsyncMode);
    }

    [TestMethod]
    public async Task StartJob_RealCompleted_StoresTranscript()
    {
      var tr = new TranscriptionResponse
      {
        Id = "tr-real-job",
        Text = "hello",
        Created = DateTime.UtcNow,
        Segments = new List<TranscriptionSegment>(),
      };
      _mockTranscriptionClient
          .Setup(x => x.StartTranscriptionJobAsync(It.IsAny<TranscriptionJobRequest>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
          .ReturnsAsync(
              new TranscriptionJobResponse
              {
                JobId = "j-real",
                Status = "completed",
                Mode = "real",
                IsSimulated = false,
                RealTranscriptionPerformed = true,
                Transcript = tr,
              });
      _mockTranscriptionClient
          .Setup(x => x.ListTranscriptionsAsync("aud-real", It.IsAny<string?>(), It.IsAny<CancellationToken>()))
          .ReturnsAsync(new List<TranscriptionResponse> { tr });

      var vm = CreateSut();
      await vm.InitializeAsync(CancellationToken.None);
      vm.SelectedAudioId = "aud-real";
      await vm.StartJobCommand.ExecuteAsync(null);
      await PumpDispatcherQueueAsync().ConfigureAwait(false);

      Assert.IsNotNull(vm.SelectedTranscription);
      Assert.AreEqual("tr-real-job", vm.SelectedTranscription.Id);
    }

    [TestMethod]
    public async Task StartJob_SimulatedCompleted_StoresTranscriptAndSetsFlag()
    {
      var tr = new TranscriptionResponse
      {
        Id = "tr-sim",
        Text = "sim",
        Created = DateTime.UtcNow,
        Segments = new List<TranscriptionSegment>(),
      };
      _mockTranscriptionClient
          .Setup(x => x.StartTranscriptionJobAsync(It.IsAny<TranscriptionJobRequest>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
          .ReturnsAsync(
              new TranscriptionJobResponse
              {
                JobId = "j-sim",
                Status = "completed",
                Mode = "simulation",
                IsSimulated = true,
                RealTranscriptionPerformed = false,
                Transcript = tr,
              });
      _mockTranscriptionClient
          .Setup(x => x.ListTranscriptionsAsync("aud-sim", It.IsAny<string?>(), It.IsAny<CancellationToken>()))
          .ReturnsAsync(new List<TranscriptionResponse> { tr });

      var vm = CreateSut();
      await vm.InitializeAsync(CancellationToken.None);
      vm.SelectedAudioId = "aud-sim";
      await vm.StartJobCommand.ExecuteAsync(null);
      await PumpDispatcherQueueAsync().ConfigureAwait(false);

      Assert.IsNotNull(vm.SelectedTranscription);
      Assert.IsTrue(vm.LastTranscriptionWasSimulated);
    }

    [TestMethod]
    public async Task StartJob_Unavailable_DoesNotSetSelectedTranscription()
    {
      _mockTranscriptionClient
          .Setup(x => x.StartTranscriptionJobAsync(It.IsAny<TranscriptionJobRequest>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
          .ReturnsAsync(
              new TranscriptionJobResponse
              {
                JobId = "j-u",
                Status = "unavailable",
                Mode = "unavailable",
                Blocker = "blocked",
              });

      var vm = CreateSut();
      await vm.InitializeAsync(CancellationToken.None);
      vm.SelectedAudioId = "aud-u";
      vm.SelectedTranscription = null;
      await vm.StartJobCommand.ExecuteAsync(null);
      await PumpDispatcherQueueAsync().ConfigureAwait(false);

      Assert.IsNull(vm.SelectedTranscription);
    }

    [TestMethod]
    public async Task StartJob_Failed_ExposesBlocker()
    {
      _mockTranscriptionClient
          .Setup(x => x.StartTranscriptionJobAsync(It.IsAny<TranscriptionJobRequest>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
          .ReturnsAsync(
              new TranscriptionJobResponse
              {
                JobId = "j-f",
                Status = "failed",
                Mode = "real",
                Blocker = "failure-detail",
              });

      var vm = CreateSut();
      await vm.InitializeAsync(CancellationToken.None);
      vm.SelectedAudioId = "aud-f";
      await vm.StartJobCommand.ExecuteAsync(null);
      await PumpDispatcherQueueAsync().ConfigureAwait(false);

      Assert.IsFalse(string.IsNullOrEmpty(vm.ErrorMessage));
      StringAssert.Contains(vm.ErrorMessage, "failure-detail");
    }

    [TestMethod]
    public async Task StartJob_EmptyAudioId_BlocksBeforeClientCall()
    {
      var vm = CreateSut();
      await vm.InitializeAsync(CancellationToken.None);
      vm.SelectedAudioId = "   ";
      await vm.StartJobCommand.ExecuteAsync(null);
      await PumpDispatcherQueueAsync().ConfigureAwait(false);

      _mockTranscriptionClient.Verify(
          x => x.StartTranscriptionJobAsync(It.IsAny<TranscriptionJobRequest>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()),
          Times.Never);
      Assert.IsFalse(string.IsNullOrEmpty(vm.ErrorMessage));
    }

    [TestMethod]
    public async Task StartJob_UnavailableIsNotSuccess_NoSuccessEventEmitted()
    {
      var completedCount = 0;
      var agg = AppServices.TryGetEventAggregator();
      Assert.IsNotNull(agg);
      using var sub = agg.Subscribe<TranscriptionCompletedEvent>(_ => completedCount++);

      _mockTranscriptionClient
          .Setup(x => x.StartTranscriptionJobAsync(It.IsAny<TranscriptionJobRequest>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
          .ReturnsAsync(
              new TranscriptionJobResponse
              {
                JobId = "j-ne",
                Status = "unavailable",
                Mode = "unavailable",
                Blocker = "no",
              });

      var vm = CreateSut();
      await vm.InitializeAsync(CancellationToken.None);
      vm.SelectedAudioId = "aud-ne";
      await vm.StartJobCommand.ExecuteAsync(null);
      await PumpDispatcherQueueAsync().ConfigureAwait(false);

      Assert.AreEqual(0, completedCount);
    }

    [TestMethod]
    public async Task StartJob_PendingThenCompleted_SetsSelectedTranscription()
    {
      var tr = new TranscriptionResponse
      {
        Id = "tr-poll",
        AudioId = "aud-poll",
        Text = "polled",
        Created = DateTime.UtcNow,
        Segments = new List<TranscriptionSegment>(),
      };
      _mockTranscriptionClient
          .Setup(x => x.StartTranscriptionJobAsync(It.IsAny<TranscriptionJobRequest>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
          .ReturnsAsync(
              new TranscriptionJobResponse
              {
                JobId = "j-poll",
                Status = "pending",
                Mode = "pending",
                Progress = 0f,
              });
      _mockTranscriptionClient
          .Setup(x => x.GetTranscriptionJobStatusAsync("j-poll", It.IsAny<CancellationToken>()))
          .ReturnsAsync(
              new TranscriptionJobResponse
              {
                JobId = "j-poll",
                Status = "completed",
                Mode = "real",
                TranscriptId = "tr-poll",
                IsSimulated = false,
                RealTranscriptionPerformed = true,
                Progress = 1f,
              });
      _mockTranscriptionClient
          .Setup(x => x.GetTranscriptionAsync("tr-poll", It.IsAny<CancellationToken>()))
          .ReturnsAsync(tr);
      _mockTranscriptionClient
          .Setup(x => x.ListTranscriptionsAsync("aud-poll", It.IsAny<string?>(), It.IsAny<CancellationToken>()))
          .ReturnsAsync(new List<TranscriptionResponse> { tr });

      var vm = CreateSut();
      await vm.InitializeAsync(CancellationToken.None);
      vm.SelectedAudioId = "aud-poll";
      await vm.StartJobCommand.ExecuteAsync(null);
      await PumpDispatcherQueueAsync().ConfigureAwait(false);

      Assert.IsNotNull(vm.SelectedTranscription);
      Assert.AreEqual("tr-poll", vm.SelectedTranscription.Id);
    }

    [TestMethod]
    public async Task StartJob_PendingThenUnavailable_SetsErrorMessage()
    {
      _mockTranscriptionClient
          .Setup(x => x.StartTranscriptionJobAsync(It.IsAny<TranscriptionJobRequest>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
          .ReturnsAsync(
              new TranscriptionJobResponse
              {
                JobId = "j-pu",
                Status = "pending",
                Mode = "pending",
              });
      _mockTranscriptionClient
          .Setup(x => x.GetTranscriptionJobStatusAsync("j-pu", It.IsAny<CancellationToken>()))
          .ReturnsAsync(
              new TranscriptionJobResponse
              {
                JobId = "j-pu",
                Status = "unavailable",
                Mode = "unavailable",
                Blocker = "engine-off",
              });

      var vm = CreateSut();
      await vm.InitializeAsync(CancellationToken.None);
      vm.SelectedAudioId = "aud-pu";
      await vm.StartJobCommand.ExecuteAsync(null);
      await PumpDispatcherQueueAsync().ConfigureAwait(false);

      StringAssert.Contains(vm.ErrorMessage, "engine-off");
      Assert.IsNull(vm.SelectedTranscription);
    }

    [TestMethod]
    public async Task StartJob_PendingThenFailed_SetsErrorMessage()
    {
      _mockTranscriptionClient
          .Setup(x => x.StartTranscriptionJobAsync(It.IsAny<TranscriptionJobRequest>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
          .ReturnsAsync(
              new TranscriptionJobResponse
              {
                JobId = "j-pf",
                Status = "pending",
                Mode = "pending",
              });
      _mockTranscriptionClient
          .Setup(x => x.GetTranscriptionJobStatusAsync("j-pf", It.IsAny<CancellationToken>()))
          .ReturnsAsync(
              new TranscriptionJobResponse
              {
                JobId = "j-pf",
                Status = "failed",
                Mode = "real",
                Blocker = "missing-audio",
              });

      var vm = CreateSut();
      await vm.InitializeAsync(CancellationToken.None);
      vm.SelectedAudioId = "aud-pf";
      await vm.StartJobCommand.ExecuteAsync(null);
      await PumpDispatcherQueueAsync().ConfigureAwait(false);

      StringAssert.Contains(vm.ErrorMessage, "missing-audio");
      Assert.IsNull(vm.SelectedTranscription);
    }

    [TestMethod]
    public async Task StartJob_PendingThenSimulatedCompleted_SetsLastTranscriptionWasSimulated()
    {
      var tr = new TranscriptionResponse
      {
        Id = "tr-sim-poll",
        AudioId = "aud-sim-poll",
        Text = "sim",
        Created = DateTime.UtcNow,
        Segments = new List<TranscriptionSegment>(),
      };
      _mockTranscriptionClient
          .Setup(x => x.StartTranscriptionJobAsync(It.IsAny<TranscriptionJobRequest>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
          .ReturnsAsync(
              new TranscriptionJobResponse
              {
                JobId = "j-sim-poll",
                Status = "pending",
                Mode = "pending",
              });
      _mockTranscriptionClient
          .Setup(x => x.GetTranscriptionJobStatusAsync("j-sim-poll", It.IsAny<CancellationToken>()))
          .ReturnsAsync(
              new TranscriptionJobResponse
              {
                JobId = "j-sim-poll",
                Status = "completed",
                Mode = "simulation",
                TranscriptId = "tr-sim-poll",
                IsSimulated = true,
                RealTranscriptionPerformed = false,
                Progress = 1f,
              });
      _mockTranscriptionClient
          .Setup(x => x.GetTranscriptionAsync("tr-sim-poll", It.IsAny<CancellationToken>()))
          .ReturnsAsync(tr);
      _mockTranscriptionClient
          .Setup(x => x.ListTranscriptionsAsync("aud-sim-poll", It.IsAny<string?>(), It.IsAny<CancellationToken>()))
          .ReturnsAsync(new List<TranscriptionResponse> { tr });

      var vm = CreateSut();
      await vm.InitializeAsync(CancellationToken.None);
      vm.SelectedAudioId = "aud-sim-poll";
      vm.SimulateTranscription = true;
      await vm.StartJobCommand.ExecuteAsync(null);
      await PumpDispatcherQueueAsync().ConfigureAwait(false);

      Assert.IsTrue(vm.LastTranscriptionWasSimulated);
      Assert.IsNotNull(vm.SelectedTranscription);
    }
  }
}
