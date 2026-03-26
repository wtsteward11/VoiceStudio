using System;
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
    private readonly IErrorLoggingService? _logService;
    private readonly ToastNotificationService? _toastNotificationService;
    private bool _isInitialized;
    private ISubscriptionToken? _projectChangedToken;
    private ISubscriptionToken? _assetAddedToken;

    /// <summary>Must match <see cref="RecordingViewModel"/> upload success publisher.</summary>
    private const string AssetSourceRecordingPanel = "recording-panel";

    /// <summary>Must match <see cref="ImportWorkflowService"/>.</summary>
    private const string AssetSourceImportWorkflow = "import-workflow";
    private readonly UndoRedoService? _undoRedoService;
    private readonly MultiSelectService _multiSelectService;
    private MultiSelectState? _multiSelectState;

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

    public ObservableCollection<SupportedLanguage> Languages { get; } = new();

    public TranscribeViewModel(
        IViewModelContext context,
        ITranscriptionClient transcriptionClient,
        IProjectAudioClient projectAudioClient)
        : base(context)
    {
      _transcriptionClient = transcriptionClient ?? throw new ArgumentNullException(nameof(transcriptionClient));
      _projectAudioClient = projectAudioClient ?? throw new ArgumentNullException(nameof(projectAudioClient));
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
    }

    /// <inheritdoc />
    public async Task OnActivatedAsync(CancellationToken cancellationToken = default)
    {
      SyncSelectedProjectFromContext();
      EnsureProjectChangedSubscription();
      EnsureAssetAddedSubscription();
      if (!_isInitialized)
        await InitializeAsync(cancellationToken);
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
    }

    private void OnProjectChanged(ProjectChangedEvent e)
    {
      Dispatcher.TryEnqueue(() =>
      {
        if (SelectedProjectId != e.ProjectId)
          SelectedProjectId = e.ProjectId;
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
      if (e.SourcePanelId != AssetSourceRecordingPanel && e.SourcePanelId != AssetSourceImportWorkflow)
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
        ReleasePanelEventSubscriptions();
      base.Dispose(disposing);
    }

    public IAsyncRelayCommand LoadLanguagesCommand { get; }
    public IAsyncRelayCommand TranscribeCommand { get; }
    public IAsyncRelayCommand LoadTranscriptionsCommand { get; }
    // GAP-CS-003: Dynamic engine discovery
    public IAsyncRelayCommand LoadEnginesCommand { get; }
    public IAsyncRelayCommand<TranscriptionResponse> DeleteTranscriptionCommand { get; }

    // Multi-select commands
    public IRelayCommand SelectAllTranscriptionsCommand { get; }
    public IRelayCommand ClearTranscriptionSelectionCommand { get; }

    /// <summary>Send selected transcription to Timeline as a subtitle track.</summary>
    public IRelayCommand SendToTimelineCommand { get; }

    private bool CanTranscribe()
    {
      return !string.IsNullOrWhiteSpace(SelectedAudioId);
    }

    partial void OnSelectedAudioIdChanged(string? value)
    {
      AudioPersistenceSemanticsHint = null;
      TranscribeCommand.NotifyCanExecuteChanged();
    }

    partial void OnIsLoadingChanged(bool value)
    {
      LoadLanguagesCommand.NotifyCanExecuteChanged();
      TranscribeCommand.NotifyCanExecuteChanged();
      LoadTranscriptionsCommand.NotifyCanExecuteChanged();
      DeleteTranscriptionCommand.NotifyCanExecuteChanged();
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
            .Select(s => new VoiceStudio.Core.Events.SubtitleSegment(s.Start, s.End, s.Text))
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
        var transcriptions = await _transcriptionClient.ListTranscriptionsAsync(SelectedAudioId, SelectedProjectId, cancellationToken);

        Transcriptions.Clear();
        foreach (var transcription in transcriptions.OrderByDescending(t => t.Created))
        {
          Transcriptions.Add(transcription);
        }

        if (Transcriptions.Count > 0)
        {
          _toastNotificationService?.ShowSuccess("Transcriptions Loaded", $"Loaded {Transcriptions.Count} transcription(s)");
        }
      }
      catch (OperationCanceledException)
      {
        return; // User cancelled
      }
      catch (Exception ex)
      {
        ErrorMessage = $"Failed to load transcriptions: {ex.Message}";
        await HandleErrorAsync(ex, "LoadTranscriptions");
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
      if (value != null)
      {
        TranscriptionText = value.Text;
      }
      else
      {
        TranscriptionText = string.Empty;
      }

      SendToTimelineCommand.NotifyCanExecuteChanged();
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