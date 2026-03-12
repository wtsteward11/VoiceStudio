using System.Threading;
using System.Threading.Tasks;
using VoiceStudio.Core.Models;

namespace VoiceStudio.Core.Services
{
  /// <summary>
  /// Audio visualization facade. Provides a focused seam for waveform and spectrogram data,
  /// delegating to the backend transport. Use this instead of IBackendClient for visualization
  /// operations to reduce coupling and enable test isolation.
  /// </summary>
  public interface IAudioVisualizationService
  {
    Task<WaveformData> GetWaveformDataAsync(string audioId, int width = 1024, string mode = "peak", CancellationToken cancellationToken = default);
    Task<SpectrogramData> GetSpectrogramDataAsync(string audioId, int width = 512, int height = 256, CancellationToken cancellationToken = default);
  }
}
