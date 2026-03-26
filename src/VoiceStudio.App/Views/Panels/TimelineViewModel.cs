using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VoiceStudio.Core.Models;
using VoiceStudio.Core.Panels;
using VoiceStudio.Core.Services;
using VoiceStudio.App.Controls;
using VoiceStudio.App.Services;
using VoiceStudio.App.Services.UndoableActions;
using VoiceStudio.App.Utilities;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using VoiceStudio.App.Logging;
using VoiceStudio.App.ViewModels;
using VoiceStudio.Core.Events;

namespace VoiceStudio.App.Views.Panels
{
  // GAP-005: Updated to inherit from BaseViewModel for standardized error handling
  public partial class TimelineViewModel : BaseViewModel, IPanelView, IPanelLifecycle, ITimelineTransportController
  {
    private readonly ITimelineSynthesisService _synthesisService;
    private readonly ITimelineClipService _clipService;
    private readonly ITimelineTrackService _trackService;
    private readonly ITimelineTranscriptionService _transcriptionService;
    private readonly IProjectAudioClient _projectAudioClient;
    private readonly IAudioVisualizationService _audioVisualizationService;
    private readonly IProjectsClient _projectsClient;
    private readonly IProfilesClient _profilesClient;
    private readonly IDialogService _dialogService;
    private IEventAggregator? _eventAggregator;
    private IContextManager? _contextManager;
    private ISubscriptionToken? _navigateToken;
    private ISubscriptionToken? _addToTimelineToken;
    private ISubscriptionToken? _transcriptionCompletedToken;
    private ISubscriptionToken? _synthesisCompletedToken;
    private ISubscriptionToken? _backupRestoredToken;
    private readonly IAudioPlayerService _audioPlayer;
    private readonly ToastNotificationService? _toastNotificationService;
    private readonly UndoRedoService? _undoRedoService;
    private readonly IErrorPresentationService? _errorService;
    private readonly IErrorLoggingService? _logService;
    private readonly ISettingsService? _settingsService;
    private readonly RecentProjectsService? _recentProjectsService;

    public string PanelId => PanelIds.Timeline;
    public string DisplayName => ResourceHelper.GetString("Panel.Timeline.DisplayName", "Timeline");
    public PanelRegion Region => PanelRegion.Center;

    [ObservableProperty]
    private ObservableCollection<Project> projects = new();

    [ObservableProperty]
    private Project? selectedProject;

    [ObservableProperty]
    private bool isLoading;

    [ObservableProperty]
    private string? errorMessage;

    [ObservableProperty]
    private string selectedEngine = "xtts"; // xtts, chatterbox, tortoise

    [ObservableProperty]
    private bool enhanceQuality;

    [ObservableProperty]
    private string synthesisText = string.Empty;

    [ObservableProperty]
    private string? selectedProfileId;

    [ObservableProperty]
    private ObservableCollection<VoiceProfile> availableProfiles = new();

    [ObservableProperty]
    private ObservableCollection<AudioTrack> tracks = new();

    [ObservableProperty]
    private AudioTrack? selectedTrack;

    [ObservableProperty]
    private double? lastQualityScore;

    [ObservableProperty]
    private string? lastSynthesizedAudioUrl;

    [ObservableProperty]
    private string? lastSynthesizedAudioId;

    [ObservableProperty]
    private double? lastSynthesizedDuration;

    [ObservableProperty]
    private bool canPlayAudio;

    [ObservableProperty]
    private bool isPlaying;

    [ObservableProperty]
    private double currentPlaybackPosition;

    [ObservableProperty]
    private bool isPreviewing;

    // Current audio file path for preview (stored when playing audio)
    private string? _currentAudioFilePath;

    // Preview settings
    private bool _previewEnabled = true;
    private double _previewDuration = 0.15; // 150ms
    private double _previewVolume = 0.6; // 60% volume

    partial void OnCurrentPlaybackPositionChanged(double value)
    {
      OnPropertyChanged(nameof(PlayheadPosition));
      OnPropertyChanged(nameof(IsPlayheadVisible));
    }

    partial void OnIsPreviewingChanged(bool value)
    {
      OnPropertyChanged(nameof(IsPlayheadVisible));
      OnPropertyChanged(nameof(PlayheadPulsing));
    }

    [ObservableProperty]
    private double timelineZoom = 1.0;

    // Multi-select support
    private readonly MultiSelectService _multiSelectService;
    private MultiSelectState? _multiSelectState;

    [ObservableProperty]
    private int selectedClipCount;

    [ObservableProperty]
    private bool hasMultipleClipSelection;

    // Pixels per second for timeline rendering (can be adjusted)
    private const double PIXELS_PER_SECOND = 100.0;

    /// <summary>
    /// Total timeline duration in seconds (for ruler). Computed from tracks or default 60.
    /// </summary>
    public double TotalDuration => ComputeTotalDuration();

    /// <summary>
    /// Pixels per second for timeline ruler and clip positioning.
    /// </summary>
    public double PixelsPerSecond => PIXELS_PER_SECOND * TimelineZoom;

    /// <summary>
    /// Playhead position in pixels for visual rendering.
    /// </summary>
    public double PlayheadPosition => CurrentPlaybackPosition * PIXELS_PER_SECOND * TimelineZoom;

    /// <summary>
    /// Visibility of the playhead indicator.
    /// </summary>
    public bool IsPlayheadVisible => IsPlaying || _audioPlayer.IsPlaying || IsPreviewing;

    /// <summary>
    /// Whether the playhead should pulse (during preview).
    /// </summary>
    public bool PlayheadPulsing => IsPreviewing;

    /// <summary>
    /// Command to seek to a specific pixel position on the timeline.
    /// </summary>
    public IRelayCommand<double> SeekToPositionCommand { get; }

    // Multi-select commands
    public IRelayCommand SelectAllClipsCommand { get; }
    public IRelayCommand ClearClipSelectionCommand { get; }

    public bool IsClipSelected(string clipId) => _multiSelectState?.SelectedIds.Contains(clipId) ?? false;

    // Get all clips from all tracks
    private IEnumerable<AudioClip> GetAllClips()
    {
      return Tracks.SelectMany(track => track.Clips ?? new List<AudioClip>());
    }

    partial void OnTimelineZoomChanged(double value)
    {
      OnPropertyChanged(nameof(ZoomLevelDisplay));
      OnPropertyChanged(nameof(PixelsPerSecond));
    }

    private double ComputeTotalDuration()
    {
      double max = 60.0;
      foreach (var track in Tracks)
      {
        foreach (var clip in track.Clips ?? new List<AudioClip>())
        {
          var end = clip.StartTime + clip.Duration.TotalSeconds;
          if (end > max)
            max = end;
        }
      }
      return Math.Max(60, max + 10);
    }

    private void NotifyTotalDurationChanged()
    {
      OnPropertyChanged(nameof(TotalDuration));
    }

    [ObservableProperty]
    private ObservableCollection<ProjectAudioFile> projectAudioFiles = new();

    [ObservableProperty]
    private ObservableCollection<Controls.SpectrogramFrame> spectrogramFrames = new();

    [ObservableProperty]
    private List<float> waveformSamples = new();

    [ObservableProperty]
    private ProjectAudioFile? selectedAudioFile;

    [ObservableProperty]
    private string visualizationMode = "spectrogram";

    [ObservableProperty]
    private bool showSpectrogram = true;

    [ObservableProperty]
    private bool showWaveform;

    public Visibility SpectrogramVisibility => ShowSpectrogram ? Visibility.Visible : Visibility.Collapsed;

    public Visibility WaveformVisibility => ShowWaveform ? Visibility.Visible : Visibility.Collapsed;

    public bool HasTracks => Tracks?.Count > 0;

    public bool HasProjectAudioFiles => ProjectAudioFiles?.Count > 0;

    // -----------------------------------------------------------------------
    // Transcript Track (Audit C-4: M-1 + X-3 remediation)
    // -----------------------------------------------------------------------

    /// <summary>
    /// Transcript segments for display as a subtitle track overlay.
    /// Each segment has Start, End (seconds) and Text.
    /// </summary>
    [ObservableProperty]
    private ObservableCollection<TranscriptSegmentDisplay> transcriptSegments = new();

    /// <summary>Whether transcript overlay is visible on the timeline.</summary>
    [ObservableProperty]
    private bool showTranscriptTrack;

    /// <summary>Computed visibility for the transcript track.</summary>
    public Visibility TranscriptTrackVisibility =>
        ShowTranscriptTrack && TranscriptSegments.Count > 0
            ? Visibility.Visible
            : Visibility.Collapsed;

    /// <summary>Whether there are any transcript segments loaded.</summary>
    public Visibility HasTranscriptSegments =>
        TranscriptSegments.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>Number of transcript segments.</summary>
    public int TranscriptSegmentCount => TranscriptSegments.Count;

    /// <summary>Command to clear all transcript segments from the timeline.</summary>
    [RelayCommand]
    private void ClearTranscript()
    {
      TranscriptSegments.Clear();
      ShowTranscriptTrack = false;
      OnPropertyChanged(nameof(HasTranscriptSegments));
      OnPropertyChanged(nameof(TranscriptSegmentCount));
      OnPropertyChanged(nameof(TranscriptTrackVisibility));
    }

    partial void OnShowTranscriptTrackChanged(bool value)
    {
      OnPropertyChanged(nameof(TranscriptTrackVisibility));
      OnPropertyChanged(nameof(HasTranscriptSegments));
    }

    /// <summary>
    /// Load transcript segments from a transcription ID.
    /// Called when user clicks "Send to Timeline" from the Transcribe panel.
    /// </summary>
    public async Task LoadTranscriptSegmentsAsync(
        string transcriptionId,
        CancellationToken ct = default)
    {
      try
      {
        var response = await _transcriptionService.GetTranscriptionAsync(transcriptionId, ct);
        if (response != null && response.Segments != null)
        {
          TranscriptSegments.Clear();
          foreach (var seg in response.Segments)
          {
            TranscriptSegments.Add(new TranscriptSegmentDisplay
            {
              Text = seg.Text ?? "",
              StartSeconds = seg.Start,
              EndSeconds = seg.End,
              PositionPixels = seg.Start * PIXELS_PER_SECOND * TimelineZoom,
              WidthPixels = Math.Max(
                  (seg.End - seg.Start) * PIXELS_PER_SECOND * TimelineZoom,
                  30) // Minimum 30px width
            });
          }

          ShowTranscriptTrack = true;
          OnPropertyChanged(nameof(TranscriptTrackVisibility));
          OnPropertyChanged(nameof(HasTranscriptSegments));
          OnPropertyChanged(nameof(TranscriptSegmentCount));
          _toastNotificationService?.ShowSuccess(
              "Transcript Loaded",
              $"Loaded {TranscriptSegments.Count} segments to timeline");
        }
      }
      catch (Exception ex)
      {
        _logService?.LogError(ex, "LoadTranscriptSegments");
        _toastNotificationService?.ShowError("Load Failed", $"Failed to load transcript: {ex.Message}");
      }
    }

    /// <summary>
    /// Seek to a specific transcript segment when clicked.
    /// </summary>
    public void SeekToTranscriptSegment(TranscriptSegmentDisplay segment)
    {
      if (segment != null)
      {
        var timeInSeconds = segment.StartSeconds;
        _audioPlayer.Seek(timeInSeconds);
        CurrentPlaybackPosition = timeInSeconds;
        _toastNotificationService?.ShowInfo("Seek", $"Seeked to {timeInSeconds:F1}s");
      }
    }

    public TimelineViewModel(
      ITimelineSynthesisService synthesisService,
      ITimelineClipService clipService,
      ITimelineTrackService trackService,
      ITimelineTranscriptionService transcriptionService,
      IProjectAudioClient projectAudioClient,
      IAudioVisualizationService audioVisualizationService,
      IProjectsClient projectsClient,
      IProfilesClient profilesClient,
      IAudioPlayerService audioPlayer,
      MultiSelectService multiSelectService,
      IDialogService dialogService,
      ToastNotificationService? toastNotificationService = null,
      UndoRedoService? undoRedoService = null,
      IErrorPresentationService? errorService = null,
      IErrorLoggingService? logService = null,
      ISettingsService? settingsService = null,
      RecentProjectsService? recentProjectsService = null)
        : base(AppServices.GetViewModelContext())
    {
      _synthesisService = synthesisService ?? throw new ArgumentNullException(nameof(synthesisService));
      _clipService = clipService ?? throw new ArgumentNullException(nameof(clipService));
      _trackService = trackService ?? throw new ArgumentNullException(nameof(trackService));
      _transcriptionService = transcriptionService ?? throw new ArgumentNullException(nameof(transcriptionService));
      _projectAudioClient = projectAudioClient ?? throw new ArgumentNullException(nameof(projectAudioClient));
      _audioVisualizationService = audioVisualizationService ?? throw new ArgumentNullException(nameof(audioVisualizationService));
      _projectsClient = projectsClient ?? throw new ArgumentNullException(nameof(projectsClient));
      _profilesClient = profilesClient ?? throw new ArgumentNullException(nameof(profilesClient));
      _audioPlayer = audioPlayer ?? throw new ArgumentNullException(nameof(audioPlayer));
      _multiSelectService = multiSelectService ?? throw new ArgumentNullException(nameof(multiSelectService));
      _dialogService = dialogService ?? throw new ArgumentNullException(nameof(dialogService));
      _multiSelectState = _multiSelectService.GetState(PanelId);

      // Get optional services using helper (reduces code duplication)
      _toastNotificationService = toastNotificationService;
      _undoRedoService = undoRedoService;
      _errorService = errorService;
      _logService = logService;
      _settingsService = settingsService;
      _recentProjectsService = recentProjectsService;

      LoadProjectsCommand = new EnhancedAsyncRelayCommand(async (ct) =>
      {
        using var profiler = PerformanceProfiler.StartCommand("LoadProjects");
        await LoadProjectsAsync(ct);
      });

      CreateProjectCommand = new EnhancedAsyncRelayCommand<string>(async (name, ct) =>
      {
        using var profiler = PerformanceProfiler.StartCommand("CreateProject");
        await CreateProjectAsync(name, ct);
      });

      DeleteProjectCommand = new EnhancedAsyncRelayCommand<string>(async (projectId, ct) =>
      {
        using var profiler = PerformanceProfiler.StartCommand("DeleteProject");
        await DeleteProjectAsync(projectId, ct);
      });

      SynthesizeCommand = new EnhancedAsyncRelayCommand(async (ct) =>
      {
        using var profiler = PerformanceProfiler.StartCommand("Synthesize");
        await SynthesizeAsync(ct);
      }, () => !string.IsNullOrWhiteSpace(SynthesisText) && !string.IsNullOrWhiteSpace(SelectedProfileId) && SelectedProject != null);

      LoadProfilesCommand = new EnhancedAsyncRelayCommand(async (ct) =>
      {
        using var profiler = PerformanceProfiler.StartCommand("LoadProfiles");
        await LoadProfilesAsync(ct);
      });

      PlayAudioCommand = new EnhancedAsyncRelayCommand(async (ct) =>
      {
        using var profiler = PerformanceProfiler.StartCommand("PlayAudio");
        await PlayAudioAsync(ct);
      }, () => CanPlayAudio && !IsLoading && !IsPlaying);

      StopAudioCommand = new RelayCommand(StopAudio, () => IsPlaying || _audioPlayer.IsPlaying);
      PauseAudioCommand = new RelayCommand(PauseAudio, () => IsPlaying || _audioPlayer.IsPlaying);
      ResumeAudioCommand = new RelayCommand(ResumeAudio, () => _audioPlayer.IsPaused);

      AddTrackCommand = new EnhancedAsyncRelayCommand(async (ct) =>
      {
        using var profiler = PerformanceProfiler.StartCommand("AddTrack");
        await AddTrackAsync(ct);
      }, () => SelectedProject != null && !IsLoading);

      AddClipToTrackCommand = new EnhancedAsyncRelayCommand(async (ct) =>
      {
        using var profiler = PerformanceProfiler.StartCommand("AddClipToTrack");
        await AddClipToTrackAsync(ct);
      }, () => !string.IsNullOrWhiteSpace(LastSynthesizedAudioId) && SelectedTrack != null);

      LoadAudioFileIntoClipCommand = new EnhancedAsyncRelayCommand<ProjectAudioFile>(async (audioFile, ct) =>
      {
        using var profiler = PerformanceProfiler.StartCommand("LoadAudioFileIntoClip");
        await LoadAudioFileIntoClipAsync(audioFile, ct);
      }, (audioFile) => audioFile != null && SelectedTrack != null);

      LoadProjectAudioCommand = new EnhancedAsyncRelayCommand(async (ct) =>
      {
        using var profiler = PerformanceProfiler.StartCommand("LoadProjectAudio");
        await LoadProjectAudioAsync(ct);
      }, () => SelectedProject != null && !IsLoading);

      PlayProjectAudioCommand = new EnhancedAsyncRelayCommand<string>(async (filename, ct) =>
      {
        using var profiler = PerformanceProfiler.StartCommand("PlayProjectAudio");
        await PlayProjectAudioAsync(filename, ct);
      }, (filename) => SelectedProject != null && !string.IsNullOrWhiteSpace(filename) && !IsLoading);

      ZoomInCommand = new RelayCommand(ZoomIn);
      ZoomOutCommand = new RelayCommand(ZoomOut);
      SeekToPositionCommand = new RelayCommand<double>(SeekToPosition);

      Tracks.CollectionChanged += (_, _) => NotifyTotalDurationChanged();

      // Multi-select commands
      SelectAllClipsCommand = new RelayCommand(SelectAllClips, () => GetAllClips().Any());
      ClearClipSelectionCommand = new RelayCommand(ClearClipSelection);

      DeleteSelectedClipsCommand = new EnhancedAsyncRelayCommand(async (ct) =>
      {
        using var profiler = PerformanceProfiler.StartCommand("DeleteSelectedClips");
        await DeleteSelectedClipsAsync(ct);
      }, () => SelectedClipCount > 0);

      PasteClipCommand = new EnhancedAsyncRelayCommand<AudioClip?>(async (fromClipboard, ct) =>
      {
        using var profiler = PerformanceProfiler.StartCommand("PasteClip");
        await PasteClipAsync(fromClipboard, ct);
      }, (clip) => clip != null && SelectedTrack != null && SelectedProject != null);

      DuplicateClipCommand = new EnhancedAsyncRelayCommand<AudioClip>(async (clip, ct) =>
      {
        if (clip == null)
          return;
        using var profiler = PerformanceProfiler.StartCommand("DuplicateClip");
        await DuplicateClipAsync(clip, ct);
      }, (clip) => clip != null && SelectedTrack != null && SelectedProject != null);

      // Subscribe to selection changes
      _multiSelectService.SelectionChanged += (s, e) =>
      {
        if (e.PanelId == PanelId)
        {
          UpdateClipSelectionProperties();
          OnPropertyChanged(nameof(SelectedClipCount));
          OnPropertyChanged(nameof(HasMultipleClipSelection));
        }
      };

      // Subscribe to audio player events
      _audioPlayer.IsPlayingChanged += (s, e) =>
      {
        IsPlaying = _audioPlayer.IsPlaying;
        PlayAudioCommand.NotifyCanExecuteChanged();
        StopAudioCommand.NotifyCanExecuteChanged();
        PauseAudioCommand.NotifyCanExecuteChanged();
        ResumeAudioCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(IsPlayheadVisible));
      };

      _audioPlayer.PositionChanged += (s, position) => CurrentPlaybackPosition = position;

      // Subscribe to cross-panel events (Audit X-3, X-6, C.3, C.4; GAP-W2)
      _eventAggregator = AppServices.TryGetEventAggregator();
      _contextManager = AppServices.TryGetContextManager();
      if (_eventAggregator != null)
      {
        _navigateToken = _eventAggregator.Subscribe<NavigateToEvent>(OnNavigateToTimeline);
        _addToTimelineToken = _eventAggregator.Subscribe<AddToTimelineEvent>(OnAddToTimeline);
        _transcriptionCompletedToken = _eventAggregator.Subscribe<TranscriptionCompletedEvent>(OnTranscriptionCompleted);
        _synthesisCompletedToken = _eventAggregator.Subscribe<SynthesisCompletedEvent>(OnSynthesisCompleted);
        _backupRestoredToken = _eventAggregator.Subscribe<BackupRestoredEvent>(
            async evt => await RunOnDispatcherQueueAsync(() => ApplyBackupRestoredAsync(evt, CancellationToken.None)));
      }

      // Load preview settings
      _ = LoadPreviewSettingsAsync();
    }

    /// <summary>
    /// Handles NavigateToEvent when the target is this timeline panel.
    /// Supports actions: "addClip" (from Synthesis), "loadTranscript" (from Transcribe).
    /// </summary>
    private void OnNavigateToTimeline(NavigateToEvent e)
    {
      if (e.TargetPanelId != PanelId && e.TargetPanelId != "timeline")
        return;

      var parameters = e.Parameters;
      if (parameters == null)
        return;

      if (parameters.TryGetValue("action", out var actionObj) && actionObj is string action)
      {
        switch (action)
        {
          case "loadTranscript":
            if (parameters.TryGetValue("transcriptionId", out var tidObj) && tidObj is string tid)
            {
              _ = LoadTranscriptSegmentsAsync(tid);
            }
            break;

          case "addClip":
            // Handle clip addition from Synthesis panel
            if (parameters.TryGetValue("clipName", out var nameObj) && nameObj is string clipName)
            {
              _toastNotificationService?.ShowSuccess("Clip Added", $"'{clipName}' added to timeline");
            }
            break;
        }
      }
    }

    /// <summary>
    /// Handles AddToTimelineEvent from Synthesis panels (C.3 remediation).
    /// Creates a clip on the current or first available track.
    /// </summary>
    private void OnAddToTimeline(AddToTimelineEvent e)
    {
      // Must have a project and track to add clips
      if (SelectedProject == null)
      {
        _toastNotificationService?.ShowWarning(
            "No project selected",
            "Please select or create a project first");
        return;
      }

      // Ensure we have a track
      var targetTrack = SelectedTrack ?? Tracks.FirstOrDefault();
      if (targetTrack == null)
      {
        _toastNotificationService?.ShowWarning(
            "No track available",
            "Creating a default track...");
        // Fire and forget - create track then retry
        _ = AddTrackAndClipAsync(e);
        return;
      }

      // Create the clip
      AddClipToTrack(targetTrack, e);
    }

    /// <summary>
    /// GAP-W2: Handle SynthesisCompletedEvent for auto-add to timeline when synthesis completes.
    /// Converts to AddToTimelineEvent and reuses existing add logic. Pass 01: ProfileId for backend.
    /// </summary>
    private void OnSynthesisCompleted(SynthesisCompletedEvent e)
    {
      var addEvent = new AddToTimelineEvent(
          e.SourcePanelId,
          e.AudioId,
          e.AudioPath,
          e.Duration,
          clipName: $"Synthesis - {e.VoiceName ?? "Unknown"}",
          targetTrackIndex: null,
          insertPosition: null,
          profileId: e.ProfileId);
      OnAddToTimeline(addEvent);
    }

    /// <summary>
    /// C.4: Handle transcription completed event to display subtitles on timeline.
    /// </summary>
    private void OnTranscriptionCompleted(TranscriptionCompletedEvent e)
    {
      // Clear existing segments
      TranscriptSegments.Clear();

      if (e.Segments.Count == 0)
      {
        _toastNotificationService?.ShowInfo("Transcription", "No segments available for subtitle display");
        return;
      }

      // Convert segments to display format
      foreach (var segment in e.Segments)
      {
        var displaySegment = new TranscriptSegmentDisplay
        {
          Text = segment.Text,
          StartSeconds = segment.StartTime,
          EndSeconds = segment.EndTime,
          PositionPixels = segment.StartTime * TimelineZoom * 100 // Initial calculation
        };
        TranscriptSegments.Add(displaySegment);
      }

      // Show the transcript overlay
      ShowTranscriptTrack = true;
      OnPropertyChanged(nameof(TranscriptSegments));
      OnPropertyChanged(nameof(TranscriptTrackVisibility));

      _toastNotificationService?.ShowSuccess(
        "Subtitles Loaded",
        $"{e.Segments.Count} segments added to timeline");
    }

    /// <inheritdoc />
    public Task OnActivatedAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    /// <inheritdoc />
    public async Task RefreshAsync(CancellationToken cancellationToken = default) =>
        await LoadProjectsAsync(cancellationToken);

    /// <inheritdoc />
    /// <summary>Unsubscribe from EventAggregator to prevent memory leaks (GAP-W3).</summary>
    public Task OnDeactivatedAsync(CancellationToken cancellationToken = default)
    {
      _navigateToken?.Dispose();
      _navigateToken = null;
      _addToTimelineToken?.Dispose();
      _addToTimelineToken = null;
      _transcriptionCompletedToken?.Dispose();
      _transcriptionCompletedToken = null;
      _synthesisCompletedToken?.Dispose();
      _synthesisCompletedToken = null;
      _backupRestoredToken?.Dispose();
      _backupRestoredToken = null;
      return Task.CompletedTask;
    }

    /// <summary>
    /// Pass 06: Reload project/profile lists after backup restore; reconcile active project with disk.
    /// Public for seam tests (<c>VoiceStudio.App</c> uses <c>GenerateAssemblyInfo=false</c>, so <c>InternalsVisibleTo</c> is not emitted from the csproj).
    /// </summary>
    public async Task ApplyBackupRestoredAsync(BackupRestoredEvent evt, CancellationToken cancellationToken)
    {
      if (evt.RestoreProjects)
      {
        var previousId = SelectedProject?.Id;
        await LoadProjectsAsync(cancellationToken);
        if (string.IsNullOrEmpty(previousId))
        {
          SelectedProject = null;
        }
        else
        {
          SelectedProject = Projects.FirstOrDefault(p => p.Id == previousId);
        }
      }

      if (evt.RestoreProfiles)
      {
        await LoadProfilesAsync(cancellationToken);
      }
    }

    private static async Task RunOnDispatcherQueueAsync(Func<Task> work)
    {
      var dq = DispatcherQueue.GetForCurrentThread();
      if (dq == null || dq.HasThreadAccess)
      {
        await work().ConfigureAwait(true);
        return;
      }

      var tcs = new TaskCompletionSource<object?>();
      dq.TryEnqueue(() => _ = RunAsync());
      async Task RunAsync()
      {
        try
        {
          await work().ConfigureAwait(true);
          tcs.TrySetResult(null);
        }
        catch (Exception ex)
        {
          tcs.TrySetException(ex);
        }
      }

      await tcs.Task.ConfigureAwait(false);
    }

    private async Task AddTrackAndClipAsync(AddToTimelineEvent e)
    {
      try
      {
        await AddTrackAsync(CancellationToken.None);
        var targetTrack = SelectedTrack ?? Tracks.FirstOrDefault();
        if (targetTrack != null)
        {
          AddClipToTrack(targetTrack, e);
        }
      }
      catch (Exception ex)
      {
        _logService?.LogError(ex, "AddTrackAndClip");
        _toastNotificationService?.ShowError(
            "Insertion failed",
            $"Could not add track or clip: {ErrorHandler.GetUserFriendlyMessage(ex)}");
      }
    }

    private void AddClipToTrack(AudioTrack track, AddToTimelineEvent e)
    {
      try
      {
        // Calculate start time (end of last clip or 0)
        var startTime = track.Clips.Count > 0
            ? track.Clips.Max(c => c.EndTime)
            : 0.0;

        // Pass 01: ProfileId required by backend; fallback to IContextManager when event has none
        var profileId = e.ProfileId;
        if (string.IsNullOrWhiteSpace(profileId))
        {
          var ctx = AppServices.TryGetContextManager();
          profileId = ctx?.ActiveProfileId ?? "";
        }

        if (string.IsNullOrWhiteSpace(profileId))
        {
          _toastNotificationService?.ShowWarning(
              "Voice profile required",
              "Select a voice profile in Profiles or Synthesis panel, then add to timeline.");
          return;
        }

        var newClip = new AudioClip
        {
          Id = Guid.NewGuid().ToString(),
          Name = e.ClipName ?? $"Clip {track.Clips.Count + 1}",
          AudioId = e.AudioId,
          AudioUrl = e.AudioPath,
          ProfileId = profileId ?? "",
          StartTime = startTime,
          Duration = e.Duration
        };

        track.Clips.Add(newClip);

        // Pass 01: Select the newly inserted clip so user can find it immediately
        if (_multiSelectState != null)
        {
          _multiSelectState.SetSingle(newClip.Id);
          UpdateClipSelectionProperties();
          _multiSelectService.OnSelectionChanged(PanelId, _multiSelectState);
        }

        // Register undo action
        if (_undoRedoService != null)
        {
          var action = new AddClipAction(Tracks, track, newClip);
          _undoRedoService.RegisterAction(action);
        }

        _toastNotificationService?.ShowSuccess(
            $"'{newClip.Name}' added to {track.Name}",
            "Clip Added");

        // Save to backend asynchronously (Pass 01: workflow-step-specific failure handling)
        if (SelectedProject != null)
        {
          _ = PersistClipToBackendAsync(SelectedProject.Id, track.Id, newClip);
        }
      }
      catch (Exception ex)
      {
        _logService?.LogError(ex, "AddClipToTrack");
        _toastNotificationService?.ShowError(
            "Insertion failed",
            $"Could not add clip to timeline: {ErrorHandler.GetUserFriendlyMessage(ex)}");
      }
    }

    /// <summary>Pass 01: Persist clip to backend with workflow-step-specific error handling.</summary>
    private async Task PersistClipToBackendAsync(string projectId, string trackId, AudioClip clip)
    {
      try
      {
        await _clipService.CreateClipAsync(projectId, trackId, clip, CancellationToken.None);
      }
      catch (Exception ex)
      {
        _logService?.LogError(ex, "PersistClipToBackend");
        _toastNotificationService?.ShowError(
            "Clip saved locally but failed to save to project",
            $"Project sync failed: {ErrorHandler.GetUserFriendlyMessage(ex)}");
      }
    }

    private async Task LoadPreviewSettingsAsync()
    {
      try
      {
        var settingsService = _settingsService;
        if (settingsService != null)
        {
          var settings = await settingsService.LoadSettingsAsync();
          if (settings?.Timeline != null)
          {
            _previewEnabled = settings.Timeline.PreviewEnabled;
            _previewDuration = settings.Timeline.PreviewDuration;
            _previewVolume = settings.Timeline.PreviewVolume;
          }
        }
      }
      catch (Exception ex)
      {
        ErrorLogger.LogWarning($"Best effort operation failed: {ex.Message}", "TimelineViewModel.LoadPreviewSettingsAsync");
      }
    }

    public EnhancedAsyncRelayCommand LoadProjectsCommand { get; }
    public EnhancedAsyncRelayCommand<string> CreateProjectCommand { get; }
    public EnhancedAsyncRelayCommand<string> DeleteProjectCommand { get; }
    public EnhancedAsyncRelayCommand SynthesizeCommand { get; }
    public EnhancedAsyncRelayCommand LoadProfilesCommand { get; }
    public EnhancedAsyncRelayCommand PlayAudioCommand { get; }
    public IRelayCommand StopAudioCommand { get; }
    public IRelayCommand PauseAudioCommand { get; }
    public IRelayCommand ResumeAudioCommand { get; }
    public EnhancedAsyncRelayCommand AddTrackCommand { get; }
    public EnhancedAsyncRelayCommand AddClipToTrackCommand { get; }
    public EnhancedAsyncRelayCommand LoadProjectAudioCommand { get; }
    public EnhancedAsyncRelayCommand<string> PlayProjectAudioCommand { get; }
    public IAsyncRelayCommand<ProjectAudioFile> LoadAudioFileIntoClipCommand { get; }
    public IRelayCommand ZoomInCommand { get; }
    public IRelayCommand ZoomOutCommand { get; }

    // Multi-select commands
    public EnhancedAsyncRelayCommand DeleteSelectedClipsCommand { get; }
    public EnhancedAsyncRelayCommand<AudioClip?> PasteClipCommand { get; }
    public EnhancedAsyncRelayCommand<AudioClip> DuplicateClipCommand { get; }

    /// <summary>
    /// Paste a clip from clipboard. View reads clipboard and passes the clip. Panel hardening: no service locator.
    /// </summary>
    public async Task PasteClipAsync(AudioClip? fromClipboard, CancellationToken cancellationToken = default)
    {
      if (fromClipboard == null || SelectedTrack == null || SelectedProject == null)
      {
        _toastNotificationService?.ShowWarning(
            ResourceHelper.GetString("Project.NoClipboardOrTrack", "No clip in clipboard or no track selected"),
            ResourceHelper.GetString("Toast.Title.Paste", "Paste"));
        return;
      }

      var pastedClip = new AudioClip
      {
        Id = Guid.NewGuid().ToString(),
        Name = fromClipboard.Name + " (Copy)",
        ProfileId = fromClipboard.ProfileId,
        AudioId = fromClipboard.AudioId,
        AudioUrl = fromClipboard.AudioUrl,
        Duration = fromClipboard.Duration,
        StartTime = SelectedTrack.Clips.Count > 0
            ? SelectedTrack.Clips.Max(c => c.EndTime)
            : 0.0,
        Engine = fromClipboard.Engine,
        QualityScore = fromClipboard.QualityScore,
        WaveformSamples = fromClipboard.WaveformSamples
      };

      var track = SelectedTrack;
      var project = SelectedProject;

      _undoRedoService?.AddAction(
          "Paste Clip",
          () =>
          {
            track.Clips.Remove(pastedClip);
            _ = _clipService.DeleteClipAsync(project.Id, track.Id, pastedClip.Id);
          },
          async () =>
          {
            track.Clips.Add(pastedClip);
            try
            {
              await _clipService.CreateClipAsync(project.Id, track.Id, pastedClip, cancellationToken);
            }
            catch (Exception ex)
            {
              ErrorLogger.LogWarning($"Redo paste sync failed: {ex.Message}", "TimelineViewModel");
            }
          });

      try
      {
        pastedClip = await _clipService.CreateClipAsync(project.Id, track.Id, pastedClip, cancellationToken);
      }
      catch (Exception ex)
      {
        _toastNotificationService?.ShowWarning(
            ResourceHelper.FormatString("Project.PasteBackendWarning", ex.Message),
            ResourceHelper.GetString("Toast.Title.Paste", "Paste"));
      }

      track.Clips.Add(pastedClip);
      _toastNotificationService?.ShowSuccess(
          ResourceHelper.FormatString("Project.ClipPasted", pastedClip.Name),
          ResourceHelper.GetString("Toast.Title.Pasted", "Pasted"));
    }

    /// <summary>
    /// Duplicate a clip. Panel hardening: no service locator.
    /// </summary>
    public async Task DuplicateClipAsync(AudioClip clip, CancellationToken cancellationToken = default)
    {
      if (SelectedTrack == null || SelectedProject == null)
      {
        _toastNotificationService?.ShowWarning(
            ResourceHelper.GetString("Project.NoTrackOrProject", "No track or project selected"),
            ResourceHelper.GetString("Toast.Title.Duplicate", "Duplicate"));
        return;
      }

      var duplicatedClip = new AudioClip
      {
        Id = Guid.NewGuid().ToString(),
        Name = clip.Name + " (Copy)",
        ProfileId = clip.ProfileId,
        AudioId = clip.AudioId,
        AudioUrl = clip.AudioUrl,
        Duration = clip.Duration,
        StartTime = clip.EndTime + 0.1,
        Engine = clip.Engine,
        QualityScore = clip.QualityScore,
        WaveformSamples = clip.WaveformSamples
      };

      var track = SelectedTrack;
      var project = SelectedProject;

      _undoRedoService?.AddAction(
          "Duplicate Clip",
          () =>
          {
            track.Clips.Remove(duplicatedClip);
            _ = _clipService.DeleteClipAsync(project.Id, track.Id, duplicatedClip.Id);
          },
          async () =>
          {
            track.Clips.Add(duplicatedClip);
            try
            {
              await _clipService.CreateClipAsync(project.Id, track.Id, duplicatedClip, cancellationToken);
            }
            catch (Exception ex)
            {
              ErrorLogger.LogWarning($"Redo duplicate sync failed: {ex.Message}", "TimelineViewModel");
            }
          });

      try
      {
        duplicatedClip = await _clipService.CreateClipAsync(project.Id, track.Id, duplicatedClip, cancellationToken);
      }
      catch (Exception ex)
      {
        _toastNotificationService?.ShowWarning(
            ResourceHelper.FormatString("Project.DuplicateBackendWarning", ex.Message),
            ResourceHelper.GetString("Toast.Title.Duplicate", "Duplicate"));
      }

      track.Clips.Add(duplicatedClip);
      _toastNotificationService?.ShowSuccess(
          ResourceHelper.FormatString("Project.ClipDuplicated", duplicatedClip.Name),
          ResourceHelper.GetString("Toast.Title.Duplicated", "Duplicated"));
    }

    /// <summary>
    /// Delete a single clip (used by View context menu). Panel hardening: confirmation via IDialogService, backend via _clipService.
    /// </summary>
    public async Task DeleteClipAsync(AudioClip clip, bool showConfirmation = true)
    {
      if (SelectedTrack == null || SelectedProject == null)
      {
        _toastNotificationService?.ShowWarning(
            ResourceHelper.GetString("Project.NoTrackOrProject", "No track or project selected"),
            ResourceHelper.GetString("Toast.Title.Delete", "Delete"));
        return;
      }

      if (showConfirmation)
      {
        var confirmed = await _dialogService.ShowConfirmationAsync(
            ResourceHelper.GetString("Project.DeleteClipTitle", "Delete Clip"),
            string.Format(ResourceHelper.GetString("Project.DeleteClipConfirm", "Are you sure you want to delete '{0}'? This action cannot be undone."), clip.Name),
            ResourceHelper.GetString("Common.Delete", "Delete"),
            ResourceHelper.GetString("Common.Cancel", "Cancel"));
        if (!confirmed)
          return;
      }

      var track = SelectedTrack;
      var project = SelectedProject;

      try
      {
        await _clipService.DeleteClipAsync(project.Id, track.Id, clip.Id);
        track.Clips?.Remove(clip);

        if (_undoRedoService != null)
        {
          var action = new DeleteClipsAction(Tracks, new[] { (track, clip) });
          _undoRedoService.RegisterAction(action);
        }

        _toastNotificationService?.ShowSuccess(
            ResourceHelper.GetString("Project.ClipDeleted", "Clip deleted"),
            ResourceHelper.GetString("Toast.Title.ClipDeleted", "Clip Deleted"));
      }
      catch (Exception ex)
      {
        var errorMsg = ErrorHandler.GetUserFriendlyMessage(ex);
        _errorService?.ShowError(ex, ResourceHelper.GetString("Error.DeleteClipFailed", "Failed to delete clip"));
        _logService?.LogError(ex, "DeleteClip");
        _toastNotificationService?.ShowError(
            ResourceHelper.FormatString("Error.DeleteClipFailed", errorMsg),
            ResourceHelper.GetString("Toast.Title.DeleteFailed", "Delete Failed"));
      }
    }

    private async Task LoadProjectsAsync(CancellationToken cancellationToken)
    {
      IsLoading = true;
      ErrorMessage = null;

      try
      {
        var projectsList = await _projectsClient.GetProjectsAsync(cancellationToken);

        Projects.Clear();
        foreach (var project in projectsList)
        {
          Projects.Add(project);
        }
      }
      catch (OperationCanceledException)
      {
        // User cancelled - expected
        return;
      }
      catch (Exception ex)
      {
        ErrorMessage = ErrorHandler.GetUserFriendlyMessage(ex);
        _errorService?.ShowError(ex, ResourceHelper.GetString("Project.LoadFailed", "Failed to load projects"));
        _logService?.LogError(ex, "LoadProjects");
      }
      finally
      {
        IsLoading = false;
      }
    }

    /// <summary>
    /// Navigates to and selects a project by ID. Used by INavigatablePanel search-result focus.
    /// </summary>
    /// <param name="itemId">Project ID.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>True if the project was found and selected; false otherwise.</returns>
    public async Task<bool> NavigateToProjectAsync(string itemId, CancellationToken ct)
    {
      if (string.IsNullOrEmpty(itemId))
        return false;

      var match = Projects.FirstOrDefault(p => p.Id == itemId);
      if (match != null)
      {
        SelectedProject = match;
        return true;
      }

      await LoadProjectsAsync(ct);
      match = Projects.FirstOrDefault(p => p.Id == itemId);
      if (match != null)
      {
        SelectedProject = match;
        return true;
      }

      return false;
    }

    private async Task CreateProjectAsync(string? name, CancellationToken cancellationToken)
    {
      if (string.IsNullOrWhiteSpace(name))
        return;

      IsLoading = true;
      ErrorMessage = null;

      try
      {
        var project = await _projectsClient.CreateProjectAsync(name, cancellationToken: cancellationToken);
        Projects.Add(project);
        SelectedProject = project;

        _toastNotificationService?.ShowSuccess(
            ResourceHelper.FormatString("Success.ProjectCreated", name),
            ResourceHelper.GetString("Toast.Title.ProjectCreated", "Project Created"));
      }
      catch (OperationCanceledException)
      {
        // User cancelled - expected
        return;
      }
      catch (Exception ex)
      {
        ErrorMessage = ErrorHandler.GetUserFriendlyMessage(ex);
        _errorService?.ShowError(ex, ResourceHelper.GetString("Error.CreateProjectFailed", "Failed to create project"));
        _logService?.LogError(ex, "CreateProject");
      }
      finally
      {
        IsLoading = false;
      }
    }

    private async Task DeleteProjectAsync(string? projectId, CancellationToken cancellationToken)
    {
      if (string.IsNullOrWhiteSpace(projectId))
        return;

      var project = Projects.FirstOrDefault(p => p.Id == projectId);
      if (project == null)
        return;

      // Show confirmation dialog (Panel Hardening: IDialogService per PANEL_HARDENING_PATTERN)
      var projectName = project.Name ?? ResourceHelper.GetString("Project.Unnamed", "Unnamed Project");
      var confirmed = await _dialogService.ShowConfirmationAsync(
          "Delete project?",
          $"Are you sure you want to delete '{projectName}'? This action cannot be undone.",
          "Delete",
          "Cancel");

      if (!confirmed)
        return;

      IsLoading = true;
      ErrorMessage = null;

      try
      {
        var success = await _projectsClient.DeleteProjectAsync(projectId, cancellationToken);
        if (success)
        {
          var projectToDelete = Projects.FirstOrDefault(p => p.Id == projectId);
          if (projectToDelete != null)
          {
            Projects.Remove(projectToDelete);
            if (SelectedProject?.Id == projectId)
            {
              SelectedProject = null;
            }

            _toastNotificationService?.ShowSuccess(
                ResourceHelper.FormatString("Success.ProjectCreated", projectName),
                ResourceHelper.GetString("Toast.Title.ProjectDeleted", "Project Deleted"));
          }
        }
        else
        {
          var errorMsg = ResourceHelper.GetString("Project.DeleteFailed", "Failed to delete project");
          ErrorMessage = errorMsg;
          _errorService?.ShowError(errorMsg, ResourceHelper.GetString("Toast.Title.DeleteFailed", "Delete Failed"));
        }
      }
      catch (OperationCanceledException)
      {
        // User cancelled - expected
        return;
      }
      catch (Exception ex)
      {
        ErrorMessage = ErrorHandler.GetUserFriendlyMessage(ex);
        _errorService?.ShowError(ex, ResourceHelper.GetString("Error.DeleteProjectFailed", "Failed to delete project"));
        _logService?.LogError(ex, "DeleteProject");
      }
      finally
      {
        IsLoading = false;
      }
    }

    private async Task LoadProfilesAsync(CancellationToken cancellationToken)
    {
      try
      {
        var profilesList = await _profilesClient.GetProfilesAsync(cancellationToken);
        AvailableProfiles.Clear();
        foreach (var profile in profilesList)
        {
          AvailableProfiles.Add(profile);
        }
      }
      catch (OperationCanceledException)
      {
        // User cancelled - expected
        return;
      }
      catch (Exception ex)
      {
        ErrorMessage = ErrorHandler.GetUserFriendlyMessage(ex);
        _errorService?.ShowError(ex, ResourceHelper.GetString("Error.LoadProfilesFailed", "Failed to load profiles"));
        _logService?.LogError(ex, "LoadProfiles");
      }
    }

    private async Task SynthesizeAsync(CancellationToken cancellationToken)
    {
      if (string.IsNullOrWhiteSpace(SynthesisText) || string.IsNullOrWhiteSpace(SelectedProfileId))
        return;

      IsLoading = true;
      ErrorMessage = null;

      try
      {
        var progress = new Progress<int>(p => SynthesizeCommand.ReportProgress(p));
        var projectId = SelectedProject?.Id;
        var result = await _synthesisService.SynthesizeAndSaveAsync(
          SelectedEngine,
          SelectedProfileId,
          SynthesisText,
          EnhanceQuality,
          projectId,
          progress,
          cancellationToken).ConfigureAwait(true);

        LastQualityScore = result.QualityScore;
        LastSynthesizedAudioUrl = result.AudioUrl;
        LastSynthesizedAudioId = result.AudioId;
        LastSynthesizedDuration = result.Duration;
        CanPlayAudio = !string.IsNullOrWhiteSpace(LastSynthesizedAudioUrl);
        PlayAudioCommand.NotifyCanExecuteChanged();
        AddClipToTrackCommand.NotifyCanExecuteChanged();

        if (projectId != null && result.SavedFilename == null)
        {
          ErrorMessage = ResourceHelper.GetString("Project.SynthesisSaveWarning", "Audio saved to project but filename could not be recorded.");
        }

        _toastNotificationService?.ShowSuccess(
          ResourceHelper.GetString("Timeline.SynthesisComplete", "Voice synthesis completed"),
          ResourceHelper.GetString("VoiceSynthesis.SynthesisComplete", "Synthesis Complete"));
      }
      catch (OperationCanceledException)
      {
        return;
      }
      catch (Exception ex)
      {
        ErrorMessage = ErrorHandler.GetUserFriendlyMessage(ex);
        _errorService?.ShowError(ex, ResourceHelper.GetString("Error.SynthesizeFailed", "Failed to synthesize voice"));
        _logService?.LogError(ex, "Synthesize");
      }
      finally
      {
        IsLoading = false;
      }
    }

    partial void OnSynthesisTextChanged(string value)
    {
      SynthesizeCommand.NotifyCanExecuteChanged();
    }

    partial void OnSelectedProfileIdChanged(string? value)
    {
      SynthesizeCommand.NotifyCanExecuteChanged();
    }

    private async Task PlayAudioAsync(CancellationToken cancellationToken)
    {
      if (string.IsNullOrWhiteSpace(LastSynthesizedAudioUrl))
        return;

      IsLoading = true;
      ErrorMessage = null;

      try
      {
        // Download audio file from URL (Phase 4: use shared HttpClient)
        var httpClient = AppServices.GetService<System.Net.Http.HttpClient>();
        if (httpClient == null)
          throw new InvalidOperationException("HttpClient not available");
        var audioBytes = await httpClient.GetByteArrayAsync(LastSynthesizedAudioUrl, cancellationToken);

        // Save to temporary file
        var tempPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"voicestudio_timeline_{Guid.NewGuid()}.wav");
        await System.IO.File.WriteAllBytesAsync(tempPath, audioBytes, cancellationToken);

        // Store file path for preview
        _currentAudioFilePath = tempPath;

        // Play audio file
        await _audioPlayer.PlayFileAsync(tempPath, () =>
        {
          // Cleanup temp file after playback
          try
          {
            if (System.IO.File.Exists(tempPath))
              System.IO.File.Delete(tempPath);
          }
          catch (Exception ex) { ErrorLogger.LogWarning($"Best effort operation failed: {ex.Message}", "TimelineViewModel.PlayAudioAsync"); }

          _currentAudioFilePath = null;
          IsPlaying = false;
        });

        IsPlaying = true;

        // Own global transport so main Play routes here
        var ctx = AppServices.TryGetContextManager();
        if (ctx != null)
        {
          var audioId = LastSynthesizedAudioId ?? "timeline";
          var title = SelectedProject != null ? $"Timeline: {SelectedProject.Name}" : "Timeline";
          ctx.SetCurrentPlayable(audioId, TransportSource.Timeline, title);
        }

        // Load visualization data for synthesized audio
        if (!string.IsNullOrWhiteSpace(LastSynthesizedAudioId))
        {
          var ct = new CancellationTokenSource(TimeSpan.FromSeconds(30)).Token;
          _ = LoadVisualizationDataAsync(LastSynthesizedAudioId, ct).ContinueWith(t =>
          {
            if (t.IsFaulted)
              _logService?.LogError(t.Exception?.InnerException ?? new Exception("LoadVisualizationData failed"), "LoadVisualizationData");
          }, TaskScheduler.Default);
        }
      }
      catch (OperationCanceledException)
      {
        // User cancelled - expected
        return;
      }
      catch (Exception ex)
      {
        ErrorMessage = ErrorHandler.GetUserFriendlyMessage(ex);
        _errorService?.ShowError(ex, ResourceHelper.GetString("Error.PlayAudioFailed", "Failed to play audio"));
        _logService?.LogError(ex, "PlayAudio");
      }
      finally
      {
        IsLoading = false;
      }
    }

    private void StopAudio()
    {
      try
      {
        _audioPlayer.Stop();
        IsPlaying = false;
      }
      catch (Exception ex)
      {
        ErrorMessage = ResourceHelper.FormatString("Timeline.StopPlaybackFailed", ex.Message);
      }
    }

    private void PauseAudio()
    {
      try
      {
        _audioPlayer.Pause();
      }
      catch (Exception ex)
      {
        ErrorMessage = ResourceHelper.FormatString("Error.PausePlaybackFailed", ex.Message);
      }
    }

    private void ResumeAudio()
    {
      try
      {
        _audioPlayer.Resume();
      }
      catch (Exception ex)
      {
        ErrorMessage = ResourceHelper.FormatString("Error.ResumePlaybackFailed", ex.Message);
      }
    }

    // ITimelineTransportController: decouples orchestration from UI-tree lookup
    Task ITimelineTransportController.PlayAsync()
    {
      if (PlayAudioCommand.CanExecute(null))
        return PlayAudioCommand.ExecuteAsync(null);
      return Task.CompletedTask;
    }

    void ITimelineTransportController.Pause()
    {
      if (PauseAudioCommand.CanExecute(null))
        PauseAudioCommand.Execute(null);
    }

    void ITimelineTransportController.Stop()
    {
      if (StopAudioCommand.CanExecute(null))
        StopAudioCommand.Execute(null);
    }

    partial void OnSelectedProjectChanged(Project? value)
    {
      SynthesizeCommand.NotifyCanExecuteChanged();
      AddTrackCommand.NotifyCanExecuteChanged();
      LoadProjectAudioCommand.NotifyCanExecuteChanged();
      PlayProjectAudioCommand.NotifyCanExecuteChanged();
      PasteClipCommand.NotifyCanExecuteChanged();
      DuplicateClipCommand.NotifyCanExecuteChanged();

      // Pass 02: Sync project selection to context so EffectsMixer receives ProjectChangedEvent
      _contextManager?.SetActiveProject(value?.Id, value != null ? (value.Name ?? ResourceHelper.GetString("Project.Unnamed", "Unnamed Project")) : null, InteractionIntent.Navigation);

      if (value != null)
      {
        // Add to recent projects (IDEA 16)
        try
        {
          var recentProjectsService = _recentProjectsService;
          if (recentProjectsService != null)
          {
            _ = recentProjectsService.AddRecentProjectAsync(value.Id, value.Name ?? ResourceHelper.GetString("Project.Unnamed", "Unnamed Project"));
          }
        }
        catch (Exception ex)
      {
        ErrorLogger.LogWarning($"Best effort operation failed: {ex.Message}", "TimelineViewModel.OnSelectedProjectChanged");
      }

        // Load tracks for the selected project
        var ct = new CancellationTokenSource(TimeSpan.FromSeconds(30)).Token;
        _ = LoadTracksForProject(value.Id, ct).ContinueWith(t =>
        {
          if (t.IsFaulted)
            _logService?.LogError(t.Exception?.InnerException ?? new Exception("LoadTracksForProject failed"), "LoadTracksForProject");
        }, TaskScheduler.Default);
        // Load project audio files
        _ = LoadProjectAudioAsync(CancellationToken.None);
      }
      else
      {
        Tracks.Clear();
        SelectedTrack = null;
        ProjectAudioFiles.Clear();
        SelectedAudioFile = null;
      }
    }

    partial void OnSelectedTrackChanged(AudioTrack? value)
    {
      AddClipToTrackCommand.NotifyCanExecuteChanged();
      PasteClipCommand.NotifyCanExecuteChanged();
      DuplicateClipCommand.NotifyCanExecuteChanged();
    }

    partial void OnVisualizationModeChanged(string value)
    {
      ShowSpectrogram = value == "spectrogram";
      ShowWaveform = value == "waveform";
    }

    partial void OnShowSpectrogramChanged(bool value)
    {
      if (value)
      {
        VisualizationMode = "spectrogram";
        ShowWaveform = false;
      }
      OnPropertyChanged(nameof(SpectrogramVisibility));
      OnPropertyChanged(nameof(WaveformVisibility));
    }

    partial void OnShowWaveformChanged(bool value)
    {
      if (value)
      {
        VisualizationMode = "waveform";
        ShowSpectrogram = false;
      }
      OnPropertyChanged(nameof(SpectrogramVisibility));
      OnPropertyChanged(nameof(WaveformVisibility));
    }

    private async Task LoadTracksForProject(string projectId, CancellationToken cancellationToken)
    {
      IsLoading = true;
      ErrorMessage = null;

      try
      {
        // Load tracks from backend
        var tracksList = await _trackService.GetTracksAsync(projectId, cancellationToken);

        Tracks.Clear();
        foreach (var track in tracksList)
        {
          Tracks.Add(track);
        }

        // Create default track if none exist
        if (Tracks.Count == 0)
        {
          var defaultTrack = await _trackService.CreateTrackAsync(projectId, "Track 1", cancellationToken: cancellationToken);
          Tracks.Add(defaultTrack);
          SelectedTrack = defaultTrack;
        }
        else
        {
          SelectedTrack = Tracks.FirstOrDefault();
        }
      }
      catch (OperationCanceledException)
      {
        // User cancelled - expected
        return;
      }
      catch (Exception ex)
      {
        ErrorMessage = ErrorHandler.GetUserFriendlyMessage(ex);
        _errorService?.ShowError(ex, ResourceHelper.GetString("Error.LoadTracksFailed", "Failed to load tracks"));
        _logService?.LogError(ex, "LoadTracksForProject");

        // Fallback to client-side track creation
        if (Tracks.Count == 0)
        {
          var defaultTrack = new AudioTrack
          {
            Id = Guid.NewGuid().ToString(),
            Name = "Track 1",
            ProjectId = projectId,
            TrackNumber = 1,
            Clips = new List<AudioClip>()
          };
          Tracks.Add(defaultTrack);
          SelectedTrack = defaultTrack;
        }
      }
      finally
      {
        IsLoading = false;
      }
    }

    private async Task AddTrackAsync(CancellationToken cancellationToken)
    {
      if (SelectedProject == null)
        return;

      IsLoading = true;
      ErrorMessage = null;

      try
      {
        var newTrackNumber = Tracks.Count > 0
            ? Tracks.Max(t => t.TrackNumber) + 1
            : 1;

        var trackName = $"Track {newTrackNumber}";
        var newTrack = await _trackService.CreateTrackAsync(SelectedProject.Id, trackName, null, cancellationToken);

        Tracks.Add(newTrack);
        SelectedTrack = newTrack;

        // Register undo action
        if (_undoRedoService != null)
        {
          var action = new AddTrackAction(
              Tracks,
              _trackService,
              newTrack,
              onUndo: (t) =>
              {
                if (SelectedTrack?.Id == t.Id)
                {
                  SelectedTrack = Tracks.FirstOrDefault();
                }
              },
              onRedo: (t) => SelectedTrack = t);
          _undoRedoService.RegisterAction(action);
        }

        // Show success toast
        _toastNotificationService?.ShowSuccess(
            ResourceHelper.FormatString("Timeline.TrackCreated", newTrack.Name),
            ResourceHelper.GetString("Toast.Title.TrackCreated", "Track Created"));
      }
      catch (OperationCanceledException)
      {
        // User cancelled - expected
        return;
      }
      catch (Exception ex)
      {
        var errorMsg = ErrorHandler.GetUserFriendlyMessage(ex);
        ErrorMessage = ResourceHelper.FormatString("Timeline.CreateTrackFailed", errorMsg);
        _errorService?.ShowError(ex, ResourceHelper.GetString("Timeline.CreateTrackFailedError", "Failed to create track"));
        _logService?.LogError(ex, "AddTrack");
        _toastNotificationService?.ShowError(
            ResourceHelper.FormatString("Timeline.CreateTrackFailed", errorMsg),
            ResourceHelper.GetString("Toast.Title.CreateTrackFailed", "Create Track Failed"));

        // Fallback to client-side track creation
        var newTrackNumber = Tracks.Count > 0
            ? Tracks.Max(t => t.TrackNumber) + 1
            : 1;

        var newTrack = new AudioTrack
        {
          Id = Guid.NewGuid().ToString(),
          Name = ResourceHelper.FormatString("Timeline.TrackName", newTrackNumber),
          ProjectId = SelectedProject.Id,
          TrackNumber = newTrackNumber,
          Clips = new List<AudioClip>()
        };

        Tracks.Add(newTrack);
        SelectedTrack = newTrack;

        // Register undo action for fallback track
        if (_undoRedoService != null)
        {
          var action = new AddTrackAction(
              Tracks,
              _trackService,
              newTrack,
              onUndo: (t) =>
              {
                if (SelectedTrack?.Id == t.Id)
                {
                  SelectedTrack = Tracks.FirstOrDefault();
                }
              },
              onRedo: (t) => SelectedTrack = t);
          _undoRedoService.RegisterAction(action);
        }
      }
      finally
      {
        IsLoading = false;
      }
    }

    private async Task AddClipToTrackAsync(CancellationToken cancellationToken)
    {
      if (SelectedTrack == null ||
          string.IsNullOrWhiteSpace(LastSynthesizedAudioId) ||
          string.IsNullOrWhiteSpace(LastSynthesizedAudioUrl) ||
          !LastSynthesizedDuration.HasValue)
        return;

      try
      {
        // Calculate start time (end of last clip or 0)
        var startTime = SelectedTrack.Clips.Count > 0
            ? SelectedTrack.Clips.Max(c => c.EndTime)
            : 0.0;

        // Get profile name for clip name
        var profile = AvailableProfiles.FirstOrDefault(p => p.Id == SelectedProfileId);
        var clipName = profile != null
            ? $"{profile.Name}: {SynthesisText.Substring(0, Math.Min(30, SynthesisText.Length))}..."
            : $"Clip {SelectedTrack.Clips.Count + 1}";

        var newClip = new AudioClip
        {
          Id = Guid.NewGuid().ToString(),
          Name = clipName,
          ProfileId = SelectedProfileId ?? string.Empty,
          AudioId = LastSynthesizedAudioId,
          AudioUrl = LastSynthesizedAudioUrl,
          Duration = TimeSpan.FromSeconds(LastSynthesizedDuration.Value),
          StartTime = startTime,
          Engine = SelectedEngine,
          QualityScore = LastQualityScore
        };

        // Load waveform data for the clip (async, non-blocking)
        var ct = new CancellationTokenSource(TimeSpan.FromSeconds(30)).Token;
        _ = LoadClipWaveformAsync(newClip, ct).ContinueWith(t =>
        {
          if (t.IsFaulted)
            _logService?.LogError(t.Exception?.InnerException ?? new Exception("LoadClipWaveform failed"), "LoadClipWaveform");
        }, TaskScheduler.Default);

        // Save clip to backend
        try
        {
          // Use the saved clip (with backend-assigned ID if different)
          newClip = await _clipService.CreateClipAsync(
              SelectedProject!.Id,
              SelectedTrack.Id,
              newClip,
              cancellationToken
          );
        }
        catch (Exception ex)
        {
          // Log error but continue with client-side clip
          var errorMsg = ErrorHandler.GetUserFriendlyMessage(ex);
          ErrorMessage = ResourceHelper.FormatString("Project.ClipSaveWarning", errorMsg);
          _logService?.LogError(ex, "CreateClip");
        }

        SelectedTrack.Clips.Add(newClip);
        NotifyTotalDurationChanged();

        // Register undo action
        if (_undoRedoService != null)
        {
          var action = new AddClipAction(
              Tracks,
              SelectedTrack,
              newClip);
          _undoRedoService.RegisterAction(action);
        }

        // Save audio to project directory for persistence
        try
        {
          if (SelectedProject == null)
            return;

          var savedFile = await _projectAudioClient.SaveAudioToProjectAsync(
              SelectedProject.Id,
              LastSynthesizedAudioId,
              $"{newClip.Id}.wav",
              cancellationToken
          );
          // Update clip with saved URL and filename (for visualization lookup)
          newClip.AudioUrl = savedFile.Url;
          // Use filename as AudioId for project audio files (backend can find by filename)
          newClip.AudioId = savedFile.Filename;
          // Refresh project audio files list
          var loadCt = new CancellationTokenSource(TimeSpan.FromSeconds(30)).Token;
          _ = LoadProjectAudioAsync(loadCt).ContinueWith(t =>
          {
            if (t.IsFaulted)
              _logService?.LogError(t.Exception?.InnerException ?? new Exception("LoadProjectAudio failed"), "LoadProjectAudio");
          }, TaskScheduler.Default);
        }
        catch (Exception saveEx)
        {
          // Log but don't fail - clip is still added
          var errorMsg = ErrorHandler.GetUserFriendlyMessage(saveEx);
          ErrorMessage = ResourceHelper.FormatString("Project.AudioSaveWarning", errorMsg);
          _logService?.LogError(saveEx, "SaveAudioToProject");
        }

        // Clear synthesis result to allow new synthesis
        LastSynthesizedAudioId = null;
        LastSynthesizedAudioUrl = null;
        LastSynthesizedDuration = null;
        CanPlayAudio = false;
        AddClipToTrackCommand.NotifyCanExecuteChanged();
        PlayAudioCommand.NotifyCanExecuteChanged();

        _toastNotificationService?.ShowSuccess(
            ResourceHelper.FormatString("Timeline.ClipAdded", newClip.Name),
            ResourceHelper.GetString("Toast.Title.ClipsDeleted", "Clip Added"));
      }
      catch (OperationCanceledException)
      {
        // User cancelled - expected
        return;
      }
      catch (Exception ex)
      {
        ErrorMessage = ErrorHandler.GetUserFriendlyMessage(ex);
        _errorService?.ShowError(ex, ResourceHelper.GetString("Error.AddClipFailed", "Failed to add clip to track"));
        _logService?.LogError(ex, "AddClipToTrack");
      }
    }

    private async Task LoadAudioFileIntoClipAsync(ProjectAudioFile? audioFile, CancellationToken cancellationToken)
    {
      if (audioFile == null || SelectedTrack == null || SelectedProject == null)
        return;

      try
      {
        // Ensure we have an audio ID to work with
        if (string.IsNullOrWhiteSpace(audioFile.AudioId))
          return;

        // Fetch waveform data to get duration and prep visuals
        var waveform = await _audioVisualizationService.GetWaveformDataAsync(audioFile.AudioId, cancellationToken: cancellationToken);

        LastSynthesizedAudioId = audioFile.AudioId;
        LastSynthesizedAudioUrl = audioFile.Url;
        LastSynthesizedDuration = waveform.Duration;

        // Trigger visualization load (non-blocking)
        var vizToken = new CancellationTokenSource(TimeSpan.FromSeconds(15)).Token;
        _ = LoadVisualizationDataAsync(audioFile.AudioId, vizToken).ContinueWith(t =>
        {
          if (t.IsFaulted)
            _logService?.LogError(t.Exception?.InnerException ?? new Exception("LoadVisualizationData failed"), "LoadVisualizationData");
        }, TaskScheduler.Default);

        // Enable add-to-track and play
        SelectedAudioFile = audioFile;
        CanPlayAudio = true;
        AddClipToTrackCommand.NotifyCanExecuteChanged();
        PlayAudioCommand.NotifyCanExecuteChanged();
      }
      catch (Exception ex)
      {
        ErrorLogger.LogWarning($"Best effort operation failed: {ex.Message}", "TimelineViewModel.LoadAudioFileIntoClipAsync");
        ErrorMessage = ErrorHandler.GetUserFriendlyMessage(ex);
        _errorService?.ShowError(ex, ResourceHelper.GetString("Timeline.LoadAudioIntoClipFailed", "Failed to load audio into clip"));
        _logService?.LogError(ex, "LoadAudioFileIntoClip");
      }
    }

    private async Task LoadProjectAudioAsync(CancellationToken cancellationToken)
    {
      if (SelectedProject == null)
        return;

      IsLoading = true;
      ErrorMessage = null;

      try
      {
        var audioFiles = await _projectAudioClient.ListProjectAudioAsync(SelectedProject.Id, cancellationToken);

        ProjectAudioFiles.Clear();
        foreach (var file in audioFiles)
        {
          ProjectAudioFiles.Add(file);
        }
      }
      catch (OperationCanceledException)
      {
        // User cancelled - expected
        return;
      }
      catch (Exception ex)
      {
        ErrorMessage = ErrorHandler.GetUserFriendlyMessage(ex);
        _errorService?.ShowError(ex, ResourceHelper.GetString("Timeline.LoadProjectAudioFailed", "Failed to load project audio files"));
        _logService?.LogError(ex, "LoadProjectAudio");
      }
      finally
      {
        IsLoading = false;
      }
    }

    private async Task PlayProjectAudioAsync(string? filename, CancellationToken cancellationToken)
    {
      if (SelectedProject == null || string.IsNullOrWhiteSpace(filename))
        return;

      IsLoading = true;
      ErrorMessage = null;

      try
      {
        // Stop any currently playing audio
        if (_audioPlayer.IsPlaying)
        {
          _audioPlayer.Stop();
        }

        // Get audio stream from backend
        await using var audioStream = await _projectAudioClient.GetProjectAudioAsync(SelectedProject.Id, filename, cancellationToken);

        if (audioStream != null)
        {
          // Play the audio stream (WAV format, typically 22050 Hz, mono)
          // AudioPlayerService will copy the stream internally, so we can dispose the original
          await _audioPlayer.PlayStreamAsync(audioStream, sampleRate: 22050, channels: 1, onPlaybackComplete: () =>
          {
            IsPlaying = false;
            PlayProjectAudioCommand.NotifyCanExecuteChanged();
          });
          IsPlaying = true;
          PlayProjectAudioCommand.NotifyCanExecuteChanged();

          // Own global transport so main Play routes here
          var ctx = AppServices.TryGetContextManager();
          if (ctx != null)
          {
            var audioId = SelectedProject != null ? $"{SelectedProject.Id}:{filename}" : filename ?? "timeline";
            var title = SelectedProject != null ? $"Timeline: {SelectedProject.Name}" : "Timeline";
            ctx.SetCurrentPlayable(audioId, TransportSource.Timeline, title);
          }

          // Load visualization data for the audio file
          var ct = new CancellationTokenSource(TimeSpan.FromSeconds(30)).Token;
          _ = LoadVisualizationDataAsync(filename, ct).ContinueWith(t =>
          {
            if (t.IsFaulted)
              _logService?.LogError(t.Exception?.InnerException ?? new Exception("LoadVisualizationData failed"), "LoadVisualizationData");
          }, TaskScheduler.Default);
        }
      }
      catch (OperationCanceledException)
      {
        // User cancelled - expected
        return;
      }
      catch (Exception ex)
      {
        ErrorMessage = ErrorHandler.GetUserFriendlyMessage(ex);
        _errorService?.ShowError(ex, ResourceHelper.GetString("Timeline.PlayAudioFileFailed", "Failed to play audio file"));
        _logService?.LogError(ex, "PlayProjectAudio");
      }
      finally
      {
        IsLoading = false;
      }
    }

    private async Task LoadVisualizationDataAsync(string? audioIdOrFilename, CancellationToken cancellationToken)
    {
      if (string.IsNullOrWhiteSpace(audioIdOrFilename))
        return;

      try
      {
        // Load waveform data
        if (ShowWaveform)
        {
          var waveformData = await _audioVisualizationService.GetWaveformDataAsync(audioIdOrFilename, width: 1024, mode: "peak", cancellationToken);
          if (waveformData?.Samples != null)
          {
            WaveformSamples = waveformData.Samples;
          }
        }

        // Load spectrogram data
        if (ShowSpectrogram)
        {
          var spectrogramData = await _audioVisualizationService.GetSpectrogramDataAsync(audioIdOrFilename, width: 512, height: 256, cancellationToken);
          if (spectrogramData?.Frames != null)
          {
            // Convert Core.Models.SpectrogramFrame to Controls.SpectrogramFrame
            SpectrogramFrames.Clear();
            foreach (var frame in spectrogramData.Frames)
            {
              SpectrogramFrames.Add(new Controls.SpectrogramFrame
              {
                Time = frame.Time,
                Frequencies = frame.Frequencies
              });
            }
          }
        }
      }
      catch (OperationCanceledException)
      {
        // User cancelled - expected
        return;
      }
      catch (Exception ex)
      {
        // Log but don't fail - visualization is optional
        _logService?.LogError(ex, "LoadVisualizationData");
        System.Diagnostics.Debug.WriteLine($"Failed to load visualization data: {ex.Message}");
      }
    }

    private async Task LoadClipWaveformAsync(AudioClip clip, CancellationToken cancellationToken)
    {
      if (clip == null || string.IsNullOrWhiteSpace(clip.AudioId))
        return;

      try
      {
        var waveformData = await _audioVisualizationService.GetWaveformDataAsync(clip.AudioId, width: 512, mode: "peak", cancellationToken);
        if (waveformData?.Samples != null)
        {
          clip.WaveformSamples = waveformData.Samples;
        }
      }
      catch (OperationCanceledException)
      {
        // User cancelled - expected
        return;
      }
      catch (Exception ex)
      {
        // Log but don't show error - waveform will show empty state
        _logService?.LogError(ex, "LoadClipWaveform");
        System.Diagnostics.Debug.WriteLine($"Failed to load waveform for clip {clip.Id}: {ex.Message}");
      }
    }

    private void ZoomIn()
    {
      TimelineZoom = Math.Min(10.0, TimelineZoom * 1.2);
    }

    private void ZoomOut()
    {
      TimelineZoom = Math.Max(0.1, TimelineZoom / 1.2);
    }

    private void SeekToPosition(double pixelPosition)
    {
      // Convert pixel position to time in seconds
      // pixels = seconds * PIXELS_PER_SECOND * zoom
      // seconds = pixels / (PIXELS_PER_SECOND * zoom)
      var timeInSeconds = pixelPosition / (PIXELS_PER_SECOND * TimelineZoom);

      // Clamp to valid range (0 to duration if available)
      if (_audioPlayer.Duration > 0)
      {
        timeInSeconds = Math.Max(0, Math.Min(timeInSeconds, _audioPlayer.Duration));
      }
      else
      {
        timeInSeconds = Math.Max(0, timeInSeconds);
      }

      // Seek to the calculated position
      _audioPlayer.Seek(timeInSeconds);
      CurrentPlaybackPosition = timeInSeconds;

      // Play audio preview if enabled and audio file is available
      if (_previewEnabled && !string.IsNullOrWhiteSpace(_currentAudioFilePath) && System.IO.File.Exists(_currentAudioFilePath))
      {
        // Stop any existing preview
        if (_audioPlayer is AudioPlayerService audioPlayerService)
        {
          audioPlayerService.StopPreview();

          // Start new preview
          IsPreviewing = true;
          _ = audioPlayerService.PlayPreviewSnippetAsync(
              _currentAudioFilePath,
              timeInSeconds,
              _previewDuration,
              _previewVolume,
              () => IsPreviewing = false
          );
        }
      }
    }

    public string ZoomLevelDisplay => $"Zoom: {TimelineZoom:F1}x";

    // Multi-select methods for clips
    public void ToggleClipSelection(string clipId, bool isCtrlPressed, bool isShiftPressed)
    {
      if (_multiSelectState == null)
        return;

      if (isShiftPressed && !string.IsNullOrEmpty(_multiSelectState.RangeAnchorId))
      {
        // Range selection
        var allClipIds = GetAllClips().Select(c => c.Id).ToList();
        _multiSelectState.SetRange(_multiSelectState.RangeAnchorId, clipId, allClipIds);
      }
      else if (isCtrlPressed)
      {
        // Toggle selection
        _multiSelectState.Toggle(clipId);
      }
      else
      {
        // Single selection (clear others)
        _multiSelectState.SetSingle(clipId);
      }

      UpdateClipSelectionProperties();
      _multiSelectService.OnSelectionChanged(PanelId, _multiSelectState);
    }

    private void SelectAllClips()
    {
      if (_multiSelectState == null)
        return;

      _multiSelectState.Clear();
      var allClips = GetAllClips().ToList();
      foreach (var clip in allClips)
      {
        _multiSelectState.Add(clip.Id);
      }
      if (allClips.Count > 0)
      {
        _multiSelectState.RangeAnchorId = allClips[0].Id;
      }

      UpdateClipSelectionProperties();
      _multiSelectService.OnSelectionChanged(PanelId, _multiSelectState);
      SelectAllClipsCommand.NotifyCanExecuteChanged();
    }

    private void ClearClipSelection()
    {
      if (_multiSelectState == null)
        return;

      _multiSelectState.Clear();
      UpdateClipSelectionProperties();
      _multiSelectService.OnSelectionChanged(PanelId, _multiSelectState);
      DeleteSelectedClipsCommand.NotifyCanExecuteChanged();
    }

    private async Task DeleteSelectedClipsAsync(CancellationToken cancellationToken)
    {
      if (_multiSelectState == null || _multiSelectState.SelectedIds.Count == 0 || SelectedProject == null)
        return;

      var selectedIds = new List<string>(_multiSelectState.SelectedIds);

      // Show confirmation dialog (Panel Hardening: IDialogService per PANEL_HARDENING_PATTERN)
      var confirmed = await _dialogService.ShowConfirmationAsync(
          "Delete clips?",
          $"Are you sure you want to delete '{selectedIds.Count} clip(s)'? This action cannot be undone.",
          "Delete",
          "Cancel");

      if (!confirmed)
        return;

      cancellationToken.ThrowIfCancellationRequested();

      IsLoading = true;
      ErrorMessage = null;

      try
      {
        // Capture clips before deletion for undo
        var clipsToDelete = Tracks
            .SelectMany(track => track.Clips?.Where(c => selectedIds.Contains(c.Id)) ?? Enumerable.Empty<AudioClip>())
            .Select(clip =>
            {
              var track = Tracks.FirstOrDefault(t => t.Clips?.Any(c => c.Id == clip.Id) == true);
              return (track!, clip);
            })
            .Where(x => x.Item1 != null)
            .ToList();

        int deletedCount = 0;
        // Delete clips from tracks
        foreach (var track in Tracks.ToList())
        {
          foreach (var clip in track.Clips?.Where(c => selectedIds.Contains(c.Id)).ToList() ?? new List<AudioClip>())
          {
            cancellationToken.ThrowIfCancellationRequested();

            // Remove from backend if possible
            try
            {
              await _clipService.DeleteClipAsync(SelectedProject.Id, track.Id, clip.Id, cancellationToken);
            }
            catch (Exception ex)
      {
        ErrorLogger.LogWarning($"Best effort operation failed: {ex.Message}", "TimelineViewModel.DeleteSelectedClipsAsync");
      }

            // Remove from track
            track.Clips?.Remove(clip);
            deletedCount++;
          }
        }

        // Register batch undo action if any clips were deleted
        if (deletedCount > 0 && _undoRedoService != null && clipsToDelete.Count > 0)
        {
          var action = new DeleteClipsAction(
              Tracks,
              clipsToDelete);
          _undoRedoService.RegisterAction(action);
        }

        // Clear selection after deletion
        ClearClipSelection();

        // Show success toast
        if (deletedCount > 0)
        {
          _toastNotificationService?.ShowSuccess(
              ResourceHelper.FormatString("Project.ClipsDeleted", deletedCount),
              ResourceHelper.GetString("Toast.Title.ClipsDeleted", "Clips Deleted"));
        }
        if (deletedCount < selectedIds.Count)
        {
          _toastNotificationService?.ShowWarning(
              ResourceHelper.FormatString("Project.ClipsDeletePartial", deletedCount, selectedIds.Count),
              ResourceHelper.GetString("Toast.Title.PartialDelete", "Partial Delete"));
        }
      }
      catch (OperationCanceledException)
      {
        // User cancelled - expected
        return;
      }
      catch (Exception ex)
      {
        var errorMsg = ErrorHandler.GetUserFriendlyMessage(ex);
        ErrorMessage = ResourceHelper.FormatString("Error.DeleteClipsFailed", errorMsg);
        _errorService?.ShowError(ex, ResourceHelper.GetString("Error.DeleteClipsFailed", "Failed to delete clips"));
        _logService?.LogError(ex, "DeleteSelectedClips");
        _toastNotificationService?.ShowError(
            ResourceHelper.FormatString("Error.DeleteClipsFailed", errorMsg),
            ResourceHelper.GetString("Toast.Title.DeleteClipsFailed", "Delete Clips Failed"));
      }
      finally
      {
        IsLoading = false;
      }
    }

    private void UpdateClipSelectionProperties()
    {
      if (_multiSelectState == null)
      {
        SelectedClipCount = 0;
        HasMultipleClipSelection = false;
      }
      else
      {
        SelectedClipCount = _multiSelectState.Count;
        HasMultipleClipSelection = _multiSelectState.IsMultipleSelection;
      }

      OnPropertyChanged(nameof(SelectedClipCount));
      OnPropertyChanged(nameof(HasMultipleClipSelection));
      DeleteSelectedClipsCommand.NotifyCanExecuteChanged();
    }
  }
}