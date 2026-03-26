using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VoiceStudio.Core.Panels;
using VoiceStudio.Core.Services;
using VoiceStudio.App.Services;
using VoiceStudio.App.Utilities;

namespace VoiceStudio.App.ViewModels
{
  /// <summary>
  /// ViewModel for the TodoPanelView panel - Todo/task management.
  /// </summary>
  public partial class TodoPanelViewModel : BaseViewModel, IPanelView, IPanelLifecycle
  {
    private readonly ITodoPanelClient _todoPanelClient;
    private readonly IErrorPresentationService? _errorService;
    private readonly IErrorLoggingService? _logService;

    public string PanelId => PanelIds.TodoPanel;
    public string DisplayName => ResourceHelper.GetString("Panel.TodoPanel.DisplayName", "Todo Panel");
    public PanelRegion Region => PanelRegion.Center;

    [ObservableProperty]
    private ObservableCollection<TodoItem> todos = new();

    [ObservableProperty]
    private TodoItem? selectedTodo;

    [ObservableProperty]
    private ObservableCollection<string> availableCategories = new();

    [ObservableProperty]
    private ObservableCollection<string> availableTags = new();

    [ObservableProperty]
    private string? newTodoTitle;

    [ObservableProperty]
    private string? newTodoDescription;

    [ObservableProperty]
    private string newTodoPriority = "medium";

    [ObservableProperty]
    private ObservableCollection<string> availablePriorities = new() { "low", "medium", "high", "urgent" };

    [ObservableProperty]
    private ObservableCollection<string> availableStatuses = new() { "pending", "in_progress", "completed", "cancelled" };

    [ObservableProperty]
    private string? newTodoCategory;

    [ObservableProperty]
    private string? newTodoTags;

    [ObservableProperty]
    private string? newTodoDueDate;

    [ObservableProperty]
    private string? filterStatus;

    [ObservableProperty]
    private string? filterPriority;

    [ObservableProperty]
    private string? filterCategory;

    [ObservableProperty]
    private string? filterTag;

    [ObservableProperty]
    private TodoSummaryItem? summary;

    [ObservableProperty]
    private bool isCreatingTodo;

    public TodoPanelViewModel(IViewModelContext context, ITodoPanelClient todoPanelClient)
        : base(context)
    {
      _todoPanelClient = todoPanelClient ?? throw new ArgumentNullException(nameof(todoPanelClient));

      // Get error services
      _errorService = ServiceProvider.TryGetErrorPresentationService();
      _logService = ServiceProvider.TryGetErrorLoggingService();

      LoadTodosCommand = new EnhancedAsyncRelayCommand(async (ct) =>
      {
        using var profiler = PerformanceProfiler.StartCommand("LoadTodos");
        await LoadTodosAsync(ct);
      });

      CreateTodoCommand = new EnhancedAsyncRelayCommand(async (ct) =>
      {
        using var profiler = PerformanceProfiler.StartCommand("CreateTodo");
        await CreateTodoAsync(ct);
      }, () => !IsCreatingTodo && !string.IsNullOrWhiteSpace(NewTodoTitle));

      UpdateTodoCommand = new EnhancedAsyncRelayCommand(async (ct) =>
      {
        using var profiler = PerformanceProfiler.StartCommand("UpdateTodo");
        await UpdateTodoAsync(ct);
      }, () => SelectedTodo != null);

      DeleteTodoCommand = new EnhancedAsyncRelayCommand(async (ct) =>
      {
        using var profiler = PerformanceProfiler.StartCommand("DeleteTodo");
        await DeleteTodoAsync(ct);
      }, () => SelectedTodo != null);

      LoadCategoriesCommand = new EnhancedAsyncRelayCommand(async (ct) =>
      {
        using var profiler = PerformanceProfiler.StartCommand("LoadCategories");
        await LoadCategoriesAsync(ct);
      });

      LoadTagsCommand = new EnhancedAsyncRelayCommand(async (ct) =>
      {
        using var profiler = PerformanceProfiler.StartCommand("LoadTags");
        await LoadTagsAsync(ct);
      });

      LoadSummaryCommand = new EnhancedAsyncRelayCommand(async (ct) =>
      {
        using var profiler = PerformanceProfiler.StartCommand("LoadSummary");
        await LoadSummaryAsync(ct);
      });

      RefreshCommand = new EnhancedAsyncRelayCommand(async (ct) =>
      {
        using var profiler = PerformanceProfiler.StartCommand("Refresh");
        await RefreshAsync(ct);
      });
    }

    /// <inheritdoc />
    public Task OnActivatedAsync(CancellationToken cancellationToken = default) => RefreshAsync(cancellationToken);

    /// <inheritdoc />
    public Task OnDeactivatedAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    /// <inheritdoc />
    public Task RefreshAsync(CancellationToken cancellationToken = default) => RefreshAsyncImpl(cancellationToken);

    public IAsyncRelayCommand LoadTodosCommand { get; }
    public IAsyncRelayCommand CreateTodoCommand { get; }
    public IAsyncRelayCommand UpdateTodoCommand { get; }
    public IAsyncRelayCommand DeleteTodoCommand { get; }
    public IAsyncRelayCommand LoadCategoriesCommand { get; }
    public IAsyncRelayCommand LoadTagsCommand { get; }
    public IAsyncRelayCommand LoadSummaryCommand { get; }
    public IAsyncRelayCommand RefreshCommand { get; }

    partial void OnIsCreatingTodoChanged(bool value)
    {
      CreateTodoCommand.NotifyCanExecuteChanged();
    }

    partial void OnNewTodoTitleChanged(string? value)
    {
      CreateTodoCommand.NotifyCanExecuteChanged();
    }

    partial void OnSelectedTodoChanged(TodoItem? value)
    {
      UpdateTodoCommand.NotifyCanExecuteChanged();
      DeleteTodoCommand.NotifyCanExecuteChanged();
    }

    partial void OnFilterStatusChanged(string? value)
    {
      var ct = new CancellationTokenSource(TimeSpan.FromSeconds(30)).Token;
      _ = LoadTodosAsync(ct).ContinueWith(t =>
      {
        if (t.IsFaulted)
          _logService?.LogError(t.Exception?.InnerException ?? new Exception("LoadTodos failed"), "LoadTodos");
      }, TaskScheduler.Default);
    }

    partial void OnFilterPriorityChanged(string? value)
    {
      var ct = new CancellationTokenSource(TimeSpan.FromSeconds(30)).Token;
      _ = LoadTodosAsync(ct).ContinueWith(t =>
      {
        if (t.IsFaulted)
          _logService?.LogError(t.Exception?.InnerException ?? new Exception("LoadTodos failed"), "LoadTodos");
      }, TaskScheduler.Default);
    }

    partial void OnFilterCategoryChanged(string? value)
    {
      var ct = new CancellationTokenSource(TimeSpan.FromSeconds(30)).Token;
      _ = LoadTodosAsync(ct).ContinueWith(t =>
      {
        if (t.IsFaulted)
          _logService?.LogError(t.Exception?.InnerException ?? new Exception("LoadTodos failed"), "LoadTodos");
      }, TaskScheduler.Default);
    }

    partial void OnFilterTagChanged(string? value)
    {
      var ct = new CancellationTokenSource(TimeSpan.FromSeconds(30)).Token;
      _ = LoadTodosAsync(ct).ContinueWith(t =>
      {
        if (t.IsFaulted)
          _logService?.LogError(t.Exception?.InnerException ?? new Exception("LoadTodos failed"), "LoadTodos");
      }, TaskScheduler.Default);
    }

    private async Task LoadTodosAsync(CancellationToken cancellationToken)
    {
      try
      {
        IsLoading = true;
        ErrorMessage = null;

        var todos = await _todoPanelClient.GetTodosAsync(FilterStatus, FilterPriority, FilterCategory, FilterTag, cancellationToken);

        if (todos != null)
        {
          Todos.Clear();
          foreach (var todo in todos)
          {
            Todos.Add(new TodoItem(
                todo.TodoId,
                todo.Title,
                todo.Description,
                todo.Status,
                todo.Priority,
                todo.Category,
                todo.Tags,
                todo.DueDate,
                todo.CreatedAt,
                todo.UpdatedAt,
                todo.CompletedAt
            ));
          }
        }
      }
      catch (OperationCanceledException)
      {
        // User cancelled - expected
        return;
      }
      catch (Exception ex)
      {
        ErrorMessage = ErrorHandler.GetUserFriendlyMessage(ex);
        _errorService?.ShowError(ex, "Failed to load todos");
        _logService?.LogError(ex, "LoadTodos");
      }
      finally
      {
        IsLoading = false;
      }
    }

    private async Task CreateTodoAsync(CancellationToken cancellationToken)
    {
      if (string.IsNullOrWhiteSpace(NewTodoTitle))
      {
        ErrorMessage = ResourceHelper.GetString("TodoPanel.TitleRequired", "Title is required");
        return;
      }

      try
      {
        IsCreatingTodo = true;
        ErrorMessage = null;

        var tags = new List<string>();
        if (!string.IsNullOrWhiteSpace(NewTodoTags))
        {
          tags = NewTodoTags.Split(',', StringSplitOptions.RemoveEmptyEntries)
              .Select(t => t.Trim())
              .Where(t => !string.IsNullOrWhiteSpace(t))
              .ToList();
        }

        var request = new VoiceStudio.Core.Services.TodoCreateRequest
        {
          Title = NewTodoTitle,
          Description = NewTodoDescription,
          Priority = NewTodoPriority,
          Category = NewTodoCategory,
          Tags = tags,
          DueDate = NewTodoDueDate
        };

        var todo = await _todoPanelClient.CreateTodoAsync(request, cancellationToken);

        if (todo != null)
        {
          Todos.Add(new TodoItem(
              todo.TodoId,
              todo.Title,
              todo.Description,
              todo.Status,
              todo.Priority,
              todo.Category,
              todo.Tags,
              todo.DueDate,
              todo.CreatedAt,
              todo.UpdatedAt,
              todo.CompletedAt
          ));

          // Clear form
          NewTodoTitle = null;
          NewTodoDescription = null;
          NewTodoPriority = "medium";
          NewTodoCategory = null;
          NewTodoTags = null;
          NewTodoDueDate = null;

          StatusMessage = ResourceHelper.GetString("TodoPanel.TodoCreated", "Todo created successfully");

          // Refresh categories, tags, and summary
          await LoadCategoriesAsync(cancellationToken);
          await LoadTagsAsync(cancellationToken);
          await LoadSummaryAsync(cancellationToken);
        }
      }
      catch (OperationCanceledException)
      {
        // User cancelled - expected
        return;
      }
      catch (Exception ex)
      {
        ErrorMessage = ErrorHandler.GetUserFriendlyMessage(ex);
        _errorService?.ShowError(ex, "Failed to create todo");
        _logService?.LogError(ex, "CreateTodo");
      }
      finally
      {
        IsCreatingTodo = false;
      }
    }

    private async Task UpdateTodoAsync(CancellationToken cancellationToken)
    {
      if (SelectedTodo == null)
      {
        return;
      }

      IsLoading = true;
      ErrorMessage = null;

      try
      {
        var request = new VoiceStudio.Core.Services.TodoUpdateRequest
        {
          Title = SelectedTodo.Title,
          Description = SelectedTodo.Description,
          Status = SelectedTodo.Status,
          Priority = SelectedTodo.Priority,
          Category = SelectedTodo.Category,
          Tags = SelectedTodo.Tags.ToList(),
          DueDate = SelectedTodo.DueDate
        };

        var todo = await _todoPanelClient.UpdateTodoAsync(SelectedTodo.TodoId, request, cancellationToken);

        if (todo != null)
        {
          var index = Todos.IndexOf(SelectedTodo);
          if (index >= 0)
          {
            Todos[index] = new TodoItem(
                todo.TodoId,
                todo.Title,
                todo.Description,
                todo.Status,
                todo.Priority,
                todo.Category,
                todo.Tags,
                todo.DueDate,
                todo.CreatedAt,
                todo.UpdatedAt,
                todo.CompletedAt
            );
          }

          StatusMessage = ResourceHelper.GetString("TodoPanel.TodoUpdated", "Todo updated successfully");
          await LoadSummaryAsync(cancellationToken);
        }
      }
      catch (OperationCanceledException)
      {
        // User cancelled - expected
        return;
      }
      catch (Exception ex)
      {
        ErrorMessage = ErrorHandler.GetUserFriendlyMessage(ex);
        _errorService?.ShowError(ex, ResourceHelper.GetString("TodoPanel.UpdateTodoFailed", "Failed to update todo"));
        _logService?.LogError(ex, "UpdateTodo");
      }
      finally
      {
        IsLoading = false;
      }
    }

    private async Task DeleteTodoAsync(CancellationToken cancellationToken)
    {
      if (SelectedTodo == null)
      {
        return;
      }

      IsLoading = true;
      ErrorMessage = null;

      try
      {
        await _todoPanelClient.DeleteTodoAsync(SelectedTodo.TodoId, cancellationToken);

        Todos.Remove(SelectedTodo);
        SelectedTodo = null;

        StatusMessage = ResourceHelper.GetString("TodoPanel.TodoDeleted", "Todo deleted successfully");
        await LoadSummaryAsync(cancellationToken);
      }
      catch (OperationCanceledException)
      {
        // User cancelled - expected
        return;
      }
      catch (Exception ex)
      {
        ErrorMessage = ErrorHandler.GetUserFriendlyMessage(ex);
        _errorService?.ShowError(ex, "Failed to delete todo");
        _logService?.LogError(ex, "DeleteTodo");
      }
      finally
      {
        IsLoading = false;
      }
    }

    private async Task LoadCategoriesAsync(CancellationToken cancellationToken)
    {
      try
      {
        var categories = await _todoPanelClient.GetCategoriesAsync(cancellationToken);

        if (categories != null)
        {
          AvailableCategories.Clear();
          foreach (var category in categories)
          {
            AvailableCategories.Add(category);
          }
        }
      }
      catch (OperationCanceledException)
      {
        // User cancelled - expected
        return;
      }
      catch (Exception ex)
      {
        ErrorMessage = ErrorHandler.GetUserFriendlyMessage(ex);
        _errorService?.ShowError(ex, "Failed to load categories");
        _logService?.LogError(ex, "LoadCategories");
      }
    }

    private async Task LoadTagsAsync(CancellationToken cancellationToken)
    {
      try
      {
        var tags = await _todoPanelClient.GetTagsAsync(cancellationToken);

        if (tags != null)
        {
          AvailableTags.Clear();
          foreach (var tag in tags)
          {
            AvailableTags.Add(tag);
          }
        }
      }
      catch (Exception ex)
      {
        ErrorMessage = $"Failed to load tags: {ex.Message}";
      }
    }

    private async Task LoadSummaryAsync(CancellationToken cancellationToken)
    {
      try
      {
        var summary = await _todoPanelClient.GetSummaryAsync(cancellationToken);

        if (summary != null)
        {
          Summary = new TodoSummaryItem(
              summary.Total,
              summary.ByStatus,
              summary.ByPriority
          );
        }
      }
      catch (OperationCanceledException)
      {
        // User cancelled - expected
        return;
      }
      catch (Exception ex)
      {
        ErrorMessage = ErrorHandler.GetUserFriendlyMessage(ex);
        _errorService?.ShowError(ex, "Failed to load summary");
        _logService?.LogError(ex, "LoadSummary");
      }
    }

    private async Task RefreshAsyncImpl(CancellationToken cancellationToken)
    {
      await LoadTodosAsync(cancellationToken);
      await LoadCategoriesAsync(cancellationToken);
      await LoadTagsAsync(cancellationToken);
      await LoadSummaryAsync(cancellationToken);
      StatusMessage = ResourceHelper.GetString("TodoPanel.Refreshed", "Refreshed");
    }

    // Request models
    private class TodoCreateRequest
    {
      public string Title { get; set; } = string.Empty;
      public string? Description { get; set; }
      public string Priority { get; set; } = "medium";
      public string? Category { get; set; }
      public List<string> Tags { get; set; } = new();
      public string? DueDate { get; set; }
    }

    private class TodoUpdateRequest
    {
      public string? Title { get; set; }
      public string? Description { get; set; }
      public string? Status { get; set; }
      public string? Priority { get; set; }
      public string? Category { get; set; }
      public List<string>? Tags { get; set; }
      public string? DueDate { get; set; }
    }

    private class Todo
    {
      public string TodoId { get; set; } = string.Empty;
      public string Title { get; set; } = string.Empty;
      public string? Description { get; set; }
      public string Status { get; set; } = string.Empty;
      public string Priority { get; set; } = string.Empty;
      public string? Category { get; set; }
      public string[] Tags { get; set; } = Array.Empty<string>();
      public string? DueDate { get; set; }
      public string CreatedAt { get; set; } = string.Empty;
      public string UpdatedAt { get; set; } = string.Empty;
      public string? CompletedAt { get; set; }
    }

    private class TodoSummary
    {
      public int Total { get; set; }
      public Dictionary<string, int> ByStatus { get; set; } = new();
      public Dictionary<string, int> ByPriority { get; set; } = new();
    }
  }

  // Data models
  public class TodoItem : ObservableObject
  {
    public string TodoId { get; set; }
    public string Title { get; set; }
    public string? Description { get; set; }
    public string Status { get; set; }
    public string Priority { get; set; }
    public string? Category { get; set; }
    public string[] Tags { get; set; }
    public string? DueDate { get; set; }
    public string CreatedAt { get; set; }
    public string UpdatedAt { get; set; }
    public string? CompletedAt { get; set; }

    public string StatusDisplay => Status.Replace("_", " ").ToUpper();
    public string PriorityDisplay => Priority.ToUpper();
    public string TagsDisplay => Tags?.Length > 0 ? string.Join(", ", Tags) : "No tags";
    public string DueDateDisplay => DueDate != null ? FormatDate(DueDate) : "No due date";
    public string CreatedAtDisplay => FormatDate(CreatedAt);

    public TodoItem(string todoId, string title, string? description, string status, string priority, string? category, string[] tags, string? dueDate, string createdAt, string updatedAt, string? completedAt)
    {
      TodoId = todoId;
      Title = title;
      Description = description;
      Status = status;
      Priority = priority;
      Category = category;
      Tags = tags ?? Array.Empty<string>();
      DueDate = dueDate;
      CreatedAt = createdAt;
      UpdatedAt = updatedAt;
      CompletedAt = completedAt;
    }

    private static string FormatDate(string isoString)
    {
      if (DateTime.TryParse(isoString, out var dateTime))
      {
        return dateTime.ToString("yyyy-MM-dd HH:mm");
      }
      return isoString;
    }
  }

  public class TodoSummaryItem : ObservableObject
  {
    public int Total { get; set; }
    public int Pending { get; set; }
    public int InProgress { get; set; }
    public int Completed { get; set; }
    public int Cancelled { get; set; }
    public int Low { get; set; }
    public int Medium { get; set; }
    public int High { get; set; }
    public int Urgent { get; set; }

    public TodoSummaryItem(int total, Dictionary<string, int> byStatus, Dictionary<string, int> byPriority)
    {
      Total = total;
      Pending = byStatus.GetValueOrDefault("pending", 0);
      InProgress = byStatus.GetValueOrDefault("in_progress", 0);
      Completed = byStatus.GetValueOrDefault("completed", 0);
      Cancelled = byStatus.GetValueOrDefault("cancelled", 0);
      Low = byPriority.GetValueOrDefault("low", 0);
      Medium = byPriority.GetValueOrDefault("medium", 0);
      High = byPriority.GetValueOrDefault("high", 0);
      Urgent = byPriority.GetValueOrDefault("urgent", 0);
    }
  }
}