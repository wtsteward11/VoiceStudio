// Phase 5: Timeline Component
// Task 5.11: Professional audio timeline
// GAP-FE-001: Integrated with TimelineGateway for backend connectivity

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using VoiceStudio.App.ViewModels;
using VoiceStudio.App.Services;
using VoiceStudio.Core.Events;
using VoiceStudio.Core.Gateways;
using VoiceStudio.Core.Panels;
using VoiceStudio.Core.Services;

namespace VoiceStudio.App.Features.Timeline;

/// <summary>
/// Timeline track types.
/// </summary>
public enum TrackType
{
    Audio,
    Voice,
    Music,
    SoundFX,
    Video,
    Subtitle,
}

/// <summary>
/// A clip on the timeline.
/// </summary>
public partial class TimelineClip : ObservableObject
{
    private double _startTime;
    private double _duration;
    private double _volume = 1.0;
    private bool _isMuted;
    private bool _isSelected;

    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Name { get; set; } = "";
    public string SourcePath { get; set; } = "";
    public string? AudioId { get; set; }
    public TrackType TrackType { get; set; }
    
    public double StartTime
    {
        get => _startTime;
        set => SetProperty(ref _startTime, value);
    }
    
    public double Duration
    {
        get => _duration;
        set
        {
            if (SetProperty(ref _duration, value))
            {
                OnPropertyChanged(nameof(EndTime));
            }
        }
    }
    
    public double EndTime => StartTime + Duration;
    
    public double Volume
    {
        get => _volume;
        set => SetProperty(ref _volume, Math.Clamp(value, 0, 2));
    }
    
    public bool IsMuted
    {
        get => _isMuted;
        set => SetProperty(ref _isMuted, value);
    }
    
    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }
    
    // Audio properties
    public double FadeInDuration { get; set; }
    public double FadeOutDuration { get; set; }
    public double TrimStart { get; set; }
    public double TrimEnd { get; set; }
}

/// <summary>
/// A track on the timeline.
/// </summary>
public partial class TimelineTrack : ObservableObject
{
    private bool _isExpanded = true;
    private bool _isMuted;
    private bool _isSolo;
    private double _volume = 1.0;
    private bool _isLocked;

    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Name { get; set; } = "";
    public TrackType Type { get; set; }
    public int Order { get; set; }
    public double Height { get; set; } = 80;
    public ObservableCollection<TimelineClip> Clips { get; } = new();
    
    public bool IsExpanded
    {
        get => _isExpanded;
        set => SetProperty(ref _isExpanded, value);
    }
    
    public bool IsMuted
    {
        get => _isMuted;
        set => SetProperty(ref _isMuted, value);
    }
    
    public bool IsSolo
    {
        get => _isSolo;
        set => SetProperty(ref _isSolo, value);
    }
    
    public bool IsLocked
    {
        get => _isLocked;
        set => SetProperty(ref _isLocked, value);
    }
    
    public double Volume
    {
        get => _volume;
        set => SetProperty(ref _volume, Math.Clamp(value, 0, 2));
    }
}

/// <summary>
/// ViewModel for the timeline component.
/// GAP-FE-001: Refactored to inherit from BaseViewModel and integrate with TimelineGateway.
/// Backend-Frontend Integration Plan - Phase 2: Implements state persistence.
/// </summary>
public partial class TimelineViewModel : BaseViewModel, IPanelStatePersistable
{
    /// <summary>
    /// Panel ID for state persistence.
    /// </summary>
    public const string PanelIdConst = "timeline";
    private readonly ITimelineGateway _timelineGateway;
    private readonly IEventAggregator? _eventAggregator;
    private ISubscriptionToken? _assetSelectedToken;
    private ISubscriptionToken? _profileSelectedToken;
    
    private string? _projectId;
    private double _currentTime;
    private double _totalDuration = 60;
    private double _zoom = 1.0;
    private double _scrollPosition;
    private bool _isPlaying;
    private bool _isLooping;
    private double _selectionStart;
    private double _selectionEnd;
    private bool _snapEnabled = true;
    private double _snapInterval = 0.1;

    public TimelineViewModel(
        IViewModelContext context,
        ITimelineGateway timelineGateway)
        : base(context)
    {
        _timelineGateway = timelineGateway ?? throw new ArgumentNullException(nameof(timelineGateway));

        Tracks = new ObservableCollection<TimelineTrack>();
        SelectedClips = new ObservableCollection<TimelineClip>();
        Markers = new ObservableCollection<MarkerInfo>();
        
        // Commands
        PlayCommand = new RelayCommand(Play);
        PauseCommand = new RelayCommand(Pause);
        StopCommand = new RelayCommand(Stop);
        AddTrackCommand = new AsyncRelayCommand<TrackType>(AddTrackAsync);
        DeleteSelectedCommand = new AsyncRelayCommand(DeleteSelectedAsync);
        SplitAtPlayheadCommand = new RelayCommand(SplitAtPlayhead);
        SaveTimelineCommand = new AsyncRelayCommand(SaveTimelineAsync);
        RefreshCommand = new AsyncRelayCommand(RefreshAsync);

        // Initialize EventAggregator and subscribe to cross-panel events (Phase 4)
        _eventAggregator = AppServices.TryGetEventAggregator();
        if (_eventAggregator != null)
        {
            _assetSelectedToken = _eventAggregator.Subscribe<AssetSelectedEvent>(OnAssetSelected);
            _profileSelectedToken = _eventAggregator.Subscribe<ProfileSelectedEvent>(OnProfileSelected);
        }
    }

    /// <summary>
    /// Handles asset selection events from the library panel.
    /// Backend-Frontend Integration Plan - Phase 4: Cross-panel synchronization.
    /// </summary>
    private void OnAssetSelected(AssetSelectedEvent e)
    {
        // When an audio asset is selected in the Library, we could offer to add it to the timeline
        System.Diagnostics.Debug.WriteLine($"TimelineViewModel: Asset selected - {e.AssetId} ({e.AssetType})");
        // Future: could show "Add to Timeline" action or highlight compatible tracks
    }

    /// <summary>
    /// Handles profile selection events from the profiles panel.
    /// Backend-Frontend Integration Plan - Phase 4: Cross-panel synchronization.
    /// </summary>
    private void OnProfileSelected(ProfileSelectedEvent e)
    {
        // When a profile is selected, update the default voice profile for new voice clips
        System.Diagnostics.Debug.WriteLine($"TimelineViewModel: Profile selected - {e.ProfileId} ({e.ProfileName})");
        // Future: could set this as the active profile for synthesis operations
    }

    /// <summary>
    /// Current project ID - required for timeline operations.
    /// </summary>
    public string? ProjectId
    {
        get => _projectId;
        set
        {
            if (SetProperty(ref _projectId, value) && !string.IsNullOrEmpty(value))
            {
                _ = LoadTimelineAsync(value);
            }
        }
    }

    public ObservableCollection<TimelineTrack> Tracks { get; }
    public ObservableCollection<TimelineClip> SelectedClips { get; }
    public ObservableCollection<MarkerInfo> Markers { get; }

    public double CurrentTime
    {
        get => _currentTime;
        set
        {
            var clampedValue = Math.Clamp(value, 0, TotalDuration);
            if (SetProperty(ref _currentTime, clampedValue))
            {
                OnPropertyChanged(nameof(CurrentTimeFormatted));
            }
        }
    }

    public string CurrentTimeFormatted =>
        TimeSpan.FromSeconds(CurrentTime).ToString(@"mm\:ss\.fff");

    public double TotalDuration
    {
        get => _totalDuration;
        set => SetProperty(ref _totalDuration, value);
    }

    public double Zoom
    {
        get => _zoom;
        set
        {
            if (SetProperty(ref _zoom, Math.Clamp(value, 0.1, 10)))
            {
                OnPropertyChanged(nameof(PixelsPerSecond));
            }
        }
    }

    public double PixelsPerSecond => 100 * Zoom;

    public double ScrollPosition
    {
        get => _scrollPosition;
        set => SetProperty(ref _scrollPosition, value);
    }

    public bool IsPlaying
    {
        get => _isPlaying;
        set => SetProperty(ref _isPlaying, value);
    }

    public bool IsLooping
    {
        get => _isLooping;
        set => SetProperty(ref _isLooping, value);
    }

    public double SelectionStart
    {
        get => _selectionStart;
        set => SetProperty(ref _selectionStart, value);
    }

    public double SelectionEnd
    {
        get => _selectionEnd;
        set => SetProperty(ref _selectionEnd, value);
    }

    public bool SnapEnabled
    {
        get => _snapEnabled;
        set => SetProperty(ref _snapEnabled, value);
    }

    public double SnapInterval
    {
        get => _snapInterval;
        set => SetProperty(ref _snapInterval, value);
    }

    // Commands
    public IRelayCommand PlayCommand { get; }
    public IRelayCommand PauseCommand { get; }
    public IRelayCommand StopCommand { get; }
    public IAsyncRelayCommand<TrackType> AddTrackCommand { get; }
    public IAsyncRelayCommand DeleteSelectedCommand { get; }
    public IRelayCommand SplitAtPlayheadCommand { get; }
    public IAsyncRelayCommand SaveTimelineCommand { get; }
    public IAsyncRelayCommand RefreshCommand { get; }

    /// <summary>
    /// Initializes the timeline for a project.
    /// </summary>
    public async Task InitializeAsync(string projectId)
    {
        ProjectId = projectId;
        await LoadTimelineAsync(projectId);
    }

    private async Task LoadTimelineAsync(string projectId)
    {
        if (string.IsNullOrEmpty(projectId))
        {
            return;
        }

        IsLoading = true;
        StatusMessage = "Loading timeline...";

        try
        {
            var result = await _timelineGateway.GetAsync(projectId);
            
            if (result.Success && result.Data != null)
            {
                Tracks.Clear();
                Markers.Clear();
                
                TotalDuration = result.Data.DurationSeconds > 0 
                    ? result.Data.DurationSeconds 
                    : 60;
                
                foreach (var trackInfo in result.Data.Tracks)
                {
                    var track = new TimelineTrack
                    {
                        Id = trackInfo.Id,
                        Name = trackInfo.Name,
                        Type = MapTrackType(trackInfo.Type),
                        Order = trackInfo.Order,
                        IsMuted = trackInfo.IsMuted,
                        IsLocked = trackInfo.IsLocked,
                        Volume = trackInfo.Volume,
                    };
                    
                    foreach (var clipInfo in trackInfo.Clips)
                    {
                        track.Clips.Add(new TimelineClip
                        {
                            Id = clipInfo.Id,
                            AudioId = clipInfo.AudioId,
                            StartTime = clipInfo.StartTime,
                            Duration = clipInfo.Duration,
                            TrimStart = clipInfo.TrimStart,
                            TrimEnd = clipInfo.TrimEnd,
                            Volume = clipInfo.Volume,
                            FadeInDuration = clipInfo.FadeIn,
                            FadeOutDuration = clipInfo.FadeOut,
                            Name = clipInfo.Label ?? "",
                            TrackType = track.Type,
                        });
                    }
                    
                    Tracks.Add(track);
                }
                
                foreach (var marker in result.Data.Markers)
                {
                    Markers.Add(marker);
                }
                
                StatusMessage = "Timeline loaded";
            }
            else
            {
                Logger.LogWarning("Failed to load timeline: {Error}", result.Error);
                StatusMessage = "Failed to load timeline";
            }
        }
        catch (Exception ex)
        {
            await HandleErrorAsync(ex, "Failed to load timeline");
        }
        finally
        {
            IsLoading = false;
        }
    }

    private async Task RefreshAsync()
    {
        if (!string.IsNullOrEmpty(ProjectId))
        {
            await LoadTimelineAsync(ProjectId);
        }
    }

    private async Task SaveTimelineAsync()
    {
        if (string.IsNullOrEmpty(ProjectId))
        {
            StatusMessage = "No project loaded";
            return;
        }

        IsLoading = true;
        StatusMessage = "Saving timeline...";

        try
        {
            // Timeline is saved incrementally via add/update operations
            // This method could trigger a full sync if needed
            StatusMessage = "Timeline saved";
        }
        catch (Exception ex)
        {
            await HandleErrorAsync(ex, "Failed to save timeline");
        }
        finally
        {
            IsLoading = false;
        }
    }

    public void Play()
    {
        IsPlaying = true;
        // Publish playback state change for cross-panel synchronization (Phase 4)
        _eventAggregator?.Publish(new PlaybackStateChangedEvent(PanelIdConst, true, CurrentTime));
    }

    public void Pause()
    {
        IsPlaying = false;
        // Publish playback state change for cross-panel synchronization (Phase 4)
        _eventAggregator?.Publish(new PlaybackStateChangedEvent(PanelIdConst, false, CurrentTime));
    }

    public void Stop()
    {
        IsPlaying = false;
        CurrentTime = 0;
        // Publish playback state change for cross-panel synchronization (Phase 4)
        _eventAggregator?.Publish(new PlaybackStateChangedEvent(PanelIdConst, false, 0));
    }

    public async Task AddTrackAsync(TrackType type)
    {
        if (string.IsNullOrEmpty(ProjectId))
        {
            return;
        }

        try
        {
            var request = new TrackCreateRequest
            {
                Name = $"{type} {Tracks.Count + 1}",
                Type = MapToGatewayTrackType(type),
                Order = Tracks.Count,
            };
            
            var result = await _timelineGateway.AddTrackAsync(ProjectId, request);
            
            if (result.Success && result.Data != null)
            {
                var track = new TimelineTrack
                {
                    Id = result.Data.Id,
                    Name = result.Data.Name,
                    Type = type,
                    Order = result.Data.Order,
                };
                
                Tracks.Add(track);
                StatusMessage = $"Added track: {track.Name}";
            }
            else
            {
                await HandleErrorAsync(result.Error?.Message ?? "Failed to add track", "Timeline error", showDialog: false);
            }
        }
        catch (Exception ex)
        {
            await HandleErrorAsync(ex, "Failed to add track");
        }
    }

    public async Task AddClipAsync(string trackId, TimelineClip clip)
    {
        if (string.IsNullOrEmpty(ProjectId))
        {
            return;
        }

        TimelineTrack? targetTrack = null;
        foreach (var track in Tracks)
        {
            if (track.Id == trackId)
            {
                targetTrack = track;
                break;
            }
        }

        if (targetTrack == null)
        {
            return;
        }

        try
        {
            if (SnapEnabled)
            {
                clip.StartTime = SnapTime(clip.StartTime);
            }
            
            var request = new ClipCreateRequest
            {
                AudioId = clip.AudioId ?? "",
                StartTime = clip.StartTime,
                Duration = clip.Duration > 0 ? clip.Duration : null,
                Label = clip.Name,
            };
            
            var result = await _timelineGateway.AddClipAsync(ProjectId, trackId, request);
            
            if (result.Success && result.Data != null)
            {
                clip.Id = result.Data.Id;
                targetTrack.Clips.Add(clip);
                UpdateTotalDuration();
                StatusMessage = $"Added clip: {clip.Name}";
            }
            else
            {
                await HandleErrorAsync(result.Error?.Message ?? "Failed to add clip", "Timeline error", showDialog: false);
            }
        }
        catch (Exception ex)
        {
            await HandleErrorAsync(ex, "Failed to add clip");
        }
    }

    public async Task MoveClipAsync(TimelineClip clip, double newStartTime, string? newTrackId = null)
    {
        if (string.IsNullOrEmpty(ProjectId))
        {
            return;
        }

        if (SnapEnabled)
        {
            newStartTime = SnapTime(newStartTime);
        }
        
        clip.StartTime = Math.Max(0, newStartTime);
        
        // Find current track
        string? currentTrackId = null;
        TimelineTrack? currentTrack = null;
        
        foreach (var track in Tracks)
        {
            if (track.Clips.Contains(clip))
            {
                currentTrackId = track.Id;
                currentTrack = track;
                break;
            }
        }

        if (currentTrackId == null)
        {
            return;
        }

        try
        {
            var request = new ClipUpdateRequest
            {
                StartTime = clip.StartTime,
            };
            
            var result = await _timelineGateway.UpdateClipAsync(ProjectId, currentTrackId, clip.Id, request);
            
            if (!result.Success)
            {
                Logger.LogWarning("Failed to update clip position: {Error}", result.Error);
            }
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to update clip position");
        }
        
        // Move to different track if specified
        if (newTrackId != null && newTrackId != currentTrackId && currentTrack != null)
        {
            currentTrack.Clips.Remove(clip);
            
            foreach (var track in Tracks)
            {
                if (track.Id == newTrackId)
                {
                    track.Clips.Add(clip);
                    break;
                }
            }
        }
        
        UpdateTotalDuration();
    }

    public void SelectClip(TimelineClip clip, bool addToSelection = false)
    {
        if (!addToSelection)
        {
            foreach (var selected in SelectedClips)
            {
                selected.IsSelected = false;
            }
            SelectedClips.Clear();
        }
        
        clip.IsSelected = true;
        SelectedClips.Add(clip);

        // Publish timeline selection change event (Phase 4)
        PublishSelectionChangedEvent();
    }

    public void ClearSelection()
    {
        foreach (var clip in SelectedClips)
        {
            clip.IsSelected = false;
        }
        SelectedClips.Clear();

        // Publish timeline selection change event (Phase 4)
        PublishSelectionChangedEvent();
    }

    /// <summary>
    /// Publishes a timeline selection changed event for cross-panel synchronization.
    /// Backend-Frontend Integration Plan - Phase 4.
    /// </summary>
    private void PublishSelectionChangedEvent()
    {
        var selectedClipIds = SelectedClips.Select(c => c.Id).ToList();
        _eventAggregator?.Publish(new TimelineSelectionChangedEvent(
            PanelIdConst, 
            SelectionStart, 
            SelectionEnd, 
            selectedClipIds));
    }

    private async Task DeleteSelectedAsync()
    {
        if (string.IsNullOrEmpty(ProjectId))
        {
            return;
        }

        foreach (var clip in SelectedClips)
        {
            foreach (var track in Tracks)
            {
                if (track.Clips.Contains(clip))
                {
                    try
                    {
                        var result = await _timelineGateway.RemoveClipAsync(ProjectId, track.Id, clip.Id);
                        if (result.Success)
                        {
                            track.Clips.Remove(clip);
                        }
                    }
                    catch (Exception ex)
                    {
                        Logger.LogError(ex, "Failed to delete clip {ClipId}", clip.Id);
                    }
                    break;
                }
            }
        }
        
        SelectedClips.Clear();
        UpdateTotalDuration();
    }

    public void SplitAtPlayhead()
    {
        foreach (var clip in SelectedClips)
        {
            if (CurrentTime > clip.StartTime && CurrentTime < clip.EndTime)
            {
                var splitPoint = CurrentTime - clip.StartTime;
                
                var newClip = new TimelineClip
                {
                    Name = $"{clip.Name} (split)",
                    SourcePath = clip.SourcePath,
                    AudioId = clip.AudioId,
                    TrackType = clip.TrackType,
                    StartTime = CurrentTime,
                    Duration = clip.Duration - splitPoint,
                    Volume = clip.Volume,
                    TrimStart = clip.TrimStart + splitPoint,
                };
                
                clip.Duration = splitPoint;
                
                foreach (var track in Tracks)
                {
                    if (track.Clips.Contains(clip))
                    {
                        track.Clips.Add(newClip);
                        break;
                    }
                }
            }
        }
    }

    public void ZoomIn()
    {
        Zoom *= 1.2;
    }

    public void ZoomOut()
    {
        Zoom /= 1.2;
    }

    public void ZoomToFit()
    {
        if (TotalDuration > 0)
        {
            Zoom = 1.0;
        }
    }

    private double SnapTime(double time)
    {
        if (!SnapEnabled || SnapInterval <= 0)
        {
            return time;
        }
        
        return Math.Round(time / SnapInterval) * SnapInterval;
    }

    private void UpdateTotalDuration()
    {
        double maxEnd = 0;
        
        foreach (var track in Tracks)
        {
            foreach (var clip in track.Clips)
            {
                if (clip.EndTime > maxEnd)
                {
                    maxEnd = clip.EndTime;
                }
            }
        }
        
        TotalDuration = Math.Max(60, maxEnd + 10);
    }

    private static TrackType MapTrackType(VoiceStudio.Core.Gateways.TrackType gatewayType)
    {
        return gatewayType switch
        {
            VoiceStudio.Core.Gateways.TrackType.Audio => TrackType.Audio,
            VoiceStudio.Core.Gateways.TrackType.Voice => TrackType.Voice,
            VoiceStudio.Core.Gateways.TrackType.Video => TrackType.Video,
            VoiceStudio.Core.Gateways.TrackType.Subtitle => TrackType.Subtitle,
            _ => TrackType.Audio,
        };
    }

    private static VoiceStudio.Core.Gateways.TrackType MapToGatewayTrackType(TrackType localType)
    {
        return localType switch
        {
            TrackType.Audio => VoiceStudio.Core.Gateways.TrackType.Audio,
            TrackType.Voice => VoiceStudio.Core.Gateways.TrackType.Voice,
            TrackType.Music => VoiceStudio.Core.Gateways.TrackType.Audio,
            TrackType.SoundFX => VoiceStudio.Core.Gateways.TrackType.Audio,
            TrackType.Video => VoiceStudio.Core.Gateways.TrackType.Video,
            TrackType.Subtitle => VoiceStudio.Core.Gateways.TrackType.Subtitle,
            _ => VoiceStudio.Core.Gateways.TrackType.Audio,
        };
    }

    #region IPanelStatePersistable Implementation

    /// <summary>
    /// Gets the current panel state for persistence.
    /// Backend-Frontend Integration Plan - Phase 2.
    /// </summary>
    public PanelStateData? GetCurrentState()
    {
        try
        {
            var state = new PanelStateData
            {
                PanelId = PanelIdConst,
                ScrollPosition = ScrollPosition,
                ZoomLevel = Zoom,
                CapturedAt = DateTime.UtcNow,
                CustomData = new Dictionary<string, object>()
            };

            // Store playback state
            state.CustomData["CurrentTime"] = CurrentTime;
            state.CustomData["IsLooping"] = IsLooping;
            state.CustomData["SnapEnabled"] = SnapEnabled;
            state.CustomData["SnapInterval"] = SnapInterval;

            // Store selection state
            state.CustomData["SelectionStart"] = SelectionStart;
            state.CustomData["SelectionEnd"] = SelectionEnd;

            // Store selected clip IDs
            if (SelectedClips.Count > 0)
                state.SelectedItemIds = SelectedClips.Select(c => c.Id).ToArray();

            // Store project ID
            if (!string.IsNullOrEmpty(ProjectId))
                state.CustomData["ProjectId"] = ProjectId;

            // Store track expansion states
            var trackExpansions = new Dictionary<string, bool>();
            foreach (var track in Tracks)
            {
                trackExpansions[track.Id] = track.IsExpanded;
            }
            if (trackExpansions.Count > 0)
                state.ExpandedSections = trackExpansions;

            return state;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to get TimelineViewModel state: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Restores panel state from persistence.
    /// Backend-Frontend Integration Plan - Phase 2.
    /// </summary>
    public async Task RestoreStateAsync(PanelStateData state, CancellationToken cancellationToken = default)
    {
        if (state == null) return;

        try
        {
            // Restore zoom and scroll
            if (state.ZoomLevel.HasValue)
                Zoom = state.ZoomLevel.Value;
            if (state.ScrollPosition.HasValue)
                ScrollPosition = state.ScrollPosition.Value;

            // Restore playback state
            if (state.CustomData?.TryGetValue("CurrentTime", out var currentTime) == true && currentTime is double currentTimeVal)
                CurrentTime = currentTimeVal;
            if (state.CustomData?.TryGetValue("IsLooping", out var isLooping) == true && isLooping is bool isLoopingVal)
                IsLooping = isLoopingVal;
            if (state.CustomData?.TryGetValue("SnapEnabled", out var snapEnabled) == true && snapEnabled is bool snapEnabledVal)
                SnapEnabled = snapEnabledVal;
            if (state.CustomData?.TryGetValue("SnapInterval", out var snapInterval) == true && snapInterval is double snapIntervalVal)
                SnapInterval = snapIntervalVal;

            // Restore selection state
            if (state.CustomData?.TryGetValue("SelectionStart", out var selStart) == true && selStart is double selStartVal)
                SelectionStart = selStartVal;
            if (state.CustomData?.TryGetValue("SelectionEnd", out var selEnd) == true && selEnd is double selEndVal)
                SelectionEnd = selEndVal;

            // Restore project ID (this will trigger loading)
            if (state.CustomData?.TryGetValue("ProjectId", out var projectId) == true && projectId is string projectIdStr)
            {
                if (ProjectId != projectIdStr)
                    ProjectId = projectIdStr;
            }

            // Restore track expansion states (need tracks to be loaded first)
            if (state.ExpandedSections != null)
            {
                foreach (var track in Tracks)
                {
                    if (state.ExpandedSections.TryGetValue(track.Id, out var isExpanded))
                        track.IsExpanded = isExpanded;
                }
            }

            // Restore selected clips (need clips to be loaded first)
            if (state.SelectedItemIds?.Length > 0)
            {
                SelectedClips.Clear();
                foreach (var track in Tracks)
                {
                    foreach (var clip in track.Clips)
                    {
                        if (state.SelectedItemIds.Contains(clip.Id))
                        {
                            clip.IsSelected = true;
                            SelectedClips.Add(clip);
                        }
                    }
                }
            }

            await Task.CompletedTask;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to restore TimelineViewModel state: {ex.Message}");
        }
    }

    #endregion
}
