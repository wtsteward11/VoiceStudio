using System.Threading;
using System.Threading.Tasks;
using VoiceStudio.Core.Models;

namespace VoiceStudio.Core.Services
{
  /// <summary>
  /// Client for Audio Analysis API (/api/audio-analysis).
  /// Use instead of IBackendClient for AudioAnalysis panel.
  /// </summary>
  public interface IAudioAnalysisClient
  {
    Task<AudioAnalysisResult?> GetAnalysisAsync(
      string audioId,
      bool includeSpectral,
      bool includeTemporal,
      bool includePerceptual,
      CancellationToken cancellationToken = default);

    Task<AudioAnalysisQueueResponse?> QueueAnalysisAsync(
      string audioId,
      CancellationToken cancellationToken = default);

    Task<AudioComparisonResponse?> CompareAudioAsync(
      string audioId,
      string referenceAudioId,
      CancellationToken cancellationToken = default);
  }
}
