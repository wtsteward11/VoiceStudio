using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Xaml;
using VoiceStudio.Core.Panels;
using VoiceStudio.Core.Services;
using VoiceStudio.Core.Models;
using VoiceStudio.App.Services;
using VoiceStudio.App.Utilities;

namespace VoiceStudio.App.Views.Panels
{
  public partial class AnalyzerViewModel : ObservableObject, IPanelView, IDisposable
  {
    private readonly IAnalyzerClient _analyzerClient;
    private readonly IAudioVisualizationService _audioVisualizationService;
    private readonly IAudioPlayerService? _audioPlayer;
    private readonly IContextManager? _contextManager;
    private readonly ToastNotificationService? _toastNotificationService;
    private readonly IErrorLoggingService? _errorLoggingService;
    private bool _disposed;

    // Store event handler for proper unsubscription
    private EventHandler<double>? _positionChangedHandler;

    public string PanelId => PanelIds.Analyzer;
    public string DisplayName => ResourceHelper.GetString("Panel.Analyzer.DisplayName", "Analyzer");
    public PanelRegion Region => PanelRegion.Right;

    [ObservableProperty]
    private List<float> waveformSamples = new();

    [ObservableProperty]
    private ObservableCollection<SpectrogramFrame> spectrogramFrames = new();

    [ObservableProperty]
    private LoudnessData? loudnessData;

    [ObservableProperty]
    private RadarData? radarData;

    [ObservableProperty]
    private PhaseData? phaseData;

    [ObservableProperty]
    private AudioOrbsData? audioOrbsData;

    // Loudness chart properties (for LoudnessChartControl individual property bindings)
    public List<double> LoudnessTimes { get; private set; } = new();
    public List<double> LoudnessLufsValues { get; private set; } = new();
    public double? LoudnessIntegratedLufs { get; private set; }
    public double? LoudnessPeakLufs { get; private set; }
    public double LoudnessDuration { get; private set; }

    [ObservableProperty]
    private string selectedTab = "Waveform";

    [ObservableProperty]
    private string? selectedAudioId;

    [ObservableProperty]
    private bool isLoading;

    [ObservableProperty]
    private string? errorMessage;

    [ObservableProperty]
    private double playbackPosition = -1.0; // -1 means no playback

    public Visibility HasError => string.IsNullOrWhiteSpace(ErrorMessage) ? Visibility.Collapsed : Visibility.Visible;

    public bool HasAudioData => (WaveformSamples?.Count > 0) ||
                                (SpectrogramFrames?.Count > 0) ||
                                LoudnessData != null ||
                                RadarData != null ||
                                PhaseData != null ||
                                AudioOrbsData != null;

    public Visibility IsWaveformTab => SelectedTab == "Waveform" ? Visibility.Visible : Visibility.Collapsed;
    public Visibility IsSpectralTab => SelectedTab == "Spectral" ? Visibility.Visible : Visibility.Collapsed;
    public Visibility IsRadarTab => SelectedTab == "Radar" ? Visibility.Visible : Visibility.Collapsed;
    public Visibility IsLoudnessTab => SelectedTab == "Loudness" ? Visibility.Visible : Visibility.Collapsed;
    public Visibility IsPhaseTab => SelectedTab == "Phase" ? Visibility.Visible : Visibility.Collapsed;
    public Visibility IsAudioOrbsTab => SelectedTab == "AudioOrbs" ? Visibility.Visible : Visibility.Collapsed;
    public Visibility IsOtherTab => SelectedTab != "Waveform" && SelectedTab != "Spectral" && SelectedTab != "Radar" && SelectedTab != "Loudness" && SelectedTab != "Phase" && SelectedTab != "AudioOrbs" ? Visibility.Visible : Visibility.Collapsed;
    public Visibility IsWaveformOrSpectralTab => (SelectedTab == "Waveform" || SelectedTab == "Spectral") ? Visibility.Visible : Visibility.Collapsed;

    public AnalyzerViewModel(IAnalyzerClient analyzerClient, IAudioVisualizationService audioVisualizationService, IAudioPlayerService? audioPlayer = null)
    {
      _analyzerClient = analyzerClient ?? throw new ArgumentNullException(nameof(analyzerClient));
      _audioVisualizationService = audioVisualizationService ?? throw new ArgumentNullException(nameof(audioVisualizationService));
      _audioPlayer = audioPlayer;
      _contextManager = AppServices.TryGetContextManager();

      // Get services (may be null if not initialized)
      try
      {
        _toastNotificationService = AppServices.TryGetToastNotificationService();
      }
      catch
      {
        // Services may not be initialized yet - that's okay
        _toastNotificationService = null;
      }

      try
      {
        _errorLoggingService = ServiceProvider.GetErrorLoggingService();
      }
      catch
      {
        // Error logging service may not be initialized yet - that's okay
        _errorLoggingService = null;
      }

      LoadVisualizationCommand = new EnhancedAsyncRelayCommand(async (ct) =>
      {
        using var profiler = PerformanceProfiler.StartCommand("LoadVisualization");
        await LoadVisualizationAsync(ct);
      }, () => !string.IsNullOrWhiteSpace(SelectedAudioId) && !IsLoading);

      BrowseAndUploadCommand = new EnhancedAsyncRelayCommand(async (ct) =>
      {
        using var profiler = PerformanceProfiler.StartCommand("BrowseAndUpload");
        await BrowseAndUploadAsync(ct);
      }, () => !IsLoading);

      // Subscribe to playback position updates if audio player is available
      if (_audioPlayer != null)
      {
        _positionChangedHandler = OnPlaybackPositionChanged;
        _audioPlayer.PositionChanged += _positionChangedHandler;
      }
    }

    public void Dispose()
    {
      if (_disposed)
        return;

      // Unsubscribe from audio player events
      if (_audioPlayer != null && _positionChangedHandler != null)
      {
        _audioPlayer.PositionChanged -= _positionChangedHandler;
      }

      _disposed = true;
    }

    private void OnPlaybackPositionChanged(object? sender, double position)
    {
      // Update playback position for visualization controls
      // This will be bound to the controls' PlaybackPosition property
      PlaybackPosition = position;
    }

    public IAsyncRelayCommand LoadVisualizationCommand { get; }

    /// <summary>
    /// Navigates to and loads an audio by ID for analysis. Used by INavigatablePanel search-result focus.
    /// </summary>
    public Task<bool> NavigateToAudioAsync(string itemId, CancellationToken ct)
    {
      if (string.IsNullOrEmpty(itemId))
        return Task.FromResult(false);

      SelectedAudioId = itemId;
      return Task.FromResult(true);
    }

    /// <summary>
    /// Command to browse for and upload an audio file for analysis.
    /// </summary>
    public IAsyncRelayCommand BrowseAndUploadCommand { get; }

    partial void OnErrorMessageChanged(string? value)
    {
      OnPropertyChanged(nameof(HasError));
    }

    partial void OnSelectedTabChanged(string value)
    {
      OnPropertyChanged(nameof(IsWaveformTab));
      OnPropertyChanged(nameof(IsSpectralTab));
      OnPropertyChanged(nameof(IsRadarTab));
      OnPropertyChanged(nameof(IsLoudnessTab));
      OnPropertyChanged(nameof(IsPhaseTab));
      OnPropertyChanged(nameof(IsAudioOrbsTab));
      OnPropertyChanged(nameof(IsOtherTab));
      OnPropertyChanged(nameof(IsWaveformOrSpectralTab));

      // Reload visualization data when tab changes
      if (!string.IsNullOrWhiteSpace(SelectedAudioId))
      {
        _ = LoadVisualizationAsync(CancellationToken.None);
      }
    }

    partial void OnSelectedAudioIdChanged(string? value)
    {
      LoadVisualizationCommand.NotifyCanExecuteChanged();

      // Publish transport context so main Play can target this audio
      _contextManager?.SetCurrentPlayable(
        !string.IsNullOrWhiteSpace(value) ? value : null,
        !string.IsNullOrWhiteSpace(value) ? TransportSource.Analyzer : null,
        !string.IsNullOrWhiteSpace(value) ? "Analyzer" : null);

      // Auto-load when audio is selected
      if (!string.IsNullOrWhiteSpace(value))
      {
        _ = LoadVisualizationAsync(CancellationToken.None);
      }
    }

    private async Task BrowseAndUploadAsync(CancellationToken cancellationToken)
    {
      try
      {
        // Create file picker
        var picker = new Windows.Storage.Pickers.FileOpenPicker();

        // Initialize for unpackaged app
        var window = App.MainWindowInstance;
        if (window != null)
        {
          var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(window);
          WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);
        }

        picker.ViewMode = Windows.Storage.Pickers.PickerViewMode.List;
        picker.SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.MusicLibrary;
        picker.FileTypeFilter.Add(".wav");
        picker.FileTypeFilter.Add(".mp3");
        picker.FileTypeFilter.Add(".flac");
        picker.FileTypeFilter.Add(".m4a");
        picker.FileTypeFilter.Add(".ogg");
        picker.FileTypeFilter.Add(".aac");

        var file = await picker.PickSingleFileAsync();
        if (file == null)
          return; // User cancelled

        IsLoading = true;
        ErrorMessage = null;

        // Upload file to backend
        var response = await _analyzerClient.UploadAudioFileAsync(file.Path, cancellationToken);

        // Set the audio ID - this will auto-trigger visualization loading
        SelectedAudioId = response.Id;

        _toastNotificationService?.ShowSuccess(
            ResourceHelper.GetString("Analyzer.UploadSuccess", "Audio Uploaded"),
            $"File '{response.Filename}' uploaded successfully.");
      }
      // ALLOWED: empty catch - cancellation is expected user action
      catch (OperationCanceledException)
      {
      }
      catch (Exception ex)
      {
        ErrorMessage = ResourceHelper.FormatString("Analyzer.UploadFailed", ex.Message);
        OnPropertyChanged(nameof(HasError));
        _errorLoggingService?.LogError(ex, "BrowseAndUpload");
        _toastNotificationService?.ShowError(
            ResourceHelper.GetString("Analyzer.UploadFailed", "Upload Failed"),
            ex.Message);
      }
      finally
      {
        IsLoading = false;
      }
    }

    private async Task LoadVisualizationAsync(CancellationToken cancellationToken)
    {
      if (string.IsNullOrWhiteSpace(SelectedAudioId))
        return;

      IsLoading = true;
      ErrorMessage = null;

      try
      {
        // Load waveform data for Waveform tab
        if (SelectedTab == "Waveform")
        {
          var waveformData = await _audioVisualizationService.GetWaveformDataAsync(SelectedAudioId, width: 1024, mode: "peak", cancellationToken);
          if (waveformData?.Samples != null)
          {
            // Replace the list so x:Bind targets update reliably.
            WaveformSamples = waveformData.Samples;
          }
        }

        // Load spectrogram data for Spectral tab
        if (SelectedTab == "Spectral")
        {
          var spectrogramData = await _audioVisualizationService.GetSpectrogramDataAsync(SelectedAudioId, width: 512, height: 256, cancellationToken);
          if (spectrogramData?.Frames != null)
          {
            SpectrogramFrames.Clear();
            foreach (var frame in spectrogramData.Frames)
            {
              SpectrogramFrames.Add(new SpectrogramFrame
              {
                Time = frame.Time,
                Frequencies = frame.Frequencies
              });
            }
          }
        }

        // Load radar chart data for Radar tab
        if (SelectedTab == "Radar")
        {
          try
          {
            var radarData = await _analyzerClient.GetRadarDataAsync(SelectedAudioId, cancellationToken);
            if (radarData != null)
            {
              RadarData = radarData;
            }
          }
          catch (OperationCanceledException)
          {
            throw; // Re-throw cancellation
          }
          catch
          {
            // Silently fail - radar data is optional
            RadarData = null;
          }
        }

        // Load loudness data for Loudness tab
        if (SelectedTab == "Loudness")
        {
          try
          {
            var loudnessData = await _analyzerClient.GetLoudnessDataAsync(SelectedAudioId, windowSize: 0.4, cancellationToken);
            LoudnessData = loudnessData;

            // Convert to individual properties for LoudnessChartControl binding
            if (loudnessData != null)
            {
              LoudnessTimes = loudnessData.Times?.Select(t => (double)t).ToList() ?? new List<double>();
              LoudnessLufsValues = loudnessData.LufsValues?.Select(l => (double)l).ToList() ?? new List<double>();
              LoudnessIntegratedLufs = loudnessData.IntegratedLufs.HasValue ? (double)loudnessData.IntegratedLufs.Value : null;
              LoudnessPeakLufs = loudnessData.PeakLufs.HasValue ? (double)loudnessData.PeakLufs.Value : null;
              LoudnessDuration = (double)loudnessData.Duration;

              OnPropertyChanged(nameof(LoudnessTimes));
              OnPropertyChanged(nameof(LoudnessLufsValues));
              OnPropertyChanged(nameof(LoudnessIntegratedLufs));
              OnPropertyChanged(nameof(LoudnessPeakLufs));
              OnPropertyChanged(nameof(LoudnessDuration));
            }
            else
            {
              LoudnessTimes = new List<double>();
              LoudnessLufsValues = new List<double>();
              LoudnessIntegratedLufs = null;
              LoudnessPeakLufs = null;
              LoudnessDuration = 0.0;

              OnPropertyChanged(nameof(LoudnessTimes));
              OnPropertyChanged(nameof(LoudnessLufsValues));
              OnPropertyChanged(nameof(LoudnessIntegratedLufs));
              OnPropertyChanged(nameof(LoudnessPeakLufs));
              OnPropertyChanged(nameof(LoudnessDuration));
            }
          }
          catch (OperationCanceledException)
          {
            throw; // Re-throw cancellation
          }
          catch (Exception ex)
          {
            // Log but don't fail - loudness data is optional
            _errorLoggingService?.LogError(ex, "LoadLoudnessData");
            LoudnessData = null;

            // Clear individual properties
            LoudnessTimes = new List<double>();
            LoudnessLufsValues = new List<double>();
            LoudnessIntegratedLufs = null;
            LoudnessPeakLufs = null;
            LoudnessDuration = 0.0;

            OnPropertyChanged(nameof(LoudnessTimes));
            OnPropertyChanged(nameof(LoudnessLufsValues));
            OnPropertyChanged(nameof(LoudnessIntegratedLufs));
            OnPropertyChanged(nameof(LoudnessPeakLufs));
            OnPropertyChanged(nameof(LoudnessDuration));
          }
        }

        // Load phase data for Phase tab
        if (SelectedTab == "Phase")
        {
          try
          {
            var phaseData = await _analyzerClient.GetPhaseDataAsync(SelectedAudioId, windowSize: 0.1, cancellationToken);
            if (phaseData != null)
            {
              PhaseData = phaseData;
            }
          }
          catch (OperationCanceledException)
          {
            throw; // Re-throw cancellation
          }
          catch (Exception ex)
          {
            // Log but don't fail - phase data is optional
            _errorLoggingService?.LogError(ex, "LoadPhaseData");
            PhaseData = null;
          }
        }

        // Load AudioOrbs data for AudioOrbs tab
        // AudioOrbs uses frequency data from spectrogram, so we derive it from spectrogram frames
        if (SelectedTab == "AudioOrbs")
        {
          try
          {
            // Get spectrogram data to derive frequency magnitudes
            var spectrogramData = await _audioVisualizationService.GetSpectrogramDataAsync(SelectedAudioId, width: 512, height: 256, cancellationToken);
            if (spectrogramData?.Frames != null && spectrogramData.Frames.Count > 0)
            {
              // Use the first frame (or average of first few frames) for AudioOrbs
              var frame = spectrogramData.Frames[0];

              // Create AudioOrbsData from spectrogram frame
              AudioOrbsData = new AudioOrbsData
              {
                Magnitudes = frame.Frequencies ?? new List<float>(),
                Frequencies = new List<float>(), // Will be calculated if needed
                OrbCount = 32,
                SampleRate = spectrogramData.SampleRate,
                FftSize = spectrogramData.FftSize,
                TimePosition = frame.Time
              };

              // Calculate frequencies if not provided (based on FFT size and sample rate)
              if (AudioOrbsData.Frequencies.Count == 0 && AudioOrbsData.Magnitudes.Count > 0)
              {
                var nyquist = AudioOrbsData.SampleRate / 2.0f;
                var binCount = AudioOrbsData.Magnitudes.Count;
                for (int i = 0; i < binCount; i++)
                {
                  var frequency = i / (float)binCount * nyquist;
                  AudioOrbsData.Frequencies.Add(frequency);
                }
              }
            }
            else
            {
              AudioOrbsData = null;
            }
          }
          catch (OperationCanceledException)
          {
            throw; // Re-throw cancellation
          }
          catch (Exception ex)
          {
            // Log but don't fail - AudioOrbs data is optional
            _errorLoggingService?.LogError(ex, "LoadAudioOrbsData");
            AudioOrbsData = null;
          }
        }

        var tabName = SelectedTab;
        _toastNotificationService?.ShowSuccess(
            ResourceHelper.GetString("Analyzer.AnalysisComplete", "Analysis Complete"),
            ResourceHelper.FormatString("Analyzer.AnalysisLoadedSuccess", tabName));
      }
      catch (OperationCanceledException)
      {
        return; // User cancelled
      }
      catch (Exception ex)
      {
        ErrorMessage = ResourceHelper.FormatString("Analyzer.LoadVisualizationFailed", ex.Message);
        OnPropertyChanged(nameof(HasError));
        _errorLoggingService?.LogError(ex, "LoadVisualization");
        _toastNotificationService?.ShowError(
            ResourceHelper.GetString("Analyzer.AnalysisFailed", "Analysis Failed"),
            ex.Message);
      }
      finally
      {
        IsLoading = false;
      }
    }
  }
}