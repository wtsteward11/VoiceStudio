using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VoiceStudio.Core.Models;
using VoiceStudio.Core.Panels;
using VoiceStudio.Core.Services;
using VoiceStudio.App.Utilities;
using VoiceStudio.App.Services;

namespace VoiceStudio.App.ViewModels
{
  /// <summary>
  /// ViewModel for the QualityControlView panel - Quality management dashboard.
  /// </summary>
  public partial class QualityControlViewModel : BaseViewModel, IPanelView
  {
    private readonly IBackendClient _backendClient;

    public string PanelId => "quality_control";
    public string DisplayName => ResourceHelper.GetString("Panel.QualityControl.DisplayName", "Quality Control");
    public PanelRegion Region => PanelRegion.Right;

    [ObservableProperty]
    private ObservableCollection<QualityPresetInfo> presets = new();

    [ObservableProperty]
    private QualityPresetInfo? selectedPreset;

    // Explicit properties to avoid source generator accessibility issues with types from Core.Models
    private QualityAnalysisResponse? _currentAnalysis;
    public QualityAnalysisResponse? CurrentAnalysis
    {
      get => _currentAnalysis;
      set => SetProperty(ref _currentAnalysis, value);
    }

    private QualityOptimizationResponse? _currentOptimization;
    public QualityOptimizationResponse? CurrentOptimization
    {
      get => _currentOptimization;
      set => SetProperty(ref _currentOptimization, value);
    }

    [ObservableProperty]
    private QualityComparisonResponse? currentComparison;

    [ObservableProperty]
    private EngineRecommendationResponse? currentRecommendation;

    [ObservableProperty]
    private double mosScore = double.NaN;

    [ObservableProperty]
    private double similarity = double.NaN;

    [ObservableProperty]
    private double naturalness = double.NaN;

    [ObservableProperty]
    private double snrDb = double.NaN;

    [ObservableProperty]
    private string targetTier = "standard";

    [ObservableProperty]
    private bool preferSpeed;

    [ObservableProperty]
    private string? selectedEngine;

    // Quality Consistency Monitoring (IDEA 59)
    [ObservableProperty]
    private ObservableCollection<QualityConsistencyReport> projectConsistencyReports = new();

    [ObservableProperty]
    private QualityConsistencyReport? selectedProjectReport;

    [ObservableProperty]
    private AllProjectsConsistencyResponse? allProjectsConsistency;

    [ObservableProperty]
    private QualityTrendsResponse? selectedProjectTrends;

    [ObservableProperty]
    private string? selectedProjectId;

    [ObservableProperty]
    private string qualityStandard = "professional";

    [ObservableProperty]
    private int consistencyTimePeriodDays = 30;

    [ObservableProperty]
    private bool isCheckingConsistency;

    // Advanced Quality Metrics Visualization (IDEA 60)
    [ObservableProperty]
    private QualityHeatmapResponse? qualityHeatmap;

    [ObservableProperty]
    private QualityCorrelationResponse? qualityCorrelations;

    [ObservableProperty]
    private QualityAnomalyResponse? qualityAnomalies;

    [ObservableProperty]
    private QualityPredictionResponse? qualityPrediction;

    [ObservableProperty]
    private QualityInsightsResponse? qualityInsights;

    [ObservableProperty]
    private string heatmapXDimension = "engine";

    [ObservableProperty]
    private string heatmapYDimension = "profile";

    [ObservableProperty]
    private string heatmapMetric = "mos_score";

    [ObservableProperty]
    private string anomalyMetric = "mos_score";

    [ObservableProperty]
    private double anomalyThresholdStd = 2.0;

    [ObservableProperty]
    private bool isGeneratingVisualizations;

    // Artifact Removal (IDEA 63)
    [ObservableProperty]
    private string? artifactRemovalAudioId;

    [ObservableProperty]
    private bool isRemovingArtifacts;

    [ObservableProperty]
    private bool hasArtifactRemovalResult;

    [ObservableProperty]
    private string? cleanedAudioId;

    [ObservableProperty]
    private int artifactsRemovedCount;

    [ObservableProperty]
    private double cleanedQualityScore;

    public QualityControlViewModel(IViewModelContext context, IBackendClient backendClient)
        : base(context)
    {
      _backendClient = backendClient ?? throw new ArgumentNullException(nameof(backendClient));

      // Get error services
      _errorService = ServiceProvider.TryGetErrorPresentationService();
      _logService = ServiceProvider.TryGetErrorLoggingService();

      LoadPresetsCommand = new EnhancedAsyncRelayCommand(async (ct) =>
      {
        using var profiler = PerformanceProfiler.StartCommand("LoadPresets");
        await LoadPresetsAsync(ct);
      });

      AnalyzeQualityCommand = new EnhancedAsyncRelayCommand(async (ct) =>
      {
        using var profiler = PerformanceProfiler.StartCommand("AnalyzeQuality");
        await AnalyzeQualityAsync(ct);
      });

      OptimizeQualityCommand = new EnhancedAsyncRelayCommand(async (ct) =>
      {
        using var profiler = PerformanceProfiler.StartCommand("OptimizeQuality");
        await OptimizeQualityAsync(ct);
      });

      GetEngineRecommendationCommand = new EnhancedAsyncRelayCommand(async (ct) =>
      {
        using var profiler = PerformanceProfiler.StartCommand("GetEngineRecommendation");
        await GetEngineRecommendationAsync(ct);
      });

      RefreshCommand = new EnhancedAsyncRelayCommand(async (ct) =>
      {
        using var profiler = PerformanceProfiler.StartCommand("Refresh");
        await RefreshAsync(ct);
      });

      // Quality Consistency Monitoring commands (IDEA 59)
      CheckProjectConsistencyCommand = new EnhancedAsyncRelayCommand(async (ct) =>
      {
        using var profiler = PerformanceProfiler.StartCommand("CheckProjectConsistency");
        await CheckProjectConsistencyAsync(ct);
      }, () => !string.IsNullOrEmpty(SelectedProjectId));

      CheckAllProjectsConsistencyCommand = new EnhancedAsyncRelayCommand(async (ct) =>
      {
        using var profiler = PerformanceProfiler.StartCommand("CheckAllProjectsConsistency");
        await CheckAllProjectsConsistencyAsync(ct);
      });

      GetProjectTrendsCommand = new EnhancedAsyncRelayCommand(async (ct) =>
      {
        using var profiler = PerformanceProfiler.StartCommand("GetProjectTrends");
        await GetProjectTrendsAsync(ct);
      }, () => !string.IsNullOrEmpty(SelectedProjectId));

      SetQualityStandardCommand = new EnhancedAsyncRelayCommand(async (ct) =>
      {
        using var profiler = PerformanceProfiler.StartCommand("SetQualityStandard");
        await SetQualityStandardAsync(ct);
      }, () => !string.IsNullOrEmpty(SelectedProjectId));

      // Advanced Quality Metrics Visualization commands (IDEA 60)
      GenerateHeatmapCommand = new EnhancedAsyncRelayCommand(async (ct) =>
      {
        using var profiler = PerformanceProfiler.StartCommand("GenerateHeatmap");
        await GenerateHeatmapAsync(ct);
      }, () => !IsGeneratingVisualizations);

      AnalyzeCorrelationsCommand = new EnhancedAsyncRelayCommand(async (ct) =>
      {
        using var profiler = PerformanceProfiler.StartCommand("AnalyzeCorrelations");
        await AnalyzeCorrelationsAsync(ct);
      }, () => !IsGeneratingVisualizations);

      DetectAnomaliesCommand = new EnhancedAsyncRelayCommand(async (ct) =>
      {
        using var profiler = PerformanceProfiler.StartCommand("DetectAnomalies");
        await DetectAnomaliesAsync(ct);
      }, () => !IsGeneratingVisualizations);

      PredictQualityCommand = new EnhancedAsyncRelayCommand(async (ct) =>
      {
        using var profiler = PerformanceProfiler.StartCommand("PredictQuality");
        await PredictQualityAsync(ct);
      }, () => !IsGeneratingVisualizations);

      GetInsightsCommand = new EnhancedAsyncRelayCommand(async (ct) =>
      {
        using var profiler = PerformanceProfiler.StartCommand("GetInsights");
        await GetInsightsAsync(ct);
      }, () => !IsGeneratingVisualizations);

      ExportReportCommand = new EnhancedAsyncRelayCommand(async (ct) =>
      {
        using var profiler = PerformanceProfiler.StartCommand("ExportReport");
        await ExportReportAsync(ct);
      });

      // Artifact Removal command (IDEA 63)
      RemoveArtifactsCommand = new EnhancedAsyncRelayCommand(async (ct) =>
      {
        using var profiler = PerformanceProfiler.StartCommand("RemoveArtifacts");
        await RemoveArtifactsAsync(ct);
      }, () => !string.IsNullOrEmpty(ArtifactRemovalAudioId) && !IsRemovingArtifacts);

      // Load initial data
      _ = LoadPresetsAsync(CancellationToken.None);
    }

    private readonly IErrorPresentationService? _errorService;
    private readonly IErrorLoggingService? _logService;

    public EnhancedAsyncRelayCommand LoadPresetsCommand { get; }
    public EnhancedAsyncRelayCommand AnalyzeQualityCommand { get; }
    public EnhancedAsyncRelayCommand OptimizeQualityCommand { get; }
    public EnhancedAsyncRelayCommand GetEngineRecommendationCommand { get; }
    public EnhancedAsyncRelayCommand RefreshCommand { get; }

    // Quality Consistency Monitoring commands (IDEA 59)
    public EnhancedAsyncRelayCommand CheckProjectConsistencyCommand { get; }
    public EnhancedAsyncRelayCommand CheckAllProjectsConsistencyCommand { get; }
    public EnhancedAsyncRelayCommand GetProjectTrendsCommand { get; }
    public EnhancedAsyncRelayCommand SetQualityStandardCommand { get; }

    // Advanced Quality Metrics Visualization commands (IDEA 60)
    public EnhancedAsyncRelayCommand GenerateHeatmapCommand { get; }
    public EnhancedAsyncRelayCommand AnalyzeCorrelationsCommand { get; }
    public EnhancedAsyncRelayCommand DetectAnomaliesCommand { get; }
    public EnhancedAsyncRelayCommand PredictQualityCommand { get; }
    public EnhancedAsyncRelayCommand GetInsightsCommand { get; }
    public EnhancedAsyncRelayCommand ExportReportCommand { get; }

    // Artifact Removal command (IDEA 63)
    public EnhancedAsyncRelayCommand RemoveArtifactsCommand { get; }

    private async Task LoadPresetsAsync(CancellationToken cancellationToken)
    {
      IsLoading = true;
      ErrorMessage = null;

      try
      {
        cancellationToken.ThrowIfCancellationRequested();

        var presetsDict = await _backendClient.GetQualityPresetsAsync();

        Presets.Clear();
        if (presetsDict != null)
        {
          foreach (var preset in presetsDict.Values)
          {
            cancellationToken.ThrowIfCancellationRequested();
            Presets.Add(preset);
          }
        }

        if (Presets.Count > 0 && SelectedPreset == null)
        {
          SelectedPreset = Presets.FirstOrDefault(p => p.Name == "standard") ?? Presets.First();
        }
      }
      catch (OperationCanceledException)
      {
        // User cancelled - expected
        return;
      }
      catch (Exception ex)
      {
        ErrorMessage = ResourceHelper.FormatString("QualityControl.LoadPresetsFailed", ex.Message);
        _errorService?.ShowError(ex, ResourceHelper.GetString("QualityControl.LoadPresetsFailed", "Failed to load quality presets"));
        _logService?.LogError(ex, "LoadPresets");
      }
      finally
      {
        IsLoading = false;
      }
    }

    private async Task AnalyzeQualityAsync(CancellationToken cancellationToken)
    {
      IsLoading = true;
      ErrorMessage = null;

      try
      {
        cancellationToken.ThrowIfCancellationRequested();

        var request = new QualityAnalysisRequest
        {
          MosScore = double.IsNaN(MosScore) ? null : MosScore,
          Similarity = double.IsNaN(Similarity) ? null : Similarity,
          Naturalness = double.IsNaN(Naturalness) ? null : Naturalness,
          SnrDb = double.IsNaN(SnrDb) ? null : SnrDb,
          TargetTier = TargetTier
        };

        CurrentAnalysis = await _backendClient.AnalyzeQualityAsync(request);
      }
      catch (OperationCanceledException)
      {
        // User cancelled - expected
        return;
      }
      catch (Exception ex)
      {
        ErrorMessage = ResourceHelper.FormatString("QualityControl.AnalyzeQualityFailed", ex.Message);
        _errorService?.ShowError(ex, ResourceHelper.GetString("QualityControl.AnalyzeQualityFailed", "Failed to analyze quality"));
        _logService?.LogError(ex, "AnalyzeQuality");
      }
      finally
      {
        IsLoading = false;
      }
    }

    private async Task OptimizeQualityAsync(CancellationToken cancellationToken)
    {
      IsLoading = true;
      ErrorMessage = null;

      try
      {
        cancellationToken.ThrowIfCancellationRequested();

        var metrics = new System.Collections.Generic.Dictionary<string, object>();
        if (!double.IsNaN(MosScore))
          metrics["mos_score"] = MosScore;
        if (!double.IsNaN(Similarity))
          metrics["similarity"] = Similarity;
        if (!double.IsNaN(Naturalness))
          metrics["naturalness"] = Naturalness;
        if (!double.IsNaN(SnrDb))
          metrics["snr_db"] = SnrDb;

        var request = new QualityOptimizationRequest
        {
          Metrics = metrics,
          CurrentParams = new System.Collections.Generic.Dictionary<string, object>(),
          TargetTier = TargetTier
        };

        CurrentOptimization = await _backendClient.OptimizeQualityAsync(request);
      }
      catch (OperationCanceledException)
      {
        // User cancelled - expected
        return;
      }
      catch (Exception ex)
      {
        ErrorMessage = ResourceHelper.FormatString("QualityControl.OptimizeQualityFailed", ex.Message);
        _errorService?.ShowError(ex, ResourceHelper.GetString("QualityControl.OptimizeQualityFailed", "Failed to optimize quality"));
        _logService?.LogError(ex, "OptimizeQuality");
      }
      finally
      {
        IsLoading = false;
      }
    }

    private async Task GetEngineRecommendationAsync(CancellationToken cancellationToken)
    {
      IsLoading = true;
      ErrorMessage = null;

      try
      {
        cancellationToken.ThrowIfCancellationRequested();

        var request = new EngineRecommendationRequest
        {
          MinMosScore = double.IsNaN(MosScore) ? null : MosScore,
          MinSimilarity = double.IsNaN(Similarity) ? null : Similarity,
          MinNaturalness = double.IsNaN(Naturalness) ? null : Naturalness,
          PreferSpeed = PreferSpeed,
          QualityTier = TargetTier
        };

        CurrentRecommendation = await _backendClient.GetEngineRecommendationAsync(request);
        if (CurrentRecommendation?.Recommendations.Any() == true)
        {
          SelectedEngine = CurrentRecommendation.Recommendations.First().RecommendedEngine;
        }
      }
      catch (OperationCanceledException)
      {
        // User cancelled - expected
        return;
      }
      catch (Exception ex)
      {
        ErrorMessage = ResourceHelper.FormatString("QualityControl.GetEngineRecommendationFailed", ex.Message);
        _errorService?.ShowError(ex, ResourceHelper.GetString("QualityControl.GetEngineRecommendationFailed", "Failed to get engine recommendation"));
        _logService?.LogError(ex, "GetEngineRecommendation");
      }
      finally
      {
        IsLoading = false;
      }
    }

    private async Task RefreshAsync(CancellationToken cancellationToken)
    {
      await LoadPresetsAsync(cancellationToken);
    }

    // Quality Consistency Monitoring methods (IDEA 59)
    private async Task CheckProjectConsistencyAsync(CancellationToken cancellationToken)
    {
      if (string.IsNullOrEmpty(SelectedProjectId))
        return;

      IsCheckingConsistency = true;
      ErrorMessage = null;

      try
      {
        cancellationToken.ThrowIfCancellationRequested();

        var report = await _backendClient.CheckProjectConsistencyAsync(
            SelectedProjectId,
            ConsistencyTimePeriodDays
        );

        SelectedProjectReport = report;

        // Update collection if needed
        var existing = ProjectConsistencyReports.FirstOrDefault(r => r.ProjectId == SelectedProjectId);
        if (existing != null)
        {
          ProjectConsistencyReports.Remove(existing);
        }
        ProjectConsistencyReports.Add(report);
      }
      catch (OperationCanceledException)
      {
        // User cancelled - expected
        return;
      }
      catch (Exception ex)
      {
        ErrorMessage = ResourceHelper.FormatString("QualityControl.CheckProjectConsistencyFailed", ex.Message);
        _errorService?.ShowError(ex, ResourceHelper.GetString("QualityControl.CheckProjectConsistencyFailed", "Failed to check project consistency"));
        _logService?.LogError(ex, "CheckProjectConsistency");
      }
      finally
      {
        IsCheckingConsistency = false;
      }
    }

    private async Task CheckAllProjectsConsistencyAsync(CancellationToken cancellationToken)
    {
      IsCheckingConsistency = true;
      ErrorMessage = null;

      try
      {
        cancellationToken.ThrowIfCancellationRequested();

        var response = await _backendClient.CheckAllProjectsConsistencyAsync(
            ConsistencyTimePeriodDays
        );

        AllProjectsConsistency = response;

        // Update collection with all project reports
        ProjectConsistencyReports.Clear();
        foreach (var projectReport in response.Projects.Values)
        {
          cancellationToken.ThrowIfCancellationRequested();
          ProjectConsistencyReports.Add(projectReport);
        }
      }
      catch (OperationCanceledException)
      {
        // User cancelled - expected
        return;
      }
      catch (Exception ex)
      {
        ErrorMessage = ResourceHelper.FormatString("QualityControl.CheckAllProjectsConsistencyFailed", ex.Message);
        _errorService?.ShowError(ex, ResourceHelper.GetString("QualityControl.CheckAllProjectsConsistencyFailed", "Failed to check all projects consistency"));
        _logService?.LogError(ex, "CheckAllProjectsConsistency");
      }
      finally
      {
        IsCheckingConsistency = false;
      }
    }

    private async Task GetProjectTrendsAsync(CancellationToken cancellationToken)
    {
      if (string.IsNullOrEmpty(SelectedProjectId))
        return;

      IsCheckingConsistency = true;
      ErrorMessage = null;

      try
      {
        cancellationToken.ThrowIfCancellationRequested();

        SelectedProjectTrends = await _backendClient.GetProjectQualityTrendsAsync(
            SelectedProjectId,
            ConsistencyTimePeriodDays
        );
      }
      catch (OperationCanceledException)
      {
        // User cancelled - expected
        return;
      }
      catch (Exception ex)
      {
        ErrorMessage = ResourceHelper.FormatString("QualityControl.GetProjectTrendsFailed", ex.Message);
        _errorService?.ShowError(ex, ResourceHelper.GetString("QualityControl.GetProjectTrendsFailed", "Failed to get project trends"));
        _logService?.LogError(ex, "GetProjectTrends");
      }
      finally
      {
        IsCheckingConsistency = false;
      }
    }

    private async Task SetQualityStandardAsync(CancellationToken cancellationToken)
    {
      if (string.IsNullOrEmpty(SelectedProjectId))
        return;

      IsLoading = true;
      ErrorMessage = null;

      try
      {
        cancellationToken.ThrowIfCancellationRequested();

        var success = await _backendClient.SetQualityStandardAsync(
            SelectedProjectId,
            QualityStandard
        );

        if (success)
        {
          // Refresh consistency report
          await CheckProjectConsistencyAsync(cancellationToken);
        }
      }
      catch (OperationCanceledException)
      {
        // User cancelled - expected
        return;
      }
      catch (Exception ex)
      {
        ErrorMessage = ResourceHelper.FormatString("QualityControl.SetQualityStandardFailed", ex.Message);
        _errorService?.ShowError(ex, ResourceHelper.GetString("QualityControl.SetQualityStandardFailed", "Failed to set quality standard"));
        _logService?.LogError(ex, "SetQualityStandard");
      }
      finally
      {
        IsLoading = false;
      }
    }

    partial void OnSelectedProjectIdChanged(string? value)
    {
      CheckProjectConsistencyCommand.NotifyCanExecuteChanged();
      GetProjectTrendsCommand.NotifyCanExecuteChanged();
      SetQualityStandardCommand.NotifyCanExecuteChanged();
    }

    // Advanced Quality Metrics Visualization methods (IDEA 60)
    private async Task GenerateHeatmapAsync(CancellationToken cancellationToken)
    {
      IsGeneratingVisualizations = true;
      ErrorMessage = null;
      GenerateHeatmapCommand.NotifyCanExecuteChanged();

      try
      {
        cancellationToken.ThrowIfCancellationRequested();

        // Get quality data from consistency monitor or use sample data
        var qualityData = await GetQualityDataForVisualizationAsync(cancellationToken);

        var request = new QualityHeatmapRequest
        {
          QualityData = qualityData,
          XDimension = HeatmapXDimension,
          YDimension = HeatmapYDimension,
          Metric = HeatmapMetric
        };

        QualityHeatmap = await _backendClient.GetQualityHeatmapAsync(request);
      }
      catch (OperationCanceledException)
      {
        // User cancelled - expected
        return;
      }
      catch (Exception ex)
      {
        ErrorMessage = ResourceHelper.FormatString("QualityControl.GenerateHeatmapFailed", ex.Message);
        _errorService?.ShowError(ex, ResourceHelper.GetString("QualityControl.GenerateHeatmapFailed", "Failed to generate heatmap"));
        _logService?.LogError(ex, "GenerateHeatmap");
      }
      finally
      {
        IsGeneratingVisualizations = false;
        GenerateHeatmapCommand.NotifyCanExecuteChanged();
      }
    }

    private async Task AnalyzeCorrelationsAsync(CancellationToken cancellationToken)
    {
      IsGeneratingVisualizations = true;
      ErrorMessage = null;
      AnalyzeCorrelationsCommand.NotifyCanExecuteChanged();

      try
      {
        cancellationToken.ThrowIfCancellationRequested();

        var qualityData = await GetQualityDataForVisualizationAsync(cancellationToken);

        QualityCorrelations = await _backendClient.GetQualityCorrelationsAsync(qualityData);
      }
      catch (OperationCanceledException)
      {
        // User cancelled - expected
        return;
      }
      catch (Exception ex)
      {
        ErrorMessage = ResourceHelper.FormatString("QualityControl.AnalyzeCorrelationsFailed", ex.Message);
        _errorService?.ShowError(ex, ResourceHelper.GetString("QualityControl.AnalyzeCorrelationsFailed", "Failed to analyze correlations"));
        _logService?.LogError(ex, "AnalyzeCorrelations");
      }
      finally
      {
        IsGeneratingVisualizations = false;
        AnalyzeCorrelationsCommand.NotifyCanExecuteChanged();
      }
    }

    private async Task DetectAnomaliesAsync(CancellationToken cancellationToken)
    {
      IsGeneratingVisualizations = true;
      ErrorMessage = null;
      DetectAnomaliesCommand.NotifyCanExecuteChanged();

      try
      {
        cancellationToken.ThrowIfCancellationRequested();

        var qualityData = await GetQualityDataForVisualizationAsync(cancellationToken);

        QualityAnomalies = await _backendClient.DetectQualityAnomaliesAsync(
            qualityData,
            AnomalyMetric,
            AnomalyThresholdStd
        );
      }
      catch (OperationCanceledException)
      {
        // User cancelled - expected
        return;
      }
      catch (Exception ex)
      {
        ErrorMessage = $"Failed to detect anomalies: {ex.Message}";
        _errorService?.ShowError(ex, "Failed to detect anomalies");
        _logService?.LogError(ex, "DetectAnomalies");
      }
      finally
      {
        IsGeneratingVisualizations = false;
        DetectAnomaliesCommand.NotifyCanExecuteChanged();
      }
    }

    private async Task PredictQualityAsync(CancellationToken cancellationToken)
    {
      IsGeneratingVisualizations = true;
      ErrorMessage = null;
      PredictQualityCommand.NotifyCanExecuteChanged();

      try
      {
        cancellationToken.ThrowIfCancellationRequested();

        var inputFactors = new Dictionary<string, object>();
        if (!string.IsNullOrEmpty(SelectedEngine))
        {
          inputFactors["engine"] = SelectedEngine;
        }
        if (!string.IsNullOrEmpty(SelectedProjectId))
        {
          inputFactors["project_id"] = SelectedProjectId;
        }

        var request = new QualityPredictionRequest
        {
          InputFactors = inputFactors
        };

        QualityPrediction = await _backendClient.PredictQualityAsync(request);
      }
      catch (OperationCanceledException)
      {
        // User cancelled - expected
        return;
      }
      catch (Exception ex)
      {
        ErrorMessage = ResourceHelper.FormatString("QualityControl.PredictQualityFailed", ex.Message);
        _errorService?.ShowError(ex, ResourceHelper.GetString("QualityControl.PredictQualityFailed", "Failed to predict quality"));
        _logService?.LogError(ex, "PredictQuality");
      }
      finally
      {
        IsGeneratingVisualizations = false;
        PredictQualityCommand.NotifyCanExecuteChanged();
      }
    }

    private async Task GetInsightsAsync(CancellationToken cancellationToken)
    {
      IsGeneratingVisualizations = true;
      ErrorMessage = null;
      GetInsightsCommand.NotifyCanExecuteChanged();

      try
      {
        cancellationToken.ThrowIfCancellationRequested();

        var qualityData = await GetQualityDataForVisualizationAsync(cancellationToken);

        QualityInsights = await _backendClient.GetQualityInsightsAsync(
            qualityData,
            ConsistencyTimePeriodDays
        );
      }
      catch (OperationCanceledException)
      {
        // User cancelled - expected
        return;
      }
      catch (Exception ex)
      {
        ErrorMessage = ResourceHelper.FormatString("QualityControl.GetInsightsFailed", ex.Message);
        _errorService?.ShowError(ex, ResourceHelper.GetString("QualityControl.GetInsightsFailed", "Failed to get insights"));
        _logService?.LogError(ex, "GetInsights");
      }
      finally
      {
        IsGeneratingVisualizations = false;
        GetInsightsCommand.NotifyCanExecuteChanged();
      }
    }

    private Task<List<Dictionary<string, object>>> GetQualityDataForVisualizationAsync(CancellationToken cancellationToken)
    {
      cancellationToken.ThrowIfCancellationRequested();

      var qualityData = new List<Dictionary<string, object>>();

      // Get quality data from consistency monitor or project consistency reports
      if (AllProjectsConsistency != null)
      {
        foreach (var projectReport in AllProjectsConsistency.Projects.Values)
        {
          if (projectReport.Statistics != null)
          {
            qualityData.Add(new Dictionary<string, object>
            {
              ["project_id"] = projectReport.ProjectId,
              ["metrics"] = projectReport.Statistics
            });
          }
        }
      }

      return Task.FromResult(qualityData);
    }

    // Artifact Removal method (IDEA 63)
    private async Task RemoveArtifactsAsync(CancellationToken cancellationToken)
    {
      if (string.IsNullOrEmpty(ArtifactRemovalAudioId))
        return;

      IsRemovingArtifacts = true;
      IsLoading = true;
      ErrorMessage = null;
      HasArtifactRemovalResult = false;
      RemoveArtifactsCommand.NotifyCanExecuteChanged();

      try
      {
        cancellationToken.ThrowIfCancellationRequested();

        var request = new ArtifactRemovalRequest
        {
          AudioId = ArtifactRemovalAudioId
        };

        var response = await _backendClient.PostAsync<ArtifactRemovalRequest, ArtifactRemovalResponse>(
            "/api/voice/remove-artifacts", request, cancellationToken);

        HasArtifactRemovalResult = response != null;

        if (response != null)
        {
          CleanedAudioId = response.CleanedAudioId;
          ArtifactsRemovedCount = response.ArtifactsRemoved;
          CleanedQualityScore = response.CleanedQualityScore;
        }
      }
      catch (OperationCanceledException)
      {
        return;
      }
      catch (Exception ex)
      {
        ErrorMessage = $"Artifact removal failed: {ex.Message}";
        _errorService?.ShowError(ex, ResourceHelper.GetString("QualityControl.RemoveArtifactsFailed", "Failed to remove artifacts"));
        _logService?.LogError(ex, "RemoveArtifacts");
      }
      finally
      {
        IsRemovingArtifacts = false;
        IsLoading = false;
        RemoveArtifactsCommand.NotifyCanExecuteChanged();
      }
    }

    partial void OnArtifactRemovalAudioIdChanged(string? value)
    {
      RemoveArtifactsCommand.NotifyCanExecuteChanged();
    }

    private async Task ExportReportAsync(CancellationToken cancellationToken = default)
    {
      try
      {
        IsLoading = true;
        ErrorMessage = null;

        // Create export data
        var exportData = new
        {
          timestamp = DateTime.UtcNow,
          presets = Presets.Select(p => new { p.Name, p.Description, p.TargetMetrics }),
          currentAnalysis = CurrentAnalysis != null ? new
          {
            CurrentAnalysis.MeetsTarget,
            CurrentAnalysis.QualityScore,
            CurrentAnalysis.Recommendations
          } : null,
          currentOptimization = CurrentOptimization != null ? new
          {
            CurrentOptimization.OptimizedParameters,
            CurrentOptimization.ExpectedImprovement
          } : null,
          currentRecommendation = CurrentRecommendation?.Recommendations?.FirstOrDefault() is { } rec ? new
          {
            RecommendedEngine = rec.RecommendedEngine,
            Reason = rec.Reasoning
          } : null,
          projectConsistency = SelectedProjectReport != null ? new
          {
            SelectedProjectReport.ProjectId,
            SelectedProjectReport.ConsistencyScore,
            SelectedProjectReport.IsConsistent,
            SelectedProjectReport.TotalSamples,
            Violations = SelectedProjectReport.Violations?.Count ?? 0
          } : null,
          allProjectsConsistency = AllProjectsConsistency != null ? new
          {
            AllProjectsConsistency.OverallConsistency,
            AllProjectsConsistency.ConsistentProjects,
            AllProjectsConsistency.TotalProjects,
            AllProjectsConsistency.TotalSamples,
            AllProjectsConsistency.TotalViolations
          } : null,
          qualityHeatmap = QualityHeatmap != null ? new
          {
            QualityHeatmap.XDimension,
            QualityHeatmap.YDimension,
            QualityHeatmap.Metric,
            QualityHeatmap.MinValue,
            QualityHeatmap.MaxValue
          } : null,
          qualityCorrelations = QualityCorrelations != null ? new
          {
            QualityCorrelations.Metrics
          } : null,
          qualityAnomalies = QualityAnomalies != null ? new
          {
            QualityAnomalies.AnomalyCount,
            QualityAnomalies.TotalSamples
          } : null
        };

        // Export as JSON
        var json = System.Text.Json.JsonSerializer.Serialize(exportData, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });

        // Save to file
        var picker = new Windows.Storage.Pickers.FileSavePicker();
        picker.SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.DocumentsLibrary;
        picker.FileTypeChoices.Add("JSON", new List<string> { ".json" });
        picker.FileTypeChoices.Add("CSV", new List<string> { ".csv" });
        picker.SuggestedFileName = $"QualityReport_{DateTime.Now:yyyyMMdd_HHmmss}";

        var file = await picker.PickSaveFileAsync();
        if (file != null)
        {
          if (file.FileType == ".json")
          {
            await Windows.Storage.FileIO.WriteTextAsync(file, json);
          }
          else if (file.FileType == ".csv")
          {
            // Convert to CSV format
            var csv = new System.Text.StringBuilder();
            csv.AppendLine("Metric,Value");
            if (CurrentAnalysis != null)
            {
              csv.AppendLine($"Meets Target,{CurrentAnalysis.MeetsTarget}")
                .AppendLine($"Quality Score,{CurrentAnalysis.QualityScore:F2}");
            }
            if (SelectedProjectReport != null)
            {
              csv.AppendLine($"Consistency Score,{SelectedProjectReport.ConsistencyScore:P0}")
                .AppendLine($"Is Consistent,{SelectedProjectReport.IsConsistent}")
                .AppendLine($"Total Samples,{SelectedProjectReport.TotalSamples}");
            }
            await Windows.Storage.FileIO.WriteTextAsync(file, csv.ToString());
          }
        }
      }
      catch (Exception ex)
      {
        ErrorMessage = ResourceHelper.FormatString("QualityControl.ExportReportFailed", ex.Message);
      }
      finally
      {
        IsLoading = false;
      }
    }

    // Artifact Removal models (IDEA 63)
    private class ArtifactRemovalRequest
    {
      public string AudioId { get; set; } = string.Empty;
    }

    private class ArtifactRemovalResponse
    {
      public string CleanedAudioId { get; set; } = string.Empty;
      public string? CleanedAudioUrl { get; set; }
      public int ArtifactsRemoved { get; set; }
      public double CleanedQualityScore { get; set; }
    }
  }
}