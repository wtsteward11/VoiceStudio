using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VoiceStudio.Core.Panels;
using VoiceStudio.Core.Services;
using SpectrogramConfigRequest = VoiceStudio.Core.Services.SpectrogramConfigRequest;
using SpectrogramDataRequest = VoiceStudio.Core.Services.SpectrogramDataRequest;
using SpectrogramRange = VoiceStudio.Core.Services.SpectrogramRange;
using VoiceStudio.App.Services;
using VoiceStudio.App.Utilities;

namespace VoiceStudio.App.ViewModels
{
  /// <summary>
  /// ViewModel for the SpectrogramView panel - Advanced spectrogram visualization.
  /// </summary>
  public partial class SpectrogramViewModel : BaseViewModel, IPanelView, IPanelLifecycle
  {
    private readonly ISpectrogramClient _spectrogramClient;
    private readonly ToastNotificationService? _toastNotificationService;

    public string PanelId => PanelIds.Spectrogram;
    public string DisplayName => ResourceHelper.GetString("Panel.Spectrogram.DisplayName", "Spectrogram");
    public PanelRegion Region => PanelRegion.Center;

    [ObservableProperty]
    private string? selectedAudioId;

    [ObservableProperty]
    private ObservableCollection<string> availableAudioIds = new();

    [ObservableProperty]
    private int windowSize = 2048;

    [ObservableProperty]
    private int hopLength = 512;

    [ObservableProperty]
    private int nFft = 2048;

    [ObservableProperty]
    private double frequencyMin = double.NaN;

    [ObservableProperty]
    private double frequencyMax = double.NaN;

    [ObservableProperty]
    private double timeStart = double.NaN;

    [ObservableProperty]
    private double timeEnd = double.NaN;

    [ObservableProperty]
    private bool logScale = true;

    [ObservableProperty]
    private string selectedColorScheme = "viridis";

    [ObservableProperty]
    private ObservableCollection<ColorSchemeInfo> availableColorSchemes = new();

    [ObservableProperty]
    private SpectrogramDataItem? spectrogramData;

    [ObservableProperty]
    private bool showPhase;

    [ObservableProperty]
    private bool showMagnitude = true;

    public SpectrogramViewModel(IViewModelContext context, ISpectrogramClient spectrogramClient)
        : base(context)
    {
      _spectrogramClient = spectrogramClient ?? throw new ArgumentNullException(nameof(spectrogramClient));

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

      LoadSpectrogramCommand = new EnhancedAsyncRelayCommand(async (ct) =>
      {
        using var profiler = PerformanceProfiler.StartCommand("LoadSpectrogram");
        await LoadSpectrogramAsync(ct);
      }, () => !string.IsNullOrEmpty(SelectedAudioId) && !IsLoading);
      UpdateConfigCommand = new EnhancedAsyncRelayCommand(async (ct) =>
      {
        using var profiler = PerformanceProfiler.StartCommand("UpdateConfig");
        await UpdateConfigAsync(ct);
      }, () => !string.IsNullOrEmpty(SelectedAudioId) && !IsLoading);
      ExportSpectrogramCommand = new EnhancedAsyncRelayCommand(async (ct) =>
      {
        using var profiler = PerformanceProfiler.StartCommand("ExportSpectrogram");
        await ExportSpectrogramAsync(ct);
      }, () => !string.IsNullOrEmpty(SelectedAudioId) && !IsLoading);
      LoadColorSchemesCommand = new EnhancedAsyncRelayCommand(async (ct) =>
      {
        using var profiler = PerformanceProfiler.StartCommand("LoadColorSchemes");
        await LoadColorSchemesAsync(ct);
      }, () => !IsLoading);
    }

    /// <inheritdoc />
    public Task OnActivatedAsync(CancellationToken cancellationToken = default) => LoadColorSchemesAsync(cancellationToken);

    /// <inheritdoc />
    public Task OnDeactivatedAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    /// <inheritdoc />
    public Task RefreshAsync(CancellationToken cancellationToken = default) => LoadColorSchemesAsync(cancellationToken);

    public IAsyncRelayCommand LoadSpectrogramCommand { get; }
    public IAsyncRelayCommand UpdateConfigCommand { get; }
    public IAsyncRelayCommand ExportSpectrogramCommand { get; }
    public IAsyncRelayCommand LoadColorSchemesCommand { get; }

    private async Task LoadSpectrogramAsync(CancellationToken cancellationToken)
    {
      if (string.IsNullOrEmpty(SelectedAudioId))
      {
        ErrorMessage = ResourceHelper.GetString("Spectrogram.AudioFileRequired", "Audio file must be selected");
        return;
      }

      IsLoading = true;
      ErrorMessage = null;

      try
      {
        var request = new SpectrogramDataRequest
        {
          WindowSize = WindowSize,
          HopLength = HopLength,
          NFft = NFft,
          FrequencyMin = double.IsNaN(FrequencyMin) ? null : FrequencyMin,
          FrequencyMax = double.IsNaN(FrequencyMax) ? null : FrequencyMax,
          TimeStart = double.IsNaN(TimeStart) ? null : TimeStart,
          TimeEnd = double.IsNaN(TimeEnd) ? null : TimeEnd,
          LogScale = LogScale
        };

        var data = await _spectrogramClient.GetSpectrogramDataAsync(SelectedAudioId, request, cancellationToken);

        if (data != null)
        {
          SpectrogramData = new SpectrogramDataItem(data);
          StatusMessage = ResourceHelper.GetString("Spectrogram.SpectrogramLoaded", "Spectrogram loaded");
          _toastNotificationService?.ShowSuccess(
              ResourceHelper.FormatString("Spectrogram.SpectrogramLoadedDetail", data.Duration.ToString("F2")),
              ResourceHelper.GetString("Toast.Title.LoadComplete", "Load Complete"));
        }
      }
      catch (OperationCanceledException)
      {
        return; // User cancelled
      }
      catch (Exception ex)
      {
        await HandleErrorAsync(ex, "LoadSpectrogram");
        _toastNotificationService?.ShowError(
            ResourceHelper.GetString("Toast.Title.LoadFailed", "Load Failed"),
            ex.Message);
      }
      finally
      {
        IsLoading = false;
      }
    }

    private async Task UpdateConfigAsync(CancellationToken cancellationToken)
    {
      if (string.IsNullOrEmpty(SelectedAudioId))
        return;

      IsLoading = true;
      ErrorMessage = null;

      try
      {
        var config = new SpectrogramConfigRequest
        {
          AudioId = SelectedAudioId,
          WindowSize = WindowSize,
          HopLength = HopLength,
          NFft = NFft,
          FrequencyRange = (!double.IsNaN(FrequencyMin) || !double.IsNaN(FrequencyMax))
            ? new SpectrogramRange { Min = double.IsNaN(FrequencyMin) ? 0.0 : FrequencyMin, Max = double.IsNaN(FrequencyMax) ? 22050.0 : FrequencyMax }
            : null,
          TimeRange = (!double.IsNaN(TimeStart) || !double.IsNaN(TimeEnd))
            ? new SpectrogramRange { Min = double.IsNaN(TimeStart) ? 0.0 : TimeStart, Max = double.IsNaN(TimeEnd) ? 10.0 : TimeEnd }
            : null,
          ColorScheme = SelectedColorScheme,
          ShowPhase = ShowPhase,
          ShowMagnitude = ShowMagnitude,
          LogScale = LogScale
        };

        await _spectrogramClient.UpdateConfigAsync(SelectedAudioId, config, cancellationToken);

        await LoadSpectrogramAsync(cancellationToken);
        StatusMessage = ResourceHelper.GetString("Spectrogram.ConfigurationUpdated", "Configuration updated");
        _toastNotificationService?.ShowSuccess(
            ResourceHelper.GetString("Spectrogram.ConfigurationUpdatedDetail", "Spectrogram configuration updated"),
            ResourceHelper.GetString("Toast.Title.ConfigUpdated", "Config Updated"));
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

    private async Task ExportSpectrogramAsync(CancellationToken cancellationToken)
    {
      if (string.IsNullOrEmpty(SelectedAudioId))
      {
        ErrorMessage = ResourceHelper.GetString("Spectrogram.AudioFileRequired", "Audio file must be selected");
        return;
      }

      try
      {
        IsLoading = true;
        ErrorMessage = null;

        // In a real implementation, this would open a file picker
        // and download the exported image
        var response = await _spectrogramClient.ExportSpectrogramAsync(
            SelectedAudioId,
            format: "png",
            width: 1920,
            height: 1080,
            cancellationToken);

        StatusMessage = ResourceHelper.GetString("Spectrogram.ExportInitiated", "Spectrogram export initiated");
        if (response != null)
        {
          _toastNotificationService?.ShowSuccess(
              ResourceHelper.FormatString("Spectrogram.SpectrogramExportedDetail", response.Width, response.Height, response.Format.ToUpper()),
              ResourceHelper.GetString("Toast.Title.ExportComplete", "Export Complete"));
        }
        else
        {
          _toastNotificationService?.ShowSuccess(
              ResourceHelper.GetString("Spectrogram.ExportInitiatedDetail", "Spectrogram export initiated"),
              ResourceHelper.GetString("Toast.Title.ExportStarted", "Export Started"));
        }
      }
      catch (OperationCanceledException)
      {
        return; // User cancelled
      }
      catch (Exception ex)
      {
        await HandleErrorAsync(ex, "ExportSpectrogram");
        _toastNotificationService?.ShowError(
            ResourceHelper.GetString("Toast.Title.ExportFailed", "Export Failed"),
            ex.Message);
      }
      finally
      {
        IsLoading = false;
      }
    }

    private async Task LoadColorSchemesAsync(CancellationToken cancellationToken)
    {
      try
      {
        var response = await _spectrogramClient.GetColorSchemesAsync(cancellationToken);

        AvailableColorSchemes.Clear();
        if (response?.Schemes != null)
        {
          foreach (var scheme in response.Schemes)
          {
            AvailableColorSchemes.Add(new ColorSchemeInfo
            {
              Id = scheme.Id,
              Name = scheme.Name,
              Description = scheme.Description
            });
          }
        }
      }
      catch (OperationCanceledException)
      {
        return; // User cancelled
      }
      catch (Exception ex)
      {
        await HandleErrorAsync(ex, "LoadColorSchemes");
      }
    }

    partial void OnSelectedAudioIdChanged(string? value)
    {
      if (!string.IsNullOrEmpty(value))
      {
        _ = LoadSpectrogramAsync(CancellationToken.None);
      }
    }

  }

  // Data models
  public class SpectrogramData
  {
    public string AudioId { get; set; } = string.Empty;
    public int SampleRate { get; set; }
    public double Duration { get; set; }
    public System.Collections.Generic.List<SpectrogramFrame> Frames { get; set; } = new();
    public double FrequencyResolution { get; set; }
    public double TimeResolution { get; set; }
    public SpectrogramConfig Config { get; set; } = new();
  }

  public class SpectrogramFrame
  {
    public double Time { get; set; }
    public System.Collections.Generic.List<double> Frequencies { get; set; } = new();
    public System.Collections.Generic.List<double> Magnitudes { get; set; } = new();
    public System.Collections.Generic.List<double>? Phases { get; set; }
  }

  public class SpectrogramConfig
  {
    public string AudioId { get; set; } = string.Empty;
    public int WindowSize { get; set; }
    public int HopLength { get; set; }
    public int NFft { get; set; }
    public System.Collections.Generic.Dictionary<string, double>? FrequencyRange { get; set; }
    public System.Collections.Generic.Dictionary<string, double>? TimeRange { get; set; }
    public string ColorScheme { get; set; } = "viridis";
    public System.Collections.Generic.Dictionary<string, double>? ColormapRange { get; set; }
    public bool ShowPhase { get; set; }
    public bool ShowMagnitude { get; set; }
    public bool LogScale { get; set; }
  }

  public class SpectrogramDataItem : ObservableObject
  {
    public string AudioId { get; set; }
    public int SampleRate { get; set; }
    public double Duration { get; set; }
    public System.Collections.Generic.List<SpectrogramFrame> Frames { get; set; }
    public double FrequencyResolution { get; set; }
    public double TimeResolution { get; set; }
    public SpectrogramConfig Config { get; set; }
    public int FrameCount => Frames?.Count ?? 0;

    public SpectrogramDataItem(SpectrogramData data)
    {
      AudioId = data.AudioId;
      SampleRate = data.SampleRate;
      Duration = data.Duration;
      Frames = data.Frames;
      FrequencyResolution = data.FrequencyResolution;
      TimeResolution = data.TimeResolution;
      Config = data.Config;
    }
  }

  public class ColorSchemeInfo : ObservableObject
  {
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
  }
}