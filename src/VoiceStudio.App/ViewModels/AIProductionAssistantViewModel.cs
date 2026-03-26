using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Xaml;
using VoiceStudio.Core.Models;
using VoiceStudio.Core.Panels;
using VoiceStudio.Core.Services;
using VoiceStudio.App.Utilities;

namespace VoiceStudio.App.ViewModels
{
  /// <summary>
  /// ViewModel for the AIProductionAssistantView panel - AI-driven helper with natural language interface.
  /// </summary>
  public partial class AIProductionAssistantViewModel : BaseViewModel, IPanelView
  {
    private readonly IAIProductionAssistantClient _client;
    private ObservableCollection<ChatMessageItem>? _messagesHooked;
    private ObservableCollection<string>? _suggestionsHooked;

    public string PanelId => PanelIds.AIProductionAssistant;
    public string DisplayName => ResourceHelper.GetString("Panel.AIProductionAssistant.DisplayName", "AI Assistant");
    public PanelRegion Region => PanelRegion.Right;

    [ObservableProperty]
    private ObservableCollection<ChatMessageItem> messages = new();

    [ObservableProperty]
    private string? currentQuery;

    [ObservableProperty]
    private string? currentSessionId;

    [ObservableProperty]
    private ObservableCollection<string> suggestions = new();

    [ObservableProperty]
    private bool isProcessing;

    [ObservableProperty]
    private AssistantContextItem? appContext;

    public Visibility ErrorMessageVisibility =>
        string.IsNullOrWhiteSpace(ErrorMessage) ? Visibility.Collapsed : Visibility.Visible;

    public Visibility WelcomeVisibility =>
        Messages.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

    public Visibility SuggestionsVisibility =>
        Suggestions.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

    public AIProductionAssistantViewModel(IViewModelContext context, IAIProductionAssistantClient client)
        : base(context)
    {
      _client = client ?? throw new ArgumentNullException(nameof(client));

      SendQueryCommand = new EnhancedAsyncRelayCommand(async (ct) =>
      {
        using var profiler = PerformanceProfiler.StartCommand("SendQuery");
        await SendQueryAsync(ct);
      }, () => !string.IsNullOrWhiteSpace(CurrentQuery) && !IsProcessing && !IsLoading);
      ExecuteActionCommand = new EnhancedAsyncRelayCommand(async (ct) =>
      {
        using var profiler = PerformanceProfiler.StartCommand("ExecuteAction");
        await ExecuteActionAsync(ct);
      }, () => SelectedMessage?.HasAction == true && !IsLoading);
      LoadContextCommand = new EnhancedAsyncRelayCommand(async (ct) =>
      {
        using var profiler = PerformanceProfiler.StartCommand("LoadContext");
        await LoadContextAsync(ct);
      }, () => !IsLoading);
      ClearChatCommand = new EnhancedAsyncRelayCommand(async (ct) =>
      {
        using var profiler = PerformanceProfiler.StartCommand("ClearChat");
        await ClearChatAsync(ct);
      }, () => !IsLoading);
      RefreshCommand = new EnhancedAsyncRelayCommand(async (ct) =>
      {
        using var profiler = PerformanceProfiler.StartCommand("Refresh");
        await RefreshAsync(ct);
      }, () => !IsLoading);

      HookMessagesCollection(Messages);
      HookSuggestionsCollection(Suggestions);

      PropertyChanged += (_, e) =>
      {
        if (string.Equals(e.PropertyName, nameof(ErrorMessage), StringComparison.Ordinal))
        {
          OnPropertyChanged(nameof(ErrorMessageVisibility));
        }
      };
    }

    public IAsyncRelayCommand SendQueryCommand { get; }
    public IAsyncRelayCommand ExecuteActionCommand { get; }
    public IAsyncRelayCommand LoadContextCommand { get; }
    public IAsyncRelayCommand ClearChatCommand { get; }
    public IAsyncRelayCommand RefreshCommand { get; }

    [ObservableProperty]
    private ChatMessageItem? selectedMessage;

    partial void OnCurrentQueryChanged(string? value)
    {
      SendQueryCommand.NotifyCanExecuteChanged();
    }

    partial void OnIsProcessingChanged(bool value)
    {
      SendQueryCommand.NotifyCanExecuteChanged();
    }

    partial void OnSelectedMessageChanged(ChatMessageItem? value)
    {
      ((System.Windows.Input.ICommand)ExecuteActionCommand).NotifyCanExecuteChanged();
    }

    partial void OnMessagesChanged(ObservableCollection<ChatMessageItem> value)
    {
      HookMessagesCollection(value);
      OnPropertyChanged(nameof(WelcomeVisibility));
    }

    partial void OnSuggestionsChanged(ObservableCollection<string> value)
    {
      HookSuggestionsCollection(value);
      OnPropertyChanged(nameof(SuggestionsVisibility));
    }

    private void HookMessagesCollection(ObservableCollection<ChatMessageItem> collection)
    {
      if (ReferenceEquals(_messagesHooked, collection))
      {
        return;
      }

      if (_messagesHooked != null)
      {
        _messagesHooked.CollectionChanged -= Messages_CollectionChanged;
      }

      _messagesHooked = collection;
      _messagesHooked.CollectionChanged += Messages_CollectionChanged;
    }

    private void HookSuggestionsCollection(ObservableCollection<string> collection)
    {
      if (ReferenceEquals(_suggestionsHooked, collection))
      {
        return;
      }

      if (_suggestionsHooked != null)
      {
        _suggestionsHooked.CollectionChanged -= Suggestions_CollectionChanged;
      }

      _suggestionsHooked = collection;
      _suggestionsHooked.CollectionChanged += Suggestions_CollectionChanged;
    }

    private void Messages_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
      OnPropertyChanged(nameof(WelcomeVisibility));
    }

    private void Suggestions_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
      OnPropertyChanged(nameof(SuggestionsVisibility));
    }

    private async Task SendQueryAsync(CancellationToken cancellationToken)
    {
      if (string.IsNullOrWhiteSpace(CurrentQuery) || IsProcessing)
      {
        return;
      }

      try
      {
        IsProcessing = true;
        ErrorMessage = null;
        cancellationToken.ThrowIfCancellationRequested();

        // Add user message to chat
        var userMessage = new ChatMessageItem
        {
          MessageId = Guid.NewGuid().ToString(),
          Role = "user",
          Content = CurrentQuery,
          Timestamp = DateTime.Now
        };
        Messages.Add(userMessage);

        // Load context if needed
        if (AppContext == null)
        {
          await LoadContextAsync(cancellationToken);
        }

        // Send query to backend
        var request = new AIProductionAssistantQueryRequest
        {
          Query = CurrentQuery,
          SessionId = CurrentSessionId,
          Context = AppContext?.ToDictionary()
        };

        var response = await _client.SendQueryAsync(request, cancellationToken);

        if (response != null)
        {
          CurrentSessionId = response.SessionId;

          // Add assistant message
          var assistantMessage = new ChatMessageItem
          {
            MessageId = response.MessageId,
            Role = "assistant",
            Content = response.Response,
            Timestamp = DateTime.Now,
            ActionData = response.ActionData
            // HasAction is computed property - no need to set
          };
          Messages.Add(assistantMessage);
          SelectedMessage = assistantMessage;

          // Update suggestions
          Suggestions.Clear();
          foreach (var suggestion in response.Suggestions)
          {
            Suggestions.Add(suggestion);
          }

          StatusMessage = ResourceHelper.GetString("AIProductionAssistant.ResponseReceived", "Response received");
        }

        // Clear query
        CurrentQuery = null;
      }
      catch (OperationCanceledException)
      {
        return; // User cancelled
      }
      catch (Exception ex)
      {
        await HandleErrorAsync(ex, "SendQuery");
      }
      finally
      {
        IsProcessing = false;
      }
    }

    private async Task ExecuteActionAsync(CancellationToken cancellationToken)
    {
      if (SelectedMessage == null || SelectedMessage.ActionData == null)
      {
        return;
      }

      try
      {
        IsProcessing = true;
        ErrorMessage = null;

        var actionData = SelectedMessage.ActionData;
        var actionType = actionData.ContainsKey("action") ? actionData["action"]?.ToString() : "";

        var request = new AIProductionAssistantExecuteRequest
        {
          SessionId = CurrentSessionId ?? "",
          ActionId = Guid.NewGuid().ToString(),
          ActionType = actionType ?? "",
          Parameters = actionData
        };

        var response = await _client.ExecuteActionAsync(request, cancellationToken);

        if (response != null)
        {
          if (response.Success)
          {
            // Add system message
            var systemMessage = new ChatMessageItem
            {
              MessageId = Guid.NewGuid().ToString(),
              Role = "system",
              Content = response.Message,
              Timestamp = DateTime.Now
            };
            Messages.Add(systemMessage);

            StatusMessage = ResourceHelper.GetString("AIProductionAssistant.ActionExecuted", "Action executed successfully");
          }
          else
          {
            ErrorMessage = response.Error ?? ResourceHelper.GetString("AIProductionAssistant.ActionExecutionFailed", "Action execution failed");
          }
        }
      }
      catch (OperationCanceledException)
      {
        return; // User cancelled
      }
      catch (Exception ex)
      {
        await HandleErrorAsync(ex, "ExecuteAction");
      }
      finally
      {
        IsProcessing = false;
      }
    }

    private async Task LoadContextAsync(CancellationToken cancellationToken)
    {
      try
      {
        var context = await _client.GetContextAsync(cancellationToken);

        if (context != null)
        {
          AppContext = new AssistantContextItem(context);
        }
      }
      catch (OperationCanceledException)
      {
        return; // User cancelled
      }
      catch (Exception ex)
      {
        await HandleErrorAsync(ex, "LoadContext");
      }
    }

    private async Task ClearChatAsync(CancellationToken cancellationToken)
    {
      cancellationToken.ThrowIfCancellationRequested();
      Messages.Clear();
      Suggestions.Clear();
      CurrentQuery = null;
      CurrentSessionId = null;
      SelectedMessage = null;
      StatusMessage = ResourceHelper.GetString("AIProductionAssistant.ChatCleared", "Chat cleared");
      await Task.CompletedTask;
    }

    private async Task RefreshAsync(CancellationToken cancellationToken)
    {
      try
      {
        await LoadContextAsync(cancellationToken);
        StatusMessage = ResourceHelper.GetString("AIProductionAssistant.Refreshed", "Refreshed");
      }
      catch (OperationCanceledException)
      {
        return; // User cancelled
      }
      catch (Exception ex)
      {
        await HandleErrorAsync(ex, "Refresh");
      }
    }

  }

  // Data models
  public class ChatMessageItem : ObservableObject
  {
    public string MessageId { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty; // user, assistant, system
    public string Content { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; }
    public Dictionary<string, object>? ActionData { get; set; }

    public bool HasAction => ActionData != null;
    public Visibility ActionVisibility => HasAction ? Visibility.Visible : Visibility.Collapsed;
    public string TimestampDisplay => Timestamp.ToString("HH:mm:ss");
    public string RoleDisplay => Role.ToUpper();
    public bool IsUser => Role == "user";
    public bool IsAssistant => Role == "assistant";
    public bool IsSystem => Role == "system";
  }

  public class AssistantContextItem : ObservableObject
  {
    public List<string> OpenPanels { get; set; } = new();
    public string? CurrentProject { get; set; }
    public string? ActiveAudioId { get; set; }
    public List<string> AvailableProfiles { get; set; } = new();
    public List<string> RecentOperations { get; set; } = new();

    public AssistantContextItem(AIProductionAssistantContextResponse response)
    {
      OpenPanels = response.OpenPanels;
      CurrentProject = response.CurrentProject;
      ActiveAudioId = response.ActiveAudioId;
      AvailableProfiles = response.AvailableProfiles;
      RecentOperations = response.RecentOperations;
    }

    public Dictionary<string, object> ToDictionary()
    {
      return new Dictionary<string, object>
            {
                { "open_panels", OpenPanels },
                { "current_project", CurrentProject ?? "" },
                { "active_audio_id", ActiveAudioId ?? "" },
                { "available_profiles", AvailableProfiles },
                { "recent_operations", RecentOperations }
            };
    }
  }
}