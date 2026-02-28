using System;
using System.IO;
using System.Threading.Tasks;
using VoiceStudio.Core.Events;
using VoiceStudio.Core.Services;
using VoiceStudio.App.Logging;

namespace VoiceStudio.App.Services
{
  /// <summary>
  /// Audio playback service using NAudio for Windows audio playback.
  /// Provides high-quality audio playback for voice cloning previews and timeline playback.
  /// </summary>
  public class AudioPlayerService : IAudioPlayerService, IDisposable
  {
    private NAudio.Wave.WaveOutEvent? _waveOut;
    private NAudio.Wave.AudioFileReader? _audioFileReader;
    private NAudio.Wave.RawSourceWaveStream? _rawStream;
    private bool _disposed;
    private double _volume = 1.0;

    // Preview playback (separate from main playback)
    private NAudio.Wave.WaveOutEvent? _previewWaveOut;
    private NAudio.Wave.AudioFileReader? _previewAudioReader;
    private System.Threading.CancellationTokenSource? _previewCancellation;

    // Inter-panel workflow
    private readonly IEventAggregator? _eventAggregator;
    private ISubscriptionToken? _playbackRequestedSubscription;

    public bool IsPlaying { get; private set; }
    public bool IsPaused { get; private set; }
    public bool IsLooping { get; set; }
    public double Position => _audioFileReader?.CurrentTime.TotalSeconds ?? 0.0;
    public double Duration => _audioFileReader?.TotalTime.TotalSeconds ?? 0.0;

    // Track the current file path for loop restart
    private string? _currentFilePath;

    public double Volume
    {
      get => _volume;
      set
      {
        _volume = Math.Clamp(value, 0.0, 1.0);
        if (_audioFileReader != null)
        {
          _audioFileReader.Volume = (float)_volume;
        }
      }
    }

    public event EventHandler<double>? PositionChanged;
    public event EventHandler? PlaybackCompleted;
    public event EventHandler<bool>? IsPlayingChanged;

    public AudioPlayerService()
    {
      // Initialize with default settings
      // Subscribe to PlaybackRequestedEvent for inter-panel workflow
      _eventAggregator = AppServices.TryGetEventAggregator();
      _playbackRequestedSubscription = _eventAggregator?.Subscribe<PlaybackRequestedEvent>(OnPlaybackRequested);
    }

    public async Task PlayFileAsync(string filePath, Action? onPlaybackComplete = null)
    {
      if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
      {
        throw new FileNotFoundException("Audio file not found", filePath);
      }

      await Task.Run(() =>
      {
        try
        {
          // Stop any current playback
          Stop();

          // Create audio file reader
          _audioFileReader = new NAudio.Wave.AudioFileReader(filePath);
          _audioFileReader.Volume = (float)_volume;
          _currentFilePath = filePath;

          // Create wave out device
          _waveOut = new NAudio.Wave.WaveOutEvent();
          _waveOut.Init(_audioFileReader);
          _waveOut.PlaybackStopped += (_, args) =>
                {
                  // Loop: if IsLooping and playback ended naturally (no error, not user-stopped)
                  if (IsLooping && args.Exception == null && _audioFileReader != null && !_disposed)
                  {
                    try
                    {
                      _audioFileReader.Position = 0;
                      _waveOut?.Play();
                      return; // Don't fire completion events
                    }
                    catch (Exception ex)
                    {
                      // Fall through to normal stop if loop restart fails
                      System.Diagnostics.Debug.WriteLine("[AudioPlayer] Loop restart failed: " + ex.Message);
                    }
                  }

                  IsPlaying = false;
                  IsPaused = false;
                  IsPlayingChanged?.Invoke(this, false);
                  PlaybackCompleted?.Invoke(this, EventArgs.Empty);
                  onPlaybackComplete?.Invoke();
                };

          // Start playback
          _waveOut.Play();
          IsPlaying = true;
          IsPaused = false;
          IsPlayingChanged?.Invoke(this, true);

          // Start position tracking
          _ = Task.Run(async () =>
                {
                  while (IsPlaying && _audioFileReader != null)
                  {
                    PositionChanged?.Invoke(this, Position);
                    await Task.Delay(100); // Update every 100ms
                  }
                });
        }
        catch (Exception ex)
        {
          throw new InvalidOperationException($"Failed to play audio file: {ex.Message}", ex);
        }
      });
    }

    public async Task PlayStreamAsync(Stream audioStream, int sampleRate = 22050, int channels = 1, Action? onPlaybackComplete = null)
    {
      if (audioStream == null)
      {
        throw new ArgumentNullException(nameof(audioStream));
      }

      await Task.Run(() =>
      {
        try
        {
          // Stop any current playback
          Stop();

          // Read audio data from stream
          using var memoryStream = new MemoryStream();
          audioStream.CopyTo(memoryStream);
          memoryStream.Position = 0;

          // Create raw audio stream
          // Note: Assumes 16-bit PCM format
          const int bytesPerSample = 2; // 16-bit = 2 bytes
          var bytesPerSecond = sampleRate * channels * bytesPerSample;
          var totalBytes = (int)memoryStream.Length;
          var duration = TimeSpan.FromSeconds((double)totalBytes / bytesPerSecond);

          _rawStream = new NAudio.Wave.RawSourceWaveStream(
                    memoryStream.ToArray(),
                    0,
                    totalBytes,
                    new NAudio.Wave.WaveFormat(sampleRate, 16, channels)
                );

          // Create wave out device
          _waveOut = new NAudio.Wave.WaveOutEvent();
          _waveOut.Init(_rawStream);
          _waveOut.PlaybackStopped += (_, args) =>
                {
                  // Loop: restart from beginning if looping is enabled
                  if (IsLooping && args.Exception == null && _rawStream != null && !_disposed)
                  {
                    try
                    {
                      _rawStream.Position = 0;
                      _waveOut?.Play();
                      return;
                    }
                    catch (Exception ex)
                    {
                      // Fall through to normal stop
                      System.Diagnostics.Debug.WriteLine("[AudioPlayer] Loop restart failed: " + ex.Message);
                    }
                  }

                  IsPlaying = false;
                  IsPaused = false;
                  IsPlayingChanged?.Invoke(this, false);
                  PlaybackCompleted?.Invoke(this, EventArgs.Empty);
                  onPlaybackComplete?.Invoke();
                };

          // Start playback
          _waveOut.Play();
          IsPlaying = true;
          IsPaused = false;
          IsPlayingChanged?.Invoke(this, true);
        }
        catch (Exception ex)
        {
          throw new InvalidOperationException($"Failed to play audio stream: {ex.Message}", ex);
        }
      });
    }

    public void Stop()
    {
      try
      {
        _waveOut?.Stop();
        _audioFileReader?.Dispose();
        _rawStream?.Dispose();
        _waveOut?.Dispose();

        _waveOut = null;
        _audioFileReader = null;
        _rawStream = null;
        IsPlaying = false;
        IsPaused = false;
        IsPlayingChanged?.Invoke(this, false);
      }
      catch (Exception ex)
      {
        ErrorLogger.LogWarning($"Best effort operation failed: {ex.Message}", "AudioPlayerService.Stop");
      }
    }

    public void Pause()
    {
      if (_waveOut != null && IsPlaying && !IsPaused)
      {
        _waveOut.Pause();
        IsPaused = true;
        IsPlayingChanged?.Invoke(this, false);
      }
    }

    public void Resume()
    {
      if (_waveOut != null && IsPaused)
      {
        // NAudio WaveOutEvent doesn't have Resume() - call Play() to resume
        _waveOut.Play();
        IsPaused = false;
        IsPlaying = true;
        IsPlayingChanged?.Invoke(this, true);
      }
    }

    public void Seek(double position)
    {
      if (_audioFileReader != null && position >= 0 && position <= Duration)
      {
        _audioFileReader.CurrentTime = TimeSpan.FromSeconds(position);
        PositionChanged?.Invoke(this, Position);
      }
    }

    /// <summary>
    /// Plays a short audio preview snippet from a file at a specific position.
    /// Used for timeline scrubbing preview (IDEA 13).
    /// </summary>
    /// <param name="filePath">Path to audio file</param>
    /// <param name="position">Start position in seconds</param>
    /// <param name="duration">Preview duration in seconds (default 0.15 = 150ms)</param>
    /// <param name="volume">Preview volume (0.0-1.0, default 0.6)</param>
    /// <param name="onPreviewComplete">Optional callback when preview completes</param>
    public async Task PlayPreviewSnippetAsync(
        string filePath,
        double position,
        double duration = 0.15,
        double volume = 0.6,
        Action? onPreviewComplete = null)
    {
      if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
        return;

      // Cancel any existing preview
      StopPreview();

      await Task.Run(() =>
      {
        try
        {
          // Create cancellation token for preview
          _previewCancellation = new System.Threading.CancellationTokenSource();
          var cancellationToken = _previewCancellation.Token;

          // Open audio file
          _previewAudioReader = new NAudio.Wave.AudioFileReader(filePath);

          // Clamp position to valid range
          var totalDuration = _previewAudioReader.TotalTime.TotalSeconds;
          position = Math.Max(0, Math.Min(position, totalDuration - duration));

          // Seek to preview start position
          _previewAudioReader.CurrentTime = TimeSpan.FromSeconds(position);

          // Set preview volume (temporarily store original volume)
          var originalVolume = _previewAudioReader.Volume;
          _previewAudioReader.Volume = (float)Math.Clamp(volume, 0.0, 1.0);

          // Create wave out for preview
          _previewWaveOut = new NAudio.Wave.WaveOutEvent();
          _previewWaveOut.Init(_previewAudioReader);

          var stopTime = position + duration;
          var previewStarted = DateTime.UtcNow;

          _previewWaveOut.PlaybackStopped += (_, _) =>
                {
                  StopPreview();
                  onPreviewComplete?.Invoke();
                };

          // Play preview
          _previewWaveOut.Play();

          // Monitor playback and stop after duration
          _ = Task.Run(async () =>
                {
                  while (!cancellationToken.IsCancellationRequested && _previewAudioReader != null)
                  {
                    var currentTime = _previewAudioReader.CurrentTime.TotalSeconds;
                    if (currentTime >= stopTime || currentTime >= totalDuration)
                    {
                      StopPreview();
                      onPreviewComplete?.Invoke();
                      break;
                    }
                    await Task.Delay(10, cancellationToken); // Check every 10ms
                  }
                }, cancellationToken);
        }
        catch (Exception)
        {
          // Silently fail for preview - don't interrupt user workflow
          StopPreview();
        }
      });
    }

    /// <summary>
    /// Stops any active preview playback without affecting main playback.
    /// </summary>
    public void StopPreview()
    {
      try
      {
        _previewCancellation?.Cancel();

        _previewWaveOut?.Stop();
        _previewWaveOut?.Dispose();
        _previewAudioReader?.Dispose();

        _previewWaveOut = null;
        _previewAudioReader = null;
        _previewCancellation?.Dispose();
        _previewCancellation = null;
      }
      catch (Exception ex)
      {
        ErrorLogger.LogWarning($"Best effort operation failed: {ex.Message}", "AudioPlayerService.StopPreview");
      }
    }

    public void Dispose()
    {
      if (!_disposed)
      {
        _playbackRequestedSubscription?.Dispose();
        _playbackRequestedSubscription = null;
        Stop();
        StopPreview();
        _disposed = true;
      }
    }

    #region Inter-Panel Workflow

    /// <summary>
    /// Handles the PlaybackRequestedEvent from Library or other panels.
    /// Plays the requested audio file.
    /// </summary>
    private async void OnPlaybackRequested(PlaybackRequestedEvent e)
    {
      try
      {
        if (string.IsNullOrEmpty(e.AssetPath))
        {
          System.Diagnostics.Debug.WriteLine("[AudioPlayer] PlaybackRequested: No asset path provided");
          return;
        }

        if (!File.Exists(e.AssetPath))
        {
          System.Diagnostics.Debug.WriteLine($"[AudioPlayer] PlaybackRequested: File not found: {e.AssetPath}");
          return;
        }

        // Play the requested audio file
        await PlayFileAsync(e.AssetPath);
        System.Diagnostics.Debug.WriteLine($"[AudioPlayer] Playing: {e.AssetName ?? e.AssetPath}");
      }
      catch (Exception ex)
      {
        System.Diagnostics.Debug.WriteLine($"[AudioPlayer] PlaybackRequested error: {ex.Message}");
        ErrorLogger.LogError($"Failed to play audio: {ex.Message}", "AudioPlayerService.OnPlaybackRequested");
      }
    }

    #endregion
  }
}