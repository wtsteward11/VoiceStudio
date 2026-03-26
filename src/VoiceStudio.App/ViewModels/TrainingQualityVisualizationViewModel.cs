using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VoiceStudio.Core.Models;
using VoiceStudio.Core.Panels;
using VoiceStudio.Core.Services;
using VoiceStudio.App.Services;
using VoiceStudio.App.Utilities;

namespace VoiceStudio.App.ViewModels
{
  /// <summary>
  /// ViewModel for the TrainingQualityVisualizationView panel - Training quality metrics visualization.
  /// </summary>
  public partial class TrainingQualityVisualizationViewModel : BaseViewModel, IPanelView, IPanelLifecycle
  {
    private readonly ITrainingClient _trainingClient;
    private readonly ToastNotificationService? _toastNotificationService;
    private CancellationTokenSource? _selectionLoadCts;

    public string PanelId => "training-quality-visualization";
    public string DisplayName => ResourceHelper.GetString("Panel.TrainingQualityVisualization.DisplayName", "Training Quality Visualization");
    public PanelRegion Region => PanelRegion.Center;

    [ObservableProperty]
    private ObservableCollection<string> availableTrainingJobs = new();

    [ObservableProperty]
    private string? selectedTrainingJobId;

    [ObservableProperty]
    private ObservableCollection<TrainingQualityMetrics> qualityHistory = new();

    [ObservableProperty]
    private TrainingQualityMetrics? bestMetrics;

    [ObservableProperty]
    private TrainingQualityMetrics? worstMetrics;

    [ObservableProperty]
    private double averageQualityScore;

    [ObservableProperty]
    private double averageMosScore;

    [ObservableProperty]
    private double averageSimilarity;

    [ObservableProperty]
    private double averageNaturalness;

    [ObservableProperty]
    private bool hasData;

    [ObservableProperty]
    private bool isLoading;

    [ObservableProperty]
    private string? errorMessage;

    [ObservableProperty]
    private string? statusMessage;

    public TrainingQualityVisualizationViewModel(IViewModelContext context, ITrainingClient trainingClient)
        : base(context)
    {
      _trainingClient = trainingClient ?? throw new ArgumentNullException(nameof(trainingClient));

      try
      {
        _toastNotificationService = AppServices.TryGetToastNotificationService();
      }
      catch
      {
        _toastNotificationService = null;
      }

      LoadTrainingJobsCommand = new EnhancedAsyncRelayCommand(async (ct) =>
      {
        using var profiler = PerformanceProfiler.StartCommand("LoadTrainingJobs");
        await LoadTrainingJobsAsync(ct);
      }, () => !IsLoading);
      LoadQualityHistoryCommand = new EnhancedAsyncRelayCommand(async (ct) =>
      {
        using var profiler = PerformanceProfiler.StartCommand("LoadQualityHistory");
        var jobId = SelectedTrainingJobId;
        if (!string.IsNullOrEmpty(jobId))
          await LoadQualityHistoryForSelectionAsync(jobId, ct);
      }, () => !string.IsNullOrEmpty(SelectedTrainingJobId) && !IsLoading);
      RefreshCommand = new EnhancedAsyncRelayCommand(async (ct) =>
      {
        using var profiler = PerformanceProfiler.StartCommand("Refresh");
        await RefreshAsync(ct);
      }, () => !IsLoading);
    }

    /// <inheritdoc />
    public async Task OnActivatedAsync(CancellationToken cancellationToken = default)
    {
      await LoadTrainingJobsAsync(cancellationToken);
    }

    Task IPanelLifecycle.OnDeactivatedAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    Task IPanelLifecycle.RefreshAsync(CancellationToken cancellationToken) => RefreshAsync(cancellationToken);

    public IAsyncRelayCommand LoadTrainingJobsCommand { get; }
    public IAsyncRelayCommand LoadQualityHistoryCommand { get; }
    public IAsyncRelayCommand RefreshCommand { get; }

    partial void OnSelectedTrainingJobIdChanged(string? value)
    {
      LoadQualityHistoryCommand.NotifyCanExecuteChanged();
      _selectionLoadCts?.Cancel();
      _selectionLoadCts?.Dispose();
      if (string.IsNullOrEmpty(value))
        return;
      var jobId = value;
      _selectionLoadCts = new CancellationTokenSource();
      var cts = _selectionLoadCts;
      _ = LoadQualityHistoryForSelectionAsync(jobId, cts.Token);
    }

    private async Task LoadTrainingJobsAsync(CancellationToken cancellationToken)
    {
      try
      {
        IsLoading = true;
        ErrorMessage = null;

        var jobs = await _trainingClient.ListTrainingJobsAsync(null, null, cancellationToken);

        AvailableTrainingJobs.Clear();
        foreach (var job in jobs)
        {
          AvailableTrainingJobs.Add(job.Id);
        }
      }
      catch (OperationCanceledException)
      {
        return; // User cancelled
      }
      catch (Exception ex)
      {
        ErrorMessage = ResourceHelper.FormatString("TrainingQualityVisualization.LoadTrainingJobsFailed", ex.Message);
        _toastNotificationService?.ShowToast(ToastType.Error,
            ResourceHelper.GetString("Toast.Title.LoadTrainingJobsFailed", "Load Training Jobs Failed"),
            ErrorMessage);
      }
      finally
      {
        IsLoading = false;
      }
    }

    private async Task LoadQualityHistoryForSelectionAsync(string jobId, CancellationToken cancellationToken)
    {
      try
      {
        IsLoading = true;
        ErrorMessage = null;
        HasData = false;
        QualityHistory.Clear();

        var history = await _trainingClient.GetTrainingQualityHistoryAsync(jobId, limit: 1000, cancellationToken);

        if (SelectedTrainingJobId != jobId)
          return;

        QualityHistory.Clear();
        foreach (var metrics in history.OrderBy(m => m.Epoch))
        {
          QualityHistory.Add(metrics);
        }

        if (QualityHistory.Count > 0)
        {
          BestMetrics = QualityHistory.OrderByDescending(m => m.QualityScore ?? 0).FirstOrDefault();
          WorstMetrics = QualityHistory.OrderBy(m => m.QualityScore ?? 0).FirstOrDefault();

          AverageQualityScore = QualityHistory.Where(m => m.QualityScore.HasValue).Average(m => m.QualityScore!.Value);
          AverageMosScore = QualityHistory.Where(m => m.MosScore.HasValue).Average(m => m.MosScore!.Value);
          AverageSimilarity = QualityHistory.Where(m => m.Similarity.HasValue).Average(m => m.Similarity!.Value);
          AverageNaturalness = QualityHistory.Where(m => m.Naturalness.HasValue).Average(m => m.Naturalness!.Value);

          HasData = true;
          StatusMessage = ResourceHelper.FormatString("TrainingQualityVisualization.QualityMetricsLoaded", QualityHistory.Count);
          _toastNotificationService?.ShowToast(ToastType.Success,
              ResourceHelper.GetString("Toast.Title.QualityHistoryLoaded", "Quality History Loaded"),
              StatusMessage);
        }
        else
        {
          HasData = false;
          StatusMessage = ResourceHelper.GetString("TrainingQualityVisualization.NoQualityHistoryData", "No quality history data available for this training job");
        }
      }
      catch (OperationCanceledException)
      {
        return; // User cancelled
      }
      catch (Exception ex)
      {
        ErrorMessage = ResourceHelper.FormatString("TrainingQualityVisualization.LoadQualityHistoryFailed", ex.Message);
        _toastNotificationService?.ShowToast(ToastType.Error,
            ResourceHelper.GetString("Toast.Title.LoadQualityHistoryFailed", "Load Quality History Failed"),
            ErrorMessage);
        HasData = false;
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
        await LoadTrainingJobsAsync(cancellationToken);
        var jobId = SelectedTrainingJobId;
        if (!string.IsNullOrEmpty(jobId))
          await LoadQualityHistoryForSelectionAsync(jobId, cancellationToken);
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

    // Chart data properties for binding
    public ObservableCollection<QualityChartDataPoint> QualityScoreData => new ObservableCollection<QualityChartDataPoint>(
        QualityHistory.Select(m => new QualityChartDataPoint
        {
          Epoch = m.Epoch,
          Value = m.QualityScore ?? 0.0,
          Label = ResourceHelper.FormatString("TrainingQualityVisualization.EpochLabel", m.Epoch)
        })
    );

    public ObservableCollection<QualityChartDataPoint> LossData => new ObservableCollection<QualityChartDataPoint>(
        QualityHistory.Select(m => new QualityChartDataPoint
        {
          Epoch = m.Epoch,
          Value = m.TrainingLoss ?? 0.0,
          Label = ResourceHelper.FormatString("TrainingQualityVisualization.EpochLabel", m.Epoch)
        })
    );

    public ObservableCollection<QualityChartDataPoint> ValidationLossData => new ObservableCollection<QualityChartDataPoint>(
        QualityHistory.Select(m => new QualityChartDataPoint
        {
          Epoch = m.Epoch,
          Value = m.ValidationLoss ?? 0.0,
          Label = ResourceHelper.FormatString("TrainingQualityVisualization.EpochLabel", m.Epoch)
        })
    );

    public ObservableCollection<QualityChartDataPoint> MosScoreData => new ObservableCollection<QualityChartDataPoint>(
        QualityHistory.Select(m => new QualityChartDataPoint
        {
          Epoch = m.Epoch,
          Value = m.MosScore ?? 0.0,
          Label = ResourceHelper.FormatString("TrainingQualityVisualization.EpochLabel", m.Epoch)
        })
    );

    public ObservableCollection<QualityChartDataPoint> SimilarityData => new ObservableCollection<QualityChartDataPoint>(
        QualityHistory.Select(m => new QualityChartDataPoint
        {
          Epoch = m.Epoch,
          Value = m.Similarity ?? 0.0,
          Label = ResourceHelper.FormatString("TrainingQualityVisualization.EpochLabel", m.Epoch)
        })
    );

    public ObservableCollection<QualityChartDataPoint> NaturalnessData => new ObservableCollection<QualityChartDataPoint>(
        QualityHistory.Select(m => new QualityChartDataPoint
        {
          Epoch = m.Epoch,
          Value = m.Naturalness ?? 0.0,
          Label = ResourceHelper.FormatString("TrainingQualityVisualization.EpochLabel", m.Epoch)
        })
    );
  }

  public class QualityChartDataPoint : ObservableObject
  {
    public int Epoch { get; set; }
    public double Value { get; set; }
    public string Label { get; set; } = string.Empty;
  }
}