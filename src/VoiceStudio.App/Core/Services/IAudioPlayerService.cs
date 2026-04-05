using System;
using System.Threading.Tasks;

namespace VoiceStudio.Core.Services
{
  /// <summary>
  /// Service interface for audio playback functionality.
  /// Supports playing audio files, streams, and managing playback state.
  /// </summary>
  public interface IAudioPlayerService
  {
    /// <summary>
    /// Plays an audio file from a file path.
    /// </summary>
    /// <param name="filePath">Path to the audio file</param>
    /// <param name="onPlaybackComplete">Optional callback when playback completes</param>
    /// <returns>Task representing the playback operation</returns>
    Task PlayFileAsync(string filePath, Action? onPlaybackComplete = null);

    /// <summary>
    /// Plays audio from a stream (e.g., from backend API).
    /// </summary>
    /// <param name="audioStream">Stream containing audio data</param>
    /// <param name="sampleRate">Sample rate of the audio (default: 22050)</param>
    /// <param name="channels">Number of audio channels (default: 1 for mono)</param>
    /// <param name="onPlaybackComplete">Optional callback when playback completes</param>
    /// <returns>Task representing the playback operation</returns>
    Task PlayStreamAsync(System.IO.Stream audioStream, int sampleRate = 22050, int channels = 1, Action? onPlaybackComplete = null);

    /// <summary>
    /// Plays audio from a URL by downloading to a temp file and playing locally.
    /// </summary>
    /// <param name="audioUrl">Full URL to the audio file</param>
    /// <param name="onPlaybackComplete">Optional callback when playback completes</param>
    /// <returns>Task representing the playback operation</returns>
    Task PlayUrlAsync(string audioUrl, Action? onPlaybackComplete = null);

    /// <summary>
    /// Plays audio by backend audio ID via the canonical /api/audio/file/{id} endpoint.
    /// </summary>
    /// <param name="audioId">Backend audio ID (from upload or synthesis)</param>
    /// <param name="baseUrl">Backend base URL (e.g., http://127.0.0.1:8000)</param>
    /// <param name="onPlaybackComplete">Optional callback when playback completes</param>
    /// <returns>Task representing the playback operation</returns>
    Task PlayBackendAudioIdAsync(string audioId, string baseUrl, Action? onPlaybackComplete = null);

    /// <summary>
    /// Stops the current playback.
    /// </summary>
    void Stop();

    /// <summary>
    /// Pauses the current playback.
    /// </summary>
    void Pause();

    /// <summary>
    /// Resumes paused playback.
    /// </summary>
    void Resume();

    /// <summary>
    /// Gets the path of the last temp file created for URL playback, if any.
    /// Set when PlayUrlAsync creates a temp file; used by UI smoke to prove temp file creation.
    /// </summary>
    string? LastTempPlaybackPath { get; }

    /// <summary>
    /// Last playback error message, if any. Set when PlaybackStopped receives an exception.
    /// Cleared when playback starts successfully.
    /// </summary>
    string? LastPlaybackError { get; }

    /// <summary>
    /// Name of the output device used for playback. Set when playback starts.
    /// </summary>
    string? LastOutputDeviceName { get; }

    /// <summary>
    /// Gets whether audio is currently playing.
    /// </summary>
    bool IsPlaying { get; }

    /// <summary>
    /// Gets whether audio is currently paused.
    /// </summary>
    bool IsPaused { get; }

    /// <summary>
    /// Gets the current playback position in seconds.
    /// </summary>
    double Position { get; }

    /// <summary>
    /// Gets the total duration of the current audio in seconds.
    /// </summary>
    double Duration { get; }

    /// <summary>
    /// Sets the playback volume (0.0 to 1.0).
    /// </summary>
    double Volume { get; set; }

    /// <summary>
    /// Seeks to a specific position in seconds.
    /// </summary>
    /// <param name="position">Position in seconds to seek to</param>
    void Seek(double position);

    /// <summary>
    /// Gets or sets whether playback should loop when it reaches the end.
    /// When true, playback automatically restarts from the beginning.
    /// </summary>
    bool IsLooping { get; set; }

    /// <summary>
    /// Event raised when playback position changes.
    /// </summary>
    event EventHandler<double>? PositionChanged;

    /// <summary>
    /// Event raised when playback completes.
    /// </summary>
    event EventHandler? PlaybackCompleted;

    /// <summary>
    /// Event raised when playback state changes.
    /// </summary>
    event EventHandler<bool>? IsPlayingChanged;
  }
}