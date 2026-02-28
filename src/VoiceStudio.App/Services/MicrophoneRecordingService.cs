using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using NAudio.Wave;

namespace VoiceStudio.App.Services
{
  /// <summary>
  /// Microphone recording service using NAudio WaveInEvent.
  /// Captures audio from the system's default input device and saves to WAV files.
  /// 
  /// Core Workflow Audit Remediation - Phase B (H-7).
  /// </summary>
  public sealed class MicrophoneRecordingService : IDisposable
  {
    private WaveInEvent? _waveIn;
    private WaveFileWriter? _writer;
    private string? _currentRecordingPath;
    private DateTime _recordingStartTime;
    private bool _disposed;
    private readonly object _lockObject = new();

    // Recording configuration
    private const int DefaultSampleRate = 44100;
    private const int DefaultBitDepth = 16;
    private const int DefaultChannels = 1;  // Mono for voice recording

    /// <summary>True when actively recording from microphone.</summary>
    public bool IsRecording { get; private set; }

    /// <summary>Duration of the current (or last) recording.</summary>
    public TimeSpan Duration
    {
      get
      {
        if (IsRecording)
        {
          return DateTime.UtcNow - _recordingStartTime;
        }
        return _lastRecordingDuration;
      }
    }
    private TimeSpan _lastRecordingDuration;

    /// <summary>Path to the last completed recording file.</summary>
    public string? LastRecordingPath { get; private set; }

    /// <summary>Current input level (0.0 to 1.0) for VU meter display.</summary>
    public float CurrentLevel { get; private set; }

    /// <summary>Raised when recording starts.</summary>
    public event EventHandler? RecordingStarted;

    /// <summary>Raised when recording stops with the file path.</summary>
    public event EventHandler<RecordingCompletedEventArgs>? RecordingStopped;

    /// <summary>Raised periodically with the current input level (for VU meter).</summary>
    public event EventHandler<float>? LevelChanged;

    /// <summary>Raised if a recording error occurs.</summary>
    public event EventHandler<string>? RecordingError;

    /// <summary>
    /// Start recording from the default microphone.
    /// </summary>
    /// <param name="outputPath">
    /// Optional output file path. If null, a temp file is created
    /// at %TEMP%\voicestudio_recording_{guid}.wav.
    /// </param>
    /// <param name="sampleRate">Sample rate in Hz (default 44100).</param>
    /// <param name="channels">Number of channels (default 1 = mono).</param>
    public Task StartRecordingAsync(
        string? outputPath = null,
        int sampleRate = DefaultSampleRate,
        int channels = DefaultChannels)
    {
      lock (_lockObject)
      {
        if (IsRecording)
        {
          throw new InvalidOperationException("Recording is already in progress.");
        }

        if (_disposed)
        {
          throw new ObjectDisposedException(nameof(MicrophoneRecordingService));
        }

        // Determine output path
        if (string.IsNullOrEmpty(outputPath))
        {
          var tempDir = Path.GetTempPath();
          var guid = Guid.NewGuid().ToString("N")[..8];
          outputPath = Path.Combine(tempDir, $"voicestudio_recording_{guid}.wav");
        }

        _currentRecordingPath = outputPath;

        // Check for available recording devices
        if (WaveInEvent.DeviceCount == 0)
        {
          RecordingError?.Invoke(this, "No microphone found. Please connect a microphone and try again.");
          throw new InvalidOperationException(
              "No audio input devices found. Please connect a microphone.");
        }

        try
        {
          // Configure WaveInEvent for microphone capture
          _waveIn = new WaveInEvent
          {
            DeviceNumber = 0,  // Default input device
            WaveFormat = new WaveFormat(sampleRate, DefaultBitDepth, channels),
            BufferMilliseconds = 50  // 50ms buffer for low latency
          };

          // Create WAV file writer
          _writer = new WaveFileWriter(outputPath, _waveIn.WaveFormat);

          // Wire up data available event
          _waveIn.DataAvailable += WaveIn_DataAvailable;
          _waveIn.RecordingStopped += WaveIn_RecordingStopped;

          // Start recording
          _waveIn.StartRecording();
          _recordingStartTime = DateTime.UtcNow;
          IsRecording = true;

          Debug.WriteLine(
              $"[MicrophoneRecordingService] Recording started: " +
              $"{sampleRate}Hz, {DefaultBitDepth}bit, {channels}ch -> {outputPath}");

          RecordingStarted?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
          // Clean up on failure
          CleanupRecordingResources();
          Debug.WriteLine($"[MicrophoneRecordingService] Failed to start recording: {ex.Message}");
          RecordingError?.Invoke(this, $"Failed to start recording: {ex.Message}");
          throw;
        }
      }

      return Task.CompletedTask;
    }

    /// <summary>
    /// Stop the current recording and finalize the WAV file.
    /// </summary>
    /// <returns>Path to the recorded WAV file.</returns>
    public Task<string> StopRecordingAsync()
    {
      lock (_lockObject)
      {
        if (!IsRecording)
        {
          throw new InvalidOperationException("No recording is in progress.");
        }

        try
        {
          _waveIn?.StopRecording();
          // The WaveIn_RecordingStopped event handler will finalize
        }
        catch (Exception ex)
        {
          Debug.WriteLine($"[MicrophoneRecordingService] Error stopping recording: {ex.Message}");
          CleanupRecordingResources();
          throw;
        }
      }

      var path = _currentRecordingPath ?? string.Empty;
      return Task.FromResult(path);
    }

    private void WaveIn_DataAvailable(object? sender, WaveInEventArgs e)
    {
      // Write audio data to WAV file
      lock (_lockObject)
      {
        if (_writer != null && IsRecording)
        {
          _writer.Write(e.Buffer, 0, e.BytesRecorded);

          // Calculate RMS level for VU meter
          float maxLevel = 0;
          for (int i = 0; i < e.BytesRecorded - 1; i += 2)
          {
            short sample = (short)(e.Buffer[i] | (e.Buffer[i + 1] << 8));
            float sampleFloat = Math.Abs(sample / 32768f);
            if (sampleFloat > maxLevel)
            {
              maxLevel = sampleFloat;
            }
          }

          CurrentLevel = maxLevel;
          LevelChanged?.Invoke(this, maxLevel);
        }
      }
    }

    private void WaveIn_RecordingStopped(object? sender, StoppedEventArgs e)
    {
      lock (_lockObject)
      {
        _lastRecordingDuration = DateTime.UtcNow - _recordingStartTime;
        IsRecording = false;
        LastRecordingPath = _currentRecordingPath;

        // Finalize and close the WAV file
        CleanupRecordingResources();

        if (e.Exception != null)
        {
          Debug.WriteLine($"[MicrophoneRecordingService] Recording error: {e.Exception.Message}");
          RecordingError?.Invoke(this, $"Recording error: {e.Exception.Message}");
        }
        else
        {
          Debug.WriteLine(
              $"[MicrophoneRecordingService] Recording stopped: " +
              $"{_lastRecordingDuration.TotalSeconds:F1}s -> {LastRecordingPath}");

          RecordingStopped?.Invoke(this, new RecordingCompletedEventArgs(
              LastRecordingPath ?? string.Empty,
              _lastRecordingDuration));
        }
      }
    }

    private void CleanupRecordingResources()
    {
      try
      {
        _writer?.Dispose();
        _writer = null;
      }
      catch (Exception ex)
      {
        Debug.WriteLine($"[MicrophoneRecordingService] Writer cleanup error: {ex.Message}");
      }

      try
      {
        if (_waveIn != null)
        {
          _waveIn.DataAvailable -= WaveIn_DataAvailable;
          _waveIn.RecordingStopped -= WaveIn_RecordingStopped;
          _waveIn.Dispose();
          _waveIn = null;
        }
      }
      catch (Exception ex)
      {
        Debug.WriteLine($"[MicrophoneRecordingService] WaveIn cleanup error: {ex.Message}");
      }
    }

    public void Dispose()
    {
      if (_disposed)
        return;

      _disposed = true;

      if (IsRecording)
      {
        try
        {
          _waveIn?.StopRecording();
        }
        catch (Exception ex)
        {
          Debug.WriteLine($"[MicrophoneRecordingService] Dispose stop error: {ex.Message}");
        }
      }

      CleanupRecordingResources();
    }
  }

  /// <summary>Event args for recording completion.</summary>
  public sealed class RecordingCompletedEventArgs : EventArgs
  {
    public string FilePath { get; }
    public TimeSpan Duration { get; }

    public RecordingCompletedEventArgs(string filePath, TimeSpan duration)
    {
      FilePath = filePath;
      Duration = duration;
    }
  }
}
