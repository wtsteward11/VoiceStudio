using System;
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
using Microsoft.UI.Dispatching;

namespace VoiceStudio.App.ViewModels
{
  /// <summary>
  /// ViewModel for the JobProgressView panel - Unified job progress monitor.
  /// </summary>
  public partial class JobProgressViewModel : BaseViewModel, IPanelView, IPanelLifecycle
  {
    private readonly IJobProgressApiClient _jobProgressApiClient;
    private readonly JobProgressWebSocketClient? _webSocketClient;
    private CancellationTokenSource? _pollingCts;
    private bool _isPolling;
    private bool _hasActivated;
    private DispatcherQueue? _dispatcherQueue;

    public string PanelId => PanelIds.JobProgress;
    public string DisplayName => ResourceHelper.GetString("Panel.JobProgress.DisplayName", "Job Progress");
    public PanelRegion Region => PanelRegion.Right;

    [ObservableProperty]
    private ObservableCollection<JobItem> jobs = new();

    [ObservableProperty]
    private JobItem? selectedJob;

    [ObservableProperty]
    private string? selectedJobType;

    [ObservableProperty]
    private string? selectedStatus;

    [ObservableProperty]
    private ObservableCollection<string> availableJobTypes = new();

    [ObservableProperty]
    private ObservableCollection<string> availableStatuses = new();

    [ObservableProperty]
    private JobSummary? summary;

    [ObservableProperty]
    private bool autoRefresh = true;

    public JobProgressViewModel(IViewModelContext context, IJobProgressApiClient jobProgressApiClient, IWebSocketService? webSocketService = null)
        : base(context)
    {
      _jobProgressApiClient = jobProgressApiClient ?? throw new ArgumentNullException(nameof(jobProgressApiClient));
      _dispatcherQueue = DispatcherQueue.GetForCurrentThread();

      // Initialize WebSocket client if available (ConnectAsync deferred to OnActivatedAsync)
      if (webSocketService != null)
      {
        _webSocketClient = new JobProgressWebSocketClient(webSocketService);
        _webSocketClient.ProgressUpdated += OnJobProgressUpdated;
        _webSocketClient.StatusChanged += OnJobStatusChanged;
        _webSocketClient.JobCompleted += OnJobCompleted;
        _webSocketClient.JobFailed += OnJobFailed;
      }

      LoadJobsCommand = new EnhancedAsyncRelayCommand(async (ct) =>
      {
        using var profiler = PerformanceProfiler.StartCommand("LoadJobs");
        await LoadJobsAsync(ct);
      }, () => !IsLoading);
      RefreshCommand = new EnhancedAsyncRelayCommand(async (ct) =>
      {
        using var profiler = PerformanceProfiler.StartCommand("Refresh");
        await RefreshAsync(ct);
      }, () => !IsLoading);
      CancelJobCommand = new EnhancedAsyncRelayCommand<JobItem>(async (job, ct) =>
      {
        using var profiler = PerformanceProfiler.StartCommand("CancelJob");
        await CancelJobAsync(job, ct);
      }, (job) => job != null && !IsLoading);
      PauseJobCommand = new EnhancedAsyncRelayCommand<JobItem>(async (job, ct) =>
      {
        using var profiler = PerformanceProfiler.StartCommand("PauseJob");
        await PauseJobAsync(job, ct);
      }, (job) => job != null && !IsLoading);
      ResumeJobCommand = new EnhancedAsyncRelayCommand<JobItem>(async (job, ct) =>
      {
        using var profiler = PerformanceProfiler.StartCommand("ResumeJob");
        await ResumeJobAsync(job, ct);
      }, (job) => job != null && !IsLoading);
      DeleteJobCommand = new EnhancedAsyncRelayCommand<JobItem>(async (job, ct) =>
      {
        using var profiler = PerformanceProfiler.StartCommand("DeleteJob");
        await DeleteJobAsync(job, ct);
      }, (job) => job != null && !IsLoading);
      ClearCompletedCommand = new EnhancedAsyncRelayCommand(async (ct) =>
      {
        using var profiler = PerformanceProfiler.StartCommand("ClearCompleted");
        await ClearCompletedAsync(ct);
      }, () => !IsLoading);
      LoadSummaryCommand = new EnhancedAsyncRelayCommand(async (ct) =>
      {
        using var profiler = PerformanceProfiler.StartCommand("LoadSummary");
        await LoadSummaryAsync(ct);
      }, () => !IsLoading);

      // Initialize available filters
      AvailableJobTypes.Add(ResourceHelper.GetString("JobProgress.FilterAll", "All"));
      AvailableJobTypes.Add("batch");
      AvailableJobTypes.Add("training");
      AvailableJobTypes.Add("synthesis");
      AvailableJobTypes.Add("export");
      AvailableJobTypes.Add("import");
      AvailableJobTypes.Add("other");

      AvailableStatuses.Add("All");
      AvailableStatuses.Add("pending");
      AvailableStatuses.Add("running");
      AvailableStatuses.Add("completed");
      AvailableStatuses.Add("failed");
      AvailableStatuses.Add("cancelled");
      AvailableStatuses.Add("paused");

      selectedJobType = ResourceHelper.GetString("JobProgress.FilterAll", "All");
      selectedStatus = ResourceHelper.GetString("JobProgress.FilterAll", "All");

      // No constructor fire-and-forget (ADR-047). Initial load deferred to OnActivatedAsync.
    }

    async Task IPanelLifecycle.OnActivatedAsync(CancellationToken cancellationToken)
    {
      _hasActivated = true;
      await LoadSummaryAsync(cancellationToken);
      await LoadJobsAsync(cancellationToken);
      if (_webSocketClient != null)
      {
        _ = _webSocketClient.ConnectAsync();
      }
      else
      {
        StartPolling();
      }
    }

    Task IPanelLifecycle.OnDeactivatedAsync(CancellationToken cancellationToken)
    {
      StopPolling();
      return Task.CompletedTask;
    }

    async Task IPanelLifecycle.RefreshAsync(CancellationToken cancellationToken) => await RefreshAsync(cancellationToken);

    private void OnJobProgressUpdated(object? sender, JobProgressUpdate update)
    {
      // Update job progress on UI thread
      _dispatcherQueue?.TryEnqueue(() =>
        {
          var job = Jobs.FirstOrDefault(j => j.Id == update.JobId);
          if (job != null)
          {
            job.Progress = update.Progress;
            job.ProgressDisplay = $"{update.Progress * 100.0:F1}%";
            if (!string.IsNullOrEmpty(update.Message))
            {
              job.CurrentStep = update.Message;
            }
          }
          else
          {
            // Job not in list, refresh to get it
            _ = LoadJobsAsync(CancellationToken.None);
          }
        });
    }

    private void OnJobStatusChanged(object? sender, JobStatusUpdate update)
    {
      // Update job status on UI thread
      _dispatcherQueue?.TryEnqueue(() =>
        {
          var job = Jobs.FirstOrDefault(j => j.Id == update.JobId);
          if (job != null)
          {
            job.Status = update.Status;
            if (!string.IsNullOrEmpty(update.Message))
            {
              job.CurrentStep = update.Message;
            }
          }
          else
          {
            // Job not in list, refresh to get it
            _ = LoadJobsAsync(CancellationToken.None);
          }
          // Refresh summary when status changes
          _ = LoadSummaryAsync(CancellationToken.None);
        });
    }

    private void OnJobCompleted(object? sender, JobCompletedUpdate update)
    {
      // Update job completion on UI thread
      _dispatcherQueue?.TryEnqueue(() =>
        {
          var job = Jobs.FirstOrDefault(j => j.Id == update.JobId);
          if (job != null)
          {
            job.Status = ResourceHelper.GetString("JobProgress.StatusCompleted", "completed");
            job.Progress = 1.0;
            job.ProgressDisplay = "100%";
            if (!string.IsNullOrEmpty(update.ResultId))
            {
              job.ResultId = update.ResultId;
            }
          }
          // Refresh summary and jobs list
          _ = LoadSummaryAsync(CancellationToken.None);
          _ = LoadJobsAsync(CancellationToken.None);
        });
    }

    private void OnJobFailed(object? sender, JobFailedUpdate update)
    {
      // Update job failure on UI thread
      _dispatcherQueue?.TryEnqueue(() =>
        {
          var job = Jobs.FirstOrDefault(j => j.Id == update.JobId);
          if (job != null)
          {
            job.Status = ResourceHelper.GetString("JobProgress.StatusFailed", "failed");
            job.ErrorMessage = update.Error;
          }
          // Refresh summary and jobs list
          _ = LoadSummaryAsync(CancellationToken.None);
          _ = LoadJobsAsync(CancellationToken.None);
        });
    }

    public IAsyncRelayCommand LoadJobsCommand { get; }
    public IAsyncRelayCommand RefreshCommand { get; }
    public IAsyncRelayCommand<JobItem> CancelJobCommand { get; }
    public IAsyncRelayCommand<JobItem> PauseJobCommand { get; }
    public IAsyncRelayCommand<JobItem> ResumeJobCommand { get; }
    public IAsyncRelayCommand<JobItem> DeleteJobCommand { get; }
    public IAsyncRelayCommand ClearCompletedCommand { get; }
    public IAsyncRelayCommand LoadSummaryCommand { get; }

    private void StartPolling()
    {
      if (_isPolling)
        return;

      _isPolling = true;
      _pollingCts = new CancellationTokenSource();
      _ = PollJobsAsync(_pollingCts.Token);
    }

    private void StopPolling()
    {
      _isPolling = false;
      _pollingCts?.Cancel();
      _pollingCts?.Dispose();
      _pollingCts = null;
    }

    private async Task PollJobsAsync(CancellationToken cancellationToken)
    {
      while (!cancellationToken.IsCancellationRequested && _isPolling && AutoRefresh)
      {
        try
        {
          await LoadJobsAsync(cancellationToken);
          await LoadSummaryAsync(cancellationToken);
          await Task.Delay(2000, cancellationToken); // Poll every 2 seconds
        }
        catch (OperationCanceledException)
        {
          break;
        }
        catch (Exception ex)
        {
          System.Diagnostics.Debug.WriteLine($"Error polling jobs: {ex.Message}");
          await Task.Delay(5000, cancellationToken); // Wait longer on error
        }
      }
    }

    private async Task LoadJobsAsync(CancellationToken cancellationToken)
    {
      IsLoading = true;
      ErrorMessage = null;

      try
      {
        var allFilter = ResourceHelper.GetString("Filter.All", "All");
        var jobs = await _jobProgressApiClient.GetJobsAsync(
            string.IsNullOrEmpty(SelectedJobType) || SelectedJobType == allFilter ? null : SelectedJobType,
            string.IsNullOrEmpty(SelectedStatus) || SelectedStatus == allFilter ? null : SelectedStatus,
            cancellationToken
        );

        Jobs.Clear();
        if (jobs != null)
        {
          foreach (var job in jobs)
          {
            Jobs.Add(new JobItem(job));
          }
        }
      }
      catch (OperationCanceledException)
      {
        return; // User cancelled
      }
      catch (Exception ex)
      {
        await HandleErrorAsync(ex, "LoadJobs");
      }
      finally
      {
        IsLoading = false;
      }
    }

    private async Task RefreshAsync(CancellationToken cancellationToken)
    {
      await LoadJobsAsync(cancellationToken);
      await LoadSummaryAsync(cancellationToken);
      StatusMessage = ResourceHelper.GetString("JobProgress.JobsRefreshed", "Jobs refreshed");
    }

    private async Task LoadSummaryAsync(CancellationToken cancellationToken)
    {
      try
      {
        Summary = await _jobProgressApiClient.GetJobSummaryAsync(cancellationToken);
      }
      catch (Exception ex)
      {
        ErrorMessage = ResourceHelper.FormatString("JobProgress.LoadSummaryFailed", ex.Message);
      }
    }

    private async Task CancelJobAsync(JobItem? job, CancellationToken cancellationToken)
    {
      if (job == null)
        return;

      IsLoading = true;
      ErrorMessage = null;

      try
      {
        await _jobProgressApiClient.CancelJobAsync(job.Id, cancellationToken);

        await LoadJobsAsync(cancellationToken);
        await LoadSummaryAsync(cancellationToken);
        StatusMessage = ResourceHelper.FormatString("JobProgress.JobCancelled", job.Name);
      }
      catch (OperationCanceledException)
      {
        return; // User cancelled
      }
      catch (Exception ex)
      {
        await HandleErrorAsync(ex, "CancelJob");
      }
      finally
      {
        IsLoading = false;
      }
    }

    private async Task PauseJobAsync(JobItem? job, CancellationToken cancellationToken)
    {
      if (job == null)
        return;

      IsLoading = true;
      ErrorMessage = null;

      try
      {
        await _jobProgressApiClient.PauseJobAsync(job.Id, cancellationToken);

        await LoadJobsAsync(cancellationToken);
        StatusMessage = $"Job '{job.Name}' paused";
      }
      catch (OperationCanceledException)
      {
        return; // User cancelled
      }
      catch (Exception ex)
      {
        await HandleErrorAsync(ex, "PauseJob");
      }
      finally
      {
        IsLoading = false;
      }
    }

    private async Task ResumeJobAsync(JobItem? job, CancellationToken cancellationToken)
    {
      if (job == null)
        return;

      try
      {
        IsLoading = true;
        ErrorMessage = null;

        await _jobProgressApiClient.ResumeJobAsync(job.Id, cancellationToken);

        await LoadJobsAsync(cancellationToken);
        StatusMessage = ResourceHelper.FormatString("JobProgress.JobResumed", job.Name);
      }
      catch (Exception ex)
      {
        ErrorMessage = ResourceHelper.FormatString("JobProgress.ResumeJobFailed", ex.Message);
      }
      finally
      {
        IsLoading = false;
      }
    }

    private async Task DeleteJobAsync(JobItem? job, CancellationToken cancellationToken)
    {
      if (job == null)
        return;

      IsLoading = true;
      ErrorMessage = null;

      try
      {
        await _jobProgressApiClient.DeleteJobAsync(job.Id, cancellationToken);

        Jobs.Remove(job);
        await LoadSummaryAsync(cancellationToken);
        StatusMessage = ResourceHelper.FormatString("JobProgress.JobDeleted", job.Name);
      }
      catch (OperationCanceledException)
      {
        return; // User cancelled
      }
      catch (Exception ex)
      {
        await HandleErrorAsync(ex, "DeleteJob");
      }
      finally
      {
        IsLoading = false;
      }
    }

    private async Task ClearCompletedAsync(CancellationToken cancellationToken)
    {
      try
      {
        IsLoading = true;
        ErrorMessage = null;

        await _jobProgressApiClient.ClearCompletedJobsAsync(cancellationToken);

        await LoadJobsAsync(cancellationToken);
        await LoadSummaryAsync(cancellationToken);
        StatusMessage = ResourceHelper.GetString("JobProgress.CompletedJobsCleared", "Completed jobs cleared");
      }
      catch (Exception ex)
      {
        ErrorMessage = ResourceHelper.FormatString("JobProgress.ClearCompletedJobsFailed", ex.Message);
      }
      finally
      {
        IsLoading = false;
      }
    }

    partial void OnAutoRefreshChanged(bool value)
    {
      // When auto-refresh is enabled, ensure WebSocket is connected or polling is active
      if (value)
      {
        if (_webSocketClient != null && !_webSocketClient.IsConnected)
        {
          _ = _webSocketClient.ConnectAsync();
        }
        else if (_webSocketClient == null && !_isPolling)
        {
          StartPolling();
        }
      }
      else
      {
        // When auto-refresh is disabled, stop polling (but keep WebSocket connected for manual refresh)
        if (_isPolling)
        {
          StopPolling();
        }
      }
    }

    partial void OnSelectedJobTypeChanged(string? value)
    {
      if (!_hasActivated)
        return;
      _ = LoadJobsAsync(CancellationToken.None);
    }

    partial void OnSelectedStatusChanged(string? value)
    {
      if (!_hasActivated)
        return;
      _ = LoadJobsAsync(CancellationToken.None);
    }

    /// <summary>
    /// Notify commands when IsLoading changes to update their CanExecute state.
    /// </summary>
    protected override void OnPropertyChanged(System.ComponentModel.PropertyChangedEventArgs e)
    {
      base.OnPropertyChanged(e);
      
      if (e.PropertyName == nameof(IsLoading))
      {
        // Notify all commands that depend on IsLoading
        LoadJobsCommand.NotifyCanExecuteChanged();
        RefreshCommand.NotifyCanExecuteChanged();
        CancelJobCommand.NotifyCanExecuteChanged();
        PauseJobCommand.NotifyCanExecuteChanged();
        ResumeJobCommand.NotifyCanExecuteChanged();
        DeleteJobCommand.NotifyCanExecuteChanged();
        ClearCompletedCommand.NotifyCanExecuteChanged();
        LoadSummaryCommand.NotifyCanExecuteChanged();
      }
    }

    protected override void Dispose(bool disposing)
    {
      if (IsDisposed)
      {
        return;
      }

      if (disposing)
      {
        StopPolling();
        
        // Unsubscribe from WebSocket events before disposing to prevent callbacks on disposed object
        if (_webSocketClient != null)
        {
          _webSocketClient.ProgressUpdated -= OnJobProgressUpdated;
          _webSocketClient.StatusChanged -= OnJobStatusChanged;
          _webSocketClient.JobCompleted -= OnJobCompleted;
          _webSocketClient.JobFailed -= OnJobFailed;
          _webSocketClient.Dispose();
        }
      }

      base.Dispose(disposing);
    }

    // Data models
    public class JobItem : ObservableObject
    {
      public string Id { get; set; }
      public string Name { get; set; }
      public string Type { get; set; }

      private string _status = "";
      public string Status { get => _status; set => SetProperty(ref _status, value); }

      private double _progress;
      public double Progress { get => _progress; set => SetProperty(ref _progress, value); }

      private string _progressDisplay = "";
      public string ProgressDisplay { get => _progressDisplay; set => SetProperty(ref _progressDisplay, value); }

      private string? _currentStep;
      public string? CurrentStep { get => _currentStep; set => SetProperty(ref _currentStep, value); }

      private string? _stepDisplay;
      public string? StepDisplay { get => _stepDisplay; set => SetProperty(ref _stepDisplay, value); }

      public string Created { get; set; }
      public string? Started { get; set; }
      public string? Completed { get; set; }

      private string? _estimatedTimeRemaining;
      public string? EstimatedTimeRemaining { get => _estimatedTimeRemaining; set => SetProperty(ref _estimatedTimeRemaining, value); }

      private string? _errorMessage;
      public string? ErrorMessage { get => _errorMessage; set => SetProperty(ref _errorMessage, value); }

      public string? ResultId { get; set; }

      public JobItem(Job job)
      {
        Id = job.Id;
        Name = job.Name;
        Type = job.Type;
        Status = job.Status;
        Progress = job.Progress;
        ProgressDisplay = $"{job.Progress * 100.0:F1}%";
        CurrentStep = job.CurrentStep;
        StepDisplay = job.CurrentStepIndex.HasValue && job.TotalSteps.HasValue
            ? $"{job.CurrentStepIndex.Value + 1}/{job.TotalSteps.Value}"
            : null;
        Created = job.Created;
        Started = job.Started;
        Completed = job.Completed;
        EstimatedTimeRemaining = job.EstimatedTimeRemaining.HasValue
            ? FormatTime(job.EstimatedTimeRemaining.Value)
            : null;
        ErrorMessage = job.ErrorMessage;
        ResultId = job.ResultId;
      }

      private static string FormatTime(int seconds)
      {
        if (seconds < 60)
          return $"{seconds}s";
        if (seconds < 3600)
          return $"{seconds / 60}m {seconds % 60}s";
        var hours = seconds / 3600;
        var minutes = seconds % 3600 / 60;
        return $"{hours}h {minutes}m";
      }
    }
  }
}