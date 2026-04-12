using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using VoiceStudio.App.Logging;

namespace VoiceStudio.App.Services;

/// <summary>
/// GAP-067 slice 7: merges startup profiler checkpoints and wall-clock markers into
/// <c>%LOCALAPPDATA%\VoiceStudio\Logs\cold_start_timing.json</c> for CI/operator baselines.
/// </summary>
public static class ColdStartTimingCollector
{
  public const int SchemaVersion = 1;
  public const string ArtifactFileName = "cold_start_timing.json";

  private static readonly object Gate = new();
  private static DateTime _appStartUtc;
  private static readonly Dictionary<string, double> MarkersMs = new(StringComparer.Ordinal);
  private static bool _shellInteractiveRecorded;
  private static bool _deferredInitCompleted;
  private static double? _deferredInitStartMs;
  private static double? _deferredInitEndMs;
  private static double? _panelsInitStartMs;
  private static double? _panelsInitEndMs;

  /// <summary>Monotonic wall time from app start (UTC-based; ms).</summary>
  public static void SetAppStartUtc(DateTime appStartUtc)
  {
    lock (Gate)
    {
      _appStartUtc = appStartUtc;
    }
  }

  public static void CaptureApplicationStartupCheckpoints(Utilities.PerformanceProfiler? profiler, DateTime appStartUtc)
  {
    if (profiler == null)
    {
      return;
    }

    lock (Gate)
    {
      _appStartUtc = appStartUtc;
      foreach (var kv in profiler.Checkpoints)
      {
        MarkersMs[kv.Key] = kv.Value.TotalMilliseconds;
      }
    }
  }

  public static void CaptureMainWindowConstructionCheckpoints(Utilities.PerformanceProfiler profiler)
  {
    lock (Gate)
    {
      foreach (var kv in profiler.Checkpoints)
      {
        MarkersMs["mainwindow_ctor:" + kv.Key] = kv.Value.TotalMilliseconds;
      }

      if (profiler.Checkpoints.TryGetValue("MainWindow Construction Complete", out var end))
      {
        MarkersMs["mainwindow_ctor_end_ms"] = end.TotalMilliseconds;
      }
    }
  }

  public static void RecordWallClockMarker(string logicalName)
  {
    double ms;
    lock (Gate)
    {
      ms = (DateTime.UtcNow - _appStartUtc).TotalMilliseconds;
      MarkersMs[logicalName] = ms;
    }
  }

  public static void RecordDeferredInitStart()
  {
    double ms;
    lock (Gate)
    {
      ms = (DateTime.UtcNow - _appStartUtc).TotalMilliseconds;
      _deferredInitStartMs = ms;
      MarkersMs["deferred_init_start_ms"] = ms;
    }
  }

  public static void RecordDeferredInitEnd()
  {
    double ms;
    lock (Gate)
    {
      ms = (DateTime.UtcNow - _appStartUtc).TotalMilliseconds;
      _deferredInitEndMs = ms;
      MarkersMs["deferred_init_end_ms"] = ms;
      _deferredInitCompleted = true;
    }

    TryWriteArtifact();
  }

  public static void RecordPanelsInitStart()
  {
    double ms;
    lock (Gate)
    {
      ms = (DateTime.UtcNow - _appStartUtc).TotalMilliseconds;
      _panelsInitStartMs = ms;
      MarkersMs["panels_init_start_ms"] = ms;
    }
  }

  public static void RecordPanelsInitEnd()
  {
    double ms;
    lock (Gate)
    {
      ms = (DateTime.UtcNow - _appStartUtc).TotalMilliseconds;
      _panelsInitEndMs = ms;
      MarkersMs["panels_init_end_ms"] = ms;
    }
  }

  /// <summary>
  /// T2: overlay hidden — shell interactive for timing purposes.
  /// </summary>
  public static void RecordShellInteractive()
  {
    lock (Gate)
    {
      var ms = (DateTime.UtcNow - _appStartUtc).TotalMilliseconds;
      MarkersMs["shell_interactive_ms"] = ms;
      _shellInteractiveRecorded = true;
    }

    TryWriteArtifact();
  }

  /// <summary>
  /// Best-effort write after T2 and deferred init complete (may also write earlier if only partial).
  /// </summary>
  public static void TryWriteArtifact()
  {
    lock (Gate)
    {
      if (!_shellInteractiveRecorded || !_deferredInitCompleted)
      {
        return;
      }

      try
      {
        var path = GetArtifactPath();
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir))
        {
          Directory.CreateDirectory(dir);
        }

        double? t1 = MarkersMs.TryGetValue("MainWindow Activated", out var a) ? a : null;
        double? t2 = MarkersMs.TryGetValue("shell_interactive_ms", out var b) ? b : null;

        var payload = new Dictionary<string, object?>
        {
          ["schema_version"] = SchemaVersion,
          ["app_start_utc"] = _appStartUtc.ToString("o"),
          ["t1_ms"] = t1,
          ["t2_ms"] = t2,
          ["budget_t1_ms"] = Utilities.PerformanceBudgets.StartupMs,
          ["budget_t2_ms"] = 10000,
          ["markers_ms"] = new Dictionary<string, double>(MarkersMs),
          ["deferred_init_start_ms"] = _deferredInitStartMs,
          ["deferred_init_end_ms"] = _deferredInitEndMs,
          ["panels_init_start_ms"] = _panelsInitStartMs,
          ["panels_init_end_ms"] = _panelsInitEndMs,
          ["process_id"] = Environment.ProcessId,
          ["thread_id"] = Environment.CurrentManagedThreadId,
        };

        var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(path, json);
      }
      catch (Exception ex)
      {
        ErrorLogger.LogWarning($"Cold-start timing artifact write failed: {ex.Message}", nameof(ColdStartTimingCollector));
      }
    }
  }

  /// <summary>
  /// Test hook: validates that a JSON document contains required cold-start keys.
  /// </summary>
  public static bool ValidateArtifactJson(string json, out string? error)
  {
    error = null;
    try
    {
      using var doc = JsonDocument.Parse(json);
      var root = doc.RootElement;
      foreach (var key in new[] { "schema_version", "app_start_utc", "t1_ms", "t2_ms", "markers_ms" })
      {
        if (!root.TryGetProperty(key, out _))
        {
          error = "Missing key: " + key;
          return false;
        }
      }

      if (!root.TryGetProperty("markers_ms", out var markers) || markers.ValueKind != JsonValueKind.Object)
      {
        error = "markers_ms missing or not object";
        return false;
      }

      foreach (var required in new[] { "shell_interactive_ms", "MainWindow Activated" })
      {
        if (!markers.TryGetProperty(required, out _))
        {
          error = "markers_ms missing: " + required;
          return false;
        }
      }

      return true;
    }
    catch (Exception ex)
    {
      error = ex.Message;
      return false;
    }
  }

  /// <summary>
  /// Test hook: build a minimal valid artifact string for schema tests.
  /// </summary>
  public static string BuildMinimalValidArtifactJsonForTests()
  {
    var markers = new Dictionary<string, double>
    {
      ["MainWindow Activated"] = 100,
      ["shell_interactive_ms"] = 500,
    };
    var payload = new Dictionary<string, object?>
    {
      ["schema_version"] = SchemaVersion,
      ["app_start_utc"] = DateTime.UtcNow.ToString("o"),
      ["t1_ms"] = 100.0,
      ["t2_ms"] = 500.0,
      ["budget_t1_ms"] = Utilities.PerformanceBudgets.StartupMs,
      ["budget_t2_ms"] = 10000,
      ["markers_ms"] = markers,
    };
    return JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true });
  }

  internal static string GetArtifactPath()
  {
    var root = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
    return Path.Combine(root, "VoiceStudio", "Logs", ArtifactFileName);
  }

  internal static IReadOnlyDictionary<string, double> GetMarkersSnapshotForTests()
  {
    lock (Gate)
    {
      return new Dictionary<string, double>(MarkersMs);
    }
  }

  internal static void ResetForTests()
  {
    lock (Gate)
    {
      MarkersMs.Clear();
      _shellInteractiveRecorded = false;
      _deferredInitCompleted = false;
      _deferredInitStartMs = null;
      _deferredInitEndMs = null;
      _panelsInitStartMs = null;
      _panelsInitEndMs = null;
      _appStartUtc = DateTime.UtcNow;
    }
  }
}
