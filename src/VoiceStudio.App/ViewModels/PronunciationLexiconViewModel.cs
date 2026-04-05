using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VoiceStudio.Core.Panels;
using VoiceStudio.Core.Services;
using VoiceStudio.Core.Models;
using VoiceStudio.App.Services;
using VoiceStudio.App.Utilities;
using Windows.Storage.Pickers;
using Windows.Storage;

namespace VoiceStudio.App.ViewModels
{
  /// <summary>
  /// ViewModel for the PronunciationLexiconView panel - Custom pronunciation management.
  /// </summary>
  public partial class PronunciationLexiconViewModel : BaseViewModel, IPanelView
  {
    private readonly IPronunciationLexiconClient _lexiconClient;
    private readonly IProfilesClient _profilesClient;
    private readonly IVoiceSynthesisService _voiceSynthesisService;
    private readonly IAudioPlayerService _audioPlayer;

    public string PanelId => PanelIds.PronunciationLexicon;
    public string DisplayName => ResourceHelper.GetString("Panel.PronunciationLexicon.DisplayName", "Pronunciation Lexicon");
    public PanelRegion Region => PanelRegion.Right;

    [ObservableProperty]
    private ObservableCollection<PronunciationLexiconEntryItem> entries = new();

    [ObservableProperty]
    private PronunciationLexiconEntryItem? selectedEntry;

    [ObservableProperty]
    private string? newWord;

    [ObservableProperty]
    private string? newPronunciation;

    [ObservableProperty]
    private string? searchQuery;

    [ObservableProperty]
    private string? selectedLanguage = "en";

    [ObservableProperty]
    private ObservableCollection<string> availableLanguages = new() { "en", "es", "fr", "de", "it", "pt", "ja", "zh" };

    [ObservableProperty]
    private string? phonemeEstimate;

    [ObservableProperty]
    private float phonemeConfidence;

    [ObservableProperty]
    private string? testAudioId;

    [ObservableProperty]
    private string? testAudioUrl;

    [ObservableProperty]
    private ObservableCollection<string> conflicts = new();

    [ObservableProperty]
    private ObservableCollection<string> validationErrors = new();

    [ObservableProperty]
    private bool isValid = true;

    public PronunciationLexiconViewModel(IViewModelContext context, IPronunciationLexiconClient lexiconClient, IProfilesClient profilesClient, IVoiceSynthesisService voiceSynthesisService, IAudioPlayerService audioPlayer)
        : base(context)
    {
      _lexiconClient = lexiconClient ?? throw new ArgumentNullException(nameof(lexiconClient));
      _profilesClient = profilesClient ?? throw new ArgumentNullException(nameof(profilesClient));
      _voiceSynthesisService = voiceSynthesisService ?? throw new ArgumentNullException(nameof(voiceSynthesisService));
      _audioPlayer = audioPlayer ?? throw new ArgumentNullException(nameof(audioPlayer));

      LoadEntriesCommand = new AsyncRelayCommand(LoadEntriesAsync);
      AddEntryCommand = new AsyncRelayCommand(AddEntryAsync, () => !string.IsNullOrWhiteSpace(NewWord) && !string.IsNullOrWhiteSpace(NewPronunciation) && IsValid);
      UpdateEntryCommand = new AsyncRelayCommand(UpdateEntryAsync, () => SelectedEntry != null && !string.IsNullOrWhiteSpace(NewPronunciation) && IsValid);
      DeleteEntryCommand = new AsyncRelayCommand(DeleteEntryAsync, () => SelectedEntry != null);
      EstimatePhonemesCommand = new AsyncRelayCommand(EstimatePhonemesAsync, () => !string.IsNullOrWhiteSpace(NewWord));
      TestPronunciationCommand = new AsyncRelayCommand(TestPronunciationAsync, () => SelectedEntry != null);
      SearchCommand = new AsyncRelayCommand(SearchEntriesAsync);
      RefreshCommand = new AsyncRelayCommand(RefreshAsync);
      ValidatePronunciationCommand = new RelayCommand(ValidatePronunciation, () => !string.IsNullOrWhiteSpace(NewPronunciation));
      ExportLexiconCommand = new AsyncRelayCommand(ExportLexiconAsync, () => Entries.Count > 0);
      ImportLexiconCommand = new AsyncRelayCommand(ImportLexiconAsync);
      PlayCommand = new AsyncRelayCommand(PlayTestAudioAsync, () => (!string.IsNullOrEmpty(TestAudioId) || !string.IsNullOrEmpty(TestAudioUrl)) && !IsLoading);

      PropertyChanged += (_, e) =>
      {
        if (e.PropertyName is nameof(TestAudioId) or nameof(TestAudioUrl) or nameof(IsLoading))
          PlayCommand.NotifyCanExecuteChanged();
      };
    }

    public IAsyncRelayCommand LoadEntriesCommand { get; }
    public IAsyncRelayCommand AddEntryCommand { get; }
    public IAsyncRelayCommand UpdateEntryCommand { get; }
    public IAsyncRelayCommand DeleteEntryCommand { get; }
    public IAsyncRelayCommand EstimatePhonemesCommand { get; }
    public IAsyncRelayCommand TestPronunciationCommand { get; }
    public IAsyncRelayCommand SearchCommand { get; }
    public IAsyncRelayCommand RefreshCommand { get; }
    public IRelayCommand ValidatePronunciationCommand { get; }
    public IAsyncRelayCommand ExportLexiconCommand { get; }
    public IAsyncRelayCommand ImportLexiconCommand { get; }
    public IAsyncRelayCommand PlayCommand { get; }

    private async Task PlayTestAudioAsync()
    {
      if (string.IsNullOrEmpty(TestAudioId) && string.IsNullOrEmpty(TestAudioUrl))
        return;
      var baseUrl = AppServices.GetService<BackendClientConfig>()?.BaseUrl?.TrimEnd('/') ?? BackendClientConfig.DefaultHttpBaseUrl;
      if (!string.IsNullOrEmpty(TestAudioId))
        await _audioPlayer.PlayBackendAudioIdAsync(TestAudioId, baseUrl);
      else if (!string.IsNullOrEmpty(TestAudioUrl))
        await _audioPlayer.PlayUrlAsync(TestAudioUrl.StartsWith("http") ? TestAudioUrl : $"{baseUrl}{TestAudioUrl}");
    }

    partial void OnNewWordChanged(string? value)
    {
      AddEntryCommand.NotifyCanExecuteChanged();
      EstimatePhonemesCommand.NotifyCanExecuteChanged();
    }

    partial void OnNewPronunciationChanged(string? value)
    {
      AddEntryCommand.NotifyCanExecuteChanged();
      UpdateEntryCommand.NotifyCanExecuteChanged();
      ValidatePronunciationCommand.NotifyCanExecuteChanged();
      ValidatePronunciation();
    }

    partial void OnSelectedEntryChanged(PronunciationLexiconEntryItem? value)
    {
      UpdateEntryCommand.NotifyCanExecuteChanged();
      DeleteEntryCommand.NotifyCanExecuteChanged();
      TestPronunciationCommand.NotifyCanExecuteChanged();

      if (value != null)
      {
        NewWord = value.Word;
        NewPronunciation = value.Pronunciation;
      }
    }

    private async Task LoadEntriesAsync()
    {
      try
      {
        IsLoading = true;
        ErrorMessage = null;

        var entries = await _lexiconClient.GetEntriesAsync(SelectedLanguage);

        if (entries != null)
        {
          Entries.Clear();
          foreach (var entry in entries)
          {
            Entries.Add(new PronunciationLexiconEntryItem(entry));
          }

          // Check for conflicts
          CheckConflicts();

          StatusMessage = ResourceHelper.FormatString("PronunciationLexicon.EntriesLoaded", Entries.Count);
        }
      }
      catch (Exception ex)
      {
        await HandleErrorAsync(ex, "LoadEntries");
      }
      finally
      {
        IsLoading = false;
      }
    }

    private async Task AddEntryAsync()
    {
      if (string.IsNullOrWhiteSpace(NewWord) || string.IsNullOrWhiteSpace(NewPronunciation))
      {
        return;
      }

      if (!IsValid)
      {
        ErrorMessage = ResourceHelper.GetString("PronunciationLexicon.FixValidationErrors", "Please fix validation errors before adding entry");
        return;
      }

      try
      {
        IsLoading = true;
        ErrorMessage = null;

        var response = await _lexiconClient.AddEntryAsync(
            NewWord,
            NewPronunciation,
            SelectedLanguage ?? "en",
            null,
            null
        );

        if (response != null)
        {
          Entries.Add(new PronunciationLexiconEntryItem(response));
          CheckConflicts();

          NewWord = null;
          NewPronunciation = null;
          StatusMessage = ResourceHelper.FormatString("PronunciationLexicon.EntryAdded", response.Word);
        }
      }
      catch (Exception ex)
      {
        await HandleErrorAsync(ex, "AddEntry");
      }
      finally
      {
        IsLoading = false;
      }
    }

    private async Task UpdateEntryAsync()
    {
      if (SelectedEntry == null || string.IsNullOrWhiteSpace(NewPronunciation))
      {
        return;
      }

      if (!IsValid)
      {
        ErrorMessage = ResourceHelper.GetString("PronunciationLexicon.FixValidationErrorsUpdate", "Please fix validation errors before updating entry");
        return;
      }

      try
      {
        IsLoading = true;
        ErrorMessage = null;

        var response = await _lexiconClient.UpdateEntryAsync(
            SelectedEntry.Word,
            NewPronunciation,
            SelectedEntry.Language,
            SelectedEntry.PartOfSpeech,
            SelectedEntry.Notes
        );

        if (response != null)
        {
          var index = Entries.IndexOf(SelectedEntry);
          if (index >= 0)
          {
            Entries[index] = new PronunciationLexiconEntryItem(response);
          }

          CheckConflicts();
          StatusMessage = ResourceHelper.FormatString("PronunciationLexicon.EntryUpdated", response.Word);
        }
      }
      catch (Exception ex)
      {
        await HandleErrorAsync(ex, "UpdateEntry");
      }
      finally
      {
        IsLoading = false;
      }
    }

    private async Task DeleteEntryAsync()
    {
      if (SelectedEntry == null)
      {
        return;
      }

      try
      {
        IsLoading = true;
        ErrorMessage = null;

        await _lexiconClient.DeleteEntryAsync(SelectedEntry.Word);

        Entries.Remove(SelectedEntry);
        SelectedEntry = null;
        CheckConflicts();

        StatusMessage = ResourceHelper.GetString("PronunciationLexicon.EntryDeleted", "Entry deleted");
      }
      catch (Exception ex)
      {
        await HandleErrorAsync(ex, "DeleteEntry");
      }
      finally
      {
        IsLoading = false;
      }
    }

    private async Task EstimatePhonemesAsync()
    {
      if (string.IsNullOrWhiteSpace(NewWord))
      {
        return;
      }

      try
      {
        IsLoading = true;
        ErrorMessage = null;

        var response = await _lexiconClient.EstimatePhonemesAsync(NewWord, SelectedLanguage ?? "en");

        if (response != null)
        {
          PhonemeEstimate = response.Pronunciation;
          PhonemeConfidence = response.Confidence;
          NewPronunciation = response.Pronunciation;
          StatusMessage = ResourceHelper.FormatString("PronunciationLexicon.PronunciationEstimated", response.Confidence);
        }
      }
      catch (Exception ex)
      {
        ErrorMessage = ResourceHelper.FormatString("PronunciationLexicon.EstimatePhonemesFailed", ex.Message);
      }
      finally
      {
        IsLoading = false;
      }
    }

    private async Task TestPronunciationAsync()
    {
      if (SelectedEntry == null)
      {
        return;
      }

      try
      {
        IsLoading = true;
        ErrorMessage = null;

        // Use voice synthesis to test pronunciation
        var profiles = await _profilesClient.GetProfilesAsync();
        if (profiles == null || profiles.Count == 0)
        {
          ErrorMessage = ResourceHelper.GetString("PronunciationLexicon.NoProfilesForTesting", "No voice profiles available for testing");
          return;
        }

        var profile = profiles.First();

        // Synthesize the word to test pronunciation (enables playback)
        var synthRequest = new VoiceSynthesisRequest
        {
          ProfileId = profile.Id,
          Text = SelectedEntry.Word,
          Language = SelectedLanguage ?? "en",
        };
        var synthResponse = await _voiceSynthesisService.SynthesizeVoiceAsync(synthRequest);
        if (synthResponse != null)
        {
          TestAudioId = synthResponse.AudioId;
          TestAudioUrl = synthResponse.AudioUrl;
        }

        StatusMessage = ResourceHelper.FormatString("PronunciationLexicon.PronunciationTested", SelectedEntry.Word);
      }
      catch (Exception ex)
      {
        ErrorMessage = ResourceHelper.FormatString("PronunciationLexicon.TestPronunciationFailed", ex.Message);
      }
      finally
      {
        IsLoading = false;
      }
    }

    private async Task SearchEntriesAsync()
    {
      await LoadEntriesAsync();
    }

    private async Task RefreshAsync()
    {
      await LoadEntriesAsync();
      StatusMessage = ResourceHelper.GetString("PronunciationLexicon.Refreshed", "Refreshed");
    }

    private void CheckConflicts()
    {
      Conflicts.Clear();
      var wordGroups = Entries.GroupBy(e => e.Word.ToLower());
      foreach (var group in wordGroups.Where(g => g.Count() > 1))
      {
        Conflicts.Add(ResourceHelper.FormatString("PronunciationLexicon.ConflictMultipleEntries", group.Key, string.Join(", ", group.Select(e => e.Pronunciation))));
      }
    }

    private void ValidatePronunciation()
    {
      ValidationErrors.Clear();

      if (string.IsNullOrWhiteSpace(NewPronunciation))
      {
        IsValid = true;
        return;
      }

      var pronunciation = NewPronunciation.Trim();
      var errors = new List<string>();

      // Check for valid IPA/phoneme format
      // IPA typically uses /.../ or [...] notation
      var hasDelimiters = (pronunciation.StartsWith("/") && pronunciation.EndsWith("/")) ||
                         (pronunciation.StartsWith("[") && pronunciation.EndsWith("]"));

      if (!hasDelimiters && pronunciation.Length > 0)
      {
        // Check if it's a phoneme string (alphanumeric with dots, dashes, etc.)
        var isValidPhonemeString = System.Text.RegularExpressions.Regex.IsMatch(
            pronunciation,
            @"^[a-zA-Z0-9\.\-\s]+$"
        );

        if (!isValidPhonemeString)
        {
          errors.Add(ResourceHelper.GetString("PronunciationLexicon.ValidationIPAFormat", "Pronunciation should be in IPA format (/.../) or phoneme notation"));
        }
      }

      // Check for common invalid characters
      var invalidChars = new[] { '@', '#', '$', '%', '^', '&', '*', '(', ')', '=', '+', '{', '}', '|', '\\', ':', ';', '"', '\'', '<', '>', ',', '?', '!' };
      if (invalidChars.Any(c => pronunciation.Contains(c)))
      {
        errors.Add(ResourceHelper.GetString("PronunciationLexicon.ValidationInvalidChars", "Pronunciation contains invalid characters"));
      }

      // Check minimum length
      var cleanPronunciation = pronunciation.Trim('/', '[', ']');
      if (cleanPronunciation.Length < 1)
      {
        errors.Add(ResourceHelper.GetString("PronunciationLexicon.ValidationEmpty", "Pronunciation cannot be empty"));
      }

      // Check maximum length (reasonable limit)
      if (cleanPronunciation.Length > 200)
      {
        errors.Add(ResourceHelper.GetString("PronunciationLexicon.ValidationTooLong", "Pronunciation is too long (maximum 200 characters)"));
      }

      ValidationErrors.Clear();
      foreach (var error in errors)
      {
        ValidationErrors.Add(error);
      }

      var wasValid = IsValid;
      IsValid = errors.Count == 0;

      // Notify commands if validation state changed
      if (wasValid != IsValid)
      {
        AddEntryCommand.NotifyCanExecuteChanged();
        UpdateEntryCommand.NotifyCanExecuteChanged();
      }
    }

    private async Task ExportLexiconAsync()
    {
      try
      {
        IsLoading = true;
        ErrorMessage = null;

        // Create export data
        var exportData = new
        {
          language = SelectedLanguage ?? "en",
          entries = Entries.Select(e => new
          {
            word = e.Word,
            pronunciation = e.Pronunciation,
            language = e.Language,
            part_of_speech = e.PartOfSpeech,
            notes = e.Notes
          }).ToList(),
          exported = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ"),
          version = "1.0"
        };

        // Convert to JSON
        var json = System.Text.Json.JsonSerializer.Serialize(exportData, new System.Text.Json.JsonSerializerOptions
        {
          WriteIndented = true
        });

        // Use file picker to save
        var picker = new FileSavePicker();
        picker.SuggestedStartLocation = PickerLocationId.DocumentsLibrary;
        picker.FileTypeChoices.Add("JSON", new[] { ".json" });
        picker.FileTypeChoices.Add("Text", new[] { ".txt" });
        picker.SuggestedFileName = $"pronunciation_lexicon_{SelectedLanguage ?? "en"}_{DateTime.Now:yyyyMMdd}";

        var file = await picker.PickSaveFileAsync();
        if (file != null)
        {
          await FileIO.WriteTextAsync(file, json);
          StatusMessage = ResourceHelper.FormatString("PronunciationLexicon.EntriesExported", Entries.Count, file.Name);
        }
      }
      catch (Exception ex)
      {
        ErrorMessage = ResourceHelper.FormatString("PronunciationLexicon.ExportLexiconFailed", ex.Message);
      }
      finally
      {
        IsLoading = false;
      }
    }

    private async Task ImportLexiconAsync()
    {
      try
      {
        IsLoading = true;
        ErrorMessage = null;

        // Use file picker to open
        var picker = new FileOpenPicker();
        picker.SuggestedStartLocation = PickerLocationId.DocumentsLibrary;
        picker.FileTypeFilter.Add(".json");
        picker.FileTypeFilter.Add(".txt");

        // WinUI 3 requires initializing the picker with the window handle
        var window = App.MainWindowInstance;
        if (window != null)
        {
          var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(window);
          WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);
        }

        var file = await picker.PickSingleFileAsync();
        if (file == null)
        {
          return;
        }

        var json = await FileIO.ReadTextAsync(file);
        var importData = System.Text.Json.JsonSerializer.Deserialize<LexiconImportData>(json);

        if (importData?.Entries == null || importData.Entries.Count == 0)
        {
          ErrorMessage = ResourceHelper.GetString("PronunciationLexicon.ImportFileEmpty", "Import file contains no entries");
          return;
        }

        // Import entries
        var importedCount = 0;
        var skippedCount = 0;

        foreach (var entryData in importData.Entries)
        {
          if (string.IsNullOrWhiteSpace(entryData.Word) || string.IsNullOrWhiteSpace(entryData.Pronunciation))
          {
            skippedCount++;
            continue;
          }

          try
          {
            var response = await _lexiconClient.AddEntryAsync(
                entryData.Word,
                entryData.Pronunciation,
                entryData.Language ?? SelectedLanguage ?? "en",
                entryData.PartOfSpeech,
                entryData.Notes
            );

            if (response != null)
            {
              // Check if entry already exists
              if (!Entries.Any(e => e.Word.Equals(response.Word, StringComparison.OrdinalIgnoreCase)))
              {
                Entries.Add(new PronunciationLexiconEntryItem(response));
                importedCount++;
              }
              else
              {
                skippedCount++;
              }
            }
          }
          catch
          {
            skippedCount++;
          }
        }

        CheckConflicts();
        StatusMessage = ResourceHelper.FormatString("PronunciationLexicon.EntriesImported", importedCount, skippedCount);
      }
      catch (Exception ex)
      {
        ErrorMessage = ResourceHelper.FormatString("PronunciationLexicon.ImportLexiconFailed", ex.Message);
      }
      finally
      {
        IsLoading = false;
      }
    }

    private class LexiconImportData
    {
      public string? Language { get; set; }
      public List<LexiconEntryImportItem> Entries { get; set; } = new();
    }

    private class LexiconEntryImportItem
    {
      public string Word { get; set; } = string.Empty;
      public string Pronunciation { get; set; } = string.Empty;
      public string? Language { get; set; }
      public string? PartOfSpeech { get; set; }
      public string? Notes { get; set; }
    }

    // Request/Response models
    private class LexiconEntryRequest
    {
      public string Word { get; set; } = string.Empty;
      public string Pronunciation { get; set; } = string.Empty;
      public string Language { get; set; } = "en";
      public string? PartOfSpeech { get; set; }
      public string? Notes { get; set; }
    }

    public class LexiconEntryResponse
    {
      public string Word { get; set; } = string.Empty;
      public string Pronunciation { get; set; } = string.Empty;
      public string Language { get; set; } = "en";
      public string? PartOfSpeech { get; set; }
      public string? Notes { get; set; }
    }

    private class PhonemeEstimateRequest
    {
      public string? Word { get; set; }
      public string? AudioId { get; set; }
      public string Language { get; set; } = "en";
    }

    public class PhonemeEstimateResponse
    {
      public string Word { get; set; } = string.Empty;
      public string Pronunciation { get; set; } = string.Empty;
      public float Confidence { get; set; }
      public string Method { get; set; } = string.Empty;
    }
  }

  // Data models
  public class PronunciationLexiconEntryItem : ObservableObject
  {
    public string Word { get; set; }
    public string Pronunciation { get; set; }
    public string Language { get; set; }
    public string? PartOfSpeech { get; set; }
    public string? Notes { get; set; }

    public string DisplayText => $"{Word} → {Pronunciation}";

    public PronunciationLexiconEntryItem(PronunciationLexiconViewModel.LexiconEntryResponse response)
    {
      Word = response.Word;
      Pronunciation = response.Pronunciation;
      Language = response.Language;
      PartOfSpeech = response.PartOfSpeech;
      Notes = response.Notes;
    }
  }
}