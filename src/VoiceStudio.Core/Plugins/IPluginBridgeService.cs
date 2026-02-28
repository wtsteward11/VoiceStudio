// VoiceStudio Plugin Bridge Service Interface
// Phase 1 Gap Fix: Frontend-Backend real-time synchronization

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace VoiceStudio.Core.Plugins;

/// <summary>
/// Interface for real-time plugin state synchronization between frontend and backend.
/// Connects to the backend WebSocket endpoint at /ws/plugins.
/// </summary>
public interface IPluginBridgeService : IDisposable
{
    #region Connection State

    /// <summary>
    /// Whether the bridge is currently connected to the backend.
    /// </summary>
    bool IsConnected { get; }

    /// <summary>
    /// Whether to automatically reconnect on connection loss.
    /// Default is true.
    /// </summary>
    bool AutoReconnect { get; set; }

    /// <summary>
    /// Connect to the backend plugin WebSocket endpoint.
    /// </summary>
    /// <param name="backendUrl">Backend URL (e.g., "ws://localhost:8765").</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task ConnectAsync(string backendUrl, CancellationToken cancellationToken = default);

    /// <summary>
    /// Disconnect from the backend.
    /// </summary>
    Task DisconnectAsync();

    #endregion

    #region Plugin State

    /// <summary>
    /// Get the current status of all plugins.
    /// </summary>
    IReadOnlyDictionary<string, PluginStatus> GetAllPluginStatuses();

    /// <summary>
    /// Get the current status of a specific plugin.
    /// </summary>
    /// <param name="pluginId">Plugin identifier.</param>
    /// <returns>Plugin status, or null if not found.</returns>
    PluginStatus? GetPluginStatus(string pluginId);

    /// <summary>
    /// Request a full state synchronization from the backend.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task RequestFullSyncAsync(CancellationToken cancellationToken = default);

    #endregion

    #region Plugin Commands

    /// <summary>
    /// Enable a plugin.
    /// </summary>
    /// <param name="pluginId">Plugin identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Command response.</returns>
    Task<PluginCommandResponse> EnablePluginAsync(
        string pluginId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Disable a plugin.
    /// </summary>
    /// <param name="pluginId">Plugin identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Command response.</returns>
    Task<PluginCommandResponse> DisablePluginAsync(
        string pluginId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Reload a plugin.
    /// </summary>
    /// <param name="pluginId">Plugin identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Command response.</returns>
    Task<PluginCommandResponse> ReloadPluginAsync(
        string pluginId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Request a health check for a plugin.
    /// </summary>
    /// <param name="pluginId">Plugin identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Health status string.</returns>
    Task<string> CheckPluginHealthAsync(
        string pluginId,
        CancellationToken cancellationToken = default);

    #endregion

    #region Events

    /// <summary>
    /// Raised when connected to the backend.
    /// </summary>
    event EventHandler? Connected;

    /// <summary>
    /// Raised when disconnected from the backend.
    /// </summary>
    event EventHandler<Exception?>? Disconnected;

    /// <summary>
    /// Raised when attempting to reconnect after connection loss.
    /// The integer parameter is the current retry attempt number.
    /// </summary>
    event EventHandler<int>? Reconnecting;

    /// <summary>
    /// Raised when successfully reconnected after connection loss.
    /// </summary>
    event EventHandler? Reconnected;

    /// <summary>
    /// Raised when any plugin state changes.
    /// </summary>
    event EventHandler<PluginStateChangedEventArgs>? PluginStateChanged;

    /// <summary>
    /// Raised when a plugin is added.
    /// </summary>
    event EventHandler<PluginStatus>? PluginAdded;

    /// <summary>
    /// Raised when a plugin is removed.
    /// </summary>
    event EventHandler<string>? PluginRemoved;

    /// <summary>
    /// Raised when a full sync is received.
    /// </summary>
    event EventHandler<IReadOnlyList<PluginStatus>>? FullSyncReceived;

    /// <summary>
    /// Raised when a synchronization error occurs.
    /// </summary>
    event EventHandler<PluginSyncErrorEventArgs>? SyncError;

    #endregion
}

/// <summary>
/// Event arguments for plugin state changes.
/// </summary>
public sealed class PluginStateChangedEventArgs : EventArgs
{
    /// <summary>
    /// Plugin identifier.
    /// </summary>
    public string PluginId { get; init; } = string.Empty;

    /// <summary>
    /// Previous state, or null if this is a new plugin.
    /// </summary>
    public PluginState? PreviousState { get; init; }

    /// <summary>
    /// Current state.
    /// </summary>
    public PluginState CurrentState { get; init; }

    /// <summary>
    /// Full plugin status.
    /// </summary>
    public PluginStatus Status { get; init; } = new();

    /// <summary>
    /// New state after the change.
    /// </summary>
    public PluginStatus NewState { get; init; } = new();

    /// <summary>
    /// Whether the plugin was removed.
    /// </summary>
    public bool WasRemoved { get; init; }
}

/// <summary>
/// Event arguments for plugin synchronization errors.
/// </summary>
public sealed class PluginSyncErrorEventArgs : EventArgs
{
    /// <summary>
    /// Error message describing what went wrong.
    /// </summary>
    public string Error { get; init; } = string.Empty;

    /// <summary>
    /// The exception that caused the error, if any.
    /// </summary>
    public Exception? Exception { get; init; }

    /// <summary>
    /// Timestamp when the error occurred.
    /// </summary>
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;
}
