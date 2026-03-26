using System.Threading;
using System.Threading.Tasks;
using VoiceStudio.Core.Models;

namespace VoiceStudio.Core.Services
{
  /// <summary>
  /// Client for assistant API (/api/assistant).
  /// Use instead of IBackendClient for chat, conversations, suggest-tasks.
  /// </summary>
  public interface IAssistantClient
  {
    Task<AssistantChatResponse?> SendChatAsync(
      string? conversationId,
      string message,
      string? projectId,
      CancellationToken cancellationToken = default);

    Task<AssistantConversation[]?> GetConversationsAsync(CancellationToken cancellationToken = default);

    Task<AssistantConversation?> GetConversationAsync(
      string conversationId,
      CancellationToken cancellationToken = default);

    Task DeleteConversationAsync(string conversationId, CancellationToken cancellationToken = default);

    Task<AssistantTaskSuggestion[]?> SuggestTasksAsync(
      string projectId,
      CancellationToken cancellationToken = default);
  }
}
