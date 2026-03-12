using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VoiceStudio.Core.Panels;
using VoiceStudio.Core.Services;
using VoiceStudio.App.Services;
using VoiceStudio.App.Utilities;
using VoiceStudio.App.Logging;

namespace VoiceStudio.App.ViewModels
{
  /// <summary>
  /// ViewModel for the VoiceBrowserView panel - Voice browser and discovery.
  /// </summary>
  public partial class VoiceBrowserViewModel : BaseViewModel, IPanelView
  {
    private readonly IBackendClient _backendClient;
    private readonly IAudioPlayerService _audioPlayer;
    private CancellationTokenSource? _searchDebounceCts;
    private const int SearchDebounceMs = 300;

    public string PanelId => "voice-browser";
    public string DisplayName => ResourceHelper.GetString("Panel.VoiceBrowser.DisplayName", "Voice Browser");
    public PanelRegion Region => PanelRegion.Center;

    [ObservableProperty]
    private ObservableCollection<VoiceProfileSummaryItem> voices = new();

    [ObservableProperty]
    private VoiceProfileSummaryItem? selectedVoice;

    [ObservableProperty]
    private string searchQuery = string.Empty;

    [ObservableProperty]
    private string? selectedLanguage;

    [ObservableProperty]
    private string? selectedGender;

    [ObservableProperty]
    private double minQualityScore;

    [ObservableProperty]
    private ObservableCollection<string> selectedTags = new();

    [ObservableProperty]
    private ObservableCollection<string> availableLanguages = new();

    [ObservableProperty]
    private ObservableCollection<string> availableTags = new();

    [ObservableProperty]
    private int totalVoices;

    [ObservableProperty]
    private int currentPage;

    [ObservableProperty]
    private int pageSize = 50;

    public VoiceBrowserViewModel(IViewModelContext context, IBackendClient backendClient, IAudioPlayerService audioPlayer)
        : base(context)
    {
      _backendClient = backendClient ?? throw new ArgumentNullException(nameof(backendClient));
      _audioPlayer = audioPlayer ?? throw new ArgumentNullException(nameof(audioPlayer));

      SearchCommand = new EnhancedAsyncRelayCommand(async (ct) =>
      {
        using var profiler = PerformanceProfiler.StartCommand("SearchVoices");
        await SearchVoicesAsync(ct);
      }, () => !IsLoading);
      LoadLanguagesCommand = new EnhancedAsyncRelayCommand(async (ct) =>
      {
        using var profiler = PerformanceProfiler.StartCommand("LoadLanguages");
        await LoadLanguagesAsync(ct);
      }, () => !IsLoading);
      LoadTagsCommand = new EnhancedAsyncRelayCommand(async (ct) =>
      {
        using var profiler = PerformanceProfiler.StartCommand("LoadTags");
        await LoadTagsCommandAsync(ct);
      }, () => !IsLoading);
      RefreshCommand = new EnhancedAsyncRelayCommand(async (ct) =>
      {
        using var profiler = PerformanceProfiler.StartCommand("Refresh");
        await RefreshAsync(ct);
      }, () => !IsLoading);
      NextPageCommand = new EnhancedAsyncRelayCommand(async (ct) =>
      {
        using var profiler = PerformanceProfiler.StartCommand("NextPage");
        await NextPageAsync(ct);
      }, () => !IsLoading && (CurrentPage + 1) * PageSize < TotalVoices);
      PreviousPageCommand = new EnhancedAsyncRelayCommand(async (ct) =>
      {
        using var profiler = PerformanceProfiler.StartCommand("PreviousPage");
        await PreviousPageAsync(ct);
      }, () => !IsLoading && CurrentPage > 0);
      PlayCommand = new EnhancedAsyncRelayCommand(async (ct) =>
      {
        using var profiler = PerformanceProfiler.StartCommand("Play");
        await PlayPreviewAsync(ct);
      }, () => SelectedVoice != null && !string.IsNullOrEmpty(SelectedVoice.PreviewAudioId) && !IsLoading);

      PropertyChanged += (_, e) =>
      {
        if (e.PropertyName is nameof(SelectedVoice) or nameof(IsLoading))
          PlayCommand.NotifyCanExecuteChanged();
      };

      // Load initial data
      _ = LoadLanguagesAsync(CancellationToken.None);
      _ = LoadTagsCommandAsync(CancellationToken.None);
      _ = SearchVoicesAsync(CancellationToken.None);
    }

    public IAsyncRelayCommand SearchCommand { get; }
    public IAsyncRelayCommand LoadLanguagesCommand { get; }
    public IAsyncRelayCommand LoadTagsCommand { get; }
    public IAsyncRelayCommand RefreshCommand { get; }
    public IAsyncRelayCommand NextPageCommand { get; }
    public IAsyncRelayCommand PreviousPageCommand { get; }
    public IAsyncRelayCommand PlayCommand { get; }

    private async Task PlayPreviewAsync(CancellationToken cancellationToken)
    {
      var voice = SelectedVoice;
      if (voice == null || string.IsNullOrEmpty(voice.PreviewAudioId))
        return;
      var baseUrl = AppServices.GetService<BackendClientConfig>()?.BaseUrl?.TrimEnd('/') ?? "http://localhost:8000";
      await _audioPlayer.PlayBackendAudioIdAsync(voice.PreviewAudioId, baseUrl);
    }

    private async Task SearchVoicesAsync(CancellationToken cancellationToken)
    {
      IsLoading = true;
      ErrorMessage = null;

      try
      {
        var queryParams = new System.Collections.Specialized.NameValueCollection();
        if (!string.IsNullOrWhiteSpace(SearchQuery))
          queryParams.Add("query", SearchQuery);
        if (!string.IsNullOrEmpty(SelectedLanguage))
          queryParams.Add("language", SelectedLanguage);
        if (!string.IsNullOrEmpty(SelectedGender))
          queryParams.Add("gender", SelectedGender);
        if (MinQualityScore > 0.0)
          queryParams.Add("min_quality_score", MinQualityScore.ToString());
        if (SelectedTags.Count > 0)
          queryParams.Add("tags", string.Join(",", SelectedTags));
        queryParams.Add("limit", PageSize.ToString());
        queryParams.Add("offset", (CurrentPage * PageSize).ToString());

        var queryString = string.Join("&",
            queryParams.AllKeys.SelectMany(key =>
                queryParams.GetValues(key)?.Select(value => $"{key}={Uri.EscapeDataString(value)}") ?? Array.Empty<string>()
            )
        );

        var url = "/api/voice-browser/voices";
        if (!string.IsNullOrEmpty(queryString))
          url += $"?{queryString}";

        var response = await _backendClient.SendRequestAsync<object, VoiceSearchResponse>(
            url,
            null,
            System.Net.Http.HttpMethod.Get,
            cancellationToken
        );

        if (response != null)
        {
          Voices.Clear();
          foreach (var voice in response.Voices)
          {
            Voices.Add(new VoiceProfileSummaryItem(voice));
          }
          TotalVoices = response.Total;
          StatusMessage = $"Found {response.Total} voices";
        }
      }
      catch (OperationCanceledException)
      {
        return; // User cancelled
      }
      catch (Exception ex)
      {
        await HandleErrorAsync(ex, "SearchVoices");
      }
      finally
      {
        IsLoading = false;
      }
    }

    private async Task LoadLanguagesAsync(CancellationToken cancellationToken)
    {
      try
      {
        var response = await _backendClient.SendRequestAsync<object, LanguagesResponse>(
            "/api/voice-browser/languages",
            null,
            System.Net.Http.HttpMethod.Get,
            cancellationToken
        );

        if (response?.Languages != null)
        {
          AvailableLanguages.Clear();
          foreach (var lang in response.Languages)
          {
            AvailableLanguages.Add(lang);
          }
        }
      }
      catch (OperationCanceledException)
      {
        return; // User cancelled
      }
      catch (Exception ex)
      {
        await HandleErrorAsync(ex, "LoadLanguages");
      }
    }

    private async Task LoadTagsCommandAsync(CancellationToken cancellationToken)
    {
      try
      {
        var response = await _backendClient.SendRequestAsync<object, TagsResponse>(
            "/api/voice-browser/tags",
            null,
            System.Net.Http.HttpMethod.Get,
            cancellationToken
        );

        if (response?.Tags != null)
        {
          AvailableTags.Clear();
          foreach (var tag in response.Tags)
          {
            AvailableTags.Add(tag);
          }
        }
      }
      catch (OperationCanceledException)
      {
        return; // User cancelled
      }
      catch (Exception ex)
      {
        await HandleErrorAsync(ex, "LoadTags");
      }
    }

    private async Task RefreshAsync(CancellationToken cancellationToken)
    {
      try
      {
        await SearchVoicesAsync(cancellationToken);
        StatusMessage = ResourceHelper.GetString("VoiceBrowser.SearchRefreshed", "Search refreshed");
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

    private async Task NextPageAsync(CancellationToken cancellationToken)
    {
      if ((CurrentPage + 1) * PageSize < TotalVoices)
      {
        CurrentPage++;
        await SearchVoicesAsync(cancellationToken);
      }
    }

    private async Task PreviousPageAsync(CancellationToken cancellationToken)
    {
      if (CurrentPage > 0)
      {
        CurrentPage--;
        await SearchVoicesAsync(cancellationToken);
      }
    }

    partial void OnSearchQueryChanged(string value)
    {
      CurrentPage = 0;
      _searchDebounceCts?.Cancel();
      _searchDebounceCts = new CancellationTokenSource();
      var cts = _searchDebounceCts;
      _ = Task.Run(async () =>
      {
        try
        {
          await Task.Delay(SearchDebounceMs, cts.Token);
          Dispatcher.TryEnqueue(() => _ = SearchVoicesAsync(cts.Token));
        }
        catch (Exception ex)
      {
        ErrorLogger.LogWarning($"Best effort operation failed: {ex.Message}", "VoiceBrowserViewModel.OnSearchQueryChanged");
      }
      });
    }

    partial void OnSelectedLanguageChanged(string? value)
    {
      CurrentPage = 0;
      _ = SearchVoicesAsync(CancellationToken.None);
    }

    partial void OnSelectedGenderChanged(string? value)
    {
      CurrentPage = 0;
      _ = SearchVoicesAsync(CancellationToken.None);
    }

    partial void OnMinQualityScoreChanged(double value)
    {
      CurrentPage = 0;
      _ = SearchVoicesAsync(CancellationToken.None);
    }

    // Response models (public for testability)
    public class VoiceSearchResponse
    {
      public VoiceProfileSummary[] Voices { get; set; } = Array.Empty<VoiceProfileSummary>();
      public int Total { get; set; }
      public int Limit { get; set; }
      public int Offset { get; set; }
    }

    public class LanguagesResponse
    {
      public string[] Languages { get; set; } = Array.Empty<string>();
    }

    public class TagsResponse
    {
      public string[] Tags { get; set; } = Array.Empty<string>();
    }
  }

  // Data models
  public class VoiceProfileSummary
  {
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string Language { get; set; } = string.Empty;
    public string? Gender { get; set; }
    public string? AgeRange { get; set; }
    public double QualityScore { get; set; }
    public int SampleCount { get; set; }
    public string[] Tags { get; set; } = Array.Empty<string>();
    public string? PreviewAudioId { get; set; }
    public string Created { get; set; } = string.Empty;
  }

  public class VoiceProfileSummaryItem : ObservableObject
  {
    public string Id { get; set; }
    public string Name { get; set; }
    public string? Description { get; set; }
    public string Language { get; set; }
    public string? Gender { get; set; }
    public string? AgeRange { get; set; }
    public double QualityScore { get; set; }
    public int SampleCount { get; set; }
    public ObservableCollection<string> Tags { get; set; }
    public string? PreviewAudioId { get; set; }
    public string Created { get; set; }
    public string QualityScoreDisplay => $"{QualityScore:F2}";
    public string SampleCountDisplay => $"{SampleCount} samples";

    public VoiceProfileSummaryItem(VoiceProfileSummary summary)
    {
      Id = summary.Id;
      Name = summary.Name;
      Description = summary.Description;
      Language = summary.Language;
      Gender = summary.Gender;
      AgeRange = summary.AgeRange;
      QualityScore = summary.QualityScore;
      SampleCount = summary.SampleCount;
      Tags = new ObservableCollection<string>(summary.Tags);
      PreviewAudioId = summary.PreviewAudioId;
      Created = summary.Created;
    }
  }
}