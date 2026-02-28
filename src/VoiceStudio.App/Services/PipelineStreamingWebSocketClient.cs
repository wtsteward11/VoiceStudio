using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using VoiceStudio.Core.Services;
using VoiceStudio.App.Logging;

namespace VoiceStudio.App.Services
{
  /// <summary>
  /// Specialized WebSocket client for streaming voice AI pipeline responses.
  /// Supports LLM streaming, TTS audio streaming, and S2S real-time communication.
  /// </summary>
  /// <remarks>
  /// GAP-009: Implements IPipelineStreamingClient for DI compatibility.
  /// </remarks>
  public class PipelineStreamingWebSocketClient : IPipelineStreamingClient
  {
    private readonly IWebSocketService _webSocketService;
    private readonly JsonSerializerOptions _jsonOptions;
    private bool _isSubscribed;
    private bool _disposed;
    private string? _currentSessionId;

    /// <summary>
    /// Event fired when a text token is received during streaming.
    /// </summary>
    public event EventHandler<PipelineTokenEvent>? TokenReceived;

    /// <summary>
    /// Event fired when audio data is received during streaming.
    /// </summary>
    public event EventHandler<PipelineAudioEvent>? AudioReceived;

    /// <summary>
    /// Event fired when streaming is complete.
    /// </summary>
    public event EventHandler<PipelineCompleteEvent>? StreamComplete;

    /// <summary>
    /// Event fired when an error occurs during streaming.
    /// </summary>
    public event EventHandler<PipelineErrorEvent>? ErrorOccurred;

    /// <summary>
    /// Event fired when session state changes.
    /// </summary>
    public event EventHandler<PipelineSessionState>? SessionStateChanged;

    /// <summary>
    /// Current session ID.
    /// </summary>
    public string? SessionId => _currentSessionId;

    public PipelineStreamingWebSocketClient(IWebSocketService webSocketService)
    {
      _webSocketService = webSocketService ?? throw new ArgumentNullException(nameof(webSocketService));
      _jsonOptions = new JsonSerializerOptions
      {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
      };

      _webSocketService.MessageReceived += OnWebSocketMessageReceived;
    }

    /// <summary>
    /// Connects and subscribes to pipeline streaming updates.
    /// </summary>
    public async Task ConnectAsync(string? sessionId = null, CancellationToken cancellationToken = default)
    {
      if (_disposed)
        throw new ObjectDisposedException(nameof(PipelineStreamingWebSocketClient));

      if (!_webSocketService.IsConnected)
      {
        await _webSocketService.ConnectAsync(new[] { "pipeline_stream" }, cancellationToken);
      }

      if (!_isSubscribed)
      {
        await _webSocketService.SubscribeAsync("pipeline_stream");
        _isSubscribed = true;
      }

      _currentSessionId = sessionId ?? Guid.NewGuid().ToString();

      await _webSocketService.SendMessageAsync(new
      {
        type = "init_session",
        session_id = _currentSessionId
      });

      SessionStateChanged?.Invoke(this, new PipelineSessionState
      {
        SessionId = _currentSessionId,
        State = "connected",
        Timestamp = DateTime.UtcNow
      });
    }

    /// <summary>
    /// Sends a text message for processing through the pipeline.
    /// </summary>
    public async Task SendTextAsync(string text, PipelineStreamConfig? config = null, CancellationToken cancellationToken = default)
    {
      if (_disposed || !_webSocketService.IsConnected)
        throw new InvalidOperationException("WebSocket is not connected");

      await _webSocketService.SendMessageAsync(new
      {
        type = "process_text",
        session_id = _currentSessionId,
        text = text,
        mode = config?.Mode ?? "streaming",
        llm_provider = config?.LlmProvider,
        llm_model = config?.LlmModel,
        tts_engine = config?.TtsEngine,
        voice_profile_id = config?.VoiceProfileId,
        language = config?.Language ?? "en",
        enable_tts = config?.EnableTts ?? true,
        timestamp = DateTime.UtcNow
      });
    }

    /// <summary>
    /// Sends audio data for STT processing through the pipeline.
    /// </summary>
    public async Task SendAudioAsync(byte[] audioData, CancellationToken cancellationToken = default)
    {
      if (_disposed || !_webSocketService.IsConnected)
        throw new InvalidOperationException("WebSocket is not connected");

      await _webSocketService.SendMessageAsync(new
      {
        type = "process_audio",
        session_id = _currentSessionId,
        audio_data = Convert.ToBase64String(audioData),
        timestamp = DateTime.UtcNow
      });
    }

    /// <summary>
    /// Stops the current streaming session.
    /// </summary>
    public async Task StopStreamingAsync(CancellationToken cancellationToken = default)
    {
      if (_disposed || !_webSocketService.IsConnected)
        return;

      await _webSocketService.SendMessageAsync(new
      {
        type = "stop_streaming",
        session_id = _currentSessionId,
        timestamp = DateTime.UtcNow
      });
    }

    /// <summary>
    /// Gets whether the WebSocket client is connected and subscribed.
    /// </summary>
    public bool IsConnected => _webSocketService.IsConnected && _isSubscribed;

    /// <summary>
    /// Disconnects from the pipeline streaming service.
    /// </summary>
    public async Task DisconnectAsync()
    {
      if (_isSubscribed)
      {
        try
        {
          await _webSocketService.UnsubscribeAsync("pipeline_stream");
        }
        catch (Exception ex)
        {
          ErrorLogger.LogWarning($"Best effort operation failed: {ex.Message}", "PipelineStreamingWebSocketClient.DisconnectAsync");
        }
        _isSubscribed = false;
      }

      SessionStateChanged?.Invoke(this, new PipelineSessionState
      {
        SessionId = _currentSessionId,
        State = "disconnected",
        Timestamp = DateTime.UtcNow
      });

      _currentSessionId = null;
    }

    private void OnWebSocketMessageReceived(object? sender, WebSocketMessage message)
    {
      if (_disposed)
        return;

      if (message.Topic != "pipeline_stream")
        return;

      try
      {
        var payloadJson = JsonSerializer.Serialize(message.Payload, _jsonOptions);
        using var doc = JsonDocument.Parse(payloadJson);
        var root = doc.RootElement;

        var messageType = root.TryGetProperty("type", out var typeProp)
            ? typeProp.GetString()
            : null;

        if (string.IsNullOrWhiteSpace(messageType))
          return;

        switch (messageType.ToLowerInvariant())
        {
          case "token":
            HandleToken(root);
            break;
          case "audio":
            HandleAudio(root);
            break;
          case "complete":
            HandleComplete(root);
            break;
          case "error":
            HandleError(root);
            break;
          case "session_state":
            HandleSessionState(root);
            break;
        }
      }
      catch (Exception ex)
      {
        System.Diagnostics.Debug.WriteLine($"Failed to process pipeline streaming message: {ex.Message}");
      }
    }

    private void HandleToken(JsonElement root)
    {
      try
      {
        var tokenEvent = JsonSerializer.Deserialize<PipelineTokenEvent>(root.GetRawText(), _jsonOptions);
        if (tokenEvent != null)
        {
          TokenReceived?.Invoke(this, tokenEvent);
        }
      }
      catch (Exception ex)
      {
        ErrorLogger.LogWarning($"Best effort operation failed: {ex.Message}", "PipelineStreamingWebSocketClient.HandleToken");
      }
    }

    private void HandleAudio(JsonElement root)
    {
      try
      {
        var audioEvent = JsonSerializer.Deserialize<PipelineAudioEvent>(root.GetRawText(), _jsonOptions);
        if (audioEvent != null)
        {
          AudioReceived?.Invoke(this, audioEvent);
        }
      }
      catch (Exception ex)
      {
        ErrorLogger.LogWarning($"Best effort operation failed: {ex.Message}", "PipelineStreamingWebSocketClient.HandleAudio");
      }
    }

    private void HandleComplete(JsonElement root)
    {
      try
      {
        var completeEvent = JsonSerializer.Deserialize<PipelineCompleteEvent>(root.GetRawText(), _jsonOptions);
        if (completeEvent != null)
        {
          StreamComplete?.Invoke(this, completeEvent);
        }
      }
      catch (Exception ex)
      {
        ErrorLogger.LogWarning($"Best effort operation failed: {ex.Message}", "PipelineStreamingWebSocketClient.HandleComplete");
      }
    }

    private void HandleError(JsonElement root)
    {
      try
      {
        var errorEvent = JsonSerializer.Deserialize<PipelineErrorEvent>(root.GetRawText(), _jsonOptions);
        if (errorEvent != null)
        {
          ErrorOccurred?.Invoke(this, errorEvent);
        }
      }
      catch (Exception ex)
      {
        ErrorLogger.LogWarning($"Best effort operation failed: {ex.Message}", "PipelineStreamingWebSocketClient.HandleError");
      }
    }

    private void HandleSessionState(JsonElement root)
    {
      try
      {
        var stateEvent = JsonSerializer.Deserialize<PipelineSessionState>(root.GetRawText(), _jsonOptions);
        if (stateEvent != null)
        {
          SessionStateChanged?.Invoke(this, stateEvent);
        }
      }
      catch (Exception ex)
      {
        ErrorLogger.LogWarning($"Best effort operation failed: {ex.Message}", "PipelineStreamingWebSocketClient.HandleSessionState");
      }
    }

    public void Dispose()
    {
      if (!_disposed)
      {
        _disposed = true;
        _webSocketService.MessageReceived -= OnWebSocketMessageReceived;
        DisconnectAsync().GetAwaiter().GetResult();
      }
    }
  }

  /// <summary>
  /// Configuration for pipeline streaming requests.
  /// </summary>
  public class PipelineStreamConfig
  {
    /// <summary>
    /// Pipeline mode: "streaming", "batch", or "half_cascade".
    /// </summary>
    public string Mode { get; set; } = "streaming";

    /// <summary>
    /// LLM provider to use (e.g., "ollama", "openai").
    /// </summary>
    public string? LlmProvider { get; set; }

    /// <summary>
    /// LLM model to use.
    /// </summary>
    public string? LlmModel { get; set; }

    /// <summary>
    /// TTS engine to use (e.g., "xtts_v2").
    /// </summary>
    public string? TtsEngine { get; set; }

    /// <summary>
    /// Voice profile ID for TTS.
    /// </summary>
    public string? VoiceProfileId { get; set; }

    /// <summary>
    /// Language code (e.g., "en").
    /// </summary>
    public string Language { get; set; } = "en";

    /// <summary>
    /// Whether to enable TTS output.
    /// </summary>
    public bool EnableTts { get; set; } = true;
  }

  /// <summary>
  /// Event for receiving a streaming token.
  /// </summary>
  public class PipelineTokenEvent
  {
    public string Token { get; set; } = string.Empty;
    public int Index { get; set; }
    public bool IsComplete { get; set; }
    public DateTime Timestamp { get; set; }
  }

  /// <summary>
  /// Event for receiving audio data during streaming.
  /// </summary>
  public class PipelineAudioEvent
  {
    public string AudioData { get; set; } = string.Empty; // Base64 encoded
    public int SampleRate { get; set; } = 22050;
    public int Channels { get; set; } = 1;
    public int ChunkIndex { get; set; }
    public DateTime Timestamp { get; set; }

    /// <summary>
    /// Gets the decoded audio bytes.
    /// </summary>
    public byte[] GetAudioBytes() => Convert.FromBase64String(AudioData);
  }

  /// <summary>
  /// Event for streaming completion.
  /// </summary>
  public class PipelineCompleteEvent
  {
    public string FullResponse { get; set; } = string.Empty;
    public double TotalDurationMs { get; set; }
    public int TokenCount { get; set; }
    public int AudioChunks { get; set; }
    public DateTime Timestamp { get; set; }
  }

  /// <summary>
  /// Event for errors during streaming.
  /// </summary>
  public class PipelineErrorEvent
  {
    public string Error { get; set; } = string.Empty;
    public string? Code { get; set; }
    public string? Details { get; set; }
    public DateTime Timestamp { get; set; }
  }

  /// <summary>
  /// Session state information.
  /// </summary>
  public class PipelineSessionState
  {
    public string? SessionId { get; set; }
    public string State { get; set; } = string.Empty; // "connected", "streaming", "idle", "disconnected"
    public DateTime Timestamp { get; set; }
  }
}
