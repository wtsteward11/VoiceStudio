// VoiceStudio Plugin Bridge Service Implementation
// Phase 1 Gap Fix S-1: Frontend-Backend real-time synchronization via WebSocket

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using VoiceStudio.Core.Plugins;

namespace VoiceStudio.App.Services;

/// <summary>
/// Service for real-time plugin state synchronization between frontend and backend.
/// Connects to the backend WebSocket endpoint at /ws/plugins.
/// </summary>
public sealed class PluginBridgeService : IPluginBridgeService
{
    private readonly ILogger<PluginBridgeService> _logger;
    private readonly ConcurrentDictionary<string, PluginStatus> _pluginStatuses = new();
    private readonly ConcurrentDictionary<string, TaskCompletionSource<JsonDocument>> _pendingRequests = new();
    private readonly JsonSerializerOptions _jsonOptions;
    private readonly Random _jitterRandom = new();

    private ClientWebSocket? _webSocket;
    private CancellationTokenSource? _receiveCts;
    private CancellationTokenSource? _heartbeatCts;
    private Task? _receiveTask;
    private Task? _heartbeatTask;
    private bool _disposed;

    // Reconnection configuration
    private bool _autoReconnect = true;
    private int _maxRetries = 5;
    private int _baseDelayMs = 1000;
    private const int MaxDelayMs = 30000;
    private const int HeartbeatIntervalMs = 30000;
    private string? _lastBackendUrl;

    public PluginBridgeService(ILogger<PluginBridgeService> logger)
    {
        _logger = logger;
        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
            PropertyNameCaseInsensitive = true,
            Converters = { new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower) }
        };
    }

    #region IPluginBridgeService Implementation

    public bool IsConnected => _webSocket?.State == WebSocketState.Open;

    /// <summary>
    /// Gets or sets whether automatic reconnection is enabled.
    /// When true, the service will attempt to reconnect on connection failure
    /// using exponential backoff.
    /// </summary>
    public bool AutoReconnect
    {
        get => _autoReconnect;
        set => _autoReconnect = value;
    }

    public async Task ConnectAsync(string backendUrl, CancellationToken cancellationToken = default)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(PluginBridgeService));

        // Store the URL for potential reconnection
        _lastBackendUrl = backendUrl;

        // Clean up any existing connection
        await CleanupConnectionAsync(fireDisconnected: false).ConfigureAwait(false);

        var wsUrl = backendUrl.TrimEnd('/') + "/ws/plugins";
        if (wsUrl.StartsWith("http://"))
            wsUrl = "ws://" + wsUrl[7..];
        else if (wsUrl.StartsWith("https://"))
            wsUrl = "wss://" + wsUrl[8..];

        _webSocket = new ClientWebSocket();
        _receiveCts = new CancellationTokenSource();
        _heartbeatCts = new CancellationTokenSource();

        try
        {
            await _webSocket.ConnectAsync(new Uri(wsUrl), cancellationToken).ConfigureAwait(false);
            _logger.LogInformation("Connected to plugin bridge at {Url}", wsUrl);

            // Start receiving messages
            _receiveTask = ReceiveLoopAsync(_receiveCts.Token);

            // Start heartbeat to detect stale connections
            _heartbeatTask = HeartbeatLoopAsync(_heartbeatCts.Token);

            Connected?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to connect to plugin bridge at {Url}", wsUrl);
            await CleanupConnectionAsync(fireDisconnected: false).ConfigureAwait(false);
            throw;
        }
    }

    public async Task DisconnectAsync()
    {
        // Disable auto-reconnect when explicitly disconnecting
        _autoReconnect = false;
        await CleanupConnectionAsync().ConfigureAwait(false);
    }

    public IReadOnlyDictionary<string, PluginStatus> GetAllPluginStatuses()
    {
        return new Dictionary<string, PluginStatus>(_pluginStatuses);
    }

    public PluginStatus? GetPluginStatus(string pluginId)
    {
        return _pluginStatuses.TryGetValue(pluginId, out var status) ? status : null;
    }

    public async Task RequestFullSyncAsync(CancellationToken cancellationToken = default)
    {
        var message = new { type = "sync_request", request_id = GenerateRequestId() };
        await SendMessageAsync(message, cancellationToken).ConfigureAwait(false);
    }

    public async Task<PluginCommandResponse> EnablePluginAsync(
        string pluginId,
        CancellationToken cancellationToken = default)
    {
        return await SendPluginCommandAsync("enable", pluginId, cancellationToken).ConfigureAwait(false);
    }

    public async Task<PluginCommandResponse> DisablePluginAsync(
        string pluginId,
        CancellationToken cancellationToken = default)
    {
        return await SendPluginCommandAsync("disable", pluginId, cancellationToken).ConfigureAwait(false);
    }

    public async Task<PluginCommandResponse> ReloadPluginAsync(
        string pluginId,
        CancellationToken cancellationToken = default)
    {
        return await SendPluginCommandAsync("reload", pluginId, cancellationToken).ConfigureAwait(false);
    }

    public async Task<string> CheckPluginHealthAsync(
        string pluginId,
        CancellationToken cancellationToken = default)
    {
        var response = await SendPluginCommandAsync("health_check", pluginId, cancellationToken).ConfigureAwait(false);
        return response.Status?.HealthStatus ?? "unknown";
    }

    #endregion

    #region Events

    public event EventHandler? Connected;
    public event EventHandler<Exception?>? Disconnected;
    public event EventHandler<PluginStateChangedEventArgs>? PluginStateChanged;
    public event EventHandler<PluginStatus>? PluginAdded;
    public event EventHandler<string>? PluginRemoved;
    public event EventHandler<IReadOnlyList<PluginStatus>>? FullSyncReceived;
    public event EventHandler<PluginSyncErrorEventArgs>? SyncError;

    /// <summary>
    /// Raised when the service is attempting to reconnect after a connection failure.
    /// The int parameter is the current retry attempt number (1-based).
    /// </summary>
    public event EventHandler<int>? Reconnecting;

    /// <summary>
    /// Raised when the service has successfully reconnected after a connection failure.
    /// </summary>
    public event EventHandler? Reconnected;

    #endregion

    #region Private Methods

    private async Task<PluginCommandResponse> SendPluginCommandAsync(
        string command,
        string pluginId,
        CancellationToken cancellationToken)
    {
        var requestId = GenerateRequestId();
        var tcs = new TaskCompletionSource<JsonDocument>();
        _pendingRequests[requestId] = tcs;

        try
        {
            var message = new
            {
                type = "plugin_command",
                command,
                plugin_id = pluginId,
                request_id = requestId
            };

            await SendMessageAsync(message, cancellationToken).ConfigureAwait(false);

            using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

            linkedCts.Token.Register(() => tcs.TrySetCanceled());

            var responseDoc = await tcs.Task.ConfigureAwait(false);
            return ParseCommandResponse(responseDoc);
        }
        finally
        {
            _pendingRequests.TryRemove(requestId, out _);
        }
    }

    private async Task SendMessageAsync(object message, CancellationToken cancellationToken)
    {
        if (_webSocket?.State != WebSocketState.Open)
        {
            throw new InvalidOperationException("WebSocket is not connected");
        }

        var json = JsonSerializer.Serialize(message, _jsonOptions);
        var bytes = Encoding.UTF8.GetBytes(json);

        await _webSocket.SendAsync(
            new ArraySegment<byte>(bytes),
            WebSocketMessageType.Text,
            endOfMessage: true,
            cancellationToken).ConfigureAwait(false);
    }

    private async Task ReceiveLoopAsync(CancellationToken cancellationToken)
    {
        var buffer = new byte[8192];
        var messageBuffer = new List<byte>();
        Exception? disconnectException = null;

        try
        {
            while (!cancellationToken.IsCancellationRequested && _webSocket?.State == WebSocketState.Open)
            {
                var result = await _webSocket.ReceiveAsync(
                    new ArraySegment<byte>(buffer),
                    cancellationToken).ConfigureAwait(false);

                if (result.MessageType == WebSocketMessageType.Close)
                {
                    _logger.LogInformation("Server closed WebSocket connection");
                    break;
                }

                messageBuffer.AddRange(new ArraySegment<byte>(buffer, 0, result.Count));

                if (result.EndOfMessage)
                {
                    var json = Encoding.UTF8.GetString(messageBuffer.ToArray());
                    messageBuffer.Clear();

                    try
                    {
                        ProcessMessage(json);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error processing WebSocket message");
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogDebug("Receive loop cancelled");
            return; // Intentional cancellation, don't reconnect
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "WebSocket receive error");
            disconnectException = ex;
        }

        // Connection dropped - attempt reconnection if enabled
        if (_autoReconnect && !cancellationToken.IsCancellationRequested && !_disposed)
        {
            _ = Task.Run(() => ReconnectLoopAsync(disconnectException));
        }
        else
        {
            Disconnected?.Invoke(this, disconnectException);
        }
    }

    private void ProcessMessage(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        if (!root.TryGetProperty("type", out var typeElement))
        {
            _logger.LogWarning("Received message without type: {Json}", json);
            return;
        }

        var messageType = typeElement.GetString();

        switch (messageType)
        {
            case "plugin_sync":
                HandlePluginSyncMessage(root);
                break;

            case "plugin_command_response":
                HandleCommandResponse(root, doc);
                break;

            case "pong":
                // Heartbeat response - ignore
                break;

            case "error":
                HandleErrorMessage(root);
                break;

            default:
                _logger.LogDebug("Unknown message type: {Type}", messageType);
                break;
        }
    }

    private void HandlePluginSyncMessage(JsonElement root)
    {
        if (!root.TryGetProperty("data", out var data))
        {
            _logger.LogWarning("Plugin sync message missing data");
            return;
        }

        if (!data.TryGetProperty("action", out var actionElement))
        {
            _logger.LogWarning("Plugin sync message missing action");
            return;
        }

        var action = actionElement.GetString();

        switch (action)
        {
            case "sync_all":
                HandleFullSync(data);
                break;

            case "state_changed":
                HandleStateChanged(data);
                break;

            case "plugin_added":
                HandlePluginAdded(data);
                break;

            case "plugin_removed":
                HandlePluginRemoved(data);
                break;

            default:
                _logger.LogDebug("Unknown sync action: {Action}", action);
                break;
        }
    }

    private void HandleFullSync(JsonElement data)
    {
        if (!data.TryGetProperty("all_plugins", out var allPlugins))
        {
            _logger.LogWarning("Full sync missing all_plugins");
            return;
        }

        var statuses = new List<PluginStatus>();
        _pluginStatuses.Clear();

        foreach (var pluginElement in allPlugins.EnumerateArray())
        {
            var status = ParsePluginStatus(pluginElement);
            if (status != null)
            {
                _pluginStatuses[status.PluginId] = status;
                statuses.Add(status);
            }
        }

        _logger.LogInformation("Full sync received: {Count} plugins", statuses.Count);
        FullSyncReceived?.Invoke(this, statuses);
    }

    private void HandleStateChanged(JsonElement data)
    {
        var pluginId = data.TryGetProperty("plugin_id", out var idElement) ? idElement.GetString() : null;
        if (string.IsNullOrEmpty(pluginId)) return;

        if (!data.TryGetProperty("status", out var statusElement)) return;

        var newStatus = ParsePluginStatus(statusElement);
        if (newStatus == null) return;

        var previousState = _pluginStatuses.TryGetValue(pluginId, out var oldStatus)
            ? oldStatus.State
            : (PluginState?)null;

        _pluginStatuses[pluginId] = newStatus;

        PluginStateChanged?.Invoke(this, new PluginStateChangedEventArgs
        {
            PluginId = pluginId,
            PreviousState = previousState,
            CurrentState = newStatus.State,
            Status = newStatus
        });
    }

    private void HandlePluginAdded(JsonElement data)
    {
        var pluginId = data.TryGetProperty("plugin_id", out var idElement) ? idElement.GetString() : null;
        if (string.IsNullOrEmpty(pluginId)) return;

        if (!data.TryGetProperty("status", out var statusElement)) return;

        var status = ParsePluginStatus(statusElement);
        if (status == null) return;

        _pluginStatuses[pluginId] = status;
        PluginAdded?.Invoke(this, status);
    }

    private void HandlePluginRemoved(JsonElement data)
    {
        var pluginId = data.TryGetProperty("plugin_id", out var idElement) ? idElement.GetString() : null;
        if (string.IsNullOrEmpty(pluginId)) return;

        _pluginStatuses.TryRemove(pluginId, out _);
        PluginRemoved?.Invoke(this, pluginId);
    }

    private void HandleCommandResponse(JsonElement root, JsonDocument doc)
    {
        var requestId = root.TryGetProperty("request_id", out var idElement) ? idElement.GetString() : null;
        if (string.IsNullOrEmpty(requestId)) return;

        if (_pendingRequests.TryRemove(requestId, out var tcs))
        {
            // Clone the document since we need it to outlive this scope
            var clonedJson = root.GetRawText();
            tcs.TrySetResult(JsonDocument.Parse(clonedJson));
        }
    }

    private void HandleErrorMessage(JsonElement root)
    {
        var message = root.TryGetProperty("error", out var errorElement) ? errorElement.GetString() : "Unknown error";
        var requestId = root.TryGetProperty("request_id", out var idElement) ? idElement.GetString() : null;

        _logger.LogError("Server error: {Message}", message);

        // Raise SyncError event for general sync errors
        SyncError?.Invoke(this, new PluginSyncErrorEventArgs
        {
            Error = message ?? "Unknown error",
            Timestamp = DateTime.UtcNow
        });

        if (!string.IsNullOrEmpty(requestId) && _pendingRequests.TryRemove(requestId, out var tcs))
        {
            tcs.TrySetException(new InvalidOperationException(message));
        }
    }

    private PluginStatus? ParsePluginStatus(JsonElement element)
    {
        try
        {
            var pluginId = element.TryGetProperty("plugin_id", out var idEl) ? idEl.GetString() : null;
            if (string.IsNullOrEmpty(pluginId)) return null;

            var stateStr = element.TryGetProperty("state", out var stateEl) ? stateEl.GetString() : "discovered";
            var state = ParsePluginState(stateStr);

            var permissions = new List<string>();
            if (element.TryGetProperty("granted_permissions", out var permsEl))
            {
                foreach (var perm in permsEl.EnumerateArray())
                {
                    var permStr = perm.GetString();
                    if (!string.IsNullOrEmpty(permStr))
                        permissions.Add(permStr);
                }
            }

            return new PluginStatus
            {
                PluginId = pluginId,
                State = state,
                Version = element.TryGetProperty("version", out var verEl) ? verEl.GetString() ?? "0.0.0" : "0.0.0",
                BackendLoaded = element.TryGetProperty("backend_loaded", out var blEl) && blEl.GetBoolean(),
                FrontendLoaded = element.TryGetProperty("frontend_loaded", out var flEl) && flEl.GetBoolean(),
                ErrorMessage = element.TryGetProperty("error_message", out var errEl) ? errEl.GetString() : null,
                LastUpdated = element.TryGetProperty("last_updated", out var updEl)
                    ? DateTime.TryParse(updEl.GetString(), out var dt) ? dt : DateTime.UtcNow
                    : DateTime.UtcNow,
                GrantedPermissions = permissions,
                HealthStatus = element.TryGetProperty("health_status", out var healthEl) ? healthEl.GetString() : null
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to parse plugin status");
            return null;
        }
    }

    private static PluginState ParsePluginState(string? state) => state?.ToLowerInvariant() switch
    {
        "discovered" => PluginState.Discovered,
        "loading" => PluginState.Loading,
        "active" => PluginState.Active,
        "disabled" => PluginState.Disabled,
        "error" => PluginState.Error,
        "unloading" => PluginState.Unloading,
        _ => PluginState.Discovered
    };

    private PluginCommandResponse ParseCommandResponse(JsonDocument doc)
    {
        var root = doc.RootElement;
        var data = root.TryGetProperty("data", out var dataEl) ? dataEl : root;

        PluginStatus? status = null;
        if (data.TryGetProperty("status", out var statusEl))
        {
            status = ParsePluginStatus(statusEl);
        }

        return new PluginCommandResponse
        {
            Success = data.TryGetProperty("success", out var successEl) && successEl.GetBoolean(),
            Message = data.TryGetProperty("message", out var msgEl) ? msgEl.GetString() : null,
            ErrorCode = data.TryGetProperty("error_code", out var codeEl) ? codeEl.GetString() : null,
            Status = status,
            RequestId = root.TryGetProperty("request_id", out var reqEl) ? reqEl.GetString() : null
        };
    }

    private static string GenerateRequestId() => Guid.NewGuid().ToString("N")[..16];

    /// <summary>
    /// Sends periodic heartbeat pings to detect stale connections.
    /// </summary>
    private async Task HeartbeatLoopAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested && _webSocket?.State == WebSocketState.Open)
            {
                await Task.Delay(HeartbeatIntervalMs, cancellationToken).ConfigureAwait(false);

                if (_webSocket?.State == WebSocketState.Open)
                {
                    try
                    {
                        var pingMessage = new { type = "ping", timestamp = DateTime.UtcNow.ToString("O") };
                        await SendMessageAsync(pingMessage, cancellationToken).ConfigureAwait(false);
                        _logger.LogDebug("Heartbeat ping sent");
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to send heartbeat ping");
                        // Connection may be stale, the receive loop will handle reconnection
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogDebug("Heartbeat loop cancelled");
        }
    }

    /// <summary>
    /// Attempts to reconnect with exponential backoff.
    /// </summary>
    private async Task ReconnectLoopAsync(Exception? originalException)
    {
        if (string.IsNullOrEmpty(_lastBackendUrl))
        {
            _logger.LogWarning("Cannot reconnect: no backend URL available");
            Disconnected?.Invoke(this, originalException);
            return;
        }

        var attempt = 0;
        while (attempt < _maxRetries && !_disposed)
        {
            attempt++;
            Reconnecting?.Invoke(this, attempt);

            // Calculate delay with exponential backoff and jitter
            var delay = Math.Min(_baseDelayMs * (int)Math.Pow(2, attempt - 1), MaxDelayMs);
            var jitter = _jitterRandom.Next(0, delay / 4); // Add up to 25% jitter
            var totalDelay = delay + jitter;

            _logger.LogInformation("Reconnection attempt {Attempt}/{MaxRetries} in {Delay}ms", attempt, _maxRetries, totalDelay);

            try
            {
                await Task.Delay(totalDelay).ConfigureAwait(false);

                if (_disposed) return;

                // Attempt reconnection
                await ConnectAsync(_lastBackendUrl).ConfigureAwait(false);

                // Success!
                _logger.LogInformation("Reconnected successfully on attempt {Attempt}", attempt);
                Reconnected?.Invoke(this, EventArgs.Empty);

                // Request full sync to restore state
                try
                {
                    await RequestFullSyncAsync().ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to request full sync after reconnection");
                }

                return;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Reconnection attempt {Attempt} failed", attempt);
            }
        }

        // All retries exhausted
        _logger.LogError("Failed to reconnect after {MaxRetries} attempts", _maxRetries);
        Disconnected?.Invoke(this, originalException);
    }

    private async Task CleanupConnectionAsync(bool fireDisconnected = true)
    {
        _receiveCts?.Cancel();
        _heartbeatCts?.Cancel();

        if (_receiveTask != null)
        {
            try
            {
                await _receiveTask.ConfigureAwait(false);
            }
            // ALLOWED: empty catch - expected during shutdown, exception is normal cancellation signal
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Error waiting for receive task");
            }
            _receiveTask = null;
        }

        if (_heartbeatTask != null)
        {
            try
            {
                await _heartbeatTask.ConfigureAwait(false);
            }
            // ALLOWED: empty catch - expected during shutdown, exception is normal cancellation signal
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Error waiting for heartbeat task");
            }
            _heartbeatTask = null;
        }

        if (_webSocket != null)
        {
            if (_webSocket.State == WebSocketState.Open)
            {
                try
                {
                    await _webSocket.CloseAsync(
                        WebSocketCloseStatus.NormalClosure,
                        "Disconnecting",
                        CancellationToken.None).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Error closing WebSocket");
                }
            }
            _webSocket.Dispose();
            _webSocket = null;
        }

        _receiveCts?.Dispose();
        _receiveCts = null;

        _heartbeatCts?.Dispose();
        _heartbeatCts = null;

        // Cancel all pending requests
        foreach (var kvp in _pendingRequests)
        {
            kvp.Value.TrySetCanceled();
        }
        _pendingRequests.Clear();

        if (fireDisconnected)
        {
            Disconnected?.Invoke(this, null);
        }
    }

    #endregion

    #region IDisposable

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        CleanupConnectionAsync().GetAwaiter().GetResult();
    }

    #endregion
}
