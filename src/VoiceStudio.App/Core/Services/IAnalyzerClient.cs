using System.Threading;
using System.Threading.Tasks;
using VoiceStudio.App.Core.Models;
using VoiceStudio.Core.Models;

namespace VoiceStudio.Core.Services
{
  /// <summary>
  /// Client for audio analysis API. Use instead of IBackendClient for analyzer panel.
  /// </summary>
  public interface IAnalyzerClient
  {
    Task<AudioUploadResponse> UploadAudioFileAsync(string filePath, CancellationToken cancellationToken = default);
    Task<RadarData> GetRadarDataAsync(string audioId, CancellationToken cancellationToken = default);
    Task<LoudnessData> GetLoudnessDataAsync(string audioId, double windowSize = 0.4, CancellationToken cancellationToken = default);
    Task<PhaseData> GetPhaseDataAsync(string audioId, double windowSize = 0.1, CancellationToken cancellationToken = default);
  }
}
