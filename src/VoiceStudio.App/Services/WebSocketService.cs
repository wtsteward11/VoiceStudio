using System;
using System.Collections.Generic;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using VoiceStudio.Core.Services;
using CoreWebSocketState = VoiceStudio.Core.Services.WebSocketState;
using NetWebSocketState = System.Net.WebSockets.WebSocketState;
using VoiceStudio.App.Logging;
using VoiceStudio.App.Utilities;

namespace VoiceStudio.App.Services
{
  /// <summary>
  /// WebSocket service implementation for real-time communication.
  /// Implements React/TypeScript WebSocket patterns in C#.
  /// </summary>
  public class WebSocketService : IWebSocketService
  {
    private ClientWebSocket? _webSocket;
    private CancellationTokenSource? _cancellationTokenSource;
    private Task? _receiveTask;
    private readonly string _webSocketUrl;
    private readonly JsonSerializerOptions _jsonOptions;
    private readonly HashSet<string> _subscribedTopics;
    private CoreWebSocketState _state = CoreWebSocketState.Disconnected;

    public event EventHandler? Connected;
    public event EventHandler<string>? Disconnected;
    public event EventHandler<Exception>? Error;
    public event EventHandler<WebSocketMessage>? MessageReceived;

    public CoreWebSocketState State => _state;
    public bool IsConnected => _state == CoreWebSocketState.Connected && _webSocket?.State == NetWebSocketState.Open;

    public WebSocketService(string webSocketUrl)
    {
      _webSocketUrl = webSocketUrl ?? throw new ArgumentNullException(nameof(webSocketUrl));
      _subscribedTopics = new HashSet<string>();
      // Use snake_case to match Python backend conventions
      _jsonOptions = JsonSerializerOptionsFactory.BackendApi;
    }

    public async Task ConnectAsync(string[]? topics = null, CancellationToken cancellationToken = default)
    {
      if (_state == CoreWebSocketState.Connected || _state == CoreWebSocketState.Connecting)
      {
        return;
      }

      try
      {
        _state = CoreWebSocketState.Connecting;
        _webSocket = new ClientWebSocket();
        _cancellationTokenSource = new CancellationTokenSource();

        // Build WebSocket URL with topics query parameter
        var uriBuilder = new UriBuilder(_webSocketUrl);
        if (topics?.Length > 0)
        {
          uriBuilder.Query = $"topics={string.Join(",", topics)}";
          foreach (var topic in topics)
          {
            _subscribedTopics.Add(topic);
          }
        }
        else
        {
          _subscribedTopics.Add("general");
        }

        await _webSocket.ConnectAsync(uriBuilder.Uri, cancellationToken);
        _state = CoreWebSocketState.Connected;

        // Start receiving messages
        _receiveTask = Task.Run(() => ReceiveMessagesAsync(_cancellationTokenSource.Token));

        Connected?.Invoke(this, EventArgs.Empty);
      }
      catch (Exception ex)
      {
        _state = CoreWebSocketState.Error;
        Error?.Invoke(this, ex);
        throw;
      }
    }

    public async Task DisconnectAsync()
    {
      if (_state == CoreWebSocketState.Disconnected || _state == CoreWebSocketState.Disconnecting)
      {
        return;
      }

      try
      {
        _state = CoreWebSocketState.Disconnecting;

        _cancellationTokenSource?.Cancel();

        if (_webSocket != null && _webSocket.State == NetWebSocketState.Open)
        {
          await _webSocket.CloseAsync(
              WebSocketCloseStatus.NormalClosure,
              "Client disconnecting",
              CancellationToken.None);
        }

        _webSocket?.Dispose();
        _webSocket = null;

        if (_receiveTask != null)
        {
          try
          {
            await _receiveTask;
          }
          catch (Exception ex)
      {
        ErrorLogger.LogWarning($"Best effort operation failed: {ex.Message}", "WebSocketService.DisconnectAsync");
      }
        }

        _state = CoreWebSocketState.Disconnected;
        _subscribedTopics.Clear();

        Disconnected?.Invoke(this, "Normal closure");
      }
      catch (Exception ex)
      {
        _state = CoreWebSocketState.Error;
        Error?.Invoke(this, ex);
      }
    }

    public async Task SubscribeAsync(string topic)
    {
      if (!IsConnected)
      {
        throw new InvalidOperationException("WebSocket is not connected");
      }

      var message = new
      {
        type = "subscribe",
        topic = topic
      };

      await SendMessageAsync(message);
      _subscribedTopics.Add(topic);
    }

    public async Task UnsubscribeAsync(string topic)
    {
      if (!IsConnected)
      {
        throw new InvalidOperationException("WebSocket is not connected");
      }

      var message = new
      {
        type = "unsubscribe",
        topic = topic
      };

      await SendMessageAsync(message);
      _subscribedTopics.Remove(topic);
    }

    public async Task PingAsync()
    {
      if (!IsConnected)
      {
        throw new InvalidOperationException("WebSocket is not connected");
      }

      var message = new
      {
        type = "ping"
      };

      await SendMessageAsync(message);
    }

    public async Task SendMessageAsync(object message)
    {
      if (!IsConnected || _webSocket == null)
      {
        throw new InvalidOperationException("WebSocket is not connected");
      }

      var json = JsonSerializer.Serialize(message, _jsonOptions);
      var bytes = Encoding.UTF8.GetBytes(json);
      var buffer = new ArraySegment<byte>(bytes);

      await _webSocket.SendAsync(
          buffer,
          WebSocketMessageType.Text,
          true,
          CancellationToken.None);
    }

    private async Task ReceiveMessagesAsync(CancellationToken cancellationToken)
    {
      if (_webSocket == null)
      {
        return;
      }

      var buffer = new byte[4096];

      try
      {
        while (_webSocket.State == NetWebSocketState.Open && !cancellationToken.IsCancellationRequested)
        {
          var result = await _webSocket.ReceiveAsync(
              new ArraySegment<byte>(buffer),
              cancellationToken);

          if (result.MessageType == WebSocketMessageType.Close)
          {
            await _webSocket.CloseAsync(
                WebSocketCloseStatus.NormalClosure,
                "Server closed connection",
                cancellationToken);
            break;
          }

          if (result.MessageType == WebSocketMessageType.Text)
          {
            var messageJson = Encoding.UTF8.GetString(buffer, 0, result.Count);
            ProcessMessage(messageJson);
          }
        }
      }
      catch (Exception ex)
      {
        ErrorLogger.LogWarning($"Best effort operation failed: {ex.Message}", "WebSocketService.ReceiveMessagesAsync");
        _state = CoreWebSocketState.Error;
        Error?.Invoke(this, ex);
      }
    }

    private void ProcessMessage(string messageJson)
    {
      try
      {
        using var doc = JsonDocument.Parse(messageJson);
        var root = doc.RootElement;

        var messageType = root.TryGetProperty("type", out var typeProp) ? typeProp.GetString() ?? "update" : "update";

        // Handle pong responses
        if (messageType == "pong")
        {
          return;
        }

        // Handle heartbeat
        if (messageType == "heartbeat")
        {
          return;
        }

        // Handle batch: backend sends multiple messages in one envelope; unwrap and emit each
        if (messageType == "batch" && root.TryGetProperty("messages", out var messagesProp))
        {
          foreach (var msg in messagesProp.EnumerateArray())
          {
            var subMsg = CreateWebSocketMessage(msg, _jsonOptions);
            if (subMsg != null)
              MessageReceived?.Invoke(this, subMsg);
          }
          return;
        }

        var message = new WebSocketMessage
        {
          Topic = root.TryGetProperty("topic", out var topicProp) ? topicProp.GetString() ?? "general" : "general",
          Type = messageType,
          Payload = root.TryGetProperty("payload", out var payloadProp) ? JsonSerializer.Deserialize<object>(payloadProp.GetRawText(), _jsonOptions) : null,
          Timestamp = root.TryGetProperty("timestamp", out var timestampProp) && timestampProp.TryGetDateTime(out var timestamp)
                ? timestamp
                : DateTime.UtcNow,
          RequestId = root.TryGetProperty("request_id", out var requestIdProp) ? requestIdProp.GetString() : null
        };

        MessageReceived?.Invoke(this, message);
      }
      catch (JsonException ex)
      {
        Error?.Invoke(this, new Exception($"Failed to parse WebSocket message: {ex.Message}", ex));
      }
    }

    private static WebSocketMessage? CreateWebSocketMessage(JsonElement root, JsonSerializerOptions jsonOptions)
    {
      var topic = root.TryGetProperty("topic", out var t) ? t.GetString() ?? "general" : "general";
      var type = root.TryGetProperty("type", out var typeP) ? typeP.GetString() ?? "update" : "update";
      object? payload = null;
      if (root.TryGetProperty("payload", out var payloadProp))
      {
        try
        {
          payload = JsonSerializer.Deserialize<object>(payloadProp.GetRawText(), jsonOptions);
        }
        catch
        {
          return null;
        }
      }

      return new WebSocketMessage
      {
        Topic = topic,
        Type = type,
        Payload = payload,
        Timestamp = root.TryGetProperty("timestamp", out var ts) && ts.TryGetDateTime(out var dt) ? dt : DateTime.UtcNow,
        RequestId = root.TryGetProperty("request_id", out var rid) ? rid.GetString() : null
      };
    }

    public void Dispose()
    {
      DisconnectAsync().GetAwaiter().GetResult();
      _cancellationTokenSource?.Dispose();
      _webSocket?.Dispose();
    }
  }
}