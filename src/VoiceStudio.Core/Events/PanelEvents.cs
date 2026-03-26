using System;
using System.Collections.Generic;

namespace VoiceStudio.Core.Events
{
  /// <summary>
  /// Backend-Frontend Integration Plan - Phase 4: Cross-panel synchronization
  /// Event messages for cross-panel communication via EventAggregator.
  /// </summary>

  /// <summary>
  /// Base class for all panel-related events.
  /// </summary>
  public abstract class PanelEventBase
  {
    /// <summary>
    /// Gets the source panel ID that published this event.
    /// </summary>
    public string SourcePanelId { get; }

    /// <summary>
    /// Gets the timestamp when the event was created.
    /// </summary>
    public DateTime Timestamp { get; }

    /// <summary>
    /// Gets the user intent associated with this event.
    /// Panels can respond differently based on intent (e.g., Preview vs Edit).
    /// Default: Navigation (standard selection/navigation behavior).
    /// </summary>
    public InteractionIntent Intent { get; }

    /// <summary>
    /// Gets the monotonic sequence number assigned by the event bus.
    /// Used for ordering and deduplication. Set by EventAggregator.
    /// </summary>
    public long Sequence { get; internal set; }

    protected PanelEventBase(string sourcePanelId, InteractionIntent intent = InteractionIntent.Navigation)
    {
      SourcePanelId = sourcePanelId;
      Timestamp = DateTime.UtcNow;
      Intent = intent;
    }
  }

  #region Profile Events

  /// <summary>
  /// Event published when a voice profile is selected.
  /// Subscribers: SettingsPanel, TimelinePanel, BatchProcessingPanel
  /// </summary>
  public class ProfileSelectedEvent : PanelEventBase
  {
    public string ProfileId { get; }
    public string? ProfileName { get; }

    public ProfileSelectedEvent(
      string sourcePanelId,
      string profileId,
      string? profileName = null,
      InteractionIntent intent = InteractionIntent.Navigation)
      : base(sourcePanelId, intent)
    {
      ProfileId = profileId;
      ProfileName = profileName;
    }
  }

  /// <summary>
  /// Event published when a voice profile is updated (settings changed).
  /// Subscribers: Any panel displaying profile info
  /// </summary>
  public class ProfileUpdatedEvent : PanelEventBase
  {
    public string ProfileId { get; }
    public Dictionary<string, object>? ChangedProperties { get; }

    public ProfileUpdatedEvent(string sourcePanelId, string profileId, Dictionary<string, object>? changedProperties = null)
      : base(sourcePanelId)
    {
      ProfileId = profileId;
      ChangedProperties = changedProperties;
    }
  }

  /// <summary>
  /// Event published when a new profile is created.
  /// </summary>
  public class ProfileCreatedEvent : PanelEventBase
  {
    public string ProfileId { get; }
    public string ProfileName { get; }

    public ProfileCreatedEvent(string sourcePanelId, string profileId, string profileName)
      : base(sourcePanelId)
    {
      ProfileId = profileId;
      ProfileName = profileName;
    }
  }

  /// <summary>
  /// Event published when a profile is deleted.
  /// </summary>
  public class ProfileDeletedEvent : PanelEventBase
  {
    public string ProfileId { get; }

    public ProfileDeletedEvent(string sourcePanelId, string profileId)
      : base(sourcePanelId)
    {
      ProfileId = profileId;
    }
  }

  #endregion

  #region Asset Events

  /// <summary>
  /// Event published when an asset is selected in the library.
  /// Subscribers: Timeline, PropertyEditor, PreviewPanel
  /// </summary>
  public class AssetSelectedEvent : PanelEventBase
  {
    public string AssetId { get; }
    public string AssetType { get; } // "audio", "voice_profile", "project", etc.
    public string? AssetName { get; }

    public AssetSelectedEvent(
      string sourcePanelId,
      string assetId,
      string assetType,
      string? assetName = null,
      InteractionIntent intent = InteractionIntent.Navigation)
      : base(sourcePanelId, intent)
    {
      AssetId = assetId;
      AssetType = assetType;
      AssetName = assetName;
    }
  }

  /// <summary>
  /// Event published when an asset is added to the library.
  /// </summary>
  public class AssetAddedEvent : PanelEventBase
  {
    public string AssetId { get; }
    public string AssetType { get; }
    public string? AssetPath { get; }

    public AssetAddedEvent(string sourcePanelId, string assetId, string assetType, string? assetPath = null)
      : base(sourcePanelId)
    {
      AssetId = assetId;
      AssetType = assetType;
      AssetPath = assetPath;
    }
  }

  /// <summary>
  /// Event published when an asset is removed from the library.
  /// </summary>
  public class AssetRemovedEvent : PanelEventBase
  {
    public string AssetId { get; }

    public AssetRemovedEvent(string sourcePanelId, string assetId)
      : base(sourcePanelId)
    {
      AssetId = assetId;
    }
  }

  #endregion

  #region Project Events

  /// <summary>
  /// Event published when the active project changes.
  /// Subscribers: All panels that display project-specific data
  /// </summary>
  public class ProjectChangedEvent : PanelEventBase
  {
    public string? ProjectId { get; }
    public string? ProjectName { get; }
    public bool IsNew { get; }

    public ProjectChangedEvent(string sourcePanelId, string? projectId, string? projectName = null, bool isNew = false)
      : base(sourcePanelId)
    {
      ProjectId = projectId;
      ProjectName = projectName;
      IsNew = isNew;
    }
  }

  /// <summary>
  /// Event published when project settings are modified.
  /// </summary>
  public class ProjectSettingsChangedEvent : PanelEventBase
  {
    public string ProjectId { get; }
    public Dictionary<string, object>? ChangedSettings { get; }

    public ProjectSettingsChangedEvent(string sourcePanelId, string projectId, Dictionary<string, object>? changedSettings = null)
      : base(sourcePanelId)
    {
      ProjectId = projectId;
      ChangedSettings = changedSettings;
    }
  }

  /// <summary>
  /// Published after a backup restore API succeeds so panels can reload on-disk-backed state (Pass 06).
  /// </summary>
  public sealed class BackupRestoredEvent : PanelEventBase
  {
    public bool RestoreProjects { get; }
    public bool RestoreProfiles { get; }
    public bool RestoreSettings { get; }
    public bool RestoreModels { get; }

    public BackupRestoredEvent(
      string sourcePanelId,
      bool restoreProjects,
      bool restoreProfiles,
      bool restoreSettings,
      bool restoreModels)
      : base(sourcePanelId)
    {
      RestoreProjects = restoreProjects;
      RestoreProfiles = restoreProfiles;
      RestoreSettings = restoreSettings;
      RestoreModels = restoreModels;
    }
  }

  #endregion

  #region Job Events

  /// <summary>
  /// Event published when a batch job starts.
  /// Subscribers: StatusBar, NotificationCenter
  /// </summary>
  public class JobStartedEvent : PanelEventBase
  {
    public string JobId { get; }
    public string JobType { get; } // "batch", "training", "synthesis"
    public string? JobName { get; }

    public JobStartedEvent(string sourcePanelId, string jobId, string jobType, string? jobName = null)
      : base(sourcePanelId)
    {
      JobId = jobId;
      JobType = jobType;
      JobName = jobName;
    }
  }

  /// <summary>
  /// Event published when a job reports progress.
  /// Enables cross-panel progress propagation with sequence ordering and intent.
  /// Subscribers: StatusBar, NotificationCenter, BatchProcessingPanel, DiagnosticsPanel
  /// </summary>
  public class JobProgressEvent : PanelEventBase
  {
    /// <summary>
    /// Gets the unique identifier of the job reporting progress.
    /// </summary>
    public string JobId { get; }

    /// <summary>
    /// Gets the progress value between 0.0 and 1.0 (0% to 100%).
    /// </summary>
    public double Progress { get; }

    /// <summary>
    /// Gets an optional human-readable status message describing current work.
    /// </summary>
    public string? StatusMessage { get; }

    /// <summary>
    /// Gets the estimated remaining time in seconds, if available.
    /// A negative value indicates the estimate is not available.
    /// </summary>
    public double EstimatedRemainingSeconds { get; }

    /// <summary>
    /// Gets the job type for filtering (e.g., "batch", "training", "synthesis").
    /// </summary>
    public string? JobType { get; }

    public JobProgressEvent(
      string sourcePanelId,
      string jobId,
      double progress,
      string? statusMessage = null,
      double estimatedRemainingSeconds = -1,
      string? jobType = null,
      InteractionIntent intent = InteractionIntent.BackgroundProcess)
      : base(sourcePanelId, intent)
    {
      JobId = jobId;
      Progress = Math.Clamp(progress, 0.0, 1.0);
      StatusMessage = statusMessage;
      EstimatedRemainingSeconds = estimatedRemainingSeconds;
      JobType = jobType;
    }
  }

  // Note: JobCompletedEvent is defined in JobCompletedEvent.cs
  // Use that existing event instead of duplicating here.

  #endregion

  #region Timeline Events

  /// <summary>
  /// Event published when timeline playback state changes.
  /// Subscribers: TransportControls, AudioMeter, Waveform
  /// </summary>
  public class PlaybackStateChangedEvent : PanelEventBase
  {
    public bool IsPlaying { get; }
    public double CurrentTime { get; }

    public PlaybackStateChangedEvent(string sourcePanelId, bool isPlaying, double currentTime)
      : base(sourcePanelId)
    {
      IsPlaying = isPlaying;
      CurrentTime = currentTime;
    }
  }

  /// <summary>
  /// Event published when the timeline selection changes.
  /// </summary>
  public class TimelineSelectionChangedEvent : PanelEventBase
  {
    public double SelectionStart { get; }
    public double SelectionEnd { get; }
    public List<string> SelectedClipIds { get; }

    public TimelineSelectionChangedEvent(string sourcePanelId, double selectionStart, double selectionEnd, List<string>? selectedClipIds = null)
      : base(sourcePanelId)
    {
      SelectionStart = selectionStart;
      SelectionEnd = selectionEnd;
      SelectedClipIds = selectedClipIds ?? new List<string>();
    }
  }

  #endregion

  #region Engine Events

  /// <summary>
  /// Event published when the active synthesis engine changes.
  /// Subscribers: SettingsPanel, BatchProcessing, Training
  /// </summary>
  public class EngineChangedEvent : PanelEventBase
  {
    public string EngineId { get; }
    public string? EngineName { get; }

    public EngineChangedEvent(string sourcePanelId, string engineId, string? engineName = null)
      : base(sourcePanelId)
    {
      EngineId = engineId;
      EngineName = engineName;
    }
  }

  /// <summary>
  /// Event published when engine settings are modified.
  /// </summary>
  public class EngineSettingsChangedEvent : PanelEventBase
  {
    public string EngineId { get; }
    public Dictionary<string, object>? ChangedSettings { get; }

    public EngineSettingsChangedEvent(string sourcePanelId, string engineId, Dictionary<string, object>? changedSettings = null)
      : base(sourcePanelId)
    {
      EngineId = engineId;
      ChangedSettings = changedSettings;
    }
  }

  #endregion

  #region Context Action Events (Audit remediation C.2)

  /// <summary>
  /// Event published when an audio asset is selected as a clone reference.
  /// Subscribers: VoiceQuickClone, VoiceCloningWizard
  /// </summary>
  public class CloneReferenceSelectedEvent : PanelEventBase
  {
    public string AssetId { get; }
    public string AssetPath { get; }
    public string? AssetName { get; }

    public CloneReferenceSelectedEvent(
      string sourcePanelId,
      string assetId,
      string assetPath,
      string? assetName = null,
      InteractionIntent intent = InteractionIntent.ImmediateUse)
      : base(sourcePanelId, intent)
    {
      AssetId = assetId;
      AssetPath = assetPath;
      AssetName = assetName;
    }
  }

  /// <summary>
  /// Event published when a voice profile is selected for synthesis.
  /// Subscribers: SynthesisPanel, BatchProcessing
  /// </summary>
  public class VoiceProfileSelectedEvent : PanelEventBase
  {
    public string ProfileId { get; }
    public string? ProfileName { get; }

    public VoiceProfileSelectedEvent(string sourcePanelId, string profileId, string? profileName = null)
      : base(sourcePanelId)
    {
      ProfileId = profileId;
      ProfileName = profileName;
    }
  }

  /// <summary>
  /// Event published when audio playback is requested.
  /// Subscribers: AudioPlayer, Timeline
  /// </summary>
  public class PlaybackRequestedEvent : PanelEventBase
  {
    public string AssetId { get; }
    public string AssetPath { get; }
    public string? AssetName { get; }

    public PlaybackRequestedEvent(string sourcePanelId, string assetId, string assetPath, string? assetName = null)
      : base(sourcePanelId)
    {
      AssetId = assetId;
      AssetPath = assetPath;
      AssetName = assetName;
    }
  }

  #endregion

  #region Synthesis and Timeline Events (Audit remediation C.3)

  /// <summary>
  /// Event published when synthesis completes successfully.
  /// Subscribers: Timeline (for auto-add), Library (for refresh)
  /// </summary>
  public class SynthesisCompletedEvent : PanelEventBase
  {
    public string AudioId { get; }
    public string AudioPath { get; }
    public string? Text { get; }
    public TimeSpan Duration { get; }
    public string? VoiceName { get; }
    public string? EngineName { get; }
    /// <summary>Voice profile ID for timeline clip creation.</summary>
    public string? ProfileId { get; }

    public SynthesisCompletedEvent(
      string sourcePanelId,
      string audioId,
      string audioPath,
      TimeSpan duration,
      string? text = null,
      string? voiceName = null,
      string? engineName = null,
      string? profileId = null,
      InteractionIntent intent = InteractionIntent.BackgroundProcess)
      : base(sourcePanelId, intent)
    {
      AudioId = audioId;
      AudioPath = audioPath;
      Duration = duration;
      Text = text;
      VoiceName = voiceName;
      EngineName = engineName;
      ProfileId = profileId;
    }
  }

  /// <summary>
  /// Event published to request adding an audio clip to the timeline.
  /// Subscribers: TimelineViewModel
  /// </summary>
  public class AddToTimelineEvent : PanelEventBase
  {
    public string AudioId { get; }
    public string AudioPath { get; }
    public string? ClipName { get; }
    public TimeSpan Duration { get; }
    public int? TargetTrackIndex { get; }
    public TimeSpan? InsertPosition { get; }
    /// <summary>Voice profile ID for clip creation. Backend requires non-empty.</summary>
    public string? ProfileId { get; }

    public AddToTimelineEvent(
      string sourcePanelId,
      string audioId,
      string audioPath,
      TimeSpan duration,
      string? clipName = null,
      int? targetTrackIndex = null,
      TimeSpan? insertPosition = null,
      string? profileId = null)
      : base(sourcePanelId)
    {
      AudioId = audioId;
      AudioPath = audioPath;
      Duration = duration;
      ClipName = clipName;
      TargetTrackIndex = targetTrackIndex;
      InsertPosition = insertPosition;
      ProfileId = profileId;
    }
  }

  #endregion

  #region Navigation Events

  /// <summary>
  /// Event published to request navigation to a specific panel or view.
  /// </summary>
  public class NavigateToEvent : PanelEventBase
  {
    public string TargetPanelId { get; }
    public Dictionary<string, object>? Parameters { get; }

    public NavigateToEvent(string sourcePanelId, string targetPanelId, Dictionary<string, object>? parameters = null)
      : base(sourcePanelId)
    {
      TargetPanelId = targetPanelId;
      Parameters = parameters;
    }
  }

  #endregion

  #region Workspace Events

  /// <summary>
  /// Event published when the active workspace changes.
  /// Enables cross-panel notification of workspace/layout transitions via EventAggregator.
  /// Subscribers: PanelHost, LayoutService, any panel needing workspace context
  /// </summary>
  public class WorkspaceChangedEvent : PanelEventBase
  {
    /// <summary>
    /// Gets the ID of the workspace being activated.
    /// </summary>
    public string WorkspaceId { get; }

    /// <summary>
    /// Gets the name of the workspace being activated.
    /// </summary>
    public string WorkspaceName { get; }

    /// <summary>
    /// Gets the ID of the previous workspace (null if first activation).
    /// </summary>
    public string? PreviousWorkspaceId { get; }

    /// <summary>
    /// Gets whether this was a user-initiated switch vs system restore.
    /// </summary>
    public bool WasUserInitiated { get; }

    public WorkspaceChangedEvent(
      string sourcePanelId,
      string workspaceId,
      string workspaceName,
      string? previousWorkspaceId = null,
      bool wasUserInitiated = true,
      InteractionIntent intent = InteractionIntent.Navigation)
      : base(sourcePanelId, intent)
    {
      WorkspaceId = workspaceId;
      WorkspaceName = workspaceName;
      PreviousWorkspaceId = previousWorkspaceId;
      WasUserInitiated = wasUserInitiated;
    }
  }

  #endregion

  #region Transcription Events (Audit remediation C.4)

  /// <summary>
  /// Represents a single subtitle segment for timeline display.
  /// </summary>
  public class SubtitleSegment
  {
    public double StartTime { get; }
    public double EndTime { get; }
    public string Text { get; }

    public SubtitleSegment(double startTime, double endTime, string text)
    {
      StartTime = startTime;
      EndTime = endTime;
      Text = text;
    }
  }

  /// <summary>
  /// Event published when transcription completes with segments for timeline display.
  /// Subscribers: Timeline (to display subtitle track)
  /// </summary>
  public class TranscriptionCompletedEvent : PanelEventBase
  {
    public string AudioId { get; }
    public string TranscriptionId { get; }
    public string FullText { get; }
    public IReadOnlyList<SubtitleSegment> Segments { get; }
    public TimeSpan Duration { get; }
    public string Language { get; }

    public TranscriptionCompletedEvent(
      string sourcePanelId,
      string audioId,
      string transcriptionId,
      string fullText,
      IReadOnlyList<SubtitleSegment> segments,
      TimeSpan duration,
      string language = "en")
      : base(sourcePanelId)
    {
      AudioId = audioId;
      TranscriptionId = transcriptionId;
      FullText = fullText;
      Segments = segments;
      Duration = duration;
      Language = language;
    }
  }

  #endregion

  #region Navigation Events (Panel Workflow Integration)

  /// <summary>
  /// Event published to request navigation to a specific panel.
  /// Subscribers: PanelHost, MainWindow, NavigationService
  /// </summary>
  public class PanelNavigationRequestEvent : PanelEventBase
  {
    public string TargetPanelId { get; }
    public Dictionary<string, object>? Parameters { get; }

    public PanelNavigationRequestEvent(
      string sourcePanelId,
      string targetPanelId,
      Dictionary<string, object>? parameters = null)
      : base(sourcePanelId)
    {
      TargetPanelId = targetPanelId;
      Parameters = parameters;
    }
  }

  #endregion
}
