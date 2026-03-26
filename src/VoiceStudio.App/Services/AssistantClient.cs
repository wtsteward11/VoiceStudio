using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using VoiceStudio.Core.Models;
using VoiceStudio.Core.Services;

namespace VoiceStudio.App.Services
{
  /// <summary>
  /// Client for /api/assistant. Thin pass-through to IBackendClient.
  /// </summary>
  public sealed class AssistantClient : IAssistantClient
  {
    private readonly IBackendClient _backend;

    public AssistantClient(IBackendClient backend)
    {
      _backend = backend ?? throw new ArgumentNullException(nameof(backend));
    }

    /// <inheritdoc />
    public Task<AssistantChatResponse?> SendChatAsync(
      string? conversationId,
      string message,
      string? projectId,
      CancellationToken cancellationToken = default)
    {
      var request = new
      {
        conversation_id = conversationId,
        message,
        context = new { project_id = projectId }
      };
      return _backend.SendRequestAsync<object, AssistantChatResponse>(
        "/api/assistant/chat",
        request,
        HttpMethod.Post,
        cancellationToken);
    }

    /// <inheritdoc />
    public Task<AssistantConversation[]?> GetConversationsAsync(CancellationToken cancellationToken = default)
    {
      return _backend.SendRequestAsync<object, AssistantConversation[]>(
        "/api/assistant/conversations",
        null,
        HttpMethod.Get,
        cancellationToken);
    }

    /// <inheritdoc />
    public Task<AssistantConversation?> GetConversationAsync(
      string conversationId,
      CancellationToken cancellationToken = default)
    {
      return _backend.SendRequestAsync<object, AssistantConversation>(
        $"/api/assistant/conversations/{Uri.EscapeDataString(conversationId)}",
        null,
        HttpMethod.Get,
        cancellationToken);
    }

    /// <inheritdoc />
    public Task DeleteConversationAsync(string conversationId, CancellationToken cancellationToken = default)
    {
      return _backend.SendRequestAsync<object, object>(
        $"/api/assistant/conversations/{Uri.EscapeDataString(conversationId)}",
        null,
        HttpMethod.Delete,
        cancellationToken);
    }

    /// <inheritdoc />
    public Task<AssistantTaskSuggestion[]?> SuggestTasksAsync(
      string projectId,
      CancellationToken cancellationToken = default)
    {
      var url = $"/api/assistant/suggest-tasks?project_id={Uri.EscapeDataString(projectId)}";
      return _backend.SendRequestAsync<object, AssistantTaskSuggestion[]>(
        url,
        null,
        HttpMethod.Post,
        cancellationToken);
    }
  }
}
