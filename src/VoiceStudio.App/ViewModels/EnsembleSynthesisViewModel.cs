using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VoiceStudio.Core.Panels;
using VoiceStudio.Core.Services;
using VoiceStudio.App.Services;
using VoiceStudio.App.Services.UndoableActions;
using VoiceStudio.Core.Models;
using VoiceStudio.App.Utilities;
using VoiceStudio.App.Logging;

namespace VoiceStudio.App.ViewModels
{
  /// <summary>
  /// ViewModel for the EnsembleSynthesisView panel - Multi-voice synthesis.
  /// </summary>
  public partial class EnsembleSynthesisViewModel : BaseViewModel, IPanelView, IPanelLifecycle
  {
    private readonly IEnsembleSynthesisClient _ensembleClient;
    private readonly IDialogService _dialogService;
    private readonly ToastNotificationService? _toastNotificationService;
    private readonly UndoRedoService? _undoRedoService;
    private readonly MultiSelectService _multiSelectService;
    private MultiSelectState? _multiSelectState;

    public string PanelId => PanelIds.EnsembleSynthesis;
    public string DisplayName => ResourceHelper.GetString("Panel.EnsembleSynthesis.DisplayName", "Ensemble Synthesis");
    public PanelRegion Region => PanelRegion.Center;

    [ObservableProperty]
    private ObservableCollection<EnsembleVoiceItem> voices = new();

    [ObservableProperty]
    private EnsembleVoiceItem? selectedVoice;

    [ObservableProperty]
    private ObservableCollection<EnsembleJobItem> jobs = new();

    [ObservableProperty]
    private EnsembleJobItem? selectedJob;

    [ObservableProperty]
    private string? selectedProjectId;

    [ObservableProperty]
    private string mixMode = "sequential";

    [ObservableProperty]
    private string outputFormat = "wav";

    [ObservableProperty]
    private ObservableCollection<string> availableProjects = new();

    [ObservableProperty]
    private ObservableCollection<string> availableProfiles = new();

    [ObservableProperty]
    private ObservableCollection<string> availableEngines = new();

    [ObservableProperty]
    private ObservableCollection<string> availableMixModes = new() { "sequential", "parallel", "layered" };

    [ObservableProperty]
    private ObservableCollection<string> availableOutputFormats = new() { "wav", "mp3", "flac" };

    // Multi-select support
    [ObservableProperty]
    private int selectedJobCount;

    [ObservableProperty]
    private bool hasMultipleJobSelection;

    // Quality metrics for synthesis results
    [ObservableProperty]
    private object? qualityMetrics;

    public bool IsJobSelected(string jobId) => _multiSelectState?.SelectedIds.Contains(jobId) ?? false;

    public EnsembleSynthesisViewModel(IViewModelContext context, IEnsembleSynthesisClient ensembleClient, IDialogService dialogService)
        : base(context)
    {
      _ensembleClient = ensembleClient ?? throw new ArgumentNullException(nameof(ensembleClient));
      _dialogService = dialogService ?? throw new ArgumentNullException(nameof(dialogService));

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

      // Get multi-select service
      var multiSelectService = AppServices.TryGetMultiSelectService();
      _multiSelectService = multiSelectService ?? throw new InvalidOperationException("MultiSelectService is required but not registered");
      _multiSelectState = _multiSelectService.GetState(PanelId);

      AddVoiceCommand = new EnhancedAsyncRelayCommand(async (ct) =>
      {
        using var profiler = PerformanceProfiler.StartCommand("AddVoice");
        await AddVoiceAsync(ct);
      }, () => !IsLoading);
      RemoveVoiceCommand = new EnhancedAsyncRelayCommand<EnsembleVoiceItem>(async (voice, ct) =>
      {
        using var profiler = PerformanceProfiler.StartCommand("RemoveVoice");
        await RemoveVoiceAsync(voice, ct);
      }, (voice) => voice != null && !IsLoading);
      SynthesizeCommand = new EnhancedAsyncRelayCommand(async (ct) =>
      {
        using var profiler = PerformanceProfiler.StartCommand("Synthesize");
        await SynthesizeAsync(ct);
      }, () => Voices.Count > 0 && !IsLoading);
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
      DeleteJobCommand = new EnhancedAsyncRelayCommand<EnsembleJobItem>(async (job, ct) =>
      {
        using var profiler = PerformanceProfiler.StartCommand("DeleteJob");
        await DeleteJobAsync(job, ct);
      }, (job) => job != null && !IsLoading);

      // Multi-select commands
      SelectAllJobsCommand = new RelayCommand(SelectAllJobs, () => Jobs?.Count > 0);
      ClearJobSelectionCommand = new RelayCommand(ClearJobSelection);
      DeleteSelectedJobsCommand = new EnhancedAsyncRelayCommand(async (ct) =>
      {
        using var profiler = PerformanceProfiler.StartCommand("DeleteSelectedJobs");
        await DeleteSelectedJobsAsync(ct);
      }, () => SelectedJobCount > 0 && !IsLoading);

      // Subscribe to selection changes
      _multiSelectService.SelectionChanged += (_, e) =>
      {
        if (e.PanelId == PanelId)
        {
          UpdateJobSelectionProperties();
          OnPropertyChanged(nameof(SelectedJobCount));
          OnPropertyChanged(nameof(HasMultipleJobSelection));
        }
      };
    }

    Task IPanelLifecycle.OnActivatedAsync(CancellationToken ct)
    {
      _ = LoadEnginesAsync(ct);
      _ = LoadJobsAsync(ct);
      return Task.CompletedTask;
    }

    Task IPanelLifecycle.OnDeactivatedAsync(CancellationToken ct) => Task.CompletedTask;

    async Task IPanelLifecycle.RefreshAsync(CancellationToken ct) => await RefreshAsync(ct);

    public IAsyncRelayCommand AddVoiceCommand { get; }
    public IAsyncRelayCommand<EnsembleVoiceItem> RemoveVoiceCommand { get; }
    public IAsyncRelayCommand SynthesizeCommand { get; }
    public IAsyncRelayCommand LoadJobsCommand { get; }
    public IAsyncRelayCommand RefreshCommand { get; }
    public IAsyncRelayCommand<EnsembleJobItem> DeleteJobCommand { get; }

    // Multi-select commands
    public IRelayCommand SelectAllJobsCommand { get; }
    public IRelayCommand ClearJobSelectionCommand { get; }
    public IAsyncRelayCommand DeleteSelectedJobsCommand { get; }

    private async Task AddVoiceAsync(CancellationToken cancellationToken)
    {
      try
      {
        var voice = new EnsembleVoiceItem
        {
          ProfileId = AvailableProfiles.FirstOrDefault() ?? "",
          Text = "",
          Engine = AvailableEngines.FirstOrDefault() ?? "xtts",
          Language = "en",
          Emotion = null
        };

        var insertIndex = Voices.Count;
        Voices.Add(voice);
        SelectedVoice = voice;

        // Register undo action
        if (_undoRedoService != null)
        {
          var action = new AddEnsembleVoiceAction(
              Voices,
              voice,
              insertIndex,
              onUndo: (v) =>
              {
                if (SelectedVoice == v)
                {
                  SelectedVoice = Voices.FirstOrDefault();
                }
              },
              onRedo: (v) => SelectedVoice = v);
          _undoRedoService.RegisterAction(action);
        }
      }
      catch (OperationCanceledException)
      {
        return; // User cancelled
      }
      catch (Exception ex)
      {
        await HandleErrorAsync(ex, "AddVoice");
      }
    }

    private async Task RemoveVoiceAsync(EnsembleVoiceItem? voice, CancellationToken cancellationToken)
    {
      if (voice == null)
        return;

      try
      {
        var originalIndex = Voices.IndexOf(voice);
        Voices.Remove(voice);
        var previousSelected = SelectedVoice;
        if (SelectedVoice == voice)
        {
          SelectedVoice = null;
        }

        // Register undo action
        if (_undoRedoService != null)
        {
          var action = new RemoveEnsembleVoiceAction(
              Voices,
              voice,
              originalIndex,
              onUndo: (v) => SelectedVoice = v,
              onRedo: (v) =>
              {
                if (SelectedVoice == v)
                {
                  SelectedVoice = Voices.FirstOrDefault();
                }
              });
          _undoRedoService.RegisterAction(action);
        }
      }
      catch (OperationCanceledException)
      {
        return; // User cancelled
      }
      catch (Exception ex)
      {
        await HandleErrorAsync(ex, "RemoveVoice");
      }
    }

    private async Task SynthesizeAsync(CancellationToken cancellationToken)
    {
      if (Voices.Count == 0)
      {
        ErrorMessage = ResourceHelper.GetString("EnsembleSynthesis.AtLeastOneVoiceRequired", "At least one voice is required");
        return;
      }

      IsLoading = true;
      ErrorMessage = null;

      try
      {
        var request = new EnsembleSynthesisRequest
        {
          Voices = Voices.Select(v => new EnsembleVoiceRequest
          {
            ProfileId = v.ProfileId,
            Text = v.Text,
            Engine = v.Engine,
            Language = v.Language,
            Emotion = v.Emotion
          }).ToArray(),
          ProjectId = SelectedProjectId,
          MixMode = MixMode,
          OutputFormat = OutputFormat
        };

        var response = await _ensembleClient.CreateSynthesisAsync(request, cancellationToken);

        if (response != null)
        {
          StatusMessage = response.Message;
          _toastNotificationService?.ShowSuccess(
              ResourceHelper.FormatString("EnsembleSynthesis.JobCreated", response.JobId),
              ResourceHelper.GetString("Toast.Title.EnsembleSynthesisStarted", "Ensemble Synthesis Started"));
          await LoadJobsAsync(cancellationToken);
        }
      }
      catch (OperationCanceledException)
      {
        return; // User cancelled
      }
      catch (Exception ex)
      {
        await HandleErrorAsync(ex, "Synthesize");
      }
      finally
      {
        IsLoading = false;
      }
    }

    private async Task LoadJobsAsync(CancellationToken cancellationToken)
    {
      IsLoading = true;
      ErrorMessage = null;

      try
      {
        var jobs = await _ensembleClient.ListJobsAsync(SelectedProjectId, cancellationToken);

        Jobs.Clear();
        if (jobs != null)
        {
          foreach (var job in jobs)
          {
            Jobs.Add(new EnsembleJobItem(job));
          }
          if (jobs.Length > 0)
          {
            _toastNotificationService?.ShowSuccess(
                ResourceHelper.FormatString("EnsembleSynthesis.JobsLoadedDetail", jobs.Length),
                ResourceHelper.GetString("Toast.Title.JobsLoaded", "Jobs Loaded"));
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

    private async Task LoadEnginesAsync(CancellationToken cancellationToken)
    {
      try
      {
        var engines = await _ensembleClient.GetEnginesAsync(cancellationToken);
        AvailableEngines.Clear();
        foreach (var e in engines)
          AvailableEngines.Add(e);
      }
      catch (OperationCanceledException) { Debug.WriteLine("EnsembleSynthesisViewModel: LoadEnginesAsync cancelled"); }
      catch (Exception ex) { ErrorLogger.LogWarning($"Best effort operation failed: {ex.Message}", "EnsembleSynthesisViewModel.LoadEnginesAsync"); }
    }

    private async Task RefreshAsync(CancellationToken cancellationToken)
    {
      try
      {
        await LoadEnginesAsync(cancellationToken);
        await LoadJobsAsync(cancellationToken);
        StatusMessage = ResourceHelper.GetString("EnsembleSynthesis.JobsRefreshed", "Jobs refreshed");
        _toastNotificationService?.ShowSuccess(
            ResourceHelper.GetString("EnsembleSynthesis.JobsRefreshedSuccessfully", "Jobs refreshed successfully"),
            ResourceHelper.GetString("Toast.Title.Refreshed", "Refreshed"));
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

    private async Task DeleteJobAsync(EnsembleJobItem? job, CancellationToken cancellationToken)
    {
      if (job == null)
        return;

      IsLoading = true;
      ErrorMessage = null;

      try
      {
        await _ensembleClient.DeleteJobAsync(job.JobId, cancellationToken);

        Jobs.Remove(job);
        StatusMessage = ResourceHelper.GetString("EnsembleSynthesis.JobDeleted", "Job deleted");
        _toastNotificationService?.ShowSuccess(
            ResourceHelper.FormatString("EnsembleSynthesis.JobDeletedDetail", job.JobId),
            ResourceHelper.GetString("Toast.Title.JobDeleted", "Job Deleted"));
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

    public void ToggleJobSelection(string jobId, bool isCtrlPressed, bool isShiftPressed)
    {
      if (_multiSelectState == null)
        return;

      if (isShiftPressed && !string.IsNullOrEmpty(_multiSelectState.RangeAnchorId))
      {
        // Range selection
        var allIds = Jobs.Select(j => j.JobId).ToList();
        _multiSelectState.SetRange(_multiSelectState.RangeAnchorId, jobId, allIds);
      }
      else if (isCtrlPressed)
      {
        // Toggle selection
        _multiSelectState.Toggle(jobId);
        if (!_multiSelectState.SelectedIds.Contains(jobId))
        {
          _multiSelectState.RangeAnchorId = null;
        }
        else if (_multiSelectState.RangeAnchorId == null)
        {
          _multiSelectState.RangeAnchorId = jobId;
        }
      }
      else
      {
        // Single selection
        _multiSelectState.SetSingle(jobId);
      }

      UpdateJobSelectionProperties();
      _multiSelectService.OnSelectionChanged(PanelId, _multiSelectState);
    }

    private void SelectAllJobs()
    {
      if (_multiSelectState == null)
        return;

      _multiSelectState.Clear();
      foreach (var job in Jobs)
      {
        _multiSelectState.Add(job.JobId);
      }
      UpdateJobSelectionProperties();
      _multiSelectService.OnSelectionChanged(PanelId, _multiSelectState);
    }

    private void ClearJobSelection()
    {
      if (_multiSelectState == null)
        return;

      _multiSelectState.Clear();
      UpdateJobSelectionProperties();
      _multiSelectService.OnSelectionChanged(PanelId, _multiSelectState);
      ((System.Windows.Input.ICommand)DeleteSelectedJobsCommand).NotifyCanExecuteChanged();
    }

    private async Task DeleteSelectedJobsAsync(CancellationToken cancellationToken)
    {
      if (_multiSelectState == null || _multiSelectState.SelectedIds.Count == 0)
        return;

      var selectedIds = new System.Collections.Generic.List<string>(_multiSelectState.SelectedIds);

      // Show confirmation dialog (Panel Hardening: IDialogService per PANEL_HARDENING_PATTERN)
      var confirmed = await _dialogService.ShowConfirmationAsync(
          "Delete jobs?",
          $"Are you sure you want to delete '{selectedIds.Count} job(s)'? This action cannot be undone.",
          "Delete",
          "Cancel");

      if (!confirmed)
        return;

      IsLoading = true;
      ErrorMessage = null;

      try
      {
        var jobsToDelete = new System.Collections.Generic.List<EnsembleJobItem>();
        int deletedCount = 0;

        foreach (var jobId in selectedIds)
        {
          cancellationToken.ThrowIfCancellationRequested();

          try
          {
            await _ensembleClient.DeleteJobAsync(jobId, cancellationToken);

            var job = Jobs.FirstOrDefault(j => j.JobId == jobId);
            if (job != null)
            {
              jobsToDelete.Add(job);
              Jobs.Remove(job);
              if (SelectedJob?.JobId == jobId)
              {
                SelectedJob = null;
              }
              deletedCount++;
            }
          }
          catch (OperationCanceledException)
          {
            throw; // Re-throw cancellation to abort batch deletion
          }
          catch (Exception ex)
      {
        ErrorLogger.LogWarning($"Best effort operation failed: {ex.Message}", "EnsembleSynthesisViewModel.DeleteSelectedJobsAsync");
      }
        }

        // Clear selection after deletion
        ClearJobSelection();

        // Show success toast
        if (deletedCount > 0)
        {
          StatusMessage = ResourceHelper.FormatString("EnsembleSynthesis.JobsDeleted", deletedCount);
          _toastNotificationService?.ShowSuccess(
              ResourceHelper.FormatString("EnsembleSynthesis.JobsDeletedDetail", deletedCount),
              ResourceHelper.GetString("Toast.Title.BatchDeleteComplete", "Batch Delete Complete"));
        }
        if (deletedCount < selectedIds.Count)
        {
          _toastNotificationService?.ShowWarning($"Some jobs could not be deleted ({deletedCount}/{selectedIds.Count} succeeded)", "Partial Delete");
        }
      }
      catch (OperationCanceledException)
      {
        return; // User cancelled
      }
      catch (Exception ex)
      {
        await HandleErrorAsync(ex, "DeleteSelectedJobs");
      }
      finally
      {
        IsLoading = false;
      }
    }

    private void UpdateJobSelectionProperties()
    {
      if (_multiSelectState == null)
      {
        SelectedJobCount = 0;
        HasMultipleJobSelection = false;
      }
      else
      {
        SelectedJobCount = _multiSelectState.Count;
        HasMultipleJobSelection = _multiSelectState.Count > 1;
      }
        ((System.Windows.Input.ICommand)DeleteSelectedJobsCommand).NotifyCanExecuteChanged();
    }

  }

  // Data models
  public class EnsembleVoiceItem : ObservableObject
  {
    public string ProfileId { get; set; } = string.Empty;
    public string Text { get; set; } = string.Empty;
    public string Engine { get; set; } = "xtts";
    public string Language { get; set; } = "en";
    public string? Emotion { get; set; }
  }

  public class EnsembleJobItem : ObservableObject
  {
    public string JobId { get; set; }
    public string Status { get; set; }
    public double Progress { get; set; }
    public int CompletedVoices { get; set; }
    public int TotalVoices { get; set; }
    public string[] AudioIds { get; set; }
    public string? Error { get; set; }
    public string Created { get; set; }
    public string Updated { get; set; }
    public string ProgressDisplay => $"{Progress:P0}";
    public string VoicesDisplay => $"{CompletedVoices}/{TotalVoices} voices";

    public EnsembleJobItem(VoiceStudio.App.Services.EnsembleJobStatus job)
    {
      JobId = job.JobId;
      Status = job.Status;
      Progress = job.Progress;
      CompletedVoices = job.CompletedVoices;
      TotalVoices = job.TotalVoices;
      AudioIds = job.AudioIds;
      Error = job.Error;
      Created = job.Created;
      Updated = job.Updated;
    }
  }
}