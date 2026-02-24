using System;
using VoiceStudio.App.Logging;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using VoiceStudio.Core.Plugins;
using VoiceStudio.Core.Panels;
using VoiceStudio.Core.Services;
using VoiceStudio.App.Services;

namespace VoiceStudio.App.Services
{
  /// <summary>
  /// Manages loading and registration of VoiceStudio plugins.
  /// </summary>
  public class PluginManager
  {
    private readonly List<IPlugin> _plugins = new();
    private readonly IPanelRegistry _panelRegistry;
    private readonly IBackendClient _backendClient;
    private readonly IErrorLoggingService? _errorLoggingService;
    private readonly PluginSchemaValidator _schemaValidator;
    private readonly string _pluginsDirectory;
    private bool _initialized;

    public PluginManager(
        IPanelRegistry panelRegistry,
        IBackendClient backendClient,
        string? pluginsDirectory = null,
        string? schemaPath = null)
    {
      _panelRegistry = panelRegistry;
      _backendClient = backendClient;
      try
      {
        _errorLoggingService = ServiceProvider.GetErrorLoggingService();
      }
      catch
      {
        // ErrorLoggingService not available, continue without it
        _errorLoggingService = null;
      }
      _pluginsDirectory = pluginsDirectory ?? Path.Combine(
          AppDomain.CurrentDomain.BaseDirectory,
          "Plugins"
      );
      
      // Initialize schema validator (Phase 1)
      _schemaValidator = new PluginSchemaValidator(schemaPath);
    }

    /// <summary>
    /// Get all loaded plugins.
    /// </summary>
    public IReadOnlyList<IPlugin> Plugins => _plugins.AsReadOnly();

    /// <summary>
    /// Get plugin by name.
    /// </summary>
    public IPlugin? GetPlugin(string name)
    {
      return _plugins.FirstOrDefault(p => p.Name == name);
    }

    /// <summary>
    /// Load all plugins from the plugins directory.
    /// </summary>
    public async Task LoadPluginsAsync()
    {
      if (_initialized)
      {
        return;
      }

      if (!Directory.Exists(_pluginsDirectory))
      {
        Directory.CreateDirectory(_pluginsDirectory);
        return;
      }

      foreach (var pluginDir in Directory.GetDirectories(_pluginsDirectory))
      {
        try
        {
          await LoadPluginAsync(pluginDir);
        }
        catch (Exception ex)
        {
          // Log error but continue loading other plugins
          var errorMessage = $"Failed to load plugin from {pluginDir}: {ex.Message}";
          ErrorLogger.LogWarning(errorMessage, "PluginManager");

          _errorLoggingService?.LogError(
              ex,
              "Plugin Loading",
              new Dictionary<string, object>
              {
                            { "PluginDirectory", pluginDir },
                            { "Action", "LoadPlugin" }
              }
          );
        }
      }

      _initialized = true;
    }

    /// <summary>
    /// Load a single plugin from directory.
    /// </summary>
    private async Task LoadPluginAsync(string pluginDirectory)
    {
      var manifestPath = Path.Combine(pluginDirectory, "manifest.json");
      if (!File.Exists(manifestPath))
      {
        return; // Skip directories without manifest
      }

      // Validate and load manifest using unified schema validator (Phase 1)
      // Use Task.Run to avoid blocking on file I/O
      var validationResult = await Task.Run(() => _schemaValidator.ValidateFile(manifestPath));
      
      if (!validationResult.IsValid)
      {
        var errors = string.Join("; ", validationResult.Errors);
        throw new InvalidOperationException(
            $"Manifest validation failed in {pluginDirectory}: {errors}"
        );
      }

      var manifest = validationResult.Manifest;
      if (manifest == null)
      {
        throw new InvalidOperationException(
            $"Invalid manifest.json in {pluginDirectory}"
        );
      }

      // Check for C# plugin assembly (use entry_points.frontend or Name + Plugin.dll)
      var assemblyName = manifest.EntryPoints?.Frontend ?? $"{manifest.Name}Plugin.dll";
      var pluginAssemblyPath = Path.Combine(
          pluginDirectory,
          assemblyName
      );

      if (File.Exists(pluginAssemblyPath))
      {
        // Load C# plugin
        var assembly = Assembly.LoadFrom(pluginAssemblyPath);
        var pluginType = assembly.GetTypes()
            .FirstOrDefault(t => typeof(IPlugin).IsAssignableFrom(t) && !t.IsInterface);

        if (pluginType != null)
        {
          var plugin = (IPlugin)Activator.CreateInstance(pluginType)!;

          // Register panels
          plugin.RegisterPanels(_panelRegistry);

          // Initialize
          plugin.Initialize(_backendClient);

          _plugins.Add(plugin);
        }
      }
    }

    /// <summary>
    /// Unload all plugins.
    /// </summary>
    public void UnloadPlugins()
    {
      foreach (var plugin in _plugins)
      {
        try
        {
          plugin.Cleanup();
        }
        catch (Exception ex)
        {
          var errorMessage = $"Error cleaning up plugin {plugin.Name}: {ex.Message}";
          ErrorLogger.LogWarning(errorMessage, "PluginManager");

          _errorLoggingService?.LogError(
              ex,
              "Plugin Cleanup",
              new Dictionary<string, object>
              {
                            { "PluginName", plugin.Name },
                            { "Action", "Cleanup" }
              }
          );
        }
      }

      _plugins.Clear();
      _initialized = false;
    }
  }
}