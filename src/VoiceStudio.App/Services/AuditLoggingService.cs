using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;

namespace VoiceStudio.App.Services
{
    /// <summary>
    /// Interface for audit logging service.
    /// Provides comprehensive audit trail functionality for VoiceStudio.
    /// </summary>
    public interface IAuditLoggingService
    {
        /// <summary>
        /// Log a file change event.
        /// </summary>
        string LogFileChange(string filePath, string operation, string role, string taskId, string summary);

        /// <summary>
        /// Log a XAML binding failure.
        /// </summary>
        string LogXamlBindingFailure(string xamlPath, string bindingPath, string message);

        /// <summary>
        /// Log a runtime exception with audit context.
        /// </summary>
        string LogRuntimeException(Exception ex, string subsystem, string? correlationId = null);

        /// <summary>
        /// Link a crash artifact to an existing audit entry.
        /// </summary>
        void LinkCrashArtifact(string entryId, string crashPath);

        /// <summary>
        /// Log a build event (warning or error).
        /// </summary>
        string LogBuildEvent(string eventType, string? errorCode, string? message, string? filePath = null);

        /// <summary>
        /// Get recent audit entries.
        /// </summary>
        IReadOnlyList<AuditEntry> GetRecentEntries(int limit = 100);

        /// <summary>
        /// Event raised when a new audit entry is logged.
        /// </summary>
        event EventHandler<AuditEntry>? AuditEntryLogged;
    }

    /// <summary>
    /// Represents a single audit log entry.
    /// Matches the Python AuditEntry schema for cross-platform consistency.
    /// </summary>
    public class AuditEntry
    {
        public string Timestamp { get; set; } = DateTime.UtcNow.ToString("O");
        public string EntryId { get; set; } = Guid.NewGuid().ToString("N")[..8];
        public string EventType { get; set; } = string.Empty;
        public string CorrelationId { get; set; } = Guid.NewGuid().ToString("N");
        public string? TaskId { get; set; }
        public string? Role { get; set; }
        public string Actor { get; set; } = "system";
        public string? FilePath { get; set; }
        public string? Operation { get; set; }
        public int LinesAdded { get; set; }
        public int LinesRemoved { get; set; }
        public string? ErrorCode { get; set; }
        public string? Message { get; set; }
        public string? StackTrace { get; set; }
        public string? Severity { get; set; }
        public string? CommitHash { get; set; }
        public string? Subsystem { get; set; }
        public string? Gate { get; set; }
        public List<string> LinkedArtifacts { get; set; } = new();
        public List<string> Tags { get; set; } = new();
        public string Summary { get; set; } = string.Empty;
        public Dictionary<string, object> Extra { get; set; } = new();
    }

    /// <summary>
    /// Implementation of IAuditLoggingService for comprehensive audit logging.
    /// Integrates with ErrorLoggingService and writes to .audit/ directory.
    /// </summary>
    public class AuditLoggingService : IAuditLoggingService, IDisposable
    {
        private readonly IErrorLoggingService _errorLogger;
        private readonly string _auditDir;
        private readonly string _filesDir;
        private readonly string _tasksDir;
        private readonly object _lock = new();
        private readonly List<AuditEntry> _recentEntries = new();
        private readonly JsonSerializerOptions _jsonOptions;
        private StreamWriter? _logFileWriter;
        private string _currentLogDate = string.Empty;
        private bool _disposed;

        public event EventHandler<AuditEntry>? AuditEntryLogged;

        public AuditLoggingService(IErrorLoggingService errorLogger)
        {
            _errorLogger = errorLogger;

            // Set up audit directory in LocalAppData
            var appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            _auditDir = Path.Combine(appDataPath, "VoiceStudio", "Audit");
            _filesDir = Path.Combine(_auditDir, "files");
            _tasksDir = Path.Combine(_auditDir, "tasks");

            // Create directories
            Directory.CreateDirectory(_auditDir);
            Directory.CreateDirectory(_filesDir);
            Directory.CreateDirectory(_tasksDir);

            // JSON serializer options
            _jsonOptions = new JsonSerializerOptions
            {
                WriteIndented = false,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            };

            // Initialize log file
            EnsureLogFile();
        }

        private void EnsureLogFile()
        {
            var today = DateTime.UtcNow.ToString("yyyy-MM-dd");
            if (today == _currentLogDate && _logFileWriter != null)
                return;

            // Close previous file if exists
            _logFileWriter?.Dispose();

            // Create new daily log file
            _currentLogDate = today;
            var logFilePath = Path.Combine(_auditDir, $"log-{today}.jsonl");
            try
            {
                _logFileWriter = new StreamWriter(logFilePath, append: true)
                {
                    AutoFlush = true
                };
            }
            catch (Exception ex)
            {
                _logFileWriter = null;
                _errorLogger.LogWarning(
                    $"Failed to create audit log file: {ex.Message}",
                    "AuditLogging",
                    new Dictionary<string, object>
                    {
                        ["logFilePath"] = logFilePath,
                        ["exception"] = ex.GetType().Name
                    });
            }
        }

        private string GetCurrentCommitHash()
        {
            try
            {
                // Try to get current git commit hash
                var workingDir = AppContext.BaseDirectory;
                var repoRoot = FindGitRoot(workingDir);
                if (repoRoot == null) return string.Empty;

                var headFile = Path.Combine(repoRoot, ".git", "HEAD");
                if (!File.Exists(headFile)) return string.Empty;

                var headContent = File.ReadAllText(headFile).Trim();
                if (headContent.StartsWith("ref: "))
                {
                    var refPath = headContent[5..];
                    var refFile = Path.Combine(repoRoot, ".git", refPath);
                    if (File.Exists(refFile))
                    {
                        return File.ReadAllText(refFile).Trim()[..7];
                    }
                }
                else if (headContent.Length >= 7)
                {
                    return headContent[..7];
                }
            }
            catch (Exception ex)
            {
                // Git info is optional - log at debug level only
                System.Diagnostics.Debug.WriteLine($"[AuditLogging] Git info read failed: {ex.Message}");
            }
            return string.Empty;
        }

        private static string? FindGitRoot(string startPath)
        {
            var dir = new DirectoryInfo(startPath);
            while (dir != null)
            {
                if (Directory.Exists(Path.Combine(dir.FullName, ".git")))
                    return dir.FullName;
                dir = dir.Parent;
            }
            return null;
        }

        private string MapFileToSubsystem(string? filePath)
        {
            if (string.IsNullOrEmpty(filePath)) return "unknown";

            if (filePath.Contains("Views/Panels") || filePath.Contains("Views\\Panels"))
                return "UI.Panels";
            if (filePath.Contains("ViewModels"))
                return "UI.ViewModels";
            if (filePath.Contains("Resources/Styles") || filePath.Contains("Resources\\Styles"))
                return "UI.Styles.Global";
            if (filePath.Contains("backend/api") || filePath.Contains("backend\\api"))
                return "Backend.API";
            if (filePath.Contains("app/core/engines") || filePath.Contains("app\\core\\engines"))
                return "Engines";
            if (filePath.EndsWith(".xaml", StringComparison.OrdinalIgnoreCase))
                return "UI.XAML";
            if (filePath.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
                return "App.CSharp";
            if (filePath.EndsWith(".py", StringComparison.OrdinalIgnoreCase))
                return "Backend.Python";

            return "unknown";
        }

        private void WriteEntry(AuditEntry entry)
        {
            lock (_lock)
            {
                // Add to recent entries
                _recentEntries.Insert(0, entry);
                if (_recentEntries.Count > 1000)
                    _recentEntries.RemoveAt(_recentEntries.Count - 1);

                // Write to daily log file
                EnsureLogFile();
                try
                {
                    var json = JsonSerializer.Serialize(entry, _jsonOptions);
                    _logFileWriter?.WriteLine(json);
                }
                catch (Exception ex)
                {
                    _errorLogger.LogWarning(
                        $"Failed to write audit entry to daily log: {ex.Message}",
                        "AuditLogging",
                        new Dictionary<string, object>
                        {
                            ["entryId"] = entry.EntryId,
                            ["eventType"] = entry.EventType,
                            ["exception"] = ex.GetType().Name
                        });
                }

                // Write to per-file log if applicable
                if (!string.IsNullOrEmpty(entry.FilePath))
                {
                    WriteToFileLog(entry);
                }

                // Write to per-task log if applicable
                if (!string.IsNullOrEmpty(entry.TaskId))
                {
                    WriteToTaskLog(entry);
                }
            }

            // Raise event
            AuditEntryLogged?.Invoke(this, entry);
        }

        private void WriteToFileLog(AuditEntry entry)
        {
            if (string.IsNullOrEmpty(entry.FilePath)) return;

            try
            {
                var safeName = entry.FilePath
                    .Replace("/", "_")
                    .Replace("\\", "_")
                    .Replace(":", "_");
                if (safeName.Length > 100) safeName = safeName[^100..];

                var filePath = Path.Combine(_filesDir, $"{safeName}.log");
                var json = JsonSerializer.Serialize(entry, _jsonOptions);
                File.AppendAllText(filePath, json + Environment.NewLine);
            }
            catch (Exception ex)
            {
                _errorLogger.LogWarning(
                    $"Failed to write audit entry to per-file log: {ex.Message}",
                    "AuditLogging",
                    new Dictionary<string, object>
                    {
                        ["entryId"] = entry.EntryId,
                        ["filePath"] = entry.FilePath ?? "unknown",
                        ["exception"] = ex.GetType().Name
                    });
            }
        }

        private void WriteToTaskLog(AuditEntry entry)
        {
            if (string.IsNullOrEmpty(entry.TaskId)) return;

            try
            {
                var taskFile = Path.Combine(_tasksDir, $"{entry.TaskId}.json");
                List<AuditEntry> entries = new();

                if (File.Exists(taskFile))
                {
                    var existing = File.ReadAllText(taskFile);
                    entries = JsonSerializer.Deserialize<List<AuditEntry>>(existing, _jsonOptions) ?? new();
                }

                entries.Add(entry);
                File.WriteAllText(taskFile, JsonSerializer.Serialize(entries, new JsonSerializerOptions
                {
                    WriteIndented = true,
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                }));
            }
            catch (Exception ex)
            {
                _errorLogger.LogWarning(
                    $"Failed to write audit entry to per-task log: {ex.Message}",
                    "AuditLogging",
                    new Dictionary<string, object>
                    {
                        ["entryId"] = entry.EntryId,
                        ["taskId"] = entry.TaskId ?? "unknown",
                        ["exception"] = ex.GetType().Name
                    });
            }
        }

        public string LogFileChange(string filePath, string operation, string role, string taskId, string summary)
        {
            var eventType = operation.ToLowerInvariant() switch
            {
                "create" => "file_create",
                "modify" => "file_modify",
                "delete" => "file_delete",
                "rename" => "file_rename",
                _ => "file_modify"
            };

            var entry = new AuditEntry
            {
                EventType = eventType,
                FilePath = filePath,
                Operation = operation.ToLowerInvariant(),
                Role = role,
                TaskId = taskId,
                Summary = summary,
                Actor = "human",
                CommitHash = GetCurrentCommitHash(),
                Subsystem = MapFileToSubsystem(filePath)
            };

            WriteEntry(entry);

            // Also log to error logging service for unified view
            _errorLogger.LogInfo($"File {operation}: {filePath}", "Audit", new Dictionary<string, object>
            {
                ["entryId"] = entry.EntryId,
                ["taskId"] = taskId,
                ["role"] = role
            });

            return entry.EntryId;
        }

        public string LogXamlBindingFailure(string xamlPath, string bindingPath, string message)
        {
            var entry = new AuditEntry
            {
                EventType = "xaml_binding_failure",
                FilePath = xamlPath,
                Message = message,
                Severity = "warning",
                Subsystem = "UI.XAML",
                Actor = "system",
                CommitHash = GetCurrentCommitHash(),
                Summary = $"XAML binding failure in {Path.GetFileName(xamlPath)}: {bindingPath}",
                Extra = new Dictionary<string, object>
                {
                    ["bindingPath"] = bindingPath
                }
            };

            WriteEntry(entry);

            // Also log as warning
            _errorLogger.LogWarning($"XAML binding failure: {bindingPath} in {xamlPath}", "XAML", new Dictionary<string, object>
            {
                ["bindingPath"] = bindingPath,
                ["xamlPath"] = xamlPath
            });

            return entry.EntryId;
        }

        public string LogRuntimeException(Exception ex, string subsystem, string? correlationId = null)
        {
            var entry = new AuditEntry
            {
                EventType = "runtime_exception",
                Message = ex.Message,
                StackTrace = ex.StackTrace,
                Severity = "error",
                Subsystem = subsystem,
                CorrelationId = correlationId ?? Guid.NewGuid().ToString("N"),
                Actor = "system",
                CommitHash = GetCurrentCommitHash(),
                Summary = $"Exception in {subsystem}: {ex.GetType().Name}",
                Extra = new Dictionary<string, object>
                {
                    ["exceptionType"] = ex.GetType().FullName ?? ex.GetType().Name
                }
            };

            WriteEntry(entry);

            return entry.EntryId;
        }

        public void LinkCrashArtifact(string entryId, string crashPath)
        {
            lock (_lock)
            {
                var entry = _recentEntries.Find(e => e.EntryId == entryId);
                if (entry != null)
                {
                    entry.LinkedArtifacts.Add(crashPath);
                }
            }
        }

        public string LogBuildEvent(string eventType, string? errorCode, string? message, string? filePath = null)
        {
            var severity = eventType.Contains("error", StringComparison.OrdinalIgnoreCase) ? "error" : "warning";

            var entry = new AuditEntry
            {
                EventType = eventType,
                ErrorCode = errorCode,
                Message = message,
                FilePath = filePath,
                Severity = severity,
                Actor = "system",
                CommitHash = GetCurrentCommitHash(),
                Subsystem = MapFileToSubsystem(filePath),
                Summary = $"Build {eventType}: {errorCode ?? "unknown"} in {filePath ?? "build"}"
            };

            WriteEntry(entry);

            return entry.EntryId;
        }

        public IReadOnlyList<AuditEntry> GetRecentEntries(int limit = 100)
        {
            lock (_lock)
            {
                var count = Math.Min(limit, _recentEntries.Count);
                return _recentEntries.GetRange(0, count).AsReadOnly();
            }
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
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
