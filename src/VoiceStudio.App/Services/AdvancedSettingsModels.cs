using System;

namespace VoiceStudio.Core.Models
{
  /// <summary>
  /// Advanced settings API response.
  /// </summary>
  public class AdvancedSettingsData
  {
    public AdvancedUISettings? Ui { get; set; }
    public AdvancedPerformanceSettings? Performance { get; set; }
    public AdvancedAudioProcessingSettings? AudioProcessing { get; set; }
    public AdvancedEngineSettings? Engine { get; set; }
    public AdvancedSystemSettings? System { get; set; }
  }

  /// <summary>
  /// UI settings section for advanced settings.
  /// </summary>
  public class AdvancedUISettings
  {
    public string Theme { get; set; } = string.Empty;
    public string AccentColor { get; set; } = string.Empty;
    public string FontSize { get; set; } = string.Empty;
    public double UiScale { get; set; }
    public bool AnimationEnabled { get; set; }
    public bool TransparencyEnabled { get; set; }
    public bool CompactMode { get; set; }
  }

  /// <summary>
  /// Performance settings section for advanced settings.
  /// </summary>
  public class AdvancedPerformanceSettings
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

  /// <summary>
  /// Audio processing settings section for advanced settings.
  /// </summary>
  public class AdvancedAudioProcessingSettings
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

  /// <summary>
  /// Engine advanced settings section.
  /// </summary>
  public class AdvancedEngineSettings
  {
    public bool AutoFallback { get; set; }
    public int TimeoutSeconds { get; set; }
    public int RetryAttempts { get; set; }
    public int BatchSize { get; set; }
    public bool EnableQualityEnhancement { get; set; }
    public double QualityThreshold { get; set; }
    public bool ModelCacheEnabled { get; set; }
  }

  /// <summary>
  /// System integration settings section for advanced settings.
  /// </summary>
  public class AdvancedSystemSettings
  {
    public bool ContextMenuEnabled { get; set; }
    public bool AutoStart { get; set; }
    public bool MinimizeToTray { get; set; }
    public bool CheckForUpdates { get; set; }
    public string UpdateChannel { get; set; } = string.Empty;
  }

  /// <summary>
  /// GPU device info from /api/gpu-status/devices.
  /// </summary>
  public class GpuDeviceInfo
  {
    public string DeviceId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Vendor { get; set; } = string.Empty;
  }
}
