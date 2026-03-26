using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using VoiceStudio.Core.Services;

namespace VoiceStudio.App.Services
{
  /// <summary>
  /// Client for todo panel API. Thin pass-through to IBackendClient.
  /// </summary>
  public sealed class TodoPanelClient : ITodoPanelClient
  {
    private readonly IBackendClient _backend;

    public TodoPanelClient(IBackendClient backend)
    {
      _backend = backend ?? throw new ArgumentNullException(nameof(backend));
    }

    public async Task<TodoPanelTodo[]?> GetTodosAsync(string? status = null, string? priority = null, string? category = null, string? tag = null, CancellationToken cancellationToken = default)
    {
      var queryParams = new List<string>();
      if (!string.IsNullOrEmpty(status)) queryParams.Add($"status={Uri.EscapeDataString(status)}");
      if (!string.IsNullOrEmpty(priority)) queryParams.Add($"priority={Uri.EscapeDataString(priority)}");
      if (!string.IsNullOrEmpty(category)) queryParams.Add($"category={Uri.EscapeDataString(category)}");
      if (!string.IsNullOrEmpty(tag)) queryParams.Add($"tag={Uri.EscapeDataString(tag)}");
      var url = "/api/todo-panel";
      if (queryParams.Count > 0) url += "?" + string.Join("&", queryParams);
      return await _backend.SendRequestAsync<object, TodoPanelTodo[]>(url, null, HttpMethod.Get, cancellationToken).ConfigureAwait(false);
    }

    public Task<TodoPanelTodo?> CreateTodoAsync(TodoCreateRequest request, CancellationToken cancellationToken = default)
    {
      var body = new
      {
        title = request.Title,
        description = request.Description,
        priority = request.Priority,
        category = request.Category,
        tags = request.Tags,
        due_date = request.DueDate
      };
      return _backend.SendRequestAsync<object, TodoPanelTodo>("/api/todo-panel", body, HttpMethod.Post, cancellationToken);
    }

    public Task<TodoPanelTodo?> UpdateTodoAsync(string todoId, TodoUpdateRequest request, CancellationToken cancellationToken = default)
    {
      var body = new
      {
        title = request.Title,
        description = request.Description,
        status = request.Status,
        priority = request.Priority,
        category = request.Category,
        tags = request.Tags,
        due_date = request.DueDate
      };
      var url = $"/api/todo-panel/{Uri.EscapeDataString(todoId)}";
      return _backend.SendRequestAsync<object, TodoPanelTodo>(url, body, HttpMethod.Put, cancellationToken);
    }

    public Task DeleteTodoAsync(string todoId, CancellationToken cancellationToken = default)
    {
      var url = $"/api/todo-panel/{Uri.EscapeDataString(todoId)}";
      return _backend.SendRequestAsync<object, object>(url, null, HttpMethod.Delete, cancellationToken);
    }

    public Task<string[]?> GetCategoriesAsync(CancellationToken cancellationToken = default) =>
      _backend.SendRequestAsync<object, string[]>("/api/todo-panel/categories/list", null, HttpMethod.Get, cancellationToken);

    public Task<string[]?> GetTagsAsync(CancellationToken cancellationToken = default) =>
      _backend.SendRequestAsync<object, string[]>("/api/todo-panel/tags/list", null, HttpMethod.Get, cancellationToken);

    public Task<TodoSummary?> GetSummaryAsync(CancellationToken cancellationToken = default) =>
      _backend.SendRequestAsync<object, TodoSummary>("/api/todo-panel/stats/summary", null, HttpMethod.Get, cancellationToken);
  }
}
