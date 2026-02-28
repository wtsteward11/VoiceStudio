using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
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
    private readonly string _pluginsDirectory;
    private bool _initialized;

    public PluginManager(
        IPanelRegistry panelRegistry,
        IBackendClient backendClient,
        string? pluginsDirectory = null)
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
          System.Diagnostics.Debug.WriteLine(errorMessage);

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

      // Load manifest
      var manifestJson = await File.ReadAllTextAsync(manifestPath);
      var manifest = JsonSerializer.Deserialize<PluginManifest>(manifestJson);

      if (manifest == null)
      {
        throw new InvalidOperationException(
            $"Invalid manifest.json in {pluginDirectory}"
        );
      }

      // Check for C# plugin assembly
      var pluginAssemblyPath = Path.Combine(
          pluginDirectory,
          $"{manifest.Name}Plugin.dll"
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
          System.Diagnostics.Debug.WriteLine(errorMessage);

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

    /// <summary>
    /// Plugin manifest model.
    /// </summary>
    private class PluginManifest
    {
      public string Name { get; set; } = string.Empty;
      public string Version { get; set; } = string.Empty;
      public string Author { get; set; } = string.Empty;
      public string Description { get; set; } = string.Empty;
    }
  }
}