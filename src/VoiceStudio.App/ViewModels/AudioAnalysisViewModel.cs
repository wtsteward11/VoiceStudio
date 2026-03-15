using System;
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
  /// ViewModel for the AudioAnalysisView panel - Advanced audio analysis.
  /// </summary>
  public partial class AudioAnalysisViewModel : BaseViewModel, IPanelView, IPanelLifecycle
  {
    private readonly IAudioAnalysisClient _audioAnalysisClient;
    private readonly ToastNotificationService? _toastNotificationService;
    private CancellationTokenSource? _selectedAudioLoadCts;

    public string PanelId => "audio-analysis";
    public string DisplayName => ResourceHelper.GetString("Panel.AudioAnalysis.DisplayName", "Audio Analysis");
    public PanelRegion Region => PanelRegion.Right;

    [ObservableProperty]
    private string? selectedAudioId;

    [ObservableProperty]
    private ObservableCollection<string> availableAudioIds = new();

    [ObservableProperty]
    private AudioAnalysisResultItem? analysisResult;

    [ObservableProperty]
    private bool includeSpectral = true;

    [ObservableProperty]
    private bool includeTemporal = true;

    [ObservableProperty]
    private bool includePerceptual = true;

    [ObservableProperty]
    private string? referenceAudioId;

    [ObservableProperty]
    private bool isLoading;

    [ObservableProperty]
    private string? errorMessage;

    [ObservableProperty]
    private string? statusMessage;

    public AudioAnalysisViewModel(IViewModelContext context, IAudioAnalysisClient audioAnalysisClient)
        : base(context)
    {
      _audioAnalysisClient = audioAnalysisClient ?? throw new ArgumentNullException(nameof(audioAnalysisClient));

      try
      {
        _toastNotificationService = AppServices.TryGetToastNotificationService();
      }
      catch
      {
        _toastNotificationService = null;
      }

      LoadAnalysisCommand = new EnhancedAsyncRelayCommand(async (ct) =>
      {
        using var profiler = PerformanceProfiler.StartCommand("LoadAnalysis");
        await LoadAnalysisAsync(ct);
      }, () => !IsLoading);
      AnalyzeAudioCommand = new EnhancedAsyncRelayCommand(async (ct) =>
      {
        using var profiler = PerformanceProfiler.StartCommand("AnalyzeAudio");
        await AnalyzeAudioAsync(ct);
      }, () => !IsLoading);
      CompareAudioCommand = new EnhancedAsyncRelayCommand(async (ct) =>
      {
        using var profiler = PerformanceProfiler.StartCommand("CompareAudio");
        await CompareAudioAsync(ct);
      }, () => !IsLoading);
      RefreshCommand = new EnhancedAsyncRelayCommand(async (ct) =>
      {
        using var profiler = PerformanceProfiler.StartCommand("Refresh");
        await RefreshAsync(ct);
      }, () => !IsLoading);
    }

    public IAsyncRelayCommand LoadAnalysisCommand { get; }
    public IAsyncRelayCommand AnalyzeAudioCommand { get; }
    public IAsyncRelayCommand CompareAudioCommand { get; }
    public IAsyncRelayCommand RefreshCommand { get; }

    /// <inheritdoc />
    public Task OnActivatedAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    Task IPanelLifecycle.OnDeactivatedAsync(CancellationToken ct) => Task.CompletedTask;

    async Task IPanelLifecycle.RefreshAsync(CancellationToken ct) => await RefreshAsync(ct);

    private async Task LoadAnalysisAsync(CancellationToken cancellationToken, string? idForStalenessCheck = null)
    {
      if (string.IsNullOrEmpty(SelectedAudioId))
      {
        ErrorMessage = ResourceHelper.GetString("AudioAnalysis.AudioFileRequired", "Audio file must be selected");
        return;
      }

      IsLoading = true;
      ErrorMessage = null;

      try
      {
        var result = await _audioAnalysisClient.GetAnalysisAsync(
          SelectedAudioId,
          IncludeSpectral,
          IncludeTemporal,
          IncludePerceptual,
          cancellationToken);

        if (result != null)
        {
          if (idForStalenessCheck != null && SelectedAudioId != idForStalenessCheck)
            return;
          AnalysisResult = new AudioAnalysisResultItem(result);
        }

        StatusMessage = ResourceHelper.GetString("AudioAnalysis.AnalysisLoaded", "Analysis loaded");
        _toastNotificationService?.ShowSuccess(
            ResourceHelper.GetString("AudioAnalysis.AnalysisLoadedDetail", "Audio analysis loaded successfully"),
            ResourceHelper.GetString("Toast.Title.AnalysisLoaded", "Analysis Loaded"));
      }
      catch (OperationCanceledException)
      {
        return;
      }
      catch (Exception ex)
      {
        await HandleErrorAsync(ex, "LoadAnalysis");
        _toastNotificationService?.ShowError(
            ResourceHelper.GetString("Toast.Title.LoadAnalysisFailed", "Failed to Load Analysis"),
            ex.Message);
      }
      finally
      {
        IsLoading = false;
      }
    }

    private async Task AnalyzeAudioAsync(CancellationToken cancellationToken)
    {
      if (string.IsNullOrEmpty(SelectedAudioId))
      {
        ErrorMessage = ResourceHelper.GetString("AudioAnalysis.AudioFileRequired", "Audio file must be selected");
        return;
      }

      IsLoading = true;
      ErrorMessage = null;

      try
      {
        var response = await _audioAnalysisClient.QueueAnalysisAsync(SelectedAudioId, cancellationToken);

        StatusMessage = response?.Message ?? "Analysis queued";
        _toastNotificationService?.ShowSuccess(
            ResourceHelper.GetString("AudioAnalysis.AnalysisStartedDetail", "Audio analysis started successfully"),
            ResourceHelper.GetString("Toast.Title.AnalysisStarted", "Analysis Started"));

        await Task.Delay(1000, cancellationToken);
        await LoadAnalysisAsync(cancellationToken);
      }
      catch (OperationCanceledException)
      {
        return;
      }
      catch (Exception ex)
      {
        await HandleErrorAsync(ex, "AnalyzeAudio");
        _toastNotificationService?.ShowError(
            ResourceHelper.GetString("Toast.Title.AnalysisFailed", "Analysis Failed"),
            ex.Message);
      }
      finally
      {
        IsLoading = false;
      }
    }

    private async Task CompareAudioAsync(CancellationToken cancellationToken)
    {
      if (string.IsNullOrEmpty(SelectedAudioId) || string.IsNullOrEmpty(ReferenceAudioId))
      {
        ErrorMessage = ResourceHelper.GetString("AudioAnalysis.BothAudioFilesRequired", "Both audio files must be selected");
        return;
      }

      try
      {
        IsLoading = true;
        ErrorMessage = null;

        var response = await _audioAnalysisClient.CompareAudioAsync(
          SelectedAudioId,
          ReferenceAudioId,
          cancellationToken);

        var msg = response?.Summary?.SimilarityScore is { } score
          ? $"Comparison complete. Similarity: {score:P0}"
          : ResourceHelper.GetString("AudioAnalysis.ComparisonComplete", "Comparison complete");
        StatusMessage = msg;
        _toastNotificationService?.ShowSuccess(
            ResourceHelper.GetString("AudioAnalysis.ComparisonCompleteDetail", "Audio comparison completed successfully"),
            ResourceHelper.GetString("Toast.Title.ComparisonComplete", "Comparison Complete"));
      }
      catch (OperationCanceledException)
      {
        return;
      }
      catch (Exception ex)
      {
        await HandleErrorAsync(ex, "CompareAudio");
      }
      finally
      {
        IsLoading = false;
      }
    }

    private async Task RefreshAsync(CancellationToken cancellationToken)
    {
      try
      {
        await LoadAnalysisAsync(cancellationToken);
        StatusMessage = ResourceHelper.GetString("AudioAnalysis.AnalysisRefreshed", "Analysis refreshed");
        _toastNotificationService?.ShowSuccess(
            ResourceHelper.GetString("AudioAnalysis.AnalysisRefreshedSuccessfully", "Analysis refreshed successfully"),
            ResourceHelper.GetString("Toast.Title.Refreshed", "Refreshed"));
      }
      catch (OperationCanceledException)
      {
        return;
      }
      catch (Exception ex)
      {
        await HandleErrorAsync(ex, "Refresh");
        _toastNotificationService?.ShowError(
            ResourceHelper.GetString("Toast.Title.RefreshFailed", "Refresh Failed"),
            ex.Message);
      }
    }

    partial void OnSelectedAudioIdChanged(string? value)
    {
      if (string.IsNullOrEmpty(value))
        return;

      _selectedAudioLoadCts?.Cancel();
      _selectedAudioLoadCts?.Dispose();
      _selectedAudioLoadCts = new CancellationTokenSource();
      var token = _selectedAudioLoadCts.Token;
      var capturedId = value;

      _ = LoadSelectionAsync(token, capturedId);
    }

    private async Task LoadSelectionAsync(CancellationToken cancellationToken, string capturedId)
    {
      try
      {
        await LoadAnalysisAsync(cancellationToken, idForStalenessCheck: capturedId);
      }
      catch (OperationCanceledException)
      {
        return;
      }
      catch (Exception ex)
      {
        await HandleErrorAsync(ex, "LoadSelection");
      }
    }
  }

  // Presentation model for display (ObservableObject)
  public class AudioAnalysisResultItem : ObservableObject
  {
    public string AudioId { get; set; }
    public int SampleRate { get; set; }
    public double Duration { get; set; }
    public int Channels { get; set; }
    public SpectralAnalysis Spectral { get; set; }
    public TemporalAnalysis Temporal { get; set; }
    public PerceptualAnalysis Perceptual { get; set; }
    public string Created { get; set; }

    public AudioAnalysisResultItem(AudioAnalysisResult result)
    {
      AudioId = result.AudioId;
      SampleRate = result.SampleRate;
      Duration = result.Duration;
      Channels = result.Channels;
      Spectral = result.Spectral;
      Temporal = result.Temporal;
      Perceptual = result.Perceptual;
      Created = result.Created;
    }
  }
}
