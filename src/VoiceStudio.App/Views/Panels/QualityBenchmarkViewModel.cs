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
using VoiceStudio.App.Utilities;
using VoiceStudio.App.ViewModels;

namespace VoiceStudio.App.Views.Panels
{
  /// <summary>
  /// ViewModel for Quality Benchmarking panel.
  /// Implements IDEA 52: Quality Benchmarking and Comparison Tool.
  /// </summary>
  public partial class QualityBenchmarkViewModel : BaseViewModel, IPanelView
  {
    private readonly IQualityControlClient _qualityClient;
    private readonly IProfilesClient _profilesClient;
    private bool _isInitialized;

    public string PanelId => "quality_benchmark";
    public string DisplayName => ResourceHelper.GetString("Panel.QualityBenchmarking.DisplayName", "Quality Benchmarking");
    public PanelRegion Region => PanelRegion.Center;

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

    [ObservableProperty]
    private bool isLoading;

    [ObservableProperty]
    private string? errorMessage;

    [ObservableProperty]
    private bool hasError;

    [ObservableProperty]
    private ObservableCollection<BenchmarkResultViewModel> benchmarkResults = new();

    public bool HasResults => BenchmarkResults?.Count > 0;

    public bool CanRunBenchmark => SelectedProfile != null && !string.IsNullOrWhiteSpace(TestText) && !IsLoading && (TestXTTS || TestChatterbox || TestTortoise);

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

    public QualityBenchmarkViewModel(IViewModelContext context, IQualityControlClient qualityClient, IProfilesClient profilesClient)
        : base(context)
    {
      _qualityClient = qualityClient ?? throw new ArgumentNullException(nameof(qualityClient));
      _profilesClient = profilesClient ?? throw new ArgumentNullException(nameof(profilesClient));

      RunBenchmarkCommand = new EnhancedAsyncRelayCommand(async (ct) =>
      {
        using var profiler = PerformanceProfiler.StartCommand("RunBenchmark");
        await RunBenchmarkAsync(ct);
      }, () => CanRunBenchmark);
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
      if (_isInitialized)
        return;
      _isInitialized = true;
      await LoadProfilesAsync(cancellationToken);
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
        return; // User cancelled
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
      }
      catch (OperationCanceledException)
      {
        return; // User cancelled
      }
      catch (Exception ex)
      {
        ErrorMessage = $"Benchmark failed: {ex.Message}";
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
    }

    partial void OnTestTextChanged(string value)
    {
      RunBenchmarkCommand.NotifyCanExecuteChanged();
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