using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VoiceStudio.Core.Panels;
using VoiceStudio.Core.Services;
using VoiceStudio.App.Services;
using VoiceStudio.App.Utilities;
using System.Text.Json;

namespace VoiceStudio.App.ViewModels
{
  /// <summary>
  /// ViewModel for the DatasetQAView panel - Dataset quality assurance reports.
  /// </summary>
  public partial class DatasetQAViewModel : BaseViewModel, IPanelView
  {
    private readonly IBackendClient _backendClient;
    private readonly ToastNotificationService? _toastNotificationService;

    public string PanelId => "dataset-qa";
    public string DisplayName => ResourceHelper.GetString("Panel.DatasetQA.DisplayName", "Dataset QA Reports");
    public PanelRegion Region => PanelRegion.Center;

    [ObservableProperty]
    private ObservableCollection<string> availableDatasets = new();

    [ObservableProperty]
    private string? selectedDatasetId;

    [ObservableProperty]
    private DatasetQAReport? qaReport;

    [ObservableProperty]
    private ObservableCollection<DatasetQAClipResult> clipResults = new();

    [ObservableProperty]
    private DatasetQAClipResult? selectedClipResult;

    [ObservableProperty]
    private double minQualityThreshold = 0.7;

    [ObservableProperty]
    private double minSnrThreshold = 20.0;

    [ObservableProperty]
    private double maxLufsThreshold = -10.0;

    [ObservableProperty]
    private bool hasReport;

    [ObservableProperty]
    private bool isLoading;

    [ObservableProperty]
    private string? errorMessage;

    [ObservableProperty]
    private string? statusMessage;

    public DatasetQAViewModel(IViewModelContext context, IBackendClient backendClient)
        : base(context)
    {
      _backendClient = backendClient ?? throw new ArgumentNullException(nameof(backendClient));

      try
      {
        _toastNotificationService = AppServices.TryGetToastNotificationService();
      }
      catch
      {
        _toastNotificationService = null;
      }

      LoadDatasetsCommand = new EnhancedAsyncRelayCommand(async (ct) =>
      {
        using var profiler = PerformanceProfiler.StartCommand("LoadDatasets");
        await LoadDatasetsAsync(ct);
      }, () => !IsLoading);
      RunQACommand = new EnhancedAsyncRelayCommand(async (ct) =>
      {
        using var profiler = PerformanceProfiler.StartCommand("RunQA");
        await RunQAAsync(ct);
      }, () => !string.IsNullOrEmpty(SelectedDatasetId) && !IsLoading);
      CullLowQualityCommand = new EnhancedAsyncRelayCommand(async (ct) =>
      {
        using var profiler = PerformanceProfiler.StartCommand("CullLowQuality");
        await CullLowQualityAsync(ct);
      }, () => HasReport && !IsLoading);
      RefreshCommand = new EnhancedAsyncRelayCommand(async (ct) =>
      {
        using var profiler = PerformanceProfiler.StartCommand("Refresh");
        await RefreshAsync(ct);
      }, () => !IsLoading);
      OptimizeDatasetCommand = new EnhancedAsyncRelayCommand(async (ct) =>
      {
        using var profiler = PerformanceProfiler.StartCommand("OptimizeDataset");
        await OptimizeDatasetAsync(ct);
      }, () => !string.IsNullOrEmpty(SelectedDatasetId) && !IsLoading);

      _ = LoadDatasetsAsync(CancellationToken.None);
    }

    public IAsyncRelayCommand LoadDatasetsCommand { get; }
    public IAsyncRelayCommand RunQACommand { get; }
    public IAsyncRelayCommand CullLowQualityCommand { get; }
    public IAsyncRelayCommand RefreshCommand { get; }
    public IAsyncRelayCommand OptimizeDatasetCommand { get; }

    private async Task LoadDatasetsAsync(CancellationToken cancellationToken)
    {
      try
      {
        IsLoading = true;
        ErrorMessage = null;

        var datasets = await _backendClient.GetTrainingDatasetsAsync(cancellationToken);

        AvailableDatasets.Clear();
        foreach (var dataset in datasets)
        {
          AvailableDatasets.Add(dataset.Id);
        }
      }
      catch (OperationCanceledException)
      {
        return; // User cancelled
      }
      catch (Exception ex)
      {
        ErrorMessage = ResourceHelper.FormatString("DatasetQA.LoadDatasetsFailed", ex.Message);
        _toastNotificationService?.ShowToast(ToastType.Error,
            ResourceHelper.GetString("Toast.Title.LoadDatasetsFailed", "Load Datasets Failed"),
            ErrorMessage);
      }
      finally
      {
        IsLoading = false;
      }
    }

    private async Task RunQAAsync(CancellationToken cancellationToken)
    {
      try
      {
        IsLoading = true;
        ErrorMessage = null;
        HasReport = false;
        ClipResults.Clear();

        if (string.IsNullOrEmpty(SelectedDatasetId))
        {
          ErrorMessage = ResourceHelper.GetString("DatasetQA.DatasetRequired", "Please select a dataset");
          return;
        }

        var dataset = await _backendClient.GetTrainingDatasetAsync(SelectedDatasetId, cancellationToken);
        if (dataset == null || dataset.AudioFiles == null || dataset.AudioFiles.Count == 0)
        {
          ErrorMessage = ResourceHelper.GetString("DatasetQA.DatasetNotFoundOrEmpty", "Dataset not found or has no audio files");
          return;
        }

        var scoreRequest = new Dictionary<string, object>
                {
                    { "clips", dataset.AudioFiles }
                };

        var json = JsonSerializer.Serialize(scoreRequest);
        var content = new System.Net.Http.StringContent(json, System.Text.Encoding.UTF8, "application/json");

        var response = await _backendClient.SendRequestAsync<Dictionary<string, object>, JsonElement[]>(
            "/api/dataset/score",
            scoreRequest,
            System.Net.Http.HttpMethod.Post,
            cancellationToken
        );

        if (response?.Length > 0)
        {
          ClipResults.Clear();
          foreach (var result in response)
          {
            var clipId = result.TryGetProperty("clip", out var clipProp) ? clipProp.GetString() ?? "" : "";
            var snr = result.TryGetProperty("snr", out var snrProp) ? snrProp.GetDouble() : 0.0;
            var lufs = result.TryGetProperty("lufs", out var lufsProp) ? lufsProp.GetDouble() : -70.0;
            var quality = result.TryGetProperty("quality", out var qualProp) ? qualProp.GetDouble() : 0.0;

            var clipResult = new DatasetQAClipResult
            {
              ClipId = clipId,
              Snr = snr,
              Lufs = lufs,
              Quality = quality,
              PassesQuality = quality >= MinQualityThreshold,
              PassesSnr = snr >= MinSnrThreshold,
              PassesLufs = lufs <= MaxLufsThreshold
            };
            clipResult.PassesAll = clipResult.PassesQuality && clipResult.PassesSnr && clipResult.PassesLufs;

            ClipResults.Add(clipResult);
          }

          var totalClips = ClipResults.Count;
          var passingClips = ClipResults.Count(c => c.PassesAll);
          var failingClips = totalClips - passingClips;
          var avgQuality = ClipResults.Count > 0 ? ClipResults.Average(c => c.Quality) : 0.0;
          var avgSnr = ClipResults.Count > 0 ? ClipResults.Average(c => c.Snr) : 0.0;
          var avgLufs = ClipResults.Count > 0 ? ClipResults.Average(c => c.Lufs) : -70.0;

          QaReport = new DatasetQAReport
          {
            DatasetId = SelectedDatasetId,
            TotalClips = totalClips,
            PassingClips = passingClips,
            FailingClips = failingClips,
            AverageQuality = avgQuality,
            AverageSnr = avgSnr,
            AverageLufs = avgLufs,
            GeneratedAt = DateTime.Now
          };

          HasReport = true;
          StatusMessage = ResourceHelper.FormatString("DatasetQA.QAReportGenerated", passingClips, totalClips);
          _toastNotificationService?.ShowToast(ToastType.Success,
              ResourceHelper.GetString("Toast.Title.QAReportGenerated", "QA Report Generated"),
              StatusMessage);
        }
      }
      catch (OperationCanceledException)
      {
        return; // User cancelled
      }
      catch (Exception ex)
      {
        ErrorMessage = ResourceHelper.FormatString("DatasetQA.RunQAFailed", ex.Message);
        _toastNotificationService?.ShowToast(ToastType.Error,
            ResourceHelper.GetString("Toast.Title.QAAnalysisFailed", "QA Analysis Failed"),
            ErrorMessage);
      }
      finally
      {
        IsLoading = false;
      }
    }

    private async Task CullLowQualityAsync(CancellationToken cancellationToken)
    {
      try
      {
        IsLoading = true;
        ErrorMessage = null;

        if (string.IsNullOrEmpty(SelectedDatasetId))
        {
          ErrorMessage = ResourceHelper.GetString("DatasetQA.DatasetRequired", "Please select a dataset");
          return;
        }

        var cullRequest = new Dictionary<string, object>
                {
                    { "dataset_id", SelectedDatasetId },
                    { "min_quality", MinQualityThreshold },
                    { "min_snr", MinSnrThreshold },
                    { "max_lufs", MaxLufsThreshold },
                    { "clips", ClipResults.Select(c => c.ClipId).ToList() }
                };

        await _backendClient.SendRequestAsync<Dictionary<string, object>, object>(
            "/api/dataset/cull",
            cullRequest,
            System.Net.Http.HttpMethod.Post,
            cancellationToken
        );

        StatusMessage = ResourceHelper.GetString("DatasetQA.ClipsCulled", "Low-quality clips culled from dataset");
        _toastNotificationService?.ShowToast(ToastType.Success,
            ResourceHelper.GetString("Toast.Title.CullComplete", "Cull Complete"),
            StatusMessage);

        await RunQAAsync(cancellationToken);
      }
      catch (OperationCanceledException)
      {
        return; // User cancelled
      }
      catch (Exception ex)
      {
        await HandleErrorAsync(ex, "CullLowQuality");
      }
      finally
      {
        IsLoading = false;
      }
    }

    private async Task OptimizeDatasetAsync(CancellationToken cancellationToken)
    {
      if (string.IsNullOrEmpty(SelectedDatasetId))
      {
        ErrorMessage = ResourceHelper.GetString("DatasetQA.DatasetRequired", "Please select a dataset");
        return;
      }

      try
      {
        IsLoading = true;
        ErrorMessage = null;

        var request = new Dictionary<string, object>
        {
          { "dataset_id", SelectedDatasetId }
        };

        await _backendClient.SendRequestAsync<Dictionary<string, object>, object>(
            $"/api/training/datasets/{Uri.EscapeDataString(SelectedDatasetId)}/optimize",
            request,
            System.Net.Http.HttpMethod.Post,
            cancellationToken
        );

        StatusMessage = ResourceHelper.GetString("DatasetQA.OptimizationComplete", "Dataset optimization completed successfully");
        _toastNotificationService?.ShowToast(ToastType.Success,
            ResourceHelper.GetString("Toast.Title.OptimizationComplete", "Optimization Complete"),
            StatusMessage);
      }
      catch (OperationCanceledException)
      {
        return;
      }
      catch (Exception ex)
      {
        ErrorMessage = ResourceHelper.FormatString("DatasetQA.OptimizationFailed", ex.Message);
        _toastNotificationService?.ShowToast(ToastType.Error,
            ResourceHelper.GetString("Toast.Title.OptimizationFailed", "Optimization Failed"),
            ErrorMessage);
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
        await LoadDatasetsAsync(cancellationToken);
        if (HasReport && !string.IsNullOrEmpty(SelectedDatasetId))
        {
          await RunQAAsync(cancellationToken);
        }
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
  }

  public class DatasetQAReport : ObservableObject
  {
    public string DatasetId { get; set; } = string.Empty;
    public int TotalClips { get; set; }
    public int PassingClips { get; set; }
    public int FailingClips { get; set; }
    public double AverageQuality { get; set; }
    public double AverageSnr { get; set; }
    public double AverageLufs { get; set; }
    public DateTime GeneratedAt { get; set; }
    public string PassRateDisplay => TotalClips > 0 ? $"{PassingClips}/{TotalClips} ({PassingClips * 100.0 / TotalClips:F1}%)" : "0/0 (0%)";
    public string AverageQualityDisplay => $"{AverageQuality:F2}";
    public string AverageSnrDisplay => $"{AverageSnr:F1} dB";
    public string AverageLufsDisplay => $"{AverageLufs:F1} LUFS";
  }

  public class DatasetQAClipResult : ObservableObject
  {
    public string ClipId { get; set; } = string.Empty;
    public double Snr { get; set; }
    public double Lufs { get; set; }
    public double Quality { get; set; }
    public bool PassesQuality { get; set; }
    public bool PassesSnr { get; set; }
    public bool PassesLufs { get; set; }
    public bool PassesAll { get; set; }
    public string SnrDisplay => $"{Snr:F1} dB";
    public string LufsDisplay => $"{Lufs:F1} LUFS";
    public string QualityDisplay => $"{Quality:F2}";
    public string StatusDisplay => PassesAll ? "Pass" : "Fail";
  }
}