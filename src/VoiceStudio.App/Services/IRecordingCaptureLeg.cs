using System;
using System.Threading;
using System.Threading.Tasks;

namespace VoiceStudio.App.Services;

/// <summary>
/// One executable capture pipeline (GAP-042 Slice 3). Production: <see cref="MicrophoneRecordingCaptureLeg"/>.
/// </summary>
public interface IRecordingCaptureLeg : IDisposable
{
  string TrackId { get; }

  bool IsRecording { get; }

  TimeSpan Duration { get; }

  float CurrentLevel { get; }

  event EventHandler<float>? LevelChanged;

  event EventHandler<string>? Error;

  Task StartAsync(
      string trackId,
      string outputPath,
      int sampleRate,
      int channels,
      int waveInDeviceNumber,
      CancellationToken cancellationToken);

  Task<string> StopRecordingAsync();
}
