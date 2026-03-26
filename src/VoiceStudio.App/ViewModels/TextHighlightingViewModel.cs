using System;
using System.Collections.Generic;
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
using Windows.Storage.Pickers;
using Windows.Storage;

namespace VoiceStudio.App.ViewModels
{
  /// <summary>
  /// ViewModel for the TextHighlightingView panel - Text highlighting with audio sync.
  /// </summary>
  public partial class TextHighlightingViewModel : BaseViewModel, IPanelView, IPanelLifecycle
  {
    private readonly ITextHighlightingClient _client;
    private readonly IProjectsClient _projectsClient;
    private readonly IProjectAudioClient _projectAudioClient;
    private readonly ToastNotificationService? _toastNotificationService;

    public string PanelId => PanelIds.TextHighlighting;
    public string DisplayName => ResourceHelper.GetString("Panel.TextHighlighting.DisplayName", "Text Highlighting");
    public PanelRegion Region => PanelRegion.Center;

    [ObservableProperty]
    private string? selectedAudioId;

    [ObservableProperty]
    private ObservableCollection<string> availableAudioIds = new();

    [ObservableProperty]
    private string text = string.Empty;

    [ObservableProperty]
    private ObservableCollection<HighlightTextSegmentItem> segments = new();

    partial void OnSegmentsChanged(ObservableCollection<HighlightTextSegmentItem> value)
    {
      ExportSessionCommand.NotifyCanExecuteChanged();
    }

    [ObservableProperty]
    private HighlightTextSegmentItem? activeSegment;

    [ObservableProperty]
    private int? activeWordIndex;

    [ObservableProperty]
    private double currentTime;

    [ObservableProperty]
    private string? sessionId;

    partial void OnSessionIdChanged(string? value)
    {
      SaveSessionCommand.NotifyCanExecuteChanged();
      ExportSessionCommand.NotifyCanExecuteChanged();
    }

    [ObservableProperty]
    private bool isPlaying;

    [ObservableProperty]
    private string selectedHighlightType = "word";

    [ObservableProperty]
    private ObservableCollection<string> availableHighlightTypes = new() { "word", "phrase", "sentence", "emphasis", "note", "error" };

    public TextHighlightingViewModel(IViewModelContext context, ITextHighlightingClient client, IProjectsClient projectsClient, IProjectAudioClient projectAudioClient)
        : base(context)
    {
      _client = client ?? throw new ArgumentNullException(nameof(client));
      _projectsClient = projectsClient ?? throw new ArgumentNullException(nameof(projectsClient));
      _projectAudioClient = projectAudioClient ?? throw new ArgumentNullException(nameof(projectAudioClient));

      // Get services using helper (reduces code duplication)
      _toastNotificationService = ServiceInitializationHelper.TryGetService(() => AppServices.TryGetToastNotificationService());

      CreateSessionCommand = new AsyncRelayCommand(CreateSessionAsync);
      SyncCommand = new AsyncRelayCommand(SyncHighlightingAsync);
      UpdateSessionCommand = new AsyncRelayCommand(UpdateSessionAsync);
      DeleteSessionCommand = new AsyncRelayCommand(DeleteSessionAsync);
      LoadAudioFilesCommand = new AsyncRelayCommand(LoadAudioFilesAsync);
      RefreshCommand = new AsyncRelayCommand(RefreshAsync);
      SaveSessionCommand = new AsyncRelayCommand(SaveSessionAsync, () => !string.IsNullOrEmpty(SessionId));
      LoadSessionCommand = new AsyncRelayCommand(LoadSessionAsync);
      ExportSessionCommand = new AsyncRelayCommand(ExportSessionAsync, () => !string.IsNullOrEmpty(SessionId) && Segments.Count > 0);
    }

    Task IPanelLifecycle.OnActivatedAsync(CancellationToken cancellationToken)
    {
      return LoadAudioFilesAsync(cancellationToken);
    }

    Task IPanelLifecycle.OnDeactivatedAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    Task IPanelLifecycle.RefreshAsync(CancellationToken cancellationToken)
    {
      return RefreshAsync(cancellationToken);
    }

    public IAsyncRelayCommand CreateSessionCommand { get; }
    public IAsyncRelayCommand SyncCommand { get; }
    public IAsyncRelayCommand UpdateSessionCommand { get; }
    public IAsyncRelayCommand DeleteSessionCommand { get; }
    public IAsyncRelayCommand LoadAudioFilesCommand { get; }
    public IAsyncRelayCommand RefreshCommand { get; }
    public IAsyncRelayCommand SaveSessionCommand { get; }
    public IAsyncRelayCommand LoadSessionCommand { get; }
    public IAsyncRelayCommand ExportSessionCommand { get; }

    private async Task LoadAudioFilesAsync(CancellationToken cancellationToken)
    {
      IsLoading = true;
      ErrorMessage = null;

      try
      {
        var projects = await _projectsClient.GetProjectsAsync(cancellationToken);
        var audioIds = new List<string>();

        foreach (var project in projects)
        {
          cancellationToken.ThrowIfCancellationRequested();
          var audioFiles = await _projectAudioClient.ListProjectAudioAsync(project.Id, cancellationToken);
          foreach (var audioFile in audioFiles)
          {
            if (!string.IsNullOrEmpty(audioFile.Filename))
            {
              audioIds.Add(audioFile.Filename);
            }
          }
        }

        AvailableAudioIds.Clear();
        foreach (var audioId in audioIds.Distinct())
        {
          AvailableAudioIds.Add(audioId);
        }

        StatusMessage = ResourceHelper.FormatString("TextHighlighting.AudioFilesLoaded", AvailableAudioIds.Count);
      }
      catch (OperationCanceledException)
      {
        return; // User cancelled
      }
      catch (Exception ex)
      {
        await HandleErrorAsync(ex, "LoadAudioFiles");
      }
      finally
      {
        IsLoading = false;
      }
    }

    private async Task CreateSessionAsync(CancellationToken cancellationToken)
    {
      if (string.IsNullOrEmpty(SelectedAudioId))
      {
        ErrorMessage = ResourceHelper.GetString("TextHighlighting.AudioMustBeSelected", "Audio must be selected");
        return;
      }

      if (string.IsNullOrWhiteSpace(Text))
      {
        ErrorMessage = ResourceHelper.GetString("TextHighlighting.TextRequired", "Text is required");
        return;
      }

      IsLoading = true;
      ErrorMessage = null;

      try
      {
        var request = new TextHighlightingCreateRequest
        {
          AudioId = SelectedAudioId,
          Text = Text,
          HighlightType = SelectedHighlightType,
          Segments = null
        };

        var session = await _client.CreateSessionAsync(request, cancellationToken);

        if (session != null)
        {
          SessionId = session.Id;
          Segments.Clear();
          foreach (var segment in session.Segments)
          {
            Segments.Add(new HighlightTextSegmentItem(ToHighlightTextSegment(segment)));
          }
          StatusMessage = ResourceHelper.GetString("TextHighlighting.SessionCreated", "Highlighting session created");
          _toastNotificationService?.ShowSuccess(
              ResourceHelper.FormatString("TextHighlighting.SessionCreatedDetail", session.Segments.Length),
              ResourceHelper.GetString("Toast.Title.SessionCreated", "Session Created"));
        }
      }
      catch (OperationCanceledException)
      {
        return; // User cancelled
      }
      catch (Exception ex)
      {
        await HandleErrorAsync(ex, "CreateSession");
      }
      finally
      {
        IsLoading = false;
      }
    }

    private async Task SyncHighlightingAsync(CancellationToken cancellationToken)
    {
      if (string.IsNullOrEmpty(SelectedAudioId))
      {
        ErrorMessage = ResourceHelper.GetString("TextHighlighting.AudioMustBeSelected", "Audio must be selected");
        return;
      }

      try
      {
        IsLoading = true;
        ErrorMessage = null;

        var request = new TextHighlightingSyncRequest
        {
          AudioId = SelectedAudioId,
          CurrentTime = CurrentTime
        };

        var response = await _client.SyncHighlightingAsync(request, cancellationToken);

        if (response != null)
        {
          ActiveSegment = Segments.FirstOrDefault(s => s.Id == response.ActiveSegmentId);
          ActiveWordIndex = response.ActiveWordIndex;
          StatusMessage = ResourceHelper.GetString("TextHighlighting.HighlightingSynced", "Highlighting synced");
          _toastNotificationService?.ShowSuccess(
              ResourceHelper.GetString("TextHighlighting.HighlightingSyncedWithAudio", "Highlighting synced with audio"),
              ResourceHelper.GetString("Toast.Title.SyncComplete", "Sync Complete"));
        }
      }
      catch (OperationCanceledException)
      {
        return; // User cancelled
      }
      catch (Exception ex)
      {
        await HandleErrorAsync(ex, "SyncHighlighting");
        ErrorMessage = ResourceHelper.FormatString("TextHighlighting.SyncHighlightingFailed", ex.Message);
        _toastNotificationService?.ShowError(
            ResourceHelper.FormatString("TextHighlighting.SyncHighlightingFailed", ex.Message),
            ResourceHelper.GetString("Toast.Title.SyncFailed", "Sync Failed"));
      }
      finally
      {
        IsLoading = false;
      }
    }

    private async Task UpdateSessionAsync(CancellationToken cancellationToken)
    {
      if (string.IsNullOrEmpty(SessionId))
      {
        ErrorMessage = ResourceHelper.GetString("TextHighlighting.SessionMustBeCreated", "Session must be created first");
        return;
      }

      IsLoading = true;
      ErrorMessage = null;

      try
      {
        var request = new TextHighlightingUpdateRequest
        {
          CurrentTime = CurrentTime,
          Segments = Segments.Select(s => new TextHighlightingUpdateSegmentDto
          {
            Id = s.Id,
            Text = s.Text,
            StartTime = s.StartTime,
            EndTime = s.EndTime,
            HighlightType = s.HighlightType,
            WordTimings = s.WordTimings
          }).ToArray()
        };

        var updated = await _client.UpdateSessionAsync(SessionId, request, cancellationToken);

        if (updated != null)
        {
          Segments.Clear();
          foreach (var segment in updated.Segments)
          {
            Segments.Add(new HighlightTextSegmentItem(ToHighlightTextSegment(segment)));
          }
          StatusMessage = ResourceHelper.GetString("TextHighlighting.SessionUpdated", "Session updated");
          _toastNotificationService?.ShowSuccess(
              ResourceHelper.GetString("TextHighlighting.SessionUpdatedDetail", "Highlighting session updated"),
              ResourceHelper.GetString("Toast.Title.SessionUpdated", "Session Updated"));
        }
      }
      catch (OperationCanceledException)
      {
        return; // User cancelled
      }
      catch (Exception ex)
      {
        await HandleErrorAsync(ex, "UpdateSession");
      }
      finally
      {
        IsLoading = false;
      }
    }

    private async Task DeleteSessionAsync(CancellationToken cancellationToken)
    {
      if (string.IsNullOrEmpty(SessionId))
      {
        ErrorMessage = ResourceHelper.GetString("TextHighlighting.SessionMustBeCreated", "Session must be created first");
        return;
      }

      try
      {
        IsLoading = true;
        ErrorMessage = null;

        await _client.DeleteSessionAsync(SessionId, cancellationToken);

        SessionId = null;
        Segments.Clear();
        ActiveSegment = null;
        StatusMessage = ResourceHelper.GetString("TextHighlighting.SessionDeleted", "Session deleted");
        _toastNotificationService?.ShowSuccess(
            ResourceHelper.GetString("TextHighlighting.SessionDeletedDetail", "Highlighting session deleted"),
            ResourceHelper.GetString("Toast.Title.SessionDeleted", "Session Deleted"));
      }
      catch (Exception ex)
      {
        ErrorMessage = ResourceHelper.FormatString("TextHighlighting.DeleteSessionFailed", ex.Message);
        _toastNotificationService?.ShowError(
            ResourceHelper.FormatString("TextHighlighting.DeleteSessionFailed", ex.Message),
            ResourceHelper.GetString("Toast.Title.DeleteFailed", "Delete Failed"));
      }
      finally
      {
        IsLoading = false;
      }
    }

    private async Task RefreshAsync(CancellationToken cancellationToken)
    {
      await LoadAudioFilesAsync(cancellationToken);
      StatusMessage = ResourceHelper.GetString("TextHighlighting.Refreshed", "Refreshed");
    }

    private async Task SaveSessionAsync(CancellationToken cancellationToken)
    {
      if (string.IsNullOrEmpty(SessionId))
      {
        ErrorMessage = ResourceHelper.GetString("TextHighlighting.SessionMustBeCreated", "Session must be created first");
        return;
      }

      try
      {
        IsLoading = true;
        ErrorMessage = null;

        var request = new TextHighlightingPersistRequest
        {
          SessionId = SessionId,
          AudioId = SelectedAudioId,
          Text = Text,
          Segments = Segments.Select(s => new TextHighlightingUpdateSegmentDto
          {
            Id = s.Id,
            Text = s.Text,
            StartTime = s.StartTime,
            EndTime = s.EndTime,
            HighlightType = s.HighlightType,
            WordTimings = s.WordTimings
          }).ToArray(),
          Created = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ")
        };

        await _client.PersistSessionAsync(SessionId, request, cancellationToken);

        StatusMessage = ResourceHelper.GetString("TextHighlighting.SessionSaved", "Session saved");
        _toastNotificationService?.ShowSuccess(
            ResourceHelper.GetString("TextHighlighting.SessionSavedDetail", "Highlighting session saved"),
            ResourceHelper.GetString("Toast.Title.SessionSaved", "Session Saved"));
      }
      catch (Exception ex)
      {
        ErrorMessage = ResourceHelper.FormatString("TextHighlighting.SaveSessionFailed", ex.Message);
        _toastNotificationService?.ShowError(
            ResourceHelper.GetString("Toast.Title.SaveFailed", "Save Failed"),
            ex.Message);
      }
      finally
      {
        IsLoading = false;
      }
    }

    private async Task LoadSessionAsync(CancellationToken cancellationToken)
    {
      IsLoading = true;
      ErrorMessage = null;

      try
      {
        var sessions = await _client.GetSessionsAsync(cancellationToken);

        cancellationToken.ThrowIfCancellationRequested();

        if (sessions?.Length > 0)
        {
          // Use file picker to select session file
          var picker = new Windows.Storage.Pickers.FileOpenPicker();
          picker.SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.DocumentsLibrary;
          picker.FileTypeFilter.Add(".json");
          picker.FileTypeFilter.Add(".txt");

          var file = await picker.PickSingleFileAsync();

          cancellationToken.ThrowIfCancellationRequested();

          if (file != null)
          {
            var json = await Windows.Storage.FileIO.ReadTextAsync(file);
            var sessionData = System.Text.Json.JsonSerializer.Deserialize<HighlightingSessionData>(json);

            if (sessionData != null)
            {
              SessionId = sessionData.SessionId;
              SelectedAudioId = sessionData.AudioId;
              Text = sessionData.Text;
              Segments.Clear();
              foreach (var segmentData in sessionData.Segments)
              {
                var segment = new HighlightTextSegment
                {
                  Id = segmentData.Id,
                  Text = segmentData.Text,
                  StartTime = segmentData.StartTime,
                  EndTime = segmentData.EndTime,
                  WordTimings = segmentData.WordTimings
                };
                var item = new HighlightTextSegmentItem(segment);
                item.HighlightType = segmentData.HighlightType ?? "word";
                Segments.Add(item);
              }
              StatusMessage = ResourceHelper.FormatString("TextHighlighting.SessionLoaded", file.Name);
              _toastNotificationService?.ShowSuccess(
                  ResourceHelper.FormatString("TextHighlighting.SessionLoaded", file.Name),
                  ResourceHelper.GetString("Toast.Title.SessionLoaded", "Session Loaded"));
            }
          }
        }
      }
      catch (OperationCanceledException)
      {
        return; // User cancelled
      }
      catch (Exception ex)
      {
        await HandleErrorAsync(ex, "LoadSession");
      }
      finally
      {
        IsLoading = false;
      }
    }

    private async Task ExportSessionAsync(CancellationToken cancellationToken)
    {
      if (string.IsNullOrEmpty(SessionId) || Segments.Count == 0)
      {
        ErrorMessage = ResourceHelper.GetString("TextHighlighting.SessionMustHaveSegments", "Session must be created and have segments");
        return;
      }

      IsLoading = true;
      ErrorMessage = null;

      try
      {
        // Create export data
        var exportData = new
        {
          session_id = SessionId,
          audio_id = SelectedAudioId,
          text = Text,
          segments = Segments.Select(s => new
          {
            id = s.Id,
            text = s.Text,
            start_time = s.StartTime,
            end_time = s.EndTime,
            highlight_type = s.HighlightType,
            duration = s.EndTime - s.StartTime,
            word_timings = s.WordTimings
          }).ToArray(),
          exported = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ"),
          version = "1.0"
        };

        // Convert to JSON
        var json = System.Text.Json.JsonSerializer.Serialize(exportData, new System.Text.Json.JsonSerializerOptions
        {
          WriteIndented = true
        });

        cancellationToken.ThrowIfCancellationRequested();

        // Use file picker to save
        var picker = new Windows.Storage.Pickers.FileSavePicker();
        picker.SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.DocumentsLibrary;
        picker.FileTypeChoices.Add(ResourceHelper.GetString("FileType.JSON", "JSON"), new[] { ".json" });
        picker.FileTypeChoices.Add(ResourceHelper.GetString("FileType.Text", "Text"), new[] { ".txt" });
        picker.SuggestedFileName = $"highlighting_session_{SessionId}_{DateTime.Now:yyyyMMdd}";

        var file = await picker.PickSaveFileAsync();

        cancellationToken.ThrowIfCancellationRequested();

        if (file != null)
        {
          await Windows.Storage.FileIO.WriteTextAsync(file, json);
          StatusMessage = ResourceHelper.FormatString("TextHighlighting.SegmentsExported", Segments.Count, file.Name);
          _toastNotificationService?.ShowSuccess(
              ResourceHelper.FormatString("TextHighlighting.SegmentsExportedCount", Segments.Count),
              ResourceHelper.GetString("Toast.Title.ExportComplete", "Export Complete"));
        }
      }
      catch (OperationCanceledException)
      {
        return; // User cancelled
      }
      catch (Exception ex)
      {
        await HandleErrorAsync(ex, "ExportSession");
      }
      finally
      {
        IsLoading = false;
      }
    }

    private static HighlightTextSegment ToHighlightTextSegment(TextHighlightingSegment s)
    {
      return new HighlightTextSegment
      {
        Id = s.Id,
        Text = s.Text,
        StartTime = s.StartTime,
        EndTime = s.EndTime,
        WordTimings = s.WordTimings
      };
    }

    private class HighlightingSessionData
    {
      public string SessionId { get; set; } = string.Empty;
      public string AudioId { get; set; } = string.Empty;
      public string Text { get; set; } = string.Empty;
      public HighlightingSegmentData[] Segments { get; set; } = Array.Empty<HighlightingSegmentData>();
    }

    private class HighlightingSegmentData
    {
      public string Id { get; set; } = string.Empty;
      public string Text { get; set; } = string.Empty;
      public double StartTime { get; set; }
      public double EndTime { get; set; }
      public string? HighlightType { get; set; }
      public System.Collections.Generic.Dictionary<string, object>[]? WordTimings { get; set; }
    }

  }

  // Data models
  public class HighlightTextSegment
  {
    public string Id { get; set; } = string.Empty;
    public string Text { get; set; } = string.Empty;
    public double StartTime { get; set; }
    public double EndTime { get; set; }
    public System.Collections.Generic.Dictionary<string, object>[]? WordTimings { get; set; }
  }

  public class HighlightTextSegmentItem : ObservableObject
  {
    public string Id { get; set; }
    public string Text { get; set; }
    public double StartTime { get; set; }
    public double EndTime { get; set; }
    public System.Collections.Generic.Dictionary<string, object>[]? WordTimings { get; set; }

    private string _highlightType = "word";
    public string HighlightType
    {
      get => _highlightType;
      set => SetProperty(ref _highlightType, value);
    }

    public string TimeRangeDisplay => $"{StartTime:F2}s - {EndTime:F2}s";
    public string DurationDisplay => $"{EndTime - StartTime:F2}s";

    public HighlightTextSegmentItem(HighlightTextSegment segment)
    {
      Id = segment.Id;
      Text = segment.Text;
      StartTime = segment.StartTime;
      EndTime = segment.EndTime;
      WordTimings = segment.WordTimings;
    }
  }
}