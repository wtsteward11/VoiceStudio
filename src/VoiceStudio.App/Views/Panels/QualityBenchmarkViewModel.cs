using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VoiceStudio.App.Helpers;
using VoiceStudio.App.Services;
using VoiceStudio.App.Utilities;
using VoiceStudio.App.ViewModels;
using VoiceStudio.Core.Models;
using VoiceStudio.Core.Panels;
using VoiceStudio.Core.Services;

namespace VoiceStudio.App.Views.Panels
{
  /// <summary>
  /// ViewModel for Quality Benchmarking panel.
  /// Implements IDEA 52: Quality Benchmarking and Comparison Tool.
  /// GAP-052: side-by-side engine comparison via <see cref="IVoiceSynthesisService"/> + playback.
  /// </summary>
  public partial class QualityBenchmarkViewModel : BaseViewModel, IPanelView
  {
    private const string SettingsKeyComparisonEnginesJson = "QualityBenchmark.SelectedComparisonEnginesJson";
    private const string SettingsKeyComparisonTestText = "QualityBenchmark.ComparisonTestText";

    private static readonly JsonSerializerOptions s_jsonOptions = new() { WriteIndented = false };

    private readonly IQualityControlClient _qualityClient;
    private readonly IProfilesClient _profilesClient;
    private readonly IEnginesClient _enginesClient;
    private readonly IAudioPlayerService _audioPlayer;
    private readonly IVoiceSynthesisService _voiceSynthesisService;
    private readonly BackendClientConfig _backendConfig;
    private bool _isInitialized;

    public string PanelId => PanelIds.QualityBenchmark;
    public string DisplayName => ResourceHelper.GetString("Panel.QualityBenchmarking.DisplayName", "Quality Benchmarking");
    public PanelRegion Region => PanelRegion.Center;

    /// <summary>Product trust Pass 01 slice 4: Quality Benchmark panel is partial — not workflow-pass-closed (matrix §2).</summary>
    public string SurfaceMaturityFootnote =>
        ResourceHelper.GetString(
            "QualityBenchmark.Pass01.SurfaceMaturityFootnote",
            "Quality benchmarking is available here, but this workflow cluster is not closed under a workflow-coherence pass (no §8 proof). Treat metrics and comparisons as partial—do not assume full production quality workflow coverage.");

    [ObservableProperty]
    private ObservableCollection<VoiceProfile> profiles = new();

    [ObservableProperty]
    private VoiceProfile? selectedProfile;

    [ObservableProperty]
    private string testText = "This is a test sentence for quality benchmarking.";

    [ObservableProperty]
    private bool testXTTS = true;

    [ObservableProperty]
    private bool testChatterbox = true;

    [ObservableProperty]
    private bool testTortoise = true;

    [ObservableProperty]
    private bool enhanceQuality = true;

    /// <summary>W8-C1: resource-backed next-step copy after a successful benchmark (bindable + seam-tested).</summary>
    [ObservableProperty]
    private string? nextStepHint;

    [ObservableProperty]
    private bool isLoading;

    [ObservableProperty]
    private string? errorMessage;

    [ObservableProperty]
    private bool hasError;

    [ObservableProperty]
    private ObservableCollection<BenchmarkResultViewModel> benchmarkResults = new();

    /// <summary>GAP-052: API-driven engines with per-row selection for side-by-side comparison.</summary>
    [ObservableProperty]
    private ObservableCollection<SelectableComparisonEngineRow> comparisonEngineOptions = new();

    /// <summary>GAP-052: horizontal comparison slots (success + failure).</summary>
    [ObservableProperty]
    private ObservableCollection<ComparisonSlot> comparisonSlots = new();

    [ObservableProperty]
    private bool isComparisonRunning;

    [ObservableProperty]
    private string? preferredEngineId;

    public bool HasResults => BenchmarkResults?.Count > 0;

    public bool HasComparisonResults => ComparisonSlots?.Count > 0;

    public bool CanRunBenchmark => SelectedProfile != null && !string.IsNullOrWhiteSpace(TestText) && !IsLoading && (TestXTTS || TestChatterbox || TestTortoise);

    public bool CanRunComparison =>
        SelectedProfile != null
        && !string.IsNullOrWhiteSpace(TestText)
        && !IsComparisonRunning
        && !IsLoading
        && ComparisonEngineOptions.Count(r => r.IsSelected) >= 2;

    public string ResultsSummary
    {
      get
      {
        if (!HasResults)
          return string.Empty;

        var successful = BenchmarkResults.Count(r => r.Success);
        var total = BenchmarkResults.Count;
        return ResourceHelper.FormatString("QualityBenchmark.BenchmarkComplete", successful, total);
      }
    }

    public IAsyncRelayCommand RunBenchmarkCommand { get; }

    public IAsyncRelayCommand RunComparisonCommand { get; }

    public IRelayCommand<ComparisonSlot> SetPreferredEngineCommand { get; }

    public QualityBenchmarkViewModel(
        IViewModelContext context,
        IQualityControlClient qualityClient,
        IProfilesClient profilesClient,
        IEnginesClient enginesClient,
        IAudioPlayerService audioPlayer,
        IVoiceSynthesisService voiceSynthesisService,
        BackendClientConfig backendConfig)
        : base(context)
    {
      _qualityClient = qualityClient ?? throw new ArgumentNullException(nameof(qualityClient));
      _profilesClient = profilesClient ?? throw new ArgumentNullException(nameof(profilesClient));
      _enginesClient = enginesClient ?? throw new ArgumentNullException(nameof(enginesClient));
      _audioPlayer = audioPlayer ?? throw new ArgumentNullException(nameof(audioPlayer));
      _voiceSynthesisService = voiceSynthesisService ?? throw new ArgumentNullException(nameof(voiceSynthesisService));
      _backendConfig = backendConfig ?? throw new ArgumentNullException(nameof(backendConfig));

      RunBenchmarkCommand = new EnhancedAsyncRelayCommand(async (ct) =>
      {
        using var profiler = PerformanceProfiler.StartCommand("RunBenchmark");
        await RunBenchmarkAsync(ct);
      }, () => CanRunBenchmark);

      RunComparisonCommand = new EnhancedAsyncRelayCommand(async (ct) =>
      {
        using var profiler = PerformanceProfiler.StartCommand("RunComparison");
        await RunComparisonAsync(ct);
      }, () => CanRunComparison);

      SetPreferredEngineCommand = new RelayCommand<ComparisonSlot>(SetPreferredEngine, s => s != null);
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
      if (_isInitialized)
        return;
      _isInitialized = true;

      RestoreComparisonSessionText();
      await LoadProfilesAsync(cancellationToken);
      await LoadComparisonEnginesAsync(cancellationToken);
    }

    private void RestoreComparisonSessionText()
    {
      var saved = UnpackagedSettingsHelper.GetValue<string?>(SettingsKeyComparisonTestText, null);
      if (!string.IsNullOrWhiteSpace(saved))
      {
        TestText = saved;
      }
    }

    private void SetPreferredEngine(ComparisonSlot? slot)
    {
      if (slot == null)
      {
        return;
      }

      foreach (var s in ComparisonSlots)
      {
        s.IsPreferred = ReferenceEquals(s, slot) && slot.IsSuccess;
      }

      PreferredEngineId = slot.IsSuccess ? slot.EngineId : null;
    }

    private async Task LoadComparisonEnginesAsync(CancellationToken cancellationToken)
    {
      try
      {
        var engineIds = await _enginesClient.GetEnginesAsync(cancellationToken).ConfigureAwait(true);
        var selectedFromSettings = LoadSelectedComparisonEngineIdsFromSettings();

        ComparisonEngineOptions.Clear();
        foreach (var id in engineIds.OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
        {
          var row = new SelectableComparisonEngineRow
          {
            EngineId = id,
            IsSelected = selectedFromSettings.Count > 0
              && selectedFromSettings.Contains(id, StringComparer.OrdinalIgnoreCase),
          };
          row.PropertyChanged += (_, e) =>
          {
            if (e.PropertyName == nameof(SelectableComparisonEngineRow.IsSelected))
            {
              RunComparisonCommand.NotifyCanExecuteChanged();
              PersistComparisonEngineSelection();
            }
          };
          ComparisonEngineOptions.Add(row);
        }

        if (selectedFromSettings.Count == 0 && ComparisonEngineOptions.Count >= 2)
        {
          ComparisonEngineOptions[0].IsSelected = true;
          ComparisonEngineOptions[1].IsSelected = true;
        }

        RunComparisonCommand.NotifyCanExecuteChanged();
      }
      catch (OperationCanceledException)
      {
        return;
      }
      catch (Exception ex)
      {
        ErrorMessage = ResourceHelper.FormatString("QualityBenchmark.LoadEnginesFailed", ex.Message);
        HasError = true;
        await HandleErrorAsync(ex, "LoadEngines").ConfigureAwait(true);
      }
    }

    private HashSet<string> LoadSelectedComparisonEngineIdsFromSettings()
    {
      try
      {
        var json = UnpackagedSettingsHelper.GetValue(SettingsKeyComparisonEnginesJson, "[]") ?? "[]";
        var list = JsonSerializer.Deserialize<List<string>>(json, s_jsonOptions);
        if (list == null || list.Count == 0)
        {
          return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }

        return new HashSet<string>(list, StringComparer.OrdinalIgnoreCase);
      }
      catch
      {
        return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
      }
    }

    private void PersistComparisonEngineSelection()
    {
      var selected = ComparisonEngineOptions.Where(r => r.IsSelected).Select(r => r.EngineId).ToList();
      var json = JsonSerializer.Serialize(selected, s_jsonOptions);
      UnpackagedSettingsHelper.SetValue(SettingsKeyComparisonEnginesJson, json);
    }

    private void PersistComparisonSession()
    {
      UnpackagedSettingsHelper.SetValue(SettingsKeyComparisonTestText, TestText);
      PersistComparisonEngineSelection();
    }

    private async Task RunComparisonAsync(CancellationToken cancellationToken)
    {
      if (SelectedProfile == null || string.IsNullOrWhiteSpace(TestText))
      {
        return;
      }

      var engines = ComparisonEngineOptions.Where(r => r.IsSelected).Select(r => r.EngineId).ToList();
      if (engines.Count < 2)
      {
        return;
      }

      IsComparisonRunning = true;
      HasError = false;
      ErrorMessage = null;
      ComparisonSlots.Clear();
      PreferredEngineId = null;
      OnPropertyChanged(nameof(HasComparisonResults));

      var baseUrl = _backendConfig.BaseUrl?.TrimEnd('/') ?? BackendClientConfig.DefaultHttpBaseUrl;

      try
      {
        var text = TestText.Trim();
        var tasks = new List<Task>();

        foreach (var engineId in engines)
        {
          var slot = new ComparisonSlot(_audioPlayer, () => baseUrl, engineId);
          ComparisonSlots.Add(slot);

          tasks.Add(PopulateComparisonSlotAsync(slot, text, cancellationToken));
        }

        await Task.WhenAll(tasks).ConfigureAwait(true);

        PersistComparisonSession();
        StatusMessage = ResourceHelper.GetString(
            "QualityBenchmark.ComparisonComplete",
            "Side-by-side comparison finished.");
        OnPropertyChanged(nameof(HasComparisonResults));
      }
      catch (OperationCanceledException)
      {
        return;
      }
      catch (Exception ex)
      {
        ErrorMessage = ResourceHelper.FormatString("QualityBenchmark.ComparisonFailed", ex.Message);
        HasError = true;
        await HandleErrorAsync(ex, "RunComparison").ConfigureAwait(true);
      }
      finally
      {
        IsComparisonRunning = false;
        RunComparisonCommand.NotifyCanExecuteChanged();
      }
    }

    private async Task PopulateComparisonSlotAsync(ComparisonSlot slot, string text, CancellationToken cancellationToken)
    {
      if (SelectedProfile == null)
      {
        return;
      }

      try
      {
        cancellationToken.ThrowIfCancellationRequested();

        var request = new VoiceSynthesisRequest
        {
          ProfileId = SelectedProfile.Id,
          Text = text,
          Engine = slot.EngineId,
          Language = string.IsNullOrWhiteSpace(SelectedProfile.Language) ? "en" : SelectedProfile.Language,
          EnhanceQuality = EnhanceQuality,
        };

        var response = await _voiceSynthesisService.SynthesizeVoiceAsync(request, cancellationToken).ConfigureAwait(true);

        slot.AudioId = response?.AudioId;
        slot.QualityMetrics = response?.QualityMetrics;
        slot.IsSuccess = !string.IsNullOrWhiteSpace(response?.AudioId);
        if (!slot.IsSuccess)
        {
          slot.Error = ResourceHelper.GetString("QualityBenchmark.NoAudioId", "Synthesis returned no audio id.");
        }
      }
      catch (OperationCanceledException)
      {
        throw;
      }
      catch (Exception ex)
      {
        slot.IsSuccess = false;
        slot.Error = ex.Message;
      }
      finally
      {
        slot.IsLoading = false;
      }
    }

    private async Task LoadProfilesAsync(CancellationToken cancellationToken)
    {
      IsLoading = true;
      HasError = false;
      ErrorMessage = null;

      try
      {
        var profileList = await _profilesClient.GetProfilesAsync(cancellationToken);
        Profiles.Clear();
        foreach (var profile in profileList)
        {
          Profiles.Add(profile);
        }
      }
      catch (OperationCanceledException)
      {
        return;
      }
      catch (Exception ex)
      {
        ErrorMessage = ResourceHelper.FormatString("QualityBenchmark.LoadProfilesFailed", ex.Message);
        HasError = true;
        await HandleErrorAsync(ex, "LoadProfiles");
      }
      finally
      {
        IsLoading = false;
      }
    }

    private async Task RunBenchmarkAsync(CancellationToken cancellationToken)
    {
      if (SelectedProfile == null || string.IsNullOrWhiteSpace(TestText))
        return;

      IsLoading = true;
      HasError = false;
      ErrorMessage = null;
      StatusMessage = null;
      NextStepHint = null;

      try
      {
        var engines = new List<string>();
        if (TestXTTS) engines.Add("xtts");
        if (TestChatterbox) engines.Add("chatterbox");
        if (TestTortoise) engines.Add("tortoise");

        var request = new BenchmarkRequest
        {
          ProfileId = SelectedProfile.Id,
          TestText = TestText,
          Language = "en",
          Engines = engines,
          EnhanceQuality = EnhanceQuality
        };

        var response = await _qualityClient.RunBenchmarkAsync(request, cancellationToken);

        BenchmarkResults.Clear();
        foreach (var result in response.Results)
        {
          BenchmarkResults.Add(new BenchmarkResultViewModel(result));
        }

        OnPropertyChanged(nameof(HasResults));
        OnPropertyChanged(nameof(ResultsSummary));
        StatusMessage = ResourceHelper.GetString(
            "QualityBenchmark.W8C1.RunSuccessToast",
            "Benchmark finished successfully.");
        NextStepHint = ResourceHelper.GetString(
            "QualityBenchmark.W8C1.NextStepHint",
            "Review per-engine metrics below.");
      }
      catch (OperationCanceledException)
      {
        return;
      }
      catch (Exception ex)
      {
        ErrorMessage = ResourceHelper.FormatString("QualityBenchmark.BenchmarkFailed", ex.Message);
        HasError = true;
        await HandleErrorAsync(ex, "RunBenchmark");
      }
      finally
      {
        IsLoading = false;
        RunBenchmarkCommand.NotifyCanExecuteChanged();
      }
    }

    partial void OnSelectedProfileChanged(VoiceProfile? value)
    {
      RunBenchmarkCommand.NotifyCanExecuteChanged();
      RunComparisonCommand.NotifyCanExecuteChanged();
    }

    partial void OnTestTextChanged(string value)
    {
      RunBenchmarkCommand.NotifyCanExecuteChanged();
      RunComparisonCommand.NotifyCanExecuteChanged();
    }

    partial void OnTestXTTSChanged(bool value) => RunBenchmarkCommand.NotifyCanExecuteChanged();

    partial void OnTestChatterboxChanged(bool value) => RunBenchmarkCommand.NotifyCanExecuteChanged();

    partial void OnTestTortoiseChanged(bool value) => RunBenchmarkCommand.NotifyCanExecuteChanged();

    partial void OnIsLoadingChanged(bool value)
    {
      RunBenchmarkCommand.NotifyCanExecuteChanged();
      RunComparisonCommand.NotifyCanExecuteChanged();
    }

    partial void OnIsComparisonRunningChanged(bool value) => RunComparisonCommand.NotifyCanExecuteChanged();
  }

  /// <summary>GAP-052: one row per engine from <see cref="IEnginesClient"/> with selection for comparison.</summary>
  public partial class SelectableComparisonEngineRow : ObservableObject
  {
    [ObservableProperty]
    private string engineId = string.Empty;

    [ObservableProperty]
    private bool isSelected;
  }

  /// <summary>GAP-052: one side-by-side result card.</summary>
  public partial class ComparisonSlot : ObservableObject
  {
    private readonly IAudioPlayerService _audioPlayer;
    private readonly Func<string> _baseUrl;

    public ComparisonSlot(IAudioPlayerService audioPlayer, Func<string> baseUrlGetter, string engineId)
    {
      _audioPlayer = audioPlayer ?? throw new ArgumentNullException(nameof(audioPlayer));
      _baseUrl = baseUrlGetter ?? throw new ArgumentNullException(nameof(baseUrlGetter));
      EngineId = engineId ?? throw new ArgumentNullException(nameof(engineId));
      IsLoading = true;
      PlaySlotCommand = new AsyncRelayCommand(PlayAsync, () => IsSuccess && !string.IsNullOrWhiteSpace(AudioId));
    }

    public string EngineId { get; }

    [ObservableProperty]
    private bool isLoading = true;

    [ObservableProperty]
    private bool isSuccess;

    [ObservableProperty]
    private string? error;

    [ObservableProperty]
    private string? audioId;

    [ObservableProperty]
    private QualityMetrics? qualityMetrics;

    /// <summary>Subjective MOS-style score 1–5; 0 = not yet rated. Bindable name: <c>SubjectiveScore</c> (sourcegen).</summary>
    [ObservableProperty]
    private double subjectiveScore;

    [ObservableProperty]
    private bool isPreferred;

    public IAsyncRelayCommand PlaySlotCommand { get; }

    public string MosScoreDisplay
    {
      get
      {
        var m = QualityMetrics?.MosScore;
        if (m is null || !m.HasValue || m.Value <= 0)
        {
          return ResourceHelper.GetString("QualityBenchmark.NotAvailable", "N/A");
        }

        return m.Value.ToString("F2", CultureInfo.InvariantCulture);
      }
    }

    public string SimilarityDisplay
    {
      get
      {
        var s = QualityMetrics?.Similarity;
        if (s is null || !s.HasValue || s.Value <= 0)
        {
          return ResourceHelper.GetString("QualityBenchmark.NotAvailable", "N/A");
        }

        return s.Value.ToString("F3", CultureInfo.InvariantCulture);
      }
    }

    private async Task PlayAsync()
    {
      if (string.IsNullOrWhiteSpace(AudioId))
      {
        return;
      }

      await _audioPlayer.PlayBackendAudioIdAsync(AudioId, _baseUrl()).ConfigureAwait(true);
    }

    partial void OnAudioIdChanged(string? value) => PlaySlotCommand.NotifyCanExecuteChanged();

    partial void OnIsSuccessChanged(bool value)
    {
      PlaySlotCommand.NotifyCanExecuteChanged();
      OnPropertyChanged(nameof(ShouldShowErrorText));
    }

    partial void OnIsLoadingChanged(bool value) => OnPropertyChanged(nameof(ShouldShowErrorText));

    partial void OnErrorChanged(string? value) => OnPropertyChanged(nameof(ShouldShowErrorText));

    /// <summary>True when synthesis finished with failure (not while loading).</summary>
    public bool ShouldShowErrorText => !IsLoading && !IsSuccess && !string.IsNullOrWhiteSpace(Error);

    partial void OnQualityMetricsChanged(QualityMetrics? value)
    {
      OnPropertyChanged(nameof(MosScoreDisplay));
      OnPropertyChanged(nameof(SimilarityDisplay));
    }
  }

  /// <summary>
  /// ViewModel wrapper for benchmark result display.
  /// </summary>
  public class BenchmarkResultViewModel : ObservableObject
  {
    private readonly BenchmarkResult _result;

    public string Engine => _result.Engine;
    public bool Success => _result.Success;
    public string StatusDisplay => Success
        ? ResourceHelper.GetString("QualityBenchmark.Success", "✓ Success")
        : ResourceHelper.FormatString("QualityBenchmark.Failed", _result.Error ?? string.Empty);

    public string MosScoreDisplay
    {
      get
      {
        if (_result.QualityMetrics.TryGetValue("mos_score", out var mos) && mos is double mosValue)
          return mosValue.ToString("F2");
        return ResourceHelper.GetString("QualityBenchmark.NotAvailable", "N/A");
      }
    }

    public string SimilarityDisplay
    {
      get
      {
        if (_result.QualityMetrics.TryGetValue("similarity", out var sim) && sim is double simValue)
          return simValue.ToString("F3");
        return ResourceHelper.GetString("QualityBenchmark.NotAvailable", "N/A");
      }
    }

    public string TimeDisplay
    {
      get
      {
        if (_result.Performance.TryGetValue("total_time", out var time) && time is double timeValue)
          return ResourceHelper.FormatString("QualityBenchmark.TimeDisplay", timeValue);
        return ResourceHelper.GetString("QualityBenchmark.NotAvailable", "N/A");
      }
    }

    public BenchmarkResultViewModel(BenchmarkResult result)
    {
      _result = result ?? throw new ArgumentNullException(nameof(result));
    }
  }
}
