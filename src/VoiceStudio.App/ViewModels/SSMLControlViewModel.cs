using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VoiceStudio.Core.Panels;
using VoiceStudio.Core.Services;
using VoiceStudio.App.Helpers;
using VoiceStudio.App.Services;
using VoiceStudio.App.Services.UndoableActions;
using VoiceStudio.App.Controls;
using VoiceStudio.App.Utilities;

namespace VoiceStudio.App.ViewModels
{
  /// <summary>
  /// ViewModel for the SSMLControlView panel - SSML editor.
  /// </summary>
  public partial class SSMLControlViewModel : BaseViewModel, IPanelView
  {
    private readonly IBackendClient _backendClient;
    private readonly IAudioPlayerService _audioPlayer;
    private readonly ToastNotificationService? _toastNotificationService;
    private readonly UndoRedoService? _undoRedoService;
    private readonly string _backendBaseUrl;

    public string PanelId => "ssml-control";
    public string DisplayName => ResourceHelper.GetString("Panel.SSMLControl.DisplayName", "SSML Editor");
    public PanelRegion Region => PanelRegion.Center;

    [ObservableProperty]
    private ObservableCollection<SSMLDocumentItem> documents = new();

    [ObservableProperty]
    private SSMLDocumentItem? selectedDocument;

    [ObservableProperty]
    private string? selectedProjectId;

    [ObservableProperty]
    private string? selectedProfileId;

    [ObservableProperty]
    private ObservableCollection<string> availableProjects = new();

    [ObservableProperty]
    private ObservableCollection<string> availableProfiles = new();

    [ObservableProperty]
    private string ssmlContent = string.Empty;

    [ObservableProperty]
    private bool isValid = true;

    [ObservableProperty]
    private ObservableCollection<string> validationErrors = new();

    [ObservableProperty]
    private ObservableCollection<string> validationWarnings = new();

    [ObservableProperty]
    private string statusMessage = string.Empty;

    // CRIT-2: Last preview AudioId for PlayCommand (replay without re-synthesizing)
    [ObservableProperty]
    private string? previewAudioId;

    public ObservableCollection<SSMLError> ValidationErrorsFormatted
    {
      get
      {
        var formatted = new ObservableCollection<SSMLError>();
        foreach (var error in ValidationErrors)
        {
          // Parse error message to extract line number if available
          // Format: "Line X: message" or "message"
          int lineNumber = 0;
          string message = error;

          var lineMatch = System.Text.RegularExpressions.Regex.Match(error, @"Line\s+(\d+)[:\s]+(.+)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
          if (lineMatch.Success && int.TryParse(lineMatch.Groups[1].Value, out int line))
          {
            lineNumber = line;
            message = lineMatch.Groups[2].Value.Trim();
          }

          formatted.Add(new SSMLError
          {
            LineNumber = lineNumber,
            ColumnNumber = 0,
            Message = message,
            Severity = ResourceHelper.GetString("SSMLControl.SeverityError", "Error")
          });
        }

        foreach (var warning in ValidationWarnings)
        {
          int lineNumber = 0;
          string message = warning;

          var lineMatch = System.Text.RegularExpressions.Regex.Match(warning, @"Line\s+(\d+)[:\s]+(.+)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
          if (lineMatch.Success && int.TryParse(lineMatch.Groups[1].Value, out int line))
          {
            lineNumber = line;
            message = lineMatch.Groups[2].Value.Trim();
          }

          formatted.Add(new SSMLError
          {
            LineNumber = lineNumber,
            ColumnNumber = 0,
            Message = message,
            Severity = ResourceHelper.GetString("SSMLControl.SeverityWarning", "Warning")
          });
        }

        return formatted;
      }
    }

    public SSMLControlViewModel(IViewModelContext context, IBackendClient backendClient, IAudioPlayerService audioPlayer)
        : base(context)
    {
      _backendClient = backendClient ?? throw new ArgumentNullException(nameof(backendClient));
      _audioPlayer = audioPlayer ?? throw new ArgumentNullException(nameof(audioPlayer));

      _backendBaseUrl = AppServices.GetService<BackendClientConfig>()?.BaseUrl?.TrimEnd('/')
          ?? "http://localhost:8000";

      // Get services (may be null if not initialized)
      try
      {
        _toastNotificationService = AppServices.TryGetToastNotificationService();
        _undoRedoService = AppServices.TryGetUndoRedoService();
      }
      catch
      {
        // Services may not be initialized yet - that's okay
        _toastNotificationService = null;
        _undoRedoService = null;
      }

      LoadDocumentsCommand = new EnhancedAsyncRelayCommand(async (ct) =>
      {
        using var profiler = PerformanceProfiler.StartCommand("LoadDocuments");
        await LoadDocumentsAsync(ct);
      }, () => !IsLoading);
      CreateDocumentCommand = new EnhancedAsyncRelayCommand(async (ct) =>
      {
        using var profiler = PerformanceProfiler.StartCommand("CreateDocument");
        await CreateDocumentAsync(ct);
      }, () => !string.IsNullOrWhiteSpace(SSMLContent) && !IsLoading);
      UpdateDocumentCommand = new EnhancedAsyncRelayCommand<SSMLDocumentItem>(async (document, ct) =>
      {
        using var profiler = PerformanceProfiler.StartCommand("UpdateDocument");
        await UpdateDocumentAsync(document, ct);
      }, (document) => document != null && !IsLoading);
      DeleteDocumentCommand = new EnhancedAsyncRelayCommand<SSMLDocumentItem>(async (document, ct) =>
      {
        using var profiler = PerformanceProfiler.StartCommand("DeleteDocument");
        await DeleteDocumentAsync(document, ct);
      }, (document) => document != null && !IsLoading);
      ValidateCommand = new EnhancedAsyncRelayCommand(async (ct) =>
      {
        using var profiler = PerformanceProfiler.StartCommand("ValidateSSML");
        await ValidateSSMLAsync(ct);
      }, () => !string.IsNullOrWhiteSpace(SSMLContent) && !IsLoading);
      PreviewCommand = new EnhancedAsyncRelayCommand(async (ct) =>
      {
        using var profiler = PerformanceProfiler.StartCommand("PreviewSSML");
        await PreviewSSMLAsync(ct);
      }, () => !string.IsNullOrWhiteSpace(SSMLContent) && !IsLoading);
      RefreshCommand = new EnhancedAsyncRelayCommand(async (ct) =>
      {
        using var profiler = PerformanceProfiler.StartCommand("Refresh");
        await RefreshAsync(ct);
      }, () => !IsLoading);
      PlayCommand = new EnhancedAsyncRelayCommand(async (ct) =>
      {
        using var profiler = PerformanceProfiler.StartCommand("Play");
        await PlayPreviewAsync(ct);
      }, () => !string.IsNullOrEmpty(PreviewAudioId) && !IsLoading);

      // Initialize with default SSML template
      SSMLContent = "<speak>\n  <p>Hello, this is a test.</p>\n</speak>";

      // Notify PlayCommand when IsLoading changes
      PropertyChanged += (_, e) =>
      {
        if (e.PropertyName == nameof(IsLoading))
          PlayCommand.NotifyCanExecuteChanged();
      };

      // Load initial data
      _ = LoadDocumentsAsync(CancellationToken.None);
    }

    public IAsyncRelayCommand LoadDocumentsCommand { get; }
    public IAsyncRelayCommand CreateDocumentCommand { get; }
    public IAsyncRelayCommand<SSMLDocumentItem> UpdateDocumentCommand { get; }
    public IAsyncRelayCommand<SSMLDocumentItem> DeleteDocumentCommand { get; }
    public IAsyncRelayCommand ValidateCommand { get; }
    public IAsyncRelayCommand PreviewCommand { get; }
    public IAsyncRelayCommand PlayCommand { get; }
    public IAsyncRelayCommand RefreshCommand { get; }

    // Compatibility alias for code expecting SSMLContent (uppercase acronym)
    public string SSMLContent
    {
      get => SsmlContent;
      set => SsmlContent = value;
    }

    private async Task LoadDocumentsAsync(CancellationToken cancellationToken)
    {
      IsLoading = true;
      ErrorMessage = null;

      try
      {
        var queryParams = new System.Collections.Specialized.NameValueCollection();
        if (!string.IsNullOrEmpty(SelectedProjectId))
          queryParams.Add("project_id", SelectedProjectId);
        if (!string.IsNullOrEmpty(SelectedProfileId))
          queryParams.Add("profile_id", SelectedProfileId);

        var queryString = string.Join("&",
            queryParams.AllKeys.SelectMany(key =>
                queryParams.GetValues(key)?.Select(value => $"{key}={Uri.EscapeDataString(value)}") ?? Array.Empty<string>()
            )
        );

        var url = "/api/ssml";
        if (!string.IsNullOrEmpty(queryString))
          url += $"?{queryString}";

        var documents = await _backendClient.SendRequestAsync<object, SSMLDocument[]>(
            url,
            null,
            System.Net.Http.HttpMethod.Get,
            cancellationToken
        );

        Documents.Clear();
        if (documents != null)
        {
          foreach (var doc in documents)
          {
            Documents.Add(new SSMLDocumentItem(doc));
          }
        }
      }
      catch (OperationCanceledException)
      {
        return; // User cancelled
      }
      catch (Exception ex)
      {
        await HandleErrorAsync(ex, "LoadDocuments");
      }
      finally
      {
        IsLoading = false;
      }
    }

    private async Task CreateDocumentAsync(CancellationToken cancellationToken)
    {
      if (string.IsNullOrWhiteSpace(SSMLContent))
      {
        ErrorMessage = ResourceHelper.GetString("SSMLControl.ContentRequired", "SSML content is required");
        return;
      }

      IsLoading = true;
      ErrorMessage = null;

      try
      {
        var request = new
        {
          name = ResourceHelper.GetString("SSMLControl.NewDocument", "New SSML Document"),
          content = SSMLContent,
          profile_id = SelectedProfileId,
          project_id = SelectedProjectId
        };

        var created = await _backendClient.SendRequestAsync<object, SSMLDocument>(
            "/api/ssml",
            request,
            System.Net.Http.HttpMethod.Post,
            cancellationToken
        );

        if (created != null)
        {
          var documentItem = new SSMLDocumentItem(created);
          Documents.Add(documentItem);
          SelectedDocument = Documents.Last();
          StatusMessage = ResourceHelper.GetString("SSMLControl.DocumentCreated", "Document created");
          _toastNotificationService?.ShowSuccess(
              ResourceHelper.GetString("SSMLControl.DocumentCreatedDetail", "SSML document created"),
              ResourceHelper.GetString("Toast.Title.DocumentCreated", "Document Created"));

          // Register undo action
          if (_undoRedoService != null)
          {
            var action = new CreateSSMLDocumentAction(
                Documents,
                _backendClient,
                documentItem,
                onUndo: (d) =>
                {
                  if (SelectedDocument?.Id == d.Id)
                  {
                    SelectedDocument = Documents.FirstOrDefault();
                  }
                },
                onRedo: (d) => SelectedDocument = d);
            _undoRedoService.RegisterAction(action);
          }
        }
      }
      catch (OperationCanceledException)
      {
        return; // User cancelled
      }
      catch (Exception ex)
      {
        await HandleErrorAsync(ex, "CreateDocument");
        _toastNotificationService?.ShowError(
            ResourceHelper.FormatString("SSMLControl.CreateDocumentFailed", ex.Message),
            ResourceHelper.GetString("Toast.Title.CreationFailed", "Creation Failed"));
      }
      finally
      {
        IsLoading = false;
      }
    }

    private async Task UpdateDocumentAsync(SSMLDocumentItem? document, CancellationToken cancellationToken)
    {
      if (document == null)
        return;

      IsLoading = true;
      ErrorMessage = null;

      try
      {
        var request = new
        {
          name = document.Name,
          content = SSMLContent,
          profile_id = SelectedProfileId
        };

        var updated = await _backendClient.SendRequestAsync<object, SSMLDocument>(
            $"/api/ssml/{document.Id}",
            request,
            System.Net.Http.HttpMethod.Put,
            cancellationToken
        );

        if (updated != null)
        {
          document.UpdateFrom(updated);
        }

        await LoadDocumentsAsync(cancellationToken);
        StatusMessage = ResourceHelper.GetString("SSMLControl.DocumentUpdated", "Document updated");
        _toastNotificationService?.ShowSuccess(
            ResourceHelper.GetString("SSMLControl.DocumentUpdatedDetail", "SSML document updated"),
            ResourceHelper.GetString("Toast.Title.DocumentUpdated", "Document Updated"));
      }
      catch (OperationCanceledException)
      {
        return; // User cancelled
      }
      catch (Exception ex)
      {
        await HandleErrorAsync(ex, "UpdateDocument");
        _toastNotificationService?.ShowError(
            ResourceHelper.FormatString("SSMLControl.UpdateDocumentFailed", ex.Message),
            ResourceHelper.GetString("Toast.Title.UpdateFailed", "Update Failed"));
      }
      finally
      {
        IsLoading = false;
      }
    }

    private async Task DeleteDocumentAsync(SSMLDocumentItem? document, CancellationToken cancellationToken)
    {
      if (document == null)
        return;

      IsLoading = true;
      ErrorMessage = null;

      try
      {
        await _backendClient.SendRequestAsync<object, object>(
            $"/api/ssml/{document.Id}",
            null,
            System.Net.Http.HttpMethod.Delete,
            cancellationToken
        );

        var documentToDelete = document;
        var originalIndex = Documents.IndexOf(documentToDelete);
        Documents.Remove(documentToDelete);
        StatusMessage = ResourceHelper.GetString("SSMLControl.DocumentDeleted", "Document deleted");
        _toastNotificationService?.ShowSuccess(
            ResourceHelper.FormatString("SSMLControl.DocumentDeletedDetail", documentToDelete.Name),
            ResourceHelper.GetString("Toast.Title.DocumentDeleted", "Document Deleted"));

        // Register undo action
        if (_undoRedoService != null && documentToDelete != null)
        {
          var action = new DeleteSSMLDocumentAction(
              Documents,
              _backendClient,
              documentToDelete,
              originalIndex,
              onUndo: (d) => SelectedDocument = d,
              onRedo: (d) =>
              {
                if (SelectedDocument?.Id == d.Id)
                {
                  SelectedDocument = null;
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
        await HandleErrorAsync(ex, "DeleteDocument");
        _toastNotificationService?.ShowError(
            ResourceHelper.FormatString("SSMLControl.DeleteDocumentFailed", ex.Message),
            ResourceHelper.GetString("Toast.Title.DeleteFailed", "Delete Failed"));
      }
      finally
      {
        IsLoading = false;
      }
    }

    private async Task ValidateSSMLAsync(CancellationToken cancellationToken)
    {
      if (string.IsNullOrWhiteSpace(SSMLContent))
      {
        ErrorMessage = ResourceHelper.GetString("SSMLControl.ContentRequired", "SSML content is required");
        return;
      }

      IsLoading = true;
      ErrorMessage = null;

      try
      {
        var request = new
        {
          name = ResourceHelper.GetString("SSMLControl.ValidationDocument", "Validation"),
          content = SSMLContent
        };

        var response = await _backendClient.SendRequestAsync<object, SSMLValidateResponse>(
            "/api/ssml/validate",
            request,
            System.Net.Http.HttpMethod.Post,
            cancellationToken
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
              ? ResourceHelper.GetString("SSMLControl.ValidationPassed", "SSML is valid")
              : ResourceHelper.GetString("SSMLControl.ValidationFailed", "SSML validation failed");
          if (response.Valid)
          {
            _toastNotificationService?.ShowSuccess(
                ResourceHelper.GetString("SSMLControl.ValidationPassed", "SSML is valid"),
                ResourceHelper.GetString("Toast.Title.ValidationSuccess", "Validation Success"));
          }
          else
          {
            _toastNotificationService?.ShowWarning(
                ResourceHelper.FormatString("SSMLControl.ValidationFailedDetail", response.Errors.Length),
                ResourceHelper.GetString("Toast.Title.ValidationFailed", "Validation Failed"));
          }
        }
      }
      catch (OperationCanceledException)
      {
        return; // User cancelled
      }
      catch (Exception ex)
      {
        await HandleErrorAsync(ex, "ValidateSSML");
        _toastNotificationService?.ShowError(
            ResourceHelper.FormatString("SSMLControl.ValidateFailed", ex.Message),
            ResourceHelper.GetString("Toast.Title.ValidationFailed", "Validation Failed"));
      }
      finally
      {
        IsLoading = false;
      }
    }

    private async Task PreviewSSMLAsync(CancellationToken cancellationToken)
    {
      if (string.IsNullOrWhiteSpace(SSMLContent))
      {
        ErrorMessage = ResourceHelper.GetString("SSMLControl.ContentRequired", "SSML content is required");
        return;
      }

      try
      {
        IsLoading = true;
        ErrorMessage = null;

        var request = new
        {
          content = SSMLContent,
          profile_id = SelectedProfileId,
          engine = (string?)null
        };

        var response = await _backendClient.SendRequestAsync<object, SSMLPreviewResponse>(
            "/api/ssml/preview",
            request,
            System.Net.Http.HttpMethod.Post,
            cancellationToken
        );

        StatusMessage = response?.Message ?? ResourceHelper.GetString("SSMLControl.PreviewSynthesized", "Preview synthesized");
        _toastNotificationService?.ShowSuccess(
            ResourceHelper.GetString("SSMLControl.PreviewSynthesized", "Preview synthesized"),
            ResourceHelper.GetString("Toast.Title.PreviewReady", "Preview Ready"));

        if (response != null && !string.IsNullOrEmpty(response.AudioId))
        {
          PreviewAudioId = response.AudioId;
          PlayCommand.NotifyCanExecuteChanged();
          await _audioPlayer.PlayBackendAudioIdAsync(response.AudioId, _backendBaseUrl, () =>
          {
            StatusMessage = ResourceHelper.GetString("SSMLControl.PreviewPlaybackComplete", "Preview playback complete");
          });
        }
      }
      catch (Exception ex)
      {
        ErrorMessage = ResourceHelper.FormatString("SSMLControl.PreviewFailed", ex.Message);
        _toastNotificationService?.ShowError(
            ResourceHelper.FormatString("SSMLControl.PreviewFailed", ex.Message),
            ResourceHelper.GetString("Toast.Title.PreviewFailed", "Preview Failed"));
      }
      finally
      {
        IsLoading = false;
      }
    }

    private async Task PlayPreviewAsync(CancellationToken cancellationToken)
    {
      if (string.IsNullOrEmpty(PreviewAudioId))
        return;

      try
      {
        await _audioPlayer.PlayBackendAudioIdAsync(PreviewAudioId, _backendBaseUrl, () =>
        {
          StatusMessage = ResourceHelper.GetString("SSMLControl.PreviewPlaybackComplete", "Preview playback complete");
        });
      }
      catch (Exception ex)
      {
        ErrorMessage = ResourceHelper.FormatString("SSMLControl.PreviewFailed", ex.Message);
      }
    }

    private async Task RefreshAsync(CancellationToken cancellationToken)
    {
      try
      {
        await LoadDocumentsAsync(cancellationToken);
        StatusMessage = ResourceHelper.GetString("SSMLControl.DocumentsRefreshed", "Documents refreshed");
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

    partial void OnSelectedDocumentChanged(SSMLDocumentItem? value)
    {
      if (value != null)
      {
        SSMLContent = value.Content;
      }
    }

    partial void OnSelectedProjectIdChanged(string? value)
    {
      _ = LoadDocumentsAsync(CancellationToken.None);
    }

    partial void OnSelectedProfileIdChanged(string? value)
    {
      _ = LoadDocumentsAsync(CancellationToken.None);
    }

    partial void OnPreviewAudioIdChanged(string? value)
    {
      PlayCommand.NotifyCanExecuteChanged();
    }

    // Response models
    private class SSMLValidateResponse
    {
      public bool Valid { get; set; }
      public string[] Errors { get; set; } = Array.Empty<string>();
      public string[] Warnings { get; set; } = Array.Empty<string>();
    }

    public class SSMLPreviewResponse
    {
      [System.Text.Json.Serialization.JsonPropertyName("audio_id")]
      public string AudioId { get; set; } = string.Empty;
      public double Duration { get; set; }
      public string Message { get; set; } = string.Empty;
    }
  }

  // Data models
  public class SSMLDocument
  {
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public string? ProfileId { get; set; }
    public string? ProjectId { get; set; }
    public string Created { get; set; } = string.Empty;
    public string Modified { get; set; } = string.Empty;
  }

  public class SSMLDocumentItem : ObservableObject
  {
    public string Id { get; set; }
    public string Name { get; set; }
    public string Content { get; set; }
    public string? ProfileId { get; set; }
    public string? ProjectId { get; set; }
    public string Created { get; set; }
    public string Modified { get; set; }

    public SSMLDocumentItem(SSMLDocument document)
    {
      Id = document.Id;
      Name = document.Name;
      Content = document.Content;
      ProfileId = document.ProfileId;
      ProjectId = document.ProjectId;
      Created = document.Created;
      Modified = document.Modified;
    }

    public void UpdateFrom(SSMLDocument document)
    {
      Name = document.Name;
      Content = document.Content;
      ProfileId = document.ProfileId;
      Modified = document.Modified;
      OnPropertyChanged(nameof(Name));
      OnPropertyChanged(nameof(Content));
      OnPropertyChanged(nameof(ProfileId));
      OnPropertyChanged(nameof(Modified));
    }
  }
}