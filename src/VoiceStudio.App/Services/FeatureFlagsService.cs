using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using VoiceStudio.Core.Services;
using VoiceStudio.App.Helpers;
using VoiceStudio.App.Logging;

namespace VoiceStudio.App.Services
{
  /// <summary>
  /// Category for grouping related feature flags.
  /// </summary>
  public enum FeatureFlagCategory
  {
    Core,
    UI,
    Backend,
    Experimental,
    ABTest
  }

  /// <summary>
  /// Extended feature flag metadata for production use.
  /// </summary>
  public class FeatureFlagDefinition
  {
    public string Name { get; set; } = string.Empty;
    public bool DefaultValue { get; set; }
    public string Description { get; set; } = string.Empty;
    public FeatureFlagCategory Category { get; set; }
    public int RolloutPercentage { get; set; } = 100;
    public bool IsRemoteConfigurable { get; set; }
    public string? ABTestId { get; set; }
  }

  /// <summary>
  /// Implementation of feature flags service with production-ready features:
  /// - Local persistence via Windows.Storage
  /// - Remote configuration sync capability
  /// - Percentage-based rollouts
  /// - A/B testing support with user bucketing
  /// - Environment overrides
  /// - Flag categories for organization
  /// </summary>
  public class FeatureFlagsService : IFeatureFlagsService
  {
    private const string SettingsKey = "FeatureFlags";
    private const string UserIdKey = "FeatureFlagsUserId";
    private readonly Dictionary<string, bool> _flags;
    private readonly Dictionary<string, FeatureFlagDefinition> _definitions;
    private readonly Dictionary<string, bool> _overrides;
    private readonly string _userId;
    private bool _remoteConfigEnabled;

    public event EventHandler<string>? FlagChanged;

    /// <summary>
    /// Event raised when remote configuration is synced.
    /// </summary>
    public event EventHandler? RemoteConfigSynced;

    public FeatureFlagsService()
    {
      _flags = new Dictionary<string, bool>();
      _definitions = new Dictionary<string, FeatureFlagDefinition>();
      _overrides = new Dictionary<string, bool>();
      _userId = GetOrCreateUserId();

      // Initialize default flags with definitions
      InitializeDefaultFlags();

      // Load environment overrides
      LoadEnvironmentOverrides();

      // Load persisted flags
      LoadFlags();
    }

    /// <summary>
    /// Gets or creates a stable user ID for consistent A/B bucketing.
    /// </summary>
    private string GetOrCreateUserId()
    {
      try
      {
        // Use UnpackagedSettingsHelper for file-based settings (works for both packaged and unpackaged apps)
        var userId = UnpackagedSettingsHelper.GetValue<string>(UserIdKey, null);
        if (!string.IsNullOrEmpty(userId))
        {
          return userId;
        }

        // Generate new stable user ID
        var newUserId = Guid.NewGuid().ToString("N");
        UnpackagedSettingsHelper.SetValue(UserIdKey, newUserId);
        return newUserId;
      }
      catch (Exception ex)
      {
        ErrorLogger.LogWarning($"Failed to get/create user ID: {ex.Message}", "FeatureFlagsService");
        return Guid.NewGuid().ToString("N");
      }
    }

    /// <summary>
    /// Loads environment variable overrides (e.g., VOICESTUDIO_FF_FlagName=true).
    /// </summary>
    private void LoadEnvironmentOverrides()
    {
      try
      {
        foreach (var key in Environment.GetEnvironmentVariables().Keys)
        {
          var keyStr = key?.ToString() ?? string.Empty;
          if (keyStr.StartsWith("VOICESTUDIO_FF_", StringComparison.OrdinalIgnoreCase))
          {
            var flagName = keyStr.Substring("VOICESTUDIO_FF_".Length);
            var value = Environment.GetEnvironmentVariable(keyStr);
            if (bool.TryParse(value, out var enabled))
            {
              _overrides[flagName] = enabled;
              ErrorLogger.LogInfo($"Environment override: {flagName}={enabled}", "FeatureFlagsService");
            }
          }
        }
      }
      catch (Exception ex)
      {
        ErrorLogger.LogWarning($"Failed to load environment overrides: {ex.Message}", "FeatureFlagsService");
      }
    }

    private void InitializeDefaultFlags()
    {
      // Core feature flags
      AddFlag("HeavyPanelsEnabled", true, "Enable heavy panels (Quality Dashboard, Spatial Stage, etc.)", FeatureFlagCategory.Core);
      AddFlag("AnalyticsEnabled", true, "Enable analytics event tracking", FeatureFlagCategory.Core, isRemoteConfigurable: true);
      AddFlag("PerformanceProfilingEnabled", false, "Enable performance profiling and budget monitoring", FeatureFlagCategory.Core);
      AddFlag("StressTestMode", false, "Enable stress test mode for performance testing", FeatureFlagCategory.Core);
      AddFlag("TelemetryEnabled", false, "Enable anonymous telemetry collection (opt-in)", FeatureFlagCategory.Core, isRemoteConfigurable: true);

      // UI feature flags
      AddFlag("RealTimeQualityMetrics", true, "Enable real-time quality metrics display", FeatureFlagCategory.UI);
      AddFlag("AdvancedEffectsEnabled", true, "Enable advanced audio effects processing", FeatureFlagCategory.UI);
      AddFlag("MultiEngineEnsemble", true, "Enable multi-engine ensemble synthesis", FeatureFlagCategory.UI);
      AddFlag("DarkModeDefault", false, "Default to dark mode on first launch", FeatureFlagCategory.UI, isRemoteConfigurable: true);
      AddFlag("CompactUIMode", false, "Enable compact UI mode for smaller screens", FeatureFlagCategory.UI);
      AddFlag("ShowExperimentalPanels", false, "Show experimental panels in Modules menu and Command Palette", FeatureFlagCategory.UI);

      // Backend feature flags
      AddFlag("BackendCachingEnabled", true, "Enable backend response caching", FeatureFlagCategory.Backend);
      AddFlag("WebSocketEnabled", true, "Enable WebSocket real-time communication", FeatureFlagCategory.Backend);
      AddFlag("CircuitBreakerEnabled", true, "Enable circuit breaker for engine failures", FeatureFlagCategory.Backend);

      // Experimental features (gradual rollout examples)
      AddFlag("ExperimentalVoiceMorphing", false, "Enable experimental voice morphing features", FeatureFlagCategory.Experimental, rolloutPercentage: 10);
      AddFlag("ExperimentalStyleTransfer", false, "Enable experimental style transfer features", FeatureFlagCategory.Experimental, rolloutPercentage: 5);
      AddFlag("ExperimentalRealtimeSynthesis", false, "Enable experimental real-time synthesis", FeatureFlagCategory.Experimental, rolloutPercentage: 0);

      // Integration audit gates (F-01, F-09): Disabled until pipeline/RVC WebSocket wiring is complete
      AddFlag("pipeline_streaming", false, "Enable pipeline streaming (requires /api/pipeline/stream wiring)", FeatureFlagCategory.Experimental);
      AddFlag("rvc_realtime", false, "Enable RVC realtime conversion (requires direct WebSocket wiring)", FeatureFlagCategory.Experimental);

      // A/B test flags
      AddFlag("ABTest_NewOnboardingFlow", false, "A/B test: New onboarding flow", FeatureFlagCategory.ABTest, rolloutPercentage: 50, abTestId: "onboarding_v2");
      AddFlag("ABTest_SimplifiedWizard", false, "A/B test: Simplified voice cloning wizard", FeatureFlagCategory.ABTest, rolloutPercentage: 50, abTestId: "wizard_simple");
    }

    private void AddFlag(
      string flag,
      bool defaultValue,
      string description,
      FeatureFlagCategory category,
      int rolloutPercentage = 100,
      bool isRemoteConfigurable = false,
      string? abTestId = null)
    {
      var definition = new FeatureFlagDefinition
      {
        Name = flag,
        DefaultValue = defaultValue,
        Description = description,
        Category = category,
        RolloutPercentage = rolloutPercentage,
        IsRemoteConfigurable = isRemoteConfigurable,
        ABTestId = abTestId
      };

      _definitions[flag] = definition;
      _flags[flag] = defaultValue;
    }

    /// <summary>
    /// Checks if a feature flag is enabled, considering overrides, rollout percentage, and user bucketing.
    /// </summary>
    public bool IsEnabled(string flag)
    {
      // Priority 1: Environment overrides (highest priority)
      if (_overrides.TryGetValue(flag, out var overrideValue))
      {
        return overrideValue;
      }

      // Priority 2: Check if flag exists
      if (!_flags.TryGetValue(flag, out var value))
      {
        return false;
      }

      // Priority 3: If flag is disabled, return false
      if (!value)
      {
        return false;
      }

      // Priority 4: Check rollout percentage
      if (_definitions.TryGetValue(flag, out var definition) && definition.RolloutPercentage < 100)
      {
        return IsUserInRollout(flag, definition.RolloutPercentage);
      }

      return value;
    }

    /// <summary>
    /// Determines if the current user is in the rollout percentage using stable bucketing.
    /// </summary>
    private bool IsUserInRollout(string flag, int percentage)
    {
      if (percentage <= 0) return false;
      if (percentage >= 100) return true;

      // Use stable hash for consistent bucketing
      var bucket = GetUserBucket(flag);
      return bucket < percentage;
    }

    /// <summary>
    /// Gets user bucket (0-99) for a flag using stable hash.
    /// </summary>
    private int GetUserBucket(string flag)
    {
      var input = $"{_userId}:{flag}";
      using var sha256 = SHA256.Create();
      var hash = sha256.ComputeHash(Encoding.UTF8.GetBytes(input));
      // Use first 4 bytes to get a number, then mod 100
      var value = BitConverter.ToUInt32(hash, 0);
      return (int)(value % 100);
    }

    /// <summary>
    /// Gets the A/B test variant for the current user.
    /// </summary>
    public string GetABTestVariant(string abTestId)
    {
      var bucket = GetUserBucket(abTestId);
      // For simple A/B tests: 0-49 = control, 50-99 = treatment
      return bucket < 50 ? "control" : "treatment";
    }

    /// <summary>
    /// Checks if user is in treatment group for an A/B test.
    /// </summary>
    public bool IsInTreatmentGroup(string abTestId)
    {
      return GetABTestVariant(abTestId) == "treatment";
    }

    public void SetFlag(string flag, bool enabled)
    {
      if (!_flags.ContainsKey(flag))
      {
        throw new ArgumentException($"Unknown feature flag: {flag}", nameof(flag));
      }

      if (_flags[flag] != enabled)
      {
        _flags[flag] = enabled;
        SaveFlags();
        FlagChanged?.Invoke(this, flag);
      }
    }

    /// <summary>
    /// Sets an override that takes priority over stored flag values.
    /// </summary>
    public void SetOverride(string flag, bool enabled)
    {
      _overrides[flag] = enabled;
      FlagChanged?.Invoke(this, flag);
    }

    /// <summary>
    /// Clears an override for a flag.
    /// </summary>
    public void ClearOverride(string flag)
    {
      if (_overrides.Remove(flag))
      {
        FlagChanged?.Invoke(this, flag);
      }
    }

    /// <summary>
    /// Clears all overrides.
    /// </summary>
    public void ClearAllOverrides()
    {
      var flags = _overrides.Keys.ToList();
      _overrides.Clear();
      foreach (var flag in flags)
      {
        FlagChanged?.Invoke(this, flag);
      }
    }

    public IReadOnlyDictionary<string, bool> GetAllFlags()
    {
      return _flags;
    }

    /// <summary>
    /// Gets all flag definitions with metadata.
    /// </summary>
    public IReadOnlyDictionary<string, FeatureFlagDefinition> GetAllDefinitions()
    {
      return _definitions;
    }

    /// <summary>
    /// Gets flags by category.
    /// </summary>
    public IEnumerable<FeatureFlagDefinition> GetFlagsByCategory(FeatureFlagCategory category)
    {
      return _definitions.Values.Where(d => d.Category == category);
    }

    public string? GetDescription(string flag)
    {
      return _definitions.TryGetValue(flag, out var definition) ? definition.Description : null;
    }

    /// <summary>
    /// Syncs flags from remote configuration.
    /// </summary>
    public async Task SyncRemoteConfigAsync(Dictionary<string, bool> remoteFlags)
    {
      if (!_remoteConfigEnabled)
      {
        ErrorLogger.LogInfo("Remote config sync skipped (disabled)", "FeatureFlagsService");
        return;
      }

      var changed = new List<string>();

      foreach (var kvp in remoteFlags)
      {
        if (_definitions.TryGetValue(kvp.Key, out var definition) && definition.IsRemoteConfigurable)
        {
          if (_flags[kvp.Key] != kvp.Value)
          {
            _flags[kvp.Key] = kvp.Value;
            changed.Add(kvp.Key);
          }
        }
      }

      if (changed.Count > 0)
      {
        SaveFlags();
        foreach (var flag in changed)
        {
          FlagChanged?.Invoke(this, flag);
        }
        RemoteConfigSynced?.Invoke(this, EventArgs.Empty);
      }

      await Task.CompletedTask;
    }

    /// <summary>
    /// Enables or disables remote configuration sync.
    /// </summary>
    public void SetRemoteConfigEnabled(bool enabled)
    {
      _remoteConfigEnabled = enabled;
    }

    /// <summary>
    /// Exports current flag state for analytics/debugging.
    /// </summary>
    public Dictionary<string, object> ExportState()
    {
      var state = new Dictionary<string, object>
      {
        ["userId"] = _userId,
        ["remoteConfigEnabled"] = _remoteConfigEnabled,
        ["flags"] = _flags.ToDictionary(kvp => kvp.Key, kvp => (object)kvp.Value),
        ["overrides"] = _overrides.ToDictionary(kvp => kvp.Key, kvp => (object)kvp.Value),
        ["effectiveFlags"] = _flags.Keys.ToDictionary(k => k, k => (object)IsEnabled(k))
      };
      return state;
    }

    private void LoadFlags()
    {
      try
      {
        // Use UnpackagedSettingsHelper for file-based settings (works for both packaged and unpackaged apps)
        var savedFlags = UnpackagedSettingsHelper.GetValue<Dictionary<string, bool>>(SettingsKey, null);
        if (savedFlags != null)
        {
          foreach (var flag in _flags.Keys.ToList())
          {
            if (savedFlags.TryGetValue(flag, out var enabled))
            {
              _flags[flag] = enabled;
            }
          }
        }
      }
      catch (Exception ex)
      {
        ErrorLogger.LogWarning($"Best effort operation failed: {ex.Message}", "FeatureFlagsService.LoadFlags");
      }
    }

    private void SaveFlags()
    {
      try
      {
        // Use UnpackagedSettingsHelper for file-based settings (works for both packaged and unpackaged apps)
        var flagsToSave = new Dictionary<string, bool>(_flags);
        UnpackagedSettingsHelper.SetValue(SettingsKey, flagsToSave);
      }
      catch (Exception ex)
      {
        ErrorLogger.LogWarning($"Best effort operation failed: {ex.Message}", "FeatureFlagsService.SaveFlags");
      }
    }
  }
}