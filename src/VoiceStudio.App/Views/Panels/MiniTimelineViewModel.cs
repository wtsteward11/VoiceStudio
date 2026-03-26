using System;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VoiceStudio.App.Services;
using VoiceStudio.App.Utilities;
using VoiceStudio.Core.Panels;
using VoiceStudio.Core.Services;

namespace VoiceStudio.App.Views.Panels
{
  /// <summary>
  /// ViewModel for Mini Timeline view.
  /// Implements IDEA 6: Mini Timeline in BottomPanelHost.
  /// </summary>
  public partial class MiniTimelineViewModel : ObservableObject, IPanelView
  {
    private readonly IAudioPlayerService _audioPlayer;
    private TimelineViewModel? _mainTimelineViewModel;

    public string PanelId => "mini-timeline";
    public string DisplayName => ResourceHelper.GetString("Panel.MiniTimeline.DisplayName", "Mini Timeline");
    public PanelRegion Region => PanelRegion.Bottom;

    [ObservableProperty]
    private double currentPlaybackPosition;

    [ObservableProperty]
    private double duration;

    [ObservableProperty]
    private bool isPlaying;

    [ObservableProperty]
    private double timelineZoom = 1.0;

    // Pixels per second for timeline rendering
    private const double BASE_PIXELS_PER_SECOND = 50.0;

    /// <summary>
    /// Pixels per second for timeline rendering (adjusted by zoom).
    /// </summary>
    public double PixelsPerSecond => BASE_PIXELS_PER_SECOND * TimelineZoom;

    /// <summary>
    /// Playhead position in pixels for visual rendering.
    /// </summary>
    public double PlayheadPosition => CurrentPlaybackPosition * PixelsPerSecond;

    /// <summary>
    /// Visibility of the playhead indicator.
    /// </summary>
    public bool IsPlayheadVisible => IsPlaying || _audioPlayer.IsPlaying;

    /// <summary>
    /// Current time display (MM:SS format).
    /// </summary>
    public string CurrentTimeDisplay => FormatTime(CurrentPlaybackPosition);

    /// <summary>
    /// Duration display (MM:SS format).
    /// </summary>
    public string DurationDisplay => FormatTime(Duration);

    /// <summary>
    /// Play/Pause button text.
    /// </summary>
    public string PlayPauseButtonText => IsPlaying ? "⏸" : "▶";

    /// <summary>
    /// Play/Pause button tooltip.
    /// </summary>
    public string PlayPauseTooltip => IsPlaying ? "Pause (Space)" : "Play (Space)";

    public IRelayCommand PlayPauseCommand { get; }
    public IRelayCommand StopCommand { get; }
    public IRelayCommand ZoomInCommand { get; }
    public IRelayCommand ZoomOutCommand { get; }

    public MiniTimelineViewModel(IAudioPlayerService audioPlayer)
    {
      _audioPlayer = audioPlayer ?? throw new ArgumentNullException(nameof(audioPlayer));

      PlayPauseCommand = new RelayCommand(PlayPause, () => Duration > 0);
      StopCommand = new RelayCommand(Stop, () => IsPlaying || _audioPlayer.IsPlaying);
      ZoomInCommand = new RelayCommand(() => TimelineZoom = Math.Min(TimelineZoom * 1.5, 10.0));
      ZoomOutCommand = new RelayCommand(() => TimelineZoom = Math.Max(TimelineZoom / 1.5, 0.1));

      // Subscribe to audio player events
      _audioPlayer.PositionChanged += AudioPlayer_PositionChanged;
      _audioPlayer.IsPlayingChanged += AudioPlayer_IsPlayingChanged;
      _audioPlayer.PlaybackCompleted += AudioPlayer_PlaybackCompleted;

      // Try to find and sync with main TimelineViewModel
      TrySyncWithMainTimeline();
    }

    private void TrySyncWithMainTimeline()
    {
      // Try to get TimelineViewModel from service provider or find it
      // For now, we'll update from audio player position
      // In a full implementation, we'd sync with TimelineViewModel if available
    }

    private void AudioPlayer_PositionChanged(object? sender, double position)
    {
      CurrentPlaybackPosition = position;
      OnPropertyChanged(nameof(PlayheadPosition));
      OnPropertyChanged(nameof(CurrentTimeDisplay));
    }

    private void AudioPlayer_IsPlayingChanged(object? sender, bool isPlaying)
    {
      IsPlaying = isPlaying;
      OnPropertyChanged(nameof(PlayPauseButtonText));
      OnPropertyChanged(nameof(PlayPauseTooltip));
      OnPropertyChanged(nameof(IsPlayheadVisible));
      PlayPauseCommand.NotifyCanExecuteChanged();
      StopCommand.NotifyCanExecuteChanged();
    }

    private void AudioPlayer_PlaybackCompleted(object? sender, EventArgs e)
    {
      IsPlaying = false;
      CurrentPlaybackPosition = 0.0;
      OnPropertyChanged(nameof(PlayPauseButtonText));
      OnPropertyChanged(nameof(PlayPauseTooltip));
      OnPropertyChanged(nameof(IsPlayheadVisible));
      PlayPauseCommand.NotifyCanExecuteChanged();
      StopCommand.NotifyCanExecuteChanged();
    }

    partial void OnTimelineZoomChanged(double value)
    {
      OnPropertyChanged(nameof(PixelsPerSecond));
      OnPropertyChanged(nameof(PlayheadPosition));
    }

    partial void OnCurrentPlaybackPositionChanged(double value)
    {
      OnPropertyChanged(nameof(PlayheadPosition));
      OnPropertyChanged(nameof(CurrentTimeDisplay));
    }

    partial void OnDurationChanged(double value)
    {
      PlayPauseCommand.NotifyCanExecuteChanged();
    }

    /// <summary>
    /// Seek to a specific time position.
    /// </summary>
    public void SeekToTime(double time)
    {
      time = Math.Max(0, Math.Min(time, Duration));
      CurrentPlaybackPosition = time;

      // Seek audio player if playing
      if (_audioPlayer.IsPlaying)
      {
        _audioPlayer.Seek(time);
      }

      // Sync with main timeline if available
      if (_mainTimelineViewModel != null)
      {
        _mainTimelineViewModel.CurrentPlaybackPosition = time;
      }
    }

    private void PlayPause()
    {
      if (IsPlaying || _audioPlayer.IsPlaying)
      {
        if (_audioPlayer.IsPaused)
        {
          _audioPlayer.Resume();
        }
        else
        {
          _audioPlayer.Pause();
        }
      }
      else
      {
        // Start playback from current position
        // In a full implementation, this would play the current project timeline
        // For now, we'll just update the state
        IsPlaying = true;
      }
    }

    private void Stop()
    {
      _audioPlayer.Stop();
      CurrentPlaybackPosition = 0.0;
      IsPlaying = false;
    }

    private string FormatTime(double seconds)
    {
      var minutes = (int)(seconds / 60);
      var secs = (int)(seconds % 60);
      return $"{minutes}:{secs:D2}";
    }

    /// <summary>
    /// Sync with main TimelineViewModel for position and duration.
    /// </summary>
    public void SyncWithMainTimeline(TimelineViewModel? mainTimeline)
    {
      _mainTimelineViewModel = mainTimeline;
      if (mainTimeline != null)
      {
        // Subscribe to main timeline changes
        mainTimeline.PropertyChanged += (s, e) =>
        {
          if (e.PropertyName == nameof(TimelineViewModel.CurrentPlaybackPosition))
          {
            CurrentPlaybackPosition = mainTimeline.CurrentPlaybackPosition;
          }
          if (e.PropertyName == nameof(TimelineViewModel.IsPlaying))
          {
            IsPlaying = mainTimeline.IsPlaying;
          }
          // Duration would come from project/timeline data
        };
      }
    }

    /// <summary>
    /// Update duration from project timeline.
    /// </summary>
    public void UpdateDuration(double newDuration)
    {
      Duration = newDuration;
    }
  }
}