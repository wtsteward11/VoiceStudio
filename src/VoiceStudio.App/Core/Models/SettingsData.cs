using System.Collections.Generic;

namespace VoiceStudio.Core.Models
{
  /// <summary>
  /// General application settings.
  /// </summary>
  public class GeneralSettings
  {
    public string Theme { get; set; } = "Dark";
    public string Language { get; set; } = "en-US";
    public bool AutoSave { get; set; } = true;
    public int AutoSaveInterval { get; set; } = 300; // seconds
  }

  /// <summary>
  /// Engine default settings.
  /// </summary>
  public class EngineSettings
  {
    public string DefaultAudioEngine { get; set; } = "xtts";
    public string DefaultImageEngine { get; set; } = "sdxl";
    public string DefaultVideoEngine { get; set; } = "svd";
    public int QualityLevel { get; set; } = 5; // 1-10
  }

  /// <summary>
  /// Audio device and quality settings.
  /// </summary>
  public class AudioSettings
  {
    public string OutputDevice { get; set; } = "Default";
    public string InputDevice { get; set; } = "Default";
    public int SampleRate { get; set; } = 44100;
    public int BufferSize { get; set; } = 1024;
  }

  /// <summary>
  /// Timeline display and behavior settings.
  /// </summary>
  public class TimelineSettings
  {
    public string TimeFormat { get; set; } = "Timecode";
    public bool SnapEnabled { get; set; } = true;
    public double SnapInterval { get; set; } = 0.1; // seconds
    public bool GridEnabled { get; set; } = true;
    public double GridInterval { get; set; } = 1.0; // seconds

    // Scrubbing preview settings (IDEA 13)
    public bool PreviewEnabled { get; set; } = true; // Enable audio preview during scrubbing
    public double PreviewDuration { get; set; } = 0.15; // Preview duration in seconds (100-200ms)
    public double PreviewVolume { get; set; } = 0.6; // Preview volume (0.0-1.0, typically 50-70% of normal)
  }

  /// <summary>
  /// Backend API connection settings.
  /// </summary>
  public class BackendSettings
  {
    public string ApiUrl { get; set; } = "http://localhost:8001";
    public int Timeout { get; set; } = 30; // seconds
    public int RetryCount { get; set; } = 3;
  }

  /// <summary>
  /// Performance and caching settings.
  /// </summary>
  public class PerformanceSettings
  {
    public bool CachingEnabled { get; set; } = true;
    public int CacheSize { get; set; } = 512; // MB
    public int MaxThreads { get; set; } = 4;
    public int MemoryLimit { get; set; } = 4096; // MB
  }

  /// <summary>
  /// Plugin management settings.
  /// </summary>
  public class PluginSettings
  {
    public List<string> EnabledPlugins { get; set; } = new();
  }

  /// <summary>
  /// MCP server configuration settings.
  /// </summary>
  public class McpSettings
  {
    public bool Enabled { get; set; }
    public string ServerUrl { get; set; } = "http://localhost:8080";
  }

  /// <summary>
  /// Diagnostics and telemetry settings.
  /// </summary>
  public class DiagnosticsSettings
  {
    public bool EnableTelemetry { get; set; }
    public bool EnableErrorReporting { get; set; }
    public bool EnablePerformanceMetrics { get; set; }
    public string LogLevel { get; set; } = "Warning";

    // Alias properties for SettingsViewModel compatibility
    public bool TelemetryEnabled
    {
      get => EnableTelemetry;
      set => EnableTelemetry = value;
    }
    public bool CrashReportingEnabled
    {
      get => EnableErrorReporting;
      set => EnableErrorReporting = value;
    }
    public bool IncludeLogsInCrashReport { get; set; }
  }

  /// <summary>
  /// Quality management settings for voice cloning.
  /// </summary>
  public class QualitySettings
  {
    public string DefaultPreset { get; set; } = "standard"; // fast, standard, high, ultra, professional
    public bool AutoEnhance { get; set; } = true; // Automatically enhance quality
    public bool AutoOptimize { get; set; }  // Automatically optimize parameters
    public double MinMosScore { get; set; } = 3.5; // Minimum acceptable MOS score
    public double MinSimilarity { get; set; } = 0.75; // Minimum acceptable similarity
    public double MinNaturalness { get; set; } = 0.70; // Minimum acceptable naturalness
    public double MinSnrDb { get; set; } = 25.0; // Minimum acceptable SNR
    public bool PreferSpeed { get; set; }  // Prefer speed over quality
    public string QualityTier { get; set; } = "standard"; // Quality tier preference
    public bool ShowQualityMetrics { get; set; } = true; // Show quality metrics in UI
    public bool AutoCompare { get; set; }  // Automatically compare synthesis results
  }

  /// <summary>
  /// Complete application settings data.
  /// </summary>
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
    public QualitySettings? Quality { get; set; }
    public DiagnosticsSettings? Diagnostics { get; set; }
    public WorkspaceLayout? WorkspaceLayout { get; set; }
  }
}