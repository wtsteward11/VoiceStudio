using System;
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
using VoiceStudio.App.Services.UndoableActions;
using VoiceStudio.App.Utilities;
using VoiceStudio.App.ViewModels;
using Windows.Storage;
using Windows.Storage.Pickers;

namespace VoiceStudio.App.Views.Panels
{
  public partial class ModelManagerViewModel : BaseViewModel, IPanelView
  {
    private readonly IModelManagerClient _modelManagerClient;
    private readonly IJobProgressApiClient? _jobProgressClient;
    private readonly UndoRedoService? _undoRedoService;
    private CancellationTokenSource? _downloadPollCts;

    public string PanelId => PanelIds.ModelManager;
    public string DisplayName => ResourceHelper.GetString("Panel.ModelManager.DisplayName", "Model Manager");
    public PanelRegion Region => PanelRegion.Right;

    [ObservableProperty]
    private ObservableCollection<ModelInfo> models = new();

    [ObservableProperty]
    private ModelInfo? selectedModel;

    [ObservableProperty]
    private string? selectedEngine;

    [ObservableProperty]
    private bool isLoading;

    [ObservableProperty]
    private string? errorMessage;

    [ObservableProperty]
    private string? statusMessage;

    [ObservableProperty]
    private StorageStats? storageStats;

    [ObservableProperty]
    private bool isVerifying;

    [ObservableProperty]
    private string? verificationResult;

    [ObservableProperty]
    private bool isDownloading;

    [ObservableProperty]
    private string downloadUrl = "";

    [ObservableProperty]
    private string downloadModelName = "";

    [ObservableProperty]
    private string downloadVersion = "1.0";

    [ObservableProperty]
    private string? downloadExpectedSha256;

    [ObservableProperty]
    private string? downloadTargetEngine;

    [ObservableProperty]
    private string? activeDownloadJobId;

    [ObservableProperty]
    private double downloadJobProgress;

    [ObservableProperty]
    private string? downloadJobStatus;

    // CS0108 fix: Intentionally hiding base HasError with local ErrorMessage binding
    public new bool HasError => !string.IsNullOrEmpty(ErrorMessage);

    public ObservableCollection<string> Engines { get; } = new()
        {
            "xtts_v2",
            "chatterbox",
            "tortoise",
            "piper",
            "openvoice",
            "sdxl",
            "realesrgan",
            "svd"
        };

    public ModelManagerViewModel(IViewModelContext context, IModelManagerClient modelManagerClient, IJobProgressApiClient? jobProgressClient = null)
        : base(context)
    {
      _modelManagerClient = modelManagerClient ?? throw new ArgumentNullException(nameof(modelManagerClient));
      _jobProgressClient = jobProgressClient;

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

      LoadModelsCommand = new EnhancedAsyncRelayCommand(async (ct) => await LoadModelsAsync(ct), () => !IsLoading);
      RefreshCommand = new EnhancedAsyncRelayCommand(async (ct) => await RefreshAsync(ct), () => !IsLoading);
      VerifyModelCommand = new EnhancedAsyncRelayCommand<ModelInfo>(async (model, ct) => await VerifyModelAsync(model, ct), model => model != null && !IsVerifying);
      UpdateChecksumCommand = new EnhancedAsyncRelayCommand<ModelInfo>(async (model, ct) => await UpdateChecksumAsync(model, ct), model => model != null && !IsLoading);
      DeleteModelCommand = new EnhancedAsyncRelayCommand<ModelInfo>(async (model, ct) => await DeleteModelAsync(model, ct), model => model != null && !IsLoading);
      LoadStorageStatsCommand = new EnhancedAsyncRelayCommand(async (ct) => await LoadStorageStatsAsync(ct), () => !IsLoading);
      ExportModelCommand = new EnhancedAsyncRelayCommand<ModelInfo>(async (model, ct) => await ExportModelAsync(model, ct), model => model != null && !IsLoading);
      ImportModelCommand = new EnhancedAsyncRelayCommand(async (ct) => await ImportModelAsync(ct), () => !IsLoading);
      DownloadTargetEngine = Engines.Count > 0 ? Engines[0] : "xtts_v2";
      StartModelDownloadCommand = new EnhancedAsyncRelayCommand(async (ct) => await StartModelDownloadAsync(ct), () => !IsDownloading && !IsLoading && !string.IsNullOrWhiteSpace(DownloadUrl) && !string.IsNullOrWhiteSpace(DownloadModelName));
      CancelModelDownloadCommand = new EnhancedAsyncRelayCommand(async (ct) => await CancelModelDownloadAsync(ct), () => _jobProgressClient != null && !string.IsNullOrEmpty(ActiveDownloadJobId) && IsDownloading);
      RetryModelDownloadCommand = new EnhancedAsyncRelayCommand(async (ct) => await RetryModelDownloadAsync(ct), () => _jobProgressClient != null && !string.IsNullOrEmpty(ActiveDownloadJobId));
      PauseModelDownloadCommand = new EnhancedAsyncRelayCommand(async (ct) => await PauseModelDownloadAsync(ct), () => _jobProgressClient != null && !string.IsNullOrEmpty(ActiveDownloadJobId) && IsDownloading);
      ResumeModelDownloadCommand = new EnhancedAsyncRelayCommand(async (ct) => await ResumeModelDownloadAsync(ct), () => _jobProgressClient != null && !string.IsNullOrEmpty(ActiveDownloadJobId));
    }

    public IAsyncRelayCommand LoadModelsCommand { get; }
    public IAsyncRelayCommand RefreshCommand { get; }
    public IAsyncRelayCommand<ModelInfo> VerifyModelCommand { get; }
    public IAsyncRelayCommand<ModelInfo> UpdateChecksumCommand { get; }
    public IAsyncRelayCommand<ModelInfo> DeleteModelCommand { get; }
    public IAsyncRelayCommand LoadStorageStatsCommand { get; }
    public IAsyncRelayCommand<ModelInfo> ExportModelCommand { get; }
    public IAsyncRelayCommand ImportModelCommand { get; }
    public IAsyncRelayCommand StartModelDownloadCommand { get; }
    public IAsyncRelayCommand CancelModelDownloadCommand { get; }
    public IAsyncRelayCommand RetryModelDownloadCommand { get; }
    public IAsyncRelayCommand PauseModelDownloadCommand { get; }
    public IAsyncRelayCommand ResumeModelDownloadCommand { get; }

    partial void OnSelectedEngineChanged(string? value)
    {
      _ = LoadModelsAsync(CancellationToken.None);
      if (string.IsNullOrEmpty(DownloadTargetEngine) && !string.IsNullOrEmpty(value))
      {
        DownloadTargetEngine = value;
      }
    }

    private async Task LoadModelsAsync(CancellationToken cancellationToken)
    {
      IsLoading = true;
      ErrorMessage = null;

      try
      {
        var modelsList = await _modelManagerClient.GetModelsAsync(SelectedEngine, cancellationToken);

        Models.Clear();
        foreach (var model in modelsList.OrderBy(m => m.Engine).ThenBy(m => m.ModelName))
        {
          Models.Add(model);
        }
        StatusMessage = ResourceHelper.FormatString("ModelManager.ModelsLoaded", Models.Count);
      }
      catch (OperationCanceledException)
      {
        return; // User cancelled
      }
      catch (Exception ex)
      {
        ErrorMessage = $"Failed to load models: {ex.Message}";
        await HandleErrorAsync(ex, "LoadModels");
      }
      finally
      {
        IsLoading = false;
      }
    }

    private async Task RefreshAsync(CancellationToken cancellationToken)
    {
      await LoadModelsAsync(cancellationToken);
      await LoadStorageStatsAsync(cancellationToken);
    }

    private async Task VerifyModelAsync(ModelInfo? model, CancellationToken cancellationToken)
    {
      if (model == null)
        return;

      IsVerifying = true;
      VerificationResult = null;
      ErrorMessage = null;

      try
      {
        var result = await _modelManagerClient.VerifyModelAsync(model.Engine, model.ModelName, cancellationToken);

        if (result.IsValid)
        {
          VerificationResult = ResourceHelper.GetString("ModelManager.VerificationSuccess", "✓ Model checksum verified successfully");
          StatusMessage = ResourceHelper.GetString("ModelManager.VerificationSuccessStatus", "Model checksum verified successfully");
        }
        else
        {
          VerificationResult = ResourceHelper.FormatString("ModelManager.VerificationFailed", result.ErrorMessage ?? string.Empty);
          ErrorMessage = result.ErrorMessage;
        }
      }
      catch (OperationCanceledException)
      {
        return; // User cancelled
      }
      catch (Exception ex)
      {
        VerificationResult = ResourceHelper.FormatString("ModelManager.VerificationError", ex.Message);
        ErrorMessage = ex.Message;
        await HandleErrorAsync(ex, "VerifyModel");
      }
      finally
      {
        IsVerifying = false;
      }
    }

    private async Task UpdateChecksumAsync(ModelInfo? model, CancellationToken cancellationToken)
    {
      if (model == null)
        return;

      IsLoading = true;
      ErrorMessage = null;

      try
      {
        var updated = await _modelManagerClient.UpdateModelChecksumAsync(model.Engine, model.ModelName, cancellationToken);

        // Update the model in the list
        var index = Models.IndexOf(model);
        if (index >= 0)
        {
          Models[index] = updated;
        }

        VerificationResult = ResourceHelper.GetString("ModelManager.ChecksumUpdatedSuccess", "✓ Checksum updated successfully");
        StatusMessage = ResourceHelper.GetString("ModelManager.ChecksumUpdatedStatus", "Checksum updated successfully");
      }
      catch (OperationCanceledException)
      {
        return; // User cancelled
      }
      catch (Exception ex)
      {
        ErrorMessage = ResourceHelper.FormatString("ModelManager.UpdateChecksumFailed", ex.Message);
        await HandleErrorAsync(ex, "UpdateChecksum");
      }
      finally
      {
        IsLoading = false;
      }
    }

    private async Task DeleteModelAsync(ModelInfo? model, CancellationToken cancellationToken)
    {
      if (model == null)
        return;

      IsLoading = true;
      ErrorMessage = null;

      try
      {
        await _modelManagerClient.DeleteModelAsync(model.Engine, model.ModelName, cancellationToken);

        // Track original index before removal
        var originalIndex = Models.IndexOf(model);

        // Remove from list
        Models.Remove(model);
        var previousSelected = SelectedModel;
        if (SelectedModel?.Engine == model.Engine && SelectedModel?.ModelName == model.ModelName)
        {
          SelectedModel = null;
        }

        // Refresh stats
        await LoadStorageStatsAsync(cancellationToken);

        StatusMessage = ResourceHelper.FormatString("ModelManager.ModelDeletedSuccess", model.ModelName);

        // Register undo action
        // Note: Undo will restore the UI state, but won't re-register the model with the backend
        // The model files may still exist on disk, but it won't appear in the backend registry
        if (_undoRedoService != null)
        {
          var action = new DeleteModelAction(
              Models,
              _modelManagerClient,
              model,
              originalIndex,
              onUndo: (m) => SelectedModel = m,
              onRedo: (m) =>
              {
                if (SelectedModel?.Engine == m.Engine && SelectedModel?.ModelName == m.ModelName)
                {
                  SelectedModel = null;
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
        ErrorMessage = ResourceHelper.FormatString("ModelManager.DeleteModelFailed", ex.Message);
        await HandleErrorAsync(ex, "DeleteModel");
      }
      finally
      {
        IsLoading = false;
      }
    }

    private async Task LoadStorageStatsAsync(CancellationToken cancellationToken)
    {
      try
      {
        StorageStats = await _modelManagerClient.GetStorageStatsAsync(cancellationToken);
      }
      catch (OperationCanceledException)
      {
        return; // User cancelled
      }
      catch (Exception ex)
      {
        ErrorMessage = $"Failed to load storage stats: {ex.Message}";
        await HandleErrorAsync(ex, "LoadStorageStats");
      }
    }

    public string FormatSize(long bytes)
    {
      if (bytes < 1024)
        return $"{bytes} B";
      if (bytes < 1024 * 1024)
        return $"{bytes / 1024.0:F2} KB";
      if (bytes < 1024 * 1024 * 1024)
        return $"{bytes / (1024.0 * 1024.0):F2} MB";
      return $"{bytes / (1024.0 * 1024.0 * 1024.0):F2} GB";
    }

    // Helper property for binding
    public string GetFormattedSize(long bytes) => FormatSize(bytes);

    private async Task ExportModelAsync(ModelInfo? model, CancellationToken cancellationToken)
    {
      if (model == null)
        return;

      IsLoading = true;
      ErrorMessage = null;

      try
      {
        // Get export stream from backend
        await using var stream = await _modelManagerClient.ExportModelAsync(model.Engine, model.ModelName, cancellationToken);

        // Show file save picker
        var savePicker = new FileSavePicker();
        savePicker.SuggestedStartLocation = PickerLocationId.DocumentsLibrary;
        savePicker.FileTypeChoices.Add("ZIP Archive", new[] { ".zip" });
        savePicker.SuggestedFileName = $"{model.Engine}_{model.ModelName}";

        var file = await savePicker.PickSaveFileAsync();
        if (file != null)
        {
          cancellationToken.ThrowIfCancellationRequested();

          // Write stream to file
          await using var fileStream = await file.OpenStreamForWriteAsync();
          await stream.CopyToAsync(fileStream, cancellationToken);
          await fileStream.FlushAsync(cancellationToken);

          VerificationResult = ResourceHelper.FormatString("ModelManager.ModelExportedSuccess", file.Name);
          StatusMessage = ResourceHelper.FormatString("ModelManager.ModelExportedStatus", file.Name);
        }
      }
      catch (OperationCanceledException)
      {
        return; // User cancelled
      }
      catch (Exception ex)
      {
        ErrorMessage = ResourceHelper.FormatString("ModelManager.ExportModelFailed", ex.Message);
        await HandleErrorAsync(ex, "ExportModel");
      }
      finally
      {
        IsLoading = false;
      }
    }

    private async Task ImportModelAsync(CancellationToken cancellationToken)
    {
      IsLoading = true;
      ErrorMessage = null;

      try
      {
        // Show file open picker
        var openPicker = new FileOpenPicker();
        openPicker.SuggestedStartLocation = PickerLocationId.DocumentsLibrary;
        openPicker.FileTypeFilter.Add(".zip");

        var file = await openPicker.PickSingleFileAsync();
        if (file != null)
        {
          cancellationToken.ThrowIfCancellationRequested();

          // Read file stream
          await using var fileStream = await file.OpenStreamForReadAsync();

          // Import model
          var importedModel = await _modelManagerClient.ImportModelAsync(fileStream, cancellationToken: cancellationToken);

          // Refresh models list
          await LoadModelsAsync(cancellationToken);
          await LoadStorageStatsAsync(cancellationToken);

          VerificationResult = $"✓ Model imported: {importedModel.Engine}/{importedModel.ModelName}";
          StatusMessage = $"Model imported: {importedModel.Engine}/{importedModel.ModelName}";
        }
      }
      catch (OperationCanceledException)
      {
        return; // User cancelled
      }
      catch (Exception ex)
      {
        ErrorMessage = ResourceHelper.FormatString("ModelManager.ImportModelFailed", ex.Message);
        await HandleErrorAsync(ex, "ImportModel");
      }
      finally
      {
        IsLoading = false;
      }
    }

    private async Task StartModelDownloadAsync(CancellationToken cancellationToken)
    {
      ErrorMessage = null;
      _downloadPollCts?.Cancel();
      _downloadPollCts?.Dispose();
      _downloadPollCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
      var pollCt = _downloadPollCts.Token;
      try
      {
        IsDownloading = true;
        DownloadJobProgress = 0;
        DownloadJobStatus = "pending";
        var engine = !string.IsNullOrEmpty(DownloadTargetEngine)
            ? DownloadTargetEngine!
            : (SelectedEngine ?? Engines.FirstOrDefault() ?? "xtts_v2");
        var ver = string.IsNullOrWhiteSpace(DownloadVersion) ? "1.0" : DownloadVersion.Trim();
        var req = new ModelDownloadStartRequest
        {
          Url = DownloadUrl.Trim(),
          Engine = engine,
          ModelName = DownloadModelName.Trim(),
          Version = ver,
          ExpectedSha256 = string.IsNullOrWhiteSpace(DownloadExpectedSha256)
              ? null
              : DownloadExpectedSha256.Trim()
        };
        var res = await _modelManagerClient.StartModelDownloadAsync(req, pollCt);
        ActiveDownloadJobId = res.JobId;
        StatusMessage = $"Download started (job {res.JobId})";
        await PollActiveDownloadJobAsync(pollCt);
      }
      catch (OperationCanceledException)
      {
        return;
      }
      catch (Exception ex)
      {
        ErrorMessage = $"Download start failed: {ex.Message}";
        await HandleErrorAsync(ex, "StartModelDownload");
      }
      finally
      {
        IsDownloading = false;
      }
    }

    private async Task PollActiveDownloadJobAsync(CancellationToken cancellationToken)
    {
      if (_jobProgressClient == null || string.IsNullOrEmpty(ActiveDownloadJobId))
      {
        return;
      }

      try
      {
        while (!cancellationToken.IsCancellationRequested)
        {
          var job = await _jobProgressClient.GetJobAsync(ActiveDownloadJobId, cancellationToken);
          if (job == null)
          {
            break;
          }

          DownloadJobProgress = job.Progress;
          DownloadJobStatus = job.Status;
          var s = job.Status?.ToLowerInvariant() ?? "";
          if (s is "completed" or "failed" or "cancelled")
          {
            if (s == "completed")
            {
              StatusMessage = "Model download completed";
              await LoadModelsAsync(cancellationToken);
              await LoadStorageStatsAsync(cancellationToken);
            }
            else if (s == "failed")
            {
              ErrorMessage = job.ErrorMessage ?? "Download failed";
            }

            break;
          }

          await Task.Delay(1500, cancellationToken);
        }
      }
      catch (OperationCanceledException)
      {
        return;
      }
    }

    private async Task CancelModelDownloadAsync(CancellationToken cancellationToken)
    {
      if (_jobProgressClient == null || string.IsNullOrEmpty(ActiveDownloadJobId))
      {
        return;
      }

      try
      {
        await _jobProgressClient.CancelJobAsync(ActiveDownloadJobId, cancellationToken);
        _downloadPollCts?.Cancel();
        StatusMessage = "Download cancel requested";
      }
      catch (Exception ex)
      {
        ErrorMessage = ex.Message;
        await HandleErrorAsync(ex, "CancelModelDownload");
      }
    }

    private async Task RetryModelDownloadAsync(CancellationToken cancellationToken)
    {
      if (_jobProgressClient == null || string.IsNullOrEmpty(ActiveDownloadJobId))
      {
        return;
      }

      try
      {
        _downloadPollCts?.Cancel();
        _downloadPollCts?.Dispose();
        _downloadPollCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        await _jobProgressClient.RetryJobAsync(ActiveDownloadJobId, cancellationToken);
        StatusMessage = "Download retry queued";
        IsDownloading = true;
        await PollActiveDownloadJobAsync(_downloadPollCts.Token);
      }
      catch (Exception ex)
      {
        ErrorMessage = ex.Message;
        await HandleErrorAsync(ex, "RetryModelDownload");
      }
      finally
      {
        IsDownloading = false;
      }
    }

    private async Task PauseModelDownloadAsync(CancellationToken cancellationToken)
    {
      if (_jobProgressClient == null || string.IsNullOrEmpty(ActiveDownloadJobId))
      {
        return;
      }

      try
      {
        await _jobProgressClient.PauseJobAsync(ActiveDownloadJobId, cancellationToken);
        StatusMessage = "Download pause requested";
      }
      catch (Exception ex)
      {
        ErrorMessage = ex.Message;
        await HandleErrorAsync(ex, "PauseModelDownload");
      }
    }

    private async Task ResumeModelDownloadAsync(CancellationToken cancellationToken)
    {
      if (_jobProgressClient == null || string.IsNullOrEmpty(ActiveDownloadJobId))
      {
        return;
      }

      try
      {
        _downloadPollCts?.Cancel();
        _downloadPollCts?.Dispose();
        _downloadPollCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        await _jobProgressClient.ResumeJobAsync(ActiveDownloadJobId, cancellationToken);
        StatusMessage = "Download resume requested";
        IsDownloading = true;
        await PollActiveDownloadJobAsync(_downloadPollCts.Token);
      }
      catch (Exception ex)
      {
        ErrorMessage = ex.Message;
        await HandleErrorAsync(ex, "ResumeModelDownload");
      }
      finally
      {
        IsDownloading = false;
      }
    }
  }
}