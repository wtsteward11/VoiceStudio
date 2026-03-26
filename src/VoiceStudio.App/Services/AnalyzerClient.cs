using System.Threading;
using System.Threading.Tasks;
using VoiceStudio.App.Core.Models;
using VoiceStudio.Core.Models;
using VoiceStudio.Core.Services;

namespace VoiceStudio.App.Services
{
  /// <summary>
  /// Client for audio analysis API. Thin pass-through to IBackendClient.
  /// </summary>
  public sealed class AnalyzerClient : IAnalyzerClient
  {
    private readonly IBackendClient _backend;

    public AnalyzerClient(IBackendClient backend)
    {
      _backend = backend ?? throw new System.ArgumentNullException(nameof(backend));
    }

    /// <inheritdoc />
    public Task<AudioUploadResponse> UploadAudioFileAsync(string filePath, CancellationToken cancellationToken = default)
      => _backend.UploadAudioFileAsync(filePath, cancellationToken);

    /// <inheritdoc />
    public Task<RadarData> GetRadarDataAsync(string audioId, CancellationToken cancellationToken = default)
      => _backend.GetRadarDataAsync(audioId, cancellationToken);

    /// <inheritdoc />
    public Task<LoudnessData> GetLoudnessDataAsync(string audioId, double windowSize = 0.4, CancellationToken cancellationToken = default)
      => _backend.GetLoudnessDataAsync(audioId, windowSize, cancellationToken);

    /// <inheritdoc />
    public Task<PhaseData> GetPhaseDataAsync(string audioId, double windowSize = 0.1, CancellationToken cancellationToken = default)
      => _backend.GetPhaseDataAsync(audioId, windowSize, cancellationToken);
  }
}
