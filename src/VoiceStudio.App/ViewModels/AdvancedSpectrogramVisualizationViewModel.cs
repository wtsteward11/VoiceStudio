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
  /// ViewModel for the AdvancedSpectrogramVisualizationView panel - Advanced spectrogram with multiple view types.
  /// </summary>
  public partial class AdvancedSpectrogramVisualizationViewModel : BaseViewModel, IPanelView
  {
    private readonly IBackendClient _backendClient;
    private readonly IProjectsClient _projectsClient;

    public string PanelId => "advanced-spectrogram-visualization";
    public string DisplayName => ResourceHelper.GetString("Panel.AdvancedSpectrogram.DisplayName", "Advanced Spectrogram");
    public PanelRegion Region => PanelRegion.Center;

    [ObservableProperty]
    private string? selectedAudioId;

    [ObservableProperty]
    private ObservableCollection<string> availableAudioIds = new();

    [ObservableProperty]
    private string selectedViewType = "magnitude";

    [ObservableProperty]
    private ObservableCollection<ViewTypeItem> availableViewTypes = new();

    [ObservableProperty]
    private int windowSize = 2048;

    [ObservableProperty]
    private int hopLength = 512;

    [ObservableProperty]
    private int nFFT = 2048;

    [ObservableProperty]
    private double? frequencyMin;

    [ObservableProperty]
    private double? frequencyMax;

    [ObservableProperty]
    private double? timeStart;

    [ObservableProperty]
    private double? timeEnd;

    [ObservableProperty]
    private string selectedColorScheme = "viridis";

    [ObservableProperty]
    private ObservableCollection<string> availableColorSchemes = new() { "viridis", "plasma", "inferno", "magma", "cividis", "hot", "cool" };

    [ObservableProperty]
    private bool applyFilters;

    [ObservableProperty]
    private ObservableCollection<string> selectedFilters = new();

    [ObservableProperty]
    private ObservableCollection<string> availableFilters = new() { "smoothing", "noise_reduction", "enhancement" };

    [ObservableProperty]
    private string? viewId;

    [ObservableProperty]
    private ObservableCollection<string> comparisonAudioIds = new();

    [ObservableProperty]
    private string comparisonType = "difference";

    public AdvancedSpectrogramVisualizationViewModel(IViewModelContext context, IBackendClient backendClient, IProjectsClient projectsClient)
        : base(context)
    {
      _backendClient = backendClient ?? throw new ArgumentNullException(nameof(backendClient));
      _projectsClient = projectsClient ?? throw new ArgumentNullException(nameof(projectsClient));

      LoadViewTypesCommand = new AsyncRelayCommand(() => LoadViewTypesAsync(CancellationToken.None));
      GenerateSpectrogramCommand = new AsyncRelayCommand(GenerateSpectrogramAsync);
      CompareSpectrogramsCommand = new AsyncRelayCommand(CompareSpectrogramsAsync);
      LoadAudioFilesCommand = new AsyncRelayCommand(() => LoadAudioFilesAsync(CancellationToken.None));
      RefreshCommand = new AsyncRelayCommand(RefreshAsync);

      // Load initial data
      _ = LoadViewTypesAsync(CancellationToken.None);
      _ = LoadAudioFilesAsync(CancellationToken.None);
    }

    public IAsyncRelayCommand LoadViewTypesCommand { get; }
    public IAsyncRelayCommand GenerateSpectrogramCommand { get; }
    public IAsyncRelayCommand CompareSpectrogramsCommand { get; }
    public IAsyncRelayCommand LoadAudioFilesCommand { get; }
    public IAsyncRelayCommand RefreshCommand { get; }

    private async Task LoadViewTypesAsync(CancellationToken cancellationToken)
    {
      IsLoading = true;
      ErrorMessage = null;

      try
      {
        var response = await _backendClient.SendRequestAsync<object, ViewTypesResponse>(
            "/api/advanced-spectrogram/view-types",
            null,
            System.Net.Http.HttpMethod.Get,
            cancellationToken
        );

        if (response?.ViewTypes != null)
        {
          AvailableViewTypes.Clear();
          foreach (var viewType in response.ViewTypes)
          {
            AvailableViewTypes.Add(new ViewTypeItem(viewType));
          }
        }
      }
      catch (OperationCanceledException)
      {
        return; // User cancelled
      }
      catch (Exception ex)
      {
        await HandleErrorAsync(ex, "LoadViewTypes");
      }
      finally
      {
        IsLoading = false;
      }
    }

    private async Task GenerateSpectrogramAsync()
    {
      if (string.IsNullOrEmpty(SelectedAudioId))
      {
        ErrorMessage = ResourceHelper.GetString("AdvancedSpectrogram.AudioRequired", "Audio must be selected");
        return;
      }

      try
      {
        IsLoading = true;
        ErrorMessage = null;

        var request = new
        {
          audio_id = SelectedAudioId,
          view_type = SelectedViewType,
          window_size = WindowSize,
          hop_length = HopLength,
          n_fft = NFFT,
          frequency_range = (FrequencyMin.HasValue || FrequencyMax.HasValue) ? new
          {
            min = FrequencyMin,
            max = FrequencyMax
          } : null,
          time_range = (TimeStart.HasValue || TimeEnd.HasValue) ? new
          {
            start = TimeStart,
            end = TimeEnd
          } : null,
          color_scheme = SelectedColorScheme,
          apply_filters = ApplyFilters,
          filters = SelectedFilters.ToArray()
        };

        var response = await _backendClient.SendRequestAsync<object, AdvancedSpectrogramResponse>(
            "/api/advanced-spectrogram/generate",
            request
        );

        if (response != null)
        {
          ViewId = response.ViewId;
          StatusMessage = response.Message;
        }
      }
      catch (Exception ex)
      {
        ErrorMessage = ResourceHelper.FormatString("AdvancedSpectrogram.GenerateSpectrogramFailed", ex.Message);
      }
      finally
      {
        IsLoading = false;
      }
    }

    private async Task CompareSpectrogramsAsync(CancellationToken cancellationToken)
    {
      if (ComparisonAudioIds.Count < 2)
      {
        ErrorMessage = ResourceHelper.GetString("AdvancedSpectrogram.MinimumAudioFilesRequired", "At least 2 audio files must be selected for comparison");
        return;
      }

      IsLoading = true;
      ErrorMessage = null;

      try
      {
        var request = new
        {
          audio_ids = ComparisonAudioIds.ToArray(),
          comparison_type = ComparisonType
        };

        var response = await _backendClient.SendRequestAsync<object, SpectrogramComparisonResponse>(
            "/api/advanced-spectrogram/compare",
            request,
            System.Net.Http.HttpMethod.Post,
            cancellationToken
        );

        if (response != null)
        {
          StatusMessage = ResourceHelper.GetString("AdvancedSpectrogram.SpectrogramsCompared", "Spectrograms compared successfully");
        }
      }
      catch (OperationCanceledException)
      {
        return; // User cancelled
      }
      catch (Exception ex)
      {
        await HandleErrorAsync(ex, "CompareSpectrograms");
      }
      finally
      {
        IsLoading = false;
      }
    }

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
          var audioFiles = await _backendClient.ListProjectAudioAsync(project.Id, cancellationToken);
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
      catch (Exception ex)
      {
        ErrorMessage = ResourceHelper.FormatString("AdvancedSpectrogram.LoadAudioFilesFailed", ex.Message);
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
        await LoadViewTypesAsync(cancellationToken);
        await LoadAudioFilesAsync(cancellationToken);
        StatusMessage = ResourceHelper.GetString("AdvancedSpectrogram.Refreshed", "Refreshed");
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

    // Response models
    private class ViewTypesResponse
    {
      public ViewTypeInfo[] ViewTypes { get; set; } = Array.Empty<ViewTypeInfo>();
    }

    private class AdvancedSpectrogramResponse
    {
      public string ViewId { get; set; } = string.Empty;
      public string? DataUrl { get; set; }
      public System.Collections.Generic.Dictionary<string, object> Metadata { get; set; } = new();
      public string Message { get; set; } = string.Empty;
    }

    private class SpectrogramComparisonResponse
    {
      public string Id { get; set; } = string.Empty;
      public string[] AudioIds { get; set; } = Array.Empty<string>();
      public string ComparisonType { get; set; } = string.Empty;
      public System.Collections.Generic.Dictionary<string, object> ResultData { get; set; } = new();
      public string Created { get; set; } = string.Empty;
    }
  }

  // Data models
  public class ViewTypeItem : ObservableObject
  {
    public string Id { get; set; }
    public string Name { get; set; }

    public ViewTypeItem(ViewTypeInfo info)
    {
      Id = info.Id;
      Name = info.Name;
    }
  }

  public class ViewTypeInfo
  {
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
  }
}