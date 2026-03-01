using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VoiceStudio.App.Helpers;
using VoiceStudio.App.Services;
using VoiceStudio.App.Utilities;
using VoiceStudio.Core.Models;
using CoreSettingsData = VoiceStudio.Core.Models.SettingsData;
using VoiceStudio.Core.Panels;
using VoiceStudio.Core.Services;
using Windows.UI;
using VoiceStudio.App.Logging;

namespace VoiceStudio.App.ViewModels
{
  /// <summary>
  /// ViewModel for settings panel.
  /// </summary>
  public partial class SettingsViewModel : BaseViewModel, IPanelView
  {
    public string PanelId => "settings";
    public string DisplayName => ResourceHelper.GetString("Panel.Settings.DisplayName", "Settings");
    public PanelRegion Region => PanelRegion.Floating;
    private readonly ISettingsService _settingsService;
    private readonly IBackendClient _backendClient;
    private readonly PluginManager? _pluginManager;
    private readonly ITelemetryService? _telemetryService;

    // General Settings
    [ObservableProperty]
    private string theme = "Dark";

    [ObservableProperty]
    private string language = "en-US";

    [ObservableProperty]
    private bool autoSave = true;

    [ObservableProperty]
    private int autoSaveInterval = 300; // seconds

    // Engine Settings
    [ObservableProperty]
    private string defaultAudioEngine = "xtts";

    [ObservableProperty]
    private string defaultImageEngine = "sdxl";

    [ObservableProperty]
    private string defaultVideoEngine = "svd";

    [ObservableProperty]
    private int qualityLevel = 5; // 1-10

    // Audio Settings
    [ObservableProperty]
    private string audioOutputDevice = "Default";

    [ObservableProperty]
    private string audioInputDevice = "Default";

    [ObservableProperty]
    private int sampleRate = 44100;

    [ObservableProperty]
    private int bufferSize = 1024;

    // Timeline Settings
    [ObservableProperty]
    private string timeFormat = "Timecode";

    [ObservableProperty]
    private bool snapEnabled = true;

    [ObservableProperty]
    private double snapInterval = 0.1; // seconds

    [ObservableProperty]
    private bool gridEnabled = true;

    [ObservableProperty]
    private double gridInterval = 1.0; // seconds

    // Backend Settings
    [ObservableProperty]
    private string apiUrl = "http://localhost:8000";

    [ObservableProperty]
    private int apiTimeout = 30; // seconds

    [ObservableProperty]
    private int apiRetryCount = 3;

    // Performance Settings
    [ObservableProperty]
    private bool cachingEnabled = true;

    [ObservableProperty]
    private int cacheSize = 512; // MB

    [ObservableProperty]
    private int maxThreads = 4;

    [ObservableProperty]
    private int memoryLimit = 4096; // MB

    // Plugin Settings
    [ObservableProperty]
    private ObservableCollection<PluginInfo> plugins = new();

    [ObservableProperty]
    private PluginInfo? selectedPlugin;

    [ObservableProperty]
    private bool isLoadingPlugins;

    // MCP Settings
    [ObservableProperty]
    private bool mcpEnabled;

    [ObservableProperty]
    private string mcpServerUrl = "http://localhost:8080";

    // Diagnostics Settings
    [ObservableProperty]
    private bool telemetryEnabled;

    [ObservableProperty]
    private bool crashReportingEnabled = true;

    [ObservableProperty]
    private bool includeLogsInCrashReport = true;

    // System/Dependency Status
    [ObservableProperty]
    private ObservableCollection<DependencyStatusItem> dependencyStatusList = new();

    [ObservableProperty]
    private int totalDependencies;

    [ObservableProperty]
    private int installedDependencies;

    [ObservableProperty]
    private int missingDependencies;

    // UI State - use inherited IsLoading, ErrorMessage, StatusMessage from BaseViewModel
    // Do NOT declare [ObservableProperty] here as it shadows the base class properties

    [ObservableProperty]
    private bool hasUnsavedChanges;

    public ObservableCollection<string> AvailableThemes { get; } = new()
        {
            "Light",
            "Dark",
            "System"
        };

    public ObservableCollection<string> AvailableLanguages { get; } = new()
        {
            "en-US",
            "zh-CN",
            "ja-JP",
            "es-ES",
            "fr-FR",
            "de-DE"
        };

    public ObservableCollection<string> AvailableTimeFormats { get; } = new()
        {
            "Timecode",
            "Seconds",
            "Samples",
            "Bars/Beats"
        };

    public ObservableCollection<string> AvailableAudioEngines { get; } = new()
        {
            "xtts",
            "coqui",
            "bark",
            "tortoise",
            "vits",
            "piper",
            "marytts",
            "festival",
            "espeak_ng",
            "rhvoice",
            "openvoice",
            "gpt_sovits",
            "mockingbird"
        };

    public ObservableCollection<string> AvailableImageEngines { get; } = new()
        {
            "sdxl",
            "sdxl_comfy",
            "comfyui",
            "automatic1111",
            "sdnext",
            "invokeai",
            "fooocus",
            "localai",
            "realistic_vision",
            "openjourney",
            "sd_cpu",
            "fastsd_cpu"
        };

    public ObservableCollection<string> AvailableVideoEngines { get; } = new()
        {
            "svd",
            "deforum",
            "fomm",
            "sadtalker",
            "deepfacelab",
            "moviepy",
            "ffmpeg_ai",
            "video_creator",
            "voice_ai"
        };

    public SettingsViewModel(
        IViewModelContext context,
        ISettingsService settingsService,
        IBackendClient backendClient,
        PluginManager? pluginManager = null,
        ITelemetryService? telemetryService = null)
        : base(context)
    {
      _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
      _backendClient = backendClient ?? throw new ArgumentNullException(nameof(backendClient));
      _pluginManager = pluginManager;
      _telemetryService = telemetryService;

      // Initialize commands in primary constructor so all construction paths have them
      LoadSettingsCommand = new EnhancedAsyncRelayCommand(async (ct) =>
      {
        using var profiler = PerformanceProfiler.StartCommand("LoadSettings");
        await LoadSettingsAsync(ct);
      });
      SaveSettingsCommand = new EnhancedAsyncRelayCommand(async (ct) =>
      {
        using var profiler = PerformanceProfiler.StartCommand("SaveSettings");
        await SaveSettingsAsync(ct);
      }, () => HasUnsavedChanges);
      ResetSettingsCommand = new EnhancedAsyncRelayCommand(async (ct) =>
      {
        using var profiler = PerformanceProfiler.StartCommand("ResetSettings");
        await ResetSettingsAsync(ct);
      });
      RefreshPluginsCommand = new EnhancedAsyncRelayCommand(async (ct) =>
      {
        using var profiler = PerformanceProfiler.StartCommand("RefreshPlugins");
        await RefreshPluginsAsync(ct);
      });
      RefreshDependencyStatusCommand = new EnhancedAsyncRelayCommand(async (ct) =>
      {
        using var profiler = PerformanceProfiler.StartCommand("RefreshDependencyStatus");
        await RefreshDependencyStatusAsync(ct);
      });

      // Load plugins on initialization
      _ = LoadPluginsAsync();

      // Load dependency status on initialization
      _ = RefreshDependencyStatusAsync(CancellationToken.None);
    }

    public SettingsViewModel(
        ISettingsService settingsService,
        IBackendClient? backendClient = null,
        PluginManager? pluginManager = null)
        : this(
              AppServices.GetRequiredService<IViewModelContext>(),
              settingsService,
              backendClient ?? AppServices.GetBackendClient(),
              pluginManager ?? AppServices.GetService<PluginManager>(),
              AppServices.GetService<ITelemetryService>())
    {
      // Commands are initialized in the primary constructor
    }

    public IAsyncRelayCommand LoadSettingsCommand { get; } = null!;
    public IAsyncRelayCommand SaveSettingsCommand { get; } = null!;
    public IAsyncRelayCommand ResetSettingsCommand { get; } = null!;
    public IAsyncRelayCommand RefreshPluginsCommand { get; } = null!;
    public IAsyncRelayCommand RefreshDependencyStatusCommand { get; } = null!;

    private async Task LoadSettingsAsync(CancellationToken cancellationToken)
    {
      IsLoading = true;
      ErrorMessage = null;
      StatusMessage = ResourceHelper.GetString("Settings.LoadingSettings", "Loading settings...");

      try
      {
        // Load settings from service (backend or local storage)
        var settings = await _settingsService.LoadSettingsAsync(cancellationToken);

        ApplySettings(settings);
        HasUnsavedChanges = false;
        StatusMessage = ResourceHelper.GetString("Settings.SettingsLoaded", "Settings loaded successfully");
      }
      catch (OperationCanceledException)
      {
        return; // User cancelled
      }
      catch (Exception ex)
      {
        ErrorMessage = ResourceHelper.FormatString("Settings.LoadSettingsFailed", ex.Message);
        StatusMessage = null;
        await HandleErrorAsync(ex, "LoadSettings");
      }
      finally
      {
        IsLoading = false;
      }
    }

    private async Task SaveSettingsAsync(CancellationToken cancellationToken)
    {
      IsLoading = true;
      ErrorMessage = null;
      StatusMessage = ResourceHelper.GetString("Settings.SavingSettings", "Saving settings...");

      try
      {
        var settings = GetSettingsData();

        // Save via service (backend and local storage)
        await _settingsService.SaveSettingsAsync(settings, cancellationToken);

        var telemetryService = _telemetryService;
        if (telemetryService != null && settings.Diagnostics != null)
        {
          telemetryService.ApplyDiagnosticsSettings(settings.Diagnostics);
        }

        HasUnsavedChanges = false;
        StatusMessage = ResourceHelper.GetString("Settings.SettingsSaved", "Settings saved successfully");
      }
      catch (OperationCanceledException)
      {
        return; // User cancelled
      }
      catch (Exception ex)
      {
        ErrorMessage = ResourceHelper.FormatString("Settings.SaveSettingsFailed", ex.Message);
        StatusMessage = null;
        await HandleErrorAsync(ex, "SaveSettings");
      }
      finally
      {
        IsLoading = false;
      }
    }

    private async Task ResetSettingsAsync(CancellationToken cancellationToken)
    {
      IsLoading = true;
      ErrorMessage = null;
      StatusMessage = ResourceHelper.GetString("Settings.ResettingSettings", "Resetting settings...");

      try
      {
        // Reset to defaults
        ResetToDefaults();

        // Save reset settings
        await SaveSettingsAsync(cancellationToken);
      }
      catch (OperationCanceledException)
      {
        return; // User cancelled
      }
      catch (Exception ex)
      {
        ErrorMessage = ResourceHelper.FormatString("Settings.ResetSettingsFailed", ex.Message);
        StatusMessage = null;
        await HandleErrorAsync(ex, "ResetSettings");
      }
      finally
      {
        IsLoading = false;
      }
    }

    private async Task LoadPluginsAsync()
    {
      if (_pluginManager == null)
        return;

      IsLoadingPlugins = true;

      try
      {
        await _pluginManager.LoadPluginsAsync();
        UpdatePluginList();
      }
      catch (OperationCanceledException)
      {
        return; // User cancelled
      }
      catch (Exception ex)
      {
        ErrorMessage = $"Failed to load plugins: {ex.Message}";
        await HandleErrorAsync(ex, "LoadPlugins");
      }
      finally
      {
        IsLoadingPlugins = false;
      }
    }

    private async Task RefreshPluginsAsync(CancellationToken cancellationToken)
    {
      await LoadPluginsAsync();
      StatusMessage = ResourceHelper.GetString("Settings.PluginsRefreshed", "Plugins refreshed");
    }

    private void UpdatePluginList()
    {
      if (_pluginManager == null)
        return;

      Plugins.Clear();
      foreach (var plugin in _pluginManager.Plugins)
      {
        Plugins.Add(new PluginInfo
        {
          Name = plugin.Name,
          Version = plugin.Version,
          Author = plugin.Author,
          Description = plugin.Description,
          IsEnabled = true, // Plugins are enabled by default when loaded
          IsInitialized = plugin.IsInitialized
        });
      }
    }

    private void ApplySettings(CoreSettingsData settings)
    {
      // General
      Theme = settings.General?.Theme ?? "Dark";
      Language = settings.General?.Language ?? "en-US";
      AutoSave = settings.General?.AutoSave ?? true;
      AutoSaveInterval = settings.General?.AutoSaveInterval ?? 300;

      // Engine
      DefaultAudioEngine = settings.Engine?.DefaultAudioEngine ?? "xtts";
      DefaultImageEngine = settings.Engine?.DefaultImageEngine ?? "sdxl";
      DefaultVideoEngine = settings.Engine?.DefaultVideoEngine ?? "svd";
      QualityLevel = settings.Engine?.QualityLevel ?? 5;

      // Audio
      AudioOutputDevice = settings.Audio?.OutputDevice ?? "Default";
      AudioInputDevice = settings.Audio?.InputDevice ?? "Default";
      SampleRate = settings.Audio?.SampleRate ?? 44100;
      BufferSize = settings.Audio?.BufferSize ?? 1024;

      // Timeline
      TimeFormat = settings.Timeline?.TimeFormat ?? "Timecode";
      SnapEnabled = settings.Timeline?.SnapEnabled ?? true;
      SnapInterval = settings.Timeline?.SnapInterval ?? 0.1;
      GridEnabled = settings.Timeline?.GridEnabled ?? true;
      GridInterval = settings.Timeline?.GridInterval ?? 1.0;

      // Backend
      ApiUrl = settings.Backend?.ApiUrl ?? "http://localhost:8000";
      ApiTimeout = settings.Backend?.Timeout ?? 30;
      ApiRetryCount = settings.Backend?.RetryCount ?? 3;

      // Performance
      CachingEnabled = settings.Performance?.CachingEnabled ?? true;
      CacheSize = settings.Performance?.CacheSize ?? 512;
      MaxThreads = settings.Performance?.MaxThreads ?? 4;
      MemoryLimit = settings.Performance?.MemoryLimit ?? 4096;

      // Plugins - Load plugins from PluginManager if available
      if (_pluginManager != null)
      {
        _ = LoadPluginsAsync();
      }

      // MCP
      McpEnabled = settings.Mcp?.Enabled ?? false;
      McpServerUrl = settings.Mcp?.ServerUrl ?? "http://localhost:8080";

      // Diagnostics
      TelemetryEnabled = settings.Diagnostics?.TelemetryEnabled ?? false;
      CrashReportingEnabled = settings.Diagnostics?.CrashReportingEnabled ?? true;
      IncludeLogsInCrashReport = settings.Diagnostics?.IncludeLogsInCrashReport ?? true;
    }

    private CoreSettingsData GetSettingsData()
    {
      return new CoreSettingsData
      {
        General = new VoiceStudio.Core.Models.GeneralSettings
        {
          Theme = Theme,
          Language = Language,
          AutoSave = AutoSave,
          AutoSaveInterval = AutoSaveInterval
        },
        Engine = new VoiceStudio.Core.Models.EngineSettings
        {
          DefaultAudioEngine = DefaultAudioEngine,
          DefaultImageEngine = DefaultImageEngine,
          DefaultVideoEngine = DefaultVideoEngine,
          QualityLevel = QualityLevel
        },
        Audio = new VoiceStudio.Core.Models.AudioSettings
        {
          OutputDevice = AudioOutputDevice,
          InputDevice = AudioInputDevice,
          SampleRate = SampleRate,
          BufferSize = BufferSize
        },
        Timeline = new VoiceStudio.Core.Models.TimelineSettings
        {
          TimeFormat = TimeFormat,
          SnapEnabled = SnapEnabled,
          SnapInterval = SnapInterval,
          GridEnabled = GridEnabled,
          GridInterval = GridInterval
        },
        Backend = new VoiceStudio.Core.Models.BackendSettings
        {
          ApiUrl = ApiUrl,
          Timeout = ApiTimeout,
          RetryCount = ApiRetryCount
        },
        Performance = new VoiceStudio.Core.Models.PerformanceSettings
        {
          CachingEnabled = CachingEnabled,
          CacheSize = CacheSize,
          MaxThreads = MaxThreads,
          MemoryLimit = MemoryLimit
        },
        Plugins = new VoiceStudio.Core.Models.PluginSettings
        {
          EnabledPlugins = new System.Collections.Generic.List<string>(
                  Plugins.Where(p => p.IsEnabled).Select(p => p.Name)
              )
        },
        Mcp = new VoiceStudio.Core.Models.McpSettings
        {
          Enabled = McpEnabled,
          ServerUrl = McpServerUrl
        },
        Diagnostics = new VoiceStudio.Core.Models.DiagnosticsSettings
        {
          TelemetryEnabled = TelemetryEnabled,
          CrashReportingEnabled = CrashReportingEnabled,
          IncludeLogsInCrashReport = IncludeLogsInCrashReport
        }
      };
    }

    private void ResetToDefaults()
    {
      Theme = "Dark";
      Language = "en-US";
      AutoSave = true;
      AutoSaveInterval = 300;
      DefaultAudioEngine = "xtts";
      DefaultImageEngine = "sdxl";
      DefaultVideoEngine = "svd";
      QualityLevel = 5;
      AudioOutputDevice = "Default";
      AudioInputDevice = "Default";
      SampleRate = 44100;
      BufferSize = 1024;
      TimeFormat = "Timecode";
      SnapEnabled = true;
      SnapInterval = 0.1;
      GridEnabled = true;
      GridInterval = 1.0;
      ApiUrl = "http://localhost:8000";
      ApiTimeout = 30;
      ApiRetryCount = 3;
      CachingEnabled = true;
      CacheSize = 512;
      MaxThreads = 4;
      MemoryLimit = 4096;
      // Plugins are managed by PluginManager, not reset here
      McpEnabled = false;
      McpServerUrl = "http://localhost:8080";
      TelemetryEnabled = false;
      CrashReportingEnabled = true;
      IncludeLogsInCrashReport = true;
    }

    private Task LoadFromLocalStorageAsync(CancellationToken cancellationToken)
    {
      cancellationToken.ThrowIfCancellationRequested();

      try
      {
        // Use UnpackagedSettingsHelper for file-based settings (works for both packaged and unpackaged apps)
        var generalJson = UnpackagedSettingsHelper.GetValue<string>("Settings.General", null);
        if (!string.IsNullOrEmpty(generalJson))
        {
          var general = System.Text.Json.JsonSerializer.Deserialize<GeneralSettings>(generalJson);
          if (general != null)
          {
            Theme = general.Theme;
            Language = general.Language;
            AutoSave = general.AutoSave;
            AutoSaveInterval = general.AutoSaveInterval;
          }
        }

        var engineJson = UnpackagedSettingsHelper.GetValue<string>("Settings.Engine", null);
        if (!string.IsNullOrEmpty(engineJson))
        {
          var engine = System.Text.Json.JsonSerializer.Deserialize<EngineSettings>(engineJson);
          if (engine != null)
          {
            DefaultAudioEngine = engine.DefaultAudioEngine;
            DefaultImageEngine = engine.DefaultImageEngine;
            DefaultVideoEngine = engine.DefaultVideoEngine;
            QualityLevel = engine.QualityLevel;
          }
        }

        var backendJson = UnpackagedSettingsHelper.GetValue<string>("Settings.Backend", null);
        if (!string.IsNullOrEmpty(backendJson))
        {
          var backend = System.Text.Json.JsonSerializer.Deserialize<BackendSettings>(backendJson);
          if (backend != null)
          {
            ApiUrl = backend.ApiUrl;
            ApiTimeout = backend.Timeout;
            ApiRetryCount = backend.RetryCount;
          }
        }

        var diagnosticsJson = UnpackagedSettingsHelper.GetValue<string>("Settings.Diagnostics", null);
        if (!string.IsNullOrEmpty(diagnosticsJson))
        {
          var diagnostics = System.Text.Json.JsonSerializer.Deserialize<DiagnosticsSettings>(diagnosticsJson);
          if (diagnostics != null)
          {
            TelemetryEnabled = diagnostics.TelemetryEnabled;
            CrashReportingEnabled = diagnostics.CrashReportingEnabled;
            IncludeLogsInCrashReport = diagnostics.IncludeLogsInCrashReport;
          }
        }
      }
      catch (OperationCanceledException)
      {
        throw; // Re-throw cancellation
      }
      catch (Exception ex)
      {
        // If loading fails, use defaults and log silently (non-critical background operation)
        ErrorLoggingService?.LogWarning($"Settings load failed, using defaults: {ex.Message}", "LoadSettings");
        ResetToDefaults();
      }

      return Task.CompletedTask;
    }

    private Task SaveToLocalStorageAsync(SettingsData settings)
    {
      try
      {
        // Use UnpackagedSettingsHelper for file-based settings (works for both packaged and unpackaged apps)
        if (settings.General != null)
        {
          UnpackagedSettingsHelper.SetValue("Settings.General", System.Text.Json.JsonSerializer.Serialize(settings.General));
        }

        if (settings.Engine != null)
        {
          UnpackagedSettingsHelper.SetValue("Settings.Engine", System.Text.Json.JsonSerializer.Serialize(settings.Engine));
        }

        if (settings.Backend != null)
        {
          UnpackagedSettingsHelper.SetValue("Settings.Backend", System.Text.Json.JsonSerializer.Serialize(settings.Backend));
        }

        if (settings.Diagnostics != null)
        {
          UnpackagedSettingsHelper.SetValue("Settings.Diagnostics", System.Text.Json.JsonSerializer.Serialize(settings.Diagnostics));
        }
      }
      catch (Exception ex)
      {
        // Log save failure but don't interrupt user workflow
        ErrorLoggingService?.LogWarning($"Failed to save settings to local storage: {ex.Message}", "SaveSettings");
      }

      return Task.CompletedTask;
    }

    protected override void OnPropertyChanged(System.ComponentModel.PropertyChangedEventArgs e)
    {
      base.OnPropertyChanged(e);
      if (e.PropertyName != nameof(HasUnsavedChanges) &&
          e.PropertyName != nameof(IsLoading) &&
          e.PropertyName != nameof(ErrorMessage) &&
          e.PropertyName != nameof(StatusMessage))
      {
        HasUnsavedChanges = true;
      }
    }

    private async Task RefreshDependencyStatusAsync(CancellationToken cancellationToken)
    {
      IsLoading = true;
      ErrorMessage = null;

      try
      {
        // Define known dependencies based on MISSING_DEPENDENCIES_AUDIT
        var dependencies = new List<DependencyStatusItem>
                {
                    new DependencyStatusItem { Name = "TensorFlow", Description = "Required for DeepFaceLab engine", Category = "Engine", IsInstalled = false },
                    new DependencyStatusItem { Name = "SpeechBrain", Description = "Required for Speaker Encoder and Quality Metrics", Category = "Engine", IsInstalled = false },
                    new DependencyStatusItem { Name = "OpenCV (cv2)", Description = "Required for image/video processing engines", Category = "Engine", IsInstalled = false },
                    new DependencyStatusItem { Name = "Face Alignment", Description = "Required for FOMM and SadTalker engines", Category = "Engine", IsInstalled = false },
                    new DependencyStatusItem { Name = "Librosa", Description = "Required for audio processing and quality metrics", Category = "Audio", IsInstalled = false },
                    new DependencyStatusItem { Name = "SoundFile", Description = "Required for audio I/O in multiple engines", Category = "Audio", IsInstalled = false },
                    new DependencyStatusItem { Name = "PyLoudNorm", Description = "Required for LUFS metering", Category = "Audio", IsInstalled = false },
                    new DependencyStatusItem { Name = "Resemblyzer", Description = "Optional - Alternative to SpeechBrain", Category = "Engine", IsInstalled = false, IsOptional = true },
                    new DependencyStatusItem { Name = "PyTorch", Description = "Required for quality calculations and ML engines", Category = "Engine", IsInstalled = false },
                    new DependencyStatusItem { Name = "NumPy", Description = "Required for numerical operations", Category = "Core", IsInstalled = false }
                };

        // Check dependency status via backend
        try
        {
          ErrorLogger.LogDebug("Starting dependency check...", "DEP-CHECK");
          var response = await _backendClient.SendRequestAsync<object, Dictionary<string, object>>(
              "/api/settings/check/dependencies",
              null,
              System.Net.Http.HttpMethod.Get,
              cancellationToken
          );

          ErrorLogger.LogDebug($"Response received: {response != null}", "DEP-CHECK");
          if (response != null)
          {
            ErrorLogger.LogDebug($"Response keys: {string.Join(", ", response.Keys)}", "DEP-CHECK");
            int matchCount = 0;
            foreach (var dep in dependencies)
            {
              var key = dep.Name.ToLower().Replace(" ", "_");
              ErrorLogger.LogDebug($"Looking for key: '{key}'", "DEP-CHECK");
              if (response.TryGetValue(key, out var statusObj))
              {
                ErrorLogger.LogDebug($"Found! Type: {statusObj?.GetType().Name ?? "null"}, Value: {statusObj}", "DEP-CHECK");
                if (statusObj is bool isInstalled)
                {
                  dep.IsInstalled = isInstalled;
                  matchCount++;
                }
                else if (statusObj is System.Text.Json.JsonElement jsonElement)
                {
                  ErrorLogger.LogDebug($"JsonElement ValueKind: {jsonElement.ValueKind}", "DEP-CHECK");
                  // Handle JsonElement (System.Text.Json deserializes to this type)
                  if (jsonElement.ValueKind == System.Text.Json.JsonValueKind.True)
                  {
                    dep.IsInstalled = true;
                    matchCount++;
                  }
                  else if (jsonElement.ValueKind == System.Text.Json.JsonValueKind.False)
                    dep.IsInstalled = false;
                  else if (jsonElement.ValueKind == System.Text.Json.JsonValueKind.String)
                  {
                    var str = jsonElement.GetString()?.ToLower();
                    dep.IsInstalled = str == "installed" || str == "true";
                    if (dep.IsInstalled) matchCount++;
                  }
                }
                else if (statusObj is string statusStr)
                {
                  dep.IsInstalled = statusStr.ToLower() == "installed" || statusStr.ToLower() == "true";
                  if (dep.IsInstalled) matchCount++;
                }
              }
              else
              {
                ErrorLogger.LogDebug($"Key NOT found: '{key}'", "DEP-CHECK");
              }
            }
            ErrorLogger.LogDebug($"Total matched as installed: {matchCount}", "DEP-CHECK");
            
            // Save successful results to cache for use when backend is unavailable
            var cacheData = new Dictionary<string, bool>();
            foreach (var dep in dependencies)
            {
              var cacheKey = dep.Name.ToLower().Replace(" ", "_");
              cacheData[cacheKey] = dep.IsInstalled;
            }
            SaveDependencyCache(cacheData);
          }
          else
          {
            ErrorLogger.LogDebug("Response was NULL", "DEP-CHECK");
          }
        }
        catch (Exception ex)
        {
          // Log dependency check failure (background operation, don't interrupt user)
          ErrorLoggingService?.LogWarning($"Dependency check failed: {ex.Message}", "DependencyCheck");

          // Backend check failed - use cached values instead of resetting to false
          var cached = LoadCachedDependencyStatus();
          if (cached != null && cached.Count > 0)
          {
            ErrorLogger.LogDebug($"Using cached status ({cached.Count} entries)", "DEP-CHECK");
            foreach (var dep in dependencies)
            {
              var key = dep.Name.ToLower().Replace(" ", "_");
              if (cached.TryGetValue(key, out var isInstalled))
              {
                dep.IsInstalled = isInstalled;
              }
              // If not in cache, leave as default (false)
            }
          }
          else
          {
            ErrorLogger.LogDebug("No cache available, showing as not installed", "DEP-CHECK");
            // No cache available - keep as not installed (default)
          }
        }

        DependencyStatusList.Clear();
        foreach (var dep in dependencies)
        {
          DependencyStatusList.Add(dep);
        }

        TotalDependencies = DependencyStatusList.Count;
        InstalledDependencies = DependencyStatusList.Count(d => d.IsInstalled);
        MissingDependencies = DependencyStatusList.Count(d => !d.IsInstalled && !d.IsOptional);

        StatusMessage = $"Dependency status refreshed: {InstalledDependencies}/{TotalDependencies} installed";
      }
      catch (OperationCanceledException)
      {
        return; // User cancelled
      }
      catch (Exception ex)
      {
        ErrorMessage = $"Failed to refresh dependency status: {ex.Message}";
        await HandleErrorAsync(ex, "RefreshDependencyStatus");
      }
      finally
      {
        IsLoading = false;
      }
    }

    #region Dependency Cache Methods

    private static readonly string DependencyCachePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "VoiceStudio",
        "dependency_cache.json");

    /// <summary>
    /// Loads cached dependency status from local storage.
    /// Returns null if cache doesn't exist or is expired (24 hours).
    /// </summary>
    private static Dictionary<string, bool>? LoadCachedDependencyStatus()
    {
      try
      {
        if (!File.Exists(DependencyCachePath))
        {
          ErrorLogger.LogDebug("No cache file found", "DEP-CACHE");
          return null;
        }

        var json = File.ReadAllText(DependencyCachePath);
        var cache = JsonSerializer.Deserialize<DependencyCacheData>(json);

        if (cache == null)
        {
          ErrorLogger.LogDebug("Cache deserialization returned null", "DEP-CACHE");
          return null;
        }

        // Check if cache is expired (24 hours)
        var cacheAge = DateTime.UtcNow - cache.LastChecked;
        if (cacheAge.TotalHours > 24)
        {
          ErrorLogger.LogDebug($"Cache expired (age: {cacheAge.TotalHours:F1} hours)", "DEP-CACHE");
          return null;
        }

        ErrorLogger.LogDebug($"Loaded cache with {cache.Dependencies?.Count ?? 0} entries (age: {cacheAge.TotalMinutes:F0} minutes)", "DEP-CACHE");
        return cache.Dependencies;
      }
      catch (Exception ex)
      {
        ErrorLogger.LogWarning($"Error loading cache: {ex.Message}", "DEP-CACHE");
        return null;
      }
    }

    /// <summary>
    /// Saves dependency status to local cache file.
    /// </summary>
    private static void SaveDependencyCache(Dictionary<string, bool> dependencies)
    {
      try
      {
        // Ensure directory exists
        var directory = Path.GetDirectoryName(DependencyCachePath);
        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
        {
          Directory.CreateDirectory(directory);
        }

        var cache = new DependencyCacheData
        {
          LastChecked = DateTime.UtcNow,
          Dependencies = dependencies
        };

        var json = JsonSerializer.Serialize(cache, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(DependencyCachePath, json);

        ErrorLogger.LogDebug($"Saved cache with {dependencies.Count} entries", "DEP-CACHE");
      }
      catch (Exception ex)
      {
        ErrorLogger.LogWarning($"Error saving cache: {ex.Message}", "DEP-CACHE");
      }
    }

    /// <summary>
    /// Data structure for dependency cache file.
    /// </summary>
    private class DependencyCacheData
    {
      public DateTime LastChecked { get; set; }
      public Dictionary<string, bool>? Dependencies { get; set; }
    }

    #endregion
  }

  // Settings data models
  public class SettingsData
  {
    public GeneralSettings? General { get; set; }
    public EngineSettings? Engine { get; set; }
    public AudioSettings? Audio { get; set; }
    public TimelineSettings? Timeline { get; set; }
    public BackendSettings? Backend { get; set; }
    public PerformanceSettings? Performance { get; set; }
    public PluginSettings? Plugins { get; set; }
    public McpSettings? Mcp { get; set; }
    public DiagnosticsSettings? Diagnostics { get; set; }
  }

  public class GeneralSettings
  {
    public string Theme { get; set; } = "Dark";
    public string Language { get; set; } = "en-US";
    public bool AutoSave { get; set; } = true;
    public int AutoSaveInterval { get; set; } = 300;
  }

  public class EngineSettings
  {
    public string DefaultAudioEngine { get; set; } = "xtts";
    public string DefaultImageEngine { get; set; } = "sdxl";
    public string DefaultVideoEngine { get; set; } = "svd";
    public int QualityLevel { get; set; } = 5;
  }

  public class AudioSettings
  {
    public string OutputDevice { get; set; } = "Default";
    public string InputDevice { get; set; } = "Default";
    public int SampleRate { get; set; } = 44100;
    public int BufferSize { get; set; } = 1024;
  }

  public class TimelineSettings
  {
    public string TimeFormat { get; set; } = "Timecode";
    public bool SnapEnabled { get; set; } = true;
    public double SnapInterval { get; set; } = 0.1;
    public bool GridEnabled { get; set; } = true;
    public double GridInterval { get; set; } = 1.0;
  }

  public class BackendSettings
  {
    public string ApiUrl { get; set; } = "http://localhost:8000";
    public int Timeout { get; set; } = 30;
    public int RetryCount { get; set; } = 3;
  }

  public class PerformanceSettings
  {
    public bool CachingEnabled { get; set; } = true;
    public int CacheSize { get; set; } = 512;
    public int MaxThreads { get; set; } = 4;
    public int MemoryLimit { get; set; } = 4096;
  }

  public class PluginSettings
  {
    public System.Collections.Generic.List<string> EnabledPlugins { get; set; } = new();
  }

  public class McpSettings
  {
    public bool Enabled { get; set; }
    public string ServerUrl { get; set; } = "http://localhost:8080";
  }

  public class DiagnosticsSettings
  {
    public bool TelemetryEnabled { get; set; }
    public bool CrashReportingEnabled { get; set; } = true;
    public bool IncludeLogsInCrashReport { get; set; } = true;
  }

  public class DependencyStatusItem : ObservableObject
  {
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public bool IsInstalled { get; set; }
    public bool IsOptional { get; set; }
    public string Status => IsInstalled ? "Installed" : (IsOptional ? "Optional" : "Missing");
    public Windows.UI.Color StatusColor => IsInstalled ? Microsoft.UI.Colors.Green : (IsOptional ? Microsoft.UI.Colors.Orange : Microsoft.UI.Colors.Red);
    public Microsoft.UI.Xaml.Media.SolidColorBrush StatusBrush => new Microsoft.UI.Xaml.Media.SolidColorBrush(StatusColor);
  }
}