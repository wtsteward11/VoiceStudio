using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VoiceStudio.App.Logging;
using VoiceStudio.Core.Panels;
using VoiceStudio.Core.Services;
using VoiceStudio.App.Utilities;
using TranscriptionSegmentDataModel = VoiceStudio.App.ViewModels.TextBasedSpeechEditorViewModel.TranscriptionSegmentData;
using AlignSegmentDataModel = VoiceStudio.App.ViewModels.TextBasedSpeechEditorViewModel.AlignSegmentData;
using AlignWordDataModel = VoiceStudio.App.ViewModels.TextBasedSpeechEditorViewModel.AlignWordData;
using WordTimestampDataModel = VoiceStudio.App.ViewModels.TextBasedSpeechEditorViewModel.WordTimestampData;

namespace VoiceStudio.App.ViewModels
{
  /// <summary>
  /// ViewModel for the TextBasedSpeechEditorView panel - Edit audio by editing its transcript.
  /// </summary>
  public partial class TextBasedSpeechEditorViewModel : BaseViewModel, IPanelView
  {
    private readonly IBackendClient _backendClient;
    private readonly IProfilesClient _profilesClient;

    public string PanelId => "text-based-speech-editor";
    public string DisplayName => ResourceHelper.GetString("Panel.TextBasedSpeechEditor.DisplayName", "Text-Based Speech Editor");
    public PanelRegion Region => PanelRegion.Center;

    [ObservableProperty]
    private string? audioId;

    [ObservableProperty]
    private string? originalTranscript;

    [ObservableProperty]
    private string? editedTranscript;

    [ObservableProperty]
    private ObservableCollection<TranscriptSegmentItem> segments = new();

    [ObservableProperty]
    private TranscriptSegmentItem? selectedSegment;

    [ObservableProperty]
    private WordAlignmentItem? selectedWord;

    [ObservableProperty]
    private string? editSessionId;

    [ObservableProperty]
    private bool showWaveform = true;

    [ObservableProperty]
    private bool showABComparison;

    [ObservableProperty]
    private string? replacementText;

    [ObservableProperty]
    private string? insertText;

    [ObservableProperty]
    private float insertPosition;

    [ObservableProperty]
    private string? selectedProfileId;

    [ObservableProperty]
    private ObservableCollection<string> availableProfiles = new();

    [ObservableProperty]
    private string? selectedEngine = "xtts";

    [ObservableProperty]
    private string? selectedQualityMode = "standard";

    [ObservableProperty]
    private ObservableCollection<string> availableEngines = new();

    [ObservableProperty]
    private ObservableCollection<string> qualityModes = new() { "fast", "standard", "high", "ultra" };

    [ObservableProperty]
    private ObservableCollection<string> fillerWords = new() { "um", "uh", "er", "ah", "like", "you know" };

    [ObservableProperty]
    private int removedFillerWordCount;

    [ObservableProperty]
    private string? finalAudioId;

    [ObservableProperty]
    private string? finalAudioUrl;

    public TextBasedSpeechEditorViewModel(IViewModelContext context, IBackendClient backendClient, IProfilesClient profilesClient)
        : base(context)
    {
      _backendClient = backendClient ?? throw new ArgumentNullException(nameof(backendClient));
      _profilesClient = profilesClient ?? throw new ArgumentNullException(nameof(profilesClient));

      LoadAudioCommand = new EnhancedAsyncRelayCommand(async (ct) =>
      {
        using var profiler = PerformanceProfiler.StartCommand("LoadAudio");
        await LoadAudioAsync(ct);
      }, () => !string.IsNullOrWhiteSpace(AudioId) && !IsLoading);
      TranscribeCommand = new EnhancedAsyncRelayCommand(async (ct) =>
      {
        using var profiler = PerformanceProfiler.StartCommand("Transcribe");
        await TranscribeAsync(ct);
      }, () => !string.IsNullOrWhiteSpace(AudioId) && !IsLoading);
      AlignTranscriptCommand = new EnhancedAsyncRelayCommand(async (ct) =>
      {
        using var profiler = PerformanceProfiler.StartCommand("AlignTranscript");
        await AlignTranscriptAsync(ct);
      }, () => !string.IsNullOrWhiteSpace(OriginalTranscript) && !IsLoading);
      DeleteWordCommand = new EnhancedAsyncRelayCommand(async (ct) =>
      {
        using var profiler = PerformanceProfiler.StartCommand("DeleteWord");
        await DeleteWordAsync(ct);
      }, () => SelectedWord != null && !IsLoading);
      ReplaceWordCommand = new EnhancedAsyncRelayCommand(async (ct) =>
      {
        using var profiler = PerformanceProfiler.StartCommand("ReplaceWord");
        await ReplaceWordAsync(ct);
      }, () => SelectedWord != null && !string.IsNullOrWhiteSpace(ReplacementText) && !IsLoading);
      InsertTextCommand = new EnhancedAsyncRelayCommand(async (ct) =>
      {
        using var profiler = PerformanceProfiler.StartCommand("InsertText");
        await InsertTextAsync(ct);
      }, () => !string.IsNullOrWhiteSpace(InsertText) && !string.IsNullOrWhiteSpace(SelectedProfileId) && !IsLoading);
      RemoveFillerWordsCommand = new EnhancedAsyncRelayCommand(async (ct) =>
      {
        using var profiler = PerformanceProfiler.StartCommand("RemoveFillerWords");
        await RemoveFillerWordsAsync(ct);
      }, () => !IsLoading);
      ApplyEditsCommand = new EnhancedAsyncRelayCommand(async (ct) =>
      {
        using var profiler = PerformanceProfiler.StartCommand("ApplyEdits");
        await ApplyEditsAsync(ct);
      }, () => !string.IsNullOrWhiteSpace(EditSessionId) && !IsLoading);
      LoadProfilesCommand = new EnhancedAsyncRelayCommand(async (ct) =>
      {
        using var profiler = PerformanceProfiler.StartCommand("LoadProfiles");
        await LoadProfilesAsync(ct);
      }, () => !IsLoading);
      RefreshCommand = new EnhancedAsyncRelayCommand(async (ct) =>
      {
        using var profiler = PerformanceProfiler.StartCommand("Refresh");
        await RefreshAsync(ct);
      }, () => !IsLoading);

      _ = LoadEnginesAsync(new CancellationTokenSource(TimeSpan.FromSeconds(30)).Token);
    }

    public IAsyncRelayCommand LoadAudioCommand { get; }
    public IAsyncRelayCommand TranscribeCommand { get; }
    public IAsyncRelayCommand AlignTranscriptCommand { get; }
    public IAsyncRelayCommand DeleteWordCommand { get; }
    public IAsyncRelayCommand ReplaceWordCommand { get; }
    public IAsyncRelayCommand InsertTextCommand { get; }
    public IAsyncRelayCommand RemoveFillerWordsCommand { get; }
    public IAsyncRelayCommand ApplyEditsCommand { get; }
    public IAsyncRelayCommand LoadProfilesCommand { get; }
    public IAsyncRelayCommand RefreshCommand { get; }

    partial void OnAudioIdChanged(string? value)
    {
      LoadAudioCommand.NotifyCanExecuteChanged();
      TranscribeCommand.NotifyCanExecuteChanged();
    }

    partial void OnOriginalTranscriptChanged(string? value)
    {
      AlignTranscriptCommand.NotifyCanExecuteChanged();
    }

    partial void OnSelectedWordChanged(WordAlignmentItem? value)
    {
      DeleteWordCommand.NotifyCanExecuteChanged();
      ReplaceWordCommand.NotifyCanExecuteChanged();
    }

    partial void OnReplacementTextChanged(string? value)
    {
      ReplaceWordCommand.NotifyCanExecuteChanged();
    }

    partial void OnInsertTextChanged(string? value)
    {
      InsertTextCommand.NotifyCanExecuteChanged();
    }

    partial void OnSelectedProfileIdChanged(string? value)
    {
      InsertTextCommand.NotifyCanExecuteChanged();
    }

    partial void OnEditSessionIdChanged(string? value)
    {
      ApplyEditsCommand.NotifyCanExecuteChanged();
    }

    private async Task LoadAudioAsync(CancellationToken cancellationToken)
    {
      if (string.IsNullOrWhiteSpace(AudioId))
      {
        return;
      }

      IsLoading = true;
      ErrorMessage = null;

      try
      {
        StatusMessage = ResourceHelper.GetString("TextBasedSpeechEditor.LoadingAudio", "Loading audio...");
      }
      catch (OperationCanceledException)
      {
        return; // User cancelled
      }
      catch (Exception ex)
      {
        await HandleErrorAsync(ex, "LoadAudio");
      }
      finally
      {
        IsLoading = false;
      }
    }

    private async Task TranscribeAsync(CancellationToken cancellationToken)
    {
      if (string.IsNullOrWhiteSpace(AudioId))
      {
        return;
      }

      try
      {
        IsLoading = true;
        ErrorMessage = null;

        var request = new TranscriptionRequest
        {
          AudioId = AudioId,
          Engine = "whisper",
          Language = "en",
          WordTimestamps = true
        };

        var response = await _backendClient.SendRequestAsync<TranscriptionRequest, TranscriptionResponse>(
            "/api/transcribe/",
            request,
            System.Net.Http.HttpMethod.Post,
            cancellationToken
        );

        if (response != null)
        {
          OriginalTranscript = response.Text;
          EditedTranscript = response.Text;

          // Convert segments
          if (response.Segments != null)
          {
            Segments.Clear();
            foreach (var seg in response.Segments)
            {
              Segments.Add(new TranscriptSegmentItem(seg));
            }
          }

          // Create edit session
          var sessionRequest = new Dictionary<string, object>
                    {
                        { "audio_id", AudioId },
                        { "transcript", OriginalTranscript }
                    };

          var sessionResponse = await _backendClient.SendRequestAsync<Dictionary<string, object>, Dictionary<string, object>>(
              "/api/edit/session/create",
              sessionRequest,
              System.Net.Http.HttpMethod.Post,
              cancellationToken
          );

          if (sessionResponse?.ContainsKey("session_id") == true)
          {
            EditSessionId = sessionResponse["session_id"]?.ToString();
          }

          StatusMessage = ResourceHelper.GetString("TextBasedSpeechEditor.TranscriptionCompleted", "Transcription completed");
        }
      }
      catch (Exception ex)
      {
        ErrorMessage = ResourceHelper.FormatString("TextBasedSpeechEditor.TranscribeFailed", ex.Message);
      }
      finally
      {
        IsLoading = false;
      }
    }

    private async Task AlignTranscriptAsync(CancellationToken cancellationToken)
    {
      if (string.IsNullOrWhiteSpace(OriginalTranscript) || string.IsNullOrWhiteSpace(AudioId))
      {
        return;
      }

      IsLoading = true;
      ErrorMessage = null;

      try
      {
        var request = new AlignRequest
        {
          AudioId = AudioId,
          Transcript = OriginalTranscript,
          Language = "en"
        };

        var response = await _backendClient.SendRequestAsync<AlignRequest, AlignResponse>(
            "/api/edit/align",
            request,
            System.Net.Http.HttpMethod.Post,
            cancellationToken
        );

        if (response != null)
        {
          Segments.Clear();
          foreach (var seg in response.Segments)
          {
            Segments.Add(new TranscriptSegmentItem(seg));
          }

          StatusMessage = ResourceHelper.FormatString("TextBasedSpeechEditor.TranscriptAligned", response.AlignmentConfidence);
        }
      }
      catch (OperationCanceledException)
      {
        return; // User cancelled
      }
      catch (Exception ex)
      {
        await HandleErrorAsync(ex, "AlignTranscript");
      }
      finally
      {
        IsLoading = false;
      }
    }

    private Task DeleteWordAsync(CancellationToken cancellationToken)
    {
      if (SelectedWord == null || SelectedSegment == null)
      {
        return Task.CompletedTask;
      }

      try
      {
        SelectedSegment.Words.Remove(SelectedWord);
        SelectedWord = null;

        // Update segment text
        SelectedSegment.Text = string.Join(" ", SelectedSegment.Words.Select(w => w.Word));

        StatusMessage = ResourceHelper.GetString("TextBasedSpeechEditor.WordDeleted", "Word deleted");
      }
      catch (Exception ex)
      {
        ErrorMessage = ResourceHelper.FormatString("TextBasedSpeechEditor.DeleteWordFailed", ex.Message);
      }

      return Task.CompletedTask;
    }

    private async Task ReplaceWordAsync(CancellationToken cancellationToken)
    {
      if (SelectedWord == null || SelectedSegment == null || string.IsNullOrWhiteSpace(ReplacementText) || string.IsNullOrWhiteSpace(EditSessionId))
      {
        return;
      }

      IsLoading = true;
      ErrorMessage = null;

      try
      {
        var segmentIndex = Segments.IndexOf(SelectedSegment);
        var wordIndex = SelectedSegment.Words.IndexOf(SelectedWord);

        var request = new ReplaceWordRequest
        {
          SessionId = EditSessionId,
          SegmentIndex = segmentIndex,
          WordIndex = wordIndex,
          NewText = ReplacementText,
          ProfileId = SelectedProfileId ?? "",
          Engine = SelectedEngine ?? "xtts",
          QualityMode = SelectedQualityMode ?? "standard"
        };

        var response = await _backendClient.SendRequestAsync<ReplaceWordRequest, ReplaceWordResponse>(
            "/api/edit/replace-word",
            request,
            System.Net.Http.HttpMethod.Post,
            cancellationToken
        );

        if (response != null)
        {
          // Update segment
          SelectedSegment.Words[wordIndex].Word = ReplacementText;
          SelectedSegment.Text = string.Join(" ", SelectedSegment.Words.Select(w => w.Word));

          ReplacementText = null;
          StatusMessage = ResourceHelper.GetString("TextBasedSpeechEditor.WordReplaced", "Word replaced");
        }
      }
      catch (OperationCanceledException)
      {
        return; // User cancelled
      }
      catch (Exception ex)
      {
        await HandleErrorAsync(ex, "ReplaceWord");
      }
      finally
      {
        IsLoading = false;
      }
    }

    private async Task InsertTextAsync(CancellationToken cancellationToken)
    {
      if (string.IsNullOrWhiteSpace(InsertText) || string.IsNullOrWhiteSpace(SelectedProfileId) || string.IsNullOrWhiteSpace(EditSessionId))
      {
        return;
      }

      try
      {
        IsLoading = true;
        ErrorMessage = null;

        var request = new InsertTextRequest
        {
          SessionId = EditSessionId,
          Position = InsertPosition,
          Text = InsertText,
          ProfileId = SelectedProfileId,
          Engine = SelectedEngine ?? "xtts",
          QualityMode = SelectedQualityMode ?? "standard"
        };

        var response = await _backendClient.SendRequestAsync<InsertTextRequest, InsertTextResponse>(
            "/api/edit/insert-text",
            request,
            System.Net.Http.HttpMethod.Post
        );

        if (response != null)
        {
          // Add new segments
          foreach (var seg in response.NewSegments)
          {
            Segments.Add(new TranscriptSegmentItem(seg));
          }

          InsertText = null;
          StatusMessage = ResourceHelper.GetString("TextBasedSpeechEditor.TextInserted", "Text inserted");
        }
      }
      catch (Exception ex)
      {
        ErrorMessage = ResourceHelper.FormatString("TextBasedSpeechEditor.InsertTextFailed", ex.Message);
      }
      finally
      {
        IsLoading = false;
      }
    }

    private async Task RemoveFillerWordsAsync(CancellationToken cancellationToken)
    {
      if (string.IsNullOrWhiteSpace(EditSessionId))
      {
        return;
      }

      IsLoading = true;
      ErrorMessage = null;

      try
      {
        var request = new RemoveFillerWordsRequest
        {
          SessionId = EditSessionId,
          FillerWords = FillerWords.ToList()
        };

        var response = await _backendClient.SendRequestAsync<RemoveFillerWordsRequest, RemoveFillerWordsResponse>(
            "/api/edit/remove-filler-words",
            request,
            System.Net.Http.HttpMethod.Post,
            cancellationToken
        );

        if (response != null)
        {
          EditedTranscript = response.UpdatedTranscript;
          RemovedFillerWordCount = response.RemovedCount;
          StatusMessage = ResourceHelper.FormatString("TextBasedSpeechEditor.FillerWordsRemoved", response.RemovedCount);
        }
      }
      catch (OperationCanceledException)
      {
        return; // User cancelled
      }
      catch (Exception ex)
      {
        await HandleErrorAsync(ex, "RemoveFillerWords");
      }
      finally
      {
        IsLoading = false;
      }
    }

    private async Task ApplyEditsAsync(CancellationToken cancellationToken)
    {
      if (string.IsNullOrWhiteSpace(EditSessionId))
      {
        return;
      }

      try
      {
        IsLoading = true;
        ErrorMessage = null;

        var request = new ApplyEditsRequest
        {
          SessionId = EditSessionId
        };

        var response = await _backendClient.SendRequestAsync<ApplyEditsRequest, ApplyEditsResponse>(
            "/api/edit/apply",
            request,
            System.Net.Http.HttpMethod.Post,
            cancellationToken
        );

        if (response != null)
        {
          FinalAudioId = response.FinalAudioId;
          FinalAudioUrl = response.FinalAudioUrl;
          StatusMessage = ResourceHelper.FormatString("TextBasedSpeechEditor.EditsApplied", response.EditCount);
        }
      }
      catch (Exception ex)
      {
        ErrorMessage = ResourceHelper.FormatString("TextBasedSpeechEditor.ApplyEditsFailed", ex.Message);
      }
      finally
      {
        IsLoading = false;
      }
    }

    private async Task LoadProfilesAsync(CancellationToken cancellationToken)
    {
      try
      {
        var profiles = await _profilesClient.GetProfilesAsync(cancellationToken);
        AvailableProfiles.Clear();
        foreach (var profile in profiles)
        {
          AvailableProfiles.Add(profile.Id);
        }
      }
      catch (OperationCanceledException)
      {
        return; // User cancelled
      }
      catch (Exception ex)
      {
        await HandleErrorAsync(ex, "LoadProfiles");
      }
    }

    private async Task LoadEnginesAsync(CancellationToken cancellationToken)
    {
      try
      {
        var engines = await _backendClient.GetEnginesAsync(cancellationToken);
        AvailableEngines.Clear();
        foreach (var eng in engines)
          AvailableEngines.Add(eng);
      }
      catch (OperationCanceledException) { Debug.WriteLine("TextBasedSpeechEditorViewModel: LoadEnginesAsync cancelled"); }
      catch (Exception ex) { ErrorLogger.LogWarning($"Best effort operation failed: {ex.Message}", "TextBasedSpeechEditorViewModel.LoadEnginesAsync"); }
    }

    private async Task RefreshAsync(CancellationToken cancellationToken)
    {
      try
      {
        await LoadEnginesAsync(cancellationToken);
        await LoadProfilesAsync(cancellationToken);
        StatusMessage = ResourceHelper.GetString("TextBasedSpeechEditor.Refreshed", "Refreshed");
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

    // Request/Response models
    private class TranscriptionRequest
    {
      public string AudioId { get; set; } = string.Empty;
      public string Engine { get; set; } = "whisper";
      public string? Language { get; set; }
      public bool WordTimestamps { get; set; }
    }

    private class TranscriptionResponse
    {
      public string Text { get; set; } = string.Empty;
      public List<TranscriptionSegmentData>? Segments { get; set; }
    }

    public class TranscriptionSegmentData
    {
      public string Text { get; set; } = string.Empty;
      public double Start { get; set; }
      public double End { get; set; }
      public List<WordTimestampData>? Words { get; set; }
    }

    public class WordTimestampData
    {
      public string Word { get; set; } = string.Empty;
      public double Start { get; set; }
      public double End { get; set; }
      public double? Confidence { get; set; }
    }

    private class AlignRequest
    {
      public string AudioId { get; set; } = string.Empty;
      public string Transcript { get; set; } = string.Empty;
      public string Language { get; set; } = "en";
    }

    private class AlignResponse
    {
      public List<AlignSegmentData> Segments { get; set; } = new();
      public float AlignmentConfidence { get; set; }
    }

    public class AlignSegmentData
    {
      public string Text { get; set; } = string.Empty;
      public float StartTime { get; set; }
      public float EndTime { get; set; }
      public List<AlignWordData> Words { get; set; } = new();
    }

    public class AlignWordData
    {
      public string Word { get; set; } = string.Empty;
      public float StartTime { get; set; }
      public float EndTime { get; set; }
      public float Confidence { get; set; }
    }

    private class ReplaceWordRequest
    {
      public string SessionId { get; set; } = string.Empty;
      public int SegmentIndex { get; set; }
      public int WordIndex { get; set; }
      public string NewText { get; set; } = string.Empty;
      public string ProfileId { get; set; } = string.Empty;
      public string Engine { get; set; } = "xtts";
      public string QualityMode { get; set; } = "standard";
    }

    private class ReplaceWordResponse
    {
      public string ReplacedAudioId { get; set; } = string.Empty;
      public string ReplacedAudioUrl { get; set; } = string.Empty;
      public float Duration { get; set; }
      public List<TranscriptionSegmentData> UpdatedSegments { get; set; } = new();
    }

    private class InsertTextRequest
    {
      public string SessionId { get; set; } = string.Empty;
      public float Position { get; set; }
      public string Text { get; set; } = string.Empty;
      public string ProfileId { get; set; } = string.Empty;
      public string Engine { get; set; } = "xtts";
      public string QualityMode { get; set; } = "standard";
    }

    private class InsertTextResponse
    {
      public string InsertedAudioId { get; set; } = string.Empty;
      public string InsertedAudioUrl { get; set; } = string.Empty;
      public float Duration { get; set; }
      public List<TranscriptionSegmentData> NewSegments { get; set; } = new();
    }

    private class RemoveFillerWordsRequest
    {
      public string SessionId { get; set; } = string.Empty;
      public List<string> FillerWords { get; set; } = new();
    }

    private class RemoveFillerWordsResponse
    {
      public string UpdatedTranscript { get; set; } = string.Empty;
      public int RemovedCount { get; set; }
      public List<string> RemovedWords { get; set; } = new();
    }

    private class ApplyEditsRequest
    {
      public string SessionId { get; set; } = string.Empty;
      public string? OutputName { get; set; }
    }

    private class ApplyEditsResponse
    {
      public string FinalAudioId { get; set; } = string.Empty;
      public string FinalAudioUrl { get; set; } = string.Empty;
      public float Duration { get; set; }
      public int EditCount { get; set; }
    }
  }

  // Data models
  public class TranscriptSegmentItem : ObservableObject
  {
    public string Text { get; set; }
    public float StartTime { get; set; }
    public float EndTime { get; set; }
    public ObservableCollection<WordAlignmentItem> Words { get; set; }

    public string TimeRangeDisplay => $"{StartTime:F2}s - {EndTime:F2}s";
    public float Duration => EndTime - StartTime;

    public TranscriptSegmentItem(TranscriptionSegmentDataModel data)
    {
      Text = data.Text;
      StartTime = (float)data.Start;
      EndTime = (float)data.End;
      Words = new ObservableCollection<WordAlignmentItem>();

      if (data.Words != null)
      {
        foreach (var word in data.Words)
        {
          Words.Add(new WordAlignmentItem(word));
        }
      }
    }

    public TranscriptSegmentItem(AlignSegmentDataModel data)
    {
      Text = data.Text;
      StartTime = data.StartTime;
      EndTime = data.EndTime;
      Words = new ObservableCollection<WordAlignmentItem>();

      if (data.Words != null)
      {
        foreach (var word in data.Words)
        {
          Words.Add(new WordAlignmentItem(word));
        }
      }
    }
  }

  public class WordAlignmentItem : ObservableObject
  {
    public string Word { get; set; }
    public float StartTime { get; set; }
    public float EndTime { get; set; }
    public float Confidence { get; set; }

    public string TimeRangeDisplay => $"{StartTime:F2}s - {EndTime:F2}s";
    public float Duration => EndTime - StartTime;
    public string ConfidenceDisplay => $"{Confidence:P0}";

    public WordAlignmentItem(WordTimestampDataModel data)
    {
      Word = data.Word;
      StartTime = (float)data.Start;
      EndTime = (float)data.End;
      Confidence = (float)(data.Confidence ?? 0.9f);
    }

    public WordAlignmentItem(AlignWordDataModel data)
    {
      Word = data.Word;
      StartTime = data.StartTime;
      EndTime = data.EndTime;
      Confidence = data.Confidence;
    }
  }
}