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
  public partial class VoiceBrowserViewModel : BaseViewModel, IPanelView, IPanelLifecycle
  {
    private readonly IVoiceBrowserClient _voiceBrowserClient;
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

    public VoiceBrowserViewModel(IViewModelContext context, IVoiceBrowserClient voiceBrowserClient, IAudioPlayerService audioPlayer)
        : base(context)
    {
      _voiceBrowserClient = voiceBrowserClient ?? throw new ArgumentNullException(nameof(voiceBrowserClient));
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

      // No constructor fire-and-forget — load from View Loaded via OnActivatedAsync (RETAINED_ASYNC_RULE)
    }

    public Task OnActivatedAsync(CancellationToken cancellationToken = default) => LoadInitialDataAsync(cancellationToken);

    public Task OnDeactivatedAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
      try
      {
        await SearchVoicesAsync(cancellationToken);
        StatusMessage = ResourceHelper.GetString("VoiceBrowser.SearchRefreshed", "Search refreshed");
      }
      catch (OperationCanceledException)
      {
        return;
      }
      catch (Exception ex)
      {
        await HandleErrorAsync(ex, "Refresh");
      }
    }

    private async Task LoadInitialDataAsync(CancellationToken cancellationToken)
    {
      await LoadLanguagesAsync(cancellationToken);
      await LoadTagsCommandAsync(cancellationToken);
      await SearchVoicesAsync(cancellationToken);
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
      var baseUrl = AppServices.GetService<BackendClientConfig>()?.BaseUrl?.TrimEnd('/') ?? BackendClientConfig.DefaultHttpBaseUrl;
      await _audioPlayer.PlayBackendAudioIdAsync(voice.PreviewAudioId, baseUrl);
    }

    private async Task SearchVoicesAsync(CancellationToken cancellationToken)
    {
      IsLoading = true;
      ErrorMessage = null;

      try
      {
        var response = await _voiceBrowserClient.SearchVoicesAsync(
          query: string.IsNullOrWhiteSpace(SearchQuery) ? null : SearchQuery,
          language: SelectedLanguage,
          gender: SelectedGender,
          minQualityScore: MinQualityScore,
          tags: SelectedTags.Count > 0 ? SelectedTags.ToArray() : null,
          limit: PageSize,
          offset: CurrentPage * PageSize,
          cancellationToken: cancellationToken
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
        var response = await _voiceBrowserClient.GetLanguagesAsync(cancellationToken);

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
        var response = await _voiceBrowserClient.GetTagsAsync(cancellationToken);

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
  }

  /// <summary>
  /// UI wrapper for VoiceProfileSummary with observable properties.
  /// </summary>
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
