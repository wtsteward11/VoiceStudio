using System;
using System.Threading;
using System.Threading.Tasks;
using VoiceStudio.Core.Models;
using VoiceStudio.Core.Services;

namespace VoiceStudio.App.Services
{
  /// <summary>
  /// Client for Audio Monitoring Dashboard API. Thin pass-through to IBackendClient.
  /// </summary>
  public sealed class AudioMonitoringDashboardClient : IAudioMonitoringDashboardClient
  {
    private readonly IBackendClient _backend;

    public AudioMonitoringDashboardClient(IBackendClient backend)
    {
      _backend = backend ?? throw new ArgumentNullException(nameof(backend));
    }

    /// <inheritdoc />
    public Task<AudioMeters> GetAudioMetersAsync(string audioId, CancellationToken cancellationToken = default)
      => _backend.GetAudioMetersAsync(audioId, cancellationToken);

    /// <inheritdoc />
    public Task<LoudnessData> GetLoudnessDataAsync(string audioId, double windowSize = 0.4, CancellationToken cancellationToken = default)
      => _backend.GetLoudnessDataAsync(audioId, windowSize, cancellationToken);
  }
}
