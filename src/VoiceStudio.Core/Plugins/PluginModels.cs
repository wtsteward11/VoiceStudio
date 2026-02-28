using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace VoiceStudio.Core.Plugins;

public sealed class PluginValidationResult
{
    /// <summary>
    /// Gets whether the manifest is valid.
    /// </summary>
    public bool IsValid { get; init; }

    /// <summary>
    /// Gets the list of validation errors.
    /// </summary>
    public IReadOnlyList<string> Errors { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Gets the parsed manifest if valid, null otherwise.
    /// </summary>
    public PluginManifest? Manifest { get; init; }
}

/// <summary>
/// Unified plugin manifest model matching the JSON schema.
/// </summary>
public sealed class PluginManifest
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("display_name")]
    public string? DisplayName { get; set; }

    [JsonPropertyName("version")]
    public string Version { get; set; } = string.Empty;

    [JsonPropertyName("author")]
    public string Author { get; set; } = string.Empty;

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("long_description")]
    public string? LongDescription { get; set; }

    [JsonPropertyName("plugin_type")]
    public string PluginType { get; set; } = string.Empty;

    [JsonPropertyName("min_app_version")]
    public string? MinAppVersion { get; set; }

    [JsonPropertyName("min_api_version")]
    public string? MinApiVersion { get; set; }

    [JsonPropertyName("capabilities")]
    public PluginCapabilities? Capabilities { get; set; }

    [JsonPropertyName("entry_points")]
    public PluginEntryPoints? EntryPoints { get; set; }

    [JsonPropertyName("dependencies")]
    public PluginDependencies? Dependencies { get; set; }

    [JsonPropertyName("permissions")]
    public List<string>? Permissions { get; set; }

    [JsonPropertyName("settings_schema")]
    public JsonElement? SettingsSchema { get; set; }

    [JsonPropertyName("metadata")]
    public PluginMetadata? Metadata { get; set; }
}

/// <summary>
/// Plugin capabilities configuration.
/// </summary>
public sealed class PluginCapabilities
{
    [JsonPropertyName("backend_routes")]
    public bool BackendRoutes { get; set; }

    [JsonPropertyName("ui_panels")]
    public List<string>? UiPanels { get; set; }

    [JsonPropertyName("engines")]
    public List<string>? Engines { get; set; }

    [JsonPropertyName("effects")]
    public List<string>? Effects { get; set; }

    [JsonPropertyName("export_formats")]
    public List<string>? ExportFormats { get; set; }

    [JsonPropertyName("import_formats")]
    public List<string>? ImportFormats { get; set; }

    [JsonPropertyName("integrations")]
    public List<string>? Integrations { get; set; }

    [JsonPropertyName("mcp_integration")]
    public JsonElement? McpIntegration { get; set; }
}

/// <summary>
/// Plugin entry points configuration.
/// </summary>
public sealed class PluginEntryPoints
{
    [JsonPropertyName("backend")]
    public string? Backend { get; set; }

    [JsonPropertyName("frontend")]
    public string? Frontend { get; set; }
}

/// <summary>
/// Plugin dependencies configuration.
/// </summary>
public sealed class PluginDependencies
{
    [JsonPropertyName("python")]
    public List<string>? Python { get; set; }

    [JsonPropertyName("plugins")]
    public List<string>? Plugins { get; set; }

    [JsonPropertyName("system")]
    public List<string>? System { get; set; }
}

/// <summary>
/// Plugin metadata configuration.
/// </summary>
public sealed class PluginMetadata
{
    [JsonPropertyName("homepage")]
    public string? Homepage { get; set; }

    [JsonPropertyName("repository")]
    public string? Repository { get; set; }

    [JsonPropertyName("documentation")]
    public string? Documentation { get; set; }

    [JsonPropertyName("license")]
    public string? License { get; set; }

    [JsonPropertyName("tags")]
    public List<string>? Tags { get; set; }

    [JsonPropertyName("icon")]
    public string? Icon { get; set; }
}

/// <summary>
/// Validates plugin manifests against the unified JSON schema.
/// PluginSchemaValidator removed — requires JsonSchema.Net not in current csproj.
/// </summary>

