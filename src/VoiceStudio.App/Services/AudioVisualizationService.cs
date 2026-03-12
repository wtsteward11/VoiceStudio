using System.Threading;
using System.Threading.Tasks;
using VoiceStudio.Core.Models;
using VoiceStudio.Core.Services;

namespace VoiceStudio.App.Services
{
  /// <summary>
  /// Audio visualization service. Delegates to IBackendClient for waveform and spectrogram data.
  /// </summary>
  public sealed class AudioVisualizationService : IAudioVisualizationService
  {
    private readonly IBackendClient _backend;

    public AudioVisualizationService(IBackendClient backend)
    {
      _backend = backend ?? throw new System.ArgumentNullException(nameof(backend));
    }

    public Task<WaveformData> GetWaveformDataAsync(string audioId, int width = 1024, string mode = "peak", CancellationToken cancellationToken = default)
      => _backend.GetWaveformDataAsync(audioId, width, mode, cancellationToken);

    public Task<SpectrogramData> GetSpectrogramDataAsync(string audioId, int width = 512, int height = 256, CancellationToken cancellationToken = default)
      => _backend.GetSpectrogramDataAsync(audioId, width, height, cancellationToken);
  }
}
