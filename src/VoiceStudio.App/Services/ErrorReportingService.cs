using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using VoiceStudio.App.Logging;

namespace VoiceStudio.App.Services
{
    /// <summary>
    /// Provides opt-in error reporting functionality for crash diagnostics.
    /// Respects user privacy and only sends data when explicitly enabled.
    /// </summary>
    public class ErrorReportingService
    {
        private readonly string _reportsDirectory;
        private readonly string _settingsFilePath;
        private readonly JsonSerializerOptions _jsonOptions;
        
        private ErrorReportingSettings _settings;
        
        public ErrorReportingService()
        {
            var appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            _reportsDirectory = Path.Combine(appDataPath, "VoiceStudio", "ErrorReports");
            _settingsFilePath = Path.Combine(appDataPath, "VoiceStudio", "error_reporting_settings.json");
            Directory.CreateDirectory(_reportsDirectory);
            
            _jsonOptions = new JsonSerializerOptions
            {
                WriteIndented = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            };
            
            _settings = LoadSettings();
        }
        
        /// <summary>
        /// Gets whether error reporting is enabled by the user.
        /// </summary>
        public bool IsEnabled => _settings.Enabled;
        
        /// <summary>
        /// Enables or disables error reporting.
        /// </summary>
        public void SetEnabled(bool enabled)
        {
            _settings.Enabled = enabled;
            SaveSettings();
        }
        
        /// <summary>
        /// Creates an error report for the given exception.
        /// Returns the report ID for reference.
        /// </summary>
        public async Task<string> CreateReportAsync(Exception exception, string? context = null)
        {
            var report = new ErrorReport
            {
                ReportId = Guid.NewGuid().ToString("N")[..12].ToUpperInvariant(),
                Timestamp = DateTime.UtcNow,
                AppVersion = GetAppVersion(),
                ExceptionType = exception.GetType().FullName ?? "Unknown",
                Message = exception.Message,
                StackTrace = exception.StackTrace ?? string.Empty,
                Context = context
            };
            
            // Add system info if user opted in
            if (_settings.IncludeSystemInfo)
            {
                report.SystemInfo = GetSystemInfo();
            }
            
            // Save report locally
            var filePath = Path.Combine(_reportsDirectory, $"{report.ReportId}.json");
            var json = JsonSerializer.Serialize(report, _jsonOptions);
            await File.WriteAllTextAsync(filePath, json);
            
            // Submit if enabled (async, non-blocking)
            if (IsEnabled)
            {
                _ = TrySubmitReportAsync(report);
            }
            
            return report.ReportId;
        }
        
        /// <summary>
        /// Creates an error report from a crash log.
        /// </summary>
        public async Task<string> CreateCrashReportAsync(
            string crashLog, 
            SessionState? sessionState = null)
        {
            var report = new ErrorReport
            {
                ReportId = Guid.NewGuid().ToString("N")[..12].ToUpperInvariant(),
                Timestamp = DateTime.UtcNow,
                AppVersion = GetAppVersion(),
                ExceptionType = "CrashLog",
                Message = "Application crash detected",
                StackTrace = crashLog
            };
            
            if (sessionState != null)
            {
                report.Context = $"Session: {sessionState.SessionId}, " +
                    $"Started: {sessionState.StartTime:O}";
                    
                // Include project name only if user opted in
                if (_settings.IncludeProjectName && 
                    !string.IsNullOrEmpty(sessionState.ActiveProjectName))
                {
                    report.Context += $", Project: {sessionState.ActiveProjectName}";
                }
            }
            
            if (_settings.IncludeSystemInfo)
            {
                report.SystemInfo = GetSystemInfo();
            }
            
            var filePath = Path.Combine(_reportsDirectory, $"{report.ReportId}.json");
            var json = JsonSerializer.Serialize(report, _jsonOptions);
            await File.WriteAllTextAsync(filePath, json);
            
            if (IsEnabled)
            {
                _ = TrySubmitReportAsync(report);
            }
            
            return report.ReportId;
        }
        
        /// <summary>
        /// Gets a list of pending (unsubmitted) error reports.
        /// </summary>
        public IEnumerable<ErrorReportSummary> GetPendingReports()
        {
            var reports = new List<ErrorReportSummary>();
            
            if (!Directory.Exists(_reportsDirectory))
                return reports;
                
            foreach (var file in Directory.GetFiles(_reportsDirectory, "*.json"))
            {
                try
                {
                    var json = File.ReadAllText(file);
                    var report = JsonSerializer.Deserialize<ErrorReport>(json, _jsonOptions);
                    
                    if (report != null && !report.Submitted)
                    {
                        reports.Add(new ErrorReportSummary
                        {
                            ReportId = report.ReportId,
                            Timestamp = report.Timestamp,
                            ExceptionType = report.ExceptionType,
                            Message = report.Message
                        });
                    }
                }
                // ALLOWED: empty catch - Skip malformed reports, failure is acceptable
                catch
                {
                }
            }
            
            return reports;
        }
        
        /// <summary>
        /// Submits all pending reports (call when user explicitly opts in).
        /// </summary>
        public async Task SubmitPendingReportsAsync()
        {
            if (!IsEnabled)
                return;
                
            foreach (var file in Directory.GetFiles(_reportsDirectory, "*.json"))
            {
                try
                {
                    var json = await File.ReadAllTextAsync(file);
                    var report = JsonSerializer.Deserialize<ErrorReport>(json, _jsonOptions);
                    
                    if (report != null && !report.Submitted)
                    {
                        await TrySubmitReportAsync(report);
                    }
                }
                // ALLOWED: empty catch - Skip problematic reports, failure is acceptable
                catch
                {
                }
            }
        }
        
        /// <summary>
        /// Deletes all local error reports.
        /// </summary>
        public void ClearReports()
        {
            try
            {
                if (Directory.Exists(_reportsDirectory))
                {
                    foreach (var file in Directory.GetFiles(_reportsDirectory, "*.json"))
                    {
                        File.Delete(file);
                    }
                }
            }
            // ALLOWED: empty catch - Best effort cleanup, failure is acceptable
            catch
            {
            }
        }
        
        private async Task TrySubmitReportAsync(ErrorReport report)
        {
            // NOTE: This is a placeholder for future remote submission.
            // VoiceStudio follows local-first principles - actual submission
            // would only occur to a self-hosted endpoint or with explicit
            // user consent to a privacy-respecting service.
            //
            // For now, reports are stored locally only.
            
            try
            {
                // Mark as submitted (locally processed)
                report.Submitted = true;
                report.SubmittedAt = DateTime.UtcNow;
                
                var filePath = Path.Combine(_reportsDirectory, $"{report.ReportId}.json");
                var json = JsonSerializer.Serialize(report, _jsonOptions);
                await File.WriteAllTextAsync(filePath, json);
                
                Debug.WriteLine($"[ErrorReporting] Report {report.ReportId} processed.");
            }
            catch (Exception ex)
            {
                ErrorLogger.LogWarning($"Failed to process error report: {ex.Message}", "ErrorReporting");
            }
        }
        
        private string GetAppVersion()
        {
            var assembly = System.Reflection.Assembly.GetExecutingAssembly();
            var version = assembly.GetName().Version;
            return version?.ToString() ?? "Unknown";
        }
        
        private SystemInfo GetSystemInfo()
        {
            return new SystemInfo
            {
                OSVersion = Environment.OSVersion.ToString(),
                Is64Bit = Environment.Is64BitOperatingSystem,
                ProcessorCount = Environment.ProcessorCount,
                DotNetVersion = Environment.Version.ToString(),
                WorkingSet = Environment.WorkingSet
            };
        }
        
        private ErrorReportingSettings LoadSettings()
        {
            try
            {
                if (File.Exists(_settingsFilePath))
                {
                    var json = File.ReadAllText(_settingsFilePath);
                    return JsonSerializer.Deserialize<ErrorReportingSettings>(json, _jsonOptions) 
                        ?? new ErrorReportingSettings();
                }
            }
            catch (Exception ex)
            {
                ErrorLogger.LogWarning($"Failed to load error reporting settings: {ex.Message}", "ErrorReporting");
            }
            return new ErrorReportingSettings();
        }
        
        private void SaveSettings()
        {
            try
            {
                var json = JsonSerializer.Serialize(_settings, _jsonOptions);
                File.WriteAllText(_settingsFilePath, json);
            }
            catch (Exception ex)
            {
                ErrorLogger.LogWarning($"Failed to save error reporting settings: {ex.Message}", "ErrorReporting");
            }
        }
    }
    
    public class ErrorReportingSettings
    {
        public bool Enabled { get; set; }
        public bool IncludeSystemInfo { get; set; }
        public bool IncludeProjectName { get; set; }
    }
    
    public class ErrorReport
    {
        public string ReportId { get; set; } = string.Empty;
        public DateTime Timestamp { get; set; }
        public string AppVersion { get; set; } = string.Empty;
        public string ExceptionType { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
        public string StackTrace { get; set; } = string.Empty;
        public string? Context { get; set; }
        public SystemInfo? SystemInfo { get; set; }
        public bool Submitted { get; set; }
        public DateTime? SubmittedAt { get; set; }
    }
    
    public class ErrorReportSummary
    {
        public string ReportId { get; set; } = string.Empty;
        public DateTime Timestamp { get; set; }
        public string ExceptionType { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
    }
    
    public class SystemInfo
    {
        public string OSVersion { get; set; } = string.Empty;
        public bool Is64Bit { get; set; }
        public int ProcessorCount { get; set; }
        public string DotNetVersion { get; set; } = string.Empty;
        public long WorkingSet { get; set; }
    }
}
