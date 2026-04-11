using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace VoiceStudio.Core.Services
{
  /// <summary>
  /// WebSocket service interface for real-time communication with backend.
  /// Implements React/TypeScript WebSocket patterns in C#.
  /// </summary>
  public interface IWebSocketService : IDisposable
  {
    /// <summary>
    /// Event fired when WebSocket connection is established.
    /// </summary>
    event EventHandler? Connected;

    /// <summary>
    /// Event fired when WebSocket connection is closed.
    /// </summary>
    event EventHandler<string>? Disconnected;

    /// <summary>
    /// Event fired when an error occurs.
    /// </summary>
    event EventHandler<Exception>? Error;

    /// <summary>
    /// Event fired when a message is received.
    /// </summary>
    event EventHandler<WebSocketMessage>? MessageReceived;

    /// <summary>
    /// Gets the current connection state.
    /// </summary>
    WebSocketState State { get; }

    /// <summary>
    /// Gets whether the WebSocket is connected.
    /// </summary>
    bool IsConnected { get; }

    /// <summary>
    /// Sets static request headers for the WebSocket upgrade handshake (e.g. X-API-Key).
    /// Call before <see cref="ConnectAsync"/>. GAP-058.
    /// </summary>
    void SetAuthHeaders(IReadOnlyDictionary<string, string>? headers);

    /// <summary>
    /// Optional per-connect credential provider (e.g. from <c>IUnifiedAuthService</c>).
    /// Merged with <see cref="SetAuthHeaders"/> and env <c>VOICESTUDIO_API_KEY</c>. GAP-058.
    /// </summary>
    void SetCredentialProvider(Func<IReadOnlyDictionary<string, string>?>? provider);

    /// <summary>
    /// Connects to the WebSocket server with specified topics.
    /// </summary>
    /// <param name="topics">Topics to subscribe to (meters, training, batch, general, quality)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    Task ConnectAsync(string[]? topics = null, System.Threading.CancellationToken cancellationToken = default);

    /// <summary>
    /// Disconnects from the WebSocket server.
    /// </summary>
    Task DisconnectAsync();

    /// <summary>
    /// Subscribes to a topic.
    /// </summary>
    /// <param name="topic">Topic to subscribe to</param>
    Task SubscribeAsync(string topic);

    /// <summary>
    /// Unsubscribes from a topic.
    /// </summary>
    /// <param name="topic">Topic to unsubscribe from</param>
    Task UnsubscribeAsync(string topic);

    /// <summary>
    /// Sends a ping message to keep connection alive.
    /// </summary>
    Task PingAsync();

    /// <summary>
    /// Sends a custom message.
    /// </summary>
    /// <param name="message">Message to send</param>
    Task SendMessageAsync(object message);
  }

  /// <summary>
  /// WebSocket connection state.
  /// </summary>
  public enum WebSocketState
  {
    Disconnected = 0,
    Connecting = 1,
    Connected = 2,
    Disconnecting = 3,
    Error = 4
  }

  /// <summary>
  /// WebSocket message wrapper.
  /// Follows the standardized protocol from backend/api/ws/protocol.py:
  /// - type: Message type (e.g., "data", "error", "ack", "progress", "complete")
  /// - topic: Optional topic for pub/sub patterns
  /// - payload: Message payload data
  /// - timestamp: ISO8601 timestamp
  /// - request_id: Optional correlation ID for request/response patterns
  /// </summary>
  public class WebSocketMessage
  {
    /// <summary>Topic for pub/sub patterns (e.g., "synthesis", "training").</summary>
    public string Topic { get; set; } = string.Empty;

    /// <summary>Message type (e.g., "data", "error", "ack", "progress", "complete", "audio_chunk").</summary>
    public string Type { get; set; } = string.Empty;

    /// <summary>Message payload data.</summary>
    public object? Payload { get; set; }

    /// <summary>Message timestamp.</summary>
    public DateTime Timestamp { get; set; }

    /// <summary>Optional correlation ID for request/response patterns.</summary>
    public string? RequestId { get; set; }
  }

  /// <summary>
  /// Standard WebSocket message type constants matching backend protocol.
  /// Named WsMessageTypes to avoid conflict with System.Net.WebSockets.WebSocketMessageType.
  /// </summary>
  public static class WsMessageTypes
  {
    public const string Data = "data";
    public const string Error = "error";
    public const string Ack = "ack";
    public const string Ping = "ping";
    public const string Pong = "pong";
    public const string Subscribe = "subscribe";
    public const string Unsubscribe = "unsubscribe";
    public const string Start = "start";
    public const string Stop = "stop";
    public const string Complete = "complete";
    public const string Progress = "progress";

    // Audio-specific
    public const string AudioChunk = "audio_chunk";
    public const string AudioComplete = "audio_complete";

    // Conversion-specific
    public const string ConvertedChunk = "converted_chunk";

    // Training-specific
    public const string TrainingUpdate = "training_update";
    public const string TrainingComplete = "training_complete";

    // Visualization
    public const string VisualizationFrame = "visualization_frame";
    public const string MetersUpdate = "meters_update";
  }

  /// <summary>
  /// Standard WebSocket error codes matching backend protocol.
  /// </summary>
  public static class WebSocketErrorCode
  {
    public const string ValidationError = "VALIDATION_ERROR";
    public const string EngineError = "ENGINE_ERROR";
    public const string NotFound = "NOT_FOUND";
    public const string Unavailable = "UNAVAILABLE";
    public const string RateLimited = "RATE_LIMITED";
    public const string Unauthorized = "UNAUTHORIZED";
    public const string InternalError = "INTERNAL_ERROR";
    public const string ConnectionError = "CONNECTION_ERROR";
    public const string Timeout = "TIMEOUT";
  }
}