using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VoiceStudio.Core.Models;
using VoiceStudio.Core.Panels;
using VoiceStudio.Core.Services;
using VoiceStudio.App.Utilities;

namespace VoiceStudio.App.ViewModels
{
  /// <summary>
  /// ViewModel for the AssistantView panel - AI production assistant.
  /// </summary>
  public partial class AssistantViewModel : BaseViewModel, IPanelView, IPanelLifecycle
  {
    private readonly IAssistantClient _assistantClient;
    private readonly IProjectsClient _projectsClient;
    private CancellationTokenSource? _loadConversationCts;

    public string PanelId => "assistant";
    public string DisplayName => ResourceHelper.GetString("Panel.Assistant.DisplayName", "AI Production Assistant");
    public PanelRegion Region => PanelRegion.Center;

    [ObservableProperty]
    private ObservableCollection<ConversationItem> conversations = new();

    [ObservableProperty]
    private ConversationItem? selectedConversation;

    [ObservableProperty]
    private ObservableCollection<MessageItem> messages = new();

    [ObservableProperty]
    private string chatMessage = string.Empty;

    [ObservableProperty]
    private string? selectedProjectId;

    [ObservableProperty]
    private ObservableCollection<AssistantProjectItem> availableProjects = new();

    [ObservableProperty]
    private AssistantProjectItem? selectedProject;

    [ObservableProperty]
    private ObservableCollection<TaskSuggestionItem> taskSuggestions = new();

    [ObservableProperty]
    private bool isLoading;

    [ObservableProperty]
    private string? errorMessage;

    [ObservableProperty]
    private string? statusMessage;

    // CS0108 fix: Intentionally hiding base HasError with local ErrorMessage binding
    public new bool HasError => !string.IsNullOrEmpty(ErrorMessage);

    public AssistantViewModel(IViewModelContext context, IAssistantClient assistantClient, IProjectsClient projectsClient)
        : base(context)
    {
      _assistantClient = assistantClient ?? throw new ArgumentNullException(nameof(assistantClient));
      _projectsClient = projectsClient ?? throw new ArgumentNullException(nameof(projectsClient));

      SendMessageCommand = new EnhancedAsyncRelayCommand(async (ct) =>
      {
        using var profiler = PerformanceProfiler.StartCommand("SendMessage");
        await SendMessageAsync(ct);
      });
      LoadConversationsCommand = new EnhancedAsyncRelayCommand(async (ct) =>
      {
        using var profiler = PerformanceProfiler.StartCommand("LoadConversations");
        await LoadConversationsAsync(ct);
      });
      LoadConversationCommand = new EnhancedAsyncRelayCommand(async (ct) =>
      {
        using var profiler = PerformanceProfiler.StartCommand("LoadConversation");
        await LoadConversationAsync(ct);
      });
      DeleteConversationCommand = new EnhancedAsyncRelayCommand(async (ct) =>
      {
        using var profiler = PerformanceProfiler.StartCommand("DeleteConversation");
        await DeleteConversationAsync(ct);
      });
      SuggestTasksCommand = new EnhancedAsyncRelayCommand(async (ct) =>
      {
        using var profiler = PerformanceProfiler.StartCommand("SuggestTasks");
        await SuggestTasksAsync(ct);
      });
      LoadProjectsCommand = new EnhancedAsyncRelayCommand(async (ct) =>
      {
        using var profiler = PerformanceProfiler.StartCommand("LoadProjects");
        await LoadProjectsAsync(ct);
      });
      LoadProjectCommand = new EnhancedAsyncRelayCommand(async (ct) =>
      {
        using var profiler = PerformanceProfiler.StartCommand("LoadProject");
        await LoadProjectAsync(ct);
      }, () => !string.IsNullOrEmpty(SelectedProjectId));
      RefreshCommand = new EnhancedAsyncRelayCommand(async (ct) =>
      {
        using var profiler = PerformanceProfiler.StartCommand("Refresh");
        await RefreshAsync(ct);
      });
    }

    public Task OnActivatedAsync(CancellationToken cancellationToken = default)
    {
      return RefreshAsync(cancellationToken);
    }

    public Task OnDeactivatedAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    public IAsyncRelayCommand SendMessageCommand { get; }
    public IAsyncRelayCommand LoadConversationsCommand { get; }
    public IAsyncRelayCommand LoadConversationCommand { get; }
    public IAsyncRelayCommand DeleteConversationCommand { get; }
    public IAsyncRelayCommand SuggestTasksCommand { get; }
    public IAsyncRelayCommand LoadProjectsCommand { get; }
    public IAsyncRelayCommand LoadProjectCommand { get; }
    public IAsyncRelayCommand RefreshCommand { get; }

    partial void OnSelectedConversationChanged(ConversationItem? value)
    {
      _loadConversationCts?.Cancel();
      _loadConversationCts?.Dispose();
      if (value != null)
      {
        _loadConversationCts = new CancellationTokenSource();
        var cts = _loadConversationCts;
        _ = LoadConversationAsync(cts.Token);
      }
    }

    partial void OnSelectedProjectIdChanged(string? value)
    {
      LoadProjectCommand.NotifyCanExecuteChanged();

      // Update selected project object
      if (!string.IsNullOrEmpty(value))
      {
        SelectedProject = AvailableProjects.FirstOrDefault(p => p.Id == value);
      }
      else
      {
        SelectedProject = null;
      }
    }

    partial void OnSelectedProjectChanged(AssistantProjectItem? value)
    {
      if (value != null && SelectedProjectId != value.Id)
      {
        SelectedProjectId = value.Id;
      }
    }

    private async Task SendMessageAsync(CancellationToken cancellationToken)
    {
      if (string.IsNullOrWhiteSpace(ChatMessage))
      {
        return;
      }

      IsLoading = true;
      ErrorMessage = null;

      try
      {
        var response = await _assistantClient.SendChatAsync(
            SelectedConversation?.ConversationId,
            ChatMessage,
            SelectedProjectId,
            cancellationToken);

        if (response != null)
        {
          // Add user message
          Messages.Add(new MessageItem
          {
            MessageId = $"user-{Guid.NewGuid()}",
            Role = "user",
            Content = ChatMessage,
            Timestamp = DateTime.UtcNow.ToString("O")
          });

          // Add assistant response
          Messages.Add(new MessageItem
          {
            MessageId = response.MessageId,
            Role = "assistant",
            Content = response.Content,
            Suggestions = new ObservableCollection<string>(response.Suggestions),
            Timestamp = response.Timestamp
          });

          // Update or create conversation
          if (SelectedConversation == null)
          {
            SelectedConversation = new ConversationItem
            {
              ConversationId = response.ConversationId,
              Title = ChatMessage.Length > 50 ? ChatMessage.Substring(0, 50) + "..." : ChatMessage,
              Created = DateTime.UtcNow.ToString("O"),
              Updated = response.Timestamp
            };
            Conversations.Insert(0, SelectedConversation);
          }
          else
          {
            SelectedConversation.Updated = response.Timestamp;
          }

          ChatMessage = string.Empty;
          StatusMessage = ResourceHelper.GetString("Assistant.MessageSent", "Message sent");
        }
      }
      catch (Exception ex)
      {
        ErrorMessage = ResourceHelper.FormatString("Assistant.SendMessageFailed", ex.Message);
      }
      finally
      {
        IsLoading = false;
      }
    }

    private async Task LoadConversationsAsync(CancellationToken cancellationToken = default)
    {
      try
      {
        IsLoading = true;
        ErrorMessage = null;

        var conversations = await _assistantClient.GetConversationsAsync(cancellationToken);

        if (conversations != null)
        {
          Conversations.Clear();
          foreach (var conv in conversations.OrderByDescending(c => c.Updated))
          {
            Conversations.Add(new ConversationItem(conv));
          }
        }
      }
      catch (Exception ex)
      {
        ErrorMessage = $"Failed to load conversations: {ex.Message}";
      }
      finally
      {
        IsLoading = false;
      }
    }

    private async Task LoadConversationAsync(CancellationToken cancellationToken = default)
    {
      if (SelectedConversation == null)
      {
        Messages.Clear();
        return;
      }

      try
      {
        IsLoading = true;
        ErrorMessage = null;

        var conversation = await _assistantClient.GetConversationAsync(
            SelectedConversation.ConversationId,
            cancellationToken);

        if (conversation != null)
        {
          Messages.Clear();
          foreach (var msg in conversation.Messages)
          {
            Messages.Add(new MessageItem(msg));
          }
        }
      }
      catch (Exception ex)
      {
        ErrorMessage = ResourceHelper.FormatString("Assistant.LoadConversationFailed", ex.Message);
      }
      finally
      {
        IsLoading = false;
      }
    }

    private async Task DeleteConversationAsync(CancellationToken cancellationToken = default)
    {
      if (SelectedConversation == null)
      {
        ErrorMessage = ResourceHelper.GetString("Assistant.NoConversationSelected", "No conversation selected");
        return;
      }

      try
      {
        IsLoading = true;
        ErrorMessage = null;

        await _assistantClient.DeleteConversationAsync(
            SelectedConversation.ConversationId,
            cancellationToken);

        Conversations.Remove(SelectedConversation);
        SelectedConversation = null;
        Messages.Clear();
        StatusMessage = ResourceHelper.GetString("Assistant.ConversationDeleted", "Conversation deleted");
      }
      catch (Exception ex)
      {
        ErrorMessage = ResourceHelper.FormatString("Assistant.DeleteConversationFailed", ex.Message);
      }
      finally
      {
        IsLoading = false;
      }
    }

    private async Task SuggestTasksAsync(CancellationToken cancellationToken = default)
    {
      if (string.IsNullOrEmpty(SelectedProjectId))
      {
        ErrorMessage = ResourceHelper.GetString("Assistant.ProjectMustBeSelected", "Project must be selected");
        return;
      }

      // Validate project exists
      if (!AvailableProjects.Any(p => p.Id == SelectedProjectId))
      {
        ErrorMessage = ResourceHelper.GetString("Assistant.ProjectDoesNotExist", "Selected project does not exist. Please refresh and select a valid project.");
        SelectedProjectId = null;
        SelectedProject = null;
        return;
      }

      try
      {
        IsLoading = true;
        ErrorMessage = null;

        var suggestions = await _assistantClient.SuggestTasksAsync(SelectedProjectId, cancellationToken);

        if (suggestions != null)
        {
          TaskSuggestions.Clear();
          foreach (var suggestion in suggestions)
          {
            TaskSuggestions.Add(new TaskSuggestionItem(suggestion));
          }
          StatusMessage = ResourceHelper.FormatString("Assistant.TaskSuggestionsGenerated", suggestions.Length);
        }
      }
      catch (OperationCanceledException)
      {
        return; // User cancelled
      }
      catch (Exception ex)
      {
        ErrorMessage = ResourceHelper.FormatString("Assistant.SuggestTasksFailed", ex.Message);
        await HandleErrorAsync(ex, "SuggestTasks");
      }
      finally
      {
        IsLoading = false;
      }
    }

    private async Task LoadProjectsAsync(CancellationToken cancellationToken)
    {
      IsLoading = true;
      ErrorMessage = null;

      try
      {
        var projects = await _projectsClient.GetProjectsAsync(cancellationToken);

        AvailableProjects.Clear();
        foreach (var project in projects)
        {
          AvailableProjects.Add(new AssistantProjectItem
          {
            Id = project.Id,
            Name = project.Name ?? project.Id,
            Description = project.Description,
            Created = project.CreatedAt,
            Modified = project.UpdatedAt
          });
        }

        // Validate selected project still exists
        if (!string.IsNullOrEmpty(SelectedProjectId))
        {
          if (!AvailableProjects.Any(p => p.Id == SelectedProjectId))
          {
            SelectedProjectId = null;
            SelectedProject = null;
            StatusMessage = ResourceHelper.GetString("Assistant.ProjectNoLongerExists", "Previously selected project no longer exists");
          }
          else
          {
            SelectedProject = AvailableProjects.FirstOrDefault(p => p.Id == SelectedProjectId);
          }
        }

        StatusMessage = ResourceHelper.FormatString("Assistant.ProjectsLoaded", AvailableProjects.Count);
      }
      catch (Exception ex)
      {
        ErrorMessage = ResourceHelper.FormatString("Assistant.LoadProjectsFailed", ex.Message);
      }
      finally
      {
        IsLoading = false;
      }
    }

    private async Task LoadProjectAsync(CancellationToken cancellationToken = default)
    {
      if (string.IsNullOrEmpty(SelectedProjectId))
      {
        ErrorMessage = ResourceHelper.GetString("Assistant.NoProjectSelected", "No project selected");
        return;
      }

      // Validate project exists
      if (!AvailableProjects.Any(p => p.Id == SelectedProjectId))
      {
        ErrorMessage = ResourceHelper.GetString("Assistant.ProjectDoesNotExistShort", "Selected project does not exist");
        SelectedProjectId = null;
        SelectedProject = null;
        return;
      }

      try
      {
        IsLoading = true;
        ErrorMessage = null;

        var project = await _projectsClient.GetProjectAsync(SelectedProjectId, cancellationToken);

        if (project != null)
        {
          // Update project details
          var projectItem = AvailableProjects.FirstOrDefault(p => p.Id == SelectedProjectId);
          if (projectItem != null)
          {
            projectItem.Name = project.Name ?? project.Id;
            projectItem.Description = project.Description;
            projectItem.Modified = project.UpdatedAt;
            SelectedProject = projectItem;
          }

          StatusMessage = ResourceHelper.FormatString("Assistant.ProjectLoaded", project.Name ?? project.Id);
        }
        else
        {
          ErrorMessage = ResourceHelper.GetString("Assistant.ProjectNotFound", "Project not found");
          SelectedProjectId = null;
          SelectedProject = null;
        }
      }
      catch (OperationCanceledException)
      {
        return; // User cancelled
      }
      catch (Exception ex)
      {
        ErrorMessage = ResourceHelper.FormatString("Assistant.LoadProjectFailed", ex.Message);
        // If project doesn't exist, clear selection
        if (ex.Message.Contains("not found") || ex.Message.Contains("404"))
        {
          SelectedProjectId = null;
          SelectedProject = null;
        }
        await HandleErrorAsync(ex, "LoadProject");
      }
      finally
      {
        IsLoading = false;
      }
    }

    public async Task RefreshAsync(CancellationToken cancellationToken)
    {
      await LoadConversationsAsync(cancellationToken);
      await LoadProjectsAsync(cancellationToken);
      StatusMessage = ResourceHelper.GetString("Assistant.Refreshed", "Refreshed");
    }

    // Response models
    public class Conversation
    {
      public string ConversationId { get; set; } = string.Empty;
      public string Title { get; set; } = string.Empty;
      public Message[] Messages { get; set; } = Array.Empty<Message>();
      public string Created { get; set; } = string.Empty;
      public string Updated { get; set; } = string.Empty;
    }

    public class Message
    {
      public string MessageId { get; set; } = string.Empty;
      public string ConversationId { get; set; } = string.Empty;
      public string Role { get; set; } = string.Empty;
      public string Content { get; set; } = string.Empty;
      public string Timestamp { get; set; } = string.Empty;
      public string[]? Suggestions { get; set; }
    }

    private class ChatResponse
    {
      public string ConversationId { get; set; } = string.Empty;
      public string MessageId { get; set; } = string.Empty;
      public string Content { get; set; } = string.Empty;
      public string[] Suggestions { get; set; } = Array.Empty<string>();
      public string Timestamp { get; set; } = string.Empty;
    }

    public class TaskSuggestion
    {
      public string TaskId { get; set; } = string.Empty;
      public string Title { get; set; } = string.Empty;
      public string Description { get; set; } = string.Empty;
      public string Category { get; set; } = string.Empty;
      public string Priority { get; set; } = string.Empty;
      public int? EstimatedTime { get; set; }
      public double Confidence { get; set; }
    }
  }

  // Data models
  public class ConversationItem : ObservableObject
  {
    public string ConversationId { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Created { get; set; } = string.Empty;
    public string Updated { get; set; } = string.Empty;

    public ConversationItem(AssistantConversation conversation)
    {
      ConversationId = conversation.ConversationId;
      Title = conversation.Title;
      Created = conversation.Created;
      Updated = conversation.Updated;
    }

    public ConversationItem() { }
  }

  public class MessageItem : ObservableObject
  {
    public string MessageId { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public ObservableCollection<string> Suggestions { get; set; } = new();
    public string Timestamp { get; set; } = string.Empty;
    public bool IsUser => Role == "user";
    public bool IsAssistant => Role == "assistant";

    public MessageItem(AssistantMessage message)
    {
      MessageId = message.MessageId;
      Role = message.Role;
      Content = message.Content;
      Timestamp = message.Timestamp;
      if (message.Suggestions != null)
      {
        Suggestions = new ObservableCollection<string>(message.Suggestions);
      }
    }

    public MessageItem() { }
  }

  public class TaskSuggestionItem : ObservableObject
  {
    public string TaskId { get; set; }
    public string Title { get; set; }
    public string Description { get; set; }
    public string Category { get; set; }
    public string Priority { get; set; }
    public int? EstimatedTime { get; set; }
    public double Confidence { get; set; }
    public string EstimatedTimeDisplay => EstimatedTime.HasValue
        ? ResourceHelper.FormatString("Assistant.EstimatedTime", EstimatedTime.Value)
        : ResourceHelper.GetString("Assistant.NotAvailable", "N/A");
    public string ConfidenceDisplay => $"{Confidence:P0}";

    public TaskSuggestionItem(AssistantTaskSuggestion suggestion)
    {
      TaskId = suggestion.TaskId;
      Title = suggestion.Title;
      Description = suggestion.Description;
      Category = suggestion.Category;
      Priority = suggestion.Priority;
      EstimatedTime = suggestion.EstimatedTime;
      Confidence = suggestion.Confidence;
    }
  }

  public class AssistantProjectItem : ObservableObject
  {
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? Created { get; set; }
    public string? Modified { get; set; }

    public string DisplayName => !string.IsNullOrEmpty(Name) ? Name : Id;
  }
}