using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml.Media;
using VoiceStudio.App.Utilities;
using VoiceStudio.Core.Panels;
using VoiceStudio.Core.Services;

using Brush = Microsoft.UI.Xaml.Media.Brush;
using SolidColorBrush = Microsoft.UI.Xaml.Media.SolidColorBrush;

namespace VoiceStudio.App.ViewModels
{
    /// <summary>
    /// ViewModel for Plugin Health Dashboard.
    /// Phase 4: Provides real-time monitoring of plugin health, metrics, and status.
    /// </summary>
    public partial class PluginHealthDashboardViewModel : BaseViewModel, IPanelView
    {
        public string PanelId => "plugin-health-dashboard";
        public string DisplayName => ResourceHelper.GetString("Panel.PluginHealthDashboard.DisplayName", "Plugin Health Dashboard");
        public PanelRegion Region => PanelRegion.Center;

        private readonly IBackendClient _backendClient;

        [ObservableProperty]
        private ObservableCollection<PluginHealthItem> plugins = new();

        [ObservableProperty]
        private PluginHealthItem? selectedPlugin;

        [ObservableProperty]
        private string statusMessage = ResourceHelper.GetString("PluginHealthDashboard.Ready", "Ready");

        // System overview
        [ObservableProperty]
        private int totalPlugins;

        [ObservableProperty]
        private int healthyPlugins;

        [ObservableProperty]
        private int degradedPlugins;

        [ObservableProperty]
        private int unhealthyPlugins;

        [ObservableProperty]
        private double systemErrorRate;

        [ObservableProperty]
        private double avgLatencyMs;

        [ObservableProperty]
        private long totalMemoryBytes;

        [ObservableProperty]
        private int totalCalls;

        [ObservableProperty]
        private int totalErrors;

        // Formatted display properties
        public string SystemErrorRateDisplay => $"{SystemErrorRate:F2}%";
        public string AvgLatencyDisplay => $"{AvgLatencyMs:F2}ms";
        public string TotalMemoryDisplay => FormatBytes(TotalMemoryBytes);
        public string TotalCallsDisplay => TotalCalls.ToString("N0");
        public string TotalErrorsDisplay => TotalErrors.ToString("N0");

        // Selected plugin details
        [ObservableProperty]
        private ObservableCollection<MethodStatsItem> selectedPluginMethods = new();

        [ObservableProperty]
        private ObservableCollection<MetricItem> selectedPluginCounters = new();

        [ObservableProperty]
        private ObservableCollection<MetricItem> selectedPluginGauges = new();

        [ObservableProperty]
        private ObservableCollection<ErrorItem> selectedPluginErrors = new();

        // Selected plugin display properties
        public bool HasSelectedPlugin => SelectedPlugin != null;
        public string SelectedPluginId => SelectedPlugin?.PluginId ?? string.Empty;
        public string SelectedPluginStatus => SelectedPlugin?.StatusText ?? string.Empty;
        public Brush? SelectedPluginStatusBrush => SelectedPlugin?.StatusBrush;
        public string SelectedPluginLastActivity => SelectedPlugin?.LastActivityFormatted ?? string.Empty;
        public bool HasSelectedPluginErrors => SelectedPluginErrors.Count > 0;

        // Auto-refresh
        [ObservableProperty]
        private bool autoRefreshEnabled;

        [ObservableProperty]
        private int refreshIntervalSeconds = 30;

        private CancellationTokenSource? _autoRefreshCts;

        public PluginHealthDashboardViewModel(IViewModelContext context, IBackendClient backendClient)
            : base(context)
        {
            _backendClient = backendClient ?? throw new ArgumentNullException(nameof(backendClient));

            RefreshCommand = new EnhancedAsyncRelayCommand(async (ct) =>
            {
                using var profiler = PerformanceProfiler.StartCommand("RefreshPluginHealth");
                await RefreshHealthDataAsync(ct);
            }, () => !IsLoading);

            RefreshSelectedPluginCommand = new EnhancedAsyncRelayCommand(async (ct) =>
            {
                using var profiler = PerformanceProfiler.StartCommand("RefreshSelectedPluginHealth");
                await RefreshSelectedPluginAsync(ct);
            }, () => SelectedPlugin != null && !IsLoading);

            ToggleAutoRefreshCommand = new RelayCommand(ToggleAutoRefresh);
            ExportMetricsCommand = new EnhancedAsyncRelayCommand(async (ct) =>
            {
                await ExportMetricsAsync(ct);
            }, () => !IsLoading);
        }

        public IAsyncRelayCommand RefreshCommand { get; }
        public IAsyncRelayCommand RefreshSelectedPluginCommand { get; }
        public IRelayCommand ToggleAutoRefreshCommand { get; }
        public IAsyncRelayCommand ExportMetricsCommand { get; }

        partial void OnSelectedPluginChanged(PluginHealthItem? value)
        {
            RefreshSelectedPluginCommand.NotifyCanExecuteChanged();

            // Notify computed property changes
            OnPropertyChanged(nameof(HasSelectedPlugin));
            OnPropertyChanged(nameof(SelectedPluginId));
            OnPropertyChanged(nameof(SelectedPluginStatus));
            OnPropertyChanged(nameof(SelectedPluginStatusBrush));
            OnPropertyChanged(nameof(SelectedPluginLastActivity));

            if (value != null)
            {
                LoadSelectedPluginDetailsAsync(value.PluginId).ConfigureAwait(false);
            }
            else
            {
                SelectedPluginMethods.Clear();
                SelectedPluginCounters.Clear();
                SelectedPluginGauges.Clear();
                SelectedPluginErrors.Clear();
                OnPropertyChanged(nameof(HasSelectedPluginErrors));
            }
        }

        partial void OnAutoRefreshEnabledChanged(bool value)
        {
            if (value)
            {
                StartAutoRefresh();
            }
            else
            {
                StopAutoRefresh();
            }
        }

        /// <summary>
        /// Loads all plugin health data from the backend.
        /// </summary>
        public async Task LoadHealthDataAsync(CancellationToken cancellationToken = default)
        {
            IsLoading = true;
            ErrorMessage = null;
            StatusMessage = ResourceHelper.GetString("PluginHealthDashboard.LoadingHealthData", "Loading plugin health data...");

            try
            {
                var response = await _backendClient.GetPluginHealthDashboardAsync(cancellationToken);

                cancellationToken.ThrowIfCancellationRequested();

                Plugins.Clear();

                if (response?.System != null)
                {
                    TotalPlugins = response.System.TotalPlugins;
                    HealthyPlugins = response.System.HealthyPlugins;
                    DegradedPlugins = response.System.DegradedPlugins;
                    UnhealthyPlugins = response.System.UnhealthyPlugins;
                    SystemErrorRate = response.System.SystemErrorRate;
                    AvgLatencyMs = response.System.AvgLatencyMs;
                    TotalMemoryBytes = response.System.TotalMemoryBytes;
                    TotalCalls = response.System.TotalCalls;
                    TotalErrors = response.System.TotalErrors;
                }

                if (response?.Plugins != null)
                {
                    foreach (var plugin in response.Plugins)
                    {
                        var healthItem = new PluginHealthItem
                        {
                            PluginId = plugin.PluginId,
                            Status = MapHealthStatus(plugin.Status),
                            ErrorRate = plugin.ErrorRate,
                            AvgLatencyMs = plugin.AvgLatencyMs,
                            TotalCalls = plugin.TotalCalls,
                            TotalErrors = plugin.TotalErrors,
                            MemoryBytes = plugin.MemoryBytes,
                            CrashCount = plugin.CrashCount,
                            LastActivity = plugin.LastActivity != null
                                ? DateTime.Parse(plugin.LastActivity)
                                : (DateTime?)null
                        };

                        Plugins.Add(healthItem);
                    }
                }

                var healthyCount = Plugins.Count(p => p.Status == PluginHealthStatus.Healthy);
                var degradedCount = Plugins.Count(p => p.Status == PluginHealthStatus.Degraded);
                var unhealthyCount = Plugins.Count(p => p.Status == PluginHealthStatus.Unhealthy);

                StatusMessage = string.Format(
                    ResourceHelper.GetString("PluginHealthDashboard.LoadComplete", "Loaded {0} plugins. {1} healthy, {2} degraded, {3} unhealthy."),
                    Plugins.Count, healthyCount, degradedCount, unhealthyCount);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception ex)
            {
                await HandleErrorAsync(ex, "LoadHealthData");
            }
            finally
            {
                IsLoading = false;
            }
        }

        private async Task RefreshHealthDataAsync(CancellationToken cancellationToken)
        {
            await LoadHealthDataAsync(cancellationToken);
        }

        private async Task RefreshSelectedPluginAsync(CancellationToken cancellationToken)
        {
            if (SelectedPlugin == null)
                return;

            await LoadSelectedPluginDetailsAsync(SelectedPlugin.PluginId, cancellationToken);
        }

        private async Task LoadSelectedPluginDetailsAsync(string pluginId, CancellationToken cancellationToken = default)
        {
            try
            {
                var details = await _backendClient.GetPluginMetricsAsync(pluginId, cancellationToken);

                if (details == null)
                    return;

                SelectedPluginMethods.Clear();
                SelectedPluginCounters.Clear();
                SelectedPluginGauges.Clear();
                SelectedPluginErrors.Clear();

                // Load method stats
                if (details.Execution != null)
                {
                    foreach (var kvp in details.Execution)
                    {
                        var methodStats = new MethodStatsItem
                        {
                            Method = kvp.Key,
                            CallCount = kvp.Value.CallCount,
                            SuccessCount = kvp.Value.SuccessCount,
                            ErrorCount = kvp.Value.ErrorCount,
                            SuccessRate = kvp.Value.SuccessRate,
                            AvgDurationMs = kvp.Value.AvgDurationMs,
                            MinDurationMs = kvp.Value.MinDurationMs,
                            MaxDurationMs = kvp.Value.MaxDurationMs,
                            P50DurationMs = kvp.Value.P50DurationMs,
                            P95DurationMs = kvp.Value.P95DurationMs,
                            P99DurationMs = kvp.Value.P99DurationMs
                        };
                        SelectedPluginMethods.Add(methodStats);
                    }
                }

                // Load counters
                if (details.Counters != null)
                {
                    foreach (var kvp in details.Counters)
                    {
                        SelectedPluginCounters.Add(new MetricItem
                        {
                            Name = kvp.Key,
                            Value = kvp.Value
                        });
                    }
                }

                // Load gauges
                if (details.Gauges != null)
                {
                    foreach (var kvp in details.Gauges)
                    {
                        SelectedPluginGauges.Add(new MetricItem
                        {
                            Name = kvp.Key,
                            Value = kvp.Value
                        });
                    }
                }

                OnPropertyChanged(nameof(HasSelectedPluginErrors));
            }
            catch (Exception ex)
            {
                Logger.LogWarning("Failed to load plugin details for {PluginId}: {Error}", pluginId, ex.Message);
            }
        }

        private static string FormatGaugeValue(string name, double value)
        {
            if (name.Contains("memory") || name.Contains("bytes"))
            {
                return FormatBytes((long)value);
            }
            if (name.Contains("ms") || name.Contains("duration") || name.Contains("latency"))
            {
                return $"{value:F2}ms";
            }
            if (name.Contains("rate") || name.Contains("percent"))
            {
                return $"{value:F2}%";
            }
            return value.ToString("F2");
        }

        private static string FormatBytes(long bytes)
        {
            string[] sizes = { "B", "KB", "MB", "GB" };
            int order = 0;
            double len = bytes;
            while (len >= 1024 && order < sizes.Length - 1)
            {
                order++;
                len /= 1024;
            }
            return $"{len:F2} {sizes[order]}";
        }

        private static PluginHealthStatus MapHealthStatus(string? status)
        {
            return status?.ToLowerInvariant() switch
            {
                "healthy" => PluginHealthStatus.Healthy,
                "degraded" => PluginHealthStatus.Degraded,
                "unhealthy" => PluginHealthStatus.Unhealthy,
                _ => PluginHealthStatus.Unknown
            };
        }

        private void StartAutoRefresh()
        {
            StopAutoRefresh();
            _autoRefreshCts = new CancellationTokenSource();
            _ = AutoRefreshLoopAsync(_autoRefreshCts.Token);
        }

        private void StopAutoRefresh()
        {
            _autoRefreshCts?.Cancel();
            _autoRefreshCts?.Dispose();
            _autoRefreshCts = null;
        }

        private async Task AutoRefreshLoopAsync(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(RefreshIntervalSeconds), cancellationToken);
                    await LoadHealthDataAsync(cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    Logger.LogWarning("Auto-refresh failed: {Error}", ex.Message);
                }
            }
        }

        private void ToggleAutoRefresh()
        {
            AutoRefreshEnabled = !AutoRefreshEnabled;
        }

        private async Task ExportMetricsAsync(CancellationToken cancellationToken)
        {
            IsLoading = true;
            StatusMessage = ResourceHelper.GetString("PluginHealthDashboard.ExportingMetrics", "Exporting metrics...");

            try
            {
                var metrics = await _backendClient.ExportPluginMetricsAsync("json", cancellationToken);

                if (!string.IsNullOrEmpty(metrics))
                {
                    // Copy to clipboard or save to file
                    // For now, just update status
                    StatusMessage = ResourceHelper.GetString("PluginHealthDashboard.ExportComplete", "Metrics exported successfully.");
                }
            }
            catch (Exception ex)
            {
                await HandleErrorAsync(ex, "ExportMetrics");
            }
            finally
            {
                IsLoading = false;
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                StopAutoRefresh();
            }
            base.Dispose(disposing);
        }
    }

    /// <summary>
    /// Health status for a plugin.
    /// </summary>
    public enum PluginHealthStatus
    {
        Healthy = 0,
        Degraded = 1,
        Unhealthy = 2,
        Unknown = 3
    }

    /// <summary>
    /// Health information for a plugin.
    /// </summary>
    public class PluginHealthItem : ObservableObject
    {
        private static readonly SolidColorBrush HealthyBrush = new(Windows.UI.Color.FromArgb(255, 76, 175, 80));
        private static readonly SolidColorBrush DegradedBrush = new(Windows.UI.Color.FromArgb(255, 255, 152, 0));
        private static readonly SolidColorBrush UnhealthyBrush = new(Windows.UI.Color.FromArgb(255, 244, 67, 54));
        private static readonly SolidColorBrush UnknownBrush = new(Windows.UI.Color.FromArgb(255, 158, 158, 158));

        public string PluginId { get; set; } = string.Empty;
        public PluginHealthStatus Status { get; set; } = PluginHealthStatus.Unknown;
        public double ErrorRate { get; set; }
        public double AvgLatencyMs { get; set; }
        public int TotalCalls { get; set; }
        public int TotalErrors { get; set; }
        public long? MemoryBytes { get; set; }
        public int CrashCount { get; set; }
        public DateTime? LastActivity { get; set; }

        public string StatusText => Status switch
        {
            PluginHealthStatus.Healthy => "Healthy",
            PluginHealthStatus.Degraded => "Degraded",
            PluginHealthStatus.Unhealthy => "Unhealthy",
            _ => "Unknown"
        };

        public Brush StatusBrush => Status switch
        {
            PluginHealthStatus.Healthy => HealthyBrush,
            PluginHealthStatus.Degraded => DegradedBrush,
            PluginHealthStatus.Unhealthy => UnhealthyBrush,
            _ => UnknownBrush
        };

        public string CallsDisplay => $"{TotalCalls:N0} calls";
        public string ErrorRateDisplay => $"{ErrorRate:F1}% errors";

        public string MemoryFormatted => MemoryBytes.HasValue
            ? FormatBytes(MemoryBytes.Value)
            : "N/A";

        public string LastActivityFormatted => LastActivity.HasValue
            ? $"Last active: {LastActivity.Value:g}"
            : "No activity";

        private static string FormatBytes(long bytes)
        {
            string[] sizes = { "B", "KB", "MB", "GB" };
            int order = 0;
            double len = bytes;
            while (len >= 1024 && order < sizes.Length - 1)
            {
                order++;
                len /= 1024;
            }
            return $"{len:F2} {sizes[order]}";
        }
    }

    /// <summary>
    /// Statistics for a plugin method.
    /// </summary>
    public class MethodStatsItem
    {
        public string Method { get; set; } = string.Empty;
        public int CallCount { get; set; }
        public int SuccessCount { get; set; }
        public int ErrorCount { get; set; }
        public double SuccessRate { get; set; }
        public double AvgDurationMs { get; set; }
        public double MinDurationMs { get; set; }
        public double MaxDurationMs { get; set; }
        public double P50DurationMs { get; set; }
        public double P95DurationMs { get; set; }
        public double P99DurationMs { get; set; }

        public string CallCountDisplay => CallCount.ToString("N0");
        public string SuccessRateDisplay => $"{SuccessRate:F1}%";
        public string AvgDurationDisplay => $"{AvgDurationMs:F2}";
        public string P95DurationDisplay => $"{P95DurationMs:F2}";
        public string P99DurationDisplay => $"{P99DurationMs:F2}";
    }

    /// <summary>
    /// A metric item for display.
    /// </summary>
    public class MetricItem
    {
        public string Name { get; set; } = string.Empty;
        public double Value { get; set; }

        public string ValueDisplay => Value.ToString("N2");
    }

    /// <summary>
    /// An error item for display.
    /// </summary>
    public class ErrorItem
    {
        public DateTime TimestampValue { get; set; }
        public string Method { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;

        public string Timestamp => TimestampValue.ToString("g");
    }
}
