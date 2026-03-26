using System;
using System.Threading;
using System.Threading.Tasks;
using VoiceStudio.App.Core.Models;
using VoiceStudio.Core.Services;

namespace VoiceStudio.App.Services
{
  /// <summary>
  /// Client for Pipeline API. PR-13: owns HTTP via BackendClientHttpPipeline; WebSocketService from IWebSocketService.
  /// </summary>
  public sealed class PipelineConversationClient : IPipelineConversationClient
  {
    private readonly BackendClientHttpPipeline _pipeline;
    private readonly IWebSocketService? _webSocketService;

    /// <summary>
    /// For DI: use BackendHttpContext.Pipeline and IWebSocketService. Tests use this ctor with mock pipeline.
    /// </summary>
    internal PipelineConversationClient(BackendClientHttpPipeline pipeline, IWebSocketService? webSocketService = null)
    {
      _pipeline = pipeline ?? throw new ArgumentNullException(nameof(pipeline));
      _webSocketService = webSocketService;
    }

    /// <inheritdoc />
    public IWebSocketService? WebSocketService => _webSocketService;

    /// <inheritdoc />
    public async Task<PipelineProvidersResponse> GetPipelineProvidersAsync(CancellationToken cancellationToken = default)
    {
      var result = await _pipeline.GetAsync<PipelineProvidersResponse>("/api/pipeline/providers", cancellationToken);
      return result ?? new PipelineProvidersResponse();
    }

    /// <inheritdoc />
    public Task<PipelineResponse> ProcessPipelineAsync(PipelineRequest request, CancellationToken cancellationToken = default)
    {
      return _pipeline.PostAsync<PipelineRequest, PipelineResponse>("/api/pipeline/process", request, cancellationToken);
    }
  }
}
