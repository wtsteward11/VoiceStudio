using System.Threading;
using System.Threading.Tasks;
using VoiceStudio.Core.Models;
using VoiceStudio.Core.Services;

namespace VoiceStudio.App.Services
{
  /// <summary>
  /// Client for Real-Time Audio Visualizer API. Thin pass-through to IBackendClient.
  /// </summary>
  public sealed class RealTimeAudioVisualizerClient : IRealTimeAudioVisualizerClient
  {
    private readonly IBackendClient _backend;

    public RealTimeAudioVisualizerClient(IBackendClient backend)
    {
      _backend = backend ?? throw new System.ArgumentNullException(nameof(backend));
    }

    /// <inheritdoc />
    public Task<VisualizerStartResponse?> StartSessionAsync(VisualizerStartRequest request, CancellationToken cancellationToken = default)
    {
      var body = new
      {
        visualization_type = request?.VisualizationType ?? "both",
        update_rate = request?.UpdateRate ?? 30.0,
        fft_size = request?.FftSize ?? 2048,
        window_type = request?.WindowType ?? "hann",
        show_phase = request?.ShowPhase ?? false,
        color_scheme = request?.ColorScheme ?? "default"
      };
      return _backend.SendRequestAsync<object, VisualizerStartResponse>(
        "/api/realtime-visualizer/start",
        body,
        System.Net.Http.HttpMethod.Post,
        cancellationToken);
    }

    /// <inheritdoc />
    public Task StopSessionAsync(string sessionId, CancellationToken cancellationToken = default)
    {
      var url = $"/api/realtime-visualizer/{System.Uri.EscapeDataString(sessionId ?? "")}/stop";
      return _backend.SendRequestAsync<object, object>(
        url,
        null,
        System.Net.Http.HttpMethod.Post,
        cancellationToken);
    }

    /// <inheritdoc />
    public Task DeleteSessionAsync(string sessionId, CancellationToken cancellationToken = default)
    {
      var url = $"/api/realtime-visualizer/{System.Uri.EscapeDataString(sessionId ?? "")}";
      return _backend.SendRequestAsync<object, object>(
        url,
        null,
        System.Net.Http.HttpMethod.Delete,
        cancellationToken);
    }
  }
}
