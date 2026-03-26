using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
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
  /// ViewModel for the UpscalingView panel - Image and video upscaling.
  /// </summary>
  public partial class UpscalingViewModel : BaseViewModel, IPanelView, IPanelLifecycle
  {
    private readonly IUpscalingClient _upscalingClient;
    private readonly ToastNotificationService? _toastNotificationService;

    public string PanelId => PanelIds.Upscaling;
    public string DisplayName => ResourceHelper.GetString("Panel.Upscaling.DisplayName", "Upscaling");
    public PanelRegion Region => PanelRegion.Center;

    [ObservableProperty]
    private ObservableCollection<UpscalingEngineItem> availableEngines = new();

    [ObservableProperty]
    private ObservableCollection<UpscalingJobItem> upscalingJobs = new();

    [ObservableProperty]
    private UpscalingEngineItem? selectedEngine;

    [ObservableProperty]
    private UpscalingJobItem? selectedJob;

    [ObservableProperty]
    private string selectedMediaType = "image";

    [ObservableProperty]
    private ObservableCollection<string> availableMediaTypes = new() { "image", "video" };

    [ObservableProperty]
    private double selectedScaleFactor = 2.0;

    [ObservableProperty]
    private ObservableCollection<double> availableScaleFactors = new() { 2.0, 4.0, 8.0 };

    [ObservableProperty]
    private string? selectedFilePath;

    [ObservableProperty]
    private bool isProcessing;

    [ObservableProperty]
    private string? outputFormat;

    [ObservableProperty]
    private double uploadProgress;

    [ObservableProperty]
    private bool isUploading;

    public UpscalingViewModel(IViewModelContext context, IUpscalingClient upscalingClient)
        : base(context)
    {
      _upscalingClient = upscalingClient ?? throw new ArgumentNullException(nameof(upscalingClient));

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

      LoadEnginesCommand = new EnhancedAsyncRelayCommand(async (ct) =>
      {
        using var profiler = PerformanceProfiler.StartCommand("LoadEngines");
        await LoadEnginesAsync(ct);
      }, () => !IsLoading);
      UpscaleCommand = new EnhancedAsyncRelayCommand(async (ct) =>
      {
        using var profiler = PerformanceProfiler.StartCommand("Upscale");
        await UpscaleAsync(ct);
      }, () => !IsProcessing && !string.IsNullOrWhiteSpace(SelectedFilePath) && SelectedEngine != null && !IsLoading);
      LoadJobsCommand = new EnhancedAsyncRelayCommand(async (ct) =>
      {
        using var profiler = PerformanceProfiler.StartCommand("LoadJobs");
        await LoadJobsAsync(ct);
      }, () => !IsLoading);
      DeleteJobCommand = new EnhancedAsyncRelayCommand(async (ct) =>
      {
        using var profiler = PerformanceProfiler.StartCommand("DeleteJob");
        await DeleteJobAsync(ct);
      }, () => SelectedJob != null && !IsLoading);
      RefreshCommand = new EnhancedAsyncRelayCommand(async (ct) =>
      {
        using var profiler = PerformanceProfiler.StartCommand("Refresh");
        await RefreshAsyncInternal(ct);
      }, () => !IsLoading);
    }

    public Task OnActivatedAsync(CancellationToken cancellationToken = default) => RefreshAsync(cancellationToken);
    public Task OnDeactivatedAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task RefreshAsync(CancellationToken cancellationToken) => RefreshAsyncInternal(cancellationToken);

    public IAsyncRelayCommand LoadEnginesCommand { get; }
    public IAsyncRelayCommand UpscaleCommand { get; }
    public IAsyncRelayCommand LoadJobsCommand { get; }
    public IAsyncRelayCommand DeleteJobCommand { get; }
    public IAsyncRelayCommand RefreshCommand { get; }

    partial void OnIsProcessingChanged(bool value)
    {
      UpscaleCommand.NotifyCanExecuteChanged();
    }

    partial void OnSelectedFilePathChanged(string? value)
    {
      UpscaleCommand.NotifyCanExecuteChanged();

      // Auto-detect media type from file extension
      if (!string.IsNullOrWhiteSpace(value))
      {
        var ext = Path.GetExtension(value).ToLowerInvariant();
        if (ext == ".jpg" || ext == ".jpeg" || ext == ".png" || ext == ".bmp" || ext == ".gif" || ext == ".webp")
        {
          SelectedMediaType = "image";
        }
        else if (ext == ".mp4" || ext == ".avi" || ext == ".mov" || ext == ".mkv" || ext == ".webm")
        {
          SelectedMediaType = "video";
        }
      }
    }

    partial void OnSelectedEngineChanged(UpscalingEngineItem? value)
    {
      ((System.Windows.Input.ICommand)UpscaleCommand).NotifyCanExecuteChanged();

      // Update available scale factors based on engine
      if (value != null)
      {
        AvailableScaleFactors.Clear();
        foreach (var scale in value.SupportedScales)
        {
          AvailableScaleFactors.Add(scale);
        }

        // Reset scale factor if current is not supported
        if (!AvailableScaleFactors.Contains(SelectedScaleFactor))
        {
          SelectedScaleFactor = AvailableScaleFactors.FirstOrDefault();
        }
      }
    }

    partial void OnSelectedJobChanged(UpscalingJobItem? value)
    {
      ((System.Windows.Input.ICommand)DeleteJobCommand).NotifyCanExecuteChanged();
    }

    partial void OnSelectedMediaTypeChanged(string value)
    {
      // Filter engines based on media type
      if (SelectedEngine != null && !SelectedEngine.SupportedTypes.Contains(value))
      {
        SelectedEngine = AvailableEngines.FirstOrDefault(e => e.SupportedTypes.Contains(value));
      }
    }

    private async Task LoadEnginesAsync(CancellationToken cancellationToken = default)
    {
      try
      {
        IsLoading = true;
        ErrorMessage = null;

        var engines = await _upscalingClient.GetEnginesAsync(cancellationToken);

        if (engines != null)
        {
          AvailableEngines.Clear();
          foreach (var engine in engines)
          {
            AvailableEngines.Add(new UpscalingEngineItem(engine));
          }

          // Select first available engine for current media type
          SelectedEngine = AvailableEngines.FirstOrDefault(e => e.SupportedTypes.Contains(SelectedMediaType));
          _toastNotificationService?.ShowInfo(
              ResourceHelper.FormatString("Upscaling.EnginesLoadedDetail", AvailableEngines.Count),
              ResourceHelper.GetString("Toast.Title.EnginesLoaded", "Engines Loaded"));
        }
      }
      catch (Exception ex)
      {
        ErrorMessage = ResourceHelper.FormatString("Upscaling.LoadEnginesFailed", ex.Message);
        _toastNotificationService?.ShowError(
            ResourceHelper.GetString("Toast.Title.LoadFailed", "Load Failed"),
            ResourceHelper.FormatString("Upscaling.LoadEnginesFailed", ex.Message));
      }
      finally
      {
        IsLoading = false;
      }
    }

    private async Task UpscaleAsync(CancellationToken cancellationToken)
    {
      if (string.IsNullOrWhiteSpace(SelectedFilePath) || SelectedEngine == null)
      {
        ErrorMessage = ResourceHelper.GetString("Upscaling.SelectionRequired", "Please select a file and engine");
        return;
      }

      if (!File.Exists(SelectedFilePath))
      {
        ErrorMessage = ResourceHelper.GetString("Upscaling.FileDoesNotExist", "Selected file does not exist");
        return;
      }

      // Validate file size (max 500MB for images, 2GB for videos)
      var fileInfo = new FileInfo(SelectedFilePath);
      var maxSize = SelectedMediaType == "image" ? 500 * 1024 * 1024L : 2L * 1024 * 1024 * 1024;
      if (fileInfo.Length > maxSize)
      {
        ErrorMessage = ResourceHelper.FormatString("Upscaling.FileSizeExceeded", maxSize / (1024.0 * 1024.0));
        return;
      }

      // Validate file format
      var ext = Path.GetExtension(SelectedFilePath).ToLowerInvariant();
      var validExtensions = SelectedMediaType == "image"
          ? new[] { ".jpg", ".jpeg", ".png", ".bmp", ".gif", ".webp" }
          : new[] { ".mp4", ".avi", ".mov", ".mkv", ".webm" };

      if (!validExtensions.Contains(ext))
      {
        ErrorMessage = ResourceHelper.FormatString("Upscaling.InvalidFileFormat", string.Join(", ", validExtensions));
        return;
      }

      IsProcessing = true;
      ErrorMessage = null;

      try
      {
        var upscaleRequest = new UpscalingUpscaleRequest
        {
          MediaType = SelectedMediaType,
          Engine = SelectedEngine.EngineId,
          ScaleFactor = SelectedScaleFactor,
          OutputFormat = OutputFormat
        };
        var jobResponse = await UploadFileAndUpscaleAsync(SelectedFilePath, upscaleRequest, cancellationToken);

        if (jobResponse != null)
        {
          await LoadJobsAsync(cancellationToken);
          StatusMessage = ResourceHelper.FormatString("Upscaling.UpscalingStarted", SelectedScaleFactor, SelectedMediaType);
          _toastNotificationService?.ShowSuccess(
              ResourceHelper.FormatString("Upscaling.UpscalingStartedDetail", SelectedScaleFactor, SelectedMediaType),
              ResourceHelper.GetString("Toast.Title.UpscalingStarted", "Upscaling Started"));
        }
      }
      catch (OperationCanceledException)
      {
        return; // User cancelled
      }
      catch (Exception ex)
      {
        await HandleErrorAsync(ex, "Upscale");
        _toastNotificationService?.ShowError(
            ResourceHelper.GetString("Toast.Title.StartFailed", "Start Failed"),
            ResourceHelper.FormatString("Upscaling.StartUpscalingFailed", ex.Message));
      }
      finally
      {
        IsProcessing = false;
      }
    }

    private async Task<UpscalingJobResponse?> UploadFileAndUpscaleAsync(string filePath, UpscalingUpscaleRequest request, CancellationToken cancellationToken = default)
    {
      IsUploading = true;
      UploadProgress = 0.0;

      try
      {
        var progress = new Progress<double>(p => UploadProgress = p);
        return await _upscalingClient.UploadAndUpscaleAsync(filePath, request, progress, cancellationToken);
      }
      finally
      {
        IsUploading = false;
        UploadProgress = 0.0;
      }
    }

    private async Task LoadJobsAsync(CancellationToken cancellationToken)
    {
      IsLoading = true;
      ErrorMessage = null;

      try
      {
        var jobs = await _upscalingClient.GetJobsAsync(cancellationToken);

        if (jobs != null)
        {
          UpscalingJobs.Clear();
          foreach (var job in jobs)
          {
            UpscalingJobs.Add(new UpscalingJobItem(job));
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

    private async Task DeleteJobAsync(CancellationToken cancellationToken = default)
    {
      if (SelectedJob == null)
      {
        return;
      }

      try
      {
        IsLoading = true;
        ErrorMessage = null;

        await _upscalingClient.DeleteJobAsync(SelectedJob.JobId, cancellationToken);

        UpscalingJobs.Remove(SelectedJob);
        SelectedJob = null;

        StatusMessage = ResourceHelper.GetString("Upscaling.JobDeleted", "Job deleted successfully");
        _toastNotificationService?.ShowSuccess(
            ResourceHelper.GetString("Upscaling.JobDeletedDetail", "Upscaling job deleted"),
            ResourceHelper.GetString("Toast.Title.JobDeleted", "Job Deleted"));
      }
      catch (Exception ex)
      {
        ErrorMessage = ResourceHelper.FormatString("Upscaling.DeleteJobFailed", ex.Message);
        _toastNotificationService?.ShowError(
            ResourceHelper.GetString("Toast.Title.DeleteFailed", "Delete Failed"),
            ResourceHelper.FormatString("Upscaling.DeleteJobFailed", ex.Message));
      }
      finally
      {
        IsLoading = false;
      }
    }

    private async Task RefreshAsyncInternal(CancellationToken cancellationToken)
    {
      try
      {
        await LoadEnginesAsync(cancellationToken);
        await LoadJobsAsync(cancellationToken);
        StatusMessage = ResourceHelper.GetString("Upscaling.Refreshed", "Refreshed");
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

  // Data models
  public class UpscalingEngineItem : ObservableObject
  {
    public string EngineId { get; set; }
    public string Name { get; set; }
    public string Description { get; set; }
    public string[] SupportedTypes { get; set; }
    public double[] SupportedScales { get; set; }
    public bool IsAvailable { get; set; }

    public string DisplayName => $"{Name} ({string.Join(", ", SupportedTypes)})";
    public string ScalesDisplay => string.Join("×, ", SupportedScales) + "×";

    public UpscalingEngineItem(UpscalingEngineResponse engine)
    {
      EngineId = engine.EngineId;
      Name = engine.Name;
      Description = engine.Description;
      SupportedTypes = engine.SupportedTypes;
      SupportedScales = engine.SupportedScales;
      IsAvailable = engine.IsAvailable;
    }
  }

  public class UpscalingJobItem : ObservableObject
  {
    public string JobId { get; set; }
    public string Status { get; set; }
    public double Progress { get; set; }
    public string? OutputFile { get; set; }
    public int? OriginalWidth { get; set; }
    public int? OriginalHeight { get; set; }
    public int? UpscaledWidth { get; set; }
    public int? UpscaledHeight { get; set; }
    public string? ErrorMessage { get; set; }

    public string ProgressDisplay => $"{Progress:F1}%";
    public string DimensionsDisplay => OriginalWidth.HasValue && OriginalHeight.HasValue
        ? $"{OriginalWidth}×{OriginalHeight} → {UpscaledWidth}×{UpscaledHeight}"
        : ResourceHelper.GetString("Upscaling.Unknown", "Unknown");

    public UpscalingJobItem(UpscalingJobResponse job)
    {
      JobId = job.JobId;
      Status = job.Status;
      Progress = job.Progress;
      OutputFile = job.OutputFile;
      OriginalWidth = job.OriginalWidth;
      OriginalHeight = job.OriginalHeight;
      UpscaledWidth = job.UpscaledWidth;
      UpscaledHeight = job.UpscaledHeight;
      ErrorMessage = job.ErrorMessage;
    }
  }
}