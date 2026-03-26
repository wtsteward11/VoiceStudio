using System;
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

namespace VoiceStudio.App.ViewModels
{
  /// <summary>
  /// ViewModel for the AdvancedWaveformVisualizationView panel - Advanced waveform visualization.
  /// </summary>
  public partial class AdvancedWaveformVisualizationViewModel : BaseViewModel, IPanelView, IPanelLifecycle
  {
    private readonly IAdvancedWaveformClient _waveformClient;
    private readonly IProjectAudioClient _projectAudioClient;
    private readonly IProjectsClient _projectsClient;

    public string PanelId => "advanced-waveform-visualization";
    public string DisplayName => ResourceHelper.GetString("Panel.AdvancedWaveform.DisplayName", "Advanced Waveform");
    public PanelRegion Region => PanelRegion.Center;

    [ObservableProperty]
    private string? selectedAudioId;

    [ObservableProperty]
    private ObservableCollection<string> availableAudioIds = new();

    [ObservableProperty]
    private double zoomLevel = 1.0;

    [ObservableProperty]
    private ObservableCollection<int> selectedChannels = new() { 0 };

    [ObservableProperty]
    private bool showRms = true;

    [ObservableProperty]
    private bool showPeak = true;

    [ObservableProperty]
    private bool showZeroCrossings;

    [ObservableProperty]
    private string selectedColorScheme = "default";

    [ObservableProperty]
    private ObservableCollection<string> availableColorSchemes = new() { "default", "heatmap", "spectral" };

    [ObservableProperty]
    private double? timeStart;

    [ObservableProperty]
    private double? timeEnd;

    [ObservableProperty]
    private WaveformDataItem? waveformData;

    [ObservableProperty]
    private WaveformAnalysisItem? analysis;

    // Converted samples for WaveformControl (List<float> from first channel)
    public System.Collections.Generic.List<float> WaveformSamples
    {
      get
      {
        if (WaveformData?.Samples == null || WaveformData.Samples.Length == 0)
          return new System.Collections.Generic.List<float>();

        var channelIndex = SelectedChannels.Count > 0 ? SelectedChannels[0] : 0;
        if (channelIndex >= WaveformData.Samples.Length)
          channelIndex = 0;

        var channelSamples = WaveformData.Samples[channelIndex];
        return channelSamples.Select(s => (float)s).ToList();
      }
    }

    partial void OnWaveformDataChanged(WaveformDataItem? value)
    {
      OnPropertyChanged(nameof(WaveformSamples));
    }

    partial void OnSelectedChannelsChanged(System.Collections.ObjectModel.ObservableCollection<int> value)
    {
      OnPropertyChanged(nameof(WaveformSamples));
    }

    public AdvancedWaveformVisualizationViewModel(
      IViewModelContext context,
      IAdvancedWaveformClient waveformClient,
      IProjectAudioClient projectAudioClient,
      IProjectsClient projectsClient)
        : base(context)
    {
      _waveformClient = waveformClient ?? throw new ArgumentNullException(nameof(waveformClient));
      _projectAudioClient = projectAudioClient ?? throw new ArgumentNullException(nameof(projectAudioClient));
      _projectsClient = projectsClient ?? throw new ArgumentNullException(nameof(projectsClient));

      LoadAudioFilesCommand = new EnhancedAsyncRelayCommand(async (ct) =>
      {
        using var profiler = PerformanceProfiler.StartCommand("LoadAudioFiles");
        await LoadAudioFilesAsync(ct);
      }, () => !IsLoading);
      LoadWaveformDataCommand = new EnhancedAsyncRelayCommand(async (ct) =>
      {
        using var profiler = PerformanceProfiler.StartCommand("LoadWaveformData");
        await LoadWaveformDataAsync(ct);
      }, () => !IsLoading);
      UpdateConfigCommand = new EnhancedAsyncRelayCommand(async (ct) =>
      {
        using var profiler = PerformanceProfiler.StartCommand("UpdateConfig");
        await UpdateConfigAsync(ct);
      }, () => !IsLoading);
      AnalyzeCommand = new EnhancedAsyncRelayCommand(async (ct) =>
      {
        using var profiler = PerformanceProfiler.StartCommand("AnalyzeWaveform");
        await AnalyzeWaveformAsync(ct);
      }, () => !IsLoading);
      RefreshCommand = new EnhancedAsyncRelayCommand(async (ct) =>
      {
        using var profiler = PerformanceProfiler.StartCommand("Refresh");
        await RefreshAsync(ct);
      }, () => !IsLoading);
    }

    /// <inheritdoc />
    public Task OnActivatedAsync(CancellationToken cancellationToken = default)
    {
      _ = LoadAudioFilesAsync(cancellationToken);
      return Task.CompletedTask;
    }

    Task IPanelLifecycle.OnDeactivatedAsync(CancellationToken ct) => Task.CompletedTask;

    async Task IPanelLifecycle.RefreshAsync(CancellationToken ct) => await RefreshAsync(ct);

    public IAsyncRelayCommand LoadAudioFilesCommand { get; }
    public IAsyncRelayCommand LoadWaveformDataCommand { get; }
    public IAsyncRelayCommand UpdateConfigCommand { get; }
    public IAsyncRelayCommand AnalyzeCommand { get; }
    public IAsyncRelayCommand RefreshCommand { get; }

    private async Task LoadAudioFilesAsync(CancellationToken cancellationToken)
    {
      try
      {
        IsLoading = true;
        ErrorMessage = null;

        var projects = await _projectsClient.GetProjectsAsync(cancellationToken);
        var audioIds = new System.Collections.Generic.List<string>();

        foreach (var project in projects)
        {
          cancellationToken.ThrowIfCancellationRequested();
          var audioFiles = await _projectAudioClient.ListProjectAudioAsync(project.Id, cancellationToken);
          foreach (var audioFile in audioFiles)
          {
            if (!string.IsNullOrEmpty(audioFile.Filename))
            {
              audioIds.Add(audioFile.Filename);
            }
          }
        }

        AvailableAudioIds.Clear();
        foreach (var audioId in audioIds.Distinct())
        {
          AvailableAudioIds.Add(audioId);
        }
      }
      catch (OperationCanceledException)
      {
        return; // User cancelled
      }
      catch (Exception ex)
      {
        await HandleErrorAsync(ex, "LoadAudioFiles");
      }
      finally
      {
        IsLoading = false;
      }
    }

    private async Task LoadWaveformDataAsync(CancellationToken cancellationToken)
    {
      if (string.IsNullOrEmpty(SelectedAudioId))
      {
        ErrorMessage = ResourceHelper.GetString("AdvancedWaveform.AudioRequired", "Audio must be selected");
        return;
      }

      try
      {
        IsLoading = true;
        ErrorMessage = null;

        var data = await _waveformClient.GetWaveformDataAsync(
            SelectedAudioId,
            ZoomLevel != 1.0 ? ZoomLevel : null,
            TimeStart,
            TimeEnd,
            cancellationToken);

        if (data != null)
        {
          WaveformData = new WaveformDataItem(data);
        }
      }
      catch (Exception ex)
      {
        ErrorMessage = ResourceHelper.FormatString("AdvancedWaveformVisualization.LoadWaveformDataFailed", ex.Message);
      }
      finally
      {
        IsLoading = false;
      }
    }

    private async Task UpdateConfigAsync(CancellationToken cancellationToken)
    {
      if (string.IsNullOrEmpty(SelectedAudioId))
      {
        ErrorMessage = ResourceHelper.GetString("AdvancedWaveform.AudioRequired", "Audio must be selected");
        return;
      }

      try
      {
        IsLoading = true;
        ErrorMessage = null;

        var request = new AdvancedWaveformConfigRequest
        {
          AudioId = SelectedAudioId,
          ZoomLevel = ZoomLevel,
          ShowChannels = SelectedChannels.ToArray(),
          ShowRms = ShowRms,
          ShowPeak = ShowPeak,
          ShowZeroCrossings = ShowZeroCrossings,
          ColorScheme = SelectedColorScheme,
          TimeRange = TimeStart.HasValue && TimeEnd.HasValue
            ? new AdvancedWaveformTimeRange { Start = TimeStart.Value, End = TimeEnd.Value }
            : null
        };

        var updated = await _waveformClient.UpdateConfigAsync(SelectedAudioId, request, cancellationToken);

        if (updated != null)
        {
          await LoadWaveformDataAsync(cancellationToken);
          StatusMessage = ResourceHelper.GetString("AdvancedWaveform.ConfigurationUpdated", "Configuration updated");
        }
      }
      catch (OperationCanceledException)
      {
        return; // User cancelled
      }
      catch (Exception ex)
      {
        await HandleErrorAsync(ex, "UpdateConfig");
      }
      finally
      {
        IsLoading = false;
      }
    }

    private async Task AnalyzeWaveformAsync(CancellationToken cancellationToken)
    {
      if (string.IsNullOrEmpty(SelectedAudioId))
      {
        ErrorMessage = ResourceHelper.GetString("AdvancedWaveform.AudioRequired", "Audio must be selected");
        return;
      }

      try
      {
        IsLoading = true;
        ErrorMessage = null;

        var analysis = await _waveformClient.GetAnalysisAsync(SelectedAudioId, cancellationToken);

        if (analysis != null)
        {
          Analysis = new WaveformAnalysisItem(analysis);
          StatusMessage = ResourceHelper.GetString("AdvancedWaveform.WaveformAnalyzed", "Waveform analyzed");
        }
      }
      catch (OperationCanceledException)
      {
        return; // User cancelled
      }
      catch (Exception ex)
      {
        await HandleErrorAsync(ex, "AnalyzeWaveform");
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
        await LoadWaveformDataAsync(cancellationToken);
        StatusMessage = ResourceHelper.GetString("AdvancedWaveform.Refreshed", "Refreshed");
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

    partial void OnSelectedAudioIdChanged(string? value)
    {
      if (!string.IsNullOrEmpty(value))
      {
        _ = LoadWaveformDataAsync(CancellationToken.None);
      }
    }
  }

  // Data models (UI presentation)
  public class WaveformDataItem : ObservableObject
  {
    public string AudioId { get; set; }
    public int SampleRate { get; set; }
    public int Channels { get; set; }
    public double Duration { get; set; }
    public double[][] Samples { get; set; }
    public double[]? RmsValues { get; set; }
    public double[]? PeakValues { get; set; }
    public int[]? ZeroCrossings { get; set; }
    public double[] TimePoints { get; set; }
    public string DurationDisplay => $"{Duration:F2}s";
    public string SampleRateDisplay => $"{SampleRate} Hz";

    public WaveformDataItem(AdvancedWaveformData data)
    {
      AudioId = data.AudioId;
      SampleRate = data.SampleRate;
      Channels = data.Channels;
      Duration = data.Duration;
      Samples = data.Samples;
      RmsValues = data.RmsValues;
      PeakValues = data.PeakValues;
      ZeroCrossings = data.ZeroCrossings;
      TimePoints = data.TimePoints;
    }
  }

  public class WaveformAnalysisItem : ObservableObject
  {
    public string AudioId { get; set; }
    public double PeakAmplitude { get; set; }
    public double RmsAmplitude { get; set; }
    public double DynamicRange { get; set; }
    public double CrestFactor { get; set; }
    public double ZeroCrossingRate { get; set; }
    public double DcOffset { get; set; }
    public string PeakAmplitudeDisplay => $"{PeakAmplitude:F3}";
    public string RmsAmplitudeDisplay => $"{RmsAmplitude:F3}";
    public string DynamicRangeDisplay => $"{DynamicRange:F1} dB";
    public string CrestFactorDisplay => $"{CrestFactor:F2}";

    public WaveformAnalysisItem(AdvancedWaveformAnalysis analysis)
    {
      AudioId = analysis.AudioId;
      PeakAmplitude = analysis.PeakAmplitude;
      RmsAmplitude = analysis.RmsAmplitude;
      DynamicRange = analysis.DynamicRange;
      CrestFactor = analysis.CrestFactor;
      ZeroCrossingRate = analysis.ZeroCrossingRate;
      DcOffset = analysis.DcOffset;
    }
  }
}