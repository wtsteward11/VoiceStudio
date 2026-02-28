// =============================================================================
// StartupDiagnostics.cs — Phase 5.3.4
// Performs launch-time diagnostic checks for system health verification.
// =============================================================================

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using VoiceStudio.App.Logging;

namespace VoiceStudio.App.Services
{
    /// <summary>
    /// Performs diagnostic checks during application startup.
    /// Validates system requirements, dependencies, and backend connectivity.
    /// </summary>
    public sealed class StartupDiagnostics
    {
        private readonly string _backendBaseUrl;
        private readonly List<DiagnosticCheck> _checks = new();
        private readonly Stopwatch _totalStopwatch = new();
        private readonly JsonSerializerOptions _jsonOptions;

        /// <summary>
        /// Gets the results of all diagnostic checks.
        /// </summary>
        public IReadOnlyList<DiagnosticCheck> Checks => _checks;

        /// <summary>
        /// Gets the total time taken for all checks.
        /// </summary>
        public TimeSpan TotalDuration => _totalStopwatch.Elapsed;

        /// <summary>
        /// Gets whether all checks passed.
        /// </summary>
        public bool AllChecksPassed { get; private set; }

        /// <summary>
        /// Gets whether any critical checks failed.
        /// </summary>
        public bool HasCriticalFailures { get; private set; }

        /// <summary>
        /// Event raised when a check completes.
        /// </summary>
        public event EventHandler<DiagnosticCheck>? CheckCompleted;

        /// <summary>
        /// Initializes a new instance of the <see cref="StartupDiagnostics"/> class.
        /// </summary>
        public StartupDiagnostics()
        {
            _backendBaseUrl = Environment.GetEnvironmentVariable("VOICESTUDIO_BACKEND_URL")
                ?? "http://localhost:8001";
            _jsonOptions = new JsonSerializerOptions
            {
                WriteIndented = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            };
        }

        /// <summary>
        /// Runs all startup diagnostic checks.
        /// </summary>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>True if all checks passed; otherwise, false.</returns>
        public async Task<bool> RunAllChecksAsync(CancellationToken cancellationToken = default)
        {
            _checks.Clear();
            _totalStopwatch.Restart();

            // Run checks in order
            await CheckSystemRequirementsAsync(cancellationToken);
            await CheckDirectoriesAsync(cancellationToken);
            await CheckBackendConnectivityAsync(cancellationToken);
            await CheckEngineAvailabilityAsync(cancellationToken);
            await CheckDiskSpaceAsync(cancellationToken);
            await CheckMemoryAsync(cancellationToken);

            _totalStopwatch.Stop();

            // Determine overall status
            AllChecksPassed = true;
            HasCriticalFailures = false;

            foreach (var check in _checks)
            {
                if (check.Status != CheckStatus.Passed)
                {
                    AllChecksPassed = false;
                }

                if (check.Status == CheckStatus.Failed && check.IsCritical)
                {
                    HasCriticalFailures = true;
                }
            }

            // Save diagnostics report
            await SaveDiagnosticsReportAsync(cancellationToken);

            return AllChecksPassed;
        }

        private async Task CheckSystemRequirementsAsync(CancellationToken cancellationToken)
        {
            var check = new DiagnosticCheck
            {
                Name = "System Requirements",
                Description = "Validates OS and runtime requirements",
                IsCritical = true
            };

            var stopwatch = Stopwatch.StartNew();

            try
            {
                // Check Windows version
                var osVersion = Environment.OSVersion;
                if (osVersion.Platform != PlatformID.Win32NT)
                {
                    check.Status = CheckStatus.Failed;
                    check.Message = "VoiceStudio requires Windows operating system";
                }
                else if (osVersion.Version.Major < 10)
                {
                    check.Status = CheckStatus.Failed;
                    check.Message = "VoiceStudio requires Windows 10 or later";
                }
                else
                {
                    check.Status = CheckStatus.Passed;
                    check.Message = $"Windows {osVersion.Version}";
                    check.Details = new Dictionary<string, object>
                    {
                        ["osVersion"] = osVersion.ToString(),
                        ["is64BitOS"] = Environment.Is64BitOperatingSystem,
                        ["is64BitProcess"] = Environment.Is64BitProcess,
                        ["processorCount"] = Environment.ProcessorCount,
                        ["clrVersion"] = Environment.Version.ToString()
                    };
                }

                await Task.Delay(1, cancellationToken); // Yield
            }
            catch (Exception ex)
            {
                check.Status = CheckStatus.Failed;
                check.Message = ex.Message;
            }

            check.Duration = stopwatch.Elapsed;
            AddCheck(check);
        }

        private async Task CheckDirectoriesAsync(CancellationToken cancellationToken)
        {
            var check = new DiagnosticCheck
            {
                Name = "Required Directories",
                Description = "Validates app data directories exist and are writable",
                IsCritical = true
            };

            var stopwatch = Stopwatch.StartNew();

            try
            {
                var localAppData = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "VoiceStudio");

                var directories = new[]
                {
                    Path.Combine(localAppData, "Logs"),
                    Path.Combine(localAppData, "Config"),
                    Path.Combine(localAppData, "Cache"),
                    Path.Combine(localAppData, "Projects"),
                    Path.Combine(localAppData, "DiagnosticExports")
                };

                var createdDirs = new List<string>();
                var failedDirs = new List<string>();

                foreach (var dir in directories)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    try
                    {
                        if (!Directory.Exists(dir))
                        {
                            Directory.CreateDirectory(dir);
                            createdDirs.Add(dir);
                        }

                        // Test write permission
                        var testFile = Path.Combine(dir, ".startup_test");
                        await File.WriteAllTextAsync(testFile, "test", cancellationToken);
                        File.Delete(testFile);
                    }
                    catch
                    {
                        failedDirs.Add(dir);
                    }
                }

                if (failedDirs.Count > 0)
                {
                    check.Status = CheckStatus.Failed;
                    check.Message = $"Cannot write to {failedDirs.Count} directories";
                }
                else
                {
                    check.Status = CheckStatus.Passed;
                    check.Message = $"All {directories.Length} directories accessible";
                }

                check.Details = new Dictionary<string, object>
                {
                    ["createdDirectories"] = createdDirs,
                    ["failedDirectories"] = failedDirs
                };
            }
            catch (Exception ex)
            {
                check.Status = CheckStatus.Failed;
                check.Message = ex.Message;
            }

            check.Duration = stopwatch.Elapsed;
            AddCheck(check);
        }

        private async Task CheckBackendConnectivityAsync(CancellationToken cancellationToken)
        {
            var check = new DiagnosticCheck
            {
                Name = "Backend Connectivity",
                Description = "Verifies connection to VoiceStudio backend API",
                IsCritical = true
            };

            var stopwatch = Stopwatch.StartNew();

            try
            {
                using var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
                using var response = await httpClient.GetAsync(
                    $"{_backendBaseUrl}/health",
                    cancellationToken);

                if (response.IsSuccessStatusCode)
                {
                    check.Status = CheckStatus.Passed;
                    check.Message = "Backend API is responsive";
                    check.Details = new Dictionary<string, object>
                    {
                        ["baseUrl"] = _backendBaseUrl,
                        ["responseTime"] = stopwatch.Elapsed.TotalMilliseconds
                    };
                }
                else
                {
                    check.Status = CheckStatus.Warning;
                    check.Message = $"Backend returned HTTP {(int)response.StatusCode}";
                }
            }
            catch (TaskCanceledException)
            {
                check.Status = CheckStatus.Failed;
                check.Message = "Backend connection timed out";
            }
            catch (HttpRequestException ex)
            {
                check.Status = CheckStatus.Failed;
                check.Message = $"Cannot connect to backend: {ex.Message}";
                check.Details = new Dictionary<string, object>
                {
                    ["baseUrl"] = _backendBaseUrl,
                    ["error"] = ex.Message
                };
            }

            check.Duration = stopwatch.Elapsed;
            AddCheck(check);
        }

        private async Task CheckEngineAvailabilityAsync(CancellationToken cancellationToken)
        {
            var check = new DiagnosticCheck
            {
                Name = "Engine Availability",
                Description = "Checks if voice synthesis engines are available",
                IsCritical = false
            };

            var stopwatch = Stopwatch.StartNew();

            try
            {
                using var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
                using var response = await httpClient.GetAsync(
                    $"{_backendBaseUrl}/api/v1/engines",
                    cancellationToken);

                if (response.IsSuccessStatusCode)
                {
                    var content = await response.Content.ReadAsStringAsync(cancellationToken);
                    using var doc = JsonDocument.Parse(content);

                    var engineCount = 0;
                    if (doc.RootElement.TryGetProperty("engines", out var engines))
                    {
                        engineCount = engines.GetArrayLength();
                    }

                    if (engineCount > 0)
                    {
                        check.Status = CheckStatus.Passed;
                        check.Message = $"{engineCount} engines available";
                    }
                    else
                    {
                        check.Status = CheckStatus.Warning;
                        check.Message = "No engines configured";
                    }

                    check.Details = new Dictionary<string, object>
                    {
                        ["engineCount"] = engineCount
                    };
                }
                else
                {
                    check.Status = CheckStatus.Warning;
                    check.Message = "Could not retrieve engine list";
                }
            }
            catch (Exception ex)
            {
                check.Status = CheckStatus.Warning;
                check.Message = $"Engine check skipped: {ex.Message}";
            }

            check.Duration = stopwatch.Elapsed;
            AddCheck(check);
        }

        private async Task CheckDiskSpaceAsync(CancellationToken cancellationToken)
        {
            var check = new DiagnosticCheck
            {
                Name = "Disk Space",
                Description = "Verifies sufficient disk space is available",
                IsCritical = false
            };

            var stopwatch = Stopwatch.StartNew();

            try
            {
                var localAppData = Environment.GetFolderPath(
                    Environment.SpecialFolder.LocalApplicationData);
                var driveInfo = new DriveInfo(Path.GetPathRoot(localAppData) ?? "C:\\");

                var freeGb = driveInfo.AvailableFreeSpace / (1024.0 * 1024 * 1024);
                const double MinFreeSpaceGb = 1.0;
                const double WarningThresholdGb = 5.0;

                if (freeGb < MinFreeSpaceGb)
                {
                    check.Status = CheckStatus.Failed;
                    check.Message = $"Only {freeGb:F1} GB free (minimum: {MinFreeSpaceGb} GB)";
                }
                else if (freeGb < WarningThresholdGb)
                {
                    check.Status = CheckStatus.Warning;
                    check.Message = $"{freeGb:F1} GB free (low disk space)";
                }
                else
                {
                    check.Status = CheckStatus.Passed;
                    check.Message = $"{freeGb:F1} GB available";
                }

                check.Details = new Dictionary<string, object>
                {
                    ["drive"] = driveInfo.Name,
                    ["freeSpaceBytes"] = driveInfo.AvailableFreeSpace,
                    ["totalSpaceBytes"] = driveInfo.TotalSize,
                    ["freeSpaceGb"] = freeGb
                };

                await Task.Delay(1, cancellationToken); // Yield
            }
            catch (Exception ex)
            {
                check.Status = CheckStatus.Warning;
                check.Message = $"Could not check disk space: {ex.Message}";
            }

            check.Duration = stopwatch.Elapsed;
            AddCheck(check);
        }

        private async Task CheckMemoryAsync(CancellationToken cancellationToken)
        {
            var check = new DiagnosticCheck
            {
                Name = "Memory",
                Description = "Checks available system memory",
                IsCritical = false
            };

            var stopwatch = Stopwatch.StartNew();

            try
            {
                var gcInfo = GC.GetGCMemoryInfo();
                var totalMemoryMb = gcInfo.TotalAvailableMemoryBytes / (1024.0 * 1024);
                var workingSetMb = Environment.WorkingSet / (1024.0 * 1024);

                const double MinMemoryMb = 512;
                const double WarningMemoryMb = 1024;

                var availableMemoryMb = totalMemoryMb - workingSetMb;

                if (availableMemoryMb < MinMemoryMb)
                {
                    check.Status = CheckStatus.Warning;
                    check.Message = $"Low memory: {availableMemoryMb:F0} MB available";
                }
                else if (availableMemoryMb < WarningMemoryMb)
                {
                    check.Status = CheckStatus.Passed;
                    check.Message = $"{availableMemoryMb:F0} MB available (moderate)";
                }
                else
                {
                    check.Status = CheckStatus.Passed;
                    check.Message = $"{availableMemoryMb:F0} MB available";
                }

                check.Details = new Dictionary<string, object>
                {
                    ["totalAvailableMb"] = totalMemoryMb,
                    ["currentWorkingSetMb"] = workingSetMb,
                    ["estimatedAvailableMb"] = availableMemoryMb
                };

                await Task.Delay(1, cancellationToken); // Yield
            }
            catch (Exception ex)
            {
                check.Status = CheckStatus.Warning;
                check.Message = $"Could not check memory: {ex.Message}";
            }

            check.Duration = stopwatch.Elapsed;
            AddCheck(check);
        }

        private void AddCheck(DiagnosticCheck check)
        {
            _checks.Add(check);
            CheckCompleted?.Invoke(this, check);
        }

        private async Task SaveDiagnosticsReportAsync(CancellationToken cancellationToken)
        {
            try
            {
                var localAppData = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "VoiceStudio",
                    "Logs");

                Directory.CreateDirectory(localAppData);

                var report = new Dictionary<string, object>
                {
                    ["timestamp"] = DateTime.UtcNow.ToString(
                        "o",
                        CultureInfo.InvariantCulture),
                    ["totalDurationMs"] = TotalDuration.TotalMilliseconds,
                    ["allPassed"] = AllChecksPassed,
                    ["hasCriticalFailures"] = HasCriticalFailures,
                    ["checks"] = CreateChecksReport()
                };

                var json = JsonSerializer.Serialize(report, _jsonOptions);
                var reportPath = Path.Combine(localAppData, "startup_diagnostics.json");
                await File.WriteAllTextAsync(reportPath, json, cancellationToken);
            }
            // ALLOWED: empty catch - Diagnostics should not fail startup
            catch
            {
            }
        }

        private List<Dictionary<string, object>> CreateChecksReport()
        {
            var checksReport = new List<Dictionary<string, object>>();

            foreach (var check in _checks)
            {
                var checkData = new Dictionary<string, object>
                {
                    ["name"] = check.Name,
                    ["description"] = check.Description,
                    ["status"] = check.Status.ToString(),
                    ["message"] = check.Message,
                    ["isCritical"] = check.IsCritical,
                    ["durationMs"] = check.Duration.TotalMilliseconds
                };

                if (check.Details != null)
                {
                    checkData["details"] = check.Details;
                }

                checksReport.Add(checkData);
            }

            return checksReport;
        }
    }

    /// <summary>
    /// Represents a single diagnostic check result.
    /// </summary>
    public sealed class DiagnosticCheck
    {
        /// <summary>
        /// Gets or sets the check name.
        /// </summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the check description.
        /// </summary>
        public string Description { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the check status.
        /// </summary>
        public CheckStatus Status { get; set; } = CheckStatus.Pending;

        /// <summary>
        /// Gets or sets the result message.
        /// </summary>
        public string Message { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets whether this is a critical check.
        /// </summary>
        public bool IsCritical { get; set; }

        /// <summary>
        /// Gets or sets how long the check took.
        /// </summary>
        public TimeSpan Duration { get; set; }

        /// <summary>
        /// Gets or sets additional details.
        /// </summary>
        public Dictionary<string, object>? Details { get; set; }
    }

    /// <summary>
    /// Status of a diagnostic check.
    /// </summary>
    public enum CheckStatus
    {
        /// <summary>Check has not run yet.</summary>
        Pending = 0,

        /// <summary>Check is currently running.</summary>
        Running = 1,

        /// <summary>Check passed successfully.</summary>
        Passed = 2,

        /// <summary>Check passed with warnings.</summary>
        Warning = 3,

        /// <summary>Check failed.</summary>
        Failed = 4
    }
}
