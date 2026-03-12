using System;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using VoiceStudio.Core.Models;
using VoiceStudio.Core.Services;
using VoiceStudio.App.Helpers;
using VoiceStudio.App.Logging;

namespace VoiceStudio.App.Services
{
  /// <summary>
  /// Service for managing application settings with backend API integration and local storage fallback.
  /// </summary>
  public class SettingsService : ISettingsService
  {
    private readonly IBackendClient _backendClient;
    private readonly JsonSerializerOptions _jsonOptions;
    private const string LocalSettingsKey = "Settings";

    // Cache for settings to avoid repeated loads
    private SettingsData? _cachedSettings;
    private DateTime _cacheTimestamp = DateTime.MinValue;
    private readonly TimeSpan _cacheExpiration = TimeSpan.FromMinutes(5);
    private readonly SemaphoreSlim _cacheLock = new SemaphoreSlim(1, 1);

    public SettingsService(IBackendClient backendClient)
    {
      _backendClient = backendClient ?? throw new ArgumentNullException(nameof(backendClient));
      _jsonOptions = new JsonSerializerOptions
      {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
      };
    }

    /// <summary>
    /// Loads all settings from backend or local storage.
    /// </summary>
    public async Task<SettingsData> LoadSettingsAsync(CancellationToken cancellationToken = default)
    {
      // Check cache first
      await _cacheLock.WaitAsync(cancellationToken);
      try
      {
        if (_cachedSettings != null && DateTime.UtcNow - _cacheTimestamp < _cacheExpiration)
        {
          return _cachedSettings;
        }
      }
      finally
      {
        _cacheLock.Release();
      }

      try
      {
        // Try to load from backend first
        var settings = await _backendClient.GetAsync<SettingsData>("/api/settings", cancellationToken).ConfigureAwait(false);
        if (settings != null)
        {
          // Validate and apply defaults if needed
          if (ValidateSettings(settings, out _))
          {
            // Also save to local storage as backup
            await SaveToLocalStorageAsync(settings).ConfigureAwait(false);

            // Update cache
            await _cacheLock.WaitAsync(cancellationToken);
            try
            {
              _cachedSettings = settings;
              _cacheTimestamp = DateTime.UtcNow;
            }
            finally
            {
              _cacheLock.Release();
            }

            return settings;
          }
        }
      }
      catch (Exception ex)
      {
        ErrorLogger.LogWarning($"Best effort operation failed: {ex.Message}", "SettingsService.Task");
      }

      // Fall back to local storage
      var localSettings = await LoadFromLocalStorageAsync().ConfigureAwait(false);

      // Update cache
      await _cacheLock.WaitAsync(cancellationToken);
      try
      {
        _cachedSettings = localSettings;
        _cacheTimestamp = DateTime.UtcNow;
      }
      finally
      {
        _cacheLock.Release();
      }

      return localSettings;
    }

    /// <summary>
    /// Loads settings for a specific category.
    /// </summary>
    public async Task<T?> LoadCategoryAsync<T>(string category, CancellationToken cancellationToken = default) where T : class
    {
      try
      {
        var categorySettings = await _backendClient.GetAsync<T>($"/api/settings/{category}", cancellationToken);
        if (categorySettings != null)
        {
          return categorySettings;
        }
      }
      catch (Exception ex)
      {
        ErrorLogger.LogWarning($"Best effort operation failed: {ex.Message}", "SettingsService.Task");
      }

      // Fall back to local storage
      var allSettings = await LoadFromLocalStorageAsync().ConfigureAwait(false);
      return GetCategoryFromSettings<T>(allSettings, category);
    }

    /// <summary>
    /// Saves all settings to backend and local storage.
    /// </summary>
    public async Task SaveSettingsAsync(SettingsData settings, CancellationToken cancellationToken = default)
    {
      // Validate settings before saving
      if (!ValidateSettings(settings, out var errorMessage))
      {
        throw new ArgumentException($"Invalid settings: {errorMessage}", nameof(settings));
      }

      try
      {
        // Save to backend
        await _backendClient.PostAsync<SettingsData, SettingsData>("/api/settings", settings, cancellationToken).ConfigureAwait(false);
      }
      catch (Exception ex)
      {
        ErrorLogger.LogWarning($"Best effort operation failed: {ex.Message}", "SettingsService.SaveSettingsAsync");
      }

      // Always save to local storage as backup
      await SaveToLocalStorageAsync(settings).ConfigureAwait(false);

      // Update cache
      await _cacheLock.WaitAsync(cancellationToken);
      try
      {
        _cachedSettings = settings;
        _cacheTimestamp = DateTime.UtcNow;
      }
      finally
      {
        _cacheLock.Release();
      }
    }

    /// <summary>
    /// Updates settings for a specific category.
    /// </summary>
    public async Task<T> UpdateCategoryAsync<T>(string category, T categorySettings, CancellationToken cancellationToken = default) where T : class
    {
      try
      {
        // Update via backend (PUT method)
        var updated = await _backendClient.PutAsync<T, T>($"/api/settings/{category}", categorySettings, cancellationToken).ConfigureAwait(false);
        if (updated != null)
        {
          // Update local storage
          var allSettings = await LoadFromLocalStorageAsync().ConfigureAwait(false);
          SetCategoryInSettings(allSettings, category, categorySettings);
          await SaveToLocalStorageAsync(allSettings).ConfigureAwait(false);

          // Invalidate cache
          await _cacheLock.WaitAsync(cancellationToken);
          try
          {
            _cachedSettings = null;
          }
          finally
          {
            _cacheLock.Release();
          }

          return updated;
        }
      }
      catch (Exception ex)
      {
        ErrorLogger.LogWarning($"Best effort operation failed: {ex.Message}", "SettingsService.Task");
      }

      // Update local storage
      var localSettings = await LoadFromLocalStorageAsync().ConfigureAwait(false);
      SetCategoryInSettings(localSettings, category, categorySettings);
      await SaveToLocalStorageAsync(localSettings).ConfigureAwait(false);

      // Invalidate cache
      await _cacheLock.WaitAsync(cancellationToken);
      try
      {
        _cachedSettings = null;
      }
      finally
      {
        _cacheLock.Release();
      }

      return categorySettings;
    }

    /// <summary>
    /// Resets all settings to defaults.
    /// </summary>
    public async Task<SettingsData> ResetSettingsAsync(CancellationToken cancellationToken = default)
    {
      var defaultSettings = GetDefaultSettings();

      try
      {
        // Reset via backend
        var reset = await _backendClient.PostAsync<object, SettingsData>("/api/settings/reset", new { }, cancellationToken);
        if (reset != null)
        {
          await SaveToLocalStorageAsync(reset);
          return reset;
        }
      }
      catch (Exception ex)
      {
        ErrorLogger.LogWarning($"Best effort operation failed: {ex.Message}", "SettingsService.Task");
      }

      // Reset local storage
      await SaveToLocalStorageAsync(defaultSettings);
      return defaultSettings;
    }

    /// <summary>
    /// Validates settings data.
    /// </summary>
    public bool ValidateSettings(SettingsData settings, out string? errorMessage)
    {
      errorMessage = null;

      if (settings == null)
      {
        errorMessage = "Settings cannot be null";
        return false;
      }

      // Validate general settings
      if (settings.General != null)
      {
        if (string.IsNullOrWhiteSpace(settings.General.Theme))
        {
          errorMessage = "Theme cannot be empty";
          return false;
        }
        if (settings.General.AutoSaveInterval < 0)
        {
          errorMessage = "Auto-save interval must be non-negative";
          return false;
        }
      }

      // Validate engine settings
      if (settings.Engine != null)
      {
        if (settings.Engine.QualityLevel < 1 || settings.Engine.QualityLevel > 10)
        {
          errorMessage = "Quality level must be between 1 and 10";
          return false;
        }
      }

      // Validate audio settings
      if (settings.Audio != null)
      {
        if (settings.Audio.SampleRate <= 0)
        {
          errorMessage = "Sample rate must be positive";
          return false;
        }
        if (settings.Audio.BufferSize <= 0)
        {
          errorMessage = "Buffer size must be positive";
          return false;
        }
      }

      // Validate timeline settings
      if (settings.Timeline != null)
      {
        if (settings.Timeline.SnapInterval < 0)
        {
          errorMessage = "Snap interval must be non-negative";
          return false;
        }
        if (settings.Timeline.GridInterval < 0)
        {
          errorMessage = "Grid interval must be non-negative";
          return false;
        }
      }

      // Validate backend settings
      if (settings.Backend != null)
      {
        if (string.IsNullOrWhiteSpace(settings.Backend.ApiUrl))
        {
          errorMessage = "API URL cannot be empty";
          return false;
        }
        if (settings.Backend.Timeout <= 0)
        {
          errorMessage = "Timeout must be positive";
          return false;
        }
        if (settings.Backend.RetryCount < 0)
        {
          errorMessage = "Retry count must be non-negative";
          return false;
        }
      }

      // Validate performance settings
      if (settings.Performance != null)
      {
        if (settings.Performance.CacheSize < 0)
        {
          errorMessage = "Cache size must be non-negative";
          return false;
        }
        if (settings.Performance.MaxThreads <= 0)
        {
          errorMessage = "Max threads must be positive";
          return false;
        }
        if (settings.Performance.MemoryLimit <= 0)
        {
          errorMessage = "Memory limit must be positive";
          return false;
        }
      }

      return true;
    }

    /// <summary>
    /// Gets default settings.
    /// </summary>
    public SettingsData GetDefaultSettings()
    {
      return new SettingsData
      {
        General = new GeneralSettings
        {
          Theme = "Dark",
          Language = "en-US",
          AutoSave = true,
          AutoSaveInterval = 300
        },
        Engine = new EngineSettings
        {
          DefaultAudioEngine = "xtts",
          DefaultImageEngine = "sdxl",
          DefaultVideoEngine = "svd",
          QualityLevel = 5
        },
        Audio = new AudioSettings
        {
          OutputDevice = "Default",
          InputDevice = "Default",
          SampleRate = 44100,
          BufferSize = 1024
        },
        Timeline = new TimelineSettings
        {
          TimeFormat = "Timecode",
          SnapEnabled = true,
          SnapInterval = 0.1,
          GridEnabled = true,
          GridInterval = 1.0
        },
        Backend = new BackendSettings
        {
          ApiUrl = "http://localhost:8000",
          Timeout = 30,
          RetryCount = 3
        },
        Performance = new PerformanceSettings
        {
          CachingEnabled = true,
          CacheSize = 512,
          MaxThreads = 4,
          MemoryLimit = 4096
        },
        Plugins = new PluginSettings
        {
          EnabledPlugins = new System.Collections.Generic.List<string>()
        },
        Mcp = new McpSettings
        {
          Enabled = false,
          ServerUrl = "http://localhost:8080"
        }
      };
    }

    /// <summary>
    /// Loads settings from local storage.
    /// </summary>
    private Task<SettingsData> LoadFromLocalStorageAsync()
    {
      try
      {
        // Use UnpackagedSettingsHelper for file-based settings (works for both packaged and unpackaged apps)
        var json = UnpackagedSettingsHelper.GetValue<string>(LocalSettingsKey, null);
        if (!string.IsNullOrWhiteSpace(json))
        {
          var settings = JsonSerializer.Deserialize<SettingsData>(json, _jsonOptions);
          if (settings != null && ValidateSettings(settings, out _))
          {
            return Task.FromResult(settings);
          }
        }
      }
      catch (Exception ex)
      {
        ErrorLogger.LogWarning($"Best effort operation failed: {ex.Message}", "SettingsService.Unknown");
      }

      // Return defaults if loading fails
      return Task.FromResult(GetDefaultSettings());
    }

    /// <summary>
    /// Saves settings to local storage.
    /// </summary>
    private Task SaveToLocalStorageAsync(SettingsData settings)
    {
      try
      {
        // Use UnpackagedSettingsHelper for file-based settings (works for both packaged and unpackaged apps)
        var json = JsonSerializer.Serialize(settings, _jsonOptions);
        UnpackagedSettingsHelper.SetValue(LocalSettingsKey, json);
      }
      catch (Exception ex)
      {
        ErrorLogger.LogWarning($"Best effort operation failed: {ex.Message}", "SettingsService.SaveToLocalStorageAsync");
      }

      return Task.CompletedTask;
    }

    /// <summary>
    /// Gets a category from settings data.
    /// </summary>
    private T? GetCategoryFromSettings<T>(SettingsData settings, string category) where T : class
    {
      return category.ToLowerInvariant() switch
      {
        "general" => settings.General as T,
        "engine" => settings.Engine as T,
        "audio" => settings.Audio as T,
        "timeline" => settings.Timeline as T,
        "backend" => settings.Backend as T,
        "performance" => settings.Performance as T,
        "plugins" => settings.Plugins as T,
        "mcp" => settings.Mcp as T,
        _ => null
      };
    }

    /// <summary>
    /// Sets a category in settings data.
    /// </summary>
    private void SetCategoryInSettings(SettingsData settings, string category, object categorySettings)
    {
      switch (category.ToLowerInvariant())
      {
        case "general":
          settings.General = categorySettings as GeneralSettings;
          break;
        case "engine":
          settings.Engine = categorySettings as EngineSettings;
          break;
        case "audio":
          settings.Audio = categorySettings as AudioSettings;
          break;
        case "timeline":
          settings.Timeline = categorySettings as TimelineSettings;
          break;
        case "backend":
          settings.Backend = categorySettings as BackendSettings;
          break;
        case "performance":
          settings.Performance = categorySettings as PerformanceSettings;
          break;
        case "plugins":
          settings.Plugins = categorySettings as PluginSettings;
          break;
        case "mcp":
          settings.Mcp = categorySettings as McpSettings;
          break;
      }
    }
  }
}