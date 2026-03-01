// VoiceStudio Plugin Schema Validator
// Phase 1: Unified manifest validation for VoiceStudio plugins.

using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Json.Schema;

namespace VoiceStudio.Core.Plugins;

/// <summary>
/// Result of plugin manifest validation.
/// </summary>
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
/// </summary>
public sealed class PluginSchemaValidator
{
    private readonly ILogger<PluginSchemaValidator>? _logger;
    private readonly JsonSchema? _schema;
    private static readonly Regex SemverRegex = new(
        @"^(?<major>\d+)\.(?<minor>\d+)\.(?<patch>\d+)(-(?<prerelease>[a-zA-Z0-9.]+))?(\+(?<build>[a-zA-Z0-9.]+))?$",
        RegexOptions.Compiled
    );

    /// <summary>
    /// Creates a new validator instance.
    /// </summary>
    /// <param name="schemaPath">Path to the schema file. Uses embedded resource if null.</param>
    /// <param name="logger">Optional logger.</param>
    public PluginSchemaValidator(string? schemaPath = null, ILogger<PluginSchemaValidator>? logger = null)
    {
        _logger = logger;
        _schema = LoadSchema(schemaPath);
    }

    private JsonSchema? LoadSchema(string? schemaPath)
    {
        try
        {
            string schemaJson;

            if (!string.IsNullOrEmpty(schemaPath))
            {
                if (!File.Exists(schemaPath))
                {
                    _logger?.LogError("Schema file not found: {Path}", schemaPath);
                    return null;
                }
                schemaJson = File.ReadAllText(schemaPath);
            }
            else
            {
                var assemblyDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
                if (assemblyDir == null)
                {
                    _logger?.LogError("Could not determine assembly directory");
                    return null;
                }

                var current = new DirectoryInfo(assemblyDir);
                while (current != null)
                {
                    var schemaFilePath = Path.Combine(current.FullName, "shared", "schemas", "plugin-manifest.schema.json");
                    if (File.Exists(schemaFilePath))
                    {
                        schemaJson = File.ReadAllText(schemaFilePath);
                        _logger?.LogDebug("Loaded schema from {Path}", schemaFilePath);
                        return JsonSchema.FromText(schemaJson);
                    }
                    current = current.Parent;
                }

                _logger?.LogError("Could not find plugin-manifest.schema.json");
                return null;
            }

            return JsonSchema.FromText(schemaJson);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to load plugin schema");
            return null;
        }
    }

    /// <summary>
    /// Validates a plugin manifest from a JSON element (e.g. from parsed manifest).
    /// </summary>
    /// <param name="manifestElement">Root JSON element of the manifest.</param>
    /// <returns>Validation result with errors and parsed manifest.</returns>
    public PluginValidationResult Validate(JsonElement manifestElement)
    {
        var errors = new List<string>();

        if (_schema != null)
        {
            var result = _schema.Evaluate(manifestElement);
            if (!result.IsValid)
            {
                CollectSchemaErrors(result, errors);
            }
        }
        else
        {
            errors.Add("Schema validator not initialized - schema file may be missing");
        }

        PluginManifest? manifest = null;
        try
        {
            var json = manifestElement.GetRawText();
            manifest = JsonSerializer.Deserialize<PluginManifest>(json);
        }
        catch (JsonException ex)
        {
            errors.Add($"Failed to parse manifest: {ex.Message}");
        }

        if (manifest != null)
        {
            var semanticErrors = ValidateSemantics(manifest);
            errors.AddRange(semanticErrors);
        }

        return new PluginValidationResult
        {
            IsValid = errors.Count == 0,
            Errors = errors,
            Manifest = errors.Count == 0 ? manifest : null
        };
    }

    /// <summary>
    /// Validates a plugin manifest file.
    /// </summary>
    /// <param name="filePath">Path to manifest.json file.</param>
    /// <returns>Validation result.</returns>
    public PluginValidationResult ValidateFile(string filePath)
    {
        if (!File.Exists(filePath))
        {
            return new PluginValidationResult
            {
                IsValid = false,
                Errors = new[] { $"Manifest file not found: {filePath}" }
            };
        }

        try
        {
            var json = File.ReadAllText(filePath);
            using var doc = JsonDocument.Parse(json);
            return Validate(doc.RootElement);
        }
        catch (JsonException ex)
        {
            return new PluginValidationResult
            {
                IsValid = false,
                Errors = new[] { $"Invalid JSON in manifest: {ex.Message}" }
            };
        }
        catch (Exception ex)
        {
            return new PluginValidationResult
            {
                IsValid = false,
                Errors = new[] { $"Failed to read manifest: {ex.Message}" }
            };
        }
    }

    private List<string> ValidateSemantics(PluginManifest manifest)
    {
        var errors = new List<string>();

        var pluginType = manifest.PluginType;
        var entryPoints = manifest.EntryPoints;

        bool hasBackend = !string.IsNullOrEmpty(entryPoints?.Backend);
        bool hasFrontend = !string.IsNullOrEmpty(entryPoints?.Frontend);

        switch (pluginType)
        {
            case "full_stack":
                if (!hasBackend)
                    errors.Add("full_stack plugin requires entry_points.backend");
                if (!hasFrontend)
                    errors.Add("full_stack plugin requires entry_points.frontend");
                break;

            case "backend_only":
                if (!hasBackend)
                    errors.Add("backend_only plugin requires entry_points.backend");
                if (hasFrontend)
                    errors.Add("backend_only plugin should not have entry_points.frontend");
                break;

            case "frontend_only":
                if (!hasFrontend)
                    errors.Add("frontend_only plugin requires entry_points.frontend");
                if (hasBackend)
                    errors.Add("frontend_only plugin should not have entry_points.backend");
                break;
        }

        var capabilities = manifest.Capabilities;
        if (capabilities != null)
        {
            if (capabilities.BackendRoutes && pluginType == "frontend_only")
                errors.Add("frontend_only plugin cannot have backend_routes capability");

            if (capabilities.UiPanels?.Count > 0 && pluginType == "backend_only")
                errors.Add("backend_only plugin cannot have ui_panels capability");
        }

        if (!string.IsNullOrEmpty(manifest.Version) && !IsValidSemver(manifest.Version))
            errors.Add($"Invalid semver format: {manifest.Version}");

        if (!string.IsNullOrEmpty(manifest.MinAppVersion) && !IsValidSemver(manifest.MinAppVersion))
            errors.Add($"Invalid min_app_version format: {manifest.MinAppVersion}");

        if (!string.IsNullOrEmpty(manifest.MinApiVersion) && !IsValidSemver(manifest.MinApiVersion))
            errors.Add($"Invalid min_api_version format: {manifest.MinApiVersion}");

        return errors;
    }

    private static bool IsValidSemver(string version)
    {
        return SemverRegex.IsMatch(version);
    }

    private static void CollectSchemaErrors(EvaluationResults result, List<string> errors)
    {
        if (result.Errors != null)
        {
            var path = result.InstanceLocation.ToString();
            if (string.IsNullOrEmpty(path))
                path = "root";
            foreach (var kvp in result.Errors)
            {
                errors.Add($"[{path}] {kvp.Value}");
            }
        }

        if (result.Details != null)
        {
            foreach (var detail in result.Details)
            {
                CollectSchemaErrors(detail, errors);
            }
        }
    }
}
