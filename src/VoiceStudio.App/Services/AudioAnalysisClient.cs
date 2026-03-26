using System.Threading;
using System.Threading.Tasks;
using VoiceStudio.Core.Models;
using VoiceStudio.Core.Services;

namespace VoiceStudio.App.Services
{
  /// <summary>
  /// Client for Audio Analysis API. Thin pass-through to IBackendClient.
  /// </summary>
  public sealed class AudioAnalysisClient : IAudioAnalysisClient
  {
    private readonly IBackendClient _backend;

    public AudioAnalysisClient(IBackendClient backend)
    {
      _backend = backend ?? throw new ArgumentNullException(nameof(backend));
    }

    /// <inheritdoc />
    public Task<AudioAnalysisResult?> GetAnalysisAsync(
      string audioId,
      bool includeSpectral,
      bool includeTemporal,
      bool includePerceptual,
      CancellationToken cancellationToken = default)
    {
      var query = $"?include_spectral={includeSpectral.ToString().ToLowerInvariant()}&include_temporal={includeTemporal.ToString().ToLowerInvariant()}&include_perceptual={includePerceptual.ToString().ToLowerInvariant()}";
      var url = $"/api/audio-analysis/{Uri.EscapeDataString(audioId ?? "")}{query}";
      return _backend.SendRequestAsync<object, AudioAnalysisResult>(
        url,
        null,
        System.Net.Http.HttpMethod.Get,
        cancellationToken);
    }

    /// <inheritdoc />
    public Task<AudioAnalysisQueueResponse?> QueueAnalysisAsync(
      string audioId,
      CancellationToken cancellationToken = default)
    {
      var url = $"/api/audio-analysis/{Uri.EscapeDataString(audioId ?? "")}/analyze";
      return _backend.SendRequestAsync<object, AudioAnalysisQueueResponse>(
        url,
        null,
        System.Net.Http.HttpMethod.Post,
        cancellationToken);
    }

    /// <inheritdoc />
    public Task<AudioComparisonResponse?> CompareAudioAsync(
      string audioId,
      string referenceAudioId,
      CancellationToken cancellationToken = default)
    {
      var url = $"/api/audio-analysis/{Uri.EscapeDataString(audioId ?? "")}/compare?reference_audio_id={Uri.EscapeDataString(referenceAudioId ?? "")}";
      return _backend.SendRequestAsync<object, AudioComparisonResponse>(
        url,
        null,
        System.Net.Http.HttpMethod.Get,
        cancellationToken);
    }
  }
}
