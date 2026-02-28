using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VoiceStudio.Core.Panels;
using VoiceStudio.Core.Services;
using VoiceStudio.App.Utilities;

namespace VoiceStudio.App.ViewModels
{
  /// <summary>
  /// ViewModel for the AdvancedSettingsView panel - Comprehensive settings.
  /// </summary>
  public partial class AdvancedSettingsViewModel : BaseViewModel, IPanelView
  {
    private readonly IBackendClient _backendClient;

    public string PanelId => "advanced-settings";
    public string DisplayName => ResourceHelper.GetString("Panel.AdvancedSettings.DisplayName", "Advanced Settings");
    public PanelRegion Region => PanelRegion.Center;

    // UI Settings
    [ObservableProperty]
    private string selectedTheme = "Dark";

    [ObservableProperty]
    private ObservableCollection<string> availableThemes = new() { "Light", "Dark", "System" };

    [ObservableProperty]
    private string accentColor = "#0078D4";

    [ObservableProperty]
    private string selectedFontSize = "Medium";

    [ObservableProperty]
    private ObservableCollection<string> availableFontSizes = new() { "Small", "Medium", "Large" };

    [ObservableProperty]
    private double uiScale = 1.0;

    [ObservableProperty]
    private bool animationEnabled = true;

    [ObservableProperty]
    private bool transparencyEnabled;

    [ObservableProperty]
    private bool compactMode;

    // Performance Settings
    [ObservableProperty]
    private bool cacheEnabled = true;

    [ObservableProperty]
    private int cacheSizeMb = 512;

    [ObservableProperty]
    private int maxThreads = 4;

    [ObservableProperty]
    private bool gpuEnabled = true;

    [ObservableProperty]
    private string? selectedGpuDevice;

    [ObservableProperty]
    private ObservableCollection<string> availableGpuDevices = new();

    [ObservableProperty]
    private double memoryLimitMb;

    [ObservableProperty]
    private bool backgroundProcessing = true;

    [ObservableProperty]
    private bool preloadEngines;

    // Audio Processing Settings
    [ObservableProperty]
    private int defaultSampleRate = 44100;

    [ObservableProperty]
    private int defaultBitDepth = 16;

    [ObservableProperty]
    private bool ditherEnabled = true;

    [ObservableProperty]
    private bool normalizationEnabled;

    [ObservableProperty]
    private bool autoFadeIn = true;

    [ObservableProperty]
    private bool autoFadeOut = true;

    [ObservableProperty]
    private int fadeDurationMs = 10;

    [ObservableProperty]
    private string selectedResamplingQuality = "High";

    [ObservableProperty]
    private ObservableCollection<string> availableResamplingQualities = new() { "Low", "Medium", "High" };

    // Engine Advanced Settings
    [ObservableProperty]
    private bool autoFallback = true;

    [ObservableProperty]
    private int timeoutSeconds = 300;

    [ObservableProperty]
    private int retryAttempts = 3;

    [ObservableProperty]
    private int batchSize = 1;

    [ObservableProperty]
    private bool enableQualityEnhancement = true;

    [ObservableProperty]
    private double qualityThreshold = 0.7;

    [ObservableProperty]
    private bool modelCacheEnabled = true;

    // System Integration Settings
    [ObservableProperty]
    private bool contextMenuEnabled;

    [ObservableProperty]
    private bool autoStart;

    [ObservableProperty]
    private bool minimizeToTray;

    [ObservableProperty]
    private bool checkForUpdates = true;

    [ObservableProperty]
    private string selectedUpdateChannel = "Stable";

    [ObservableProperty]
    private ObservableCollection<string> availableUpdateChannels = new() { "Stable", "Beta", "Dev" };

    [ObservableProperty]
    private string selectedCategory = "UI";

    [ObservableProperty]
    private ObservableCollection<string> availableCategories = new() { "UI", "Performance", "Audio Processing", "Engine", "System" };

    public AdvancedSettingsViewModel(IViewModelContext context, IBackendClient backendClient)
        : base(context)
    {
      _backendClient = backendClient ?? throw new ArgumentNullException(nameof(backendClient));

      LoadSettingsCommand = new EnhancedAsyncRelayCommand(async (ct) =>
      {
        using var profiler = PerformanceProfiler.StartCommand("LoadSettings");
        await LoadSettingsAsync(ct);
      }, () => !IsLoading);
      SaveSettingsCommand = new EnhancedAsyncRelayCommand(async (ct) =>
      {
        using var profiler = PerformanceProfiler.StartCommand("SaveSettings");
        await SaveSettingsAsync(ct);
      }, () => !IsLoading);
      ResetSettingsCommand = new EnhancedAsyncRelayCommand(async (ct) =>
      {
        using var profiler = PerformanceProfiler.StartCommand("ResetSettings");
        await ResetSettingsAsync(ct);
      }, () => !IsLoading);
      RefreshCommand = new EnhancedAsyncRelayCommand(async (ct) =>
      {
        using var profiler = PerformanceProfiler.StartCommand("Refresh");
        await RefreshAsync(ct);
      }, () => !IsLoading);

      // Load initial data
      _ = LoadSettingsAsync(CancellationToken.None);
      _ = LoadGpuDevicesAsync(CancellationToken.None);
    }

    private async Task LoadGpuDevicesAsync(CancellationToken cancellationToken)
    {
      try
      {
        var devices = await _backendClient.SendRequestAsync<object, List<GpuDeviceInfo>>(
            "/api/gpu-status/devices",
            null,
            System.Net.Http.HttpMethod.Get,
            cancellationToken
        );

        if (devices != null)
        {
          AvailableGpuDevices.Clear();
          foreach (var device in devices)
          {
            AvailableGpuDevices.Add(device.DeviceId);
          }
        }
      }
      catch (Exception ex)
      {
        // GPU device loading is optional - don't show error if it fails
        // Just leave AvailableGpuDevices empty
        System.Diagnostics.Debug.WriteLine($"Failed to load GPU devices: {ex.Message}");
      }
    }

    public IAsyncRelayCommand LoadSettingsCommand { get; }
    public IAsyncRelayCommand SaveSettingsCommand { get; }
    public IAsyncRelayCommand ResetSettingsCommand { get; }
    public IAsyncRelayCommand RefreshCommand { get; }

    private async Task LoadSettingsAsync(CancellationToken cancellationToken)
    {
      try
      {
        IsLoading = true;
        ErrorMessage = null;

        var settings = await _backendClient.SendRequestAsync<object, AdvancedSettingsData>(
            "/api/advanced-settings",
            null,
            System.Net.Http.HttpMethod.Get,
            cancellationToken
        );

        if (settings != null)
        {
          // UI Settings
          SelectedTheme = settings.Ui?.Theme ?? "Dark";
          AccentColor = settings.Ui?.AccentColor ?? "#0078D4";
          SelectedFontSize = settings.Ui?.FontSize ?? "Medium";
          UiScale = settings.Ui?.UiScale ?? 1.0;
          AnimationEnabled = settings.Ui?.AnimationEnabled ?? true;
          TransparencyEnabled = settings.Ui?.TransparencyEnabled ?? false;
          CompactMode = settings.Ui?.CompactMode ?? false;

          // Performance Settings
          CacheEnabled = settings.Performance?.CacheEnabled ?? true;
          CacheSizeMb = settings.Performance?.CacheSizeMb ?? 512;
          MaxThreads = settings.Performance?.MaxThreads ?? 4;
          GpuEnabled = settings.Performance?.GpuEnabled ?? true;
          SelectedGpuDevice = settings.Performance?.GpuDevice;
          MemoryLimitMb = settings.Performance?.MemoryLimitMb ?? 0;
          BackgroundProcessing = settings.Performance?.BackgroundProcessing ?? true;
          PreloadEngines = settings.Performance?.PreloadEngines ?? false;

          // Audio Processing Settings
          DefaultSampleRate = settings.AudioProcessing?.DefaultSampleRate ?? 44100;
          DefaultBitDepth = settings.AudioProcessing?.DefaultBitDepth ?? 16;
          DitherEnabled = settings.AudioProcessing?.DitherEnabled ?? true;
          NormalizationEnabled = settings.AudioProcessing?.NormalizationEnabled ?? false;
          AutoFadeIn = settings.AudioProcessing?.AutoFadeIn ?? true;
          AutoFadeOut = settings.AudioProcessing?.AutoFadeOut ?? true;
          FadeDurationMs = settings.AudioProcessing?.FadeDurationMs ?? 10;
          SelectedResamplingQuality = settings.AudioProcessing?.ResamplingQuality ?? "High";

          // Engine Advanced Settings
          AutoFallback = settings.Engine?.AutoFallback ?? true;
          TimeoutSeconds = settings.Engine?.TimeoutSeconds ?? 300;
          RetryAttempts = settings.Engine?.RetryAttempts ?? 3;
          BatchSize = settings.Engine?.BatchSize ?? 1;
          EnableQualityEnhancement = settings.Engine?.EnableQualityEnhancement ?? true;
          QualityThreshold = settings.Engine?.QualityThreshold ?? 0.7;
          ModelCacheEnabled = settings.Engine?.ModelCacheEnabled ?? true;

          // System Integration Settings
          ContextMenuEnabled = settings.System?.ContextMenuEnabled ?? false;
          AutoStart = settings.System?.AutoStart ?? false;
          MinimizeToTray = settings.System?.MinimizeToTray ?? false;
          CheckForUpdates = settings.System?.CheckForUpdates ?? true;
          SelectedUpdateChannel = settings.System?.UpdateChannel ?? "Stable";

          StatusMessage = ResourceHelper.GetString("AdvancedSettings.SettingsLoaded", "Settings loaded");
        }
      }
      catch (OperationCanceledException)
      {
        return; // User cancelled
      }
      catch (Exception ex)
      {
        await HandleErrorAsync(ex, "LoadSettings");
      }
      finally
      {
        IsLoading = false;
      }
    }

    private async Task SaveSettingsAsync(CancellationToken cancellationToken)
    {
      try
      {
        IsLoading = true;
        ErrorMessage = null;

        var settings = new
        {
          ui = new
          {
            theme = SelectedTheme,
            accent_color = AccentColor,
            font_size = SelectedFontSize,
            ui_scale = UiScale,
            animation_enabled = AnimationEnabled,
            transparency_enabled = TransparencyEnabled,
            compact_mode = CompactMode
          },
          performance = new
          {
            cache_enabled = CacheEnabled,
            cache_size_mb = CacheSizeMb,
            max_threads = MaxThreads,
            gpu_enabled = GpuEnabled,
            gpu_device = SelectedGpuDevice,
            memory_limit_mb = MemoryLimitMb,
            background_processing = BackgroundProcessing,
            preload_engines = PreloadEngines
          },
          audio_processing = new
          {
            default_sample_rate = DefaultSampleRate,
            default_bit_depth = DefaultBitDepth,
            dither_enabled = DitherEnabled,
            normalization_enabled = NormalizationEnabled,
            auto_fade_in = AutoFadeIn,
            auto_fade_out = AutoFadeOut,
            fade_duration_ms = FadeDurationMs,
            resampling_quality = SelectedResamplingQuality
          },
          engine = new
          {
            auto_fallback = AutoFallback,
            timeout_seconds = TimeoutSeconds,
            retry_attempts = RetryAttempts,
            batch_size = BatchSize,
            enable_quality_enhancement = EnableQualityEnhancement,
            quality_threshold = QualityThreshold,
            model_cache_enabled = ModelCacheEnabled
          },
          system = new
          {
            context_menu_enabled = ContextMenuEnabled,
            auto_start = AutoStart,
            minimize_to_tray = MinimizeToTray,
            check_for_updates = CheckForUpdates,
            update_channel = SelectedUpdateChannel
          }
        };

        await _backendClient.SendRequestAsync<object, object>(
            "/api/advanced-settings",
            settings,
            System.Net.Http.HttpMethod.Put,
            cancellationToken
        );

        StatusMessage = ResourceHelper.GetString("AdvancedSettings.SettingsSaved", "Settings saved");
      }
      catch (OperationCanceledException)
      {
        return; // User cancelled
      }
      catch (Exception ex)
      {
        await HandleErrorAsync(ex, "SaveSettings");
      }
      finally
      {
        IsLoading = false;
      }
    }

    private async Task ResetSettingsAsync(CancellationToken cancellationToken)
    {
      try
      {
        IsLoading = true;
        ErrorMessage = null;

        await _backendClient.SendRequestAsync<object, object>(
            "/api/advanced-settings/reset",
            null,
            System.Net.Http.HttpMethod.Post,
            cancellationToken
        );

        await LoadSettingsAsync(cancellationToken);
        StatusMessage = ResourceHelper.GetString("AdvancedSettings.SettingsReset", "Settings reset to defaults");
      }
      catch (OperationCanceledException)
      {
        return; // User cancelled
      }
      catch (Exception ex)
      {
        await HandleErrorAsync(ex, "ResetSettings");
      }
      finally
      {
        IsLoading = false;
      }
    }

    private async Task RefreshAsync(CancellationToken cancellationToken)
    {
      try
      {
        await LoadSettingsAsync(cancellationToken);
        await LoadGpuDevicesAsync(cancellationToken);
        StatusMessage = ResourceHelper.GetString("AdvancedSettings.Refreshed", "Refreshed");
      }
      catch (OperationCanceledException)
      {
        return; // User cancelled
      }
      catch (Exception ex)
      {
        await HandleErrorAsync(ex, "Refresh");
      }
    }

    // Response models
    private class AdvancedSettingsData
    {
      public UISettings? Ui { get; set; }
      public PerformanceSettings? Performance { get; set; }
      public AudioProcessingSettings? AudioProcessing { get; set; }
      public EngineAdvancedSettings? Engine { get; set; }
      public SystemIntegrationSettings? System { get; set; }
    }

    private class UISettings
    {
      public string Theme { get; set; } = string.Empty;
      public string AccentColor { get; set; } = string.Empty;
      public string FontSize { get; set; } = string.Empty;
      public double UiScale { get; set; }
      public bool AnimationEnabled { get; set; }
      public bool TransparencyEnabled { get; set; }
      public bool CompactMode { get; set; }
    }

    private class PerformanceSettings
    {
      public bool CacheEnabled { get; set; }
      public int CacheSizeMb { get; set; }
      public int MaxThreads { get; set; }
      public bool GpuEnabled { get; set; }
      public string? GpuDevice { get; set; }
      public double MemoryLimitMb { get; set; }
      public bool BackgroundProcessing { get; set; }
      public bool PreloadEngines { get; set; }
    }

    private class AudioProcessingSettings
    {
      public int DefaultSampleRate { get; set; }
      public int DefaultBitDepth { get; set; }
      public bool DitherEnabled { get; set; }
      public bool NormalizationEnabled { get; set; }
      public bool AutoFadeIn { get; set; }
      public bool AutoFadeOut { get; set; }
      public int FadeDurationMs { get; set; }
      public string ResamplingQuality { get; set; } = string.Empty;
    }

    private class EngineAdvancedSettings
    {
      public bool AutoFallback { get; set; }
      public int TimeoutSeconds { get; set; }
      public int RetryAttempts { get; set; }
      public int BatchSize { get; set; }
      public bool EnableQualityEnhancement { get; set; }
      public double QualityThreshold { get; set; }
      public bool ModelCacheEnabled { get; set; }
    }

    private class SystemIntegrationSettings
    {
      public bool ContextMenuEnabled { get; set; }
      public bool AutoStart { get; set; }
      public bool MinimizeToTray { get; set; }
      public bool CheckForUpdates { get; set; }
      public string UpdateChannel { get; set; } = string.Empty;
    }

    private class GpuDeviceInfo
    {
      public string DeviceId { get; set; } = string.Empty;
      public string Name { get; set; } = string.Empty;
      public string Vendor { get; set; } = string.Empty;
    }
  }
}