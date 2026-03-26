using System.Threading;
using System.Threading.Tasks;
using VoiceStudio.App.Core.Models;

namespace VoiceStudio.Core.Services
{
  /// <summary>
  /// Client for Pipeline API (/api/pipeline).
  /// Use instead of IBackendClient for PipelineConversation panel.
  /// Exposes WebSocketService for fallback when IWebSocketClientFactory is null.
  /// </summary>
  public interface IPipelineConversationClient
  {
    /// <summary>
    /// WebSocket service for pipeline streaming. Used when IWebSocketClientFactory is null.
    /// </summary>
    IWebSocketService? WebSocketService { get; }

    Task<PipelineProvidersResponse> GetPipelineProvidersAsync(CancellationToken cancellationToken = default);

    Task<PipelineResponse> ProcessPipelineAsync(PipelineRequest request, CancellationToken cancellationToken = default);
  }
}
