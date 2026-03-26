using System;
using System.Threading;
using System.Threading.Tasks;
using VoiceStudio.Core.Models;
using VoiceStudio.Core.Services;

namespace VoiceStudio.App.Services
{
  /// <summary>
  /// Client for AI Production Assistant API. Thin pass-through to IBackendClient.
  /// </summary>
  public sealed class AIProductionAssistantClient : IAIProductionAssistantClient
  {
    private readonly IBackendClient _backend;

    public AIProductionAssistantClient(IBackendClient backend)
    {
      _backend = backend ?? throw new ArgumentNullException(nameof(backend));
    }

    /// <inheritdoc />
    public Task<AIProductionAssistantQueryResponse?> SendQueryAsync(
      AIProductionAssistantQueryRequest request,
      CancellationToken cancellationToken = default)
    {
      return _backend.SendRequestAsync<AIProductionAssistantQueryRequest, AIProductionAssistantQueryResponse>(
        "/api/assistant/query",
        request,
        System.Net.Http.HttpMethod.Post,
        cancellationToken);
    }

    /// <inheritdoc />
    public Task<AIProductionAssistantExecuteResponse?> ExecuteActionAsync(
      AIProductionAssistantExecuteRequest request,
      CancellationToken cancellationToken = default)
    {
      return _backend.SendRequestAsync<AIProductionAssistantExecuteRequest, AIProductionAssistantExecuteResponse>(
        "/api/assistant/execute",
        request,
        System.Net.Http.HttpMethod.Post,
        cancellationToken);
    }

    /// <inheritdoc />
    public Task<AIProductionAssistantContextResponse?> GetContextAsync(
      CancellationToken cancellationToken = default)
    {
      return _backend.SendRequestAsync<object, AIProductionAssistantContextResponse>(
        "/api/assistant/context",
        null,
        System.Net.Http.HttpMethod.Get,
        cancellationToken);
    }
  }
}
