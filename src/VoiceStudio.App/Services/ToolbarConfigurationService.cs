using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using VoiceStudio.Core.Models;

namespace VoiceStudio.App.Services
{
  /// <summary>
  /// Service for managing customizable command toolbar configuration.
  /// Implements IDEA 18: Customizable Command Toolbar.
  /// </summary>
  public class ToolbarConfigurationService
  {
    private const string ConfigFileName = "toolbar_config.json";
    private const string PresetsFileName = "toolbar_presets.json";
    private ToolbarConfiguration? _currentConfiguration;
    private readonly List<ToolbarPreset> _presets;

    public event EventHandler<ToolbarConfigurationChangedEventArgs>? ConfigurationChanged;

    public ToolbarConfigurationService()
    {
      _presets = new List<ToolbarPreset>();
      InitializeDefaultPresets();
    }

    /// <summary>
    /// Gets the current toolbar configuration.
    /// </summary>
    public ToolbarConfiguration GetConfiguration()
    {
      if (_currentConfiguration == null)
      {
        // Use synchronous load to avoid deadlock when called from UI thread
        _currentConfiguration = LoadConfigurationSync() ?? CreateDefaultConfiguration();
      }
      return _currentConfiguration;
    }

    /// <summary>
    /// Updates the toolbar configuration.
    /// </summary>
    public async Task UpdateConfigurationAsync(ToolbarConfiguration configuration)
    {
      _currentConfiguration = configuration;
      await SaveConfigurationAsync(configuration);
      ConfigurationChanged?.Invoke(this, new ToolbarConfigurationChangedEventArgs(configuration));
    }

    /// <summary>
    /// Gets all available toolbar presets.
    /// </summary>
    public IReadOnlyList<ToolbarPreset> GetPresets()
    {
      return _presets.AsReadOnly();
    }

    /// <summary>
    /// Applies a preset to the toolbar configuration.
    /// </summary>
    public async Task ApplyPresetAsync(string presetName)
    {
      var preset = _presets.FirstOrDefault(p => p.Name == presetName);
      if (preset != null)
      {
        await UpdateConfigurationAsync(preset.Configuration);
      }
    }

    /// <summary>
    /// Saves a custom preset.
    /// </summary>
    public async Task SavePresetAsync(string name, ToolbarConfiguration configuration)
    {
      var preset = new ToolbarPreset
      {
        Name = name,
        Configuration = configuration,
        IsCustom = true
      };

      _presets.Add(preset);
      await SavePresetsAsync();
    }

    /// <summary>
    /// Deletes a custom preset.
    /// </summary>
    public async Task DeletePresetAsync(string presetName)
    {
      var preset = _presets.FirstOrDefault(p => p.Name == presetName && p.IsCustom);
      if (preset != null)
      {
        _presets.Remove(preset);
        await SavePresetsAsync();
      }
    }

    private void InitializeDefaultPresets()
    {
      // Default preset
      _presets.Add(new ToolbarPreset
      {
        Name = "Default",
        Configuration = CreateDefaultConfiguration(),
        IsCustom = false
      });

      // Minimal preset
      _presets.Add(new ToolbarPreset
      {
        Name = "Minimal",
        Configuration = CreateMinimalConfiguration(),
        IsCustom = false
      });

      // Full preset
      _presets.Add(new ToolbarPreset
      {
        Name = "Full",
        Configuration = CreateFullConfiguration(),
        IsCustom = false
      });
    }

    private ToolbarConfiguration CreateDefaultConfiguration()
    {
      return new ToolbarConfiguration
      {
        Items = new ObservableCollection<ToolbarItem>
                {
                    new ToolbarItem { Id = "play", Label = "Play", Icon = "▶", IsVisible = true, Order = 0, Section = ToolbarSection.Transport },
                    new ToolbarItem { Id = "pause", Label = "Pause", Icon = "⏸", IsVisible = true, Order = 1, Section = ToolbarSection.Transport },
                    new ToolbarItem { Id = "stop", Label = "Stop", Icon = "⏹", IsVisible = true, Order = 2, Section = ToolbarSection.Transport },
                    new ToolbarItem { Id = "record", Label = "Record", Icon = "⏺", IsVisible = true, Order = 3, Section = ToolbarSection.Transport },
                    new ToolbarItem { Id = "loop", Label = "Loop", Icon = "↻", IsVisible = true, Order = 4, Section = ToolbarSection.Transport },
                    new ToolbarItem { Id = "project", Label = "Project", Icon = "📁", IsVisible = true, Order = 5, Section = ToolbarSection.Project },
                    new ToolbarItem { Id = "import_audio", Label = "Import Audio", Icon = "📥", IsVisible = true, Order = 6, Section = ToolbarSection.Project },
                    new ToolbarItem { Id = "engine", Label = "Engine", Icon = "⚙", IsVisible = true, Order = 7, Section = ToolbarSection.Project },
                    new ToolbarItem { Id = "undo", Label = "Undo", Icon = "↶", IsVisible = true, Order = 8, Section = ToolbarSection.History },
                    new ToolbarItem { Id = "redo", Label = "Redo", Icon = "↷", IsVisible = true, Order = 9, Section = ToolbarSection.History },
                    new ToolbarItem { Id = "workspace", Label = "Workspace", Icon = "🖥", IsVisible = true, Order = 10, Section = ToolbarSection.Workspace },
                    new ToolbarItem { Id = "cpu", Label = "CPU", Icon = "💻", IsVisible = true, Order = 11, Section = ToolbarSection.Performance },
                    new ToolbarItem { Id = "gpu", Label = "GPU", Icon = "🎮", IsVisible = true, Order = 12, Section = ToolbarSection.Performance },
                    new ToolbarItem { Id = "latency", Label = "Latency", Icon = "⏱", IsVisible = true, Order = 13, Section = ToolbarSection.Performance }
                }
      };
    }

    private ToolbarConfiguration CreateMinimalConfiguration()
    {
      return new ToolbarConfiguration
      {
        Items = new ObservableCollection<ToolbarItem>
                {
                    new ToolbarItem { Id = "play", Label = "Play", Icon = "▶", IsVisible = true, Order = 0, Section = ToolbarSection.Transport },
                    new ToolbarItem { Id = "stop", Label = "Stop", Icon = "⏹", IsVisible = true, Order = 1, Section = ToolbarSection.Transport },
                    new ToolbarItem { Id = "project", Label = "Project", Icon = "📁", IsVisible = true, Order = 2, Section = ToolbarSection.Project }
                }
      };
    }

    private ToolbarConfiguration CreateFullConfiguration()
    {
      // Add any additional items for "Full" preset
      return CreateDefaultConfiguration();
    }

    private static string GetLocalFolderPath()
    {
      var appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
      var folderPath = Path.Combine(appDataPath, "VoiceStudio");
      Directory.CreateDirectory(folderPath);
      return folderPath;
    }

    private async Task<ToolbarConfiguration?> LoadConfigurationAsync()
    {
      try
      {
        // Use regular file I/O for unpackaged app compatibility
        var configFilePath = Path.Combine(GetLocalFolderPath(), ConfigFileName);
        if (File.Exists(configFilePath))
        {
          var json = await File.ReadAllTextAsync(configFilePath);
          return JsonSerializer.Deserialize<ToolbarConfiguration>(json);
        }
      }
      catch (Exception ex)
      {
        System.Diagnostics.Debug.WriteLine($"Failed to load toolbar configuration: {ex.Message}");
      }

      return null;
    }

    /// <summary>
    /// Synchronously loads toolbar configuration to avoid UI thread deadlock.
    /// </summary>
    private ToolbarConfiguration? LoadConfigurationSync()
    {
      try
      {
        var configFilePath = Path.Combine(GetLocalFolderPath(), ConfigFileName);
        if (File.Exists(configFilePath))
        {
          var json = File.ReadAllText(configFilePath);
          return JsonSerializer.Deserialize<ToolbarConfiguration>(json);
        }
      }
      catch (Exception ex)
      {
        System.Diagnostics.Debug.WriteLine($"Failed to load toolbar configuration: {ex.Message}");
      }

      return null;
    }

    private async Task SaveConfigurationAsync(ToolbarConfiguration configuration)
    {
      try
      {
        // Use regular file I/O for unpackaged app compatibility
        var configFilePath = Path.Combine(GetLocalFolderPath(), ConfigFileName);
        var json = JsonSerializer.Serialize(configuration, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(configFilePath, json);
      }
      catch (Exception ex)
      {
        System.Diagnostics.Debug.WriteLine($"Failed to save toolbar configuration: {ex.Message}");
      }
    }

    private async Task SavePresetsAsync()
    {
      try
      {
        // Use regular file I/O for unpackaged app compatibility
        var presetsFilePath = Path.Combine(GetLocalFolderPath(), PresetsFileName);
        var customPresets = _presets.Where(p => p.IsCustom).ToList();
        var json = JsonSerializer.Serialize(customPresets, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(presetsFilePath, json);
      }
      catch (Exception ex)
      {
        System.Diagnostics.Debug.WriteLine($"Failed to save toolbar presets: {ex.Message}");
      }
    }
  }

  /// <summary>
  /// Toolbar configuration model.
  /// </summary>
  public class ToolbarConfiguration
  {
    public ObservableCollection<ToolbarItem> Items { get; set; } = new ObservableCollection<ToolbarItem>();
  }

  /// <summary>
  /// Toolbar item model.
  /// </summary>
  public class ToolbarItem
  {
    public string Id { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public string Icon { get; set; } = string.Empty;
    public bool IsVisible { get; set; } = true;
    public int Order { get; set; }
    public ToolbarSection Section { get; set; }
  }

  /// <summary>
  /// Toolbar section enum.
  /// </summary>
  public enum ToolbarSection
  {
    Transport = 0,
    Project = 1,
    History = 2,
    Workspace = 3,
    Performance = 4
  }

  /// <summary>
  /// Toolbar preset model.
  /// </summary>
  public class ToolbarPreset
  {
    public string Name { get; set; } = string.Empty;
    public ToolbarConfiguration Configuration { get; set; } = new ToolbarConfiguration();
    public bool IsCustom { get; set; }
  }

  /// <summary>
  /// Event arguments for toolbar configuration changes.
  /// </summary>
  public class ToolbarConfigurationChangedEventArgs : EventArgs
  {
    public ToolbarConfiguration Configuration { get; }

    public ToolbarConfigurationChangedEventArgs(ToolbarConfiguration configuration)
    {
      Configuration = configuration;
    }
  }
}