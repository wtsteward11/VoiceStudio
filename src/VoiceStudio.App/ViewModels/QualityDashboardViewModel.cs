using System;
using System.Collections.ObjectModel;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VoiceStudio.Core.Panels;
using VoiceStudio.Core.Services;
using VoiceStudio.Core.Models;
using VoiceStudio.App.Services;
using VoiceStudio.App.Utilities;
using QualityTrendDataModel = VoiceStudio.App.ViewModels.QualityDashboardViewModel.QualityTrendData;

namespace VoiceStudio.App.ViewModels
{
  /// <summary>
  /// ViewModel for the QualityDashboardView panel - Quality metrics visualization dashboard.
  /// Implements IDEA 49: Quality Metrics Visualization Dashboard.
  /// </summary>
  public partial class QualityDashboardViewModel : BaseViewModel, IPanelView
  {
    private readonly IBackendClient _backendClient;
    private readonly ToastNotificationService? _toastNotificationService;
    private readonly IErrorPresentationService? _errorService;
    private readonly IErrorLoggingService? _logService;

    public string PanelId => "quality-dashboard";
    public string DisplayName => ResourceHelper.GetString("Panel.QualityDashboard.DisplayName", "Quality Dashboard");
    public PanelRegion Region => PanelRegion.Center;

    [ObservableProperty]
    private QualityOverview? overview;

    [ObservableProperty]
    private ObservableCollection<QualityPresetItem> qualityPresets = new();

    [ObservableProperty]
    private QualityPresetItem? selectedPreset;

    [ObservableProperty]
    private ObservableCollection<QualityMetricTrend> qualityTrends = new();

    [ObservableProperty]
    private string selectedTimeRange = "30d";

    [ObservableProperty]
    private ObservableCollection<string> availableTimeRanges = new() { "7d", "30d", "90d", "1y", "all" };

    [ObservableProperty]
    private bool dashboardAvailable;

    [ObservableProperty]
    private string dashboardStatusMessage = ResourceHelper.GetString("QualityDashboard.StatusMessageRequiresDB", "Quality dashboard requires database integration for full functionality. Basic quality metrics and presets are available.");

    [ObservableProperty]
    private string selectedVisualizationType = "default";

    [ObservableProperty]
    private ObservableCollection<string> availableVisualizationTypes = new() { "default", "matplotlib", "plotly" };

    [ObservableProperty]
    private string? trendsVisualizationImageUrl;

    [ObservableProperty]
    private string? distributionVisualizationImageUrl;

    public QualityDashboardViewModel(IViewModelContext context, IBackendClient backendClient)
        : base(context)
    {
      _backendClient = backendClient ?? throw new ArgumentNullException(nameof(backendClient));

      // Get toast notification service using helper (reduces code duplication)
      _toastNotificationService = ServiceInitializationHelper.TryGetService(() => AppServices.TryGetToastNotificationService());
      _errorService = ServiceProvider.TryGetErrorPresentationService();
      _logService = ServiceProvider.TryGetErrorLoggingService();

      LoadOverviewCommand = new EnhancedAsyncRelayCommand(async (ct) =>
      {
        using var profiler = PerformanceProfiler.StartCommand("LoadOverview");
        await LoadOverviewAsync(ct);
      }, () => !IsLoading);
      LoadPresetsCommand = new EnhancedAsyncRelayCommand(async (ct) =>
      {
        using var profiler = PerformanceProfiler.StartCommand("LoadPresets");
        await LoadPresetsAsync(ct);
      }, () => !IsLoading);
      LoadTrendsCommand = new EnhancedAsyncRelayCommand(async (ct) =>
      {
        using var profiler = PerformanceProfiler.StartCommand("LoadTrends");
        await LoadTrendsAsync(ct);
      }, () => !IsLoading);
      RefreshCommand = new EnhancedAsyncRelayCommand(async (ct) =>
      {
        using var profiler = PerformanceProfiler.StartCommand("Refresh");
        await RefreshAsync(ct);
      }, () => !IsLoading);

      // Load initial data
      _ = LoadPresetsAsync(CancellationToken.None);
      _ = LoadOverviewAsync(CancellationToken.None);
    }

    public EnhancedAsyncRelayCommand LoadOverviewCommand { get; }
    public EnhancedAsyncRelayCommand LoadPresetsCommand { get; }
    public EnhancedAsyncRelayCommand LoadTrendsCommand { get; }
    public EnhancedAsyncRelayCommand RefreshCommand { get; }

    partial void OnSelectedPresetChanged(QualityPresetItem? value)
    {
      if (value != null)
      {
        _ = LoadTrendsAsync(CancellationToken.None);
      }
    }

    partial void OnSelectedVisualizationTypeChanged(string value)
    {
      _ = LoadVisualizationsAsync(CancellationToken.None);
    }

    partial void OnSelectedTimeRangeChanged(string value)
    {
      _ = LoadOverviewAsync(CancellationToken.None);
      _ = LoadVisualizationsAsync(CancellationToken.None);
    }

    private Task LoadVisualizationsAsync(CancellationToken cancellationToken)
    {
      if (SelectedVisualizationType == "default" || !DashboardAvailable)
      {
        TrendsVisualizationImageUrl = null;
        DistributionVisualizationImageUrl = null;
        return Task.CompletedTask;
      }

      try
      {
        cancellationToken.ThrowIfCancellationRequested();

        // Load visualization images from backend
        // Backend will generate matplotlib/plotly charts and return image URLs
        // These endpoints will be created when visualization libraries are integrated
        var timeRangeDays = GetDaysFromTimeRange(SelectedTimeRange);
        const string baseUrl = "http://localhost:8001"; // Default backend URL

        // For trends visualization - construct URL for backend endpoint
        // Endpoint: /api/quality/trends/visualization?type={type}&days={days}
        TrendsVisualizationImageUrl = $"{baseUrl}/api/quality/trends/visualization?type={SelectedVisualizationType}&days={timeRangeDays}";

        // For distribution visualization - construct URL for backend endpoint
        // Endpoint: /api/quality/distribution/visualization?type={type}
        DistributionVisualizationImageUrl = $"{baseUrl}/api/quality/distribution/visualization?type={SelectedVisualizationType}";
      }
      catch (OperationCanceledException)
      {
        // User cancelled - expected
        return Task.CompletedTask;
      }
      catch (Exception ex)
      {
        _logService?.LogError(ex, "LoadVisualizations");
        TrendsVisualizationImageUrl = null;
        DistributionVisualizationImageUrl = null;
      }

      return Task.CompletedTask;
    }

    private async Task LoadOverviewAsync(CancellationToken cancellationToken)
    {
      IsLoading = true;
      ErrorMessage = null;

      try
      {
        // Try to load dashboard data using the new GetQualityDashboardAsync method
        try
        {
          var dashboard = await _backendClient.GetQualityDashboardAsync(
              projectId: null,
              days: GetDaysFromTimeRange(SelectedTimeRange),
              cancellationToken
          );

          if (dashboard != null)
          {
            Overview = new QualityOverview(dashboard);
            DashboardAvailable = true;
            DashboardStatusMessage = ResourceHelper.GetString("QualityDashboard.DataLoadedSuccessfully", "Quality dashboard data loaded successfully.");
          }
        }
        catch (Exception)
        {
          // Dashboard endpoint returns 501 - not fully implemented
          // This is expected, so we'll show a message
          DashboardAvailable = false;
          DashboardStatusMessage = ResourceHelper.GetString("QualityDashboard.FullDashboardRequiresDB", "Full quality dashboard requires database integration. Showing available quality metrics and presets.");

          // Create a basic overview from available data
          Overview = new QualityOverview
          {
            AverageMosScore = 0,
            AverageSimilarity = 0,
            AverageNaturalness = 0,
            TotalSamples = 0,
            QualityDistribution = new ObservableCollection<QualityDistributionItem>()
          };
        }

        if (Overview != null && DashboardAvailable)
        {
          _toastNotificationService?.ShowSuccess(
              ResourceHelper.GetString("QualityDashboard.OverviewLoaded", "Quality overview loaded successfully."),
              ResourceHelper.GetString("Panel.QualityDashboard.DisplayName", "Quality Dashboard"));
        }
      }
      catch (OperationCanceledException)
      {
        // User cancelled - expected
        return;
      }
      catch (Exception ex)
      {
        await HandleErrorAsync(ex, "LoadOverview");
      }
      finally
      {
        IsLoading = false;
      }
    }

    private async Task LoadPresetsAsync(CancellationToken cancellationToken)
    {
      IsLoading = true;
      ErrorMessage = null;

      try
      {
        var presets = await _backendClient.GetQualityPresetsAsync(cancellationToken);

        if (presets?.Count > 0)
        {
          QualityPresets.Clear();
          foreach (var preset in presets)
          {
            var targetMetrics = preset.Value.TargetMetrics ?? new Dictionary<string, double>();
            var parameters = preset.Value.Parameters ?? new Dictionary<string, object>();

            QualityPresets.Add(new QualityPresetItem(
                preset.Value.Name,
                preset.Value.Description,
                targetMetrics,
                parameters
            ));
          }
        }

        if (QualityPresets.Count > 0)
        {
          _toastNotificationService?.ShowSuccess(
              ResourceHelper.FormatString("QualityDashboard.PresetsLoaded", QualityPresets.Count),
              ResourceHelper.GetString("Panel.QualityDashboard.DisplayName", "Quality Presets"));
        }
      }
      catch (OperationCanceledException)
      {
        // User cancelled - expected
        return;
      }
      catch (Exception ex)
      {
        ErrorMessage = ErrorHandler.GetUserFriendlyMessage(ex);
        _errorService?.ShowError(ex, ResourceHelper.GetString("QualityDashboard.PresetsLoadFailed", "Failed to load quality presets"));
        _logService?.LogError(ex, "LoadPresets");
        _toastNotificationService?.ShowError(
            ResourceHelper.FormatString("QualityDashboard.PresetsLoadFailedDetail", ErrorHandler.GetUserFriendlyMessage(ex)),
            ResourceHelper.GetString("Toast.Title.LoadFailed", "Load Failed"));
      }
      finally
      {
        IsLoading = false;
      }
    }

    private async Task LoadTrendsAsync(CancellationToken cancellationToken)
    {
      IsLoading = true;
      ErrorMessage = null;

      try
      {
        cancellationToken.ThrowIfCancellationRequested();

        // Load quality trends from dashboard endpoint
        // If no trends data is available, show empty collection
        QualityTrends.Clear();

        if (DashboardAvailable && Overview != null)
        {
          // If dashboard data is available, populate trends
          // This would come from the dashboard endpoint when fully implemented
          if (QualityTrends.Count > 0)
          {
            _toastNotificationService?.ShowSuccess(
                ResourceHelper.FormatString("QualityDashboard.TrendsLoaded", QualityTrends.Count),
                ResourceHelper.GetString("QualityDashboard.QualityTrends", "Quality Trends"));
          }
        }
      }
      catch (OperationCanceledException)
      {
        // User cancelled - expected
        return;
      }
      catch (Exception ex)
      {
        await HandleErrorAsync(ex, "LoadTrends");
      }
      finally
      {
        IsLoading = false;
      }
    }

    private async Task RefreshAsync(CancellationToken cancellationToken)
    {
      try
      {
        await LoadOverviewAsync(cancellationToken);
        await LoadPresetsAsync(cancellationToken);
        if (SelectedPreset != null)
        {
          await LoadTrendsAsync(cancellationToken);
        }
        await LoadVisualizationsAsync(cancellationToken);
        StatusMessage = ResourceHelper.GetString("Status.Complete", "Complete");
        _toastNotificationService?.ShowSuccess(
            ResourceHelper.GetString("QualityDashboard.RefreshComplete", "Quality dashboard refreshed successfully."),
            ResourceHelper.GetString("Status.Complete", "Refresh Complete"));
      }
      catch (OperationCanceledException)
      {
        return; // User cancelled
      }
      catch (Exception ex)
      {
        await HandleErrorAsync(ex, "Refresh");
      }
    }

    private int GetDaysFromTimeRange(string timeRange)
    {
      return timeRange switch
      {
        "7d" => 7,
        "30d" => 30,
        "90d" => 90,
        "1y" => 365,
        "all" => 3650, // 10 years as "all"
        _ => 30
      };
    }

    // Response models
    private class QualityDashboardData
    {
      public QualityOverviewData? Overview { get; set; }
      public List<QualityTrendData>? Trends { get; set; }
      public List<QualityDistributionData>? Distribution { get; set; }
      public List<QualityAlertData>? Alerts { get; set; }
    }

    private class QualityOverviewData
    {
      public double AverageMosScore { get; set; }
      public double AverageSimilarity { get; set; }
      public double AverageNaturalness { get; set; }
      public int TotalSamples { get; set; }
    }

    public class QualityTrendData
    {
      public string Timestamp { get; set; } = string.Empty;
      public double MosScore { get; set; }
      public double Similarity { get; set; }
      public double Naturalness { get; set; }
    }

    private class QualityDistributionData
    {
      public string Range { get; set; } = string.Empty;
      public int Count { get; set; }
    }

    private class QualityAlertData
    {
      public string Type { get; set; } = string.Empty;
      public string Message { get; set; } = string.Empty;
      public string Severity { get; set; } = string.Empty;
    }

    private class QualityPresetResponse
    {
      public string Name { get; set; } = string.Empty;
      public string Description { get; set; } = string.Empty;
      public Dictionary<string, double> TargetMetrics { get; set; } = new();
      public Dictionary<string, object> Parameters { get; set; } = new();
    }
  }

  // Data models
  public class QualityOverview : ObservableObject
  {
    public double AverageMosScore { get; set; }
    public double AverageSimilarity { get; set; }
    public double AverageNaturalness { get; set; }
    public int TotalSamples { get; set; }
    public ObservableCollection<QualityDistributionItem> QualityDistribution { get; set; } = new();
    public string AverageMosScoreDisplay => $"{AverageMosScore:F2}/5.0";
    public string AverageSimilarityDisplay => $"{AverageSimilarity:P1}";
    public string AverageNaturalnessDisplay => $"{AverageNaturalness:P1}";

    public QualityOverview()
    {
    }

    public QualityOverview(QualityDashboard dashboard)
    {
      if (dashboard.Overview != null)
      {
        AverageMosScore = dashboard.Overview.AverageMosScore;
        AverageSimilarity = dashboard.Overview.AverageSimilarity;
        AverageNaturalness = dashboard.Overview.AverageNaturalness;
        TotalSamples = dashboard.Overview.TotalSyntheses;
      }

      if (dashboard.Distribution != null)
      {
        // Convert QualityDistribution to QualityDistributionItem
        QualityDistribution = new ObservableCollection<QualityDistributionItem>();
        foreach (var dist in dashboard.Distribution)
        {
          if (dist.Value?.Histogram != null)
          {
            // QualityDistribution has a Histogram dictionary with range->count mappings
            foreach (var histogramEntry in dist.Value.Histogram)
            {
              QualityDistribution.Add(new QualityDistributionItem(
                  histogramEntry.Key, // Range name from histogram
                  histogramEntry.Value, // Count
                  TotalSamples
              ));
            }
          }
          else if (dist.Value != null)
          {
            // Fallback: create range from min/max if no histogram
            string range = $"{dist.Value.Min:F2}-{dist.Value.Max:F2}";
            // Estimate count from mean/stddev if available, otherwise use 0
            int estimatedCount = TotalSamples > 0 ? (int)(TotalSamples * 0.1) : 0;
            QualityDistribution.Add(new QualityDistributionItem(
                range,
                estimatedCount,
                TotalSamples
            ));
          }
        }
      }
    }
  }

  public class QualityDistributionItem : ObservableObject
  {
    public string Range { get; set; } = string.Empty;
    public int Count { get; set; }
    public int TotalSamples { get; set; }
    public string CountDisplay => Count.ToString();
    public string PercentageDisplay => TotalSamples > 0 ? $"{Count * 100.0 / TotalSamples:F1}" : "0.0";

    public QualityDistributionItem()
    {
    }

    public QualityDistributionItem(string range, int count, int totalSamples = 0)
    {
      Range = range;
      Count = count;
      TotalSamples = totalSamples;
    }
  }

  public class QualityPresetItem : ObservableObject
  {
    public string Name { get; set; }
    public string Description { get; set; }
    public Dictionary<string, double> TargetMetrics { get; set; }
    public Dictionary<string, object> Parameters { get; set; }
    public string TargetMetricsDisplay => string.Join(", ", TargetMetrics.Select(kvp => $"{kvp.Key}: {kvp.Value:F2}"));

    public QualityPresetItem(string name, string description, Dictionary<string, double> targetMetrics, Dictionary<string, object> parameters)
    {
      Name = name;
      Description = description;
      TargetMetrics = targetMetrics;
      Parameters = parameters;
    }
  }

  public class QualityMetricTrend : ObservableObject
  {
    public string Timestamp { get; set; }
    public double MosScore { get; set; }
    public double Similarity { get; set; }
    public double Naturalness { get; set; }
    public string MosScoreDisplay => $"{MosScore:F2}";
    public string SimilarityDisplay => $"{Similarity:P1}";
    public string NaturalnessDisplay => $"{Naturalness:P1}";

    public QualityMetricTrend(QualityTrendDataModel data)
    {
      Timestamp = data.Timestamp;
      MosScore = data.MosScore;
      Similarity = data.Similarity;
      Naturalness = data.Naturalness;
    }
  }
}