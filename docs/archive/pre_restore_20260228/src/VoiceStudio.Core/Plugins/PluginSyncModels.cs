// VoiceStudio Plugin Synchronization Models
// Phase 1: Shared models for frontend/backend plugin state synchronization

using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace VoiceStudio.Core.Plugins;

/// <summary>
/// Plugin execution state synchronized between backend and frontend.
/// </summary>
public enum PluginState
{
    /// <summary>Plugin discovered but not loaded.</summary>
    [JsonPropertyName("discovered")]
    Discovered,

    /// <summary>Plugin is being loaded/initialized.</summary>
    [JsonPropertyName("loading")]
    Loading,

    /// <summary>Plugin loaded and active.</summary>
    [JsonPropertyName("active")]
    Active,

    /// <summary>Plugin disabled by user or system.</summary>
    [JsonPropertyName("disabled")]
    Disabled,

    /// <summary>Plugin encountered an error.</summary>
    [JsonPropertyName("error")]
    Error,

    /// <summary>Plugin is being unloaded.</summary>
    [JsonPropertyName("unloading")]
    Unloading
}

/// <summary>
/// Plugin status snapshot for synchronization.
/// </summary>
public sealed class PluginStatus
{
    /// <summary>
    /// Plugin unique identifier (from manifest.name).
    /// </summary>
    [JsonPropertyName("plugin_id")]
    public string PluginId { get; init; } = string.Empty;

    /// <summary>
    /// Current execution state.
    /// </summary>
    [JsonPropertyName("state")]
    public PluginState State { get; init; } = PluginState.Discovered;

    /// <summary>
    /// Plugin version from manifest.
    /// </summary>
    [JsonPropertyName("version")]
    public string Version { get; init; } = "0.0.0";

    /// <summary>
    /// Whether the backend component is loaded.
    /// </summary>
    [JsonPropertyName("backend_loaded")]
    public bool BackendLoaded { get; init; }

    /// <summary>
    /// Whether the frontend component is loaded.
    /// </summary>
    [JsonPropertyName("frontend_loaded")]
    public bool FrontendLoaded { get; init; }

    /// <summary>
    /// Error message if state is Error.
    /// </summary>
    [JsonPropertyName("error_message")]
    public string? ErrorMessage { get; init; }

    /// <summary>
    /// Timestamp of last state change.
    /// </summary>
    [JsonPropertyName("last_updated")]
    public DateTime LastUpdated { get; init; } = DateTime.UtcNow;

    /// <summary>
    /// List of granted permissions.
    /// </summary>
    [JsonPropertyName("granted_permissions")]
    public IReadOnlyList<string> GrantedPermissions { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Health status (for lifecycle monitoring).
    /// </summary>
    [JsonPropertyName("health_status")]
    public string? HealthStatus { get; init; }

    /// <summary>
    /// Human-readable description of the current state.
    /// </summary>
    [JsonIgnore]
    public string StatusDescription => State switch
    {
        PluginState.Discovered => "Discovered",
        PluginState.Loading => "Loading...",
        PluginState.Active => "Active",
        PluginState.Disabled => "Disabled",
        PluginState.Error => ErrorMessage ?? "Error",
        PluginState.Unloading => "Unloading...",
        _ => "Unknown"
    };
}

/// <summary>
/// Message for plugin state synchronization via WebSocket.
/// </summary>
public sealed class PluginSyncMessage
{
    /// <summary>
    /// Message type constant.
    /// </summary>
    public const string MessageType = "plugin_sync";

    /// <summary>
    /// Action to perform or that occurred.
    /// </summary>
    [JsonPropertyName("action")]
    public PluginSyncAction Action { get; init; }

    /// <summary>
    /// Plugin ID for the action (null for bulk operations).
    /// </summary>
    [JsonPropertyName("plugin_id")]
    public string? PluginId { get; init; }

    /// <summary>
    /// Plugin status (for update/add actions).
    /// </summary>
    [JsonPropertyName("status")]
    public PluginStatus? Status { get; init; }

    /// <summary>
    /// Full list of plugin statuses (for sync_all action).
    /// </summary>
    [JsonPropertyName("all_plugins")]
    public IReadOnlyList<PluginStatus>? AllPlugins { get; init; }

    /// <summary>
    /// Request ID for correlation.
    /// </summary>
    [JsonPropertyName("request_id")]
    public string? RequestId { get; init; }

    /// <summary>
    /// Timestamp of the message.
    /// </summary>
    [JsonPropertyName("timestamp")]
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;
}

/// <summary>
/// Plugin synchronization actions.
/// </summary>
public enum PluginSyncAction
{
    /// <summary>Request full state synchronization.</summary>
    [JsonPropertyName("sync_request")]
    SyncRequest,

    /// <summary>Response with all plugin states.</summary>
    [JsonPropertyName("sync_all")]
    SyncAll,

    /// <summary>Single plugin state updated.</summary>
    [JsonPropertyName("state_changed")]
    StateChanged,

    /// <summary>Plugin added/installed.</summary>
    [JsonPropertyName("plugin_added")]
    PluginAdded,

    /// <summary>Plugin removed/uninstalled.</summary>
    [JsonPropertyName("plugin_removed")]
    PluginRemoved,

    /// <summary>Request to enable a plugin.</summary>
    [JsonPropertyName("enable_request")]
    EnableRequest,

    /// <summary>Request to disable a plugin.</summary>
    [JsonPropertyName("disable_request")]
    DisableRequest,

    /// <summary>Permission grant/revoke update.</summary>
    [JsonPropertyName("permission_changed")]
    PermissionChanged
}

/// <summary>
/// Request to change plugin state from frontend.
/// </summary>
public sealed class PluginCommandRequest
{
    /// <summary>
    /// Command type.
    /// </summary>
    [JsonPropertyName("command")]
    public PluginCommand Command { get; init; }

    /// <summary>
    /// Target plugin ID.
    /// </summary>
    [JsonPropertyName("plugin_id")]
    public string PluginId { get; init; } = string.Empty;

    /// <summary>
    /// Optional command parameters.
    /// </summary>
    [JsonPropertyName("parameters")]
    public Dictionary<string, object>? Parameters { get; init; }

    /// <summary>
    /// Request ID for correlation.
    /// </summary>
    [JsonPropertyName("request_id")]
    public string? RequestId { get; init; }
}

/// <summary>
/// Plugin commands that can be sent from frontend to backend.
/// </summary>
public enum PluginCommand
{
    /// <summary>Enable and load a plugin.</summary>
    [JsonPropertyName("enable")]
    Enable,

    /// <summary>Disable and unload a plugin.</summary>
    [JsonPropertyName("disable")]
    Disable,

    /// <summary>Reload a plugin.</summary>
    [JsonPropertyName("reload")]
    Reload,

    /// <summary>Request plugin health check.</summary>
    [JsonPropertyName("health_check")]
    HealthCheck,

    /// <summary>Install a plugin from a path.</summary>
    [JsonPropertyName("install")]
    Install,

    /// <summary>Uninstall a plugin.</summary>
    [JsonPropertyName("uninstall")]
    Uninstall
}

/// <summary>
/// Response to a plugin command.
/// </summary>
public sealed class PluginCommandResponse
{
    /// <summary>
    /// Whether the command succeeded.
    /// </summary>
    [JsonPropertyName("success")]
    public bool Success { get; init; }

    /// <summary>
    /// Result message.
    /// </summary>
    [JsonPropertyName("message")]
    public string? Message { get; init; }

    /// <summary>
    /// Error code if failed.
    /// </summary>
    [JsonPropertyName("error_code")]
    public string? ErrorCode { get; init; }

    /// <summary>
    /// Updated plugin status after command.
    /// </summary>
    [JsonPropertyName("status")]
    public PluginStatus? Status { get; init; }

    /// <summary>
    /// Correlated request ID.
    /// </summary>
    [JsonPropertyName("request_id")]
    public string? RequestId { get; init; }
}
