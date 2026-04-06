using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.UI.Dispatching;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using VoiceStudio.App.Core.Models;
using VoiceStudio.App.Core.Services;
using VoiceStudio.App.Services;
using VoiceStudio.App.Tests.Fixtures;
using VoiceStudio.Core.Events;
using VoiceStudio.App.Services.UndoableActions;
using VoiceStudio.App.ViewModels;
using VoiceStudio.App.Views.Panels;
using VoiceStudio.Core.Models;
using VoiceStudio.Core.Services;
using VoiceStudio.Core.Transcription;
using JobDto = VoiceStudio.App.Services.Job;

namespace VoiceStudio.App.Tests.ViewModels;

/// <summary>
/// GAP-045 inline transcript edit/apply: buffered segment draft, ReplaceRange intent + regen with replacement text.
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class TranscribeViewModelInlineEditTests
{
  /// <summary>Serializes harness + AppServices mutations for this class vs parallel suites that also touch <see cref="AppServices"/>.</summary>
  private static readonly SemaphoreSlim AppServicesHarnessGate = new(1, 1);

  private DispatcherQueueController? _dispatcherController;
  private Mock<ITranscriptRegenerationClient>? _regenMock;
  private Mock<ITranscriptionClient>? _overrideVmTranscriptionClientMock;

  [TestInitialize]
  public async Task TestInitializeAsync()
  {
    await AppServicesHarnessGate.WaitAsync().ConfigureAwait(false);
    InstallHarness(jobFails: false);
  }

  [TestCleanup]
  public void TestCleanup()
  {
    _overrideVmTranscriptionClientMock = null;
    try
    {
      if (_dispatcherController != null)
      {
        _dispatcherController.ShutdownQueueAsync().AsTask().GetAwaiter().GetResult();
        _dispatcherController = null;
      }

      TestAppServicesHelper.RebuildDefaultProvider();
    }
    finally
    {
      AppServicesHarnessGate.Release();
    }
  }

  private void InstallHarness(bool jobFails, Project? linkedProject = null, bool transcriptPersistFails = false)
  {
    if (_dispatcherController != null)
    {
      _dispatcherController.ShutdownQueueAsync().AsTask().GetAwaiter().GetResult();
      _dispatcherController = null;
    }

    TestAppServicesHelper.RebuildDefaultProvider();
    _dispatcherController = DispatcherQueueController.CreateOnDedicatedThread();
    var dispatcher = _dispatcherController.DispatcherQueue;
    var context = new ViewModelContext(NullLogger.Instance, dispatcher);
    var gate = new TimelineSelectedProjectGate();
    gate.SetSelectedProject(linkedProject ?? BuildLinkedProject());
    var linkage = new ClipTranscriptLinkageService();
    var resolver = new TranscriptSegmentTargetResolver(gate, linkage);

    _regenMock = new Mock<ITranscriptRegenerationClient>();
    _regenMock
        .Setup(x => x.StartRegenerateSegmentAsync(It.IsAny<RegenerateSegmentStartRequest>(), It.IsAny<CancellationToken>()))
        .ReturnsAsync(new RegenerateSegmentJobStartResponse { JobId = "job-inline", Status = "pending" });

    var jobsMock = new Mock<IJobProgressApiClient>();
    var terminal = jobFails
        ? new JobDto
        {
            Id = "job-inline",
            Status = "failed",
            ErrorMessage = "synthesis failed",
        }
        : new JobDto
        {
            Id = "job-inline",
            Status = "completed",
            ResultId = "audio-new",
            Metadata = new Dictionary<string, object>
            {
                ["audio_url"] = "/new.wav",
                ["duration_seconds"] = 4.2,
            },
        };
    jobsMock.Setup(j => j.GetJobAsync("job-inline", It.IsAny<CancellationToken>())).ReturnsAsync(terminal);

    var backendMock = new Mock<IBackendClient>();
    backendMock
        .Setup(b => b.UpdateClipAsync(
            "p1",
            "t1",
            "c1",
            null,
            null,
            "audio-new",
            "/new.wav",
            4.2,
            null,
            null,
            null,
            null,
            It.IsAny<CancellationToken>()))
        .ReturnsAsync(new AudioClip { Id = "c1", AudioId = "audio-new" });
    backendMock
        .Setup(b => b.UpdateClipAsync(
            "p1",
            "t1",
            "c1",
            null,
            null,
            "audio-old",
            "/old",
            2,
            null,
            null,
            null,
            null,
            It.IsAny<CancellationToken>()))
        .ReturnsAsync(new AudioClip { Id = "c1", AudioId = "audio-old" });

    var coordinationTxMock = new Mock<ITranscriptionClient>();
    if (transcriptPersistFails)
    {
      coordinationTxMock
          .Setup(t => t.UpdateTranscriptionTextAsync(
              It.IsAny<string>(),
              It.IsAny<string>(),
              It.IsAny<List<TranscriptionSegment>>(),
              It.IsAny<CancellationToken>()))
          .ThrowsAsync(new InvalidOperationException("persist unavailable"));
    }
    else
    {
      coordinationTxMock
          .Setup(t => t.UpdateTranscriptionTextAsync(
              It.IsAny<string>(),
              It.IsAny<string>(),
              It.IsAny<List<TranscriptionSegment>>(),
              It.IsAny<CancellationToken>()))
          .ReturnsAsync(
              (string id, string text, List<TranscriptionSegment> segs, CancellationToken _) =>
                  new TranscriptionResponse
                  {
                    Id = id,
                    Text = text,
                    Segments = TranscriptTextUndoPayload.CloneSegmentList(segs),
                  });
    }

    var undoRedo = new UndoRedoService();

    var services = new ServiceCollection();
    services.AddSingleton<IViewModelContext>(context);
    services.AddSingleton<MultiSelectService>();
    services.AddSingleton<IEventAggregator, EventAggregator>();
    services.AddSingleton<IWorkflowCoordinatorService, WorkflowCoordinatorService>();
    services.AddSingleton<ITimelineSelectedProjectGate>(gate);
    services.AddSingleton<IClipTranscriptLinkageService>(linkage);
    services.AddSingleton<ITranscriptSegmentTargetResolver>(resolver);
    services.AddSingleton<ITranscriptEditIntentService>(sp => new TranscriptEditIntentService(
        sp.GetRequiredService<ITranscriptSegmentTargetResolver>(),
        sp.GetRequiredService<ITimelineSelectedProjectGate>()));
    services.AddSingleton(undoRedo);
    services.AddSingleton(sp => new TranscriptSegmentRegenerationCoordinator(
        _regenMock.Object,
        jobsMock.Object,
        backendMock.Object,
        linkage,
        gate,
        resolver,
        null,
        undoRedo,
        sp.GetRequiredService<IEventAggregator>(),
        null,
        coordinationTxMock.Object));
    services.AddSingleton<TranscriptEditHistoryService>();
    AppServices.Initialize(services.BuildServiceProvider());
  }

  /// <summary>First regen job fails; second succeeds — GOV-VOICESTUDIO-EDIT-APPLY-RETRY-RECOVERY-01.</summary>
  private void InstallRetryHarness(Project? linkedProject = null)
  {
    if (_dispatcherController != null)
    {
      _dispatcherController.ShutdownQueueAsync().AsTask().GetAwaiter().GetResult();
      _dispatcherController = null;
    }

    TestAppServicesHelper.RebuildDefaultProvider();
    _dispatcherController = DispatcherQueueController.CreateOnDedicatedThread();
    var dispatcher = _dispatcherController.DispatcherQueue;
    var context = new ViewModelContext(NullLogger.Instance, dispatcher);
    var gate = new TimelineSelectedProjectGate();
    gate.SetSelectedProject(linkedProject ?? BuildLinkedProject());
    var linkage = new ClipTranscriptLinkageService();
    var resolver = new TranscriptSegmentTargetResolver(gate, linkage);

    _regenMock = new Mock<ITranscriptRegenerationClient>();
    var jobIds = new Queue<string>(new[] { "job-inline-a", "job-inline-b" });
    _regenMock
        .Setup(x => x.StartRegenerateSegmentAsync(It.IsAny<RegenerateSegmentStartRequest>(), It.IsAny<CancellationToken>()))
        .ReturnsAsync(() =>
        {
          var id = jobIds.Count > 0 ? jobIds.Dequeue() : "job-inline-extra";
          return new RegenerateSegmentJobStartResponse { JobId = id, Status = "pending" };
        });

    var jobsMock = new Mock<IJobProgressApiClient>();
    var failed = new JobDto
    {
      Id = "job-inline-a",
      Status = "failed",
      ErrorMessage = "synthesis failed",
    };
    var success = new JobDto
    {
      Id = "job-inline-b",
      Status = "completed",
      ResultId = "audio-new",
      Metadata = new Dictionary<string, object>
      {
        ["audio_url"] = "/new.wav",
        ["duration_seconds"] = 4.2,
      },
    };
    jobsMock
        .Setup(j => j.GetJobAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
        .ReturnsAsync((string id, CancellationToken _) =>
            string.Equals(id, "job-inline-a", StringComparison.Ordinal) ? failed : success);

    var backendMock = new Mock<IBackendClient>();
    backendMock
        .Setup(b => b.UpdateClipAsync(
            "p1",
            "t1",
            "c1",
            null,
            null,
            "audio-new",
            "/new.wav",
            4.2,
            null,
            null,
            null,
            null,
            It.IsAny<CancellationToken>()))
        .ReturnsAsync(new AudioClip { Id = "c1", AudioId = "audio-new" });
    backendMock
        .Setup(b => b.UpdateClipAsync(
            "p1",
            "t1",
            "c1",
            null,
            null,
            "audio-old",
            "/old",
            2,
            null,
            null,
            null,
            null,
            It.IsAny<CancellationToken>()))
        .ReturnsAsync(new AudioClip { Id = "c1", AudioId = "audio-old" });

    var coordinationTxMock = new Mock<ITranscriptionClient>();
    coordinationTxMock
        .Setup(t => t.UpdateTranscriptionTextAsync(
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<List<TranscriptionSegment>>(),
            It.IsAny<CancellationToken>()))
        .ReturnsAsync(
            (string id, string text, List<TranscriptionSegment> segs, CancellationToken _) =>
                new TranscriptionResponse
                {
                  Id = id,
                  Text = text,
                  Segments = TranscriptTextUndoPayload.CloneSegmentList(segs),
                });

    var undoRedo = new UndoRedoService();

    var services = new ServiceCollection();
    services.AddSingleton<IViewModelContext>(context);
    services.AddSingleton<MultiSelectService>();
    services.AddSingleton<IEventAggregator, EventAggregator>();
    services.AddSingleton<IWorkflowCoordinatorService, WorkflowCoordinatorService>();
    services.AddSingleton<ITimelineSelectedProjectGate>(gate);
    services.AddSingleton<IClipTranscriptLinkageService>(linkage);
    services.AddSingleton<ITranscriptSegmentTargetResolver>(resolver);
    services.AddSingleton<ITranscriptEditIntentService>(sp => new TranscriptEditIntentService(
        sp.GetRequiredService<ITranscriptSegmentTargetResolver>(),
        sp.GetRequiredService<ITimelineSelectedProjectGate>()));
    services.AddSingleton(undoRedo);
    services.AddSingleton(sp => new TranscriptSegmentRegenerationCoordinator(
        _regenMock.Object,
        jobsMock.Object,
        backendMock.Object,
        linkage,
        gate,
        resolver,
        null,
        undoRedo,
        sp.GetRequiredService<IEventAggregator>(),
        null,
        coordinationTxMock.Object));
    services.AddSingleton<TranscriptEditHistoryService>();
    AppServices.Initialize(services.BuildServiceProvider());
  }

  private static Project BuildLinkedProject()
  {
    var clip = new AudioClip
    {
      Id = "c1",
      Name = "c",
      ProfileId = "prof-1",
      AudioId = "audio-old",
      AudioUrl = "/old",
      Duration = TimeSpan.FromSeconds(2),
      StartTime = 0,
    };
    var track = new AudioTrack
    {
      Id = "t1",
      Name = "t",
      ProjectId = "p1",
      Clips = new List<AudioClip> { clip },
    };
    return new Project
    {
      Id = "p1",
      Name = "p",
      Tracks = new List<AudioTrack> { track },
      ClipTranscriptLinks = new List<ClipTranscriptLink>
      {
        new()
        {
          ClipId = "c1",
          TranscriptionId = "tr1",
          AudioId = "a1",
          SegmentIds = new List<string> { "s9" },
        },
      },
    };
  }

  private static Mock<ITranscriptionClient> CreateTranscriptionClientMock()
  {
    var mock = new Mock<ITranscriptionClient>();
    mock.Setup(x => x.GetTranscriptionEnginesAsync(It.IsAny<CancellationToken>()))
        .ReturnsAsync(new List<TranscriptionEngine>());
    mock.Setup(x => x.GetSupportedLanguagesAsync(It.IsAny<CancellationToken>()))
        .ReturnsAsync(new List<SupportedLanguage>());
    return mock;
  }

  private static Mock<IProjectAudioClient> CreateProjectAudioMock()
  {
    var mock = new Mock<IProjectAudioClient>();
    mock.Setup(x => x.SaveAudioToProjectAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
        .ReturnsAsync(new ProjectAudioFile { Filename = "stub.wav" });
    return mock;
  }

  private TranscribeViewModel CreateSut()
  {
    var context = new ViewModelContext(NullLogger.Instance, _dispatcherController!.DispatcherQueue);
    var tx = _overrideVmTranscriptionClientMock ?? CreateTranscriptionClientMock();
    return new TranscribeViewModel(context, tx.Object, CreateProjectAudioMock().Object);
  }

  private Task PumpDispatcherOnceAsync()
  {
    var dq = _dispatcherController!.DispatcherQueue;
    var tcs = new TaskCompletionSource<bool>();
    _ = dq.TryEnqueue(() => tcs.TrySetResult(true));
    return tcs.Task;
  }

  /// <summary>
  /// Pumps the VM dispatcher until the newest apply job row reaches <see cref="TranscriptApplyOperatorJobStatus.Succeeded"/>
  /// (or failed — asserts). Wall-clock bound drains enqueued progress/finalize under parallel full-suite load.
  /// </summary>
  private async Task PumpUntilApplyJobRowSucceededAsync(TranscribeViewModel vm, int maxWaitMs = 2000)
  {
    var deadline = DateTime.UtcNow.AddMilliseconds(maxWaitMs);
    while (DateTime.UtcNow < deadline)
    {
      if (vm.TranscriptApplyJobStatusEntries.Count > 0)
      {
        var st = vm.TranscriptApplyJobStatusEntries[0].OperatorStatus;
        if (st == TranscriptApplyOperatorJobStatus.Succeeded)
          return;
        if (st == TranscriptApplyOperatorJobStatus.Failed)
        {
          Assert.Fail(
              $"Expected succeeded apply job row; got Failed: {vm.TranscriptApplyJobStatusEntries[0].StatusMessage ?? string.Empty}");
        }
      }

      await PumpDispatcherOnceAsync().ConfigureAwait(false);
    }

    var last = vm.TranscriptApplyJobStatusEntries.Count > 0
        ? vm.TranscriptApplyJobStatusEntries[0].OperatorStatus.ToString()
        : "(no rows)";
    Assert.Fail($"Timeout waiting for apply job row Succeeded (last status {last}).");
  }

  /// <summary>
  /// Drains coordinator finalize/progress onto the VM dispatcher until the newest row is <see cref="TranscriptApplyOperatorJobStatus.Failed"/>.
  /// </summary>
  private async Task PumpUntilApplyJobRowFailedAsync(TranscribeViewModel vm, int maxWaitMs = 2000)
  {
    var deadline = DateTime.UtcNow.AddMilliseconds(maxWaitMs);
    while (DateTime.UtcNow < deadline)
    {
      if (vm.TranscriptApplyJobStatusEntries.Count > 0
          && vm.TranscriptApplyJobStatusEntries[0].OperatorStatus == TranscriptApplyOperatorJobStatus.Failed)
        return;

      await PumpDispatcherOnceAsync().ConfigureAwait(false);
    }

    var last = vm.TranscriptApplyJobStatusEntries.Count > 0
        ? vm.TranscriptApplyJobStatusEntries[0].OperatorStatus.ToString()
        : "(no rows)";
    Assert.Fail($"Timeout waiting for apply job row Failed (last status {last}).");
  }

  private static TranscriptionResponse BuildTranscription()
  {
    return new TranscriptionResponse
    {
      Id = "tr1",
      Segments = new List<TranscriptionSegment>
      {
        new() { Id = "s9", Start = 1, End = 4, Text = "original" },
      },
    };
  }

  /// <summary>Same audio graph as <see cref="BuildLinkedProject"/> but no transcript-to-clip links (resolver Unlinked).</summary>
  private static Project BuildLinkedProjectWithoutTranscriptLinks()
  {
    var clip = new AudioClip
    {
      Id = "c1",
      Name = "c",
      ProfileId = "prof-1",
      AudioId = "audio-old",
      AudioUrl = "/old",
      Duration = TimeSpan.FromSeconds(2),
      StartTime = 0,
    };
    var track = new AudioTrack
    {
      Id = "t1",
      Name = "t",
      ProjectId = "p1",
      Clips = new List<AudioClip> { clip },
    };
    return new Project
    {
      Id = "p1",
      Name = "p",
      Tracks = new List<AudioTrack> { track },
      ClipTranscriptLinks = new List<ClipTranscriptLink>(),
    };
  }

  private static TranscriptApplyJobStatusEntry BuildApplyJobStatusEntry(
      IReadOnlyList<string>? segmentIds = null,
      string transcriptionId = "tr1",
      string? projectId = "p1",
      string? clipId = "c1") =>
      new(
          "op-test",
          TranscriptEditOperationKind.SingleSegmentApply,
          segmentIds ?? new[] { "s9" },
          clipId,
          DateTimeOffset.UtcNow,
          transcriptionId,
          projectId,
          replacementTextSnapshot: null,
          rangeEndInclusiveIndex: null,
          anchorSegmentStart: 1,
          anchorSegmentEnd: 4);

  /// <summary>Two segments linked to different clips — multi-segment range must fail closed.</summary>
  private static Project BuildCrossClipLinkedProject()
  {
    var clip1 = new AudioClip
    {
      Id = "c1",
      Name = "c1",
      ProfileId = "prof-1",
      AudioId = "audio-a",
      AudioUrl = "/a",
      Duration = TimeSpan.FromSeconds(2),
      StartTime = 0,
    };
    var clip2 = new AudioClip
    {
      Id = "c2",
      Name = "c2",
      ProfileId = "prof-1",
      AudioId = "audio-b",
      AudioUrl = "/b",
      Duration = TimeSpan.FromSeconds(2),
      StartTime = 0,
    };
    var track1 = new AudioTrack { Id = "t1", Name = "t1", ProjectId = "p1", Clips = new List<AudioClip> { clip1 } };
    var track2 = new AudioTrack { Id = "t2", Name = "t2", ProjectId = "p1", Clips = new List<AudioClip> { clip2 } };
    return new Project
    {
      Id = "p1",
      Name = "p",
      Tracks = new List<AudioTrack> { track1, track2 },
      ClipTranscriptLinks = new List<ClipTranscriptLink>
      {
        new()
        {
          ClipId = "c1",
          TranscriptionId = "tr1",
          AudioId = "a1",
          SegmentIds = new List<string> { "s1" },
        },
        new()
        {
          ClipId = "c2",
          TranscriptionId = "tr1",
          AudioId = "a2",
          SegmentIds = new List<string> { "s2" },
        },
      },
    };
  }

  private static TranscriptionResponse BuildTranscriptionTwoSegments()
  {
    return new TranscriptionResponse
    {
      Id = "tr1",
      Segments = new List<TranscriptionSegment>
      {
        new() { Id = "s1", Start = 0, End = 1, Text = "one" },
        new() { Id = "s2", Start = 1, End = 2, Text = "two" },
      },
    };
  }

  private static Project BuildLinkedProjectThreeSegmentsOneClip()
  {
    var clip = new AudioClip
    {
      Id = "c1",
      Name = "c",
      ProfileId = "prof-1",
      AudioId = "audio-old",
      AudioUrl = "/old",
      Duration = TimeSpan.FromSeconds(6),
      StartTime = 0,
    };
    var track = new AudioTrack
    {
      Id = "t1",
      Name = "t",
      ProjectId = "p1",
      Clips = new List<AudioClip> { clip },
    };
    return new Project
    {
      Id = "p1",
      Name = "p",
      Tracks = new List<AudioTrack> { track },
      ClipTranscriptLinks = new List<ClipTranscriptLink>
      {
        new()
        {
          ClipId = "c1",
          TranscriptionId = "tr1",
          AudioId = "a1",
          SegmentIds = new List<string> { "s1", "s2", "s3" },
        },
      },
    };
  }

  private static TranscriptionResponse BuildTranscriptionThreeSegments()
  {
    return new TranscriptionResponse
    {
      Id = "tr1",
      Segments = new List<TranscriptionSegment>
      {
        new() { Id = "s1", Start = 0, End = 1, Text = "aa" },
        new() { Id = "s2", Start = 1, End = 2, Text = "bb" },
        new() { Id = "s3", Start = 2, End = 3, Text = "cc" },
      },
    };
  }

  [TestMethod]
  public void BeginEdit_SetsEditingState()
  {
    var vm = CreateSut();
    vm.SelectedTranscription = BuildTranscription();
    var segment = vm.SelectedTranscription.Segments![0];
    vm.BeginEditSegment(segment);

    Assert.AreEqual("s9", vm.EditingSegmentId);
    Assert.AreEqual("original", vm.EditingSegmentOriginalText);
    Assert.AreEqual("original", vm.EditingSegmentDraftText);
    Assert.IsTrue(vm.IsEditingSegment);
    Assert.IsFalse(vm.IsEditDirty);
    StringAssert.Contains(vm.SegmentEditOperatorHint ?? "", "Editing segment text");
  }

  [TestMethod]
  public void CancelEdit_ClearsEditingState()
  {
    var vm = CreateSut();
    vm.SelectedTranscription = BuildTranscription();
    vm.BeginEditSegment(vm.SelectedTranscription.Segments![0]);
    vm.EditingSegmentDraftText = "changed";
    vm.CancelSegmentEdit();

    Assert.IsFalse(vm.IsEditingSegment);
    Assert.IsNull(vm.EditingSegmentId);
    Assert.IsNull(vm.SegmentEditOperatorHint);
  }

  [TestMethod]
  public async Task ApplyEdit_CallsRegenerateWithReplacementText()
  {
    var vm = CreateSut();
    vm.SelectedTranscription = BuildTranscription();
    vm.BeginEditSegment(vm.SelectedTranscription.Segments![0]);
    vm.EditingSegmentDraftText = "new words";

    var err = await vm.ApplyEditedSegmentAsync(CancellationToken.None).ConfigureAwait(false);

    Assert.IsNull(err);
    _regenMock!.Verify(
        x => x.StartRegenerateSegmentAsync(
            It.Is<RegenerateSegmentStartRequest>(r =>
                r.TranscriptionId == "tr1"
                && r.SegmentId == "s9"
                && r.ReplacementText == "new words"),
            It.IsAny<CancellationToken>()),
        Times.Once);
  }

  [TestMethod]
  public async Task ApplyEdit_Success_ClearsEditingState()
  {
    var vm = CreateSut();
    vm.SelectedTranscription = BuildTranscription();
    vm.BeginEditSegment(vm.SelectedTranscription.Segments![0]);
    vm.EditingSegmentDraftText = "new words";

    var err = await vm.ApplyEditedSegmentAsync(CancellationToken.None).ConfigureAwait(false);

    Assert.IsNull(err);
    Assert.IsFalse(vm.IsEditingSegment);
    Assert.IsNull(vm.EditingSegmentId);
  }

  [TestMethod]
  public async Task ApplyEdit_Success_RecordsSucceededApplyJobStatusRow()
  {
    var vm = CreateSut();
    vm.SelectedTranscription = BuildTranscription();
    vm.BeginEditSegment(vm.SelectedTranscription.Segments![0]);
    vm.EditingSegmentDraftText = "new words";

    var err = await vm.ApplyEditedSegmentAsync(CancellationToken.None).ConfigureAwait(false);
    Assert.IsNull(err);

    await PumpUntilApplyJobRowSucceededAsync(vm, maxWaitMs: 20000).ConfigureAwait(false);
    // Finalize + last progress reports are queued on the VM dispatcher; drain until terminal.
    var terminalBy = DateTime.UtcNow.AddSeconds(4);
    while (DateTime.UtcNow < terminalBy
           && vm.TranscriptApplyJobStatusEntries.Count > 0
           && vm.TranscriptApplyJobStatusEntries[0].OperatorStatus != TranscriptApplyOperatorJobStatus.Succeeded)
      await PumpDispatcherOnceAsync().ConfigureAwait(false);

    Assert.IsTrue(vm.TranscriptApplyJobStatusEntries.Count >= 1);
    Assert.AreEqual(TranscriptApplyOperatorJobStatus.Succeeded, vm.TranscriptApplyJobStatusEntries[0].OperatorStatus);
    var msg = vm.TranscriptApplyJobStatusEntries[0].StatusMessage ?? string.Empty;
    Assert.IsTrue(
        msg.Contains("clip", StringComparison.OrdinalIgnoreCase)
        || msg.Contains("timeline", StringComparison.OrdinalIgnoreCase)
        || msg.Contains("Regeneration complete", StringComparison.OrdinalIgnoreCase)
        || msg.Contains("Synthesis complete", StringComparison.OrdinalIgnoreCase),
        $"Expected apply status to mention clip/timeline/regen; got: {msg}");
  }

  [TestMethod]
  public async Task ApplyEdit_Failure_PreservesEditingState()
  {
    InstallHarness(jobFails: true);
    var vm = CreateSut();
    vm.SelectedTranscription = BuildTranscription();
    vm.BeginEditSegment(vm.SelectedTranscription.Segments![0]);
    vm.EditingSegmentDraftText = "new words";

    var err = await vm.ApplyEditedSegmentAsync(CancellationToken.None).ConfigureAwait(false);

    Assert.IsNotNull(err);
    Assert.AreEqual("s9", vm.EditingSegmentId);
    Assert.AreEqual("new words", vm.EditingSegmentDraftText);

    Assert.IsTrue(vm.TranscriptApplyJobStatusEntries.Count >= 1);
    await PumpUntilApplyJobRowFailedAsync(vm, maxWaitMs: 20000).ConfigureAwait(false);
    var failTerminalBy = DateTime.UtcNow.AddSeconds(4);
    while (DateTime.UtcNow < failTerminalBy
           && vm.TranscriptApplyJobStatusEntries.Count > 0
           && vm.TranscriptApplyJobStatusEntries[0].OperatorStatus != TranscriptApplyOperatorJobStatus.Failed)
      await PumpDispatcherOnceAsync().ConfigureAwait(false);

    Assert.AreEqual(TranscriptApplyOperatorJobStatus.Failed, vm.TranscriptApplyJobStatusEntries[0].OperatorStatus);
  }

  [TestMethod]
  public async Task ApplyEdit_Failure_ThenRetry_ReplaysSnapshotReplacementText()
  {
    InstallRetryHarness();
    var vm = CreateSut();
    vm.SelectedProjectId = "p1";
    vm.SelectedTranscription = BuildTranscription();
    vm.BeginEditSegment(vm.SelectedTranscription.Segments![0]);
    vm.EditingSegmentDraftText = "new words";

    var err = await vm.ApplyEditedSegmentAsync(CancellationToken.None).ConfigureAwait(false);
    Assert.IsNotNull(err);
    await PumpDispatcherOnceAsync().ConfigureAwait(false);
    await PumpDispatcherOnceAsync().ConfigureAwait(false);

    var failedEntry = vm.TranscriptApplyJobStatusEntries[0];
    Assert.AreEqual(TranscriptApplyOperatorJobStatus.Failed, failedEntry.OperatorStatus);
    Assert.IsTrue(failedEntry.CanShowRetry);
    Assert.AreEqual("new words", failedEntry.ReplacementTextSnapshot);

    vm.EditingSegmentDraftText = "changed draft after failure";

    await vm.RetryTranscriptApplyJobAsync(failedEntry, CancellationToken.None).ConfigureAwait(false);
    await PumpDispatcherOnceAsync().ConfigureAwait(false);
    await PumpDispatcherOnceAsync().ConfigureAwait(false);

    _regenMock!.Verify(
        x => x.StartRegenerateSegmentAsync(
            It.Is<RegenerateSegmentStartRequest>(r =>
                r.ReplacementText == "new words" && r.SegmentId == "s9"),
            It.IsAny<CancellationToken>()),
        Times.Exactly(2));

    Assert.IsTrue(vm.TranscriptApplyJobStatusEntries.Count >= 2);
    Assert.AreEqual(TranscriptApplyOperatorJobStatus.Succeeded, vm.TranscriptApplyJobStatusEntries[0].OperatorStatus);
    Assert.AreEqual(TranscriptApplyOperatorJobStatus.Failed, vm.TranscriptApplyJobStatusEntries[1].OperatorStatus);
  }

  [TestMethod]
  public async Task ApplyEdit_Failure_Retry_WhenSegmentTimingChanged_DoesNotCallRegenerateAgain()
  {
    InstallRetryHarness();
    var vm = CreateSut();
    vm.SelectedProjectId = "p1";
    vm.SelectedTranscription = BuildTranscription();
    vm.BeginEditSegment(vm.SelectedTranscription.Segments![0]);
    vm.EditingSegmentDraftText = "new words";

    _ = await vm.ApplyEditedSegmentAsync(CancellationToken.None).ConfigureAwait(false);
    await PumpDispatcherOnceAsync().ConfigureAwait(false);

    var failedEntry = vm.TranscriptApplyJobStatusEntries[0];
    vm.SelectedTranscription.Segments![0].Start = 99;

    await vm.RetryTranscriptApplyJobAsync(failedEntry, CancellationToken.None).ConfigureAwait(false);

    _regenMock!.Verify(
        x => x.StartRegenerateSegmentAsync(It.IsAny<RegenerateSegmentStartRequest>(), It.IsAny<CancellationToken>()),
        Times.Once);
  }

  [TestMethod]
  public async Task ApplyEdit_Failure_Retry_WhenTranscriptionIdMismatch_DoesNotCallRegenerateAgain()
  {
    InstallRetryHarness();
    var vm = CreateSut();
    vm.SelectedProjectId = "p1";
    vm.SelectedTranscription = BuildTranscription();
    vm.BeginEditSegment(vm.SelectedTranscription.Segments![0]);
    vm.EditingSegmentDraftText = "new words";

    _ = await vm.ApplyEditedSegmentAsync(CancellationToken.None).ConfigureAwait(false);
    await PumpDispatcherOnceAsync().ConfigureAwait(false);

    var failedEntry = vm.TranscriptApplyJobStatusEntries[0];
    vm.SelectedTranscription = new TranscriptionResponse
    {
      Id = "other",
      Segments = new List<TranscriptionSegment>
      {
        new() { Id = "s9", Start = 1, End = 4, Text = "original" },
      },
    };

    await vm.RetryTranscriptApplyJobAsync(failedEntry, CancellationToken.None).ConfigureAwait(false);

    _regenMock!.Verify(
        x => x.StartRegenerateSegmentAsync(It.IsAny<RegenerateSegmentStartRequest>(), It.IsAny<CancellationToken>()),
        Times.Once);
  }

  [TestMethod]
  public async Task ApplyEdit_EmptyText_Rejected()
  {
    var vm = CreateSut();
    vm.SelectedTranscription = BuildTranscription();
    vm.BeginEditSegment(vm.SelectedTranscription.Segments![0]);
    vm.EditingSegmentDraftText = "   ";

    var err = await vm.ApplyEditedSegmentAsync(CancellationToken.None).ConfigureAwait(false);

    StringAssert.Contains(err ?? "", "empty");
    Assert.AreEqual("s9", vm.EditingSegmentId);
    _regenMock!.Verify(
        x => x.StartRegenerateSegmentAsync(It.IsAny<RegenerateSegmentStartRequest>(), It.IsAny<CancellationToken>()),
        Times.Never);
  }

  [TestMethod]
  public void IsEditDirty_WhenDraftDiffers_True()
  {
    var vm = CreateSut();
    vm.SelectedTranscription = BuildTranscription();
    vm.BeginEditSegment(vm.SelectedTranscription.Segments![0]);
    vm.EditingSegmentDraftText = "changed";

    Assert.IsTrue(vm.IsEditDirty);
    StringAssert.Contains(vm.SegmentEditOperatorHint ?? "", "edited");
  }

  [TestMethod]
  public void IsEditDirty_WhenDraftMatchesOriginal_False()
  {
    var vm = CreateSut();
    vm.SelectedTranscription = BuildTranscription();
    vm.BeginEditSegment(vm.SelectedTranscription.Segments![0]);

    Assert.IsFalse(vm.IsEditDirty);
  }

  [TestMethod]
  public async Task ApplyEdit_Success_UpdatesSegmentTextAndMarksRegenerated()
  {
    var vm = CreateSut();
    vm.SelectedTranscription = BuildTranscription();
    vm.BeginEditSegment(vm.SelectedTranscription.Segments![0]);
    vm.EditingSegmentDraftText = "new words";

    var err = await vm.ApplyEditedSegmentAsync(CancellationToken.None).ConfigureAwait(false);

    Assert.IsNull(err);
    Assert.AreEqual("new words", vm.SelectedTranscription!.Segments![0].Text);
    Assert.IsTrue(vm.WasSegmentRegeneratedInSession("s9"));
    Assert.IsTrue(string.IsNullOrEmpty(vm.RegeneratingSegmentId));
  }

  [TestMethod]
  public async Task ApplyEdit_Failure_ClearsRegeneratingSegmentId()
  {
    InstallHarness(jobFails: true);
    var vm = CreateSut();
    vm.SelectedTranscription = BuildTranscription();
    vm.BeginEditSegment(vm.SelectedTranscription.Segments![0]);
    vm.EditingSegmentDraftText = "new words";

    var err = await vm.ApplyEditedSegmentAsync(CancellationToken.None).ConfigureAwait(false);

    Assert.IsNotNull(err);
    Assert.IsTrue(string.IsNullOrEmpty(vm.RegeneratingSegmentId));
    Assert.IsFalse(vm.WasSegmentRegeneratedInSession("s9"));
    Assert.AreEqual("original", vm.SelectedTranscription!.Segments![0].Text);
  }

  [TestMethod]
  public void ApplyEditedSegmentCommand_CannotExecuteWhenRegeneratingSegmentIdSet()
  {
    var vm = CreateSut();
    vm.SelectedTranscription = BuildTranscription();
    vm.BeginEditSegment(vm.SelectedTranscription.Segments![0]);
    vm.EditingSegmentDraftText = "edited";

    Assert.IsTrue(vm.ApplyEditedSegmentCommand.CanExecute(null));

    vm.RegeneratingSegmentId = "s9";
    Assert.IsFalse(vm.ApplyEditedSegmentCommand.CanExecute(null));

    vm.RegeneratingSegmentId = null;
    Assert.IsTrue(vm.ApplyEditedSegmentCommand.CanExecute(null));
  }

  [TestMethod]
  public async Task RegenerateSegment_WithoutReplacement_StillMarksRegenerated_TextUnchanged()
  {
    var vm = CreateSut();
    vm.SelectedTranscription = BuildTranscription();
    var segment = vm.SelectedTranscription.Segments![0];

    var err = await vm.RegenerateSegmentAudioAsync(segment, null, CancellationToken.None).ConfigureAwait(false);

    Assert.IsNull(err);
    Assert.AreEqual("original", segment.Text);
    Assert.IsTrue(vm.WasSegmentRegeneratedInSession("s9"));
  }

  [TestMethod]
  public void BeginEditRange_CrossClip_Blocked()
  {
    InstallHarness(jobFails: false, BuildCrossClipLinkedProject());
    var vm = CreateSut();
    var tr = BuildTranscriptionTwoSegments();
    vm.SelectedTranscription = tr;
    var a = tr.Segments![0];
    var b = tr.Segments![1];

    vm.BeginEditRange(a, b);

    Assert.IsFalse(vm.IsEditingSegment);
    StringAssert.Contains(vm.TranscriptOperatorMessage ?? string.Empty, "multiple timeline clips");
  }

  [TestMethod]
  public void BeginEditRange_SameClip_SetsCombinedOriginalAndMultiFlag()
  {
    InstallHarness(jobFails: false, BuildLinkedProjectThreeSegmentsOneClip());
    var vm = CreateSut();
    var tr = BuildTranscriptionThreeSegments();
    vm.SelectedTranscription = tr;

    vm.BeginEditRange(tr.Segments![0], tr.Segments![2]);

    Assert.IsTrue(vm.IsEditingSegment);
    Assert.IsTrue(vm.IsMultiSegmentRangeEdit);
    Assert.AreEqual("s1", vm.EditingSegmentId);
    Assert.AreEqual("s3", vm.EditingRangeEndSegmentId);
    Assert.AreEqual("aa bb cc", vm.EditingSegmentOriginalText);
    Assert.AreEqual("aa bb cc", vm.EditingSegmentDraftText);
  }

  [TestMethod]
  public async Task ApplyEdit_Range_CallsRegenerateWithFirstSegmentAnchor()
  {
    InstallHarness(jobFails: false, BuildLinkedProjectThreeSegmentsOneClip());
    var vm = CreateSut();
    var tr = BuildTranscriptionThreeSegments();
    vm.SelectedTranscription = tr;
    vm.BeginEditRange(tr.Segments![0], tr.Segments![2]);
    vm.EditingSegmentDraftText = "merged words";

    var err = await vm.ApplyEditedSegmentAsync(CancellationToken.None).ConfigureAwait(false);

    Assert.IsNull(err);
    _regenMock!.Verify(
        x => x.StartRegenerateSegmentAsync(
            It.Is<RegenerateSegmentStartRequest>(r =>
                r.TranscriptionId == "tr1"
                && r.SegmentId == "s1"
                && r.ReplacementText == "merged words"),
            It.IsAny<CancellationToken>()),
        Times.Once);
  }

  [TestMethod]
  public async Task ApplyEdit_Range_Success_UpdatesFirstSegment_ClearsOthers()
  {
    InstallHarness(jobFails: false, BuildLinkedProjectThreeSegmentsOneClip());
    var vm = CreateSut();
    var tr = BuildTranscriptionThreeSegments();
    vm.SelectedTranscription = tr;
    vm.BeginEditRange(tr.Segments![0], tr.Segments![2]);
    vm.EditingSegmentDraftText = "merged words";

    var err = await vm.ApplyEditedSegmentAsync(CancellationToken.None).ConfigureAwait(false);

    Assert.IsNull(err);
    Assert.AreEqual("merged words", vm.SelectedTranscription!.Segments![0].Text);
    Assert.AreEqual(string.Empty, vm.SelectedTranscription.Segments![1].Text);
    Assert.AreEqual(string.Empty, vm.SelectedTranscription.Segments![2].Text);
    Assert.IsTrue(vm.WasSegmentRegeneratedInSession("s1"));
    Assert.IsTrue(vm.WasSegmentRegeneratedInSession("s2"));
    Assert.IsTrue(vm.WasSegmentRegeneratedInSession("s3"));
  }

  [TestMethod]
  public async Task ApplyEdit_Range_Failure_PreservesEditingState()
  {
    InstallHarness(jobFails: true, BuildLinkedProjectThreeSegmentsOneClip());
    var vm = CreateSut();
    var tr = BuildTranscriptionThreeSegments();
    vm.SelectedTranscription = tr;
    vm.BeginEditRange(tr.Segments![0], tr.Segments![2]);
    vm.EditingSegmentDraftText = "merged words";

    var err = await vm.ApplyEditedSegmentAsync(CancellationToken.None).ConfigureAwait(false);

    Assert.IsNotNull(err);
    Assert.AreEqual("s1", vm.EditingSegmentId);
    Assert.AreEqual("s3", vm.EditingRangeEndSegmentId);
    Assert.AreEqual("merged words", vm.EditingSegmentDraftText);
    Assert.AreEqual("aa", vm.SelectedTranscription!.Segments![0].Text);
  }

  [TestMethod]
  public void CancelEdit_ClearsRangeEnd()
  {
    InstallHarness(jobFails: false, BuildLinkedProjectThreeSegmentsOneClip());
    var vm = CreateSut();
    var tr = BuildTranscriptionThreeSegments();
    vm.SelectedTranscription = tr;
    vm.BeginEditRange(tr.Segments![0], tr.Segments![2]);
    vm.CancelSegmentEdit();
    Assert.IsNull(vm.EditingRangeEndSegmentId);
    Assert.IsFalse(vm.IsEditingSegment);
  }

  [TestMethod]
  public void RemoveFillersFromDraft_UpdatesDraft_AndMessage()
  {
    var vm = CreateSut();
    vm.SelectedTranscription = BuildTranscription();
    vm.BeginEditSegment(vm.SelectedTranscription.Segments![0]);
    vm.EditingSegmentDraftText = "hello um world";

    var err = vm.TryRemoveFillersFromEditingDraft();

    Assert.IsNull(err);
    Assert.AreEqual("hello world", vm.EditingSegmentDraftText);
    StringAssert.Contains(vm.TranscriptOperatorMessage ?? string.Empty, "Removed");
  }

  [TestMethod]
  public void RemoveFillersFromDraft_RangeMergedText_Works()
  {
    InstallHarness(jobFails: false, BuildLinkedProjectThreeSegmentsOneClip());
    var vm = CreateSut();
    var tr = BuildTranscriptionThreeSegments();
    vm.SelectedTranscription = tr;
    vm.BeginEditRange(tr.Segments![0], tr.Segments![2]);
    vm.EditingSegmentDraftText = "aa uh bb cc";

    var err = vm.TryRemoveFillersFromEditingDraft();

    Assert.IsNull(err);
    Assert.AreEqual("aa bb cc", vm.EditingSegmentDraftText);
  }

  [TestMethod]
  public void RemoveFillersFromDraft_WouldLeaveEmpty_ReturnsError()
  {
    var vm = CreateSut();
    vm.SelectedTranscription = BuildTranscription();
    vm.BeginEditSegment(vm.SelectedTranscription.Segments![0]);
    vm.EditingSegmentDraftText = "um uh";

    var err = vm.TryRemoveFillersFromEditingDraft();

    Assert.IsNotNull(err);
    StringAssert.Contains(err, "all text");
    Assert.AreEqual("um uh", vm.EditingSegmentDraftText);
  }

  [TestMethod]
  public async Task RemoveFillersFromDraft_ThenApply_SendsCleanedReplacementText()
  {
    var vm = CreateSut();
    vm.SelectedTranscription = BuildTranscription();
    vm.BeginEditSegment(vm.SelectedTranscription.Segments![0]);
    vm.EditingSegmentDraftText = "new um words";

    var cleanErr = vm.TryRemoveFillersFromEditingDraft();
    Assert.IsNull(cleanErr);

    var err = await vm.ApplyEditedSegmentAsync(CancellationToken.None).ConfigureAwait(false);

    Assert.IsNull(err);
    _regenMock!.Verify(
        x => x.StartRegenerateSegmentAsync(
            It.Is<RegenerateSegmentStartRequest>(r =>
                r.ReplacementText == "new words"),
            It.IsAny<CancellationToken>()),
        Times.Once);
  }

  [TestMethod]
  public void RemoveFillersFromDraft_NotEditing_ReturnsError()
  {
    var vm = CreateSut();
    var err = vm.TryRemoveFillersFromEditingDraft();
    Assert.IsNotNull(err);
    StringAssert.Contains(err, "No segment edit");
  }

  [TestMethod]
  public void RemoveFillersFromDraft_RiskyLike_OffByDefault_PreviewKeepsLike()
  {
    var vm = CreateSut();
    vm.SelectedTranscription = BuildTranscription();
    vm.BeginEditSegment(vm.SelectedTranscription.Segments![0]);
    vm.EditingSegmentDraftText = "I like um";
    var likeToggle = vm.FillerRemovalToggles.First(t =>
        string.Equals(t.Key, "like", StringComparison.OrdinalIgnoreCase));
    var umToggle = vm.FillerRemovalToggles.First(t =>
        string.Equals(t.Key, "um", StringComparison.OrdinalIgnoreCase));
    Assert.IsTrue(likeToggle.IsRisky);
    Assert.IsFalse(likeToggle.IsRemoveEnabled);
    Assert.IsTrue(umToggle.IsRemoveEnabled);
    Assert.AreEqual("I like", vm.FillerRemovalPreviewText?.Trim());
  }

  /// <summary>GAP-047: draft-only filler cleanup must not start regen or authoritative apply path.</summary>
  [TestMethod]
  public void RemoveFillersFromDraft_DoesNotStartRegeneration()
  {
    var vm = CreateSut();
    vm.SelectedProjectId = "p1";
    vm.SelectedTranscription = BuildTranscription();
    vm.BeginEditSegment(vm.SelectedTranscription.Segments![0]);
    vm.EditingSegmentDraftText = "hello um world";

    var err = vm.TryRemoveFillersFromEditingDraft();

    Assert.IsNull(err);
    _regenMock!.Verify(
        x => x.StartRegenerateSegmentAsync(It.IsAny<RegenerateSegmentStartRequest>(), It.IsAny<CancellationToken>()),
        Times.Never);
  }

  /// <summary>GAP-047: preview/toggle review alone must not trigger regen.</summary>
  [TestMethod]
  public void FillerToggleChange_DoesNotStartRegeneration()
  {
    var vm = CreateSut();
    vm.SelectedTranscription = BuildTranscription();
    vm.BeginEditSegment(vm.SelectedTranscription.Segments![0]);
    vm.EditingSegmentDraftText = "hello um world";
    var umToggle = vm.FillerRemovalToggles.First(t =>
        string.Equals(t.Key, "um", StringComparison.OrdinalIgnoreCase));
    umToggle.IsRemoveEnabled = false;
    umToggle.IsRemoveEnabled = true;

    _regenMock!.Verify(
        x => x.StartRegenerateSegmentAsync(It.IsAny<RegenerateSegmentStartRequest>(), It.IsAny<CancellationToken>()),
        Times.Never);
  }

  /// <summary>GAP-047: committed segment text is unchanged until explicit Apply.</summary>
  [TestMethod]
  public void RemoveFillersFromDraft_LeavesCommittedSegmentTextUnchangedUntilApply()
  {
    var vm = CreateSut();
    vm.SelectedTranscription = BuildTranscription();
    vm.BeginEditSegment(vm.SelectedTranscription.Segments![0]);
    vm.EditingSegmentDraftText = "original um";

    var err = vm.TryRemoveFillersFromEditingDraft();

    Assert.IsNull(err);
    Assert.AreEqual("original", vm.SelectedTranscription!.Segments![0].Text);
    Assert.AreEqual("original", vm.EditingSegmentDraftText?.Trim());
  }

  /// <summary>GAP-047: cancel discards draft + filler state; canonical segment text never reflected draft-only edits.</summary>
  [TestMethod]
  public void CancelEdit_AfterRemoveFillers_LeavesCanonicalSegmentTextUnchanged()
  {
    var vm = CreateSut();
    vm.SelectedTranscription = BuildTranscription();
    vm.BeginEditSegment(vm.SelectedTranscription.Segments![0]);
    vm.EditingSegmentDraftText = "speech um here";

    Assert.IsNull(vm.TryRemoveFillersFromEditingDraft());
    Assert.AreEqual("speech here", vm.EditingSegmentDraftText?.Trim());
    Assert.AreEqual("original", vm.SelectedTranscription!.Segments![0].Text);

    vm.CancelSegmentEdit();

    Assert.IsFalse(vm.IsEditingSegment);
    Assert.AreEqual("original", vm.SelectedTranscription.Segments[0].Text);
  }

  /// <summary>GAP-047 post-apply coherence: one cross-consumer reconcile signal after successful filler cleanup Apply.</summary>
  [TestMethod]
  public async Task ApplyFillerCleanup_Success_UpdatesCanonicalConsumerStateExactlyOnce()
  {
    InstallHarness(jobFails: false);
    var bus = AppServices.TryGetEventAggregator();
    Assert.IsNotNull(bus);
    var segmentApplyCoherenceCount = 0;
    using var _ = bus!.Subscribe<NavigateToEvent>(ev =>
    {
      if (ev.Parameters != null
          && ev.Parameters.TryGetValue("action", out var a)
          && string.Equals(a?.ToString(), "coherentReloadAfterSegmentApply", StringComparison.Ordinal))
      {
        segmentApplyCoherenceCount++;
      }
    });

    var vm = CreateSut();
    vm.SelectedProjectId = "p1";
    vm.SelectedTranscription = BuildTranscription();
    vm.BeginEditSegment(vm.SelectedTranscription.Segments![0]);
    vm.EditingSegmentDraftText = "new um words";
    Assert.IsNull(vm.TryRemoveFillersFromEditingDraft());

    var err = await vm.ApplyEditedSegmentAsync(CancellationToken.None).ConfigureAwait(false);

    Assert.IsNull(err);
    Assert.AreEqual(1, segmentApplyCoherenceCount);
  }

  /// <summary>GAP-047 post-apply coherence: failed Apply must not publish cross-consumer reload.</summary>
  [TestMethod]
  public async Task ApplyFillerCleanup_Failure_LeavesCrossConsumersUnchanged()
  {
    InstallHarness(jobFails: true);
    var bus = AppServices.TryGetEventAggregator();
    Assert.IsNotNull(bus);
    var segmentApplyCoherenceCount = 0;
    using var _ = bus!.Subscribe<NavigateToEvent>(ev =>
    {
      if (ev.Parameters != null
          && ev.Parameters.TryGetValue("action", out var a)
          && string.Equals(a?.ToString(), "coherentReloadAfterSegmentApply", StringComparison.Ordinal))
      {
        segmentApplyCoherenceCount++;
      }
    });

    var vm = CreateSut();
    vm.SelectedProjectId = "p1";
    vm.SelectedTranscription = BuildTranscription();
    vm.BeginEditSegment(vm.SelectedTranscription.Segments![0]);
    vm.EditingSegmentDraftText = "new um words";
    Assert.IsNull(vm.TryRemoveFillersFromEditingDraft());

    var err = await vm.ApplyEditedSegmentAsync(CancellationToken.None).ConfigureAwait(false);

    Assert.IsNotNull(err);
    Assert.AreEqual(0, segmentApplyCoherenceCount);
  }

  /// <summary>GAP-047 persist recovery: coordinator compensates clip; no undo registration on persist failure.</summary>
  [TestMethod]
  public async Task Apply_WithTranscriptPersistFailure_DoesNotCorruptUndoStack()
  {
    InstallHarness(jobFails: false, transcriptPersistFails: true);
    var vm = CreateSut();
    vm.SelectedProjectId = "p1";
    vm.SelectedTranscription = BuildTranscription();
    vm.BeginEditSegment(vm.SelectedTranscription.Segments![0]);
    vm.EditingSegmentDraftText = "new um words";
    Assert.IsNull(vm.TryRemoveFillersFromEditingDraft());

    var err = await vm.ApplyEditedSegmentAsync(CancellationToken.None).ConfigureAwait(false);

    Assert.IsNotNull(err);
    StringAssert.Contains(err, "transcript persistence failed", StringComparison.OrdinalIgnoreCase);
    await PumpUntilApplyJobRowFailedAsync(vm).ConfigureAwait(false);
    Assert.AreEqual(0, AppServices.GetUndoRedoService().UndoCount);
  }

  /// <summary>GAP-047 persist recovery: failed apply must not publish timeline coherence reload.</summary>
  [TestMethod]
  public async Task Apply_WithTranscriptPersistFailure_DoesNotLeaveTimelineOverlayStale()
  {
    InstallHarness(jobFails: false, transcriptPersistFails: true);
    var bus = AppServices.TryGetEventAggregator();
    Assert.IsNotNull(bus);
    var segmentApplyCoherenceCount = 0;
    using var _ = bus!.Subscribe<NavigateToEvent>(ev =>
    {
      if (ev.Parameters != null
          && ev.Parameters.TryGetValue("action", out var a)
          && string.Equals(a?.ToString(), "coherentReloadAfterSegmentApply", StringComparison.Ordinal))
      {
        segmentApplyCoherenceCount++;
      }
    });

    var vm = CreateSut();
    vm.SelectedProjectId = "p1";
    vm.SelectedTranscription = BuildTranscription();
    vm.BeginEditSegment(vm.SelectedTranscription.Segments![0]);
    vm.EditingSegmentDraftText = "new um words";
    Assert.IsNull(vm.TryRemoveFillersFromEditingDraft());

    var err = await vm.ApplyEditedSegmentAsync(CancellationToken.None).ConfigureAwait(false);

    Assert.IsNotNull(err);
    await PumpUntilApplyJobRowFailedAsync(vm).ConfigureAwait(false);

    Assert.AreEqual(0, segmentApplyCoherenceCount);
  }

  /// <summary>GAP-047 post-apply coherence: draft-only cleanup + cancel never publishes cross-consumer reload.</summary>
  [TestMethod]
  public async Task CancelAfterDraftCleanup_LeavesCrossConsumersUnchanged()
  {
    InstallHarness(jobFails: false);
    var bus = AppServices.TryGetEventAggregator();
    Assert.IsNotNull(bus);
    var segmentApplyCoherenceCount = 0;
    using var _ = bus!.Subscribe<NavigateToEvent>(ev =>
    {
      if (ev.Parameters != null
          && ev.Parameters.TryGetValue("action", out var a)
          && string.Equals(a?.ToString(), "coherentReloadAfterSegmentApply", StringComparison.Ordinal))
      {
        segmentApplyCoherenceCount++;
      }
    });

    var vm = CreateSut();
    vm.SelectedProjectId = "p1";
    vm.SelectedTranscription = BuildTranscription();
    vm.BeginEditSegment(vm.SelectedTranscription.Segments![0]);
    vm.EditingSegmentDraftText = "speech um here";
    Assert.IsNull(vm.TryRemoveFillersFromEditingDraft());
    vm.CancelSegmentEdit();
    await PumpDispatcherOnceAsync().ConfigureAwait(false);

    Assert.AreEqual(0, segmentApplyCoherenceCount);
  }

  /// <summary>GAP-047 range parity: one coherence event after successful range filler cleanup Apply.</summary>
  [TestMethod]
  public async Task RangeApply_AfterFillerCleanup_PublishesSingleCoherenceEvent()
  {
    InstallHarness(jobFails: false, BuildLinkedProjectThreeSegmentsOneClip());
    var bus = AppServices.TryGetEventAggregator();
    Assert.IsNotNull(bus);
    var segmentApplyCoherenceCount = 0;
    using var _ = bus!.Subscribe<NavigateToEvent>(ev =>
    {
      if (ev.Parameters != null
          && ev.Parameters.TryGetValue("action", out var a)
          && string.Equals(a?.ToString(), "coherentReloadAfterSegmentApply", StringComparison.Ordinal))
      {
        segmentApplyCoherenceCount++;
      }
    });

    var vm = CreateSut();
    vm.SelectedProjectId = "p1";
    var tr = BuildTranscriptionThreeSegments();
    vm.SelectedTranscription = tr;
    vm.BeginEditRange(tr.Segments![0], tr.Segments![2]);
    vm.EditingSegmentDraftText = "aa uh bb cc";
    Assert.IsNull(vm.TryRemoveFillersFromEditingDraft());

    var err = await vm.ApplyEditedSegmentAsync(CancellationToken.None).ConfigureAwait(false);
    Assert.IsNull(err);
    await PumpUntilApplyJobRowSucceededAsync(vm).ConfigureAwait(false);

    Assert.AreEqual(1, segmentApplyCoherenceCount);
  }

  /// <summary>GAP-047 range parity: failed range Apply must not publish cross-consumer reload.</summary>
  [TestMethod]
  public async Task RangeApply_Failure_DoesNotPublishCoherenceEvent()
  {
    InstallHarness(jobFails: true, BuildLinkedProjectThreeSegmentsOneClip());
    var bus = AppServices.TryGetEventAggregator();
    Assert.IsNotNull(bus);
    var segmentApplyCoherenceCount = 0;
    using var _ = bus!.Subscribe<NavigateToEvent>(ev =>
    {
      if (ev.Parameters != null
          && ev.Parameters.TryGetValue("action", out var a)
          && string.Equals(a?.ToString(), "coherentReloadAfterSegmentApply", StringComparison.Ordinal))
      {
        segmentApplyCoherenceCount++;
      }
    });

    var vm = CreateSut();
    vm.SelectedProjectId = "p1";
    var tr = BuildTranscriptionThreeSegments();
    vm.SelectedTranscription = tr;
    vm.BeginEditRange(tr.Segments![0], tr.Segments![2]);
    vm.EditingSegmentDraftText = "merged um words";
    Assert.IsNull(vm.TryRemoveFillersFromEditingDraft());

    var err = await vm.ApplyEditedSegmentAsync(CancellationToken.None).ConfigureAwait(false);
    Assert.IsNotNull(err);
    await PumpUntilApplyJobRowFailedAsync(vm).ConfigureAwait(false);

    Assert.AreEqual(0, segmentApplyCoherenceCount);
  }

  /// <summary>GAP-047 range parity: cancel after range draft cleanup publishes no coherence event.</summary>
  [TestMethod]
  public async Task CancelAfterRangeDraftCleanup_DoesNotPublishCoherenceEvent()
  {
    InstallHarness(jobFails: false, BuildLinkedProjectThreeSegmentsOneClip());
    var bus = AppServices.TryGetEventAggregator();
    Assert.IsNotNull(bus);
    var segmentApplyCoherenceCount = 0;
    using var _ = bus!.Subscribe<NavigateToEvent>(ev =>
    {
      if (ev.Parameters != null
          && ev.Parameters.TryGetValue("action", out var a)
          && string.Equals(a?.ToString(), "coherentReloadAfterSegmentApply", StringComparison.Ordinal))
      {
        segmentApplyCoherenceCount++;
      }
    });

    var vm = CreateSut();
    vm.SelectedProjectId = "p1";
    var tr = BuildTranscriptionThreeSegments();
    vm.SelectedTranscription = tr;
    vm.BeginEditRange(tr.Segments![0], tr.Segments![2]);
    vm.EditingSegmentDraftText = "aa um bb cc";
    Assert.IsNull(vm.TryRemoveFillersFromEditingDraft());
    vm.CancelSegmentEdit();
    await PumpDispatcherOnceAsync().ConfigureAwait(false);

    Assert.AreEqual(0, segmentApplyCoherenceCount);
  }

  /// <summary>GAP-047 undo: single-segment filler cleanup Apply then Undo restores canonical segment text.</summary>
  [TestMethod]
  public async Task FillerCleanupApply_Undo_RestoresCanonicalSingleSegmentText()
  {
    InstallHarness(jobFails: false);
    var vm = CreateSut();
    vm.SelectedProjectId = "p1";
    vm.SelectedTranscription = BuildTranscription();
    vm.BeginEditSegment(vm.SelectedTranscription.Segments![0]);
    vm.EditingSegmentDraftText = "new um words";
    Assert.IsNull(vm.TryRemoveFillersFromEditingDraft());

    var err = await vm.ApplyEditedSegmentAsync(CancellationToken.None).ConfigureAwait(false);
    Assert.IsNull(err);
    await PumpUntilApplyJobRowSucceededAsync(vm).ConfigureAwait(false);

    Assert.AreEqual("new words", vm.SelectedTranscription!.Segments![0].Text.Trim());

    var undo = AppServices.TryGetUndoRedoService();
    Assert.IsNotNull(undo);
    Assert.IsTrue(undo!.Undo());

    Assert.AreEqual("original", vm.SelectedTranscription.Segments[0].Text);
  }

  /// <summary>GAP-047 undo: range filler cleanup Apply then Undo restores canonical multi-segment text.</summary>
  [TestMethod]
  public async Task FillerCleanupRangeApply_Undo_RestoresCanonicalRangeText()
  {
    InstallHarness(jobFails: false, BuildLinkedProjectThreeSegmentsOneClip());
    var vm = CreateSut();
    vm.SelectedProjectId = "p1";
    var tr = BuildTranscriptionThreeSegments();
    vm.SelectedTranscription = tr;
    vm.BeginEditRange(tr.Segments![0], tr.Segments![2]);
    vm.EditingSegmentDraftText = "aa uh bb cc";
    Assert.IsNull(vm.TryRemoveFillersFromEditingDraft());

    var err = await vm.ApplyEditedSegmentAsync(CancellationToken.None).ConfigureAwait(false);
    Assert.IsNull(err);
    await PumpUntilApplyJobRowSucceededAsync(vm).ConfigureAwait(false);

    Assert.AreEqual("aa bb cc", tr.Segments[0].Text.Trim());
    Assert.AreEqual(string.Empty, tr.Segments[1].Text.Trim());
    Assert.AreEqual(string.Empty, tr.Segments[2].Text.Trim());

    var undo = AppServices.TryGetUndoRedoService();
    Assert.IsNotNull(undo);
    Assert.IsTrue(undo!.Undo());

    Assert.AreEqual("aa", tr.Segments[0].Text);
    Assert.AreEqual("bb", tr.Segments[1].Text);
    Assert.AreEqual("cc", tr.Segments[2].Text);
  }

  /// <summary>GAP-047 undo: one coherence NavigateToEvent per Undo (no duplicate reload signal).</summary>
  [TestMethod]
  public async Task UndoAfterFillerCleanup_DoesNotDuplicateCoherenceReload()
  {
    InstallHarness(jobFails: false);
    var bus = AppServices.TryGetEventAggregator();
    Assert.IsNotNull(bus);
    var coherenceDuringUndo = 0;
    using var sub = bus!.Subscribe<NavigateToEvent>(ev =>
    {
      if (ev.Parameters != null
          && ev.Parameters.TryGetValue("action", out var a)
          && string.Equals(a?.ToString(), "coherentReloadAfterSegmentApply", StringComparison.Ordinal))
      {
        coherenceDuringUndo++;
      }
    });

    var vm = CreateSut();
    vm.SelectedProjectId = "p1";
    vm.SelectedTranscription = BuildTranscription();
    vm.BeginEditSegment(vm.SelectedTranscription.Segments![0]);
    vm.EditingSegmentDraftText = "x um y";
    Assert.IsNull(vm.TryRemoveFillersFromEditingDraft());
    Assert.IsNull(await vm.ApplyEditedSegmentAsync(CancellationToken.None).ConfigureAwait(false));
    await PumpUntilApplyJobRowSucceededAsync(vm).ConfigureAwait(false);

    coherenceDuringUndo = 0;
    Assert.IsTrue(AppServices.TryGetUndoRedoService()!.Undo());
    Assert.AreEqual(1, coherenceDuringUndo);
  }

  /// <summary>GAP-047 history: draft-only filler cleanup never creates committed apply/regeneration history rows.</summary>
  [TestMethod]
  public void DraftOnlyFillerCleanup_DoesNotCreateCommittedHistoryEntry()
  {
    var vm = CreateSut();
    vm.SelectedProjectId = "p1";
    vm.SelectedTranscription = BuildTranscription();
    vm.BeginEditSegment(vm.SelectedTranscription.Segments![0]);
    vm.EditingSegmentDraftText = "hello um";
    var hist = AppServices.TryGetTranscriptEditHistoryService();
    hist!.ClearSession();

    Assert.IsNull(vm.TryRemoveFillersFromEditingDraft());

    Assert.AreEqual(1, hist.Entries.Count);
    Assert.AreEqual(TranscriptEditOperationKind.FillerCleanupDraft, hist.Entries[0].OperationKind);
    Assert.IsFalse(
        hist.Entries.Any(
            e => e.OperationKind == TranscriptEditOperationKind.SingleSegmentApply
                || e.OperationKind == TranscriptEditOperationKind.MultiSegmentRangeApply));
  }

  /// <summary>GAP-047 undo: cancel after draft cleanup does not register coordinator undo.</summary>
  [TestMethod]
  public async Task CancelAfterFillerCleanup_DoesNotCreateUndoableMutation()
  {
    InstallHarness(jobFails: false);
    var vm = CreateSut();
    vm.SelectedProjectId = "p1";
    vm.SelectedTranscription = BuildTranscription();
    vm.BeginEditSegment(vm.SelectedTranscription.Segments![0]);
    vm.EditingSegmentDraftText = "speech um here";
    Assert.IsNull(vm.TryRemoveFillersFromEditingDraft());
    vm.CancelSegmentEdit();
    await PumpDispatcherOnceAsync().ConfigureAwait(false);

    var undo = AppServices.TryGetUndoRedoService();
    Assert.IsNotNull(undo);
    Assert.IsFalse(undo!.CanUndo);
  }

  /// <summary>GAP-047 seam: after Apply + Undo, list rehydrate replaces selection with authoritative backend transcript.</summary>
  [TestMethod]
  public async Task ApplyUndoRehydrate_UsesAuthoritativeBackendTruth()
  {
    InstallHarness(jobFails: false);
    _overrideVmTranscriptionClientMock = CreateTranscriptionClientMock();
    _overrideVmTranscriptionClientMock
        .Setup(x => x.ListTranscriptionsAsync(It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
        .ReturnsAsync(new List<TranscriptionResponse>());
    _overrideVmTranscriptionClientMock
        .Setup(x => x.ListTranscriptionsAsync("aud-gap047", "proj-gap047", It.IsAny<CancellationToken>()))
        .ReturnsAsync(
            new List<TranscriptionResponse>
            {
              new()
              {
                Id = "tr1",
                Text = "rehydrated authority",
                Segments = new List<TranscriptionSegment>
                {
                  new() { Id = "s9", Start = 1, End = 4, Text = "rehydrated authority" },
                },
              },
            });

    var vm = CreateSut();
    await vm.InitializeAsync(CancellationToken.None).ConfigureAwait(false);
    vm.SelectedProjectId = "proj-gap047";
    vm.SelectedAudioId = "aud-gap047";
    vm.SelectedTranscription = BuildTranscription();
    vm.BeginEditSegment(vm.SelectedTranscription.Segments![0]);
    vm.EditingSegmentDraftText = "new um words";
    Assert.IsNull(vm.TryRemoveFillersFromEditingDraft());
    Assert.IsNull(await vm.ApplyEditedSegmentAsync(CancellationToken.None).ConfigureAwait(false));
    await PumpUntilApplyJobRowSucceededAsync(vm).ConfigureAwait(false);
    Assert.IsTrue(AppServices.TryGetUndoRedoService()!.Undo());

    await vm.LoadTranscriptionsCommand.ExecuteAsync(null).ConfigureAwait(false);
    for (var i = 0; i < 40; i++)
    {
      await PumpDispatcherOnceAsync().ConfigureAwait(false);
      if (string.Equals(
              vm.SelectedTranscription?.Segments?[0].Text,
              "rehydrated authority",
              StringComparison.Ordinal))
        break;
      await Task.Delay(25).ConfigureAwait(false);
    }

    Assert.IsNotNull(vm.SelectedTranscription);
    Assert.AreEqual("rehydrated authority", vm.SelectedTranscription!.Segments![0].Text);
    _overrideVmTranscriptionClientMock.Verify(
        x => x.ListTranscriptionsAsync("aud-gap047", "proj-gap047", It.IsAny<CancellationToken>()),
        Times.AtLeastOnce);
  }

  /// <summary>GAP-047 range parity: draft-only range filler cleanup does not publish coherence or mutate committed segments.</summary>
  [TestMethod]
  public void RangeApply_DoesNotLeakDraftOnlyStateAcrossConsumers()
  {
    InstallHarness(jobFails: false, BuildLinkedProjectThreeSegmentsOneClip());
    var bus = AppServices.TryGetEventAggregator();
    Assert.IsNotNull(bus);
    var segmentApplyCoherenceCount = 0;
    using var _ = bus!.Subscribe<NavigateToEvent>(ev =>
    {
      if (ev.Parameters != null
          && ev.Parameters.TryGetValue("action", out var a)
          && string.Equals(a?.ToString(), "coherentReloadAfterSegmentApply", StringComparison.Ordinal))
      {
        segmentApplyCoherenceCount++;
      }
    });

    var vm = CreateSut();
    vm.SelectedProjectId = "p1";
    var tr = BuildTranscriptionThreeSegments();
    vm.SelectedTranscription = tr;
    vm.BeginEditRange(tr.Segments![0], tr.Segments![2]);
    vm.EditingSegmentDraftText = "aa uh bb cc";
    Assert.IsNull(vm.TryRemoveFillersFromEditingDraft());

    Assert.AreEqual("aa", tr.Segments![0].Text);
    Assert.AreEqual("bb", tr.Segments![1].Text);
    Assert.AreEqual("cc", tr.Segments![2].Text);
    Assert.AreEqual(0, segmentApplyCoherenceCount);
  }

  [TestMethod]
  public void RemoveFillersFromDraft_AllTogglesOff_ReturnsError()
  {
    var vm = CreateSut();
    vm.SelectedTranscription = BuildTranscription();
    vm.BeginEditSegment(vm.SelectedTranscription.Segments![0]);
    vm.EditingSegmentDraftText = "hello um world";
    foreach (var t in vm.FillerRemovalToggles)
      t.IsRemoveEnabled = false;
    var err = vm.TryRemoveFillersFromEditingDraft();
    Assert.IsNotNull(err);
    StringAssert.Contains(err, "Enable at least one");
  }

  [TestMethod]
  public void CancelEdit_ClearsFillerRemovalToggles()
  {
    var vm = CreateSut();
    vm.SelectedTranscription = BuildTranscription();
    vm.BeginEditSegment(vm.SelectedTranscription.Segments![0]);
    vm.EditingSegmentDraftText = "um";
    Assert.IsTrue(vm.FillerRemovalToggles.Count > 0);
    vm.CancelSegmentEdit();
    Assert.AreEqual(0, vm.FillerRemovalToggles.Count);
    Assert.IsNull(vm.FillerRemovalPreviewText);
  }

  [TestMethod]
  public void RemoveFillersFromEditingDraftCommand_CannotExecuteWhenRegeneratingSegmentIdSet()
  {
    var vm = CreateSut();
    vm.SelectedTranscription = BuildTranscription();
    vm.BeginEditSegment(vm.SelectedTranscription.Segments![0]);

    Assert.IsTrue(vm.RemoveFillersFromEditingDraftCommand.CanExecute(null));

    vm.RegeneratingSegmentId = "s9";
    Assert.IsFalse(vm.RemoveFillersFromEditingDraftCommand.CanExecute(null));
  }

  [TestMethod]
  public void TranscriptHarness_Resolve_tr1_s9_ReturnsClip()
  {
    InstallHarness(jobFails: false);
    var resolver = AppServices.TryGetTranscriptSegmentTargetResolver();
    Assert.IsNotNull(resolver);
    var r = resolver!.Resolve("tr1", "s9", 1, 4);
    Assert.AreEqual(TranscriptSegmentTargetResolutionKind.Resolved, r.Kind, r.Reason ?? "");
    Assert.AreEqual("c1", r.ClipId);
  }

  [TestMethod]
  public async Task ApplyEdit_Success_AddsSingleHistoryEntry()
  {
    var vm = CreateSut();
    vm.SelectedProjectId = "p1";
    vm.SelectedTranscription = BuildTranscription();
    vm.BeginEditSegment(vm.SelectedTranscription.Segments![0]);
    vm.EditingSegmentDraftText = "new words";
    var hist = AppServices.TryGetTranscriptEditHistoryService();
    Assert.IsNotNull(hist);
    hist!.ClearSession();

    var err = await vm.ApplyEditedSegmentAsync(CancellationToken.None).ConfigureAwait(false);

    Assert.IsNull(err);
    Assert.AreEqual(1, hist.Entries.Count);
    var entry = hist.Entries[0];
    Assert.AreEqual(TranscriptEditOperationKind.SingleSegmentApply, entry.OperationKind);
    Assert.IsTrue(entry.Succeeded);
    Assert.IsTrue(entry.WasRegenerated);
    CollectionAssert.AreEqual(new[] { "s9" }, entry.SegmentIds.ToArray());
    Assert.AreEqual("tr1", entry.TranscriptionId);
    Assert.AreEqual("c1", entry.ClipId);
  }

  [TestMethod]
  public async Task ApplyEdit_Failure_AddsFailedHistoryEntry()
  {
    InstallHarness(jobFails: true);
    var vm = CreateSut();
    vm.SelectedProjectId = "p1";
    vm.SelectedTranscription = BuildTranscription();
    vm.BeginEditSegment(vm.SelectedTranscription.Segments![0]);
    vm.EditingSegmentDraftText = "new words";
    var hist = AppServices.TryGetTranscriptEditHistoryService();
    hist!.ClearSession();

    var err = await vm.ApplyEditedSegmentAsync(CancellationToken.None).ConfigureAwait(false);

    Assert.IsNotNull(err);
    Assert.AreEqual(1, hist.Entries.Count);
    var entry = hist.Entries[0];
    Assert.IsFalse(entry.Succeeded);
    Assert.IsFalse(entry.WasRegenerated);
    Assert.AreEqual(TranscriptEditOperationKind.SingleSegmentApply, entry.OperationKind);
  }

  [TestMethod]
  public async Task ApplyEdit_RangeSuccess_HistoryListsAllSegmentIds()
  {
    InstallHarness(jobFails: false, BuildLinkedProjectThreeSegmentsOneClip());
    var vm = CreateSut();
    vm.SelectedProjectId = "p1";
    var tr = BuildTranscriptionThreeSegments();
    vm.SelectedTranscription = tr;
    vm.BeginEditRange(tr.Segments![0], tr.Segments![2]);
    vm.EditingSegmentDraftText = "merged text";
    var hist = AppServices.TryGetTranscriptEditHistoryService();
    hist!.ClearSession();

    var err = await vm.ApplyEditedSegmentAsync(CancellationToken.None).ConfigureAwait(false);

    Assert.IsNull(err);
    Assert.AreEqual(1, hist.Entries.Count);
    var entry = hist.Entries[0];
    Assert.AreEqual(TranscriptEditOperationKind.MultiSegmentRangeApply, entry.OperationKind);
    CollectionAssert.AreEqual(new[] { "s1", "s2", "s3" }, entry.SegmentIds.ToArray());
  }

  [TestMethod]
  public void RemoveFillersFromDraft_AddsFillerCleanupHistoryEntry()
  {
    var vm = CreateSut();
    vm.SelectedProjectId = "p1";
    vm.SelectedTranscription = BuildTranscription();
    vm.BeginEditSegment(vm.SelectedTranscription.Segments![0]);
    vm.EditingSegmentDraftText = "hello um";
    var hist = AppServices.TryGetTranscriptEditHistoryService();
    hist!.ClearSession();

    var err = vm.TryRemoveFillersFromEditingDraft();

    Assert.IsNull(err);
    Assert.AreEqual(1, hist.Entries.Count);
    Assert.AreEqual(TranscriptEditOperationKind.FillerCleanupDraft, hist.Entries[0].OperationKind);
    Assert.IsFalse(hist.Entries[0].WasRegenerated);
    Assert.IsTrue(hist.Entries[0].Succeeded);
  }

  [TestMethod]
  public void ClearTranscriptEditHistory_RemovesEntries()
  {
    var vm = CreateSut();
    vm.SelectedTranscription = BuildTranscription();
    vm.BeginEditSegment(vm.SelectedTranscription.Segments![0]);
    vm.EditingSegmentDraftText = "um hi";
    var hist = AppServices.TryGetTranscriptEditHistoryService();
    hist!.ClearSession();
    _ = vm.TryRemoveFillersFromEditingDraft();
    Assert.IsTrue(hist.Entries.Count > 0);
    vm.ClearTranscriptEditHistoryCommand.Execute(null);
    Assert.AreEqual(0, hist.Entries.Count);
  }

  [TestMethod]
  public async Task NavigateFromEditHistoryEntry_PublishesSeekWhenResolved()
  {
    InstallHarness(jobFails: false);
    var bus = AppServices.TryGetEventAggregator();
    Assert.IsNotNull(bus);
    NavigateToEvent? published = null;
    using var sub = bus!.Subscribe<NavigateToEvent>(ev => published = ev);
    var vm = CreateSut();
    vm.SelectedProjectId = "p1";
    vm.Transcriptions.Add(BuildTranscription());
    vm.SelectedTranscription = vm.Transcriptions[0];
    var entry = new TranscriptEditHistoryEntry
    {
      TranscriptionId = "tr1",
      SegmentIds = new List<string> { "s9" },
      OperationKind = TranscriptEditOperationKind.SingleSegmentApply,
      Succeeded = true,
      WasRegenerated = true,
      MessageSummary = "test",
    };
    vm.NavigateFromEditHistoryEntry(entry);
    await PumpDispatcherOnceAsync().ConfigureAwait(false);

    Assert.IsNotNull(published);
    Assert.AreEqual("timeline", published!.TargetPanelId);
    Assert.IsTrue(published.Parameters?.ContainsKey("action"));
    Assert.AreEqual("seekPlayhead", published.Parameters!["action"]?.ToString());
    Assert.AreEqual("c1", published.Parameters!["clipId"]?.ToString());
  }

  [TestMethod]
  public async Task NavigateFromApplyJobStatusEntry_PublishesSeekWhenResolved()
  {
    InstallHarness(jobFails: false);
    var bus = AppServices.TryGetEventAggregator();
    Assert.IsNotNull(bus);
    NavigateToEvent? published = null;
    using var sub = bus!.Subscribe<NavigateToEvent>(ev => published = ev);
    var vm = CreateSut();
    vm.SelectedProjectId = "p1";
    vm.Transcriptions.Add(BuildTranscription());
    vm.SelectedTranscription = vm.Transcriptions[0];
    var jobEntry = BuildApplyJobStatusEntry();
    vm.NavigateFromApplyJobStatusEntry(jobEntry);
    await PumpDispatcherOnceAsync().ConfigureAwait(false);

    Assert.IsNotNull(published);
    Assert.AreEqual("timeline", published!.TargetPanelId);
    Assert.AreEqual("seekPlayhead", published.Parameters!["action"]?.ToString());
    Assert.AreEqual("c1", published.Parameters!["clipId"]?.ToString());
  }

  [TestMethod]
  public async Task NavigateFromApplyJobStatusEntry_ProjectMismatch_FailsClosed()
  {
    InstallHarness(jobFails: false);
    var bus = AppServices.TryGetEventAggregator();
    NavigateToEvent? published = null;
    using var sub = bus!.Subscribe<NavigateToEvent>(ev => published = ev);
    var vm = CreateSut();
    vm.SelectedProjectId = "p1";
    vm.Transcriptions.Add(BuildTranscription());
    vm.SelectedTranscription = vm.Transcriptions[0];
    vm.NavigateFromApplyJobStatusEntry(BuildApplyJobStatusEntry(projectId: "other-p"));
    await PumpDispatcherOnceAsync().ConfigureAwait(false);

    Assert.IsNull(published);
    StringAssert.Contains(vm.TranscriptOperatorMessage ?? string.Empty, TranscriptStaleContextExplainability.JumpPrefix);
    StringAssert.Contains(vm.TranscriptOperatorMessage ?? string.Empty, "active project");
  }

  [TestMethod]
  public async Task NavigateFromEditHistoryEntry_ProjectMismatch_FailsClosed()
  {
    InstallHarness(jobFails: false);
    var bus = AppServices.TryGetEventAggregator();
    NavigateToEvent? published = null;
    using var sub = bus!.Subscribe<NavigateToEvent>(ev => published = ev);
    var vm = CreateSut();
    vm.SelectedProjectId = "p1";
    vm.Transcriptions.Add(BuildTranscription());
    vm.SelectedTranscription = vm.Transcriptions[0];
    var entry = new TranscriptEditHistoryEntry
    {
      TranscriptionId = "tr1",
      ProjectId = "other-p",
      SegmentIds = new List<string> { "s9" },
      OperationKind = TranscriptEditOperationKind.SingleSegmentApply,
      Succeeded = true,
      MessageSummary = "test",
    };
    vm.NavigateFromEditHistoryEntry(entry);
    await PumpDispatcherOnceAsync().ConfigureAwait(false);

    Assert.IsNull(published);
    StringAssert.Contains(vm.TranscriptOperatorMessage ?? string.Empty, TranscriptStaleContextExplainability.JumpPrefix);
    StringAssert.Contains(vm.TranscriptOperatorMessage ?? string.Empty, "active project");
  }

  [TestMethod]
  public async Task NavigateFromApplyJobStatusEntry_TranscriptionNotInList_FailsClosed()
  {
    InstallHarness(jobFails: false);
    var bus = AppServices.TryGetEventAggregator();
    NavigateToEvent? published = null;
    using var sub = bus!.Subscribe<NavigateToEvent>(ev => published = ev);
    var vm = CreateSut();
    vm.SelectedProjectId = "p1";
    vm.Transcriptions.Add(BuildTranscription());
    vm.NavigateFromApplyJobStatusEntry(BuildApplyJobStatusEntry(transcriptionId: "missing-tr"));
    await PumpDispatcherOnceAsync().ConfigureAwait(false);

    Assert.IsNull(published);
    StringAssert.Contains(vm.TranscriptOperatorMessage ?? string.Empty, TranscriptStaleContextExplainability.JumpPrefix);
    StringAssert.Contains(vm.TranscriptOperatorMessage ?? string.Empty, "current session list");
  }

  [TestMethod]
  public async Task NavigateFromApplyJobStatusEntry_StaleSegment_FailsClosed()
  {
    InstallHarness(jobFails: false);
    var bus = AppServices.TryGetEventAggregator();
    NavigateToEvent? published = null;
    using var sub = bus!.Subscribe<NavigateToEvent>(ev => published = ev);
    var vm = CreateSut();
    vm.SelectedProjectId = "p1";
    vm.Transcriptions.Add(BuildTranscription());
    vm.SelectedTranscription = vm.Transcriptions[0];
    vm.NavigateFromApplyJobStatusEntry(BuildApplyJobStatusEntry(segmentIds: new[] { "gone" }));
    await PumpDispatcherOnceAsync().ConfigureAwait(false);

    Assert.IsNull(published);
    StringAssert.Contains(vm.TranscriptOperatorMessage ?? string.Empty, TranscriptStaleContextExplainability.JumpPrefix);
    StringAssert.Contains(vm.TranscriptOperatorMessage ?? string.Empty, "selected transcription");
  }

  [TestMethod]
  public async Task NavigateFromApplyJobStatusEntry_NoSegmentTarget_FailsClosed()
  {
    InstallHarness(jobFails: false);
    var bus = AppServices.TryGetEventAggregator();
    NavigateToEvent? published = null;
    using var sub = bus!.Subscribe<NavigateToEvent>(ev => published = ev);
    var vm = CreateSut();
    vm.SelectedProjectId = "p1";
    vm.Transcriptions.Add(BuildTranscription());
    vm.SelectedTranscription = vm.Transcriptions[0];
    vm.NavigateFromApplyJobStatusEntry(BuildApplyJobStatusEntry(segmentIds: Array.Empty<string>()));
    await PumpDispatcherOnceAsync().ConfigureAwait(false);

    Assert.IsNull(published);
    StringAssert.Contains(vm.TranscriptOperatorMessage ?? string.Empty, TranscriptStaleContextExplainability.JumpPrefix);
    StringAssert.Contains(vm.TranscriptOperatorMessage ?? string.Empty, "does not identify");
  }

  [TestMethod]
  public async Task NavigateFromApplyJobStatusEntry_ClipMismatch_FailsClosed()
  {
    InstallHarness(jobFails: false);
    var bus = AppServices.TryGetEventAggregator();
    NavigateToEvent? published = null;
    using var sub = bus!.Subscribe<NavigateToEvent>(ev => published = ev);
    var vm = CreateSut();
    vm.SelectedProjectId = "p1";
    vm.Transcriptions.Add(BuildTranscription());
    vm.SelectedTranscription = vm.Transcriptions[0];
    vm.NavigateFromApplyJobStatusEntry(BuildApplyJobStatusEntry(clipId: "stale-clip"));
    await PumpDispatcherOnceAsync().ConfigureAwait(false);

    Assert.IsNull(published);
    StringAssert.Contains(vm.TranscriptOperatorMessage ?? string.Empty, TranscriptStaleContextExplainability.JumpPrefix);
    StringAssert.Contains(vm.TranscriptOperatorMessage ?? string.Empty, "resolved clip");
  }

  [TestMethod]
  public async Task NavigateFromApplyJobStatusEntry_UnlinkedResolver_FailsClosed()
  {
    InstallHarness(jobFails: false, linkedProject: BuildLinkedProjectWithoutTranscriptLinks());
    var bus = AppServices.TryGetEventAggregator();
    NavigateToEvent? published = null;
    using var sub = bus!.Subscribe<NavigateToEvent>(ev => published = ev);
    var vm = CreateSut();
    vm.SelectedProjectId = "p1";
    vm.Transcriptions.Add(BuildTranscription());
    vm.SelectedTranscription = vm.Transcriptions[0];
    vm.NavigateFromApplyJobStatusEntry(BuildApplyJobStatusEntry(clipId: null));
    await PumpDispatcherOnceAsync().ConfigureAwait(false);

    Assert.IsNull(published);
    StringAssert.Contains(vm.TranscriptOperatorMessage ?? string.Empty, TranscriptStaleContextExplainability.JumpPrefix);
    StringAssert.Contains(vm.TranscriptOperatorMessage ?? string.Empty, "not linked");
  }

  [TestMethod]
  public async Task NavigateFromApplyJobStatusEntry_EmptyTranscriptionId_SetsExplainabilityMessage()
  {
    InstallHarness(jobFails: false);
    var vm = CreateSut();
    vm.SelectedProjectId = "p1";
    vm.Transcriptions.Add(BuildTranscription());
    vm.NavigateFromApplyJobStatusEntry(
        BuildApplyJobStatusEntry(transcriptionId: " "));
    await PumpDispatcherOnceAsync().ConfigureAwait(false);

    Assert.AreEqual(TranscriptStaleContextExplainability.JumpNoTranscriptionId, vm.TranscriptOperatorMessage);
  }
}
