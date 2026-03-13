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
using VoiceStudio.App.Services.UndoableActions;
using VoiceStudio.App.Utilities;

namespace VoiceStudio.App.ViewModels
{
  /// <summary>
  /// ViewModel for the TrainingDatasetEditorView panel - Advanced dataset editor.
  /// </summary>
  public partial class TrainingDatasetEditorViewModel : BaseViewModel, IPanelView
  {
    private readonly ITrainingClient _trainingClient;
    private readonly IBackendClient _backendClient;
    private readonly UndoRedoService? _undoRedoService;
    private readonly ToastNotificationService? _toastNotificationService;

    public string PanelId => "training-dataset-editor";
    public string DisplayName => ResourceHelper.GetString("Panel.TrainingDatasetEditor.DisplayName", "Dataset Editor");
    public PanelRegion Region => PanelRegion.Center;

    [ObservableProperty]
    private ObservableCollection<string> availableDatasets = new();

    [ObservableProperty]
    private string? selectedDatasetId;

    [ObservableProperty]
    private DatasetDetailItem? datasetDetail;

    [ObservableProperty]
    private DatasetAudioFileItem? selectedAudioFile;

    [ObservableProperty]
    private string newAudioId = string.Empty;

    [ObservableProperty]
    private string? newTranscript;

    [ObservableProperty]
    private bool isValid = true;

    [ObservableProperty]
    private ObservableCollection<string> validationErrors = new();

    [ObservableProperty]
    private ObservableCollection<string> validationWarnings = new();

    public TrainingDatasetEditorViewModel(IViewModelContext context, ITrainingClient trainingClient, IBackendClient backendClient)
        : base(context)
    {
      _trainingClient = trainingClient ?? throw new ArgumentNullException(nameof(trainingClient));
      _backendClient = backendClient ?? throw new ArgumentNullException(nameof(backendClient));

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

      // Get toast notification service (may be null if not initialized)
      try
      {
        _toastNotificationService = AppServices.TryGetToastNotificationService();
      }
      catch
      {
        // Service may not be initialized yet - that's okay
        _toastNotificationService = null;
      }

      LoadDatasetCommand = new AsyncRelayCommand(LoadDatasetAsync);
      AddAudioCommand = new AsyncRelayCommand(AddAudioAsync);
      UpdateAudioCommand = new AsyncRelayCommand<DatasetAudioFileItem>(UpdateAudioAsync);
      RemoveAudioCommand = new AsyncRelayCommand<DatasetAudioFileItem>(RemoveAudioAsync);
      ValidateCommand = new AsyncRelayCommand(ValidateDatasetAsync);
      RefreshCommand = new AsyncRelayCommand(RefreshAsync);

      // Load initial data
      _ = LoadAvailableDatasetsAsync(CancellationToken.None);
    }

    public IAsyncRelayCommand LoadDatasetCommand { get; }
    public IAsyncRelayCommand AddAudioCommand { get; }
    public IAsyncRelayCommand<DatasetAudioFileItem> UpdateAudioCommand { get; }
    public IAsyncRelayCommand<DatasetAudioFileItem> RemoveAudioCommand { get; }
    public IAsyncRelayCommand ValidateCommand { get; }
    public IAsyncRelayCommand RefreshCommand { get; }

    private async Task LoadAvailableDatasetsAsync(CancellationToken cancellationToken)
    {
      IsLoading = true;
      ErrorMessage = null;

      try
      {
        var datasets = await _trainingClient.ListDatasetsAsync(cancellationToken);

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
        await HandleErrorAsync(ex, "LoadAvailableDatasets");
        _toastNotificationService?.ShowError(
            ResourceHelper.GetString("Toast.Title.LoadDatasetsFailed", "Failed to Load Datasets"),
            ex.Message);
      }
      finally
      {
        IsLoading = false;
      }
    }

    private async Task LoadDatasetAsync(CancellationToken cancellationToken)
    {
      if (string.IsNullOrEmpty(SelectedDatasetId))
      {
        ErrorMessage = ResourceHelper.GetString("TrainingDatasetEditor.DatasetRequired", "Dataset must be selected");
        return;
      }

      IsLoading = true;
      ErrorMessage = null;

      try
      {
        var detail = await _backendClient.SendRequestAsync<object, DatasetDetail>(
            $"/api/dataset-editor/{Uri.EscapeDataString(SelectedDatasetId)}",
            null,
            System.Net.Http.HttpMethod.Get,
            cancellationToken
        );

        if (detail != null)
        {
          DatasetDetail = new DatasetDetailItem(detail);
          _toastNotificationService?.ShowSuccess(
              ResourceHelper.FormatString("TrainingDatasetEditor.DatasetLoadedDetail", detail.Name),
              ResourceHelper.GetString("Toast.Title.DatasetLoaded", "Dataset Loaded"));
        }
      }
      catch (OperationCanceledException)
      {
        return; // User cancelled
      }
      catch (Exception ex)
      {
        await HandleErrorAsync(ex, "LoadDataset");
        _toastNotificationService?.ShowError(
            ResourceHelper.GetString("Toast.Title.LoadDatasetFailed", "Failed to Load Dataset"),
            ex.Message);
      }
      finally
      {
        IsLoading = false;
      }
    }

    private async Task AddAudioAsync(CancellationToken cancellationToken)
    {
      if (string.IsNullOrEmpty(SelectedDatasetId))
      {
        ErrorMessage = ResourceHelper.GetString("TrainingDatasetEditor.DatasetRequired", "Dataset must be selected");
        return;
      }

      if (string.IsNullOrWhiteSpace(NewAudioId))
      {
        ErrorMessage = ResourceHelper.GetString("TrainingDatasetEditor.AudioIdRequired", "Audio ID is required");
        return;
      }

      IsLoading = true;
      ErrorMessage = null;

      try
      {
        var request = new
        {
          audio_id = NewAudioId,
          transcript = NewTranscript,
          order = (int?)null
        };

        var updated = await _backendClient.SendRequestAsync<object, DatasetDetail>(
            $"/api/dataset-editor/{Uri.EscapeDataString(SelectedDatasetId)}/audio",
            request,
            System.Net.Http.HttpMethod.Post,
            cancellationToken
        );

        if (updated != null)
        {
          var oldAudioFiles = DatasetDetail?.AudioFiles?.ToList() ?? new List<DatasetAudioFileItem>();
          var oldAudioFileIds = oldAudioFiles.Select(af => af.Id).ToHashSet();
          DatasetDetail = new DatasetDetailItem(updated);
          // Find the newly added audio file (it should be in the new list but not in the old list)
          var addedAudioFile = DatasetDetail.AudioFiles.FirstOrDefault(af =>
              !oldAudioFileIds.Contains(af.Id));

          // Register undo action after DatasetDetail is updated
          if (_undoRedoService != null && addedAudioFile != null && DatasetDetail != null)
          {
            var action = new AddDatasetAudioAction(
                DatasetDetail,
                addedAudioFile,
                _backendClient,
                onUndo: (af) =>
                {
                  if (SelectedAudioFile?.Id == af.Id)
                  {
                    SelectedAudioFile = null;
                  }
                },
                onRedo: (af) => SelectedAudioFile = af);
            _undoRedoService.RegisterAction(action);
          }

          NewAudioId = string.Empty;
          NewTranscript = null;
          StatusMessage = ResourceHelper.GetString("TrainingDatasetEditor.AudioAdded", "Audio added to dataset");
          _toastNotificationService?.ShowSuccess(
              ResourceHelper.GetString("TrainingDatasetEditor.AudioAddedDetail", "Audio file added to dataset successfully"),
              ResourceHelper.GetString("Toast.Title.AudioAdded", "Audio Added"));
        }
      }
      catch (OperationCanceledException)
      {
        return; // User cancelled
      }
      catch (Exception ex)
      {
        await HandleErrorAsync(ex, "AddAudio");
        _toastNotificationService?.ShowError(
            ResourceHelper.GetString("Toast.Title.AddAudioFailed", "Failed to Add Audio"),
            ex.Message);
      }
      finally
      {
        IsLoading = false;
      }
    }

    private async Task UpdateAudioAsync(DatasetAudioFileItem? audioFile, CancellationToken cancellationToken)
    {
      if (audioFile == null || string.IsNullOrEmpty(SelectedDatasetId))
        return;

      IsLoading = true;
      ErrorMessage = null;

      try
      {
        // Store original values for undo
        var originalTranscript = audioFile.Transcript;
        var originalOrder = audioFile.Order;

        var request = new
        {
          transcript = audioFile.Transcript,
          order = audioFile.Order
        };

        var updated = await _backendClient.SendRequestAsync<object, DatasetDetail>(
            $"/api/dataset-editor/{Uri.EscapeDataString(SelectedDatasetId)}/audio/{Uri.EscapeDataString(audioFile.Id)}",
            request,
            System.Net.Http.HttpMethod.Put,
            cancellationToken
        );

        if (updated != null)
        {
          var updatedAudioFile = updated.AudioFiles.FirstOrDefault(af => af.Id == audioFile.Id);
          if (updatedAudioFile != null)
          {
            var newTranscript = updatedAudioFile.Transcript;
            var newOrder = updatedAudioFile.Order;

            DatasetDetail = new DatasetDetailItem(updated);

            // Find the updated audio file in the new dataset detail
            var audioFileInDetail = DatasetDetail.AudioFiles.FirstOrDefault(af => af.Id == audioFile.Id);

            // Register undo action after DatasetDetail is updated
            if (_undoRedoService != null && audioFileInDetail != null && DatasetDetail != null)
            {
              var action = new UpdateDatasetAudioAction(
                  DatasetDetail,
                  audioFileInDetail,
                  _backendClient,
                  originalTranscript ?? string.Empty,
                  originalOrder,
                  newTranscript ?? string.Empty,
                  newOrder,
                  onUndo: (af) =>
                  {
                    // Selection is maintained automatically
                  },
                  onRedo: (af) =>
                  {
                    // Selection is maintained automatically
                  });
              _undoRedoService.RegisterAction(action);
            }
          }

          StatusMessage = ResourceHelper.GetString("TrainingDatasetEditor.AudioUpdated", "Audio updated");
          _toastNotificationService?.ShowSuccess(
              ResourceHelper.GetString("TrainingDatasetEditor.AudioUpdatedDetail", "Audio file updated successfully"),
              ResourceHelper.GetString("Toast.Title.AudioUpdated", "Audio Updated"));
        }
      }
      catch (OperationCanceledException)
      {
        return; // User cancelled
      }
      catch (Exception ex)
      {
        await HandleErrorAsync(ex, "UpdateAudio");
        _toastNotificationService?.ShowError(
            ResourceHelper.GetString("Toast.Title.UpdateAudioFailed", "Failed to Update Audio"),
            ex.Message);
      }
      finally
      {
        IsLoading = false;
      }
    }

    private async Task RemoveAudioAsync(DatasetAudioFileItem? audioFile, CancellationToken cancellationToken)
    {
      if (audioFile == null || string.IsNullOrEmpty(SelectedDatasetId))
        return;

      IsLoading = true;
      ErrorMessage = null;

      try
      {
        var updated = await _backendClient.SendRequestAsync<object, DatasetDetail>(
            $"/api/dataset-editor/{Uri.EscapeDataString(SelectedDatasetId)}/audio/{Uri.EscapeDataString(audioFile.Id)}",
            null,
            System.Net.Http.HttpMethod.Delete,
            cancellationToken
        );

        if (updated != null)
        {
          var audioFileToRemove = audioFile;
          var originalIndex = DatasetDetail?.AudioFiles?.ToList().FindIndex(af => af.Id == audioFile.Id) ?? -1;
          if (SelectedAudioFile?.Id == audioFile.Id)
          {
            SelectedAudioFile = null;
          }

          DatasetDetail = new DatasetDetailItem(updated);

          // Register undo action after DatasetDetail is updated
          if (_undoRedoService != null && audioFileToRemove != null && DatasetDetail != null && originalIndex >= 0)
          {
            var action = new RemoveDatasetAudioAction(
                DatasetDetail,
                audioFileToRemove,
                _backendClient,
                originalIndex,
                onUndo: (af) => SelectedAudioFile = af,
                onRedo: (af) =>
                {
                  if (SelectedAudioFile?.Id == af.Id)
                  {
                    SelectedAudioFile = null;
                  }
                });
            _undoRedoService.RegisterAction(action);
          }

          StatusMessage = ResourceHelper.GetString("TrainingDatasetEditor.AudioRemoved", "Audio removed from dataset");
          _toastNotificationService?.ShowSuccess(
              ResourceHelper.GetString("TrainingDatasetEditor.AudioRemovedDetail", "Audio file removed from dataset successfully"),
              ResourceHelper.GetString("Toast.Title.AudioRemoved", "Audio Removed"));
        }
      }
      catch (OperationCanceledException)
      {
        return; // User cancelled
      }
      catch (Exception ex)
      {
        await HandleErrorAsync(ex, "RemoveAudio");
        _toastNotificationService?.ShowError(
            ResourceHelper.GetString("Toast.Title.RemoveAudioFailed", "Failed to Remove Audio"),
            ex.Message);
      }
      finally
      {
        IsLoading = false;
      }
    }

    private async Task ValidateDatasetAsync(CancellationToken cancellationToken)
    {
      if (string.IsNullOrEmpty(SelectedDatasetId))
      {
        ErrorMessage = ResourceHelper.GetString("TrainingDatasetEditor.DatasetRequired", "Dataset must be selected");
        return;
      }

      try
      {
        IsLoading = true;
        ErrorMessage = null;

        var response = await _backendClient.SendRequestAsync<object, DatasetValidateResponse>(
            $"/api/dataset-editor/{Uri.EscapeDataString(SelectedDatasetId)}/validate",
            null,
            System.Net.Http.HttpMethod.Post
        );

        if (response != null)
        {
          IsValid = response.Valid;
          ValidationErrors.Clear();
          ValidationWarnings.Clear();

          foreach (var error in response.Errors)
          {
            ValidationErrors.Add(error);
          }

          foreach (var warning in response.Warnings)
          {
            ValidationWarnings.Add(warning);
          }

          StatusMessage = response.Valid
              ? ResourceHelper.GetString("TrainingDatasetEditor.DatasetValid", "Dataset is valid")
              : ResourceHelper.GetString("TrainingDatasetEditor.DatasetValidationFailed", "Dataset validation failed");

          if (response.Valid)
          {
            _toastNotificationService?.ShowSuccess(
                ResourceHelper.FormatString("TrainingDatasetEditor.DatasetValidDetail", response.TotalFiles),
                ResourceHelper.GetString("Toast.Title.DatasetValid", "Dataset Valid"));
          }
          else
          {
            var errorCount = response.Errors?.Length ?? 0;
            _toastNotificationService?.ShowError(
                ResourceHelper.GetString("Toast.Title.DatasetValidationFailed", "Dataset Validation Failed"),
                ResourceHelper.FormatString("TrainingDatasetEditor.ValidationFailedDetail", errorCount));
          }
        }
      }
      catch (OperationCanceledException)
      {
        return; // User cancelled
      }
      catch (Exception ex)
      {
        await HandleErrorAsync(ex, "ValidateDataset");
        _toastNotificationService?.ShowError(
            ResourceHelper.GetString("Toast.Title.ValidationFailed", "Validation Failed"),
            ex.Message);
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
        await LoadDatasetAsync(cancellationToken);
        await ValidateDatasetAsync(cancellationToken);
        StatusMessage = ResourceHelper.GetString("TrainingDatasetEditor.DatasetRefreshed", "Dataset refreshed");
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

    partial void OnSelectedDatasetIdChanged(string? value)
    {
      if (!string.IsNullOrEmpty(value))
      {
        _ = LoadDatasetAsync(CancellationToken.None);
      }
    }

    // Response models
    private class DatasetValidateResponse
    {
      public bool Valid { get; set; }
      public string[] Errors { get; set; } = Array.Empty<string>();
      public string[] Warnings { get; set; } = Array.Empty<string>();
      public double TotalDuration { get; set; }
      public int TotalFiles { get; set; }
    }
  }

  // Data models
  public class DatasetDetail
  {
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public System.Collections.Generic.List<DatasetAudioFile> AudioFiles { get; set; } = new();
    public double TotalDuration { get; set; }
    public int TotalFiles { get; set; }
    public string Created { get; set; } = string.Empty;
    public string Modified { get; set; } = string.Empty;
  }

  public class DatasetAudioFile
  {
    public string Id { get; set; } = string.Empty;
    public string AudioId { get; set; } = string.Empty;
    public string? Transcript { get; set; }
    public double? Duration { get; set; }
    public int? SampleRate { get; set; }
    public int Order { get; set; }
    public string Created { get; set; } = string.Empty;
  }

  public class DatasetDetailItem : ObservableObject
  {
    public string Id { get; set; }
    public string Name { get; set; }
    public string? Description { get; set; }
    public ObservableCollection<DatasetAudioFileItem> AudioFiles { get; set; }
    public double TotalDuration { get; set; }
    public int TotalFiles { get; set; }
    public string Created { get; set; }
    public string Modified { get; set; }
    public string DurationDisplay => $"{TotalDuration:F2}s";
    public string FilesDisplay => $"{TotalFiles} files";

    public DatasetDetailItem(DatasetDetail detail)
    {
      Id = detail.Id;
      Name = detail.Name;
      Description = detail.Description;
      AudioFiles = new ObservableCollection<DatasetAudioFileItem>(
          detail.AudioFiles.Select(af => new DatasetAudioFileItem(af))
      );
      TotalDuration = detail.TotalDuration;
      TotalFiles = detail.TotalFiles;
      Created = detail.Created;
      Modified = detail.Modified;
    }
  }

  public class DatasetAudioFileItem : ObservableObject
  {
    public string Id { get; set; }
    public string AudioId { get; set; }
    public string? Transcript { get; set; }
    public double? Duration { get; set; }
    public int? SampleRate { get; set; }
    public int Order { get; set; }
    public string Created { get; set; }
    public string DurationDisplay => Duration.HasValue ? $"{Duration.Value:F2}s" : ResourceHelper.GetString("TrainingDatasetEditor.Unknown", "Unknown");

    public DatasetAudioFileItem(DatasetAudioFile audioFile)
    {
      Id = audioFile.Id;
      AudioId = audioFile.AudioId;
      Transcript = audioFile.Transcript;
      Duration = audioFile.Duration;
      SampleRate = audioFile.SampleRate;
      Order = audioFile.Order;
      Created = audioFile.Created;
    }
  }
}