using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RelayCommand = CommunityToolkit.Mvvm.Input.RelayCommand;
using IRelayCommand = CommunityToolkit.Mvvm.Input.IRelayCommand;
using VoiceStudio.Core.Models;
using VoiceStudio.Core.Panels;
using VoiceStudio.Core.Services;
using VoiceStudio.App.Services;
using VoiceStudio.App.Services.UndoableActions;
using VoiceStudio.App.Utilities;
using VoiceStudio.App.ViewModels;
using VoiceStudio.Core.Events;

// Backend-Frontend Integration Plan - Phase 3: WebSocket migration

namespace VoiceStudio.App.Views.Panels
{
  public partial class TrainingViewModel : BaseViewModel, IPanelView
  {
    private readonly IBackendClient _backendClient;
    private readonly ToastNotificationService? _toastNotificationService;
    private readonly UndoRedoService? _undoRedoService;
    private readonly MultiSelectService _multiSelectService;
    private MultiSelectState? _multiSelectState;
    private CancellationTokenSource? _pollingCts;
    private bool _isPolling;

    // Phase 3: WebSocket client for real-time training progress updates
    private readonly JobProgressWebSocketClient? _jobProgressClient;
    private bool _isWebSocketConnected;

    // Phase 4: EventAggregator for cross-panel synchronization
    private readonly IEventAggregator? _eventAggregator;

    public string PanelId => "training";
    public string DisplayName => ResourceHelper.GetString("Panel.Training.DisplayName", "Training");
    public PanelRegion Region => PanelRegion.Bottom;

    [ObservableProperty]
    private ObservableCollection<TrainingDataset> datasets = new();

    [ObservableProperty]
    private TrainingDataset? selectedDataset;

    [ObservableProperty]
    private ObservableCollection<TrainingStatus> trainingJobs = new();

    [ObservableProperty]
    private TrainingStatus? selectedTrainingJob;

    [ObservableProperty]
    private ObservableCollection<TrainingLogEntry> trainingLogs = new();

    [ObservableProperty]
    private string? selectedProfileId;

    [ObservableProperty]
    private string selectedEngine = "xtts";

    [ObservableProperty]
    private int epochs = 100;

    [ObservableProperty]
    private int batchSize = 4;

    [ObservableProperty]
    private double learningRate = 0.0001;

    [ObservableProperty]
    private bool useGpu = true;

    [ObservableProperty]
    private string datasetName = string.Empty;

    [ObservableProperty]
    private string? datasetDescription;

    [ObservableProperty]
    private string audioFilesText = string.Empty; // Comma-separated audio IDs

    [ObservableProperty]
    private bool isLoading;

    [ObservableProperty]
    private string? errorMessage;

    [ObservableProperty]
    private bool autoRefresh;

    [ObservableProperty]
    private string? filterStatus;

    // Multi-select support
    [ObservableProperty]
    private int selectedDatasetCount;

    [ObservableProperty]
    private bool hasMultipleDatasetSelection;

    [ObservableProperty]
    private int selectedTrainingJobCount;

    [ObservableProperty]
    private bool hasMultipleTrainingJobSelection;

    // Quality monitoring (IDEA 54)
    [ObservableProperty]
    private ObservableCollection<TrainingQualityMetrics> qualityHistory = new();

    [ObservableProperty]
    private bool isLoadingQualityHistory;

    [ObservableProperty]
    private bool hasQualityHistory;

    // Progress predictions (IDEA 28)
    public string EstimatedTimeRemaining
    {
      get
      {
        if (SelectedTrainingJob == null || SelectedTrainingJob.Status != "running")
          return "N/A";

        var progress = SelectedTrainingJob.Progress;
        if (progress <= 0 || progress >= 1.0)
          return "N/A";

        if (SelectedTrainingJob.Started.HasValue)
        {
          var elapsed = DateTime.UtcNow - SelectedTrainingJob.Started.Value;
          var estimatedTotal = TimeSpan.FromSeconds(elapsed.TotalSeconds / progress);
          var remaining = estimatedTotal - elapsed;

          if (remaining.TotalHours >= 1)
            return $"{remaining.TotalHours:F1} hours";
          else if (remaining.TotalMinutes >= 1)
            return $"{remaining.TotalMinutes:F1} minutes";
          else
            return $"{remaining.TotalSeconds:F0} seconds";
        }

        return "Calculating...";
      }
    }

    public string EstimatedCompletionTime
    {
      get
      {
        if (SelectedTrainingJob == null || SelectedTrainingJob.Status != "running")
          return "N/A";

        var progress = SelectedTrainingJob.Progress;
        if (progress <= 0 || progress >= 1.0)
          return "N/A";

        if (SelectedTrainingJob.Started.HasValue)
        {
          var elapsed = DateTime.UtcNow - SelectedTrainingJob.Started.Value;
          var estimatedTotal = TimeSpan.FromSeconds(elapsed.TotalSeconds / progress);
          var completion = DateTime.UtcNow + (estimatedTotal - elapsed);

          return completion.ToString("HH:mm:ss");
        }

        return "Calculating...";
      }
    }

    public string ProgressRate
    {
      get
      {
        if (SelectedTrainingJob == null || SelectedTrainingJob.Status != "running")
          return "N/A";

        var progress = SelectedTrainingJob.Progress;
        if (progress <= 0 || !SelectedTrainingJob.Started.HasValue)
          return "N/A";

        var elapsed = DateTime.UtcNow - SelectedTrainingJob.Started.Value;
        if (elapsed.TotalSeconds <= 0)
          return "N/A";

        var rate = progress / elapsed.TotalSeconds * 100; // % per second
        return $"{rate:F3}%/s";
      }
    }

    public bool IsDatasetSelected(string datasetId) => _multiSelectState?.SelectedIds.Contains($"dataset_{datasetId}") ?? false;
    public bool IsTrainingJobSelected(string jobId) => _multiSelectState?.SelectedIds.Contains($"job_{jobId}") ?? false;

    public ObservableCollection<string> Engines { get; } = new()
        {
            "xtts",
            "rvc",
            "coqui"
        };

    public TrainingViewModel(IViewModelContext context, IBackendClient backendClient)
        : base(context)
    {
      _backendClient = backendClient ?? throw new ArgumentNullException(nameof(backendClient));

      // Get multi-select service
      var multiSelectService = AppServices.TryGetMultiSelectService();
      _multiSelectService = multiSelectService ?? throw new InvalidOperationException("MultiSelectService is required but not registered");
      _multiSelectState = _multiSelectService.GetState(PanelId);

      // Get services (may be null if not initialized)
      try
      {
        _toastNotificationService = AppServices.TryGetToastNotificationService();
      }
      catch
      {
        // Services may not be initialized yet - that's okay
        _toastNotificationService = null;
      }

      // Get undo/redo service (may be null if not initialized)
      try
      {
        _undoRedoService = AppServices.TryGetUndoRedoService();
      }
      catch
      {
        // Service may not be initialized yet - that's okay
        _undoRedoService = null;
      }

      // Phase 3: Initialize WebSocket client for real-time training updates
      try
      {
        var webSocketFactory = AppServices.TryGetWebSocketClientFactory();
        if (webSocketFactory != null)
        {
          var jobProgressClient = webSocketFactory.CreateJobProgressClient();
          if (jobProgressClient is JobProgressWebSocketClient wsClient)
          {
            _jobProgressClient = wsClient;
            SubscribeToWebSocketEvents();
          }
        }
      }
      catch
      {
        // WebSocket service may not be available - fall back to polling
        _jobProgressClient = null;
      }

      // Phase 4: Initialize EventAggregator for cross-panel synchronization
      try
      {
        _eventAggregator = AppServices.TryGetEventAggregator();
      }
      catch
      {
        // EventAggregator service may not be available
        _eventAggregator = null;
      }

      LoadDatasetsCommand = new EnhancedAsyncRelayCommand(async (ct) =>
      {
        using var profiler = PerformanceProfiler.StartCommand("LoadDatasets");
        await LoadDatasetsAsync(ct);
      }, () => !IsLoading);
      CreateDatasetCommand = new EnhancedAsyncRelayCommand(async (ct) =>
      {
        using var profiler = PerformanceProfiler.StartCommand("CreateDataset");
        await CreateDatasetAsync(ct);
      }, () => !IsLoading && CanCreateDataset());
      DeleteDatasetCommand = new EnhancedAsyncRelayCommand<TrainingDataset>(async (dataset, ct) =>
      {
        using var profiler = PerformanceProfiler.StartCommand("DeleteDataset");
        await DeleteDatasetAsync(dataset, ct);
      }, dataset => dataset != null && !IsLoading);
      StartTrainingCommand = new EnhancedAsyncRelayCommand(async (ct) =>
      {
        using var profiler = PerformanceProfiler.StartCommand("StartTraining");
        await StartTrainingAsync(ct);
      }, () => !IsLoading && CanStartTraining());
      LoadTrainingJobsCommand = new EnhancedAsyncRelayCommand(async (ct) =>
      {
        using var profiler = PerformanceProfiler.StartCommand("LoadTrainingJobs");
        await LoadTrainingJobsAsync(ct);
      }, () => !IsLoading);
      RefreshCommand = new EnhancedAsyncRelayCommand(async (ct) =>
      {
        using var profiler = PerformanceProfiler.StartCommand("Refresh");
        await RefreshAsync(ct);
      }, () => !IsLoading);
      CancelTrainingCommand = new EnhancedAsyncRelayCommand<TrainingStatus>(async (job, ct) =>
      {
        using var profiler = PerformanceProfiler.StartCommand("CancelTraining");
        await CancelTrainingAsync(job, ct);
      }, job => job != null && !IsLoading && (job.Status == "pending" || job.Status == "running"));
      DeleteTrainingJobCommand = new EnhancedAsyncRelayCommand<TrainingStatus>(async (job, ct) =>
      {
        using var profiler = PerformanceProfiler.StartCommand("DeleteTrainingJob");
        await DeleteTrainingJobAsync(job, ct);
      }, job => job != null && !IsLoading && job.Status != "running" && job.Status != "pending");
      LoadLogsCommand = new EnhancedAsyncRelayCommand(async (ct) =>
      {
        using var profiler = PerformanceProfiler.StartCommand("LoadLogs");
        await LoadLogsAsync(ct);
      }, () => !IsLoading && SelectedTrainingJob != null);

      // Quality monitoring commands (IDEA 54)
      LoadQualityHistoryCommand = new EnhancedAsyncRelayCommand(async (ct) =>
      {
        using var profiler = PerformanceProfiler.StartCommand("LoadQualityHistory");
        await LoadQualityHistoryAsync(ct);
      }, () => !IsLoading && SelectedTrainingJob != null && !IsLoadingQualityHistory);

      // Multi-select commands
      SelectAllDatasetsCommand = new RelayCommand(SelectAllDatasets, () => Datasets?.Count > 0);
      ClearDatasetSelectionCommand = new RelayCommand(ClearDatasetSelection);
      SelectAllTrainingJobsCommand = new RelayCommand(SelectAllTrainingJobs, () => TrainingJobs?.Count > 0);
      ClearTrainingJobSelectionCommand = new RelayCommand(ClearTrainingJobSelection);

      // Subscribe to selection changes
      _multiSelectService.SelectionChanged += (s, e) =>
      {
        if (e.PanelId == PanelId)
        {
          UpdateDatasetSelectionProperties();
          UpdateTrainingJobSelectionProperties();
          OnPropertyChanged(nameof(SelectedDatasetCount));
          OnPropertyChanged(nameof(HasMultipleDatasetSelection));
          OnPropertyChanged(nameof(SelectedTrainingJobCount));
          OnPropertyChanged(nameof(HasMultipleTrainingJobSelection));
        }
      };
    }

    public IAsyncRelayCommand LoadDatasetsCommand { get; }
    public IAsyncRelayCommand CreateDatasetCommand { get; }
    public IAsyncRelayCommand<TrainingDataset> DeleteDatasetCommand { get; }
    public IAsyncRelayCommand StartTrainingCommand { get; }
    public IAsyncRelayCommand LoadTrainingJobsCommand { get; }
    public IAsyncRelayCommand RefreshCommand { get; }
    public IAsyncRelayCommand<TrainingStatus> CancelTrainingCommand { get; }
    public IAsyncRelayCommand<TrainingStatus> DeleteTrainingJobCommand { get; }
    public IAsyncRelayCommand LoadLogsCommand { get; }

    // Quality monitoring commands (IDEA 54)
    public IAsyncRelayCommand LoadQualityHistoryCommand { get; }

    // Multi-select commands
    public IRelayCommand SelectAllDatasetsCommand { get; }
    public IRelayCommand ClearDatasetSelectionCommand { get; }
    public IRelayCommand SelectAllTrainingJobsCommand { get; }
    public IRelayCommand ClearTrainingJobSelectionCommand { get; }

    private bool CanCreateDataset()
    {
      return !string.IsNullOrWhiteSpace(DatasetName);
    }

    private bool CanStartTraining()
    {
      return SelectedDataset != null && !string.IsNullOrWhiteSpace(SelectedProfileId);
    }

    partial void OnDatasetNameChanged(string value)
    {
      CreateDatasetCommand.NotifyCanExecuteChanged();
    }

    partial void OnSelectedDatasetChanged(TrainingDataset? value)
    {
      StartTrainingCommand.NotifyCanExecuteChanged();
    }

    partial void OnSelectedProfileIdChanged(string? value)
    {
      StartTrainingCommand.NotifyCanExecuteChanged();
    }

    partial void OnSelectedTrainingJobChanged(TrainingStatus? value)
    {
      if (value != null)
      {
        // Auto-load logs and quality history when job is selected
        _ = LoadLogsAsync(CancellationToken.None);
        _ = LoadQualityHistoryAsync(CancellationToken.None);
      }
      else
      {
        TrainingLogs.Clear();
        QualityHistory.Clear();
        HasQualityHistory = false;
      }

      // Update quality-related property notifications
      OnPropertyChanged(nameof(HasQualityAlerts));
      OnPropertyChanged(nameof(HasEarlyStoppingRecommendation));
      OnPropertyChanged(nameof(QualityScoreDisplay));

      // Update progress prediction properties
      OnPropertyChanged(nameof(EstimatedTimeRemaining));
      OnPropertyChanged(nameof(EstimatedCompletionTime));
      OnPropertyChanged(nameof(ProgressRate));

      LoadLogsCommand.NotifyCanExecuteChanged();
      LoadQualityHistoryCommand.NotifyCanExecuteChanged();
    }

    partial void OnIsLoadingChanged(bool value)
    {
      LoadDatasetsCommand.NotifyCanExecuteChanged();
      CreateDatasetCommand.NotifyCanExecuteChanged();
      StartTrainingCommand.NotifyCanExecuteChanged();
      LoadTrainingJobsCommand.NotifyCanExecuteChanged();
      RefreshCommand.NotifyCanExecuteChanged();
      CancelTrainingCommand.NotifyCanExecuteChanged();
      DeleteTrainingJobCommand.NotifyCanExecuteChanged();
      LoadLogsCommand.NotifyCanExecuteChanged();
    }

    partial void OnAutoRefreshChanged(bool value)
    {
      if (value)
      {
        StartPolling();
      }
      else
      {
        StopPolling();
      }
    }

    private async Task LoadDatasetsAsync(CancellationToken cancellationToken)
    {
      IsLoading = true;
      ErrorMessage = null;

      try
      {
        var datasets = await _backendClient.ListDatasetsAsync(cancellationToken);

        Datasets.Clear();
        foreach (var dataset in datasets.OrderByDescending(d => d.Created))
        {
          Datasets.Add(dataset);
        }

        if (Datasets.Count > 0)
        {
          _toastNotificationService?.ShowSuccess(
              ResourceHelper.GetString("Toast.Title.DatasetsLoaded", "Datasets Loaded"),
              ResourceHelper.FormatString("Training.DatasetsLoaded", Datasets.Count));
        }
      }
      catch (OperationCanceledException)
      {
        return; // User cancelled
      }
      catch (Exception ex)
      {
        ErrorMessage = $"Failed to load datasets: {ErrorHandler.GetUserFriendlyMessage(ex)}";
        await HandleErrorAsync(ex, "LoadDatasets");
      }
      finally
      {
        IsLoading = false;
      }
    }

    private async Task CreateDatasetAsync(CancellationToken cancellationToken)
    {
      if (string.IsNullOrWhiteSpace(DatasetName))
        return;

      IsLoading = true;
      ErrorMessage = null;

      try
      {
        var audioFiles = string.IsNullOrWhiteSpace(AudioFilesText)
            ? new List<string>()
            : AudioFilesText.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();

        var dataset = await _backendClient.CreateDatasetAsync(DatasetName, DatasetDescription, audioFiles, cancellationToken);

        Datasets.Insert(0, dataset);
        SelectedDataset = dataset;

        // Register undo action
        if (_undoRedoService != null)
        {
          var action = new CreateTrainingDatasetAction(
              Datasets,
              _backendClient,
              dataset,
              onUndo: (d) =>
              {
                if (SelectedDataset?.Id == d.Id)
                {
                  SelectedDataset = Datasets.FirstOrDefault();
                }
              },
              onRedo: (d) => SelectedDataset = d);
          _undoRedoService.RegisterAction(action);
        }

        // Clear form
        DatasetName = string.Empty;
        DatasetDescription = null;
        AudioFilesText = string.Empty;

        _toastNotificationService?.ShowSuccess(
            ResourceHelper.GetString("Toast.Title.DatasetCreated", "Dataset Created"),
            ResourceHelper.FormatString("Training.DatasetCreatedSuccess", dataset.Name));
      }
      catch (OperationCanceledException)
      {
        return; // User cancelled
      }
      catch (Exception ex)
      {
        ErrorMessage = $"Failed to create dataset: {ErrorHandler.GetUserFriendlyMessage(ex)}";
        await HandleErrorAsync(ex, "CreateDataset");
      }
      finally
      {
        IsLoading = false;
      }
    }

    private async Task DeleteDatasetAsync(TrainingDataset? dataset, CancellationToken cancellationToken)
    {
      if (dataset == null)
        return;

      IsLoading = true;
      ErrorMessage = null;

      try
      {
        await _backendClient.DeleteDatasetAsync(dataset.Id, cancellationToken);

        var datasetIndex = Datasets.IndexOf(dataset);
        Datasets.Remove(dataset);

        if (SelectedDataset?.Id == dataset.Id)
        {
          SelectedDataset = Datasets.FirstOrDefault();
        }

        // Note: Register undo action - DeleteTrainingDatasetAction not implemented
        // if (_undoRedoService != null && datasetIndex >= 0)
        // {
        //     var action = new DeleteTrainingDatasetAction(
        //         Datasets,
        //         _backendClient,
        //         dataset,
        //         datasetIndex);
        //     _undoRedoService.RegisterAction(action);
        // }

        _toastNotificationService?.ShowSuccess(
            ResourceHelper.GetString("Toast.Title.DatasetDeleted", "Dataset Deleted"),
            ResourceHelper.FormatString("Training.DatasetDeletedSuccess", dataset.Name));
      }
      catch (OperationCanceledException)
      {
        return; // User cancelled
      }
      catch (Exception ex)
      {
        ErrorMessage = $"Failed to delete dataset: {ErrorHandler.GetUserFriendlyMessage(ex)}";
        await HandleErrorAsync(ex, "DeleteDataset");
      }
      finally
      {
        IsLoading = false;
      }
    }

    private async Task StartTrainingAsync(CancellationToken cancellationToken)
    {
      if (SelectedDataset == null || string.IsNullOrWhiteSpace(SelectedProfileId))
        return;

      IsLoading = true;
      ErrorMessage = null;

      try
      {
        var request = new TrainingRequest
        {
          DatasetId = SelectedDataset.Id,
          ProfileId = SelectedProfileId,
          Engine = SelectedEngine,
          Epochs = Epochs,
          BatchSize = BatchSize,
          LearningRate = LearningRate,
          Gpu = UseGpu
        };

        var status = await _backendClient.StartTrainingAsync(request, cancellationToken);

        TrainingJobs.Insert(0, status);
        SelectedTrainingJob = status;

        // Reload jobs
        await LoadTrainingJobsAsync(cancellationToken);

        _toastNotificationService?.ShowSuccess(
            ResourceHelper.GetString("Toast.Title.TrainingStarted", "Training Started"),
            ResourceHelper.FormatString("Training.TrainingStarted", SelectedProfileId, SelectedEngine));

        // Phase 4: Publish job started event for cross-panel synchronization
        _eventAggregator?.Publish(new JobStartedEvent(PanelId, status.Id, "training", $"Training for {SelectedProfileId}"));
      }
      catch (OperationCanceledException)
      {
        return; // User cancelled
      }
      catch (Exception ex)
      {
        ErrorMessage = $"Failed to start training: {ErrorHandler.GetUserFriendlyMessage(ex)}";
        await HandleErrorAsync(ex, "StartTraining");
      }
      finally
      {
        IsLoading = false;
      }
    }

    private async Task LoadTrainingJobsAsync(CancellationToken cancellationToken)
    {
      IsLoading = true;
      ErrorMessage = null;

      try
      {
        var jobs = await _backendClient.ListTrainingJobsAsync(SelectedProfileId, FilterStatus, cancellationToken);

        TrainingJobs.Clear();
        foreach (var job in jobs.OrderByDescending(j => j.Started ?? DateTime.MinValue))
        {
          TrainingJobs.Add(job);
        }

        if (TrainingJobs.Count > 0)
        {
          _toastNotificationService?.ShowSuccess(
              ResourceHelper.GetString("Toast.Title.TrainingJobsLoaded", "Training Jobs Loaded"),
              ResourceHelper.FormatString("Training.TrainingJobsLoaded", TrainingJobs.Count));
        }
      }
      catch (OperationCanceledException)
      {
        return; // User cancelled
      }
      catch (Exception ex)
      {
        ErrorMessage = $"Failed to load training jobs: {ErrorHandler.GetUserFriendlyMessage(ex)}";
        await HandleErrorAsync(ex, "LoadTrainingJobs");
      }
      finally
      {
        IsLoading = false;
      }
    }

    private async Task RefreshAsync(CancellationToken cancellationToken)
    {
      await Task.WhenAll(
          LoadDatasetsAsync(cancellationToken),
          LoadTrainingJobsAsync(cancellationToken)
      );
    }

    private async Task CancelTrainingAsync(TrainingStatus? job, CancellationToken cancellationToken)
    {
      if (job == null)
        return;

      IsLoading = true;
      ErrorMessage = null;

      try
      {
        await _backendClient.CancelTrainingAsync(job.Id, cancellationToken);

        // Reload jobs to get updated status
        await LoadTrainingJobsAsync(cancellationToken);

        _toastNotificationService?.ShowSuccess(
            ResourceHelper.GetString("Toast.Title.TrainingCancelled", "Training Cancelled"),
            ResourceHelper.FormatString("Training.TrainingCancelled", job.Id));
      }
      catch (OperationCanceledException)
      {
        return; // User cancelled
      }
      catch (Exception ex)
      {
        ErrorMessage = $"Failed to cancel training: {ErrorHandler.GetUserFriendlyMessage(ex)}";
        await HandleErrorAsync(ex, "CancelTraining");
      }
      finally
      {
        IsLoading = false;
      }
    }

    private async Task DeleteTrainingJobAsync(TrainingStatus? job, CancellationToken cancellationToken)
    {
      if (job == null)
        return;

      IsLoading = true;
      ErrorMessage = null;

      try
      {
        await _backendClient.DeleteTrainingJobAsync(job.Id, cancellationToken);

        TrainingJobs.Remove(job);

        if (SelectedTrainingJob == job)
        {
          SelectedTrainingJob = null;
          TrainingLogs.Clear();
        }

        _toastNotificationService?.ShowSuccess(
            ResourceHelper.GetString("Toast.Title.TrainingJobDeleted", "Training Job Deleted"),
            ResourceHelper.FormatString("Training.TrainingJobDeleted", job.Id));
      }
      catch (OperationCanceledException)
      {
        return; // User cancelled
      }
      catch (Exception ex)
      {
        ErrorMessage = $"Failed to delete training job: {ErrorHandler.GetUserFriendlyMessage(ex)}";
        await HandleErrorAsync(ex, "DeleteTrainingJob");
      }
      finally
      {
        IsLoading = false;
      }
    }

    private async Task LoadLogsAsync(CancellationToken cancellationToken)
    {
      if (SelectedTrainingJob == null)
        return;

      IsLoading = true;
      ErrorMessage = null;

      try
      {
        var logs = await _backendClient.GetTrainingLogsAsync(SelectedTrainingJob.Id, limit: 100, cancellationToken);

        TrainingLogs.Clear();
        foreach (var log in logs.OrderBy(l => l.Timestamp))
        {
          TrainingLogs.Add(log);
        }
      }
      catch (OperationCanceledException)
      {
        return; // User cancelled
      }
      catch (Exception ex)
      {
        ErrorMessage = ResourceHelper.FormatString("Training.LoadTrainingLogsFailed", ErrorHandler.GetUserFriendlyMessage(ex));
        await HandleErrorAsync(ex, "LoadLogs");
      }
      finally
      {
        IsLoading = false;
      }
    }

    // Quality monitoring methods (IDEA 54)
    private async Task LoadQualityHistoryAsync(CancellationToken cancellationToken)
    {
      if (SelectedTrainingJob == null)
        return;

      IsLoadingQualityHistory = true;
      LoadQualityHistoryCommand.NotifyCanExecuteChanged();

      try
      {
        var history = await _backendClient.GetTrainingQualityHistoryAsync(SelectedTrainingJob.Id, limit: 100, cancellationToken);

        QualityHistory.Clear();
        foreach (var metrics in history.OrderBy(m => m.Epoch))
        {
          QualityHistory.Add(metrics);
        }

        HasQualityHistory = QualityHistory.Count > 0;
      }
      catch (OperationCanceledException)
      {
        return; // User cancelled
      }
      catch (Exception ex)
      {
        await HandleErrorAsync(ex, "LoadQualityHistory");
        HasQualityHistory = false;
      }
      finally
      {
        IsLoadingQualityHistory = false;
        LoadQualityHistoryCommand.NotifyCanExecuteChanged();
      }
    }

    // Helper properties for quality display
    public bool HasQualityAlerts => SelectedTrainingJob?.QualityAlerts?.Count > 0;

    public bool HasEarlyStoppingRecommendation => SelectedTrainingJob?.EarlyStoppingRecommendation != null;

    public string QualityScoreDisplay => SelectedTrainingJob?.QualityScore.HasValue == true
        ? $"{SelectedTrainingJob.QualityScore.Value:P0}"
        : ResourceHelper.GetString("Training.NotAvailable", "N/A");

    #region Phase 3: WebSocket Integration

    private void SubscribeToWebSocketEvents()
    {
      if (_jobProgressClient == null)
        return;

      _jobProgressClient.ProgressUpdated += OnTrainingProgressUpdated;
      _jobProgressClient.StatusChanged += OnTrainingStatusChanged;
      _jobProgressClient.JobCompleted += OnTrainingJobCompleted;
      _jobProgressClient.JobFailed += OnTrainingJobFailed;
    }

    private void UnsubscribeFromWebSocketEvents()
    {
      if (_jobProgressClient == null)
        return;

      _jobProgressClient.ProgressUpdated -= OnTrainingProgressUpdated;
      _jobProgressClient.StatusChanged -= OnTrainingStatusChanged;
      _jobProgressClient.JobCompleted -= OnTrainingJobCompleted;
      _jobProgressClient.JobFailed -= OnTrainingJobFailed;
    }

    private async Task ConnectWebSocketAsync()
    {
      if (_jobProgressClient == null || _isWebSocketConnected)
        return;

      try
      {
        await _jobProgressClient.ConnectAsync();
        _isWebSocketConnected = _jobProgressClient.IsConnected;
        System.Diagnostics.Debug.WriteLine($"TrainingViewModel: WebSocket connected: {_isWebSocketConnected}");
      }
      catch (Exception ex)
      {
        System.Diagnostics.Debug.WriteLine($"TrainingViewModel: WebSocket connection failed: {ex.Message}");
        _isWebSocketConnected = false;
      }
    }

    private async Task DisconnectWebSocketAsync()
    {
      if (_jobProgressClient == null || !_isWebSocketConnected)
        return;

      try
      {
        await _jobProgressClient.DisconnectAsync();
        _isWebSocketConnected = false;
      }
      catch (Exception ex)
      {
        System.Diagnostics.Debug.WriteLine($"TrainingViewModel: WebSocket disconnect error: {ex.Message}");
      }
    }

    private void OnTrainingProgressUpdated(object? sender, JobProgressUpdate update)
    {
      // Only handle training job types
      if (update.JobType != "training" && !string.IsNullOrEmpty(update.JobType))
        return;

      Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread()?.TryEnqueue(() =>
      {
        var job = TrainingJobs.FirstOrDefault(j => j.Id == update.JobId);
        if (job != null)
        {
          job.Progress = update.Progress;
          job.Status = update.Status ?? job.Status;
          OnPropertyChanged(nameof(TrainingJobs));
          OnPropertyChanged(nameof(ProgressRate));
          OnPropertyChanged(nameof(EstimatedCompletionTime));
        }
      });
    }

    private void OnTrainingStatusChanged(object? sender, JobStatusUpdate update)
    {
      if (update.JobType != "training" && !string.IsNullOrEmpty(update.JobType))
        return;

      Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread()?.TryEnqueue(() =>
      {
        var job = TrainingJobs.FirstOrDefault(j => j.Id == update.JobId);
        if (job != null)
        {
          job.Status = update.Status;
          OnPropertyChanged(nameof(TrainingJobs));
          
          // Refresh commands that depend on job status
          StartTrainingCommand.NotifyCanExecuteChanged();
          CancelTrainingCommand.NotifyCanExecuteChanged();
        }
      });
    }

    private void OnTrainingJobCompleted(object? sender, JobCompletedUpdate update)
    {
      if (update.JobType != "training" && !string.IsNullOrEmpty(update.JobType))
        return;

      Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread()?.TryEnqueue(async () =>
      {
        var job = TrainingJobs.FirstOrDefault(j => j.Id == update.JobId);
        if (job != null)
        {
          job.Status = "completed";
          job.Progress = 1.0;
          OnPropertyChanged(nameof(TrainingJobs));
          
          // Show success notification
          _toastNotificationService?.ShowSuccess($"Training job completed successfully");
        }

        // Refresh logs and jobs to get final state
        await LoadLogsAsync(CancellationToken.None);
        await LoadTrainingJobsAsync(CancellationToken.None);
      });
    }

    private void OnTrainingJobFailed(object? sender, JobFailedUpdate update)
    {
      if (update.JobType != "training" && !string.IsNullOrEmpty(update.JobType))
        return;

      Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread()?.TryEnqueue(() =>
      {
        var job = TrainingJobs.FirstOrDefault(j => j.Id == update.JobId);
        if (job != null)
        {
          job.Status = "failed";
          job.ErrorMessage = update.Error;
          OnPropertyChanged(nameof(TrainingJobs));
          
          // Show error notification
          _toastNotificationService?.ShowError($"Training failed: {update.Error}");
        }
      });
    }

    #endregion

    private void StartPolling()
    {
      // Phase 3: Prefer WebSocket over polling
      if (_jobProgressClient != null)
      {
        _ = ConnectWebSocketAsync();
        // Even with WebSocket, do initial load
        if (!_isPolling)
        {
          _isPolling = true;
          // Load once, then rely on WebSocket for updates
          _ = LoadDatasetsAsync(CancellationToken.None);
          _ = LoadTrainingJobsAsync(CancellationToken.None);
        }
        return;
      }

      // Fall back to polling if WebSocket is not available
      if (_isPolling)
        return;

      _isPolling = true;
      _pollingCts = new CancellationTokenSource();
      _ = PollTrainingStatusAsync(_pollingCts.Token);
    }

    /// <summary>
    /// Calculate estimated time remaining for a training job.
    /// </summary>
    public string GetEstimatedTimeRemaining(TrainingStatus? job)
    {
      if (job == null || job.Started == null || job.Progress <= 0 || job.Progress >= 1.0)
        return string.Empty;

      var elapsed = DateTime.UtcNow - job.Started.Value;
      if (elapsed.TotalSeconds < 1)
        return ResourceHelper.GetString("Training.Calculating", "Calculating...");

      var estimatedTotal = TimeSpan.FromSeconds(elapsed.TotalSeconds / job.Progress);
      var remaining = estimatedTotal - elapsed;

      if (remaining.TotalSeconds < 0)
        return ResourceHelper.GetString("Training.AlmostDone", "Almost done");

      if (remaining.TotalHours >= 1)
        return ResourceHelper.FormatString("Training.TimeRemainingHours", remaining.TotalHours);
      else if (remaining.TotalMinutes >= 1)
        return ResourceHelper.FormatString("Training.TimeRemainingMinutes", remaining.TotalMinutes);
      else
        return ResourceHelper.FormatString("Training.TimeRemainingSeconds", remaining.TotalSeconds);
    }

    private void StopPolling()
    {
      if (!_isPolling)
        return;

      _isPolling = false;
      _pollingCts?.Cancel();
      _pollingCts?.Dispose();
      _pollingCts = null;

      // Phase 3: Also disconnect WebSocket
      if (_jobProgressClient != null)
      {
        _ = DisconnectWebSocketAsync();
        UnsubscribeFromWebSocketEvents();
        _jobProgressClient.Dispose();
      }
    }

    private async Task PollTrainingStatusAsync(CancellationToken cancellationToken)
    {
      while (!cancellationToken.IsCancellationRequested && _isPolling)
      {
        try
        {
          if (SelectedTrainingJob != null && (SelectedTrainingJob.Status == "running" || SelectedTrainingJob.Status == "pending"))
          {
            var updatedStatus = await _backendClient.GetTrainingStatusAsync(SelectedTrainingJob.Id, cancellationToken);

            // Update in collection
            var index = TrainingJobs.IndexOf(SelectedTrainingJob);
            if (index >= 0)
            {
              TrainingJobs[index] = updatedStatus;
              SelectedTrainingJob = updatedStatus;
            }

            // Reload logs if training is running
            if (updatedStatus.Status == "running")
            {
              await LoadLogsAsync(cancellationToken);
            }
          }

          // Also refresh job list
          await LoadTrainingJobsAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
          break;
        }
        catch (Exception ex)
        {
          // Log but don't show error for polling failures
          System.Diagnostics.Debug.WriteLine($"Polling error: {ex.Message}");
        }

        await Task.Delay(2000, cancellationToken); // Poll every 2 seconds
      }
    }

    // Multi-select methods for datasets
    public void ToggleDatasetSelection(string datasetId, bool isCtrlPressed, bool isShiftPressed)
    {
      if (_multiSelectState == null)
        return;

      var prefixedId = $"dataset_{datasetId}";

      if (isShiftPressed && !string.IsNullOrEmpty(_multiSelectState.RangeAnchorId) && _multiSelectState.RangeAnchorId.StartsWith("dataset_"))
      {
        // Range selection
        var allDatasetIds = Datasets.Select(d => $"dataset_{d.Id}").ToList();
        _multiSelectState.SetRange(_multiSelectState.RangeAnchorId, prefixedId, allDatasetIds);
      }
      else if (isCtrlPressed)
      {
        // Toggle selection
        _multiSelectState.Toggle(prefixedId);
      }
      else
      {
        // Single selection (clear others)
        _multiSelectState.SetSingle(prefixedId);
      }

      UpdateDatasetSelectionProperties();
      _multiSelectService.OnSelectionChanged(PanelId, _multiSelectState);
    }

    private void SelectAllDatasets()
    {
      if (_multiSelectState == null)
        return;

      _multiSelectState.Clear();
      foreach (var dataset in Datasets)
      {
        _multiSelectState.Add($"dataset_{dataset.Id}");
      }
      if (Datasets.Count > 0)
      {
        _multiSelectState.RangeAnchorId = $"dataset_{Datasets[0].Id}";
      }

      UpdateDatasetSelectionProperties();
      _multiSelectService.OnSelectionChanged(PanelId, _multiSelectState);
      SelectAllDatasetsCommand.NotifyCanExecuteChanged();
    }

    private void ClearDatasetSelection()
    {
      if (_multiSelectState == null)
        return;

      // Remove only dataset selections
      foreach (var id in _multiSelectState.SelectedIds.Where(id => id.StartsWith("dataset_")).ToList())
      {
        _multiSelectState.Remove(id);
      }
      if (_multiSelectState.SelectedIds.Count == 0)
      {
        _multiSelectState.RangeAnchorId = null;
      }

      UpdateDatasetSelectionProperties();
      _multiSelectService.OnSelectionChanged(PanelId, _multiSelectState);
    }

    private void UpdateDatasetSelectionProperties()
    {
      if (_multiSelectState == null)
      {
        SelectedDatasetCount = 0;
        HasMultipleDatasetSelection = false;
      }
      else
      {
        var datasetIds = _multiSelectState.SelectedIds.Where(id => id.StartsWith("dataset_")).ToList();
        SelectedDatasetCount = datasetIds.Count;
        HasMultipleDatasetSelection = datasetIds.Count > 1;
      }

      OnPropertyChanged(nameof(SelectedDatasetCount));
      OnPropertyChanged(nameof(HasMultipleDatasetSelection));
    }

    // Multi-select methods for training jobs
    public void ToggleTrainingJobSelection(string jobId, bool isCtrlPressed, bool isShiftPressed)
    {
      if (_multiSelectState == null)
        return;

      var prefixedId = $"job_{jobId}";

      if (isShiftPressed && !string.IsNullOrEmpty(_multiSelectState.RangeAnchorId) && _multiSelectState.RangeAnchorId.StartsWith("job_"))
      {
        // Range selection
        var allJobIds = TrainingJobs.Select(j => $"job_{j.Id}").ToList();
        _multiSelectState.SetRange(_multiSelectState.RangeAnchorId, prefixedId, allJobIds);
      }
      else if (isCtrlPressed)
      {
        // Toggle selection
        _multiSelectState.Toggle(prefixedId);
      }
      else
      {
        // Single selection (clear others)
        _multiSelectState.SetSingle(prefixedId);
      }

      UpdateTrainingJobSelectionProperties();
      _multiSelectService.OnSelectionChanged(PanelId, _multiSelectState);
    }

    private void SelectAllTrainingJobs()
    {
      if (_multiSelectState == null)
        return;

      // Don't clear all, just add training jobs
      foreach (var job in TrainingJobs)
      {
        var prefixedId = $"job_{job.Id}";
        if (!_multiSelectState.SelectedIds.Contains(prefixedId))
        {
          _multiSelectState.Add(prefixedId);
        }
      }
      if (TrainingJobs.Count > 0)
      {
        _multiSelectState.RangeAnchorId = $"job_{TrainingJobs[0].Id}";
      }

      UpdateTrainingJobSelectionProperties();
      _multiSelectService.OnSelectionChanged(PanelId, _multiSelectState);
      SelectAllTrainingJobsCommand.NotifyCanExecuteChanged();
    }

    private void ClearTrainingJobSelection()
    {
      if (_multiSelectState == null)
        return;

      // Remove only training job selections
      foreach (var id in _multiSelectState.SelectedIds.Where(id => id.StartsWith("job_")).ToList())
      {
        _multiSelectState.Remove(id);
      }
      if (_multiSelectState.SelectedIds.Count == 0)
      {
        _multiSelectState.RangeAnchorId = null;
      }

      UpdateTrainingJobSelectionProperties();
      _multiSelectService.OnSelectionChanged(PanelId, _multiSelectState);
    }

    private void UpdateTrainingJobSelectionProperties()
    {
      if (_multiSelectState == null)
      {
        SelectedTrainingJobCount = 0;
        HasMultipleTrainingJobSelection = false;
      }
      else
      {
        var jobIds = _multiSelectState.SelectedIds.Where(id => id.StartsWith("job_")).ToList();
        SelectedTrainingJobCount = jobIds.Count;
        HasMultipleTrainingJobSelection = jobIds.Count > 1;
      }

      OnPropertyChanged(nameof(SelectedTrainingJobCount));
      OnPropertyChanged(nameof(HasMultipleTrainingJobSelection));
    }

    #region Export Methods (GAP-004: Business logic moved from View code-behind)

    /// <summary>
    /// Serializes a dataset for export. The View handles file picker and file write.
    /// </summary>
    /// <param name="dataset">Dataset to export</param>
    /// <param name="format">Export format: "json" or "csv"</param>
    /// <returns>Serialized content string</returns>
    public string GetExportDatasetContent(TrainingDataset dataset, string format)
    {
      if (dataset == null)
        throw new ArgumentNullException(nameof(dataset));

      if (format.Equals("json", StringComparison.OrdinalIgnoreCase))
      {
        var jsonData = new
        {
          Name = dataset.Name,
          Description = dataset.Description,
          CreatedAt = dataset.Created,
          UpdatedAt = dataset.Modified,
          AudioCount = dataset.AudioFiles?.Count ?? 0,
          Duration = 0,
          Status = string.Empty
        };
        return System.Text.Json.JsonSerializer.Serialize(jsonData, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
      }
      else
      {
        // CSV format
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("Name,Description,CreatedAt,UpdatedAt,AudioCount,Duration,Status");
        sb.Append($"\"{dataset.Name}\",\"{dataset.Description ?? ""}\",\"{dataset.Created}\",\"{dataset.Modified}\",{dataset.AudioFiles?.Count ?? 0},0,\"\"");
        return sb.ToString();
      }
    }

    /// <summary>
    /// Serializes a training job for export. The View handles file picker and file write.
    /// </summary>
    /// <param name="job">Training job to export</param>
    /// <returns>Serialized JSON content string</returns>
    public string GetExportTrainingJobContent(TrainingStatus job)
    {
      if (job == null)
        throw new ArgumentNullException(nameof(job));

      var jobData = new
      {
        JobId = job.Id ?? "unknown",
        DatasetId = job.DatasetId ?? "unknown",
        ProfileId = job.ProfileId ?? "unknown",
        Engine = job.Engine ?? "unknown",
        Status = job.Status ?? "unknown",
        Progress = job.Progress,
        CurrentEpoch = job.CurrentEpoch,
        TotalEpochs = job.TotalEpochs,
        Started = job.Started?.ToString("o") ?? "not started",
        Completed = job.Completed?.ToString("o"),
        Loss = job.Loss,
        QualityScore = job.QualityScore,
        ErrorMessage = job.ErrorMessage
      };
      return System.Text.Json.JsonSerializer.Serialize(jobData, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
    }

    #endregion
  }
}