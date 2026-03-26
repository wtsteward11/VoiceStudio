using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VoiceStudio.Core.Panels;
using VoiceStudio.Core.Services;
using VoiceStudio.App.Utilities;

namespace VoiceStudio.App.ViewModels
{
  /// <summary>
  /// ViewModel for the SonographyVisualizationView panel - Sonography (waterfall/3D spectrogram) visualization.
  /// </summary>
  public partial class SonographyVisualizationViewModel : BaseViewModel, IPanelView, IPanelLifecycle
  {
    private readonly ISonographyClient _sonographyClient;
    private readonly IProjectsClient _projectsClient;
    private readonly IProjectAudioClient _projectAudioClient;

    public string PanelId => PanelIds.Sonography;
    public string DisplayName => ResourceHelper.GetString("Panel.SonographyVisualization.DisplayName", "Sonography Visualization");
    public PanelRegion Region => PanelRegion.Center;

    [ObservableProperty]
    private string? selectedAudioId;

    [ObservableProperty]
    private ObservableCollection<string> availableAudioIds = new();

    [ObservableProperty]
    private double timeWindow = 1.0;

    [ObservableProperty]
    private double overlap = 0.5;

    [ObservableProperty]
    private int frequencyResolution = 1024;

    [ObservableProperty]
    private int timeResolution = 100;

    [ObservableProperty]
    private string selectedColorScheme = "waterfall";

    [ObservableProperty]
    private ObservableCollection<string> availableColorSchemes = new();

    [ObservableProperty]
    private string selectedPerspective = "3d";

    [ObservableProperty]
    private ObservableCollection<string> availablePerspectives = new();

    [ObservableProperty]
    private double rotationX = 45.0;

    [ObservableProperty]
    private double rotationY = 30.0;

    [ObservableProperty]
    private double zoom = 1.0;

    [ObservableProperty]
    private SonographyDataItem? sonographyData;

    public SonographyVisualizationViewModel(IViewModelContext context, ISonographyClient sonographyClient, IProjectsClient projectsClient, IProjectAudioClient projectAudioClient)
        : base(context)
    {
      _sonographyClient = sonographyClient ?? throw new ArgumentNullException(nameof(sonographyClient));
      _projectsClient = projectsClient ?? throw new ArgumentNullException(nameof(projectsClient));
      _projectAudioClient = projectAudioClient ?? throw new ArgumentNullException(nameof(projectAudioClient));

      GenerateSonographyCommand = new EnhancedAsyncRelayCommand(async (ct) =>
      {
        using var profiler = PerformanceProfiler.StartCommand("GenerateSonography");
        await GenerateSonographyAsync(ct);
      }, () => !string.IsNullOrEmpty(SelectedAudioId) && !IsLoading);
      LoadPerspectivesCommand = new EnhancedAsyncRelayCommand(async (ct) =>
      {
        using var profiler = PerformanceProfiler.StartCommand("LoadPerspectives");
        await LoadPerspectivesAsync(ct);
      }, () => !IsLoading);
      LoadColorSchemesCommand = new EnhancedAsyncRelayCommand(async (ct) =>
      {
        using var profiler = PerformanceProfiler.StartCommand("LoadColorSchemes");
        await LoadColorSchemesAsync(ct);
      }, () => !IsLoading);
      LoadAudioFilesCommand = new EnhancedAsyncRelayCommand(async (ct) =>
      {
        using var profiler = PerformanceProfiler.StartCommand("LoadAudioFiles");
        await LoadAudioFilesAsync(ct);
      }, () => !IsLoading);
      RefreshCommand = new EnhancedAsyncRelayCommand(async (ct) =>
      {
        using var profiler = PerformanceProfiler.StartCommand("Refresh");
        await RefreshAsync(ct);
      }, () => !IsLoading);
    }

    /// <inheritdoc />
    public Task OnActivatedAsync(CancellationToken cancellationToken = default) =>
      RefreshAsyncImpl(cancellationToken);

    /// <inheritdoc />
    public Task OnDeactivatedAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    /// <inheritdoc />
    public Task RefreshAsync(CancellationToken cancellationToken = default) => RefreshAsyncImpl(cancellationToken);

    public IAsyncRelayCommand GenerateSonographyCommand { get; }
    public IAsyncRelayCommand LoadPerspectivesCommand { get; }
    public IAsyncRelayCommand LoadColorSchemesCommand { get; }
    public IAsyncRelayCommand LoadAudioFilesCommand { get; }
    public IAsyncRelayCommand RefreshCommand { get; }

    private async Task GenerateSonographyAsync(CancellationToken cancellationToken)
    {
      if (string.IsNullOrEmpty(SelectedAudioId))
      {
        ErrorMessage = ResourceHelper.GetString("SonographyVisualization.AudioRequired", "Audio must be selected");
        return;
      }

      IsLoading = true;
      ErrorMessage = null;

      try
      {
        var data = await _sonographyClient.GenerateAsync(
            SelectedAudioId,
            TimeWindow,
            Overlap,
            FrequencyResolution,
            TimeResolution,
            SelectedColorScheme,
            SelectedPerspective,
            cancellationToken
        );

        if (data != null)
        {
          SonographyData = new SonographyDataItem(data);
          StatusMessage = ResourceHelper.GetString("SonographyVisualization.DataGenerated", "Sonography data generated");
        }
      }
      catch (OperationCanceledException)
      {
        return; // User cancelled
      }
      catch (Exception ex)
      {
        await HandleErrorAsync(ex, "GenerateSonography");
      }
      finally
      {
        IsLoading = false;
      }
    }

    private async Task LoadPerspectivesAsync(CancellationToken cancellationToken = default)
    {
      try
      {
        var response = await _sonographyClient.GetPerspectivesAsync(cancellationToken);

        if (response?.Perspectives != null)
        {
          AvailablePerspectives.Clear();
          foreach (var perspective in response.Perspectives)
          {
            AvailablePerspectives.Add(perspective.Id);
          }
        }
      }
      catch (Exception ex)
      {
        ErrorMessage = ResourceHelper.FormatString("SonographyVisualization.LoadPerspectivesFailed", ex.Message);
      }
    }

    private async Task LoadColorSchemesAsync(CancellationToken cancellationToken)
    {
      try
      {
        var response = await _sonographyClient.GetColorSchemesAsync(cancellationToken);

        if (response?.ColorSchemes != null)
        {
          AvailableColorSchemes.Clear();
          foreach (var scheme in response.ColorSchemes)
          {
            AvailableColorSchemes.Add(scheme.Id);
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

    private async Task LoadAudioFilesAsync(CancellationToken cancellationToken)
    {
      IsLoading = true;
      ErrorMessage = null;

      try
      {
        var projects = await _projectsClient.GetProjectsAsync(cancellationToken);
        var audioIds = new System.Collections.Generic.List<string>();

        foreach (var project in projects)
        {
          cancellationToken.ThrowIfCancellationRequested();
          var audioFiles = await _projectAudioClient.ListProjectAudioAsync(project.Id, cancellationToken);
          foreach (var audioFile in audioFiles)
          {
            if (!string.IsNullOrEmpty(audioFile.AudioId))
            {
              audioIds.Add(audioFile.AudioId);
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

    private async Task RefreshAsyncImpl(CancellationToken cancellationToken)
    {
      try
      {
        await LoadPerspectivesAsync(cancellationToken);
        await LoadColorSchemesAsync(cancellationToken);
        await LoadAudioFilesAsync(cancellationToken);
        StatusMessage = ResourceHelper.GetString("SonographyVisualization.Refreshed", "Refreshed");
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

  }

  // Data models
  public class SonographyData
  {
    public string AudioId { get; set; } = string.Empty;
    public SonographyConfig Config { get; set; } = new();
    public SonographyFrame[] Frames { get; set; } = Array.Empty<SonographyFrame>();
    public double TotalDuration { get; set; }
    public int SampleRate { get; set; }
  }

  public class SonographyConfig
  {
    public string AudioId { get; set; } = string.Empty;
    public double TimeWindow { get; set; }
    public double Overlap { get; set; }
    public int FrequencyResolution { get; set; }
    public int TimeResolution { get; set; }
    public string ColorScheme { get; set; } = string.Empty;
    public string Perspective { get; set; } = string.Empty;
    public double RotationX { get; set; }
    public double RotationY { get; set; }
    public double Zoom { get; set; }
  }

  public class SonographyFrame
  {
    public double Timestamp { get; set; }
    public double[] Frequencies { get; set; } = Array.Empty<double>();
    public double[] Magnitudes { get; set; } = Array.Empty<double>();
    public double[]? Phase { get; set; }
  }

  public class SonographyDataItem : ObservableObject
  {
    public string AudioId { get; set; }
    public SonographyConfig Config { get; set; }
    public ObservableCollection<SonographyFrameItem> Frames { get; set; }
    public double TotalDuration { get; set; }
    public int SampleRate { get; set; }
    public string DurationDisplay => $"{TotalDuration:F2}s";
    public string FrameCountDisplay => $"{Frames.Count} frames";

    public SonographyDataItem(SonographyData data)
    {
      AudioId = data.AudioId;
      Config = data.Config;
      Frames = new ObservableCollection<SonographyFrameItem>(
          data.Frames.Select(f => new SonographyFrameItem(f))
      );
      TotalDuration = data.TotalDuration;
      SampleRate = data.SampleRate;
    }
  }

  public class SonographyFrameItem : ObservableObject
  {
    public double Timestamp { get; set; }
    public double[] Frequencies { get; set; }
    public double[] Magnitudes { get; set; }
    public double[]? Phase { get; set; }
    public string TimestampDisplay => $"{Timestamp:F3}s";
    public double MaxMagnitude => Magnitudes.Length > 0 ? Magnitudes.Max() : 0.0;

    public SonographyFrameItem(SonographyFrame frame)
    {
      Timestamp = frame.Timestamp;
      Frequencies = frame.Frequencies;
      Magnitudes = frame.Magnitudes;
      Phase = frame.Phase;
    }
  }
}