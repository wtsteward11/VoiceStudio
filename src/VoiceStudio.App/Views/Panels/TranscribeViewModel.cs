using System;
using System.Diagnostics;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.Generic;
using VoiceStudio.Core.Events;
using VoiceStudio.Core.Models;
using VoiceStudio.Core.Panels;
using VoiceStudio.Core.Services;
using VoiceStudio.Core.Transcription;
using VoiceStudio.App.Core.Models;
using VoiceStudio.App.Core.Services;
using VoiceStudio.App.Services;
using VoiceStudio.App.Services.UndoableActions;
using VoiceStudio.App.Utilities;
using VoiceStudio.App.ViewModels;

namespace VoiceStudio.App.Views.Panels
{
  public partial class TranscribeViewModel : BaseViewModel, IPanelView, IPanelLifecycle
  {
    private readonly ITranscriptionClient _transcriptionClient;
    private readonly IProjectAudioClient _projectAudioClient;
    private readonly IProjectRepository? _projectRepository;
    private readonly IErrorLoggingService? _logService;
    private readonly ToastNotificationService? _toastNotificationService;
    private bool _isInitialized;
    private ISubscriptionToken? _projectChangedToken;
    private ISubscriptionToken? _assetAddedToken;
    private ISubscriptionToken? _clipTranscriptToken;
    private ISubscriptionToken? _transcriptTruthStateToken;
    private string? _truthRefreshTrackId;
    private string? _truthRefreshClipId;

    /// <summary>GAP-045 reload/rehydrate: cancels in-flight list fetch when project/audio scope changes.</summary>
    private readonly object _rehydrateLock = new();

    private CancellationTokenSource? _rehydrateCts;

    /// <summary>GAP-045 feedback: segment ids regenerated this session (cleared on transcription/project context change or truth refresh).</summary>
    private readonly HashSet<string> _sessionRegeneratedSegmentIds = new(StringComparer.Ordinal);

    /// <summary>Must match <see cref="ImportWorkflowService"/>.</summary>
    private const string AssetSourceImportWorkflow = "import-workflow";

    private static bool IsRecordingDerivedAssetSource(string? sourcePanelId) =>
        string.Equals(sourcePanelId, PanelIds.Recording, StringComparison.Ordinal)
        || string.Equals(sourcePanelId, "recording-panel", StringComparison.OrdinalIgnoreCase);
    private readonly UndoRedoService? _undoRedoService;
    private readonly MultiSelectService _multiSelectService;
    private MultiSelectState? _multiSelectState;
    private readonly IShellProgressPublisher _shellProgress;
    private readonly IDialogueServiceClient? _dialogueServiceClient;

    public string PanelId => PanelIds.Transcribe;
    public string DisplayName => ResourceHelper.GetString("Panel.Transcribe.DisplayName", "Transcribe");
    public PanelRegion Region => PanelRegion.Bottom;

    [ObservableProperty]
    private ObservableCollection<TranscriptionResponse> transcriptions = new();

    [ObservableProperty]
    private TranscriptionResponse? selectedTranscription;

    [ObservableProperty]
    private string? selectedAudioId;

    [ObservableProperty]
    private string? selectedProjectId;

    [ObservableProperty]
    private string selectedEngine = "whisper";

    [ObservableProperty]
    private string? selectedLanguage;

    [ObservableProperty]
    private bool wordTimestamps;

    [ObservableProperty]
    private bool diarization;

    [ObservableProperty]
    private bool useVad;

    /// <summary>When true, <see cref="StartJobCommand"/> requests a simulation job (no engine required).</summary>
    [ObservableProperty]
    private bool simulateTranscription;

    /// <summary>Set after the last job path completion when the backend used simulation.</summary>
    [ObservableProperty]
    private bool lastTranscriptionWasSimulated;

    /// <summary>Target timeline track id for <c>POST /api/dialogue/transcripts/{{id}}/create-timeline-clips</c>.</summary>
    [ObservableProperty]
    private string createTimelineClipsTrackId = string.Empty;

    /// <summary>Clip ids returned from the last successful create-timeline-clips call.</summary>
    [ObservableProperty]
    private List<string> lastCreatedTimelineClipIds = new();

    /// <summary>Backend job <c>mode</c> (<c>real</c>, <c>simulation</c>, <c>unavailable</c>) for operator visibility.</summary>
    [ObservableProperty]
    private string? transcriptionJobMode;

    /// <summary>Canonical job progress (0..1) while a durable transcription job is in flight.</summary>
    [ObservableProperty]
    private float transcriptionJobProgress;

    /// <summary>GAP-067 slice 5: progressive disclosure for optional project scope and STT toggles.</summary>
    [ObservableProperty]
    private bool isAdvancedTranscribeOptionsExpanded;

    [ObservableProperty]
    private bool isLoading;

    [ObservableProperty]
    private string? errorMessage;

    /// <summary>Pass 05 C3 Option B: one-line honesty about library vs project audio (bindable).</summary>
    [ObservableProperty]
    private string? audioPersistenceSemanticsHint;

    /// <summary>Product trust Pass 01 slice 1: always-visible scope note — batch/drag-drop parity is not the same guarantee as transcribe/import project copy until A4 is signed.</summary>
    public string PersistenceScopeFootnote =>
        ResourceHelper.GetString(
            "Transcribe.Pass01.PersistenceScopeFootnote",
            "Dragging or dropping library items onto the project may not copy audio to the project yet. "
            + "With a project selected, transcribing library audio can copy to project audio (Pass 05 Option A paths)—not all import paths behave the same until batch/drag-drop is separately enabled.");

    [ObservableProperty]
    private string transcriptionText = string.Empty;

    // Multi-select support
    [ObservableProperty]
    private int selectedTranscriptionCount;

    [ObservableProperty]
    private bool hasMultipleTranscriptionSelection;

    public bool IsTranscriptionSelected(string transcriptionId) => _multiSelectState?.SelectedIds.Contains(transcriptionId) ?? false;

    // GAP-CS-003: Dynamic engine discovery from backend API
    // Replaces hardcoded list with backend-sourced engines
    public ObservableCollection<string> Engines { get; } = new();
    
    [ObservableProperty]
    private bool isLoadingEngines;

    /// <summary>GAP-033: segment ids highlighted from timeline clip selection.</summary>
    [ObservableProperty]
    private IReadOnlyList<string> linkedTranscriptSegmentIds = System.Array.Empty<string>();

    /// <summary>GAP-045: operator-visible status for transcript→timeline targeting and edit-intent (non-error path).</summary>
    [ObservableProperty]
    private string? transcriptOperatorMessage;

    /// <summary>GAP-045 Option B: stale transcript truth / refresh messaging bindable to InfoBar.</summary>
    [ObservableProperty]
    private string? transcriptTruthReconciliationHint;

    /// <summary>GAP-045 Option B: show transcript truth InfoBar when <see cref="TranscriptTruthReconciliationHint"/> is set.</summary>
    [ObservableProperty]
    private bool showTranscriptTruthReconciliationBar;

    /// <summary>GAP-045 inline edit/apply: stable segment id being edited in the Transcribe panel (UI-owned draft until Apply).</summary>
    [ObservableProperty]
    private string? editingSegmentId;

    /// <summary>GAP-045 inline edit/apply: canonical segment text when edit started (for dirty compare).</summary>
    [ObservableProperty]
    private string? editingSegmentOriginalText;

    /// <summary>GAP-045 inline edit/apply: operator draft; Apply sends trimmed value as regen <c>replacement_text</c>.</summary>
    [ObservableProperty]
    private string? editingSegmentDraftText;

    /// <summary>GAP-045 multi-segment: inclusive end segment id when editing a contiguous range; null or same as <see cref="EditingSegmentId"/> = single segment.</summary>
    [ObservableProperty]
    private string? editingRangeEndSegmentId;

    /// <summary>GAP-045 inline edit/apply: status strip hint for flyout edit session.</summary>
    [ObservableProperty]
    private string? segmentEditOperatorHint;

    /// <summary>GAP-047 review lane: session-local filler removal toggles (flyout only; not persisted).</summary>
    public ObservableCollection<FillerRemovalToggleItem> FillerRemovalToggles { get; } = new();

    /// <summary>GAP-047: preview of draft text if only enabled filler terms are stripped.</summary>
    [ObservableProperty]
    private string? fillerRemovalPreviewText;

    /// <summary>GAP-045 transcript edit history: fallback when <see cref="TranscriptEditHistoryService"/> is not registered (tests).</summary>
    private readonly ObservableCollection<TranscriptEditHistoryEntry> _emptyTranscriptEditHistory = new();

    /// <summary>GAP-045: session-visible edit history (newest-first ring buffer via service).</summary>
    public ObservableCollection<TranscriptEditHistoryEntry> TranscriptEditHistoryEntries =>
        AppServices.TryGetTranscriptEditHistoryService()?.Entries ?? _emptyTranscriptEditHistory;

    /// <summary>GOV-VOICESTUDIO-EDIT-APPLY-JOB-STATUS-01: session-local transcript apply/regenerate job rows (newest-first, capped).</summary>
    public ObservableCollection<TranscriptApplyJobStatusEntry> TranscriptApplyJobStatusEntries { get; } = new();

    private const int MaxTranscriptApplyJobStatusEntries = 15;

    /// <summary>GAP-045 feedback: segment id currently running regeneration (busy UI).</summary>
    [ObservableProperty]
    private string? regeneratingSegmentId;

    /// <summary>GAP-045 feedback: bump when segment rows must rebind (ItemsRepeater + non-INPC segments).</summary>
    [ObservableProperty]
    private int transcriptSegmentLayoutRevision;

    public bool IsEditingSegment => !string.IsNullOrWhiteSpace(EditingSegmentId);

    public bool IsEditDirty =>
        IsEditingSegment
        && !string.Equals(
            (EditingSegmentDraftText ?? string.Empty).Trim(),
            (EditingSegmentOriginalText ?? string.Empty).Trim(),
            StringComparison.Ordinal);

    /// <summary>True when flyout is editing more than one segment (contiguous range on same clip).</summary>
    public bool IsMultiSegmentRangeEdit =>
        IsEditingSegment
        && !string.IsNullOrWhiteSpace(EditingRangeEndSegmentId)
        && !string.Equals(EditingSegmentId, EditingRangeEndSegmentId, StringComparison.Ordinal);

    public ObservableCollection<SupportedLanguage> Languages { get; } = new();

    public TranscribeViewModel(
        IViewModelContext context,
        ITranscriptionClient transcriptionClient,
        IProjectAudioClient projectAudioClient,
        IProjectRepository? projectRepository = null,
        IShellProgressPublisher? shellProgressPublisher = null,
        IDialogueServiceClient? dialogueServiceClient = null)
        : base(context)
    {
      _transcriptionClient = transcriptionClient ?? throw new ArgumentNullException(nameof(transcriptionClient));
      _projectAudioClient = projectAudioClient ?? throw new ArgumentNullException(nameof(projectAudioClient));
      _projectRepository = projectRepository ?? AppServices.TryGetProjectRepository();
      _shellProgress = shellProgressPublisher ?? NullShellProgressPublisher.Instance;
      _dialogueServiceClient = dialogueServiceClient ?? AppServices.GetService<IDialogueServiceClient>();
      _logService = ServiceProvider.TryGetErrorLoggingService();

      // Get multi-select service
      var multiSelectService = AppServices.TryGetMultiSelectService();
      _multiSelectService = multiSelectService ?? throw new InvalidOperationException("MultiSelectService is required but not registered");
      _multiSelectState = _multiSelectService.GetState(PanelId);

      // Get services (may be null if not initialized)
      try
      {
        _toastNotificationService = AppServices.TryGetToastNotificationService();
        _undoRedoService = AppServices.TryGetUndoRedoService();
      }
      catch
      {
        // Services may not be initialized yet - that's okay
        _toastNotificationService = null;
        _undoRedoService = null;
      }

      LoadLanguagesCommand = new EnhancedAsyncRelayCommand(async (ct) =>
      {
        using var profiler = PerformanceProfiler.StartCommand("LoadLanguages");
        await LoadLanguagesAsync(ct);
      }, () => !IsLoading);
      TranscribeCommand = new EnhancedAsyncRelayCommand(async (ct) =>
      {
        using var profiler = PerformanceProfiler.StartCommand("Transcribe");
        await TranscribeAsync(ct);
      }, () => !IsLoading && CanTranscribe());
      StartJobCommand = new EnhancedAsyncRelayCommand(async (ct) =>
      {
        using var profiler = PerformanceProfiler.StartCommand("StartTranscriptionJob");
        await StartTranscriptionJobAsync(ct);
      }, () => !IsLoading && CanTranscribe());
      LoadTranscriptionsCommand = new EnhancedAsyncRelayCommand(async (ct) =>
      {
        using var profiler = PerformanceProfiler.StartCommand("LoadTranscriptions");
        await LoadTranscriptionsAsync(ct);
      }, () => !IsLoading);
      // GAP-CS-003: Dynamic engine discovery command
      LoadEnginesCommand = new EnhancedAsyncRelayCommand(async (ct) =>
      {
        using var profiler = PerformanceProfiler.StartCommand("LoadEngines");
        await LoadEnginesAsync(ct);
      }, () => !IsLoadingEngines);
      DeleteTranscriptionCommand = new EnhancedAsyncRelayCommand<TranscriptionResponse>(async (transcription, ct) =>
      {
        using var profiler = PerformanceProfiler.StartCommand("DeleteTranscription");
        await DeleteTranscriptionAsync(transcription, ct);
      }, t => t != null && !IsLoading);

      // Multi-select commands
      SelectAllTranscriptionsCommand = new RelayCommand(SelectAllTranscriptions, () => Transcriptions?.Count > 0);
      ClearTranscriptionSelectionCommand = new RelayCommand(ClearTranscriptionSelection);

      // Send to Timeline command (Audit X-3: Transcribe -> Timeline)
      SendToTimelineCommand = new RelayCommand(
          SendSelectedTranscriptionToTimeline,
          () => SelectedTranscription != null);

      CreateTimelineClipsCommand = new EnhancedAsyncRelayCommand(
          async (ct) =>
          {
            using var profiler = PerformanceProfiler.StartCommand("CreateTimelineClips");
            await CreateTimelineClipsFromTranscriptAsync(ct).ConfigureAwait(true);
          },
          () => !IsLoading && SelectedTranscription != null && _dialogueServiceClient != null);

      RefreshTranscriptTruthCommand = new EnhancedAsyncRelayCommand(
          async (ct) =>
          {
            using var profiler = PerformanceProfiler.StartCommand("RefreshTranscriptTruth");
            await RefreshTranscriptTruthAsync(ct);
          },
          () => !IsLoading && CanRefreshTranscriptTruth());

      ApplyEditedSegmentCommand = new EnhancedAsyncRelayCommand(
          async (ct) =>
          {
            using var profiler = PerformanceProfiler.StartCommand("ApplyEditedSegment");
            _ = await ApplyEditedSegmentAsync(ct).ConfigureAwait(true);
          },
          () =>
              !IsLoading
              && IsEditingSegment
              && IsEditDirty
              && string.IsNullOrWhiteSpace(RegeneratingSegmentId));

      RemoveFillersFromEditingDraftCommand = new RelayCommand(
          RemoveFillersFromEditingDraftCore,
          () =>
              !IsLoading
              && IsEditingSegment
              && string.IsNullOrWhiteSpace(RegeneratingSegmentId));

      ClearTranscriptEditHistoryCommand = new RelayCommand(
          () => AppServices.TryGetTranscriptEditHistoryService()?.ClearSession());

      ClearTranscriptApplyJobStatusCommand = new RelayCommand(() => TranscriptApplyJobStatusEntries.Clear());

      // Subscribe to selection changes
      _multiSelectService.SelectionChanged += (s, e) =>
      {
        if (e.PanelId == PanelId)
        {
          UpdateTranscriptionSelectionProperties();
          OnPropertyChanged(nameof(SelectedTranscriptionCount));
          OnPropertyChanged(nameof(HasMultipleTranscriptionSelection));
        }
      };
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
      if (_isInitialized)
        return;
      SyncSelectedProjectFromContext();
      EnsureProjectChangedSubscription();
      EnsureAssetAddedSubscription();
      _isInitialized = true;
      await LoadLanguagesAsync(cancellationToken);
      await LoadEnginesAsync(cancellationToken);
      EnsureClipTranscriptSelectionSubscription();
      EnsureTranscriptTruthStateSubscription();
      RefreshTranscriptTruthHints();
      ScheduleBackendTranscriptRehydrate("initialize");
    }

    /// <inheritdoc />
    public async Task OnActivatedAsync(CancellationToken cancellationToken = default)
    {
      SyncSelectedProjectFromContext();
      EnsureProjectChangedSubscription();
      EnsureAssetAddedSubscription();
      if (!_isInitialized)
        await InitializeAsync(cancellationToken);
      EnsureClipTranscriptSelectionSubscription();
      EnsureTranscriptTruthStateSubscription();
      RefreshTranscriptTruthHints();
      ScheduleBackendTranscriptRehydrate("activated");
    }

    /// <inheritdoc />
    public Task OnDeactivatedAsync(CancellationToken cancellationToken = default)
    {
      ReleasePanelEventSubscriptions();
      return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task RefreshAsync(CancellationToken cancellationToken = default)
    {
      SyncSelectedProjectFromContext();
      RefreshTranscriptTruthHints();
      ScheduleBackendTranscriptRehydrate("refresh");
      return Task.CompletedTask;
    }

    /// <summary>Pass 05 C1: align with active project from <see cref="IContextManager"/>.</summary>
    private void SyncSelectedProjectFromContext()
    {
      var ctx = AppServices.TryGetContextManager();
      if (ctx == null)
        return;
      var activeId = ctx.ActiveProjectId;
      if (SelectedProjectId != activeId)
        SelectedProjectId = activeId;
    }

    private void EnsureProjectChangedSubscription()
    {
      if (_projectChangedToken != null)
        return;
      var agg = AppServices.TryGetEventAggregator();
      if (agg == null)
        return;
      _projectChangedToken = agg.Subscribe<ProjectChangedEvent>(OnProjectChanged);
    }

    /// <summary>Pass 05 C2: prefill <see cref="SelectedAudioId"/> from recording/import without overwriting user input.</summary>
    private void EnsureAssetAddedSubscription()
    {
      if (_assetAddedToken != null)
        return;
      var agg = AppServices.TryGetEventAggregator();
      if (agg == null)
        return;
      _assetAddedToken = agg.Subscribe<AssetAddedEvent>(OnAssetAdded);
    }

    private void ReleasePanelEventSubscriptions()
    {
      var agg = AppServices.TryGetEventAggregator();
      if (_projectChangedToken != null)
      {
        agg?.Unsubscribe(_projectChangedToken);
        _projectChangedToken = null;
      }

      if (_assetAddedToken != null)
      {
        agg?.Unsubscribe(_assetAddedToken);
        _assetAddedToken = null;
      }

      if (_clipTranscriptToken != null)
      {
        agg?.Unsubscribe(_clipTranscriptToken);
        _clipTranscriptToken = null;
      }

      if (_transcriptTruthStateToken != null)
      {
        agg?.Unsubscribe(_transcriptTruthStateToken);
        _transcriptTruthStateToken = null;
      }
    }

    private void EnsureClipTranscriptSelectionSubscription()
    {
      if (_clipTranscriptToken != null)
        return;
      var agg = AppServices.TryGetEventAggregator();
      if (agg == null)
        return;
      _clipTranscriptToken = agg.Subscribe<ClipTranscriptSelectionEvent>(OnClipTranscriptSelectionFromTimeline);
    }

    private void OnClipTranscriptSelectionFromTimeline(ClipTranscriptSelectionEvent e)
    {
      Dispatcher.TryEnqueue(() =>
      {
        LinkedTranscriptSegmentIds = e.SegmentIds;
        var match = Transcriptions.FirstOrDefault(t => t.Id == e.TranscriptionId);
        if (match != null)
          SelectedTranscription = match;
        RefreshTranscriptTruthHints();
      });
    }

    private void EnsureTranscriptTruthStateSubscription()
    {
      if (_transcriptTruthStateToken != null)
        return;
      var agg = AppServices.TryGetEventAggregator();
      if (agg == null)
        return;
      _transcriptTruthStateToken = agg.Subscribe<TranscriptTruthStateChangedEvent>(OnTranscriptTruthStateChangedForUi);
    }

    private void OnTranscriptTruthStateChangedForUi(TranscriptTruthStateChangedEvent e)
    {
      Dispatcher.TryEnqueue(RefreshTranscriptTruthHints);
    }

    private void RefreshTranscriptTruthHints()
    {
      _truthRefreshTrackId = null;
      _truthRefreshClipId = null;

      var gate = AppServices.TryGetTimelineSelectedProjectGate();
      var project = gate?.SelectedProject;
      if (project == null
          || string.IsNullOrWhiteSpace(SelectedAudioId)
          || string.IsNullOrWhiteSpace(SelectedProjectId)
          || !string.Equals(project.Id, SelectedProjectId, StringComparison.Ordinal))
      {
        TranscriptTruthReconciliationHint = null;
        ShowTranscriptTruthReconciliationBar = false;
        RefreshTranscriptTruthCommand.NotifyCanExecuteChanged();
        return;
      }

      var staleClips = new List<(string TrackId, string ClipId)>();
      foreach (var t in project.Tracks ?? Enumerable.Empty<AudioTrack>())
      {
        foreach (var c in t.Clips ?? Enumerable.Empty<AudioClip>())
        {
          if (!string.Equals(c.AudioId, SelectedAudioId, StringComparison.Ordinal))
            continue;
          if (c.TranscriptTruth != TranscriptTruthState.StaleAfterClipRegeneration)
            continue;
          staleClips.Add((t.Id, c.Id));
        }
      }

      if (staleClips.Count == 0)
      {
        TranscriptTruthReconciliationHint = null;
        ShowTranscriptTruthReconciliationBar = false;
        RefreshTranscriptTruthCommand.NotifyCanExecuteChanged();
        return;
      }

      if (staleClips.Count > 1)
      {
        TranscriptTruthReconciliationHint =
            "Multiple timeline clips with this audio id are marked stale; reconciliation is ambiguous. Resolve duplicates before refresh.";
        ShowTranscriptTruthReconciliationBar = true;
        RefreshTranscriptTruthCommand.NotifyCanExecuteChanged();
        return;
      }

      _truthRefreshTrackId = staleClips[0].TrackId;
      _truthRefreshClipId = staleClips[0].ClipId;
      TranscriptTruthReconciliationHint =
          "Transcript linkage was removed after clip audio changed. Refresh to transcribe current audio and rebuild linkage.";
      ShowTranscriptTruthReconciliationBar = true;
      RefreshTranscriptTruthCommand.NotifyCanExecuteChanged();
    }

    private bool CanRefreshTranscriptTruth() =>
        !string.IsNullOrWhiteSpace(_truthRefreshTrackId)
        && !string.IsNullOrWhiteSpace(_truthRefreshClipId)
        && !string.IsNullOrWhiteSpace(SelectedProjectId);

    private async Task RefreshTranscriptTruthAsync(CancellationToken cancellationToken)
    {
      var coord = AppServices.TryGetTranscriptTruthRefreshCoordinator();
      var gate = AppServices.TryGetTimelineSelectedProjectGate();
      var project = gate?.SelectedProject;
      if (coord == null || project == null || !CanRefreshTranscriptTruth())
      {
        _toastNotificationService?.ShowWarning(
            ResourceHelper.GetString("Transcribe.TruthRefreshUnavailable", "Refresh unavailable."),
            ResourceHelper.GetString("Transcribe.TruthRefreshUnavailableTitle", "Transcript refresh"));
        return;
      }

      var err = await coord
          .TryRefreshStaleTranscriptForClipAsync(
              project,
              _truthRefreshTrackId!,
              _truthRefreshClipId!,
              SelectedEngine,
              SelectedLanguage,
              WordTimestamps,
              Diarization,
              UseVad,
              PanelId,
              SelectedProjectId,
              cancellationToken)
          .ConfigureAwait(true);

      if (err != null)
      {
        _toastNotificationService?.ShowToast(ToastType.Error, "Transcript refresh", err);
        return;
      }

      ClearSessionRegeneratedSegmentTracking();
      await LoadTranscriptionsAsync(cancellationToken).ConfigureAwait(true);
      RefreshTranscriptTruthHints();
      _toastNotificationService?.ShowSuccess(
          ResourceHelper.GetString(
              "Transcribe.TruthRefreshCompleteDetail",
              "Transcript refreshed; timeline linkage rebuilt for the clip."),
          ResourceHelper.GetString("Transcribe.TruthRefreshCompleteTitle", "Transcript truth"));
    }

    /// <summary>
    /// GAP-033: seek timeline playhead from transcript context (source-audio seconds).
    /// </summary>
    public void RequestSeekTimelineToSeconds(double timeSeconds)
    {
      if (timeSeconds < 0 || double.IsNaN(timeSeconds) || double.IsInfinity(timeSeconds))
        return;
      var agg = AppServices.TryGetEventAggregator();
      if (agg == null)
        return;
      agg.Publish(new NavigateToEvent(
          PanelId,
          "timeline",
          new Dictionary<string, object>
          {
            { "action", "seekPlayhead" },
            { "timeSeconds", timeSeconds },
          }));
    }

    /// <summary>
    /// GAP-045: segment tap → deterministic clip + timeline seek (segment times in source-audio space; seek uses clip <c>StartTime</c> offset).
    /// </summary>
    /// <param name="expectedClipId">When non-null, jump-from-row preflight: resolved clip must still match (GOV-VOICESTUDIO-EDIT-APPLY-CONTEXT-JUMP-01).</param>
    public void OnTargetTranscriptionSegmentTapped(TranscriptionSegment? segment, string? expectedClipId = null)
    {
      if (segment == null || SelectedTranscription == null)
        return;

      var resolver = AppServices.TryGetTranscriptSegmentTargetResolver();
      if (resolver == null)
      {
        TranscriptOperatorMessage = TranscriptStaleContextExplainability.JumpResolverNotRegistered;
        return;
      }

      var r = resolver.Resolve(SelectedTranscription.Id, segment.Id, segment.Start, segment.End);
      if (r.Kind != TranscriptSegmentTargetResolutionKind.Resolved)
      {
        TranscriptOperatorMessage = TranscriptStaleContextExplainability.JumpResolverFailure(r);
        return;
      }

      if (!string.IsNullOrWhiteSpace(expectedClipId)
          && !string.Equals(r.ClipId, expectedClipId, StringComparison.Ordinal))
      {
        TranscriptOperatorMessage = TranscriptStaleContextExplainability.JumpClipMismatchRowVsResolve;
        return;
      }

      TranscriptOperatorMessage = "Timeline: focused linked clip and applied seek.";
      PublishTranscriptResolutionNavigate(r);
    }

    /// <summary>
    /// GAP-045: record edit intent after resolution. For <see cref="TranscriptEditIntentKind.ReplaceRange"/>, pass <paramref name="replacementText"/> (required non-empty).
    /// </summary>
    public bool TryRecordTranscriptEditIntent(
        TranscriptEditIntentKind kind,
        TranscriptionSegment? segment,
        out string? errorMessage,
        string? replacementText = null)
    {
      errorMessage = null;
      if (segment == null || SelectedTranscription == null)
      {
        errorMessage = "Select a transcription and segment first.";
        return false;
      }

      var svc = AppServices.TryGetTranscriptEditIntentService();
      if (svc == null)
      {
        errorMessage = "Edit intent service is unavailable.";
        return false;
      }

      if (!svc.TryRecordIntent(
              kind,
              SelectedTranscription.Id,
              segment.Id,
              segment.Start,
              segment.End,
              out errorMessage,
              replacementText))
        return false;

      if (svc.Current != null)
      {
        TranscriptOperatorMessage = svc.Current.DownstreamExecutable
            ? $"Recorded {kind} intent (executable)."
            : $"Recorded {kind} intent (non-executing). {svc.Current.ExecutionBlockedReason}";
      }

      return true;
    }

    /// <summary>GAP-046: run backend regen job and apply audio to the linked timeline clip. Optional <paramref name="replacementText"/> overrides stored segment text for synthesis.</summary>
    /// <param name="rangeEndInclusiveIndex">When set with multi-segment apply, updates local segment display for indices from segment through this index (inclusive).</param>
    /// <param name="historyOperationKind">GAP-045: logical operation for session edit history (draft-only filler cleanup is not passed here).</param>
    public async Task<string?> RegenerateSegmentAudioAsync(
        TranscriptionSegment? segment,
        string? replacementText = null,
        CancellationToken cancellationToken = default,
        int? rangeEndInclusiveIndex = null,
        TranscriptEditOperationKind historyOperationKind = TranscriptEditOperationKind.RegenerateSegment,
        bool requestTimelineSubtitleCoherence = false)
    {
      if (segment == null || SelectedTranscription == null)
        return "Select a transcription and segment first.";

      var effectiveHistoryKind = string.IsNullOrWhiteSpace(replacementText)
          ? TranscriptEditOperationKind.RegenerateSegment
          : historyOperationKind;

      var coordinator = AppServices.TryGetTranscriptSegmentRegenerationCoordinator();
      if (coordinator == null)
      {
        EnqueueFailedTranscriptApplyJobStatus(
            effectiveHistoryKind,
            segment,
            rangeEndInclusiveIndex,
            TryResolveClipIdForApplyJobStatus(segment),
            "Regeneration is unavailable (coordinator not registered).",
            replacementText);
        return "Regeneration is unavailable (coordinator not registered).";
      }

      var segId = string.IsNullOrWhiteSpace(segment.Id) ? null : segment.Id;
      RegeneratingSegmentId = segId;
      ApplyEditedSegmentCommand.NotifyCanExecuteChanged();
      TranscriptSegmentLayoutRevision++;
      try
      {
        string? historyClipId = null;
        var snapshotResolver = AppServices.TryGetTranscriptSegmentTargetResolver();
        if (snapshotResolver != null && !string.IsNullOrWhiteSpace(segment.Id))
        {
          var snap = snapshotResolver.Resolve(
              SelectedTranscription.Id,
              segment.Id,
              segment.Start,
              segment.End);
          if (snap.Kind == TranscriptSegmentTargetResolutionKind.Resolved)
            historyClipId = snap.ClipId;
        }

        var opId = Guid.NewGuid().ToString("N");
        var segmentIdsForStatus = BuildHistorySegmentIds(segment, rangeEndInclusiveIndex, SelectedTranscription);
        var statusEntry = new TranscriptApplyJobStatusEntry(
            opId,
            effectiveHistoryKind,
            segmentIdsForStatus,
            historyClipId,
            DateTimeOffset.UtcNow,
            SelectedTranscription.Id,
            SelectedProjectId,
            replacementText,
            rangeEndInclusiveIndex,
            segment.Start,
            segment.End);
        InsertTranscriptApplyJobStatusEntry(statusEntry);
        var progress = CreateTranscriptApplyJobProgressReporter(opId, statusEntry);

        var err = await coordinator
            .TryExecuteAsync(
                SelectedTranscription,
                segment,
                PanelId,
                replacementText,
                cancellationToken,
                progress,
                opId,
                rangeEndInclusiveIndex)
            .ConfigureAwait(true);
        FinalizeTranscriptApplyJobStatusAfterCoordinator(statusEntry, err);
        if (err == null)
        {
          RefreshTranscriptTruthHints();
          if (rangeEndInclusiveIndex is { } endIdx
              && SelectedTranscription.Segments != null
              && endIdx >= 0)
          {
            var startIdx = SelectedTranscription.Segments.FindIndex(s => s.Id == segment.Id);
            if (startIdx >= 0 && endIdx >= startIdx)
              ApplyLocalRangeAfterRegen(startIdx, endIdx, replacementText);
            else
              ApplyLocalSegmentTextAfterSuccessfulRegen(segment, replacementText);
          }
          else
            ApplyLocalSegmentTextAfterSuccessfulRegen(segment, replacementText);
        }

        RecordTranscriptEditHistoryAfterRegenAttempt(
            effectiveHistoryKind,
            segment,
            rangeEndInclusiveIndex,
            err,
            historyClipId);
        if (err == null && requestTimelineSubtitleCoherence)
          PublishTimelineCoherenceAfterSegmentApplySuccess();
        return err;
      }
      finally
      {
        RegeneratingSegmentId = null;
        ApplyEditedSegmentCommand.NotifyCanExecuteChanged();
        TranscriptSegmentLayoutRevision++;
      }
    }

    private string? TryResolveClipIdForApplyJobStatus(TranscriptionSegment segment)
    {
      if (SelectedTranscription == null || string.IsNullOrWhiteSpace(segment.Id))
        return null;
      var snapshotResolver = AppServices.TryGetTranscriptSegmentTargetResolver();
      if (snapshotResolver == null)
        return null;
      var snap = snapshotResolver.Resolve(
          SelectedTranscription.Id,
          segment.Id,
          segment.Start,
          segment.End);
      return snap.Kind == TranscriptSegmentTargetResolutionKind.Resolved ? snap.ClipId : null;
    }

    private void EnqueueFailedTranscriptApplyJobStatus(
        TranscriptEditOperationKind kind,
        TranscriptionSegment segment,
        int? rangeEndInclusiveIndex,
        string? clipId,
        string message,
        string? replacementTextSnapshot)
    {
      if (SelectedTranscription == null)
        return;
      var opId = Guid.NewGuid().ToString("N");
      var segmentIds = BuildHistorySegmentIds(segment, rangeEndInclusiveIndex, SelectedTranscription);
      var entry = new TranscriptApplyJobStatusEntry(
          opId,
          kind,
          segmentIds,
          clipId,
          DateTimeOffset.UtcNow,
          SelectedTranscription.Id,
          SelectedProjectId,
          replacementTextSnapshot,
          rangeEndInclusiveIndex,
          segment.Start,
          segment.End)
      {
        OperatorStatus = TranscriptApplyOperatorJobStatus.Failed,
        StatusMessage = message,
        CompletedUtc = DateTimeOffset.UtcNow,
      };
      InsertTranscriptApplyJobStatusEntry(entry);
    }

    /// <summary>GOV-VOICESTUDIO-EDIT-APPLY-RETRY-RECOVERY-01: replay a failed job from its frozen snapshot (not live draft).</summary>
    public async Task RetryTranscriptApplyJobAsync(
        TranscriptApplyJobStatusEntry? entry,
        CancellationToken cancellationToken = default)
    {
      if (entry == null || !entry.CanShowRetry)
        return;

      if (!string.IsNullOrWhiteSpace(RegeneratingSegmentId))
      {
        _toastNotificationService?.ShowToast(
            ToastType.Warning,
            TranscriptStaleContextExplainability.RetryAnotherRegenerationInProgress,
            "Retry");
        return;
      }

      if (SelectedTranscription == null)
      {
        _toastNotificationService?.ShowToast(
            ToastType.Warning,
            TranscriptStaleContextExplainability.RetryNoTranscriptionSelected,
            "Retry unavailable");
        return;
      }

      if (!string.Equals(SelectedTranscription.Id, entry.TranscriptionId, StringComparison.Ordinal))
      {
        _toastNotificationService?.ShowToast(
            ToastType.Warning,
            TranscriptStaleContextExplainability.RetryTranscriptionMismatch,
            "Retry unavailable");
        return;
      }

      if (!string.IsNullOrEmpty(entry.ProjectId)
          && !string.Equals(SelectedProjectId ?? string.Empty, entry.ProjectId, StringComparison.Ordinal))
      {
        _toastNotificationService?.ShowToast(
            ToastType.Warning,
            TranscriptStaleContextExplainability.RetryProjectMismatch,
            "Retry unavailable");
        return;
      }

      var anchorId = entry.SegmentIds[0];
      var segment = SelectedTranscription.Segments?.FirstOrDefault(s => s.Id == anchorId);
      if (segment == null)
      {
        _toastNotificationService?.ShowToast(
            ToastType.Warning,
            TranscriptStaleContextExplainability.RetrySegmentMissing,
            "Retry unavailable");
        return;
      }

      var eps = TranscriptApplyJobStatusEntry.RetryAnchorTimingEpsilonSeconds;
      if (Math.Abs(segment.Start - entry.AnchorSegmentStart) > eps
          || Math.Abs(segment.End - entry.AnchorSegmentEnd) > eps)
      {
        _toastNotificationService?.ShowToast(
            ToastType.Warning,
            TranscriptStaleContextExplainability.RetryRangeTimingInvalidated,
            "Retry unavailable");
        return;
      }

      if (!string.IsNullOrWhiteSpace(entry.ClipId))
      {
        var resolver = AppServices.TryGetTranscriptSegmentTargetResolver();
        if (resolver == null)
        {
          _toastNotificationService?.ShowToast(
              ToastType.Warning,
              TranscriptStaleContextExplainability.RetryResolverUnavailable,
              "Retry unavailable");
          return;
        }

        if (string.IsNullOrWhiteSpace(segment.Id))
        {
          _toastNotificationService?.ShowToast(
              ToastType.Warning,
              TranscriptStaleContextExplainability.RetrySegmentIdMissing,
              "Retry unavailable");
          return;
        }

        var snap = resolver.Resolve(
            SelectedTranscription.Id,
            segment.Id,
            segment.Start,
            segment.End);
        if (snap.Kind != TranscriptSegmentTargetResolutionKind.Resolved
            || !string.Equals(snap.ClipId, entry.ClipId, StringComparison.Ordinal))
        {
          _toastNotificationService?.ShowToast(
              ToastType.Warning,
              TranscriptStaleContextExplainability.RetryClipMismatch,
              "Retry unavailable");
          return;
        }
      }

      _ = await RegenerateSegmentAudioAsync(
              segment,
              entry.ReplacementTextSnapshot,
              cancellationToken,
              entry.RangeEndInclusiveIndex,
              entry.OperationKind,
              requestTimelineSubtitleCoherence: true)
          .ConfigureAwait(true);
    }

    private void InsertTranscriptApplyJobStatusEntry(TranscriptApplyJobStatusEntry entry)
    {
      while (TranscriptApplyJobStatusEntries.Count >= MaxTranscriptApplyJobStatusEntries)
        TranscriptApplyJobStatusEntries.RemoveAt(TranscriptApplyJobStatusEntries.Count - 1);
      TranscriptApplyJobStatusEntries.Insert(0, entry);
    }

    private IProgress<TranscriptRegenerationJobProgressReport> CreateTranscriptApplyJobProgressReporter(
        string operationCorrelationId,
        TranscriptApplyJobStatusEntry entry)
    {
      return new Progress<TranscriptRegenerationJobProgressReport>(report =>
      {
        if (!string.Equals(report.OperationCorrelationId, operationCorrelationId, StringComparison.Ordinal))
          return;

        void Apply()
        {
          if (!string.IsNullOrWhiteSpace(report.JobId))
            entry.JobId = report.JobId;
          entry.JobProgress = report.Progress;
          entry.CurrentStep = report.CurrentStep;
          var op = TranscriptApplyJobStatusMapper.MapToOperator(report.BackendStatus);
          // Ordering: coordinator completion may enqueue Finalize before an earlier "pending" Apply runs.
          // Never let stale non-terminal progress clobber a terminal row (fixes flaky tests / UI flicker).
          if (entry.OperatorStatus is TranscriptApplyOperatorJobStatus.Succeeded
              or TranscriptApplyOperatorJobStatus.Failed)
          {
            if (op is TranscriptApplyOperatorJobStatus.Queued or TranscriptApplyOperatorJobStatus.Running)
              return;
          }

          entry.OperatorStatus = op;
          entry.StatusMessage = TranscriptApplyJobStatusMapper.BuildStatusMessage(report, op);
          if (op is TranscriptApplyOperatorJobStatus.Succeeded or TranscriptApplyOperatorJobStatus.Failed)
            entry.CompletedUtc = DateTimeOffset.UtcNow;
          else
            entry.CompletedUtc = null;

          switch (op)
          {
            case TranscriptApplyOperatorJobStatus.Running:
              _shellProgress.ReportProgress(operationCorrelationId, report.Progress);
              break;
            case TranscriptApplyOperatorJobStatus.Succeeded:
              _shellProgress.ReportComplete(operationCorrelationId);
              break;
            case TranscriptApplyOperatorJobStatus.Failed:
              _shellProgress.ReportError(operationCorrelationId);
              break;
            case TranscriptApplyOperatorJobStatus.Queued:
            default:
              break;
          }
        }

        if (!Dispatcher.TryEnqueue(Apply))
          Apply();
      });
    }

    private void FinalizeTranscriptApplyJobStatusAfterCoordinator(TranscriptApplyJobStatusEntry entry, string? err)
    {
      void FinalizeCore()
      {
        if (err != null)
        {
          if (entry.OperatorStatus != TranscriptApplyOperatorJobStatus.Failed)
          {
            entry.OperatorStatus = TranscriptApplyOperatorJobStatus.Failed;
            entry.StatusMessage = err;
            entry.CompletedUtc = DateTimeOffset.UtcNow;
          }
          else if (entry.CompletedUtc == null)
            entry.CompletedUtc = DateTimeOffset.UtcNow;
        }
        else
        {
          if (entry.OperatorStatus != TranscriptApplyOperatorJobStatus.Succeeded)
          {
            entry.OperatorStatus = TranscriptApplyOperatorJobStatus.Succeeded;
            entry.StatusMessage = "Regeneration complete; clip updated.";
            entry.CompletedUtc = DateTimeOffset.UtcNow;
          }
          else if (entry.CompletedUtc == null)
            entry.CompletedUtc = DateTimeOffset.UtcNow;
        }
      }

      if (!Dispatcher.TryEnqueue(FinalizeCore))
        FinalizeCore();
    }

    /// <summary>GAP-045 feedback: session-scoped regen marker for segment row accent.</summary>
    public bool WasSegmentRegeneratedInSession(string? segmentId) =>
        !string.IsNullOrWhiteSpace(segmentId)
        && _sessionRegeneratedSegmentIds.Contains(segmentId!);

    private void ClearSessionRegeneratedSegmentTracking()
    {
      var had = _sessionRegeneratedSegmentIds.Count > 0;
      _sessionRegeneratedSegmentIds.Clear();
      if (had)
        TranscriptSegmentLayoutRevision++;
    }

    private void ApplyLocalSegmentTextAfterSuccessfulRegen(TranscriptionSegment segment, string? replacementText)
    {
      if (SelectedTranscription?.Segments == null)
        return;
      var segId = segment.Id;
      if (string.IsNullOrWhiteSpace(segId))
        return;

      _ = _sessionRegeneratedSegmentIds.Add(segId);

      var trimmed = (replacementText ?? string.Empty).Trim();
      if (!string.IsNullOrEmpty(trimmed))
      {
        var list = SelectedTranscription.Segments;
        var idx = list.FindIndex(s => s.Id == segId);
        if (idx >= 0)
        {
          var old = list[idx];
          var copy = new List<TranscriptionSegment>(list.Count);
          for (var i = 0; i < list.Count; i++)
          {
            if (i != idx)
            {
              copy.Add(list[i]);
              continue;
            }

            copy.Add(
                new TranscriptionSegment
                {
                  Id = old.Id,
                  Start = old.Start,
                  End = old.End,
                  Text = trimmed,
                  Words = old.Words,
                });
          }

          SelectedTranscription.Segments = copy;
        }
      }

      TranscriptSegmentLayoutRevision++;
    }

    /// <summary>GAP-045 multi-segment: after successful regen, show full draft on the first row; clear text on other rows in range (execution row §5).</summary>
    private void ApplyLocalRangeAfterRegen(int startIdx, int endIdx, string? replacementText)
    {
      if (SelectedTranscription?.Segments == null)
        return;
      var list = SelectedTranscription.Segments;
      if (startIdx < 0 || endIdx >= list.Count || startIdx > endIdx)
        return;

      var trimmed = (replacementText ?? string.Empty).Trim();
      if (string.IsNullOrEmpty(trimmed))
        return;

      var copy = new List<TranscriptionSegment>(list.Count);
      for (var i = 0; i < list.Count; i++)
      {
        if (i < startIdx || i > endIdx)
        {
          copy.Add(list[i]);
          continue;
        }

        var old = list[i];
        _ = _sessionRegeneratedSegmentIds.Add(old.Id ?? string.Empty);

        var newText = i == startIdx ? trimmed : string.Empty;
        copy.Add(
            new TranscriptionSegment
            {
              Id = old.Id ?? string.Empty,
              Start = old.Start,
              End = old.End,
              Text = newText,
              Words = old.Words,
            });
      }

      SelectedTranscription.Segments = copy;
      TranscriptSegmentLayoutRevision++;
    }

    private static string CombineRangeOriginalText(IReadOnlyList<TranscriptionSegment> list, int start, int end)
    {
      var sb = new System.Text.StringBuilder();
      for (var i = start; i <= end; i++)
      {
        if (i > start)
          sb.Append(' ');
        sb.Append((list[i].Text ?? string.Empty).Trim());
      }

      return sb.ToString();
    }

    private bool TryGetRangeIndices(out int startIdx, out int endIdx, out string? error)
    {
      startIdx = endIdx = -1;
      error = null;
      if (SelectedTranscription?.Segments == null || string.IsNullOrWhiteSpace(EditingSegmentId))
      {
        error = "No segment edit in progress.";
        return false;
      }

      var list = SelectedTranscription.Segments;
      var ia = list.FindIndex(s => s.Id == EditingSegmentId);
      if (ia < 0)
      {
        error = "Editing segment not found on the selected transcription.";
        return false;
      }

      if (string.IsNullOrWhiteSpace(EditingRangeEndSegmentId)
          || string.Equals(EditingRangeEndSegmentId, EditingSegmentId, StringComparison.Ordinal))
      {
        startIdx = endIdx = ia;
        return true;
      }

      var ib = list.FindIndex(s => s.Id == EditingRangeEndSegmentId);
      if (ib < 0)
      {
        error = "Range end segment not found on the selected transcription.";
        return false;
      }

      startIdx = Math.Min(ia, ib);
      endIdx = Math.Max(ia, ib);
      return true;
    }

    /// <summary>GAP-045 multi-segment: every index in [start,end] must resolve to the same timeline clip.</summary>
    private string? TryValidateContiguousSameClip(int startIdx, int endIdx)
    {
      if (SelectedTranscription?.Segments == null)
        return "No transcription segments loaded.";
      var list = SelectedTranscription.Segments;
      var s = Math.Min(startIdx, endIdx);
      var e = Math.Max(startIdx, endIdx);
      if (s < 0 || e >= list.Count)
        return "Invalid segment range.";

      var resolver = AppServices.TryGetTranscriptSegmentTargetResolver();
      if (resolver == null)
        return "Transcript targeting is unavailable (resolver not registered).";

      string? clipId = null;
      for (var i = s; i <= e; i++)
      {
        var seg = list[i];
        if (string.IsNullOrWhiteSpace(seg.Id))
          return "A segment in the range has no id.";
        var r = resolver.Resolve(SelectedTranscription.Id, seg.Id, seg.Start, seg.End);
        if (r.Kind != TranscriptSegmentTargetResolutionKind.Resolved)
          return r.Reason ?? "A segment in the range could not be resolved to the timeline.";
        if (clipId == null)
          clipId = r.ClipId;
        else if (!string.Equals(clipId, r.ClipId, StringComparison.Ordinal))
          return "This range spans multiple timeline clips. Edit one clip at a time.";
      }

      return null;
    }

    /// <summary>GAP-045 inline edit/apply: start a UI-only buffered edit for one segment.</summary>
    public void BeginEditSegment(TranscriptionSegment? segment)
    {
      if (segment == null || SelectedTranscription == null)
        return;
      EditingRangeEndSegmentId = null;
      EditingSegmentId = string.IsNullOrWhiteSpace(segment.Id) ? null : segment.Id;
      EditingSegmentOriginalText = segment.Text ?? string.Empty;
      EditingSegmentDraftText = segment.Text ?? string.Empty;
      RebuildFillerRemovalTogglesAndPreview();
    }

    /// <summary>GAP-045 multi-segment: buffered edit for inclusive contiguous range [a,b] on the same clip (display order).</summary>
    public void BeginEditRange(TranscriptionSegment? a, TranscriptionSegment? b)
    {
      if (a == null || b == null || SelectedTranscription?.Segments == null || string.IsNullOrWhiteSpace(a.Id) || string.IsNullOrWhiteSpace(b.Id))
        return;
      var list = SelectedTranscription.Segments;
      var ia = list.FindIndex(s => s.Id == a.Id);
      var ib = list.FindIndex(s => s.Id == b.Id);
      if (ia < 0 || ib < 0)
      {
        TranscriptOperatorMessage = "Segments must belong to the current transcription list.";
        return;
      }

      var start = Math.Min(ia, ib);
      var end = Math.Max(ia, ib);
      var err = TryValidateContiguousSameClip(start, end);
      if (err != null)
      {
        TranscriptOperatorMessage = err;
        return;
      }

      EditingSegmentId = list[start].Id;
      EditingRangeEndSegmentId = list[end].Id;
      EditingSegmentOriginalText = CombineRangeOriginalText(list, start, end);
      EditingSegmentDraftText = EditingSegmentOriginalText;
      TranscriptOperatorMessage =
          start == end
              ? null
              : "Editing a contiguous segment range on one timeline clip — Apply replaces audio using the first segment as the regen anchor.";
      RebuildFillerRemovalTogglesAndPreview();
    }

    /// <summary>GAP-045 inline edit/apply: discard draft without backend calls.</summary>
    public void CancelSegmentEdit()
    {
      EditingSegmentId = null;
      EditingRangeEndSegmentId = null;
      EditingSegmentOriginalText = null;
      EditingSegmentDraftText = null;
      SegmentEditOperatorHint = null;
      ClearFillerRemovalReviewState();
    }

    private void ClearFillerRemovalReviewState()
    {
      FillerRemovalToggles.Clear();
      FillerRemovalPreviewText = null;
    }

    private void RebuildFillerRemovalTogglesAndPreview()
    {
      if (!IsEditingSegment)
        return;

      var draft = EditingSegmentDraftText ?? string.Empty;
      if (string.IsNullOrEmpty(draft))
      {
        FillerRemovalToggles.Clear();
        FillerRemovalPreviewText = null;
        return;
      }

      var plan = TranscriptFillerCleanupHelper.GetRemovalPlan(draft, null, null);
      var previous = FillerRemovalToggles.ToDictionary(t => t.Key, t => t.IsRemoveEnabled, StringComparer.OrdinalIgnoreCase);
      FillerRemovalToggles.Clear();
      if (plan.Count == 0)
      {
        FillerRemovalPreviewText = draft;
        return;
      }

      foreach (var entry in plan)
      {
        var risky = !entry.IsPhrase && TranscriptFillerCleanupHelper.RiskySingleTokenKeys.Contains(entry.CatalogKey);
        var defaultOn = !risky;
        var enabled = previous.TryGetValue(entry.CatalogKey, out var prior) ? prior : defaultOn;
        FillerRemovalToggles.Add(new FillerRemovalToggleItem(
            entry.CatalogKey,
            entry.OccurrenceCount,
            risky,
            enabled,
            RefreshFillerRemovalPreview));
      }

      RefreshFillerRemovalPreview();
    }

    private void RefreshFillerRemovalPreview()
    {
      if (!IsEditingSegment)
      {
        FillerRemovalPreviewText = null;
        return;
      }

      var draft = EditingSegmentDraftText ?? string.Empty;
      if (string.IsNullOrEmpty(draft))
      {
        FillerRemovalPreviewText = null;
        return;
      }

      FillerRemovalPreviewText = TranscriptFillerCleanupHelper.GetPreviewAfterRemoval(
          draft,
          BuildEnabledPhraseKeys(),
          BuildEnabledTokenKeys());
    }

    private HashSet<string> BuildEnabledPhraseKeys()
    {
      var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
      foreach (var t in FillerRemovalToggles)
      {
        if (t.IsRemoveEnabled && TranscriptFillerCleanupHelper.IsPhraseCatalogKey(t.Key))
          keys.Add(t.Key);
      }

      return keys;
    }

    private HashSet<string> BuildEnabledTokenKeys()
    {
      var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
      foreach (var t in FillerRemovalToggles)
      {
        if (t.IsRemoveEnabled && !TranscriptFillerCleanupHelper.IsPhraseCatalogKey(t.Key))
          keys.Add(t.Key);
      }

      return keys;
    }

    /// <summary>
    /// GAP-045 inline edit/apply: record ReplaceRange + run regen with draft text; clears edit state on success only.
    /// GAP-047 apply authority: this is the sole entry that may start segment regen / authoritative transcript update for inline edit
    /// (draft-only filler cleanup must never call into the coordinator; use <see cref="TryRemoveFillersFromEditingDraft"/> for draft text only).
    /// </summary>
    public async Task<string?> ApplyEditedSegmentAsync(CancellationToken cancellationToken = default)
    {
      if (!IsEditingSegment || SelectedTranscription == null || string.IsNullOrWhiteSpace(EditingSegmentId))
        return "No segment edit in progress.";

      var segment = SelectedTranscription.Segments?.FirstOrDefault(s => s.Id == EditingSegmentId);
      if (segment == null)
        return "Segment not found on the selected transcription.";

      var draft = (EditingSegmentDraftText ?? string.Empty).Trim();
      if (string.IsNullOrEmpty(draft))
        return "Replacement text cannot be empty.";

      var svc = AppServices.TryGetTranscriptEditIntentService();
      if (svc == null)
        return "Edit intent service is unavailable.";

      int? rangeEndInclusiveIndex = null;
      TranscriptionSegment? lastInRange = null;
      if (IsMultiSegmentRangeEdit)
      {
        if (!TryGetRangeIndices(out var i0, out var i1, out var rangeErr))
          return rangeErr;
        var clipErr = TryValidateContiguousSameClip(i0, i1);
        if (clipErr != null)
          return clipErr;
        lastInRange = SelectedTranscription.Segments![i1];
        if (!svc.TryRecordIntent(
                TranscriptEditIntentKind.ReplaceRange,
                SelectedTranscription.Id,
                segment.Id,
                segment.Start,
                lastInRange.End,
                out var intentErr,
                draft))
          return intentErr;
        rangeEndInclusiveIndex = i1;
      }
      else
      {
        if (!svc.TryRecordIntent(
                TranscriptEditIntentKind.ReplaceRange,
                SelectedTranscription.Id,
                segment.Id,
                segment.Start,
                segment.End,
                out var intentErr,
                draft))
          return intentErr;
      }

      if (svc.Current != null)
      {
        TranscriptOperatorMessage = svc.Current.DownstreamExecutable
            ? "Replace-range intent recorded (executable); applying regeneration."
            : $"Replace-range intent (non-executing). {svc.Current.ExecutionBlockedReason}";
      }

      var historyKind = IsMultiSegmentRangeEdit
          ? TranscriptEditOperationKind.MultiSegmentRangeApply
          : TranscriptEditOperationKind.SingleSegmentApply;
      var err = await RegenerateSegmentAudioAsync(
          segment,
          draft,
          cancellationToken,
          rangeEndInclusiveIndex,
          historyKind,
          requestTimelineSubtitleCoherence: true).ConfigureAwait(true);
      if (err == null)
        CancelSegmentEdit();
      return err;
    }

    private void RecordTranscriptEditHistoryAfterRegenAttempt(
        TranscriptEditOperationKind operationKind,
        TranscriptionSegment anchorSegment,
        int? rangeEndInclusiveIndex,
        string? coordinatorError,
        string? preResolvedClipId)
    {
      if (SelectedTranscription == null)
        return;
      var historySvc = AppServices.TryGetTranscriptEditHistoryService();
      if (historySvc == null)
        return;

      var segmentIds = BuildHistorySegmentIds(anchorSegment, rangeEndInclusiveIndex, SelectedTranscription);
      var succeeded = coordinatorError == null;
      var clipId = preResolvedClipId;

      var wasRegenerated = succeeded;
      var msg = succeeded
          ? (operationKind == TranscriptEditOperationKind.RegenerateSegment
              ? "Segment audio regenerated."
              : "Edit applied; timeline clip audio updated.")
          : (coordinatorError ?? "Regeneration failed.");
      if (msg.Length > 160)
        msg = msg.Substring(0, 160);

      historySvc.AddEntry(
          new TranscriptEditHistoryEntry
          {
            OperationKind = operationKind,
            ProjectId = SelectedProjectId,
            ClipId = clipId,
            TranscriptionId = SelectedTranscription.Id,
            SegmentIds = segmentIds,
            WasRegenerated = wasRegenerated,
            Succeeded = succeeded,
            MessageSummary = msg,
          });
    }

    private static IReadOnlyList<string> BuildHistorySegmentIds(
        TranscriptionSegment anchorSegment,
        int? rangeEndInclusiveIndex,
        TranscriptionResponse transcription)
    {
      if (transcription.Segments == null)
        return new List<string> { anchorSegment.Id ?? string.Empty };
      if (rangeEndInclusiveIndex is not int endIdx)
        return new List<string> { anchorSegment.Id ?? string.Empty };

      var startIdx = transcription.Segments.FindIndex(s => s.Id == anchorSegment.Id);
      if (startIdx < 0 || endIdx < startIdx || endIdx >= transcription.Segments.Count)
        return new List<string> { anchorSegment.Id ?? string.Empty };

      var ids = new List<string>();
      for (var i = startIdx; i <= endIdx; i++)
        ids.Add(transcription.Segments[i].Id ?? string.Empty);
      return ids;
    }

    private IReadOnlyList<string> GetEditingScopeSegmentIdsForHistory()
    {
      if (SelectedTranscription?.Segments == null || string.IsNullOrWhiteSpace(EditingSegmentId))
        return Array.Empty<string>();
      if (!IsMultiSegmentRangeEdit)
        return new List<string> { EditingSegmentId };
      if (!TryGetRangeIndices(out var i0, out var i1, out _))
        return new List<string> { EditingSegmentId };
      var ids = new List<string>();
      for (var i = i0; i <= i1; i++)
        ids.Add(SelectedTranscription.Segments[i].Id ?? string.Empty);
      return ids;
    }

    private void RecordFillerCleanupDraftHistory(TranscriptFillerCleanupHelper.FillerCleanupResult result)
    {
      var historySvc = AppServices.TryGetTranscriptEditHistoryService();
      if (historySvc == null || SelectedTranscription == null || result.RemovedOccurrenceCount <= 0)
        return;

      string? clipId = null;
      var resolver = AppServices.TryGetTranscriptSegmentTargetResolver();
      var segmentIds = GetEditingScopeSegmentIdsForHistory();
      if (segmentIds.Count > 0 && resolver != null && !string.IsNullOrWhiteSpace(EditingSegmentId))
      {
        var anchor = SelectedTranscription.Segments?.FirstOrDefault(s => s.Id == EditingSegmentId);
        if (anchor != null)
        {
          var r = resolver.Resolve(SelectedTranscription.Id, anchor.Id, anchor.Start, anchor.End);
          if (r.Kind == TranscriptSegmentTargetResolutionKind.Resolved)
            clipId = r.ClipId;
        }
      }

      var terms = string.IsNullOrEmpty(result.TermsSummary) ? $"{result.RemovedOccurrenceCount} occurrence(s)" : result.TermsSummary;
      var msg = $"Removed {result.RemovedOccurrenceCount}: {terms}";
      if (msg.Length > 160)
        msg = msg.Substring(0, 160);

      historySvc.AddEntry(
          new TranscriptEditHistoryEntry
          {
            OperationKind = TranscriptEditOperationKind.FillerCleanupDraft,
            ProjectId = SelectedProjectId,
            ClipId = clipId,
            TranscriptionId = SelectedTranscription.Id,
            SegmentIds = segmentIds,
            WasRegenerated = false,
            Succeeded = true,
            MessageSummary = msg,
          });
    }

    /// <summary>GAP-045: focus transcription + segment from a history row (timeline seek when resolvable).</summary>
    public void NavigateFromEditHistoryEntry(TranscriptEditHistoryEntry? entry)
    {
      if (entry == null)
        return;
      if (string.IsNullOrWhiteSpace(entry.TranscriptionId))
      {
        TranscriptOperatorMessage = TranscriptStaleContextExplainability.JumpNoTranscriptionId;
        return;
      }

      Dispatcher.TryEnqueue(() => NavigateFromEditHistoryEntryCore(entry));
    }

    private void NavigateFromEditHistoryEntryCore(TranscriptEditHistoryEntry entry) =>
        JumpTranscriptRowToSourceContext(
            entry.TranscriptionId,
            entry.ProjectId,
            entry.SegmentIds,
            entry.ClipId);

    /// <summary>GOV-VOICESTUDIO-EDIT-APPLY-CONTEXT-JUMP-01: jump from apply/regenerate job status row.</summary>
    public void NavigateFromApplyJobStatusEntry(TranscriptApplyJobStatusEntry? entry)
    {
      if (entry == null)
        return;
      if (string.IsNullOrWhiteSpace(entry.TranscriptionId))
      {
        TranscriptOperatorMessage = TranscriptStaleContextExplainability.JumpNoTranscriptionId;
        return;
      }

      Dispatcher.TryEnqueue(() => JumpTranscriptRowToSourceContext(
          entry.TranscriptionId,
          entry.ProjectId,
          entry.SegmentIds,
          entry.ClipId));
    }

    /// <summary>
    /// Shared fail-closed jump for edit history and job-status rows (no live draft; segment from current <see cref="SelectedTranscription"/>).
    /// </summary>
    private void JumpTranscriptRowToSourceContext(
        string transcriptionId,
        string? projectId,
        IReadOnlyList<string> segmentIds,
        string? clipIdSnapshot)
    {
      if (string.IsNullOrWhiteSpace(transcriptionId))
      {
        TranscriptOperatorMessage = TranscriptStaleContextExplainability.JumpNoTranscriptionId;
        return;
      }

      if (!string.IsNullOrEmpty(projectId)
          && !string.Equals(SelectedProjectId ?? string.Empty, projectId, StringComparison.Ordinal))
      {
        TranscriptOperatorMessage = TranscriptStaleContextExplainability.JumpProjectMismatch;
        return;
      }

      var match = Transcriptions.FirstOrDefault(t =>
          string.Equals(t.Id, transcriptionId, StringComparison.Ordinal));
      if (match == null)
      {
        TranscriptOperatorMessage = TranscriptStaleContextExplainability.JumpTranscriptionNotInSessionList;
        return;
      }

      SelectedTranscription = match;

      var firstSegId = segmentIds.FirstOrDefault(s => !string.IsNullOrWhiteSpace(s));
      if (string.IsNullOrWhiteSpace(firstSegId) || SelectedTranscription.Segments == null)
      {
        TranscriptOperatorMessage = TranscriptStaleContextExplainability.JumpNoSegmentTarget;
        return;
      }

      var seg = SelectedTranscription.Segments.FirstOrDefault(s => s.Id == firstSegId);
      if (seg == null)
      {
        TranscriptOperatorMessage = TranscriptStaleContextExplainability.JumpSegmentNotInTranscription;
        return;
      }

      OnTargetTranscriptionSegmentTapped(seg, clipIdSnapshot);
    }

    private void PublishTranscriptResolutionNavigate(TranscriptSegmentTargetResolution r)
    {
      if (r.Kind != TranscriptSegmentTargetResolutionKind.Resolved || string.IsNullOrWhiteSpace(r.ClipId))
        return;
      var agg = AppServices.TryGetEventAggregator();
      if (agg == null)
        return;
      agg.Publish(new NavigateToEvent(
          PanelId,
          "timeline",
          new Dictionary<string, object>
          {
            { "action", "seekPlayhead" },
            { "timeSeconds", r.TimelineSeekSeconds },
            { "clipId", r.ClipId },
          }));
    }

    private void OnProjectChanged(ProjectChangedEvent e)
    {
      Dispatcher.TryEnqueue(() =>
      {
        if (SelectedProjectId != e.ProjectId)
          SelectedProjectId = e.ProjectId;
        RefreshTranscriptTruthHints();
      });
    }

    private void OnAssetAdded(AssetAddedEvent e)
    {
      if (e == null)
        return;
      if (!string.Equals(e.AssetType, "audio", StringComparison.OrdinalIgnoreCase))
        return;
      if (string.IsNullOrWhiteSpace(e.AssetId))
        return;
      if (!IsRecordingDerivedAssetSource(e.SourcePanelId) && e.SourcePanelId != AssetSourceImportWorkflow)
        return;

      Dispatcher.TryEnqueue(() =>
      {
        if (!string.IsNullOrWhiteSpace(SelectedAudioId))
          return;
        SelectedAudioId = e.AssetId;
        _toastNotificationService?.ShowToast(
            ToastType.Info,
            ResourceHelper.GetString("Transcribe.AudioIdPrefilled", "Audio id set from recording or import."),
            ResourceHelper.GetString("Transcribe.AudioIdPrefilledTitle", "Audio id ready"));
      });
    }

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
      if (disposing)
      {
        lock (_rehydrateLock)
        {
          _rehydrateCts?.Cancel();
          _rehydrateCts?.Dispose();
          _rehydrateCts = null;
        }

        ReleasePanelEventSubscriptions();
      }

      base.Dispose(disposing);
    }

    public IAsyncRelayCommand LoadLanguagesCommand { get; }
    public IAsyncRelayCommand TranscribeCommand { get; }
    public IAsyncRelayCommand StartJobCommand { get; }
    public IAsyncRelayCommand LoadTranscriptionsCommand { get; }
    // GAP-CS-003: Dynamic engine discovery
    public IAsyncRelayCommand LoadEnginesCommand { get; }
    public IAsyncRelayCommand<TranscriptionResponse> DeleteTranscriptionCommand { get; }

    // Multi-select commands
    public IRelayCommand SelectAllTranscriptionsCommand { get; }
    public IRelayCommand ClearTranscriptionSelectionCommand { get; }

    /// <summary>Send selected transcription to Timeline as a subtitle track.</summary>
    public IRelayCommand SendToTimelineCommand { get; }

    /// <summary>POST dialogue create-timeline-clips for the selected transcript (requires <see cref="CreateTimelineClipsTrackId"/>).</summary>
    public IAsyncRelayCommand CreateTimelineClipsCommand { get; }

    /// <summary>GAP-045 Option B: operator-triggered canonical transcript refresh for stale clip audio.</summary>
    public IAsyncRelayCommand RefreshTranscriptTruthCommand { get; }

    /// <summary>GAP-045 inline edit/apply: apply buffered segment draft via regen <c>replacement_text</c>.</summary>
    public IAsyncRelayCommand ApplyEditedSegmentCommand { get; }

    /// <summary>GAP-047: remove common fillers from <see cref="EditingSegmentDraftText"/> only (no Apply).</summary>
    public IRelayCommand RemoveFillersFromEditingDraftCommand { get; }

    /// <summary>GAP-045: clear session transcript edit history ring buffer.</summary>
    public IRelayCommand ClearTranscriptEditHistoryCommand { get; }

    /// <summary>GOV-VOICESTUDIO-EDIT-APPLY-JOB-STATUS-01: clear session apply/regenerate job status rows.</summary>
    public IRelayCommand ClearTranscriptApplyJobStatusCommand { get; }

    private void RemoveFillersFromEditingDraftCore()
    {
      var err = TryRemoveFillersFromEditingDraft();
      if (string.IsNullOrEmpty(err))
        return;
      _toastNotificationService?.ShowToast(
          ToastType.Warning,
          ResourceHelper.GetString("Transcribe.RemoveFillersTitle", "Remove fillers"),
          err);
    }

    /// <summary>
    /// GAP-047: deterministic filler cleanup on current <see cref="EditingSegmentDraftText"/> only (no Apply, no regen, no transcript PUT).
    /// Canonical segment text changes only after operator <see cref="ApplyEditedSegmentAsync"/>.
    /// </summary>
    public string? TryRemoveFillersFromEditingDraft()
    {
      if (!IsEditingSegment)
        return "No segment edit in progress.";
      var draft = EditingSegmentDraftText ?? string.Empty;
      if (string.IsNullOrWhiteSpace(draft))
        return "Nothing to clean up.";

      RebuildFillerRemovalTogglesAndPreview();

      if (FillerRemovalToggles.Count == 0)
      {
        TranscriptOperatorMessage = "No matching filler words in the draft.";
        return null;
      }

      if (!FillerRemovalToggles.Any(t => t.IsRemoveEnabled))
        return "Enable at least one filler term to remove (check the list in the flyout).";

      var result = TranscriptFillerCleanupHelper.RemoveFillers(draft, BuildEnabledPhraseKeys(), BuildEnabledTokenKeys());
      if (string.IsNullOrWhiteSpace(result.CleanedText))
        return "Filler cleanup would remove all text; cancelled.";
      EditingSegmentDraftText = result.CleanedText.Trim();
      if (result.RemovedOccurrenceCount == 0)
        TranscriptOperatorMessage = "No matching filler words in the draft.";
      else
      {
        TranscriptOperatorMessage =
            string.IsNullOrEmpty(result.TermsSummary)
                ? $"Removed {result.RemovedOccurrenceCount} filler occurrence(s)."
                : $"Removed {result.RemovedOccurrenceCount} filler occurrence(s): {result.TermsSummary}.";
        RecordFillerCleanupDraftHistory(result);
      }

      RefreshSegmentEditHint();
      return null;
    }

    private bool CanTranscribe()
    {
      return !string.IsNullOrWhiteSpace(SelectedAudioId);
    }

    partial void OnSelectedAudioIdChanged(string? value)
    {
      AudioPersistenceSemanticsHint = null;
      TranscribeCommand.NotifyCanExecuteChanged();
      StartJobCommand.NotifyCanExecuteChanged();
      RefreshTranscriptTruthHints();
      RefreshTranscriptTruthCommand.NotifyCanExecuteChanged();
      ScheduleBackendTranscriptRehydrate("audio_id");
    }

    partial void OnSelectedProjectIdChanged(string? value)
    {
      ClearSessionRegeneratedSegmentTracking();
      TranscriptSegmentLayoutRevision++;
      // GAP-045 lifecycle: never treat the previous project's SelectedTranscription as authoritative
      // for the new SelectedProjectId — rehydrate may use per-project LastSubtitleTranscriptionId or list default.
      Transcriptions.Clear();
      SelectedTranscription = null;
      TranscriptionText = string.Empty;
      TranscriptOperatorMessage = null;
      RefreshTranscriptTruthHints();
      RefreshTranscriptTruthCommand.NotifyCanExecuteChanged();
      ScheduleBackendTranscriptRehydrate("project_id");
    }

    partial void OnEditingSegmentIdChanged(string? value)
    {
      OnPropertyChanged(nameof(IsEditingSegment));
      OnPropertyChanged(nameof(IsMultiSegmentRangeEdit));
      OnPropertyChanged(nameof(IsEditDirty));
      RefreshSegmentEditHint();
      ApplyEditedSegmentCommand.NotifyCanExecuteChanged();
      RemoveFillersFromEditingDraftCommand.NotifyCanExecuteChanged();
      if (string.IsNullOrWhiteSpace(value))
        ClearFillerRemovalReviewState();
    }

    partial void OnEditingSegmentOriginalTextChanged(string? value)
    {
      OnPropertyChanged(nameof(IsEditDirty));
      RefreshSegmentEditHint();
      ApplyEditedSegmentCommand.NotifyCanExecuteChanged();
      RemoveFillersFromEditingDraftCommand.NotifyCanExecuteChanged();
    }

    partial void OnEditingSegmentDraftTextChanged(string? value)
    {
      OnPropertyChanged(nameof(IsEditDirty));
      RefreshSegmentEditHint();
      ApplyEditedSegmentCommand.NotifyCanExecuteChanged();
      RemoveFillersFromEditingDraftCommand.NotifyCanExecuteChanged();
      if (IsEditingSegment)
        RebuildFillerRemovalTogglesAndPreview();
    }

    partial void OnEditingRangeEndSegmentIdChanged(string? value)
    {
      OnPropertyChanged(nameof(IsMultiSegmentRangeEdit));
      OnPropertyChanged(nameof(IsEditDirty));
      RefreshSegmentEditHint();
      ApplyEditedSegmentCommand.NotifyCanExecuteChanged();
      RemoveFillersFromEditingDraftCommand.NotifyCanExecuteChanged();
    }

    private void RefreshSegmentEditHint()
    {
      if (!IsEditingSegment)
      {
        SegmentEditOperatorHint = null;
        return;
      }

      if (IsMultiSegmentRangeEdit)
      {
        SegmentEditOperatorHint = IsEditDirty
            ? "Range text edited — Apply regenerates clip audio from the full replacement string (first segment anchors the job). Cancel to discard."
            : "Editing a contiguous range — type one replacement for the whole span, then Apply. Tip: click a segment, then Shift+click another on the same clip.";
        return;
      }

      SegmentEditOperatorHint = IsEditDirty
          ? "Segment text edited — Apply to regenerate audio with the new wording, or Cancel."
          : "Editing segment text — change the text, then Apply.";
    }

    partial void OnRegeneratingSegmentIdChanged(string? value)
    {
      ApplyEditedSegmentCommand.NotifyCanExecuteChanged();
      RemoveFillersFromEditingDraftCommand.NotifyCanExecuteChanged();
    }

    partial void OnIsLoadingChanged(bool value)
    {
      ApplyEditedSegmentCommand.NotifyCanExecuteChanged();
      RemoveFillersFromEditingDraftCommand.NotifyCanExecuteChanged();
      LoadLanguagesCommand.NotifyCanExecuteChanged();
      TranscribeCommand.NotifyCanExecuteChanged();
      StartJobCommand.NotifyCanExecuteChanged();
      LoadTranscriptionsCommand.NotifyCanExecuteChanged();
      DeleteTranscriptionCommand.NotifyCanExecuteChanged();
      RefreshTranscriptTruthCommand.NotifyCanExecuteChanged();
      CreateTimelineClipsCommand.NotifyCanExecuteChanged();
    }

    private async Task LoadLanguagesAsync(CancellationToken cancellationToken)
    {
      IsLoading = true;
      ErrorMessage = null;

      try
      {
        var languages = await _transcriptionClient.GetSupportedLanguagesAsync(cancellationToken);
        Languages.Clear();
        foreach (var lang in languages)
        {
          Languages.Add(lang);
        }

        // Set default to auto-detect if not set
        if (string.IsNullOrEmpty(SelectedLanguage))
        {
          SelectedLanguage = "auto";
        }

        if (Languages.Count > 0)
        {
          _toastNotificationService?.ShowSuccess("Languages Loaded", $"Loaded {Languages.Count} supported languages");
        }
      }
      catch (OperationCanceledException)
      {
        return; // User cancelled
      }
      catch (Exception ex)
      {
        ErrorMessage = $"Failed to load languages: {ex.Message}";
        await HandleErrorAsync(ex, "LoadLanguages");
      }
      finally
      {
        IsLoading = false;
      }
    }

    // GAP-CS-003: Dynamic engine discovery from backend API
    private async Task LoadEnginesAsync(CancellationToken cancellationToken = default)
    {
      IsLoadingEngines = true;
      ErrorMessage = null;

      try
      {
        var engines = await _transcriptionClient.GetTranscriptionEnginesAsync(cancellationToken);
        
        Engines.Clear();
        foreach (var engine in engines)
        {
          Engines.Add(engine.Id);
        }

        // Set default engine if current selection is not available
        if (Engines.Count > 0 && !Engines.Contains(SelectedEngine))
        {
          SelectedEngine = Engines[0];
        }
        
        // Add fallback engines if none were discovered
        if (Engines.Count == 0)
        {
          Engines.Add("whisper_cpp");
          Engines.Add("whisper");
          Engines.Add("vosk");
          SelectedEngine = "whisper_cpp";
        }
      }
      catch (OperationCanceledException)
      {
        return; // User cancelled
      }
      catch (Exception ex)
      {
        // On error, populate with fallback engines
        if (Engines.Count == 0)
        {
          Engines.Add("whisper_cpp");
          Engines.Add("whisper");
          Engines.Add("vosk");
        }
        
        // Log but don't show error to user since we have fallback
        System.Diagnostics.Debug.WriteLine($"Failed to load engines from backend: {ex.Message}");
      }
      finally
      {
        IsLoadingEngines = false;
      }
    }

    private async Task TranscribeAsync(CancellationToken cancellationToken = default)
    {
      if (string.IsNullOrWhiteSpace(SelectedAudioId))
      {
        var msg = ResourceHelper.GetString("Transcribe.MissingAudioId", "Enter a backend audio id before transcribing or loading transcriptions.");
        ErrorMessage = msg;
        _toastNotificationService?.ShowWarning(
            ResourceHelper.GetString("Transcribe.MissingAudioIdTitle", "Audio id required"),
            msg);
        return;
      }

      try
      {
        IsLoading = true;
        ErrorMessage = null;
        AudioPersistenceSemanticsHint = null;

        var request = new TranscriptionRequest
        {
          AudioId = SelectedAudioId,
          Engine = SelectedEngine,
          Language = SelectedLanguage == "auto" ? null : SelectedLanguage,
          WordTimestamps = WordTimestamps,
          Diarization = Diarization,
          UseVad = UseVad
        };

        var transcription = await _transcriptionClient.TranscribeAudioAsync(request, SelectedProjectId, cancellationToken);

        // Add to collection
        Transcriptions.Insert(0, transcription);
        SelectedTranscription = transcription;
        TranscriptionText = transcription.Text;

        // Reload transcriptions list
        await LoadTranscriptionsAsync(cancellationToken);

        // C.4: Publish TranscriptionCompletedEvent for timeline subtitle track
        var eventAggregator = AppServices.TryGetEventAggregator();
        if (eventAggregator != null && transcription.Segments.Count > 0)
        {
          var subtitleSegments = transcription.Segments
            .Select(s => new VoiceStudio.Core.Events.SubtitleSegment(
                s.Start,
                s.End,
                s.Text,
                string.IsNullOrWhiteSpace(s.Id) ? null : s.Id))
            .ToList();

          eventAggregator.Publish(new VoiceStudio.Core.Events.TranscriptionCompletedEvent(
            PanelId,
            transcription.AudioId,
            transcription.Id,
            transcription.Text,
            subtitleSegments,
            TimeSpan.FromSeconds(transcription.Duration),
            transcription.Language));
        }

        var saveOutcome = await TranscribeToProjectPersistence.TrySaveLibraryAudioToProjectAsync(
            _projectAudioClient,
            _logService,
            SelectedProjectId,
            transcription.AudioId,
            cancellationToken).ConfigureAwait(false);

        var title = ResourceHelper.GetString("Transcribe.C3.TranscribeCompleteTitle", "Transcription complete");
        string detail;
        string hint;
        switch (saveOutcome)
        {
          case TranscribeProjectAudioSaveOutcome.Saved:
          {
            var detailFmt = ResourceHelper.GetString(
                "Transcribe.A1.TranscribeCompleteDetailWithProjectCopy",
                "Transcribed with {0}. Source audio was also added to project audio.");
            detail = string.Format(System.Globalization.CultureInfo.CurrentCulture, detailFmt, SelectedEngine);
            hint = ResourceHelper.GetString(
                "Transcribe.A1.AudioPersistenceHintProjectCopy",
                "Source audio is in the library and was copied to project audio.");
            break;
          }
          case TranscribeProjectAudioSaveOutcome.Failed:
          {
            var detailFmt = ResourceHelper.GetString(
                "Transcribe.A1.TranscribeCompleteDetailProjectCopyFailed",
                "Transcribed with {0}. The transcript is ready; copying source audio to the project failed. Check logs.");
            detail = string.Format(System.Globalization.CultureInfo.CurrentCulture, detailFmt, SelectedEngine);
            hint = ResourceHelper.GetString(
                "Transcribe.A1.AudioPersistenceHintProjectCopyFailed",
                "Transcript is ready; source audio could not be copied to the project. See logs.");
            break;
          }
          default:
          {
            var detailTemplate = ResourceHelper.GetString(
                "Transcribe.C3.TranscribeCompleteDetail",
                "Transcribed with {0}. Source audio remains a library asset; this step does not add it to project audio. Creating a transcript does not save source audio to the project.");
            detail = string.Format(System.Globalization.CultureInfo.CurrentCulture, detailTemplate, SelectedEngine);
            hint = ResourceHelper.GetString(
                "Transcribe.C3.AudioPersistenceHint",
                "Source audio remains a library asset; transcribing does not add it to project audio.");
            break;
          }
        }

        _toastNotificationService?.ShowSuccess(detail, title);
        AudioPersistenceSemanticsHint = hint;
      }
      catch (Exception ex)
      {
        ErrorMessage = $"Transcription failed: {ex.Message}";
        _toastNotificationService?.ShowError("Transcription Failed", ex.Message);
      }
      finally
      {
        IsLoading = false;
      }
    }

    private async Task StartTranscriptionJobAsync(CancellationToken cancellationToken = default)
    {
      if (string.IsNullOrWhiteSpace(SelectedAudioId))
      {
        var msg = ResourceHelper.GetString("Transcribe.MissingAudioId", "Enter a backend audio id before transcribing or loading transcriptions.");
        ErrorMessage = msg;
        _toastNotificationService?.ShowWarning(
            ResourceHelper.GetString("Transcribe.MissingAudioIdTitle", "Audio id required"),
            msg);
        return;
      }

      try
      {
        IsLoading = true;
        ErrorMessage = null;
        TranscriptionJobMode = null;
        TranscriptionJobProgress = 0f;
        AudioPersistenceSemanticsHint = null;

        var request = new TranscriptionJobRequest
        {
          AudioId = SelectedAudioId,
          Engine = SelectedEngine,
          Language = SelectedLanguage == "auto" ? null : SelectedLanguage,
          WordTimestamps = WordTimestamps,
          Simulate = SimulateTranscription,
          AsyncMode = true,
        };

        var job = await _transcriptionClient.StartTranscriptionJobAsync(request, SelectedProjectId, cancellationToken).ConfigureAwait(true);
        TranscriptionJobMode = string.IsNullOrWhiteSpace(job.Mode) ? null : job.Mode;
        TranscriptionJobProgress = job.Progress ?? 0f;

        if (string.Equals(job.Status, "pending", StringComparison.OrdinalIgnoreCase)
            || string.Equals(job.Status, "running", StringComparison.OrdinalIgnoreCase))
        {
          await PollTranscriptionJobAsync(job.JobId, cancellationToken).ConfigureAwait(true);
          return;
        }

        await HydrateJobTranscriptIfMissingAsync(job, cancellationToken).ConfigureAwait(true);
        await ApplyTranscriptionJobOutcomeAsync(job, cancellationToken).ConfigureAwait(true);
      }
      catch (Exception ex)
      {
        ErrorMessage = $"Transcription job failed: {ex.Message}";
        _toastNotificationService?.ShowError("Transcription job failed", ex.Message);
      }
      finally
      {
        IsLoading = false;
        TranscribeCommand.NotifyCanExecuteChanged();
        StartJobCommand.NotifyCanExecuteChanged();
      }
    }

    private async Task PollTranscriptionJobAsync(string jobId, CancellationToken cancellationToken)
    {
      const int maxWaitMs = 300_000;
      const int intervalMs = 250;
      var sw = Stopwatch.StartNew();
      while (sw.ElapsedMilliseconds < maxWaitMs)
      {
        await Task.Delay(intervalMs, cancellationToken).ConfigureAwait(true);
        var status = await _transcriptionClient.GetTranscriptionJobStatusAsync(jobId, cancellationToken).ConfigureAwait(true);
        TranscriptionJobMode = string.IsNullOrWhiteSpace(status.Mode) ? null : status.Mode;
        TranscriptionJobProgress = status.Progress ?? 0f;
        if (!string.Equals(status.Status, "pending", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(status.Status, "running", StringComparison.OrdinalIgnoreCase))
        {
          await HydrateJobTranscriptIfMissingAsync(status, cancellationToken).ConfigureAwait(true);
          await ApplyTranscriptionJobOutcomeAsync(status, cancellationToken).ConfigureAwait(true);
          return;
        }
      }

      ErrorMessage = ResourceHelper.GetString(
          "Transcribe.JobPollTimeout",
          "Transcription job timed out.");
      _toastNotificationService?.ShowWarning(
          ResourceHelper.GetString("Transcribe.JobPollTimeoutTitle", "Transcription job"),
          ErrorMessage);
    }

    private async Task HydrateJobTranscriptIfMissingAsync(TranscriptionJobResponse job, CancellationToken cancellationToken)
    {
      if (!string.Equals(job.Status, "completed", StringComparison.OrdinalIgnoreCase))
        return;
      if (job.Transcript != null || string.IsNullOrWhiteSpace(job.TranscriptId))
        return;
      var loaded = await _transcriptionClient.GetTranscriptionAsync(job.TranscriptId, cancellationToken).ConfigureAwait(true);
      job.Transcript = loaded;
    }

    private async Task ApplyTranscriptionJobOutcomeAsync(TranscriptionJobResponse job, CancellationToken cancellationToken)
    {
      var outcome = TranscriptionJobOutcomeClassifier.Classify(job);

      switch (outcome)
      {
        case TranscriptionJobOutcome.RealCompleted:
        case TranscriptionJobOutcome.SimulatedCompleted:
        {
          var transcription = job.Transcript!;
          LastTranscriptionWasSimulated = outcome == TranscriptionJobOutcome.SimulatedCompleted;
          Transcriptions.Insert(0, transcription);
          SelectedTranscription = transcription;
          TranscriptionText = transcription.Text;

          await LoadTranscriptionsAsync(cancellationToken).ConfigureAwait(true);

          var eventAggregator = AppServices.TryGetEventAggregator();
          var subtitleSource = transcription.Segments ?? new List<TranscriptionSegment>();
          if (eventAggregator != null && subtitleSource.Count > 0)
          {
            var subtitleSegments = subtitleSource
                .Select(s => new VoiceStudio.Core.Events.SubtitleSegment(
                    s.Start,
                    s.End,
                    s.Text,
                    string.IsNullOrWhiteSpace(s.Id) ? null : s.Id))
                .ToList();

            eventAggregator.Publish(new VoiceStudio.Core.Events.TranscriptionCompletedEvent(
                PanelId,
                transcription.AudioId,
                transcription.Id,
                transcription.Text,
                subtitleSegments,
                TimeSpan.FromSeconds(transcription.Duration),
                transcription.Language));
          }

          var saveOutcome = await TranscribeToProjectPersistence.TrySaveLibraryAudioToProjectAsync(
              _projectAudioClient,
              _logService,
              SelectedProjectId,
              transcription.AudioId,
              cancellationToken).ConfigureAwait(false);

          var title = ResourceHelper.GetString("Transcribe.C3.TranscribeCompleteTitle", "Transcription complete");
          string detail;
          string hint;
          switch (saveOutcome)
          {
            case TranscribeProjectAudioSaveOutcome.Saved:
            {
              var detailFmt = ResourceHelper.GetString(
                  "Transcribe.A1.TranscribeCompleteDetailWithProjectCopy",
                  "Transcribed with {0}. Source audio was also added to project audio.");
              detail = string.Format(System.Globalization.CultureInfo.CurrentCulture, detailFmt, SelectedEngine);
              hint = ResourceHelper.GetString(
                  "Transcribe.A1.AudioPersistenceHintProjectCopy",
                  "Source audio is in the library and was copied to project audio.");
              break;
            }
            case TranscribeProjectAudioSaveOutcome.Failed:
            {
              var detailFmt = ResourceHelper.GetString(
                  "Transcribe.A1.TranscribeCompleteDetailProjectCopyFailed",
                  "Transcribed with {0}. The transcript is ready; copying source audio to the project failed. Check logs.");
              detail = string.Format(System.Globalization.CultureInfo.CurrentCulture, detailFmt, SelectedEngine);
              hint = ResourceHelper.GetString(
                  "Transcribe.A1.AudioPersistenceHintProjectCopyFailed",
                  "Transcript is ready; source audio could not be copied to the project. See logs.");
              break;
            }
            default:
            {
              var detailTemplate = ResourceHelper.GetString(
                  "Transcribe.C3.TranscribeCompleteDetail",
                  "Transcribed with {0}. Source audio remains a library asset; this step does not add it to project audio. Creating a transcript does not save source audio to the project.");
              detail = string.Format(System.Globalization.CultureInfo.CurrentCulture, detailTemplate, SelectedEngine);
              hint = ResourceHelper.GetString(
                  "Transcribe.C3.AudioPersistenceHint",
                  "Source audio remains a library asset; transcribing does not add it to project audio.");
              break;
            }
          }

          _toastNotificationService?.ShowSuccess(detail, title);
          AudioPersistenceSemanticsHint = hint;
          break;
        }
        case TranscriptionJobOutcome.Unavailable:
        {
          var blocker = string.IsNullOrWhiteSpace(job.Blocker) ? "(no detail)" : job.Blocker;
          ErrorMessage = $"Transcription {job.Mode}: {blocker}";
          _toastNotificationService?.ShowWarning(
              ResourceHelper.GetString("Transcribe.JobUnavailableTitle", "Transcription unavailable"),
              ErrorMessage);
          break;
        }
        case TranscriptionJobOutcome.Failed:
        {
          var blocker = string.IsNullOrWhiteSpace(job.Blocker) ? "(no detail)" : job.Blocker;
          ErrorMessage = $"Transcription {job.Mode}: {blocker}";
          _toastNotificationService?.ShowError(
              ResourceHelper.GetString("Transcribe.JobFailedTitle", "Transcription job failed"),
              ErrorMessage);
          break;
        }
        case TranscriptionJobOutcome.InvalidCompleted:
        default:
        {
          ErrorMessage = "Transcription completed but returned no transcript";
          _toastNotificationService?.ShowError(
              ResourceHelper.GetString("Transcribe.JobInvalidTitle", "Transcription incomplete"),
              ErrorMessage);
          break;
        }
      }
    }

    /// <summary>
    /// GAP-045 reload lane: when project + audio scope are known, replace in-memory list from
    /// <see cref="ITranscriptionClient.ListTranscriptionsAsync"/> (backend authority). Coalesces rapid scope changes via cancellation.
    /// </summary>
    private void ScheduleBackendTranscriptRehydrate(string triggerReason)
    {
      if (string.IsNullOrWhiteSpace(SelectedAudioId) || string.IsNullOrWhiteSpace(SelectedProjectId))
        return;

      CancellationToken token;
      lock (_rehydrateLock)
      {
        _rehydrateCts?.Cancel();
        _rehydrateCts?.Dispose();
        _rehydrateCts = new CancellationTokenSource();
        token = _rehydrateCts.Token;
      }

      _ = RunBackendTranscriptRehydrateAsync(triggerReason, token);
    }

    private async Task RunBackendTranscriptRehydrateAsync(string triggerReason, CancellationToken cancellationToken)
    {
      var audioId = SelectedAudioId;
      var projectId = SelectedProjectId;
      if (string.IsNullOrWhiteSpace(audioId) || string.IsNullOrWhiteSpace(projectId))
        return;

      var inMemoryPreviousId = SelectedTranscription?.Id;
      string? previousSelectionId = inMemoryPreviousId;
      var previousFromProjectRestore = false;

      if (string.IsNullOrEmpty(previousSelectionId) && _projectRepository != null)
      {
        previousSelectionId = await _projectRepository
            .GetLastSubtitleTranscriptionIdAsync(projectId, cancellationToken)
            .ConfigureAwait(true);
        previousFromProjectRestore = !string.IsNullOrEmpty(previousSelectionId);
      }

      if (!string.Equals(SelectedAudioId, audioId, StringComparison.Ordinal)
          || !string.Equals(SelectedProjectId, projectId, StringComparison.Ordinal))
      {
        return;
      }

      try
      {
        var transcriptions = await _transcriptionClient.ListTranscriptionsAsync(audioId, projectId, cancellationToken).ConfigureAwait(true);
        if (!string.Equals(SelectedAudioId, audioId, StringComparison.Ordinal)
            || !string.Equals(SelectedProjectId, projectId, StringComparison.Ordinal))
        {
          return;
        }

        ApplyTranscriptionListFromBackend(
            transcriptions,
            showSuccessToast: false,
            triggerReason: triggerReason,
            previousSelectionId: previousSelectionId,
            previousSelectionFromProjectRestore: previousFromProjectRestore);

        PublishTimelineCoherenceAfterRehydrate(inMemoryPreviousId);
      }
      catch (OperationCanceledException)
      {
        return;
      }
      catch (Exception ex)
      {
        if (cancellationToken.IsCancellationRequested)
          return;
        ErrorMessage = $"Transcript rehydrate failed ({triggerReason}): {ex.Message}";
        _logService?.LogError(ex, "TranscribeViewModel.Rehydrate");
        await HandleErrorAsync(ex, "RehydrateTranscripts").ConfigureAwait(true);
      }
    }

    /// <summary>
    /// Applies backend list to <see cref="Transcriptions"/>; restores selection by id when possible.
    /// </summary>
    private void ApplyTranscriptionListFromBackend(
        IReadOnlyList<TranscriptionResponse> transcriptions,
        bool showSuccessToast,
        string? triggerReason,
        string? previousSelectionId,
        bool previousSelectionFromProjectRestore = false)
    {
      Transcriptions.Clear();
      foreach (var transcription in transcriptions.OrderByDescending(t => t.Created))
      {
        Transcriptions.Add(transcription);
      }

      if (!string.IsNullOrEmpty(previousSelectionId))
      {
        var match = Transcriptions.FirstOrDefault(t => string.Equals(t.Id, previousSelectionId, StringComparison.Ordinal));
        if (match != null)
        {
          SelectedTranscription = match;
        }
        else
        {
          SelectedTranscription = Transcriptions.FirstOrDefault();
          if (previousSelectionFromProjectRestore)
          {
            TranscriptOperatorMessage =
                "[Restore] Last subtitle transcription no longer exists for this project — cleared";
          }
          else
          {
            var ctx = string.IsNullOrWhiteSpace(triggerReason) ? "load" : triggerReason;
            TranscriptOperatorMessage =
                $"Rehydrate ({ctx}): previously selected transcript is not in the backend list for this audio/project. "
                + "Showing the newest available row.";
          }
        }
      }
      else if (Transcriptions.Count > 0)
      {
        SelectedTranscription ??= Transcriptions[0];
      }

      if (showSuccessToast && Transcriptions.Count > 0)
      {
        _toastNotificationService?.ShowSuccess("Transcriptions Loaded", $"Loaded {Transcriptions.Count} transcription(s)");
      }
    }

    private async Task LoadTranscriptionsAsync(CancellationToken cancellationToken)
    {
      if (string.IsNullOrWhiteSpace(SelectedAudioId))
      {
        var msg = ResourceHelper.GetString("Transcribe.MissingAudioId", "Enter a backend audio id before transcribing or loading transcriptions.");
        ErrorMessage = msg;
        _toastNotificationService?.ShowWarning(
            ResourceHelper.GetString("Transcribe.MissingAudioIdTitle", "Audio id required"),
            msg);
        return;
      }

      IsLoading = true;
      ErrorMessage = null;

      try
      {
        var previousSelectionId = SelectedTranscription?.Id;
        var transcriptions = await _transcriptionClient.ListTranscriptionsAsync(SelectedAudioId, SelectedProjectId, cancellationToken).ConfigureAwait(true);
        ApplyTranscriptionListFromBackend(
            transcriptions,
            showSuccessToast: true,
            triggerReason: null,
            previousSelectionId: previousSelectionId,
            previousSelectionFromProjectRestore: false);
      }
      catch (OperationCanceledException)
      {
        return; // User cancelled
      }
      catch (Exception ex)
      {
        ErrorMessage = $"Failed to load transcriptions: {ex.Message}";
        await HandleErrorAsync(ex, "LoadTranscriptions").ConfigureAwait(true);
      }
      finally
      {
        IsLoading = false;
      }
    }

    private async Task DeleteTranscriptionAsync(TranscriptionResponse? transcription, CancellationToken cancellationToken)
    {
      if (transcription == null)
        return;

      IsLoading = true;
      ErrorMessage = null;

      try
      {
        await _transcriptionClient.DeleteTranscriptionAsync(transcription.Id, cancellationToken);

        var transcriptionToDelete = transcription;
        var originalIndex = Transcriptions.IndexOf(transcription);

        // Remove from collection
        Transcriptions.Remove(transcription);

        if (SelectedTranscription == transcription)
        {
          SelectedTranscription = null;
          TranscriptionText = string.Empty;
        }

        // Register undo action
        if (_undoRedoService != null && originalIndex >= 0)
        {
          var action = new DeleteTranscriptionAction(
              Transcriptions,
              _transcriptionClient,
              transcriptionToDelete,
              originalIndex,
              onUndo: (t) =>
              {
                SelectedTranscription = t;
                TranscriptionText = t.Text;
              },
              onRedo: (t) =>
              {
                if (SelectedTranscription?.Id == t.Id)
                {
                  SelectedTranscription = null;
                  TranscriptionText = string.Empty;
                }
              });
          _undoRedoService.RegisterAction(action);
        }

        _toastNotificationService?.ShowSuccess("Transcription Deleted", "Transcription deleted successfully");
      }
      catch (OperationCanceledException)
      {
        return; // User cancelled
      }
      catch (Exception ex)
      {
        ErrorMessage = $"Failed to delete transcription: {ex.Message}";
        await HandleErrorAsync(ex, "DeleteTranscription");
      }
      finally
      {
        IsLoading = false;
      }
    }

    partial void OnSelectedTranscriptionChanged(TranscriptionResponse? value)
    {
      ClearSessionRegeneratedSegmentTracking();
      if (value != null)
      {
        TranscriptionText = value.Text;
      }
      else
      {
        TranscriptionText = string.Empty;
      }

      SendToTimelineCommand.NotifyCanExecuteChanged();
      CreateTimelineClipsCommand.NotifyCanExecuteChanged();
      TranscriptSegmentLayoutRevision++;
    }

    partial void OnCreateTimelineClipsTrackIdChanged(string value)
    {
      CreateTimelineClipsCommand.NotifyCanExecuteChanged();
    }

    /// <summary>
    /// GAP-045 cross-consumer: after backend list rehydrate, ask Timeline to refetch subtitle segments when
    /// the timeline overlay was tied to the pre-rehydrate selection (avoids stale in-memory segment text).
    /// </summary>
    private void PublishTimelineCoherenceAfterRehydrate(string? previousTranscriptionId)
    {
      var eventAggregator = AppServices.TryGetEventAggregator();
      if (eventAggregator == null)
      {
        return;
      }

      eventAggregator.Publish(
          new NavigateToEvent(
              PanelId,
              "timeline",
              new Dictionary<string, object>
              {
                { "action", "coherentReloadAfterRehydrate" },
                { "previousTranscriptionId", previousTranscriptionId ?? string.Empty },
                { "transcriptionId", SelectedTranscription?.Id ?? string.Empty },
              }));
    }

    /// <summary>
    /// GAP-047 post-apply cross-consumer: after successful apply/regen that persists transcript, ask Timeline to quiet-refetch
    /// subtitle segments when the overlay still matches this transcription + project (fail-closed otherwise).
    /// </summary>
    private void PublishTimelineCoherenceAfterSegmentApplySuccess()
    {
      if (SelectedTranscription == null)
        return;
      var projectId = SelectedProjectId;
      if (string.IsNullOrWhiteSpace(projectId))
        return;

      var eventAggregator = AppServices.TryGetEventAggregator();
      if (eventAggregator == null)
        return;

      eventAggregator.Publish(
          new NavigateToEvent(
              PanelId,
              "timeline",
              new Dictionary<string, object>
              {
                { "action", "coherentReloadAfterSegmentApply" },
                { "transcriptionId", SelectedTranscription.Id },
                { "projectId", projectId },
              }));
    }

    /// <summary>
    /// Creates one timeline clip per transcript segment via dialogue API (requires <see cref="CreateTimelineClipsTrackId"/>).
    /// </summary>
    private async Task CreateTimelineClipsFromTranscriptAsync(CancellationToken cancellationToken)
    {
      if (_dialogueServiceClient == null)
      {
        var msg = ResourceHelper.GetString(
            "Transcribe.DialogueClientUnavailable",
            "Dialogue service is not available.");
        ErrorMessage = msg;
        _toastNotificationService?.ShowWarning(
            ResourceHelper.GetString("Transcribe.TimelineClipsTitle", "Timeline clips"),
            msg);
        return;
      }

      if (SelectedTranscription == null)
      {
        var msg = ResourceHelper.GetString(
            "Transcribe.NoTranscriptionForTimelineClips",
            "Select a transcript before creating timeline clips.");
        ErrorMessage = msg;
        _toastNotificationService?.ShowWarning(
            ResourceHelper.GetString("Transcribe.TimelineClipsTitle", "Timeline clips"),
            msg);
        return;
      }

      if (string.IsNullOrWhiteSpace(CreateTimelineClipsTrackId))
      {
        var msg = ResourceHelper.GetString(
            "Transcribe.TrackIdRequiredForTimelineClips",
            "Enter a timeline track id (create a track on the Timeline panel, then paste its id here).");
        ErrorMessage = msg;
        _toastNotificationService?.ShowWarning(
            ResourceHelper.GetString("Transcribe.TimelineClipsTitle", "Timeline clips"),
            msg);
        return;
      }

      try
      {
        IsLoading = true;
        ErrorMessage = null;

        var req = new CreateTimelineClipsFromTranscriptRequest
        {
          TrackId = CreateTimelineClipsTrackId.Trim(),
          ProjectId = string.IsNullOrWhiteSpace(SelectedProjectId) ? null : SelectedProjectId,
          SessionId = null,
          ReplaceExisting = false,
        };

        var resp = await _dialogueServiceClient
            .CreateTimelineClipsAsync(SelectedTranscription.Id, req, cancellationToken)
            .ConfigureAwait(true);

        LastCreatedTimelineClipIds = resp.CreatedClipIds != null && resp.CreatedClipIds.Count > 0
            ? new List<string>(resp.CreatedClipIds)
            : new List<string>();

        var simNote = LastTranscriptionWasSimulated
            ? ResourceHelper.GetString("Transcribe.TimelineClipsSimulatedNote", " (simulated transcript)")
            : string.Empty;
        TranscriptOperatorMessage =
            $"Created {LastCreatedTimelineClipIds.Count} clip(s); segment_count={resp.SegmentCount}; status={resp.Status}{simNote}";

        _toastNotificationService?.ShowSuccess(
            TranscriptOperatorMessage,
            ResourceHelper.GetString("Transcribe.TimelineClipsTitle", "Timeline clips"));

        var eventAggregator = AppServices.TryGetEventAggregator();
        if (eventAggregator != null)
        {
          eventAggregator.Publish(
              new NavigateToEvent(
                  PanelId,
                  "timeline",
                  new Dictionary<string, object>
                  {
                    { "action", "reloadAfterCreateTranscriptClips" },
                    { "transcriptionId", SelectedTranscription.Id },
                    { "trackId", req.TrackId },
                  }));
        }
      }
      catch (Exception ex)
      {
        ErrorMessage = $"Failed to create timeline clips: {ex.Message}";
        _toastNotificationService?.ShowError(
            ResourceHelper.GetString("Transcribe.TimelineClipsTitle", "Timeline clips"),
            ex.Message);
      }
      finally
      {
        IsLoading = false;
      }
    }

    /// <summary>
    /// Send the selected transcription's segments to the Timeline panel
    /// as a subtitle track overlay.
    /// Audit remediation X-3: Transcription -> Timeline integration.
    /// </summary>
    private void SendSelectedTranscriptionToTimeline()
    {
      if (SelectedTranscription == null)
        return;

      var eventAggregator = AppServices.TryGetEventAggregator();
      if (eventAggregator == null)
      {
        _toastNotificationService?.ShowWarning("Timeline", "Event system unavailable");
        return;
      }

      eventAggregator.Publish(new NavigateToEvent(
          PanelId,
          "timeline",
          new Dictionary<string, object>
          {
            { "action", "loadTranscript" },
            { "transcriptionId", SelectedTranscription.Id }
          }));

      var stTitle = ResourceHelper.GetString("Transcribe.C3.SendToTimelineTitle", "Sent to Timeline");
      var stDetail = ResourceHelper.GetString(
          "Transcribe.C3.SendToTimelineDetail",
          "Transcript overlay loads on the Timeline; it does not persist source audio to the project.");
      _toastNotificationService?.ShowSuccess(stDetail, stTitle);

      AudioPersistenceSemanticsHint = ResourceHelper.GetString(
          "Transcribe.C3.SendToTimelineHint",
          "Timeline shows a transcript overlay only; source audio is not saved to the project from this action.");
    }

    // Multi-select methods
    public void ToggleTranscriptionSelection(string transcriptionId, bool isCtrlPressed, bool isShiftPressed)
    {
      if (_multiSelectState == null)
        return;

      if (isShiftPressed && !string.IsNullOrEmpty(_multiSelectState.RangeAnchorId))
      {
        // Range selection
        var allTranscriptionIds = Transcriptions.Select(t => t.Id).ToList();
        _multiSelectState.SetRange(_multiSelectState.RangeAnchorId, transcriptionId, allTranscriptionIds);
      }
      else if (isCtrlPressed)
      {
        // Toggle selection
        _multiSelectState.Toggle(transcriptionId);
      }
      else
      {
        // Single selection (clear others)
        _multiSelectState.SetSingle(transcriptionId);
      }

      UpdateTranscriptionSelectionProperties();
      _multiSelectService.OnSelectionChanged(PanelId, _multiSelectState);
    }

    private void SelectAllTranscriptions()
    {
      if (_multiSelectState == null)
        return;

      _multiSelectState.Clear();
      foreach (var transcription in Transcriptions)
      {
        _multiSelectState.Add(transcription.Id);
      }
      if (Transcriptions.Count > 0)
      {
        _multiSelectState.RangeAnchorId = Transcriptions[0].Id;
      }

      UpdateTranscriptionSelectionProperties();
      _multiSelectService.OnSelectionChanged(PanelId, _multiSelectState);
      SelectAllTranscriptionsCommand.NotifyCanExecuteChanged();
    }

    private void ClearTranscriptionSelection()
    {
      if (_multiSelectState == null)
        return;

      _multiSelectState.Clear();
      UpdateTranscriptionSelectionProperties();
      _multiSelectService.OnSelectionChanged(PanelId, _multiSelectState);
    }

    private void UpdateTranscriptionSelectionProperties()
    {
      if (_multiSelectState == null)
      {
        SelectedTranscriptionCount = 0;
        HasMultipleTranscriptionSelection = false;
      }
      else
      {
        SelectedTranscriptionCount = _multiSelectState.Count;
        HasMultipleTranscriptionSelection = _multiSelectState.IsMultipleSelection;
      }

      OnPropertyChanged(nameof(SelectedTranscriptionCount));
      OnPropertyChanged(nameof(HasMultipleTranscriptionSelection));
    }
  }
}