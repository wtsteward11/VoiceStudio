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
    private readonly ISSMLClient _ssmlClient;
    private readonly IDialogService? _dialogService;
    private readonly IAudioPlayerService _audioPlayer;
    private readonly ToastNotificationService? _toastNotificationService;
    private readonly UndoRedoService? _undoRedoService;
    private readonly string _backendBaseUrl;
    private bool _isInitialized;

    public string PanelId => PanelIds.SSMLControl;
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

    public SSMLControlViewModel(IViewModelContext context, ISSMLClient ssmlClient, IAudioPlayerService audioPlayer, IDialogService? dialogService = null)
        : base(context)
    {
      _ssmlClient = ssmlClient ?? throw new ArgumentNullException(nameof(ssmlClient));
      _audioPlayer = audioPlayer ?? throw new ArgumentNullException(nameof(audioPlayer));
      _dialogService = dialogService ?? AppServices.GetService<IDialogService>();

      _backendBaseUrl = AppServices.GetService<BackendClientConfig>()?.BaseUrl?.TrimEnd('/')
          ?? BackendClientConfig.DefaultHttpBaseUrl;

      // Get services (may be null if not initialized)
      try
      {
        _toastNotificationService = AppServices.TryGetToastNotificationService();
        _undoRedoService = AppServices.TryGetUndoRedoService();
      }
      catch (Exception ex)
      {
        System.Diagnostics.Debug.WriteLine($"[SSMLControlViewModel] Services not available: {ex.Message}");
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

    }

    /// <summary>
    /// Initialize panel data. Call from view Loaded event (ADR-047).
    /// </summary>
    public async Task InitializeAsync(CancellationToken ct = default)
    {
      if (_isInitialized)
      {
        return;
      }

      _isInitialized = true;
      await LoadDocumentsAsync(ct).ConfigureAwait(false);
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
        var documents = await _ssmlClient.GetDocumentsAsync(SelectedProjectId, SelectedProfileId, cancellationToken).ConfigureAwait(false);

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
        var request = new SSMLCreateRequest
        {
          Name = ResourceHelper.GetString("SSMLControl.NewDocument", "New SSML Document"),
          Content = SSMLContent,
          ProfileId = SelectedProfileId,
          ProjectId = SelectedProjectId
        };

        var created = await _ssmlClient.CreateDocumentAsync(request, cancellationToken).ConfigureAwait(false);

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
        var request = new SSMLUpdateRequest
        {
          Name = document.Name,
          Content = SSMLContent,
          ProfileId = SelectedProfileId
        };

        var updated = await _ssmlClient.UpdateDocumentAsync(document.Id, request, cancellationToken).ConfigureAwait(false);

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
        var u = ActionableErrorTranslator.Translate(ex, ActionableOperationContext.General);
        _toastNotificationService?.ShowError(
            ResourceHelper.FormatString("SSMLControl.UpdateDocumentFailed", u.PrimaryMessage),
            ResourceHelper.GetString("Toast.Title.UpdateFailed", "Update Failed"));
      }
      finally
      {
        IsLoading = false;
      }
    }

    /// <summary>
    /// Shows confirmation dialog and deletes document via backend if confirmed.
    /// </summary>
    public async Task DeleteDocumentWithConfirmationAsync(SSMLDocumentItem document, CancellationToken ct = default)
    {
      if (_dialogService == null)
      {
        await DeleteDocumentAsync(document, ct).ConfigureAwait(false);
        return;
      }

      var confirmed = await _dialogService.ShowConfirmationAsync(
          ResourceHelper.GetString("SSMLControl.DeleteDocument.Title", "Delete Document"),
          ResourceHelper.GetString("SSMLControl.DeleteDocument.Message", "Are you sure you want to delete this SSML document? This action cannot be undone."),
          ResourceHelper.GetString("SSMLControl.DeleteDocument.Confirm", "Delete"),
          ResourceHelper.GetString("SSMLControl.DeleteDocument.Cancel", "Cancel")).ConfigureAwait(false);

      if (confirmed)
      {
        await DeleteDocumentAsync(document, ct).ConfigureAwait(false);
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
        await _ssmlClient.DeleteDocumentAsync(document.Id, cancellationToken).ConfigureAwait(false);

        cancellationToken.ThrowIfCancellationRequested();

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
        var d = ActionableErrorTranslator.Translate(ex, ActionableOperationContext.General);
        _toastNotificationService?.ShowError(
            ResourceHelper.FormatString("SSMLControl.DeleteDocumentFailed", d.PrimaryMessage),
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
        var response = await _ssmlClient.ValidateAsync(
            SSMLContent,
            ResourceHelper.GetString("SSMLControl.ValidationDocument", "Validation"),
            cancellationToken).ConfigureAwait(false);

        if (response != null)
        {
          IsValid = response.Valid;
          ValidationErrors.Clear();
          ValidationWarnings.Clear();

          if (response.Errors != null)
          {
            foreach (var error in response.Errors)
            {
              ValidationErrors.Add(error);
            }
          }

          if (response.Warnings != null)
          {
            foreach (var warning in response.Warnings)
            {
              ValidationWarnings.Add(warning);
            }
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
                ResourceHelper.FormatString("SSMLControl.ValidationFailedDetail", response.Errors?.Length ?? 0),
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
        var v = ActionableErrorTranslator.Translate(ex, ActionableOperationContext.SSMLValidate);
        _toastNotificationService?.ShowError(
            ResourceHelper.FormatString("SSMLControl.ValidateFailed", v.PrimaryMessage),
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

        var response = await _ssmlClient.PreviewAsync(SSMLContent, SelectedProfileId, null, cancellationToken).ConfigureAwait(false);

        StatusMessage = response?.Message ?? ResourceHelper.GetString("SSMLControl.PreviewSynthesized", "Preview synthesized");
        _toastNotificationService?.ShowSuccess(
            ResourceHelper.GetString("SSMLControl.PreviewSynthesized", "Preview synthesized"),
            ResourceHelper.GetString("Toast.Title.PreviewReady", "Preview Ready"));

        var ssmlNotice = ActionableErrorTranslator.BuildSsmlHandlingUserNotice(response?.SsmlHandling);
        if (ssmlNotice != null)
        {
          var body = string.IsNullOrWhiteSpace(ssmlNotice.SecondaryDetail)
              ? ssmlNotice.PrimaryMessage
              : $"{ssmlNotice.PrimaryMessage}{Environment.NewLine}{ssmlNotice.SecondaryDetail}";
          _toastNotificationService?.ShowWarning(body, ssmlNotice.Title);
        }

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
        var p = ActionableErrorTranslator.Translate(ex, ActionableOperationContext.SSMLPreview);
        ErrorMessage = ResourceHelper.FormatString("SSMLControl.PreviewFailed", p.PrimaryMessage);
        var previewDetail = string.IsNullOrWhiteSpace(p.SecondaryDetail)
            ? p.PrimaryMessage
            : $"{p.PrimaryMessage}{Environment.NewLine}{p.SecondaryDetail}";
        _toastNotificationService?.ShowError(
            ResourceHelper.FormatString("SSMLControl.PreviewFailed", previewDetail),
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
        var p = ActionableErrorTranslator.Translate(ex, ActionableOperationContext.SSMLPreview);
        ErrorMessage = ResourceHelper.FormatString("SSMLControl.PreviewFailed", p.PrimaryMessage);
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