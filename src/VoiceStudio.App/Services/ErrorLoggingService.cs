using System;
using System.Collections.Generic;
using System.Linq;
using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using VoiceStudio.Core.Exceptions;
using VoiceStudio.App.Utilities;
using VoiceStudio.App.Logging;

namespace VoiceStudio.App.Services
{
  /// <summary>
  /// Implementation of IErrorLoggingService for centralized error logging with structured logging support.
  /// GAP-I12: Enhanced with correlation ID provider for cross-layer tracing.
  /// </summary>
  public class ErrorLoggingService : IErrorLoggingService, IDisposable
  {
    private readonly List<ErrorLogEntry> _logEntries = new();
    private readonly Dictionary<string, List<Breadcrumb>> _breadcrumbs = new();
    private readonly Dictionary<string, CorrelationContext> _correlations = new();
    private readonly object _lock = new();
    private const int MaxLogEntries = 1000;
    private const int MaxBreadcrumbsPerCorrelation = 100;
    private readonly string _logDirectory;
    private readonly string _logFilePath;
    private readonly JsonSerializerOptions _jsonOptions;
    private readonly StreamWriter? _logFileWriter;
    private readonly ICorrelationIdProvider? _correlationProvider;
    private bool _disposed;

    public event EventHandler<ErrorLogEntry>? ErrorLogged;

    /// <summary>
    /// Initializes a new instance of ErrorLoggingService without correlation provider.
    /// </summary>
    public ErrorLoggingService() : this(null)
    {
    }

    /// <summary>
    /// GAP-I12: Initializes a new instance with optional correlation ID provider.
    /// </summary>
    /// <param name="correlationProvider">Optional provider for correlation context.</param>
    public ErrorLoggingService(ICorrelationIdProvider? correlationProvider)
    {
      _correlationProvider = correlationProvider;

      // Set up log directory
      var appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
      _logDirectory = Path.Combine(appDataPath, "VoiceStudio", "Logs");
      Directory.CreateDirectory(_logDirectory);

      // Create daily log file
      var logFileName = $"voicestudio_{DateTime.Now:yyyyMMdd}.jsonl";
      _logFilePath = Path.Combine(_logDirectory, logFileName);

      // JSON serializer options
      _jsonOptions = new JsonSerializerOptions
      {
        WriteIndented = false,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
      };

      // Open log file for appending
      try
      {
        _logFileWriter = new StreamWriter(_logFilePath, append: true)
        {
          AutoFlush = true
        };
      }
      catch
      {
        // If file writing fails, continue with in-memory logging only
        _logFileWriter = null;
      }
    }

    public void LogError(Exception exception, string context = "", Dictionary<string, object>? metadata = null)
    {
      if (exception == null)
        return;

      // GAP-I12: Include correlation ID for cross-layer tracing
      var correlationId = GetCurrentCorrelationId();

      var entry = new ErrorLogEntry
      {
        Timestamp = DateTime.UtcNow,
        Level = "Error",
        Message = LogRedactionHelper.Redact(GetErrorMessage(exception)),
        Context = context,
        ExceptionType = exception.GetType().Name,
        StackTrace = exception.StackTrace,
        Metadata = LogRedactionHelper.RedactMetadata(metadata),
        CorrelationId = correlationId
      };

      // Add BackendException-specific metadata
      if (exception is BackendException bex)
      {
        entry.Metadata ??= new Dictionary<string, object>();
        if (bex.StatusCode.HasValue)
          entry.Metadata["StatusCode"] = bex.StatusCode.Value;
        if (!string.IsNullOrEmpty(bex.ErrorCode))
          entry.Metadata["ErrorCode"] = bex.ErrorCode;
        entry.Metadata["IsRetryable"] = bex.IsRetryable;
      }

      lock (_lock)
      {
        _logEntries.Add(entry);

        // Keep only the most recent entries
        if (_logEntries.Count > MaxLogEntries)
        {
          _logEntries.RemoveAt(0);
        }
      }

      // Write structured log to file
      WriteStructuredLog(entry);

      ErrorLogged?.Invoke(this, entry);
    }

    public void LogWarning(string message, string context = "", Dictionary<string, object>? metadata = null)
    {
      var correlationId = GetCurrentCorrelationId();
      var entry = new ErrorLogEntry
      {
        Timestamp = DateTime.UtcNow,
        Level = "Warning",
        Message = LogRedactionHelper.Redact(message),
        Context = context,
        Metadata = LogRedactionHelper.RedactMetadata(metadata),
        CorrelationId = correlationId
      };

      lock (_lock)
      {
        _logEntries.Add(entry);

        if (_logEntries.Count > MaxLogEntries)
        {
          _logEntries.RemoveAt(0);
        }
      }

      // Write structured log to file
      WriteStructuredLog(entry);

      ErrorLogged?.Invoke(this, entry);
    }

    public void LogInfo(string message, string context = "", Dictionary<string, object>? metadata = null)
    {
      var correlationId = GetCurrentCorrelationId();
      var entry = new ErrorLogEntry
      {
        Timestamp = DateTime.UtcNow,
        Level = "Info",
        Message = LogRedactionHelper.Redact(message),
        Context = context,
        Metadata = LogRedactionHelper.RedactMetadata(metadata),
        CorrelationId = correlationId
      };

      lock (_lock)
      {
        _logEntries.Add(entry);

        if (_logEntries.Count > MaxLogEntries)
        {
          _logEntries.RemoveAt(0);
        }
      }

      // Write structured log to file
      WriteStructuredLog(entry);

      ErrorLogged?.Invoke(this, entry);
    }

    /// <summary>
    /// Writes a structured log entry to file in JSON Lines format (JSONL).
    /// GAP-I12: Enhanced with correlation, trace, and span IDs.
    /// </summary>
    private void WriteStructuredLog(ErrorLogEntry entry)
    {
      if (_logFileWriter == null)
        return;

      try
      {
        // GAP-I12: Capture correlation context
        var correlationId = entry.CorrelationId ?? GetCurrentCorrelationId() ?? "N/A";
        var traceId = GetCurrentTraceId() ?? "N/A";
        var spanId = GetCurrentSpanId() ?? "N/A";

        // Create structured log object with correlation context
        var structuredLog = new
        {
          timestamp = entry.Timestamp.ToString("O"), // ISO 8601 format
          level = entry.Level,
          message = entry.Message,
          context = entry.Context ?? string.Empty,
          correlationId = correlationId,
          traceId = traceId,
          spanId = spanId,
          exceptionType = entry.ExceptionType ?? string.Empty,
          stackTrace = entry.StackTrace ?? string.Empty,
          metadata = entry.Metadata ?? new Dictionary<string, object>()
        };

        // Serialize to JSON and write as single line (JSONL format)
        var jsonLine = JsonSerializer.Serialize(structuredLog, _jsonOptions);
        _logFileWriter.WriteLine(jsonLine);
      }
      catch (Exception ex)
      {
        ErrorLogger.LogWarning($"Best effort operation failed: {ex.Message}", "ErrorLoggingService.WriteStructuredLog");
      }
    }

    /// <summary>
    /// Exports logs in structured JSON format.
    /// </summary>
    public string ExportLogsAsJson(int count = 1000)
    {
      lock (_lock)
      {
        var entries = _logEntries
            .OrderByDescending(e => e.Timestamp)
            .Take(count)
            .ToList();

        var exportData = new
        {
          exportTimestamp = DateTime.UtcNow.ToString("O"),
          totalEntries = entries.Count,
          entries = entries.Select(e => new
          {
            timestamp = e.Timestamp.ToString("O"),
            level = e.Level,
            message = e.Message,
            context = e.Context ?? string.Empty,
            exceptionType = e.ExceptionType ?? string.Empty,
            stackTrace = e.StackTrace ?? string.Empty,
            metadata = e.Metadata ?? new Dictionary<string, object>()
          })
        };

        var jsonOptions = new JsonSerializerOptions
        {
          WriteIndented = true,
          PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        return JsonSerializer.Serialize(exportData, jsonOptions);
      }
    }

    /// <summary>
    /// Exports logs in key-value format (for easy parsing).
    /// </summary>
    public string ExportLogsAsKeyValue(int count = 1000)
    {
      lock (_lock)
      {
        var entries = _logEntries
            .OrderByDescending(e => e.Timestamp)
            .Take(count)
            .ToList();

        var lines = new List<string>();
        foreach (var entry in entries)
        {
          var kvp = new List<string>
                    {
                        $"timestamp={entry.Timestamp:O}",
                        $"level={entry.Level}",
                        $"message={EscapeValue(entry.Message)}",
                        $"context={EscapeValue(entry.Context ?? string.Empty)}"
                    };

          if (!string.IsNullOrEmpty(entry.ExceptionType))
            kvp.Add($"exceptionType={entry.ExceptionType}");

          if (!string.IsNullOrEmpty(entry.StackTrace))
            kvp.Add($"stackTrace={EscapeValue(entry.StackTrace)}");

          if (entry.Metadata?.Count > 0)
          {
            foreach (var meta in entry.Metadata)
            {
              kvp.Add($"metadata.{meta.Key}={EscapeValue(meta.Value?.ToString() ?? string.Empty)}");
            }
          }

          lines.Add(string.Join(" ", kvp));
        }

        return string.Join(Environment.NewLine, lines);
      }
    }

    private static string EscapeValue(string value)
    {
      return value
          .Replace(" ", "\\ ")
          .Replace("=", "\\=")
          .Replace("\n", "\\n")
          .Replace("\r", "\\r");
    }

    public IReadOnlyList<ErrorLogEntry> GetRecentErrors(int count = 100)
    {
      lock (_lock)
      {
        return _logEntries
            .OrderByDescending(e => e.Timestamp)
            .Take(count)
            .ToList()
            .AsReadOnly();
      }
    }

    public void ClearLogs()
    {
      lock (_lock)
      {
        _logEntries.Clear();
      }
    }

    private string GetErrorMessage(Exception ex)
    {
      return ex switch
      {
        BackendException bex => bex.Message,
        _ => ex.Message
      };
    }

    public void Dispose()
    {
      Dispose(true);
      GC.SuppressFinalize(this);
    }

    public string StartCorrelation(string action, Dictionary<string, object>? metadata = null)
    {
      var correlationId = Guid.NewGuid().ToString("N");
      var context = new CorrelationContext
      {
        CorrelationId = correlationId,
        Action = action,
        StartTime = DateTime.UtcNow,
        Metadata = metadata ?? new Dictionary<string, object>()
      };

      lock (_lock)
      {
        _correlations[correlationId] = context;
      }

      LogInfo($"Correlation started: {action}", "Correlation", new Dictionary<string, object>
      {
        ["correlationId"] = correlationId,
        ["action"] = action
      }.Merge(metadata));

      return correlationId;
    }

    public void EndCorrelation(string correlationId, bool success = true, string? message = null)
    {
      CorrelationContext? context;
      lock (_lock)
      {
        if (!_correlations.TryGetValue(correlationId, out context))
          return;

        context.EndTime = DateTime.UtcNow;
        context.Success = success;
      }

      var duration = context.EndTime.HasValue
          ? (context.EndTime.Value - context.StartTime).TotalMilliseconds
          : 0;
      var level = success ? "Info" : "Warning";
      var logMessage = message ?? $"Correlation ended: {context.Action} ({(success ? "success" : "failed")})";

      LogInfo(logMessage, "Correlation", new Dictionary<string, object>
      {
        ["correlationId"] = correlationId,
        ["action"] = context.Action,
        ["success"] = success,
        ["durationMs"] = duration
      });

      // Clean up old correlations (keep last 100)
      lock (_lock)
      {
        if (_correlations.Count > 100)
        {
          var oldest = _correlations.OrderBy(c => c.Value.StartTime).First();
          _correlations.Remove(oldest.Key);
          _breadcrumbs.Remove(oldest.Key);
        }
      }
    }

    public void AddBreadcrumb(string message, string category = "UserAction", Dictionary<string, object>? metadata = null)
    {
      var correlationId = GetCurrentCorrelationId();
      var breadcrumb = new Breadcrumb
      {
        Timestamp = DateTime.UtcNow,
        Message = message,
        Category = category,
        CorrelationId = correlationId,
        Metadata = metadata
      };

      if (string.IsNullOrEmpty(correlationId))
        return;

      lock (_lock)
      {
        if (!_breadcrumbs.TryGetValue(correlationId, out var crumbs))
        {
          crumbs = new List<Breadcrumb>();
          _breadcrumbs[correlationId] = crumbs;
        }

        crumbs.Add(breadcrumb);

        // Keep only recent breadcrumbs
        if (crumbs.Count > MaxBreadcrumbsPerCorrelation)
        {
          crumbs.RemoveAt(0);
        }
      }
    }

    public IReadOnlyList<Breadcrumb> GetBreadcrumbs(string correlationId)
    {
      lock (_lock)
      {
        if (_breadcrumbs.TryGetValue(correlationId, out var crumbs))
        {
          return crumbs.AsReadOnly();
        }
        return Array.Empty<Breadcrumb>().AsReadOnly();
      }
    }

    /// <summary>
    /// GAP-I12: Gets the current correlation ID from provider or internal context.
    /// </summary>
    private string? GetCurrentCorrelationId()
    {
      // GAP-I12: First try the injected correlation provider (AsyncLocal-based)
      if (_correlationProvider != null)
      {
        var providedId = _correlationProvider.GetCurrentCorrelationId();
        if (!string.IsNullOrEmpty(providedId))
        {
          return providedId;
        }
      }

      // Fallback to internal correlation context tracking
      lock (_lock)
      {
        var mostRecent = _correlations.Values
            .Where(c => c.EndTime == null)
            .OrderByDescending(c => c.StartTime)
            .FirstOrDefault();

        return mostRecent?.CorrelationId;
      }
    }

    /// <summary>
    /// GAP-I12: Gets the current trace ID from correlation provider.
    /// </summary>
    private string? GetCurrentTraceId()
    {
      return _correlationProvider?.GetCurrentTraceId();
    }

    /// <summary>
    /// GAP-I12: Gets the current span ID from correlation provider.
    /// </summary>
    private string? GetCurrentSpanId()
    {
      return _correlationProvider?.GetCurrentSpanId();
    }

    private class CorrelationContext
    {
      public string CorrelationId { get; set; } = string.Empty;
      public string Action { get; set; } = string.Empty;
      public DateTime StartTime { get; set; }
      public DateTime? EndTime { get; set; }
      public bool Success { get; set; }
      public Dictionary<string, object> Metadata { get; set; } = new();
    }

    protected virtual void Dispose(bool disposing)
    {
      if (!_disposed)
      {
        if (disposing)
        {
          _logFileWriter?.Dispose();
        }
        _disposed = true;
      }
    }
  }
}