using System;
using System.Collections.ObjectModel;
using System.Collections.Generic;
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
using EditorSessionModel = VoiceStudio.App.Services.EditorSession;
using TextSegmentModel = VoiceStudio.App.Services.TextSegment;

namespace VoiceStudio.App.ViewModels
{
  /// <summary>
  /// ViewModel for the TextSpeechEditorView panel - Text-based speech editing.
  /// </summary>
  public partial class TextSpeechEditorViewModel : BaseViewModel, IPanelView, IPanelLifecycle
  {
    private readonly ITextSpeechEditorClient _textSpeechEditorClient;
    private readonly IProjectsClient _projectsClient;
    private readonly IProfilesClient _profilesClient;
    private readonly IAudioPlayerService _audioPlayer;
    private readonly UndoRedoService? _undoRedoService;

    public string PanelId => PanelIds.TextSpeechEditor;
    public string DisplayName => ResourceHelper.GetString("Panel.TextSpeechEditor.DisplayName", "Text Speech Editor");
    public PanelRegion Region => PanelRegion.Center;

    [ObservableProperty]
    private ObservableCollection<EditorSessionItem> sessions = new();

    [ObservableProperty]
    private EditorSessionItem? selectedSession;

    [ObservableProperty]
    private ObservableCollection<TextSegmentItem> segments = new();

    [ObservableProperty]
    private TextSegmentItem? selectedSegment;

    [ObservableProperty]
    private string newSessionTitle = string.Empty;

    [ObservableProperty]
    private string? selectedProjectId;

    [ObservableProperty]
    private ObservableCollection<string> availableProjects = new();

    [ObservableProperty]
    private string? selectedVoiceProfileId;

    [ObservableProperty]
    private ObservableCollection<string> availableVoiceProfiles = new();

    [ObservableProperty]
    private string? selectedEngine;

    [ObservableProperty]
    private ObservableCollection<string> availableEngines = new();

    [ObservableProperty]
    private bool ssmlMode;

    [ObservableProperty]
    private string editedTranscript = string.Empty;

    [ObservableProperty]
    private string? previewAudioId;

    [ObservableProperty]
    private string? previewAudioUrl;

    public TextSpeechEditorViewModel(IViewModelContext context, ITextSpeechEditorClient textSpeechEditorClient, IProjectsClient projectsClient, IProfilesClient profilesClient, IAudioPlayerService audioPlayer)
        : base(context)
    {
      _textSpeechEditorClient = textSpeechEditorClient ?? throw new ArgumentNullException(nameof(textSpeechEditorClient));
      _projectsClient = projectsClient ?? throw new ArgumentNullException(nameof(projectsClient));
      _profilesClient = profilesClient ?? throw new ArgumentNullException(nameof(profilesClient));
      _audioPlayer = audioPlayer ?? throw new ArgumentNullException(nameof(audioPlayer));

      // Get undo/redo service using helper (reduces code duplication)
      _undoRedoService = ServiceInitializationHelper.TryGetService(() => AppServices.TryGetUndoRedoService());

      LoadSessionsCommand = new EnhancedAsyncRelayCommand(async (ct) =>
      {
        using var profiler = PerformanceProfiler.StartCommand("LoadSessions");
        await LoadSessionsAsync(ct);
      });
      CreateSessionCommand = new EnhancedAsyncRelayCommand(async (ct) =>
      {
        using var profiler = PerformanceProfiler.StartCommand("CreateSession");
        await CreateSessionAsync(ct);
      });
      UpdateSessionCommand = new EnhancedAsyncRelayCommand(async (ct) =>
      {
        using var profiler = PerformanceProfiler.StartCommand("UpdateSession");
        await UpdateSessionAsync(ct);
      });
      DeleteSessionCommand = new EnhancedAsyncRelayCommand(async (ct) =>
      {
        using var profiler = PerformanceProfiler.StartCommand("DeleteSession");
        await DeleteSessionAsync(ct);
      });
      AddSegmentCommand = new EnhancedAsyncRelayCommand(async (ct) =>
      {
        using var profiler = PerformanceProfiler.StartCommand("AddSegment");
        await AddSegmentAsync(ct);
      });
      RemoveSegmentCommand = new EnhancedAsyncRelayCommand(async (ct) =>
      {
        using var profiler = PerformanceProfiler.StartCommand("RemoveSegment");
        await RemoveSegmentAsync(ct);
      });
      SynthesizeSessionCommand = new EnhancedAsyncRelayCommand(async (ct) =>
      {
        using var profiler = PerformanceProfiler.StartCommand("SynthesizeSession");
        await SynthesizeSessionAsync(ct);
      });
      PreviewSynthesisCommand = new EnhancedAsyncRelayCommand(async (ct) =>
      {
        using var profiler = PerformanceProfiler.StartCommand("PreviewSynthesis");
        await PreviewSynthesisAsync(ct);
      }, () => SsmlMode && !string.IsNullOrWhiteSpace(EditedTranscript) && !string.IsNullOrWhiteSpace(SelectedVoiceProfileId));
      PlayCommand = new EnhancedAsyncRelayCommand(async (ct) =>
      {
        using var profiler = PerformanceProfiler.StartCommand("Play");
        await PlayPreviewAsync(ct);
      }, () => (!string.IsNullOrEmpty(PreviewAudioId) || !string.IsNullOrEmpty(PreviewAudioUrl)) && !IsLoading);
      RefreshCommand = new EnhancedAsyncRelayCommand(async (ct) =>
      {
        using var profiler = PerformanceProfiler.StartCommand("Refresh");
        await RefreshAsync(ct);
      });

      PropertyChanged += (_, e) =>
      {
        if (e.PropertyName is nameof(PreviewAudioId) or nameof(PreviewAudioUrl) or nameof(IsLoading))
          PlayCommand.NotifyCanExecuteChanged();
      };
    }

    public Task OnActivatedAsync(CancellationToken cancellationToken = default) => RefreshAsync(cancellationToken);
    public Task OnDeactivatedAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    public IAsyncRelayCommand LoadSessionsCommand { get; }
    public IAsyncRelayCommand CreateSessionCommand { get; }
    public IAsyncRelayCommand UpdateSessionCommand { get; }
    public IAsyncRelayCommand DeleteSessionCommand { get; }
    public IAsyncRelayCommand AddSegmentCommand { get; }
    public IAsyncRelayCommand RemoveSegmentCommand { get; }
    public IAsyncRelayCommand SynthesizeSessionCommand { get; }
    public IAsyncRelayCommand PreviewSynthesisCommand { get; }
    public IAsyncRelayCommand PlayCommand { get; }
    public IAsyncRelayCommand RefreshCommand { get; }

    partial void OnSelectedSessionChanged(EditorSessionItem? value)
    {
      if (value != null)
      {
        Segments.Clear();
        foreach (var segment in value.Segments)
        {
          Segments.Add(segment);
        }
      }
      else
      {
        Segments.Clear();
      }
    }

    private async Task LoadSessionsAsync(CancellationToken cancellationToken)
    {
      IsLoading = true;
      ErrorMessage = null;

      try
      {
        var sessions = await _textSpeechEditorClient.GetSessionsAsync(cancellationToken);

        if (sessions != null)
        {
          Sessions.Clear();
          foreach (var session in sessions)
          {
            Sessions.Add(new EditorSessionItem(session));
          }
        }
      }
      catch (OperationCanceledException)
      {
        return; // User cancelled
      }
      catch (Exception ex)
      {
        ErrorMessage = ResourceHelper.FormatString("TextSpeechEditor.LoadSessionsFailed", ex.Message);
        await HandleErrorAsync(ex, "LoadSessions");
      }
      finally
      {
        IsLoading = false;
      }
    }

    private async Task CreateSessionAsync(CancellationToken cancellationToken)
    {
      if (string.IsNullOrWhiteSpace(NewSessionTitle))
      {
        ErrorMessage = ResourceHelper.GetString("TextSpeechEditor.SessionTitleRequired", "Session title is required");
        return;
      }

      IsLoading = true;
      ErrorMessage = null;

      try
      {
        var request = new
        {
          title = NewSessionTitle,
          project_id = SelectedProjectId,
          language = "en"
        };

        var session = await _textSpeechEditorClient.CreateSessionAsync(request, cancellationToken);

        if (session != null)
        {
          var sessionItem = new EditorSessionItem(session);
          Sessions.Add(sessionItem);
          SelectedSession = sessionItem;
          NewSessionTitle = string.Empty;
          StatusMessage = ResourceHelper.GetString("TextSpeechEditor.SessionCreated", "Session created");

          // Register undo action
          if (_undoRedoService != null)
          {
            var action = new CreateTextSpeechSessionAction(
                Sessions,
                _textSpeechEditorClient,
                sessionItem,
                onUndo: (s) =>
                {
                  if (SelectedSession?.SessionId == s.SessionId)
                  {
                    SelectedSession = Sessions.FirstOrDefault();
                  }
                },
                onRedo: (s) => SelectedSession = s);
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
        ErrorMessage = ResourceHelper.FormatString("TextSpeechEditor.CreateSessionFailed", ex.Message);
        await HandleErrorAsync(ex, "CreateSession");
      }
      finally
      {
        IsLoading = false;
      }
    }

    private async Task UpdateSessionAsync(CancellationToken cancellationToken)
    {
      if (SelectedSession == null)
      {
        ErrorMessage = ResourceHelper.GetString("TextSpeechEditor.NoSessionSelected", "No session selected");
        return;
      }

      IsLoading = true;
      ErrorMessage = null;

      try
      {
        var request = new
        {
          title = SelectedSession.Title,
          segments = SelectedSession.Segments.Select(s => new
          {
            id = s.Id,
            text = s.Text,
            start_time = s.StartTime,
            end_time = s.EndTime,
            speaker = s.Speaker,
            prosody = s.Prosody,
            phonemes = s.Phonemes,
            notes = s.Notes
          }).ToArray()
        };

        var session = await _textSpeechEditorClient.UpdateSessionAsync(SelectedSession.SessionId, request, cancellationToken);

        if (session != null)
        {
          var index = Sessions.IndexOf(SelectedSession);
          var updatedItem = new EditorSessionItem(session);
          Sessions[index] = updatedItem;
          SelectedSession = updatedItem;
          StatusMessage = ResourceHelper.GetString("TextSpeechEditor.SessionUpdated", "Session updated");
        }
      }
      catch (OperationCanceledException)
      {
        return; // User cancelled
      }
      catch (Exception ex)
      {
        ErrorMessage = ResourceHelper.FormatString("TextSpeechEditor.UpdateSessionFailed", ex.Message);
        await HandleErrorAsync(ex, "UpdateSession");
      }
      finally
      {
        IsLoading = false;
      }
    }

    private async Task DeleteSessionAsync(CancellationToken cancellationToken)
    {
      if (SelectedSession == null)
      {
        ErrorMessage = ResourceHelper.GetString("TextSpeechEditor.NoSessionSelected", "No session selected");
        return;
      }

      IsLoading = true;
      ErrorMessage = null;

      try
      {
        await _textSpeechEditorClient.DeleteSessionAsync(SelectedSession.SessionId, cancellationToken);

        var sessionToDelete = SelectedSession;
        var originalIndex = Sessions.IndexOf(sessionToDelete);
        Sessions.Remove(sessionToDelete);
        SelectedSession = null;
        Segments.Clear();
        StatusMessage = ResourceHelper.GetString("TextSpeechEditor.SessionDeleted", "Session deleted");

        // Register undo action
        if (_undoRedoService != null && sessionToDelete != null)
        {
          var action = new DeleteTextSpeechSessionAction(
              Sessions,
              _textSpeechEditorClient,
              sessionToDelete,
              originalIndex,
              onUndo: (s) =>
              {
                SelectedSession = s;
                // Reload segments
                Segments.Clear();
                foreach (var segment in s.Segments)
                {
                  Segments.Add(segment);
                }
              },
              onRedo: (s) =>
              {
                if (SelectedSession?.SessionId == s.SessionId)
                {
                  SelectedSession = null;
                  Segments.Clear();
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
        ErrorMessage = ResourceHelper.FormatString("TextSpeechEditor.DeleteSessionFailed", ex.Message);
        await HandleErrorAsync(ex, "DeleteSession");
      }
      finally
      {
        IsLoading = false;
      }
    }

    private Task AddSegmentAsync(CancellationToken cancellationToken)
    {
      cancellationToken.ThrowIfCancellationRequested();

      if (SelectedSession == null)
      {
        ErrorMessage = ResourceHelper.GetString("TextSpeechEditor.NoSessionSelected", "No session selected");
        return Task.CompletedTask;
      }

      var newSegment = new TextSegmentItem
      {
        Id = $"seg-{Guid.NewGuid()}",
        Text = string.Empty,
        StartTime = Segments.Count > 0 ? Segments.Last().EndTime : 0.0,
        EndTime = Segments.Count > 0 ? Segments.Last().EndTime + 1.0 : 1.0
      };

      Segments.Add(newSegment);
      SelectedSession.Segments.Add(newSegment);
      SelectedSegment = newSegment;

      // Register undo action
      if (_undoRedoService != null)
      {
        var action = new AddTextSegmentAction(
            Segments,
            SelectedSession,
            newSegment,
            onUndo: (s) =>
            {
              if (SelectedSegment?.Id == s.Id)
              {
                SelectedSegment = null;
              }
            },
            onRedo: (s) => SelectedSegment = s);
        _undoRedoService.RegisterAction(action);
      }

      return Task.CompletedTask;
    }

    private Task RemoveSegmentAsync(CancellationToken cancellationToken)
    {
      cancellationToken.ThrowIfCancellationRequested();

      if (SelectedSegment == null || SelectedSession == null)
      {
        return Task.CompletedTask;
      }

      var segmentToRemove = SelectedSegment;
      var originalIndex = Segments.IndexOf(segmentToRemove);
      Segments.Remove(segmentToRemove);
      SelectedSession.Segments.Remove(segmentToRemove);
      SelectedSegment = null;

      // Register undo action
      if (_undoRedoService != null && segmentToRemove != null)
      {
        var action = new RemoveTextSegmentAction(
            Segments,
            SelectedSession,
            segmentToRemove,
            originalIndex,
            onUndo: (s) => SelectedSegment = s,
            onRedo: (s) =>
            {
              if (SelectedSegment?.Id == s.Id)
              {
                SelectedSegment = null;
              }
            });
        _undoRedoService.RegisterAction(action);
      }

      return Task.CompletedTask;
    }

    private async Task SynthesizeSessionAsync(CancellationToken cancellationToken)
    {
      if (SelectedSession == null)
      {
        ErrorMessage = ResourceHelper.GetString("TextSpeechEditor.NoSessionSelected", "No session selected");
        return;
      }

      if (string.IsNullOrEmpty(SelectedVoiceProfileId))
      {
        ErrorMessage = ResourceHelper.GetString("TextSpeechEditor.VoiceProfileRequired", "Voice profile must be selected");
        return;
      }

      IsLoading = true;
      ErrorMessage = null;

      try
      {
        var request = new
        {
          session_id = SelectedSession.SessionId,
          voice_profile_id = SelectedVoiceProfileId,
          engine = SelectedEngine,
          output_format = "wav"
        };

        var response = await _textSpeechEditorClient.SynthesizeSessionAsync(SelectedSession.SessionId, request, cancellationToken);

        if (response != null)
        {
          StatusMessage = $"Synthesis complete: {response.AudioId}";
        }
      }
      catch (OperationCanceledException)
      {
        return; // User cancelled
      }
      catch (Exception ex)
      {
        ErrorMessage = ResourceHelper.FormatString("TextSpeechEditor.SynthesizeFailed", ex.Message);
        await HandleErrorAsync(ex, "SynthesizeSession");
      }
      finally
      {
        IsLoading = false;
      }
    }

    private async Task PreviewSynthesisAsync(CancellationToken cancellationToken)
    {
      if (string.IsNullOrWhiteSpace(EditedTranscript) || string.IsNullOrWhiteSpace(SelectedVoiceProfileId))
      {
        ErrorMessage = ResourceHelper.GetString("TextSpeechEditor.TranscriptAndProfileRequired", "Transcript and voice profile are required for preview");
        return;
      }

      IsLoading = true;
      ErrorMessage = null;

      try
      {
        var request = new
        {
          content = EditedTranscript,
          profile_id = SelectedVoiceProfileId,
          engine = SelectedEngine ?? "xtts"
        };

        var response = await _textSpeechEditorClient.PreviewSynthesisAsync(request, cancellationToken);

        if (response != null)
        {
          PreviewAudioId = response.AudioId;
          PreviewAudioUrl = $"/api/audio/{response.AudioId}";
          StatusMessage = ResourceHelper.GetString("TextSpeechEditor.PreviewGenerated", "Preview generated successfully");
        }
      }
      catch (OperationCanceledException)
      {
        return; // User cancelled
      }
      catch (Exception ex)
      {
        ErrorMessage = $"Failed to generate preview: {ex.Message}";
        await HandleErrorAsync(ex, "PreviewSynthesis");
      }
      finally
      {
        IsLoading = false;
      }
    }

    private async Task PlayPreviewAsync(CancellationToken cancellationToken)
    {
      if (string.IsNullOrEmpty(PreviewAudioId) && string.IsNullOrEmpty(PreviewAudioUrl))
        return;

      try
      {
        if (!string.IsNullOrEmpty(PreviewAudioId))
        {
          var baseUrl = AppServices.GetService<BackendClientConfig>()?.BaseUrl?.TrimEnd('/')
              ?? "http://localhost:8000";
          await _audioPlayer.PlayBackendAudioIdAsync(PreviewAudioId, baseUrl);
        }
        else if (!string.IsNullOrEmpty(PreviewAudioUrl))
        {
          if (PreviewAudioUrl.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
              || PreviewAudioUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
          {
            await _audioPlayer.PlayUrlAsync(PreviewAudioUrl);
          }
          else
          {
            var baseUrl = AppServices.GetService<BackendClientConfig>()?.BaseUrl?.TrimEnd('/')
                ?? "http://localhost:8000";
            var audioId = PreviewAudioUrl.TrimStart('/').Replace("api/audio/", "");
            if (!string.IsNullOrEmpty(audioId))
              await _audioPlayer.PlayBackendAudioIdAsync(audioId, baseUrl);
          }
        }
      }
      catch (Exception ex)
      {
        await HandleErrorAsync(ex, "PlayPreview");
      }
    }

    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
      await LoadSessionsAsync(cancellationToken);
      await LoadAvailableProjectsAsync(cancellationToken);
      await LoadAvailableVoiceProfilesAsync(cancellationToken);
      await LoadAvailableEnginesAsync(cancellationToken);
      StatusMessage = ResourceHelper.GetString("TextSpeechEditor.Refreshed", "Refreshed");
    }

    private async Task LoadAvailableProjectsAsync(CancellationToken cancellationToken)
    {
      try
      {
        var projects = await _projectsClient.GetProjectsAsync(cancellationToken);
        AvailableProjects.Clear();
        foreach (var project in projects)
        {
          AvailableProjects.Add(project.Id);
        }
      }
      catch (OperationCanceledException)
      {
        return; // User cancelled
      }
      catch (Exception ex)
      {
        ErrorMessage = $"Failed to load projects: {ex.Message}";
        await HandleErrorAsync(ex, "LoadAvailableProjects");
      }
    }

    private async Task LoadAvailableVoiceProfilesAsync(CancellationToken cancellationToken)
    {
      try
      {
        var profiles = await _profilesClient.GetProfilesAsync(cancellationToken);
        AvailableVoiceProfiles.Clear();
        foreach (var profile in profiles)
        {
          AvailableVoiceProfiles.Add(profile.Id);
        }
      }
      catch (OperationCanceledException)
      {
        return; // User cancelled
      }
      catch (Exception ex)
      {
        ErrorMessage = ResourceHelper.FormatString("TextSpeechEditor.LoadVoiceProfilesFailed", ex.Message);
        await HandleErrorAsync(ex, "LoadAvailableVoiceProfiles");
      }
    }

    private async Task LoadAvailableEnginesAsync(CancellationToken cancellationToken)
    {
      try
      {
        // Use the new GetEnginesAsync method for direct engine discovery
        var engines = await _textSpeechEditorClient.GetEnginesAsync(cancellationToken);
        AvailableEngines.Clear();
        foreach (var engine in engines)
        {
          AvailableEngines.Add(engine);
        }

      }
      catch (OperationCanceledException)
      {
        return; // User cancelled
      }
      catch (Exception ex)
      {
        await HandleErrorAsync(ex, "LoadAvailableEngines");
      }
    }

  }

  // Data models
  public class EditorSessionItem : ObservableObject
  {
    public string SessionId { get; set; }
    public string? ProjectId { get; set; }
    public string Title { get; set; }
    public ObservableCollection<TextSegmentItem> Segments { get; set; }
    public string? AudioId { get; set; }
    public string Language { get; set; }
    public string Created { get; set; }
    public string Modified { get; set; }
    public string DurationDisplay => $"{Segments.Sum(s => s.EndTime - s.StartTime):F2}s";
    public string SegmentCountDisplay => $"{Segments.Count} segments";

    public EditorSessionItem(EditorSessionModel session)
    {
      SessionId = session.SessionId;
      ProjectId = session.ProjectId;
      Title = session.Title;
      Segments = new ObservableCollection<TextSegmentItem>(
          session.Segments.Select(s => new TextSegmentItem(s))
      );
      AudioId = session.AudioId;
      Language = session.Language;
      Created = session.Created;
      Modified = session.Modified;
    }
  }

  public class TextSegmentItem : ObservableObject
  {
    public string Id { get; set; }
    public string Text { get; set; }
    public double StartTime { get; set; }
    public double EndTime { get; set; }
    public string? Speaker { get; set; }
    public Dictionary<string, object>? Prosody { get; set; }
    public ObservableCollection<string> Phonemes { get; set; }
    public string? Notes { get; set; }
    public string TimeRangeDisplay => $"{StartTime:F2}s - {EndTime:F2}s";
    public string DurationDisplay => $"{EndTime - StartTime:F2}s";

    public TextSegmentItem(TextSegmentModel segment)
    {
      Id = segment.Id;
      Text = segment.Text;
      StartTime = segment.StartTime;
      EndTime = segment.EndTime;
      Speaker = segment.Speaker;
      Prosody = segment.Prosody;
      Phonemes = segment.Phonemes != null
          ? new ObservableCollection<string>(segment.Phonemes)
          : new ObservableCollection<string>();
      Notes = segment.Notes;
    }

    public TextSegmentItem()
    {
      Id = string.Empty;
      Text = string.Empty;
      Phonemes = new ObservableCollection<string>();
    }
  }
}