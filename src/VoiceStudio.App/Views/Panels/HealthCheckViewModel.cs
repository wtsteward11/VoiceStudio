// =============================================================================
// HealthCheckViewModel.cs — Phase 5.3.3
// ViewModel for health check aggregation panel.
// =============================================================================

using System;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI;
using Microsoft.UI.Xaml.Media;
using VoiceStudio.Core.Panels;

namespace VoiceStudio.App.Views.Panels
{
    /// <summary>
    /// ViewModel for aggregating health check data from multiple endpoints.
    /// </summary>
    public partial class HealthCheckViewModel : ObservableObject, IPanelView
    {
        private readonly HttpClient _httpClient;
        private readonly string _backendBaseUrl;

        /// <inheritdoc/>
        public string PanelId => "health-check";

        /// <inheritdoc/>
        public string DisplayName => "Health Check";

        /// <inheritdoc/>
        public PanelRegion Region => PanelRegion.Right;

        /// <summary>
        /// Collection of health check results.
        /// </summary>
        [ObservableProperty]
        private ObservableCollection<HealthCheckItem> healthChecks = new();

        /// <summary>
        /// Whether data is currently loading.
        /// </summary>
        [ObservableProperty]
        private bool isLoading;

        /// <summary>
        /// Overall status text.
        /// </summary>
        [ObservableProperty]
        private string overallStatusText = "Unknown";

        /// <summary>
        /// Overall status color brush.
        /// </summary>
        [ObservableProperty]
        private SolidColorBrush overallStatusColor = new(Microsoft.UI.Colors.Gray);

        /// <summary>
        /// Text showing last check time.
        /// </summary>
        [ObservableProperty]
        private string lastCheckTimeText = string.Empty;

        /// <summary>
        /// Count of healthy components.
        /// </summary>
        [ObservableProperty]
        private int healthyCount;

        /// <summary>
        /// Count of components with warnings.
        /// </summary>
        [ObservableProperty]
        private int warningCount;

        /// <summary>
        /// Count of critical components.
        /// </summary>
        [ObservableProperty]
        private int criticalCount;

        /// <summary>
        /// Count of components with unknown status.
        /// </summary>
        [ObservableProperty]
        private int unknownCount;

        /// <summary>
        /// Refresh command.
        /// </summary>
        public IRelayCommand RefreshCommand { get; }

        /// <summary>
        /// Initializes a new instance of the <see cref="HealthCheckViewModel"/> class.
        /// </summary>
        public HealthCheckViewModel()
        {
            _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
            _backendBaseUrl = Environment.GetEnvironmentVariable("VOICESTUDIO_BACKEND_URL")
                ?? "http://localhost:8001";

            RefreshCommand = new AsyncRelayCommand(LoadHealthChecksAsync);
        }

        /// <summary>
        /// Loads health check data from all endpoints.
        /// </summary>
        public async Task LoadHealthChecksAsync()
        {
            IsLoading = true;

            try
            {
                HealthChecks.Clear();

                // Check Backend API
                await CheckEndpointAsync(
                    "Backend API",
                    "Core REST API for voice synthesis and management",
                    $"{_backendBaseUrl}/health");

                // Check Metrics endpoint
                await CheckEndpointAsync(
                    "Metrics Service",
                    "Prometheus-compatible metrics endpoint",
                    $"{_backendBaseUrl}/metrics/health");

                // Check WebSocket endpoint
                await CheckEndpointAsync(
                    "WebSocket Service",
                    "Real-time event streaming",
                    $"{_backendBaseUrl}/ws/health");

                // Check Engine Service
                await CheckEndpointAsync(
                    "Engine Service",
                    "Voice synthesis engine orchestration",
                    $"{_backendBaseUrl}/api/v1/engines/health");

                // Check SLO Monitor
                await CheckEndpointAsync(
                    "SLO Monitor",
                    "Service level objective monitoring",
                    $"{_backendBaseUrl}/api/v1/diagnostics/slo/health");

                // Check Trace Store
                await CheckEndpointAsync(
                    "Trace Store",
                    "Distributed tracing storage",
                    $"{_backendBaseUrl}/api/v1/diagnostics/traces/health");

                UpdateSummary();
                LastCheckTimeText = $"Last updated: {DateTime.Now:HH:mm:ss}";
            }
            catch (HttpRequestException)
            {
                // Backend completely unreachable
                HealthChecks.Add(new HealthCheckItem
                {
                    Name = "Backend API",
                    Description = "Core REST API",
                    Status = HealthStatus.Critical,
                    StatusMessage = "Backend is unreachable"
                });
                UpdateSummary();
            }
            finally
            {
                IsLoading = false;
            }
        }

        private async Task CheckEndpointAsync(
            string name,
            string description,
            string url)
        {
            var item = new HealthCheckItem
            {
                Name = name,
                Description = description,
                LastCheck = DateTime.Now
            };

            var startTime = DateTime.Now;

            try
            {
                using var response = await _httpClient.GetAsync(url);
                item.ResponseTime = DateTime.Now - startTime;

                if (response.IsSuccessStatusCode)
                {
                    var content = await response.Content.ReadAsStringAsync();

                    // Try to parse health response
                    try
                    {
                        using var doc = JsonDocument.Parse(content);
                        var root = doc.RootElement;

                        if (root.TryGetProperty("status", out var statusProp))
                        {
                            var status = statusProp.GetString()?.ToUpperInvariant();
                            item.Status = status switch
                            {
                                "HEALTHY" or "OK" or "UP" => HealthStatus.Healthy,
                                "DEGRADED" or "WARNING" => HealthStatus.Warning,
                                "UNHEALTHY" or "DOWN" or "CRITICAL" => HealthStatus.Critical,
                                _ => HealthStatus.Unknown
                            };
                        }
                        else
                        {
                            item.Status = HealthStatus.Healthy;
                        }

                        if (root.TryGetProperty("message", out var msgProp))
                        {
                            item.StatusMessage = msgProp.GetString() ?? string.Empty;
                        }
                    }
                    catch (JsonException)
                    {
                        // Simple OK response without JSON body
                        item.Status = HealthStatus.Healthy;
                    }
                }
                else
                {
                    item.Status = HealthStatus.Critical;
                    item.StatusMessage = $"HTTP {(int)response.StatusCode}";
                }
            }
            catch (TaskCanceledException)
            {
                item.Status = HealthStatus.Critical;
                item.StatusMessage = "Request timed out";
                item.ResponseTime = TimeSpan.FromSeconds(10);
            }
            catch (HttpRequestException ex)
            {
                item.Status = HealthStatus.Critical;
                item.StatusMessage = ex.Message;
            }

            HealthChecks.Add(item);
        }

        private void UpdateSummary()
        {
            HealthyCount = 0;
            WarningCount = 0;
            CriticalCount = 0;
            UnknownCount = 0;

            foreach (var check in HealthChecks)
            {
                switch (check.Status)
                {
                    case HealthStatus.Healthy:
                        HealthyCount++;
                        break;
                    case HealthStatus.Warning:
                        WarningCount++;
                        break;
                    case HealthStatus.Critical:
                        CriticalCount++;
                        break;
                    default:
                        UnknownCount++;
                        break;
                }
            }

            // Determine overall status
            if (CriticalCount > 0)
            {
                OverallStatusText = "Critical";
                OverallStatusColor = new SolidColorBrush(
                    Windows.UI.Color.FromArgb(255, 239, 68, 68));
            }
            else if (WarningCount > 0)
            {
                OverallStatusText = "Degraded";
                OverallStatusColor = new SolidColorBrush(
                    Windows.UI.Color.FromArgb(255, 245, 158, 11));
            }
            else if (HealthyCount > 0)
            {
                OverallStatusText = "Healthy";
                OverallStatusColor = new SolidColorBrush(
                    Windows.UI.Color.FromArgb(255, 16, 185, 129));
            }
            else
            {
                OverallStatusText = "Unknown";
                OverallStatusColor = new SolidColorBrush(Microsoft.UI.Colors.Gray);
            }
        }
    }

    /// <summary>
    /// Represents a single health check result.
    /// </summary>
    public class HealthCheckItem : ObservableObject
    {
        /// <summary>
        /// Gets or sets the component name.
        /// </summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the component description.
        /// </summary>
        public string Description { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the health status.
        /// </summary>
        public HealthStatus Status { get; set; } = HealthStatus.Unknown;

        /// <summary>
        /// Gets or sets the status message.
        /// </summary>
        public string StatusMessage { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the response time.
        /// </summary>
        public TimeSpan ResponseTime { get; set; }

        /// <summary>
        /// Gets or sets the last check time.
        /// </summary>
        public DateTime LastCheck { get; set; } = DateTime.Now;

        /// <summary>
        /// Gets the status color brush.
        /// </summary>
        public SolidColorBrush StatusColor => Status switch
        {
            HealthStatus.Healthy => new SolidColorBrush(
                Windows.UI.Color.FromArgb(255, 16, 185, 129)),
            HealthStatus.Warning => new SolidColorBrush(
                Windows.UI.Color.FromArgb(255, 245, 158, 11)),
            HealthStatus.Critical => new SolidColorBrush(
                Windows.UI.Color.FromArgb(255, 239, 68, 68)),
            _ => new SolidColorBrush(
                Windows.UI.Color.FromArgb(255, 107, 114, 128))
        };

        /// <summary>
        /// Gets the status message color.
        /// </summary>
        public SolidColorBrush StatusMessageColor => Status switch
        {
            HealthStatus.Healthy => new SolidColorBrush(
                Windows.UI.Color.FromArgb(255, 16, 185, 129)),
            HealthStatus.Warning => new SolidColorBrush(
                Windows.UI.Color.FromArgb(255, 245, 158, 11)),
            HealthStatus.Critical => new SolidColorBrush(
                Windows.UI.Color.FromArgb(255, 239, 68, 68)),
            _ => new SolidColorBrush(Microsoft.UI.Colors.Gray)
        };

        /// <summary>
        /// Gets whether there is a status message.
        /// </summary>
        public Microsoft.UI.Xaml.Visibility HasStatusMessage =>
            string.IsNullOrEmpty(StatusMessage)
                ? Microsoft.UI.Xaml.Visibility.Collapsed
                : Microsoft.UI.Xaml.Visibility.Visible;

        /// <summary>
        /// Gets the formatted response time.
        /// </summary>
        public string ResponseTimeText =>
            ResponseTime.TotalMilliseconds < 1000
                ? $"{ResponseTime.TotalMilliseconds:F0} ms"
                : $"{ResponseTime.TotalSeconds:F1} s";

        /// <summary>
        /// Gets the formatted last check time.
        /// </summary>
        public string LastCheckText =>
            LastCheck.ToString("HH:mm:ss", CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Health status enumeration.
    /// </summary>
    public enum HealthStatus
    {
        /// <summary>Component is healthy.</summary>
        Healthy = 0,

        /// <summary>Component has warnings.</summary>
        Warning = 1,

        /// <summary>Component is critical or down.</summary>
        Critical = 2,

        /// <summary>Component status is unknown.</summary>
        Unknown = 3
    }
}
