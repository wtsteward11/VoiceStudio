using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Xaml;
using VoiceStudio.Core.Models;
using VoiceStudio.Core.Panels;
using VoiceStudio.Core.Services;
using VoiceStudio.App.Services;
using VoiceStudio.App.Utilities;
using VoiceStudio.App.ViewModels;

namespace VoiceStudio.App.Views.Panels
{
  /// <summary>
  /// ViewModel for A/B Testing panel.
  /// Implements IDEA 46: A/B Testing Interface for Quality Comparison.
  /// </summary>
  public partial class ABTestingViewModel : BaseViewModel, IPanelView
  {
    private readonly IABTestService _abTestService;
    private readonly IProfilesClient _profilesClient;
    private readonly IAudioPlayerService _audioPlayer;

    public string PanelId => PanelIds.ABTesting;
    public string DisplayName => ResourceHelper.GetString("Panel.ABTesting.DisplayName", "A/B Testing");
    public PanelRegion Region => PanelRegion.Center;

    [ObservableProperty]
    private ObservableCollection<VoiceProfile> profiles = new();

    [ObservableProperty]
    private VoiceProfile? selectedProfile;

    [ObservableProperty]
    private string testText = string.Empty;

    [ObservableProperty]
    private string engineA = "xtts";

    [ObservableProperty]
    private string? emotionA;

    [ObservableProperty]
    private bool enhanceQualityA = true;

    [ObservableProperty]
    private string engineB = "tortoise";

    [ObservableProperty]
    private string? emotionB;

    [ObservableProperty]
    private bool enhanceQualityB = true;

    [ObservableProperty]
    private ABTestResponse? testResults;

    public bool HasResults => TestResults != null;

    // XAML compiler stability: bind Visibility to Visibility-typed properties (avoid bool->Visibility x:Bind).
    public Visibility ResultsVisibility => HasResults ? Visibility.Visible : Visibility.Collapsed;
    public Visibility ErrorVisibility => HasError ? Visibility.Visible : Visibility.Collapsed;

    public ABTestResult? SampleA => TestResults?.SampleA;

    public ABTestResult? SampleB => TestResults?.SampleB;

    public bool CanRunTest => SelectedProfile != null && !string.IsNullOrWhiteSpace(TestText) && !IsLoading;

    public bool CanPlaySampleA => SampleA != null && !string.IsNullOrWhiteSpace(SampleA.AudioUrl);

    public bool CanPlaySampleB => SampleB != null && !string.IsNullOrWhiteSpace(SampleB.AudioUrl);

    public string SampleAMetricsDisplay
    {
      get
      {
        if (SampleA?.QualityMetrics == null)
          return "No metrics available";

        var qm = SampleA.QualityMetrics;
        return $"MOS: {qm.MosScore:F2}\n" +
               $"Similarity: {qm.Similarity:F3}\n" +
               $"Naturalness: {qm.Naturalness:F3}\n" +
               $"SNR: {qm.SnrDb:F1} dB";
      }
    }

    public string SampleBMetricsDisplay
    {
      get
      {
        if (SampleB?.QualityMetrics == null)
          return "No metrics available";

        var qm = SampleB.QualityMetrics;
        return $"MOS: {qm.MosScore:F2}\n" +
               $"Similarity: {qm.Similarity:F3}\n" +
               $"Naturalness: {qm.Naturalness:F3}\n" +
               $"SNR: {qm.SnrDb:F1} dB";
      }
    }

    public string ComparisonSummary
    {
      get
      {
        if (TestResults?.Comparison == null || TestResults.Comparison.Count == 0)
          return "No comparison data available";

        var comp = TestResults.Comparison;
        var overallWinner = comp.ContainsKey("overall_winner")
            ? comp["overall_winner"]?.ToString()
            : "Unknown";

        return $"Overall Winner: Sample {overallWinner}\n" +
               "See detailed metrics above for per-metric winners.";
      }
    }

    public IAsyncRelayCommand RunTestCommand { get; }
    public CommunityToolkit.Mvvm.Input.IRelayCommand PlaySampleACommand { get; }
    public CommunityToolkit.Mvvm.Input.IRelayCommand PlaySampleBCommand { get; }

    public ABTestingViewModel(IViewModelContext context, IABTestService abTestService, IProfilesClient profilesClient, IAudioPlayerService audioPlayer)
        : base(context)
    {
      _abTestService = abTestService ?? throw new ArgumentNullException(nameof(abTestService));
      _profilesClient = profilesClient ?? throw new ArgumentNullException(nameof(profilesClient));
      _audioPlayer = audioPlayer ?? throw new ArgumentNullException(nameof(audioPlayer));

      PropertyChanged += (_, e) =>
      {
        if (e.PropertyName == nameof(HasError))
          OnPropertyChanged(nameof(ErrorVisibility));
      };

      RunTestCommand = new EnhancedAsyncRelayCommand(async (ct) =>
      {
        using var profiler = PerformanceProfiler.StartCommand("RunABTest");
        await RunTestAsync(ct);
      }, () => CanRunTest);
      PlaySampleACommand = new CommunityToolkit.Mvvm.Input.RelayCommand(PlaySampleA, () => CanPlaySampleA);
      PlaySampleBCommand = new CommunityToolkit.Mvvm.Input.RelayCommand(PlaySampleB, () => CanPlaySampleB);

      _ = LoadProfilesAsync(CancellationToken.None);
    }

    partial void OnTestResultsChanged(ABTestResponse? value)
    {
      OnPropertyChanged(nameof(ResultsVisibility));
      OnPropertyChanged(nameof(SampleA));
      OnPropertyChanged(nameof(SampleB));
      OnPropertyChanged(nameof(SampleAMetricsDisplay));
      OnPropertyChanged(nameof(SampleBMetricsDisplay));
      OnPropertyChanged(nameof(ComparisonSummary));
    }

    private async Task LoadProfilesAsync(CancellationToken cancellationToken)
    {
      IsLoading = true;
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
        ErrorMessage = $"Failed to load profiles: {ex.Message}";
        await HandleErrorAsync(ex, "LoadProfiles");
      }
      finally
      {
        IsLoading = false;
      }
    }

    private async Task RunTestAsync(CancellationToken cancellationToken)
    {
      if (SelectedProfile == null || string.IsNullOrWhiteSpace(TestText))
        return;

      IsLoading = true;
      ErrorMessage = null;

      try
      {
        var request = new ABTestRequest
        {
          ProfileId = SelectedProfile.Id,
          Text = TestText,
          Language = "en",
          EngineA = EngineA,
          EmotionA = EmotionA,
          EnhanceQualityA = EnhanceQualityA,
          EngineB = EngineB,
          EmotionB = EmotionB,
          EnhanceQualityB = EnhanceQualityB
        };

        TestResults = await _abTestService.RunABTestAsync(request, cancellationToken);

        // Notify property changes
        OnPropertyChanged(nameof(HasResults));
        OnPropertyChanged(nameof(SampleA));
        OnPropertyChanged(nameof(SampleB));
        OnPropertyChanged(nameof(CanPlaySampleA));
        OnPropertyChanged(nameof(CanPlaySampleB));
        OnPropertyChanged(nameof(SampleAMetricsDisplay));
        OnPropertyChanged(nameof(SampleBMetricsDisplay));
        OnPropertyChanged(nameof(ComparisonSummary));
      }
      catch (OperationCanceledException)
      {
        return; // User cancelled
      }
      catch (Exception ex)
      {
        ErrorMessage = $"A/B test failed: {ex.Message}";
        await HandleErrorAsync(ex, "RunABTest");
      }
      finally
      {
        IsLoading = false;
        RunTestCommand.NotifyCanExecuteChanged();
      }
    }

    private async void PlaySampleA()
    {
      if (SampleA == null || string.IsNullOrWhiteSpace(SampleA.AudioUrl))
        return;

      try
      {
        // Stop any existing playback
        _audioPlayer.Stop();
        
        var audioStream = await _abTestService.GetAudioStreamAsync(SampleA.AudioId);
        if (audioStream != null)
        {
          // Play audio using audio player service
          await _audioPlayer.PlayStreamAsync(audioStream, sampleRate: 22050, channels: 1);
        }
      }
      catch (Exception ex)
      {
        ErrorMessage = $"Failed to play sample A: {ex.Message}";
      }
    }

    private async void PlaySampleB()
    {
      if (SampleB == null || string.IsNullOrWhiteSpace(SampleB.AudioUrl))
        return;

      try
      {
        // Stop any existing playback
        _audioPlayer.Stop();
        
        var audioStream = await _abTestService.GetAudioStreamAsync(SampleB.AudioId);
        if (audioStream != null)
        {
          // Play audio using audio player service
          await _audioPlayer.PlayStreamAsync(audioStream, sampleRate: 22050, channels: 1);
        }
      }
      catch (Exception ex)
      {
        ErrorMessage = $"Failed to play sample B: {ex.Message}";
      }
    }

    partial void OnSelectedProfileChanged(VoiceProfile? value)
    {
      RunTestCommand.NotifyCanExecuteChanged();
    }

    partial void OnTestTextChanged(string value)
    {
      RunTestCommand.NotifyCanExecuteChanged();
    }
  }
}