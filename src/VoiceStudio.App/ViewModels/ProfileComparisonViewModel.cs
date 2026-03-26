using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VoiceStudio.Core.Models;
using VoiceStudio.Core.Panels;
using VoiceStudio.Core.Services;
using VoiceStudio.App.Services;
using VoiceStudio.App.Utilities;

namespace VoiceStudio.App.ViewModels
{
  /// <summary>
  /// ViewModel for the ProfileComparisonView panel - Voice Profile Comparison Tool.
  /// Implements IDEA 24: Voice Profile Comparison Tool.
  /// </summary>
  public partial class ProfileComparisonViewModel : BaseViewModel, IPanelView
  {
    private readonly IVoiceSynthesisService _voiceSynthesisService;
    private readonly IProfilesClient _profilesClient;
    private readonly IAudioPlayerService _audioPlayer;
    private readonly ToastNotificationService? _toastNotificationService;
    private bool _isInitialized;

    public string PanelId => PanelIds.ProfileComparison;
    public string DisplayName => ResourceHelper.GetString("Panel.ProfileComparison.DisplayName", "Profile Comparison");
    public PanelRegion Region => PanelRegion.Center;

    [ObservableProperty]
    private ObservableCollection<VoiceProfile> availableProfiles = new();

    [ObservableProperty]
    private VoiceProfile? selectedProfileA;

    [ObservableProperty]
    private VoiceProfile? selectedProfileB;

    [ObservableProperty]
    private ProfileComparisonData? comparisonData;

    [ObservableProperty]
    private bool isComparing;

    [ObservableProperty]
    private bool isPlayingA;

    [ObservableProperty]
    private bool isPlayingB;

    [ObservableProperty]
    private string? previewText = ResourceHelper.GetString("ProfileComparison.PreviewTextDefault", "Hello, this is a comparison of two voice profiles.");

    /// <summary>Engine id applied to both synthesis requests (single policy surface; default xtts).</summary>
    [ObservableProperty]
    private string comparisonEngineId = "xtts";

    /// <summary>Selectable engines for comparison (lowercase ids aligned with synthesis backend).</summary>
    public IReadOnlyList<string> ComparisonEngineOptions { get; } = new[] { "xtts", "chatterbox", "tortoise" };

    [ObservableProperty]
    private string? audioUrlA;

    [ObservableProperty]
    private string? audioUrlB;

    public ProfileComparisonViewModel(IViewModelContext context, IVoiceSynthesisService voiceSynthesisService, IProfilesClient profilesClient, IAudioPlayerService audioPlayer)
        : base(context)
    {
      _voiceSynthesisService = voiceSynthesisService ?? throw new ArgumentNullException(nameof(voiceSynthesisService));
      _profilesClient = profilesClient ?? throw new ArgumentNullException(nameof(profilesClient));
      _audioPlayer = audioPlayer ?? throw new ArgumentNullException(nameof(audioPlayer));

      _toastNotificationService = AppServices.TryGetToastNotificationService();

      LoadProfilesCommand = new EnhancedAsyncRelayCommand(async (ct) =>
      {
        using var profiler = PerformanceProfiler.StartCommand("LoadProfiles");
        await LoadProfilesAsync(ct);
      }, () => !IsLoading);
      CompareProfilesCommand = new EnhancedAsyncRelayCommand(async (ct) =>
      {
        using var profiler = PerformanceProfiler.StartCommand("CompareProfiles");
        await CompareProfilesAsync(ct);
      }, CanExecuteCompareProfiles);
      PlayProfileACommand = new EnhancedAsyncRelayCommand(async (ct) =>
      {
        using var profiler = PerformanceProfiler.StartCommand("PlayProfileA");
        await PlayProfileAAsync(ct);
      }, () => SelectedProfileA != null && !string.IsNullOrEmpty(AudioUrlA) && !IsPlayingA && !IsLoading);
      PlayProfileBCommand = new EnhancedAsyncRelayCommand(async (ct) =>
      {
        using var profiler = PerformanceProfiler.StartCommand("PlayProfileB");
        await PlayProfileBAsync(ct);
      }, () => SelectedProfileB != null && !string.IsNullOrEmpty(AudioUrlB) && !IsPlayingB && !IsLoading);
      StopPlaybackCommand = new RelayCommand(StopPlayback, () => IsPlayingA || IsPlayingB);
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
      if (_isInitialized)
        return;
      _isInitialized = true;
      await LoadProfilesAsync(cancellationToken);
    }

    public bool HasComparisonResults => ComparisonData is not null;

    public IAsyncRelayCommand LoadProfilesCommand { get; }
    public IAsyncRelayCommand CompareProfilesCommand { get; }
    public IAsyncRelayCommand PlayProfileACommand { get; }
    public IAsyncRelayCommand PlayProfileBCommand { get; }
    public IRelayCommand StopPlaybackCommand { get; }

    private bool CanExecuteCompareProfiles()
    {
      return SelectedProfileA != null
          && SelectedProfileB != null
          && !IsComparing
          && !IsLoading
          && !string.IsNullOrWhiteSpace(PreviewText)
          && !string.IsNullOrWhiteSpace(ComparisonEngineId);
    }

    partial void OnPreviewTextChanged(string? value)
    {
      CompareProfilesCommand.NotifyCanExecuteChanged();
    }

    partial void OnComparisonEngineIdChanged(string value)
    {
      CompareProfilesCommand.NotifyCanExecuteChanged();
    }

    partial void OnComparisonDataChanged(ProfileComparisonData? value)
    {
      OnPropertyChanged(nameof(HasComparisonResults));
    }

    partial void OnSelectedProfileAChanged(VoiceProfile? value)
    {
      CompareProfilesCommand.NotifyCanExecuteChanged();
      PlayProfileACommand.NotifyCanExecuteChanged();
      if (value != null && SelectedProfileB != null)
      {
        _ = CompareProfilesAsync(CancellationToken.None); // Fire-and-forget: user-triggered auto-compare on selection change
      }
    }

    partial void OnSelectedProfileBChanged(VoiceProfile? value)
    {
      CompareProfilesCommand.NotifyCanExecuteChanged();
      PlayProfileBCommand.NotifyCanExecuteChanged();
      if (value != null && SelectedProfileA != null)
      {
        _ = CompareProfilesAsync(CancellationToken.None); // Fire-and-forget: user-triggered auto-compare on selection change
      }
    }

    partial void OnAudioUrlAChanged(string? value)
    {
      PlayProfileACommand.NotifyCanExecuteChanged();
    }

    partial void OnAudioUrlBChanged(string? value)
    {
      PlayProfileBCommand.NotifyCanExecuteChanged();
    }

    partial void OnIsPlayingAChanged(bool value)
    {
      PlayProfileACommand.NotifyCanExecuteChanged();
      StopPlaybackCommand.NotifyCanExecuteChanged();
    }

    partial void OnIsPlayingBChanged(bool value)
    {
      PlayProfileBCommand.NotifyCanExecuteChanged();
      StopPlaybackCommand.NotifyCanExecuteChanged();
    }

    private async Task LoadProfilesAsync(CancellationToken cancellationToken)
    {
      IsLoading = true;
      ErrorMessage = null;

      try
      {
        var profiles = await _profilesClient.GetProfilesAsync(cancellationToken);

        AvailableProfiles.Clear();
        foreach (var profile in profiles)
        {
          AvailableProfiles.Add(profile);
        }
      }
      catch (OperationCanceledException)
      {
        return; // User cancelled
      }
      catch (Exception ex)
      {
        await HandleErrorAsync(ex, "LoadProfiles");
        _toastNotificationService?.ShowError(
            ResourceHelper.GetString("Toast.Title.LoadProfilesFailed", "Load Profiles Failed"),
            ResourceHelper.FormatString("ProfileComparison.LoadProfilesFailed", ex.Message));
      }
      finally
      {
        IsLoading = false;
      }
    }

    private async Task CompareProfilesAsync(CancellationToken cancellationToken)
    {
      if (SelectedProfileA == null || SelectedProfileB == null || string.IsNullOrWhiteSpace(PreviewText))
        return;

      var engine = (ComparisonEngineId ?? string.Empty).Trim();
      if (string.IsNullOrEmpty(engine))
        return;

      IsComparing = true;
      ErrorMessage = null;
      ((System.Windows.Input.ICommand)CompareProfilesCommand).NotifyCanExecuteChanged();

      try
      {
        cancellationToken.ThrowIfCancellationRequested();

        var text = PreviewText.Trim();

        var requestA = new VoiceSynthesisRequest
        {
          ProfileId = SelectedProfileA.Id,
          Text = text,
          Engine = engine,
          Language = SelectedProfileA.Language ?? "en"
        };

        var responseA = await _voiceSynthesisService.SynthesizeVoiceAsync(requestA, cancellationToken);

        cancellationToken.ThrowIfCancellationRequested();

        var requestB = new VoiceSynthesisRequest
        {
          ProfileId = SelectedProfileB.Id,
          Text = text,
          Engine = engine,
          Language = SelectedProfileB.Language ?? "en"
        };

        var responseB = await _voiceSynthesisService.SynthesizeVoiceAsync(requestB, cancellationToken);

        AudioUrlA = responseA?.AudioUrl;
        AudioUrlB = responseB?.AudioUrl;

        // Create comparison data
        ComparisonData = new ProfileComparisonData
        {
          ProfileA = SelectedProfileA,
          ProfileB = SelectedProfileB,
          QualityMetricsA = responseA?.QualityMetrics,
          QualityMetricsB = responseB?.QualityMetrics,
          QualityScoreA = responseA?.QualityScore ?? 0,
          QualityScoreB = responseB?.QualityScore ?? 0,
          AudioUrlA = AudioUrlA,
          AudioUrlB = AudioUrlB
        };

        _toastNotificationService?.ShowSuccess(
            ResourceHelper.GetString("ProfileComparison.ComparisonCompleted", "Profile comparison completed"),
            ResourceHelper.GetString("Toast.Title.ComparisonComplete", "Comparison Complete"));
      }
      catch (OperationCanceledException)
      {
        return; // User cancelled
      }
      catch (Exception ex)
      {
        await HandleErrorAsync(ex, "CompareProfiles");
        _toastNotificationService?.ShowError(
            ResourceHelper.GetString("Toast.Title.ComparisonFailed", "Comparison Failed"),
            ResourceHelper.FormatString("ProfileComparison.CompareProfilesFailed", ex.Message));
      }
      finally
      {
        IsComparing = false;
        ((System.Windows.Input.ICommand)CompareProfilesCommand).NotifyCanExecuteChanged();
      }
    }

    private async Task PlayProfileAAsync(CancellationToken cancellationToken)
    {
      if (string.IsNullOrEmpty(AudioUrlA))
        return;

      IsPlayingA = true;

      try
      {
        cancellationToken.ThrowIfCancellationRequested();
        // Note: IAudioPlayerService.PlayAsync may not support CancellationToken directly
        // Play the audio file
        await _audioPlayer.PlayFileAsync(AudioUrlA);
      }
      catch (OperationCanceledException)
      {
        return; // User cancelled
      }
      catch (Exception ex)
      {
        await HandleErrorAsync(ex, "PlayProfileA");
        _toastNotificationService?.ShowError(
            ResourceHelper.GetString("Toast.Title.PlaybackFailed", "Playback Failed"),
            ResourceHelper.FormatString("ProfileComparison.PlayAudioFailed", ex.Message));
      }
      finally
      {
        IsPlayingA = false;
      }
    }

    private async Task PlayProfileBAsync(CancellationToken cancellationToken)
    {
      if (string.IsNullOrEmpty(AudioUrlB))
        return;

      IsPlayingB = true;

      try
      {
        cancellationToken.ThrowIfCancellationRequested();
        // Play the audio file
        await _audioPlayer.PlayFileAsync(AudioUrlB);
      }
      catch (OperationCanceledException)
      {
        return; // User cancelled
      }
      catch (Exception ex)
      {
        await HandleErrorAsync(ex, "PlayProfileB");
        _toastNotificationService?.ShowError(
            ResourceHelper.GetString("Toast.Title.PlaybackFailed", "Playback Failed"),
            ResourceHelper.FormatString("ProfileComparison.PlayAudioFailed", ex.Message));
      }
      finally
      {
        IsPlayingB = false;
      }
    }

    private void StopPlayback()
    {
      _audioPlayer.Stop();
      IsPlayingA = false;
      IsPlayingB = false;
    }
  }

  /// <summary>
  /// Data model for profile comparison results.
  /// </summary>
  public class ProfileComparisonData : ObservableObject
  {
    public VoiceProfile? ProfileA { get; set; }
    public VoiceProfile? ProfileB { get; set; }
    public QualityMetrics? QualityMetricsA { get; set; }
    public QualityMetrics? QualityMetricsB { get; set; }
    public double QualityScoreA { get; set; }
    public double QualityScoreB { get; set; }
    public string? AudioUrlA { get; set; }
    public string? AudioUrlB { get; set; }

    // Comparison helpers
    public string QualityScoreADisplay => $"{QualityScoreA:F2}/5.0";
    public string QualityScoreBDisplay => $"{QualityScoreB:F2}/5.0";
    public string QualityScoreDifference => $"{QualityScoreA - QualityScoreB:+0.00;-0.00;0.00}";
    public bool ProfileAIsBetter => QualityScoreA > QualityScoreB;
    public bool ProfileBIsBetter => QualityScoreB > QualityScoreA;

    public string? MosScoreA => QualityMetricsA?.MosScore?.ToString("F2");
    public string? MosScoreB => QualityMetricsB?.MosScore?.ToString("F2");
    public string? SimilarityA => QualityMetricsA?.Similarity?.ToString("P1");
    public string? SimilarityB => QualityMetricsB?.Similarity?.ToString("P1");
    public string? NaturalnessA => QualityMetricsA?.Naturalness?.ToString("P1");
    public string? NaturalnessB => QualityMetricsB?.Naturalness?.ToString("P1");
    public string? SnrDbA => QualityMetricsA?.SnrDb?.ToString("F1");
    public string? SnrDbB => QualityMetricsB?.SnrDb?.ToString("F1");
  }
}