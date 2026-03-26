using System.Threading;
using System.Threading.Tasks;
using VoiceStudio.Core.Models;

namespace VoiceStudio.Core.Services
{
  /// <summary>
  /// Client for Audio Monitoring Dashboard API (audio meters, loudness).
  /// Use instead of IBackendClient for AudioMonitoringDashboard panel.
  /// </summary>
  public interface IAudioMonitoringDashboardClient
  {
    Task<AudioMeters> GetAudioMetersAsync(string audioId, CancellationToken cancellationToken = default);

    Task<LoudnessData> GetLoudnessDataAsync(string audioId, double windowSize = 0.4, CancellationToken cancellationToken = default);
  }
}
