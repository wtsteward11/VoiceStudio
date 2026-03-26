using System;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Xaml;
using VoiceStudio.App.Core.Models;
using VoiceStudio.App.Services;
using VoiceStudio.App.Utilities;
using VoiceStudio.Core.Panels;
using VoiceStudio.Core.Services;

namespace VoiceStudio.App.ViewModels
{
  /// <summary>
  /// ViewModel for the voice AI pipeline conversation interface.
  /// Supports LLM chat with TTS output for conversational AI.
  /// </summary>
  public partial class PipelineConversationViewModel : BaseViewModel, IPanelView
  {
    private readonly IPipelineConversationClient _client;
    private readonly IWebSocketClientFactory? _webSocketClientFactory;
    private readonly PipelineStreamingWebSocketClient? _streamingClient;
    private ObservableCollection<ConversationMessageItem>? _messagesHooked;

    public string PanelId => "pipeline-conversation";
    public string DisplayName => ResourceHelper.GetString("Panel.PipelineConversation.DisplayName", "AI Conversation");
    public PanelRegion Region => PanelRegion.Center;

    [ObservableProperty]
    private ObservableCollection<ConversationMessageItem> messages = new();

    [ObservableProperty]
    private string? userInput;

    [ObservableProperty]
    private string? currentSessionId;

    [ObservableProperty]
    private bool isProcessing;

    [ObservableProperty]
    private bool isStreaming;

    [ObservableProperty]
    private bool isConnected;

    [ObservableProperty]
    private string? currentStreamingText;

    [ObservableProperty]
    private string? selectedLlmProvider;

    [ObservableProperty]
    private string? selectedLlmModel;

    [ObservableProperty]
    private string? selectedTtsEngine;

    [ObservableProperty]
    private string? selectedVoiceProfileId;

    [ObservableProperty]
    private string selectedLanguage = "en";

    [ObservableProperty]
    private bool enableTts = true;

    [ObservableProperty]
    private bool enableStreaming;

    /// <summary>
    /// Whether pipeline streaming is enabled (F-01 gate). When false, streaming toggle is disabled.
    /// </summary>
    public bool IsPipelineStreamingEnabled { get; }

    [ObservableProperty]
    private ObservableCollection<PipelineProvider> availableLlmProviders = new();

    [ObservableProperty]
    private ObservableCollection<PipelineProvider> availableTtsProviders = new();

    public Visibility ErrorMessageVisibility =>
        string.IsNullOrWhiteSpace(ErrorMessage) ? Visibility.Collapsed : Visibility.Visible;

    public Visibility WelcomeVisibility =>
        Messages.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

    public Visibility StreamingIndicatorVisibility =>
        IsStreaming ? Visibility.Visible : Visibility.Collapsed;

    // GAP-009: Updated to use IWebSocketClientFactory for DI
    public PipelineConversationViewModel(
        IViewModelContext context,
        IPipelineConversationClient client,
        IWebSocketClientFactory? webSocketClientFactory = null)
        : base(context)
    {
      _client = client ?? throw new ArgumentNullException(nameof(client));
      _webSocketClientFactory = webSocketClientFactory;

      // F-01 gate: pipeline_streaming disabled until /api/pipeline/stream wiring is complete
      IsPipelineStreamingEnabled = AppServices.TryGetFeatureFlagsService()?.IsEnabled("pipeline_streaming") ?? false;
      if (IsPipelineStreamingEnabled)
        EnableStreaming = true;

      // Initialize streaming client via factory (GAP-009 remediation)
      if (_webSocketClientFactory != null)
      {
        _streamingClient = _webSocketClientFactory.CreatePipelineStreamingClient() as PipelineStreamingWebSocketClient;
        if (_streamingClient != null)
        {
          _streamingClient.TokenReceived += OnTokenReceived;
          _streamingClient.AudioReceived += OnAudioReceived;
          _streamingClient.StreamComplete += OnStreamComplete;
          _streamingClient.ErrorOccurred += OnStreamError;
          _streamingClient.SessionStateChanged += OnSessionStateChanged;
        }
      }
      else if (client.WebSocketService != null)
      {
        // Fallback for backward compatibility
        _streamingClient = new PipelineStreamingWebSocketClient(client.WebSocketService);
        _streamingClient.TokenReceived += OnTokenReceived;
        _streamingClient.AudioReceived += OnAudioReceived;
        _streamingClient.StreamComplete += OnStreamComplete;
        _streamingClient.ErrorOccurred += OnStreamError;
        _streamingClient.SessionStateChanged += OnSessionStateChanged;
      }

      SendMessageCommand = new EnhancedAsyncRelayCommand(async (ct) =>
      {
        using var profiler = PerformanceProfiler.StartCommand("SendMessage");
        await SendMessageAsync(ct);
      }, () => !string.IsNullOrWhiteSpace(UserInput) && !IsProcessing && !IsLoading);

      StopStreamingCommand = new EnhancedAsyncRelayCommand(async (ct) =>
      {
        using var profiler = PerformanceProfiler.StartCommand("StopStreaming");
        await StopStreamingAsync(ct);
      }, () => IsStreaming);

      ClearConversationCommand = new EnhancedAsyncRelayCommand(async (ct) =>
      {
        using var profiler = PerformanceProfiler.StartCommand("ClearConversation");
        await ClearConversationAsync(ct);
      }, () => Messages.Count > 0 && !IsLoading);

      RefreshProvidersCommand = new EnhancedAsyncRelayCommand(async (ct) =>
      {
        using var profiler = PerformanceProfiler.StartCommand("RefreshProviders");
        await RefreshProvidersAsync(ct);
      }, () => !IsLoading);

      RefreshCommand = new EnhancedAsyncRelayCommand(async (ct) =>
      {
        using var profiler = PerformanceProfiler.StartCommand("Refresh");
        await RefreshAsync(ct);
      }, () => !IsLoading);

      HookMessagesCollection(Messages);

      PropertyChanged += (_, e) =>
      {
        if (string.Equals(e.PropertyName, nameof(ErrorMessage), StringComparison.Ordinal))
        {
          OnPropertyChanged(nameof(ErrorMessageVisibility));
        }
        if (string.Equals(e.PropertyName, nameof(IsStreaming), StringComparison.Ordinal))
        {
          OnPropertyChanged(nameof(StreamingIndicatorVisibility));
          ((EnhancedAsyncRelayCommand)StopStreamingCommand).NotifyCanExecuteChanged();
        }
      };
    }

    public IAsyncRelayCommand SendMessageCommand { get; }
    public IAsyncRelayCommand StopStreamingCommand { get; }
    public IAsyncRelayCommand ClearConversationCommand { get; }
    public IAsyncRelayCommand RefreshProvidersCommand { get; }
    public IAsyncRelayCommand RefreshCommand { get; }

    private async Task RefreshAsync(CancellationToken cancellationToken)
    {
      await RefreshProvidersAsync(cancellationToken);
    }

    private async Task RefreshProvidersAsync(CancellationToken cancellationToken)
    {
      try
      {
        var providers = await _client.GetPipelineProvidersAsync(cancellationToken);

        AvailableLlmProviders.Clear();
        foreach (var p in providers.LlmProviders)
        {
          AvailableLlmProviders.Add(p);
        }

        AvailableTtsProviders.Clear();
        foreach (var p in providers.TtsProviders)
        {
          AvailableTtsProviders.Add(p);
        }

        // Set defaults if available
        if (string.IsNullOrEmpty(SelectedLlmProvider) && AvailableLlmProviders.Count > 0)
        {
          SelectedLlmProvider = AvailableLlmProviders[0].Id;
        }
        if (string.IsNullOrEmpty(SelectedTtsEngine) && AvailableTtsProviders.Count > 0)
        {
          SelectedTtsEngine = AvailableTtsProviders[0].Id;
        }
      }
      catch (Exception ex)
      {
        await HandleErrorAsync(ex, "RefreshProviders");
      }
    }

    private async Task SendMessageAsync(CancellationToken cancellationToken)
    {
      if (string.IsNullOrWhiteSpace(UserInput))
        return;

      var userMessage = UserInput;
      UserInput = string.Empty;

      // Add user message to conversation
      Messages.Add(new ConversationMessageItem
      {
        Role = "user",
        Content = userMessage,
        Timestamp = DateTime.Now
      });
      OnPropertyChanged(nameof(WelcomeVisibility));

      IsProcessing = true;
      ((EnhancedAsyncRelayCommand)SendMessageCommand).NotifyCanExecuteChanged();

      try
      {
        if (EnableStreaming && IsPipelineStreamingEnabled && _streamingClient != null)
        {
          await SendStreamingMessageAsync(userMessage, cancellationToken);
        }
        else
        {
          await SendBatchMessageAsync(userMessage, cancellationToken);
        }
      }
      catch (Exception ex)
      {
        await HandleErrorAsync(ex, "SendMessage");
        Messages.Add(new ConversationMessageItem
        {
          Role = "error",
          Content = $"Error: {ex.Message}",
          Timestamp = DateTime.Now
        });
      }
      finally
      {
        IsProcessing = false;
        IsStreaming = false;
        ((EnhancedAsyncRelayCommand)SendMessageCommand).NotifyCanExecuteChanged();
      }
    }

    private async Task SendStreamingMessageAsync(string text, CancellationToken cancellationToken)
    {
      ArgumentNullException.ThrowIfNull(text);
      if (string.IsNullOrWhiteSpace(text))
      {
        return; // No-op for empty input
      }

      if (_streamingClient == null)
      {
        await SendBatchMessageAsync(text, cancellationToken);
        return;
      }

      IsStreaming = true;
      CurrentStreamingText = string.Empty;

      // Add placeholder for assistant response
      var assistantMessage = new ConversationMessageItem
      {
        Role = "assistant",
        Content = "",
        Timestamp = DateTime.Now,
        IsStreaming = true
      };
      Messages.Add(assistantMessage);

      if (!_streamingClient.IsConnected)
      {
        await _streamingClient.ConnectAsync(CurrentSessionId, cancellationToken);
        CurrentSessionId = _streamingClient.SessionId;
      }

      await _streamingClient.SendTextAsync(text, new PipelineStreamConfig
      {
        Mode = "streaming",
        LlmProvider = SelectedLlmProvider,
        LlmModel = SelectedLlmModel,
        TtsEngine = SelectedTtsEngine,
        VoiceProfileId = SelectedVoiceProfileId,
        Language = SelectedLanguage,
        EnableTts = EnableTts
      }, cancellationToken);
    }

    private async Task SendBatchMessageAsync(string text, CancellationToken cancellationToken)
    {
      ArgumentNullException.ThrowIfNull(text);
      if (string.IsNullOrWhiteSpace(text))
      {
        return; // No-op for empty input
      }

      var request = new PipelineRequest
      {
        Text = text,
        Mode = "batch",
        LlmProvider = SelectedLlmProvider,
        LlmModel = SelectedLlmModel,
        TtsEngine = SelectedTtsEngine,
        VoiceProfileId = SelectedVoiceProfileId,
        Language = SelectedLanguage,
        EnableFunctionCalling = true
      };

      var response = await _client.ProcessPipelineAsync(request, cancellationToken);

      Messages.Add(new ConversationMessageItem
      {
        Role = "assistant",
        Content = response.Response,
        AudioData = response.Audio,
        Timestamp = DateTime.Now
      });
    }

    private async Task StopStreamingAsync(CancellationToken cancellationToken)
    {
      if (_streamingClient != null && IsStreaming)
      {
        await _streamingClient.StopStreamingAsync(cancellationToken);
        IsStreaming = false;
      }
    }

    private async Task ClearConversationAsync(CancellationToken cancellationToken)
    {
      Messages.Clear();
      CurrentSessionId = null;
      CurrentStreamingText = null;
      OnPropertyChanged(nameof(WelcomeVisibility));

      if (_streamingClient != null)
      {
        await _streamingClient.DisconnectAsync();
      }

      await Task.CompletedTask;
    }

    private void OnTokenReceived(object? sender, PipelineTokenEvent e)
    {
      var token = e.Token;
      Dispatcher.TryEnqueue(() =>
      {
        if (Messages.Count > 0 && Messages[^1].IsStreaming)
        {
          CurrentStreamingText += token;
          Messages[^1].Content = CurrentStreamingText ?? "";
          OnPropertyChanged(nameof(Messages));
        }
      });
    }

    private void OnAudioReceived(object? sender, PipelineAudioEvent e)
    {
      var audioData = e.AudioData;
      Dispatcher.TryEnqueue(() =>
      {
        if (Messages.Count > 0)
        {
          var lastMessage = Messages[^1];
          var existingAudio = lastMessage.AudioData ?? "";
          lastMessage.AudioData = existingAudio + audioData;
        }
      });
    }

    private void OnStreamComplete(object? sender, PipelineCompleteEvent e)
    {
      var fullResponse = e.FullResponse;
      Dispatcher.TryEnqueue(() =>
      {
        if (Messages.Count > 0 && Messages[^1].IsStreaming)
        {
          Messages[^1].Content = fullResponse;
          Messages[^1].IsStreaming = false;
        }
        IsStreaming = false;
        CurrentStreamingText = null;
      });
    }

    private void OnStreamError(object? sender, PipelineErrorEvent e)
    {
      Dispatcher.TryEnqueue(() =>
      {
        ErrorMessage = $"Streaming error: {e.Error}";
        IsStreaming = false;
        if (Messages.Count > 0 && Messages[^1].IsStreaming)
        {
          Messages[^1].IsStreaming = false;
          Messages[^1].Content = $"[Error: {e.Error}]";
        }
      });
    }

    private void OnSessionStateChanged(object? sender, PipelineSessionState e)
    {
      var state = e.State;
      Dispatcher.TryEnqueue(() => IsConnected = state == "connected" || state == "streaming");
    }

    private void HookMessagesCollection(ObservableCollection<ConversationMessageItem> collection)
    {
      if (_messagesHooked != null)
      {
        _messagesHooked.CollectionChanged -= OnMessagesCollectionChanged;
      }
      _messagesHooked = collection;
      if (_messagesHooked != null)
      {
        _messagesHooked.CollectionChanged += OnMessagesCollectionChanged;
      }
    }

    private void OnMessagesCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
      OnPropertyChanged(nameof(WelcomeVisibility));
      ((EnhancedAsyncRelayCommand)ClearConversationCommand).NotifyCanExecuteChanged();
    }

    partial void OnMessagesChanged(ObservableCollection<ConversationMessageItem> value)
    {
      HookMessagesCollection(value);
      OnPropertyChanged(nameof(WelcomeVisibility));
    }

    // CS0108 fix: Intentionally hiding base Dispose to include streaming client cleanup
    public new void Dispose()
    {
      if (_streamingClient != null)
      {
        _streamingClient.TokenReceived -= OnTokenReceived;
        _streamingClient.AudioReceived -= OnAudioReceived;
        _streamingClient.StreamComplete -= OnStreamComplete;
        _streamingClient.ErrorOccurred -= OnStreamError;
        _streamingClient.SessionStateChanged -= OnSessionStateChanged;
        _streamingClient.Dispose();
      }
      base.Dispose();
    }
  }

  /// <summary>
  /// A message item in the conversation.
  /// </summary>
  public class ConversationMessageItem
  {
    public string Role { get; set; } = string.Empty; // "user", "assistant", "error"
    public string Content { get; set; } = string.Empty;
    public string? AudioData { get; set; } // Base64 encoded audio
    public DateTime Timestamp { get; set; }
    public bool IsStreaming { get; set; }
    public bool HasAudio => !string.IsNullOrEmpty(AudioData);
  }
}
