using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using VoiceStudio.Core.Models;
using VoiceStudio.Core.Services;

namespace VoiceStudio.App.Services.Stores
{
  /// <summary>
  /// Centralized store for audio-related state.
  /// Implements React/TypeScript audioStore pattern in C#.
  /// </summary>
  public partial class AudioStore : ObservableObject
  {
    private readonly IBackendClient _backendClient;
    private readonly ITimelineTrackService _trackService;
    private readonly StateCacheService? _stateCacheService;

    [ObservableProperty]
    private ObservableCollection<AudioClip> audioClips = new();

    [ObservableProperty]
    private ObservableCollection<ProjectAudioFile> audioFiles = new();

    [ObservableProperty]
    private AudioClip? selectedClip;

    [ObservableProperty]
    private ProjectAudioFile? selectedFile;

    [ObservableProperty]
    private bool isLoading;

    [ObservableProperty]
    private string? errorMessage;

    [ObservableProperty]
    private DateTime? lastUpdated;

    public AudioStore(IBackendClient backendClient, ITimelineTrackService trackService, StateCacheService? stateCacheService = null)
    {
      _backendClient = backendClient ?? throw new ArgumentNullException(nameof(backendClient));
      _trackService = trackService ?? throw new ArgumentNullException(nameof(trackService));
      _stateCacheService = stateCacheService;
    }

    /// <summary>
    /// Loads all audio clips for a project.
    /// </summary>
    public async Task LoadAudioClipsAsync(string projectId)
    {
      try
      {
        IsLoading = true;
        ErrorMessage = null;

        // Try to load from cache first
        if (_stateCacheService != null)
        {
          var cached = await _stateCacheService.GetCachedStateAsync<ObservableCollection<AudioClip>>($"audio_clips_{projectId}");
          if (cached != null)
          {
            AudioClips = cached;
            IsLoading = false;
            // Still fetch from backend in background to update
            _ = RefreshAudioClipsAsync(projectId);
            return;
          }
        }

        await RefreshAudioClipsAsync(projectId);
      }
      catch (Exception ex)
      {
        ErrorMessage = $"Failed to load audio clips: {ex.Message}";
      }
      finally
      {
        IsLoading = false;
      }
    }

    /// <summary>
    /// Refreshes audio clips from backend.
    /// </summary>
    public async Task RefreshAudioClipsAsync(string projectId)
    {
      try
      {
        // Get tracks for the project, then get clips from each track
        var tracks = await _trackService.GetTracksAsync(projectId);

        AudioClips.Clear();
        foreach (var track in tracks)
        {
          // Tracks contain clips, so add them
          if (track.Clips != null)
          {
            foreach (var clip in track.Clips)
            {
              AudioClips.Add(clip);
            }
          }
        }

        LastUpdated = DateTime.UtcNow;

        // Cache the result
        if (_stateCacheService != null)
        {
          await _stateCacheService.CacheStateAsync($"audio_clips_{projectId}", AudioClips);
        }
      }
      catch (Exception ex)
      {
        ErrorMessage = $"Failed to refresh audio clips: {ex.Message}";
      }
    }

    /// <summary>
    /// Loads audio files for a specific project.
    /// </summary>
    /// <param name="projectId">The project ID to load audio files for.</param>
    public async Task LoadAudioFilesAsync(string projectId)
    {
      if (string.IsNullOrEmpty(projectId))
      {
        ErrorMessage = "Project ID is required to load audio files";
        return;
      }

      try
      {
        IsLoading = true;
        ErrorMessage = null;

        // Try to load from cache first
        if (_stateCacheService != null)
        {
          var cached = await _stateCacheService.GetCachedStateAsync<ObservableCollection<ProjectAudioFile>>($"audio_files_{projectId}");
          if (cached != null)
          {
            AudioFiles = cached;
            IsLoading = false;
            // Still fetch from backend in background to update
            _ = RefreshAudioFilesAsync(projectId);
            return;
          }
        }

        await RefreshAudioFilesAsync(projectId);
      }
      catch (Exception ex)
      {
        ErrorMessage = $"Failed to load audio files: {ex.Message}";
      }
      finally
      {
        IsLoading = false;
      }
    }

    /// <summary>
    /// Refreshes audio files from backend for a specific project.
    /// </summary>
    /// <param name="projectId">The project ID to fetch audio files for.</param>
    public async Task RefreshAudioFilesAsync(string projectId)
    {
      if (string.IsNullOrEmpty(projectId))
      {
        ErrorMessage = "Project ID is required to refresh audio files";
        return;
      }

      try
      {
        IsLoading = true;
        ErrorMessage = null;

        // Fetch audio files from backend using ListProjectAudioAsync
        var files = await _backendClient.ListProjectAudioAsync(projectId);

        AudioFiles.Clear();
        foreach (var file in files)
        {
          AudioFiles.Add(file);
        }

        LastUpdated = DateTime.UtcNow;

        // Cache the result
        if (_stateCacheService != null)
        {
          await _stateCacheService.CacheStateAsync($"audio_files_{projectId}", AudioFiles);
        }
      }
      catch (Exception ex)
      {
        ErrorMessage = $"Failed to refresh audio files: {ex.Message}";
      }
      finally
      {
        IsLoading = false;
      }
    }


    /// <summary>
    /// Adds an audio clip to the store.
    /// </summary>
    public void AddAudioClip(AudioClip clip)
    {
      if (!AudioClips.Any(c => c.Id == clip.Id))
      {
        AudioClips.Add(clip);
        LastUpdated = DateTime.UtcNow;
      }
    }

    /// <summary>
    /// Removes an audio clip from the store.
    /// </summary>
    public void RemoveAudioClip(string clipId)
    {
      var clip = AudioClips.FirstOrDefault(c => c.Id == clipId);
      if (clip != null)
      {
        AudioClips.Remove(clip);
        LastUpdated = DateTime.UtcNow;
      }
    }

    /// <summary>
    /// Updates an audio clip in the store.
    /// </summary>
    public void UpdateAudioClip(AudioClip clip)
    {
      var existing = AudioClips.FirstOrDefault(c => c.Id == clip.Id);
      if (existing != null)
      {
        var index = AudioClips.IndexOf(existing);
        AudioClips[index] = clip;
        LastUpdated = DateTime.UtcNow;
      }
    }

    /// <summary>
    /// Clears all audio state.
    /// </summary>
    public void Clear()
    {
      AudioClips.Clear();
      AudioFiles.Clear();
      SelectedClip = null;
      SelectedFile = null;
      LastUpdated = null;
    }
  }
}