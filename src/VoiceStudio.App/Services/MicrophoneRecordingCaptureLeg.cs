using System;
using System.Threading;
using System.Threading.Tasks;

namespace VoiceStudio.App.Services;

/// <summary>
/// Wraps <see cref="MicrophoneRecordingService"/> as a named capture leg for multitrack fan-out.
/// </summary>
public sealed class MicrophoneRecordingCaptureLeg : IRecordingCaptureLeg
{
  private readonly MicrophoneRecordingService _inner;
  private string _trackId = string.Empty;

  public MicrophoneRecordingCaptureLeg(MicrophoneRecordingService inner)
  {
    _inner = inner ?? throw new ArgumentNullException(nameof(inner));
    _inner.LevelChanged += (_, level) => LevelChanged?.Invoke(this, level);
    _inner.RecordingError += (_, msg) => Error?.Invoke(this, msg);
  }

  public string TrackId => _trackId;

  public bool IsRecording => _inner.IsRecording;

  public TimeSpan Duration => _inner.Duration;

  public float CurrentLevel => _inner.CurrentLevel;

  public event EventHandler<float>? LevelChanged;

  public event EventHandler<string>? Error;

  public Task StartAsync(
      string trackId,
      string outputPath,
      int sampleRate,
      int channels,
      int waveInDeviceNumber,
      CancellationToken cancellationToken)
  {
    ArgumentException.ThrowIfNullOrWhiteSpace(trackId);
    _trackId = trackId;
    cancellationToken.ThrowIfCancellationRequested();
    return _inner.StartRecordingAsync(outputPath, sampleRate, channels, waveInDeviceNumber);
  }

  public Task<string> StopRecordingAsync() => _inner.StopRecordingAsync();

  public void Dispose() => _inner.Dispose();
}
