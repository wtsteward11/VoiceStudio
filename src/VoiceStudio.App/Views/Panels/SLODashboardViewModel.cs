using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using VoiceStudio.Core.Models;
using VoiceStudio.Core.Panels;
using VoiceStudio.Core.Services;
using VoiceStudio.App.Utilities;
using VoiceStudio.App.ViewModels;

namespace VoiceStudio.App.Views.Panels
{
    /// <summary>
    /// ViewModel for SLO Dashboard panel.
    /// Phase 5.2.1: SLO Dashboard with gauge chart visualization.
    /// </summary>
    public partial class SLODashboardViewModel : BaseViewModel, IPanelView
    {
        private readonly ISLODashboardClient _sloClient;

        /// <inheritdoc/>
        public string PanelId => "slo_dashboard";

        /// <inheritdoc/>
        public string DisplayName => ResourceHelper.GetString(
            "Panel.SLODashboard.DisplayName",
            "SLO Dashboard");

        /// <inheritdoc/>
        public PanelRegion Region => PanelRegion.Center;

        /// <summary>Gets or sets the collection of SLO metrics.</summary>
        [ObservableProperty]
        private ObservableCollection<SloMetric> sloMetrics = new();

        /// <summary>Gets or sets whether data is loading.</summary>
        [ObservableProperty]
        private bool isLoading;

        /// <summary>Gets or sets the error message.</summary>
        [ObservableProperty]
        private string? errorMessage;

        /// <summary>Gets or sets the status message.</summary>
        [ObservableProperty]
        private string? statusMessage;

        /// <summary>Gets or sets the total SLO count.</summary>
        [ObservableProperty]
        private int totalSloCount;

        /// <summary>Gets or sets the healthy SLO count.</summary>
        [ObservableProperty]
        private int healthySloCount;

        /// <summary>Gets or sets the warning SLO count.</summary>
        [ObservableProperty]
        private int warningSloCount;

        /// <summary>Gets or sets the critical SLO count.</summary>
        [ObservableProperty]
        private int criticalSloCount;

        /// <summary>
        /// Gets visibility for the "All SLOs Healthy" badge.
        /// </summary>
        public Visibility AllSlosHealthy => CriticalSloCount == 0 && WarningSloCount == 0
            ? Visibility.Visible
            : Visibility.Collapsed;

        /// <summary>Command to refresh SLO data.</summary>
        public IAsyncRelayCommand RefreshCommand { get; }

        /// <summary>
        /// Initializes a new instance of the SLODashboardViewModel.
        /// </summary>
        /// <param name="context">The ViewModel context.</param>
        /// <param name="sloClient">The SLO dashboard client.</param>
        public SLODashboardViewModel(
            IViewModelContext context,
            ISLODashboardClient sloClient)
            : base(context)
        {
            _sloClient = sloClient
                ?? throw new ArgumentNullException(nameof(sloClient));

            RefreshCommand = new AsyncRelayCommand(LoadSloDataAsync);
        }

        /// <summary>
        /// Loads SLO data from the backend.
        /// </summary>
        public async Task LoadSloDataAsync()
        {
            if (IsLoading) return;

            IsLoading = true;
            ErrorMessage = null;

            try
            {
                var response = await _sloClient.GetSloDataAsync();

                if (response?.Slos != null)
                {
                    SloMetrics.Clear();
                    foreach (var dto in response.Slos)
                    {
                        SloMetrics.Add(SloMetric.FromDto(dto));
                    }
                }

                UpdateSummaryStats();
                StatusMessage = $"Loaded {TotalSloCount} SLOs";
            }
            catch (HttpRequestException)
            {
                // Backend unavailable - load sample data for development
                LoadSampleData();
                StatusMessage = "Using sample data (backend unavailable)";
            }
            catch (JsonException ex)
            {
                // JSON parsing failed - use sample data
                ErrorMessage = $"Failed to parse SLO data: {ex.Message}";
                LoadSampleData();
            }
            catch (InvalidOperationException ex)
            {
                // Backend returned unexpected response
                ErrorMessage = $"Invalid SLO response: {ex.Message}";
                LoadSampleData();
            }
            finally
            {
                IsLoading = false;
                OnPropertyChanged(nameof(AllSlosHealthy));
            }
        }

        private void UpdateSummaryStats()
        {
            TotalSloCount = SloMetrics.Count;
            HealthySloCount = SloMetrics.Count(s => s.Status == "Healthy");
            WarningSloCount = SloMetrics.Count(s => s.Status == "Warning");
            CriticalSloCount = SloMetrics.Count(s => s.Status == "Critical");
        }

        /// <summary>
        /// Clears SLO data and sets error state when backend is unavailable.
        /// No fake sample data is returned - the UI should display "No SLO data available".
        /// </summary>
        private void LoadSampleData()
        {
            // Do NOT add fake sample data - show empty state with error
            SloMetrics.Clear();
            
            // UI should display "SLO data unavailable. Check backend connection."
            // instead of fake metrics that could mislead users
            ErrorMessage = "SLO data unavailable. Backend connection required.";
            
            UpdateSummaryStats();
        }
    }

    /// <summary>
    /// Represents a single SLO metric with gauge visualization support.
    /// Phase 5.2.1: SLO Dashboard.
    /// </summary>
    public partial class SloMetric : ObservableObject
    {
        /// <summary>Creates a SloMetric from API DTO.</summary>
        public static SloMetric FromDto(SloMetricDto dto)
        {
            if (dto == null) throw new ArgumentNullException(nameof(dto));
            return new SloMetric
            {
                Name = dto.Name ?? string.Empty,
                CurrentValue = dto.CurrentValue,
                Target = dto.Target,
                WarningThreshold = dto.WarningThreshold,
                Unit = dto.Unit ?? string.Empty,
                MetricType = dto.MetricType ?? string.Empty
            };
        }

        /// <summary>Gets or sets the SLO name.</summary>
        [ObservableProperty]
        private string name = string.Empty;

        /// <summary>Gets or sets the current metric value.</summary>
        [ObservableProperty]
        private double currentValue;

        /// <summary>Gets or sets the target value.</summary>
        [ObservableProperty]
        private double target;

        /// <summary>Gets or sets the warning threshold.</summary>
        [ObservableProperty]
        private double warningThreshold;

        /// <summary>Gets or sets the unit of measurement.</summary>
        [ObservableProperty]
        private string unit = string.Empty;

        /// <summary>Gets or sets the metric type (latency, availability, etc.).</summary>
        [ObservableProperty]
        private string metricType = string.Empty;

        /// <summary>Gets the current value as a percentage of target.</summary>
        public double CurrentValuePercent
        {
            get
            {
                if (Target == 0) return 0;

                // For latency metrics, lower is better
                if (MetricType == "latency")
                {
                    // If under target, show as percentage toward 100%
                    return Math.Min(100, ((1 - (CurrentValue / Target)) * 100) + 50);
                }

                // For other metrics (availability, success_rate), higher is better
                return Math.Min(100, (CurrentValue / Target) * 100);
            }
        }

        /// <summary>Gets the formatted current value.</summary>
        public string CurrentValueFormatted
        {
            get
            {
                return CurrentValue switch
                {
                    < 10 => $"{CurrentValue:F2}",
                    < 100 => $"{CurrentValue:F1}",
                    _ => $"{CurrentValue:F0}"
                };
            }
        }

        /// <summary>Gets the formatted target value.</summary>
        public string TargetFormatted => $"{Target:F1} {Unit}";

        /// <summary>Gets the formatted warning threshold.</summary>
        public string WarningThresholdFormatted => $"{WarningThreshold:F1} {Unit}";

        /// <summary>Gets the current status based on thresholds.</summary>
        public string Status
        {
            get
            {
                if (MetricType == "latency")
                {
                    // For latency, lower is better
                    if (CurrentValue >= Target) return "Critical";
                    if (CurrentValue >= WarningThreshold) return "Warning";
                    return "Healthy";
                }

                // For other metrics, higher is better
                if (CurrentValue < WarningThreshold) return "Critical";
                if (CurrentValue < Target) return "Warning";
                return "Healthy";
            }
        }

        /// <summary>Gets the status color brush.</summary>
        public SolidColorBrush StatusColor
        {
            get
            {
                return Status switch
                {
                    "Healthy" => new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(255, 76, 175, 80)),
                    "Warning" => new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(255, 255, 193, 7)),
                    "Critical" => new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(255, 244, 67, 54)),
                    _ => new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(255, 158, 158, 158))
                };
            }
        }

        /// <summary>Gets the status border brush.</summary>
        public SolidColorBrush StatusBorderBrush => StatusColor;

        /// <summary>Gets the status badge background brush.</summary>
        public SolidColorBrush StatusBadgeBackground => StatusColor;
    }

}
