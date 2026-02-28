using System;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using VoiceStudio.Core.Services;
using VoiceStudio.App.Logging;

namespace VoiceStudio.App.Services
{
  /// <summary>
  /// Specialized WebSocket client for real-time voice conversion.
  /// Can connect directly to /api/rvc/convert/realtime or use topic-based ws/realtime.
  /// </summary>
  /// <remarks>
  /// GAP-009: Implements IRealtimeVoiceClient for DI compatibility.
  /// </remarks>
  public class RealtimeVoiceWebSocketClient : IRealtimeVoiceClient
  {
    private readonly IWebSocketService? _webSocketService;
    private readonly string? _directEndpoint;
    private ClientWebSocket? _directSocket;
    private readonly JsonSerializerOptions _jsonOptions;
    private bool _isSubscribed;
    private bool _disposed;
    private CancellationTokenSource? _receiveCts;
    private Task? _receiveTask;
    private bool _useDirectConnection;

    /// <summary>
    /// Event fired when audio data is received for real-time conversion.
    /// </summary>
    public event EventHandler<RealtimeAudioData>? AudioDataReceived;

    /// <summary>
    /// Event fired when conversion status changes.
    /// </summary>
    public event EventHandler<RealtimeConversionStatus>? StatusChanged;

    /// <summary>
    /// Event fired when conversion quality metrics are updated.
    /// </summary>
    public event EventHandler<RealtimeQualityMetrics>? QualityMetricsUpdated;

    /// <summary>
    /// Event fired when latency information is received.
    /// </summary>
    public event EventHandler<RealtimeLatencyInfo>? LatencyInfoReceived;

    /// <summary>
    /// Creates a client using the topic-based WebSocket service.
    /// </summary>
    public RealtimeVoiceWebSocketClient(IWebSocketService webSocketService)
    {
      _webSocketService = webSocketService ?? throw new ArgumentNullException(nameof(webSocketService));
      _useDirectConnection = false;
      _jsonOptions = new JsonSerializerOptions
      {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
      };

      _webSocketService.MessageReceived += OnWebSocketMessageReceived;
    }

    /// <summary>
    /// Creates a client that connects directly to the RVC real-time WebSocket endpoint.
    /// </summary>
    /// <param name="backendBaseUrl">Base URL of the backend (e.g., http://localhost:8001)</param>
    public RealtimeVoiceWebSocketClient(string backendBaseUrl)
    {
      if (string.IsNullOrEmpty(backendBaseUrl))
        throw new ArgumentNullException(nameof(backendBaseUrl));

      // Convert http(s) to ws(s)
      var wsUrl = backendBaseUrl.Replace("http://", "ws://").Replace("https://", "wss://");
      _directEndpoint = $"{wsUrl.TrimEnd('/')}/api/rvc/convert/realtime";
      _useDirectConnection = true;
      _jsonOptions = new JsonSerializerOptions
      {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
      };
    }

    /// <summary>
    /// Connects and subscribes to real-time voice conversion updates.
    /// </summary>
    public async Task ConnectAsync(string? sessionId = null, CancellationToken cancellationToken = default)
    {
      if (_disposed)
        throw new ObjectDisposedException(nameof(RealtimeVoiceWebSocketClient));

      if (_useDirectConnection)
      {
        await ConnectDirectAsync(cancellationToken);
      }
      else
      {
        await ConnectTopicBasedAsync(sessionId, cancellationToken);
      }
    }

    private async Task ConnectDirectAsync(CancellationToken cancellationToken)
    {
      _directSocket?.Dispose();
      _directSocket = new ClientWebSocket();

      await _directSocket.ConnectAsync(new Uri(_directEndpoint!), cancellationToken);

      // Start receiving messages
      _receiveCts = new CancellationTokenSource();
      _receiveTask = Task.Run(() => ReceiveLoopAsync(_receiveCts.Token), _receiveCts.Token);
    }

    private async Task ConnectTopicBasedAsync(string? sessionId, CancellationToken cancellationToken)
    {
      if (!_webSocketService!.IsConnected)
      {
        await _webSocketService.ConnectAsync(new[] { "realtime_voice" }, cancellationToken);
      }

      if (!_isSubscribed)
      {
        await _webSocketService.SubscribeAsync("realtime_voice");
        _isSubscribed = true;
      }

      if (!string.IsNullOrEmpty(sessionId))
      {
        await _webSocketService.SendMessageAsync(new
        {
          type = "init_session",
          session_id = sessionId
        });
      }
    }

    private async Task ReceiveLoopAsync(CancellationToken cancellationToken)
    {
      var buffer = new byte[8192];

      try
      {
        while (!cancellationToken.IsCancellationRequested && _directSocket?.State == System.Net.WebSockets.WebSocketState.Open)
        {
          var result = await _directSocket.ReceiveAsync(new ArraySegment<byte>(buffer), cancellationToken);

          if (result.MessageType == WebSocketMessageType.Close)
          {
            break;
          }

          if (result.MessageType == WebSocketMessageType.Binary)
          {
            // Audio data received directly
            var audioData = new byte[result.Count];
            Array.Copy(buffer, audioData, result.Count);
            AudioDataReceived?.Invoke(this, new RealtimeAudioData
            {
              AudioData = audioData,
              SampleRate = 44100, // Default, could be configured
              Channels = 1,
              Timestamp = DateTime.UtcNow
            });
          }
          else if (result.MessageType == WebSocketMessageType.Text)
          {
            var message = Encoding.UTF8.GetString(buffer, 0, result.Count);
            ProcessDirectMessage(message);
          }
        }
      }
      // ALLOWED: empty catch - OperationCanceledException is expected during shutdown
      catch (OperationCanceledException)
      {
        // Intentionally empty - cancellation is normal during shutdown
      }
      catch (Exception ex)
      {
        ErrorLogger.LogWarning($"WebSocket receive error: {ex.Message}", "RealtimeVoiceWebSocketClient.ReceiveLoopAsync");
      }
    }

    private void ProcessDirectMessage(string message)
    {
      try
      {
        using var doc = JsonDocument.Parse(message);
        var root = doc.RootElement;

        var messageType = root.TryGetProperty("type", out var typeProp)
            ? typeProp.GetString()
            : "audio_data";

        switch (messageType?.ToLowerInvariant())
        {
          case "status":
            HandleStatusUpdate(root);
            break;
          case "quality_metrics":
            HandleQualityMetrics(root);
            break;
          case "latency":
            HandleLatencyInfo(root);
            break;
          case "error":
            var error = root.TryGetProperty("error", out var errProp) ? errProp.GetString() : "Unknown error";
            StatusChanged?.Invoke(this, new RealtimeConversionStatus
            {
              Status = "error",
              Message = error,
              Timestamp = DateTime.UtcNow
            });
            break;
        }
      }
      catch (Exception ex)
      {
        System.Diagnostics.Debug.WriteLine($"Failed to process direct message: {ex.Message}");
      }
    }

    /// <summary>
    /// Sends audio data for real-time conversion.
    /// </summary>
    public async Task SendAudioDataAsync(byte[] audioData, CancellationToken cancellationToken = default)
    {
      if (_disposed || !IsConnected)
        throw new InvalidOperationException("WebSocket is not connected");

      if (_useDirectConnection)
      {
        // Send binary audio data directly
        await _directSocket!.SendAsync(new ArraySegment<byte>(audioData), WebSocketMessageType.Binary, true, cancellationToken);
      }
      else
      {
        await _webSocketService!.SendMessageAsync(new
        {
          type = "audio_data",
          data = Convert.ToBase64String(audioData),
          timestamp = DateTime.UtcNow
        });
      }
    }

    /// <summary>
    /// Gets whether the WebSocket client is connected and subscribed.
    /// </summary>
    public bool IsConnected
    {
      get
      {
        if (_useDirectConnection)
          return _directSocket?.State == System.Net.WebSockets.WebSocketState.Open;
        return _webSocketService?.IsConnected == true && _isSubscribed;
      }
    }

    /// <summary>
    /// Unsubscribes from real-time voice conversion updates.
    /// </summary>
    public async Task DisconnectAsync()
    {
      if (_useDirectConnection)
      {
        try
        {
          _receiveCts?.Cancel();
          if (_directSocket?.State == System.Net.WebSockets.WebSocketState.Open)
          {
            await _directSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Client disconnecting", CancellationToken.None);
          }
        }
        catch (Exception ex)
        {
          ErrorLogger.LogWarning($"Direct WebSocket close error: {ex.Message}", "RealtimeVoiceWebSocketClient.DisconnectAsync");
        }
        finally
        {
          _directSocket?.Dispose();
          _directSocket = null;
          _receiveCts?.Dispose();
          _receiveCts = null;
        }
      }
      else if (_isSubscribed)
      {
        try
        {
          await _webSocketService!.UnsubscribeAsync("realtime_voice");
        }
        catch (Exception ex)
        {
          ErrorLogger.LogWarning($"Best effort operation failed: {ex.Message}", "RealtimeVoiceWebSocketClient.DisconnectAsync");
        }
        _isSubscribed = false;
      }
    }

    private void OnWebSocketMessageReceived(object? sender, WebSocketMessage message)
    {
      if (_disposed)
        return;

      // Only process real-time voice topic
      if (message.Topic != "realtime_voice")
        return;

      try
      {
        var payloadJson = JsonSerializer.Serialize(message.Payload, _jsonOptions);
        using var doc = JsonDocument.Parse(payloadJson);
        var root = doc.RootElement;

        // Determine message type
        var messageType = root.TryGetProperty("type", out var typeProp)
            ? typeProp.GetString()
            : null;

        messageType = string.IsNullOrWhiteSpace(messageType) ? "audio_data" : messageType;

        switch (messageType.ToLowerInvariant())
        {
          case "audio_data":
            HandleAudioData(root);
            break;
          case "status":
            HandleStatusUpdate(root);
            break;
          case "quality_metrics":
            HandleQualityMetrics(root);
            break;
          case "latency":
            HandleLatencyInfo(root);
            break;
        }
      }
      catch (Exception ex)
      {
        // Log error but don't throw
        System.Diagnostics.Debug.WriteLine($"Failed to process real-time voice message: {ex.Message}");
      }
    }

    private void HandleAudioData(JsonElement root)
    {
      try
      {
        var update = JsonSerializer.Deserialize<RealtimeAudioData>(root.GetRawText(), _jsonOptions);
        if (update != null)
        {
          AudioDataReceived?.Invoke(this, update);
        }
      }
      catch (Exception ex)
      {
        ErrorLogger.LogWarning($"Best effort operation failed: {ex.Message}", "RealtimeVoiceWebSocketClient.HandleAudioData");
      }
    }

    private void HandleStatusUpdate(JsonElement root)
    {
      try
      {
        var update = JsonSerializer.Deserialize<RealtimeConversionStatus>(root.GetRawText(), _jsonOptions);
        if (update != null)
        {
          StatusChanged?.Invoke(this, update);
        }
      }
      catch (Exception ex)
      {
        ErrorLogger.LogWarning($"Best effort operation failed: {ex.Message}", "RealtimeVoiceWebSocketClient.HandleStatusUpdate");
      }
    }

    private void HandleQualityMetrics(JsonElement root)
    {
      try
      {
        var update = JsonSerializer.Deserialize<RealtimeQualityMetrics>(root.GetRawText(), _jsonOptions);
        if (update != null)
        {
          QualityMetricsUpdated?.Invoke(this, update);
        }
      }
      catch (Exception ex)
      {
        ErrorLogger.LogWarning($"Best effort operation failed: {ex.Message}", "RealtimeVoiceWebSocketClient.HandleQualityMetrics");
      }
    }

    private void HandleLatencyInfo(JsonElement root)
    {
      try
      {
        var update = JsonSerializer.Deserialize<RealtimeLatencyInfo>(root.GetRawText(), _jsonOptions);
        if (update != null)
        {
          LatencyInfoReceived?.Invoke(this, update);
        }
      }
      catch (Exception ex)
      {
        ErrorLogger.LogWarning($"Best effort operation failed: {ex.Message}", "RealtimeVoiceWebSocketClient.HandleLatencyInfo");
      }
    }

    public void Dispose()
    {
      if (!_disposed)
      {
        _disposed = true;
        if (!_useDirectConnection && _webSocketService != null)
        {
          _webSocketService.MessageReceived -= OnWebSocketMessageReceived;
        }
        DisconnectAsync().GetAwaiter().GetResult();
      }
    }
  }

  /// <summary>
  /// Real-time audio data message.
  /// </summary>
  public class RealtimeAudioData
  {
    public byte[] AudioData { get; set; } = Array.Empty<byte>();
    public int SampleRate { get; set; }
    public int Channels { get; set; }
    public DateTime Timestamp { get; set; }
  }

  /// <summary>
  /// Real-time conversion status message.
  /// </summary>
  public class RealtimeConversionStatus
  {
    public string Status { get; set; } = string.Empty; // "idle", "converting", "paused", "error"
    public string? Message { get; set; }
    public DateTime Timestamp { get; set; }
  }

  /// <summary>
  /// Real-time quality metrics message.
  /// </summary>
  public class RealtimeQualityMetrics
  {
    public double Similarity { get; set; }
    public double Naturalness { get; set; }
    public double Clarity { get; set; }
    public DateTime Timestamp { get; set; }
  }

  /// <summary>
  /// Real-time latency information message.
  /// </summary>
  public class RealtimeLatencyInfo
  {
    public double ProcessingLatency { get; set; } // milliseconds
    public double NetworkLatency { get; set; } // milliseconds
    public double TotalLatency { get; set; } // milliseconds
    public DateTime Timestamp { get; set; }
  }
}