using System.Threading;
using System.Threading.Tasks;
using VoiceStudio.Core.Models;

namespace VoiceStudio.Core.Services
{
  /// <summary>
  /// Client for Real-Time Audio Visualizer API (/api/realtime-visualizer).
  /// Use instead of IBackendClient for RealTimeAudioVisualizer panel.
  /// </summary>
  public interface IRealTimeAudioVisualizerClient
  {
    Task<VisualizerStartResponse?> StartSessionAsync(VisualizerStartRequest request, CancellationToken cancellationToken = default);

    Task StopSessionAsync(string sessionId, CancellationToken cancellationToken = default);

    Task DeleteSessionAsync(string sessionId, CancellationToken cancellationToken = default);
  }
}
