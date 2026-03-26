using System.Threading;
using System.Threading.Tasks;
using VoiceStudio.Core.Models;

namespace VoiceStudio.Core.Services
{
  /// <summary>
  /// Client for AI Production Assistant API (/api/assistant/query, execute, context).
  /// Use instead of IBackendClient for AIProductionAssistant panel.
  /// </summary>
  public interface IAIProductionAssistantClient
  {
    Task<AIProductionAssistantQueryResponse?> SendQueryAsync(
      AIProductionAssistantQueryRequest request,
      CancellationToken cancellationToken = default);

    Task<AIProductionAssistantExecuteResponse?> ExecuteActionAsync(
      AIProductionAssistantExecuteRequest request,
      CancellationToken cancellationToken = default);

    Task<AIProductionAssistantContextResponse?> GetContextAsync(
      CancellationToken cancellationToken = default);
  }
}
