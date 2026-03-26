using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace VoiceStudio.Core.Services
{
  /// <summary>
  /// Client for todo panel API.
  /// Use instead of IBackendClient for todo panel.
  /// </summary>
  public interface ITodoPanelClient
  {
    Task<TodoPanelTodo[]?> GetTodosAsync(string? status = null, string? priority = null, string? category = null, string? tag = null, CancellationToken cancellationToken = default);
    Task<TodoPanelTodo?> CreateTodoAsync(TodoCreateRequest request, CancellationToken cancellationToken = default);
    Task<TodoPanelTodo?> UpdateTodoAsync(string todoId, TodoUpdateRequest request, CancellationToken cancellationToken = default);
    Task DeleteTodoAsync(string todoId, CancellationToken cancellationToken = default);
    Task<string[]?> GetCategoriesAsync(CancellationToken cancellationToken = default);
    Task<string[]?> GetTagsAsync(CancellationToken cancellationToken = default);
    Task<TodoSummary?> GetSummaryAsync(CancellationToken cancellationToken = default);
  }

  public class TodoPanelTodo
  {
    public string TodoId { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string Status { get; set; } = string.Empty;
    public string Priority { get; set; } = string.Empty;
    public string? Category { get; set; }
    public string[] Tags { get; set; } = System.Array.Empty<string>();
    public string? DueDate { get; set; }
    public string CreatedAt { get; set; } = string.Empty;
    public string UpdatedAt { get; set; } = string.Empty;
    public string? CompletedAt { get; set; }
  }

  public class TodoCreateRequest
  {
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string Priority { get; set; } = "medium";
    public string? Category { get; set; }
    public List<string> Tags { get; set; } = new();
    public string? DueDate { get; set; }
  }

  public class TodoUpdateRequest
  {
    public string? Title { get; set; }
    public string? Description { get; set; }
    public string? Status { get; set; }
    public string? Priority { get; set; }
    public string? Category { get; set; }
    public List<string>? Tags { get; set; }
    public string? DueDate { get; set; }
  }

  public class TodoSummary
  {
    public int Total { get; set; }
    public Dictionary<string, int> ByStatus { get; set; } = new();
    public Dictionary<string, int> ByPriority { get; set; } = new();
  }
}
