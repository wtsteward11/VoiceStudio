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
  /// ViewModel for the AdvancedSpectrogramVisualizationView panel - Advanced spectrogram with multiple view types.
  /// </summary>
  public partial class AdvancedSpectrogramVisualizationViewModel : BaseViewModel, IPanelView, IPanelLifecycle
  {
    private readonly IAdvancedSpectrogramClient _spectrogramClient;
    private readonly IProjectAudioClient _projectAudioClient;
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

    public AdvancedSpectrogramVisualizationViewModel(
      IViewModelContext context,
      IAdvancedSpectrogramClient spectrogramClient,
      IProjectAudioClient projectAudioClient,
      IProjectsClient projectsClient)
        : base(context)
    {
      _spectrogramClient = spectrogramClient ?? throw new ArgumentNullException(nameof(spectrogramClient));
      _projectAudioClient = projectAudioClient ?? throw new ArgumentNullException(nameof(projectAudioClient));
      _projectsClient = projectsClient ?? throw new ArgumentNullException(nameof(projectsClient));

      LoadViewTypesCommand = new AsyncRelayCommand(() => LoadViewTypesAsync(CancellationToken.None));
      GenerateSpectrogramCommand = new AsyncRelayCommand(GenerateSpectrogramAsync);
      CompareSpectrogramsCommand = new AsyncRelayCommand(CompareSpectrogramsAsync);
      LoadAudioFilesCommand = new AsyncRelayCommand(() => LoadAudioFilesAsync(CancellationToken.None));
      RefreshCommand = new AsyncRelayCommand(RefreshAsync);
    }

    /// <inheritdoc />
    public Task OnActivatedAsync(CancellationToken cancellationToken = default)
    {
      _ = LoadViewTypesAsync(cancellationToken);
      _ = LoadAudioFilesAsync(cancellationToken);
      return Task.CompletedTask;
    }

    Task IPanelLifecycle.OnDeactivatedAsync(CancellationToken ct) => Task.CompletedTask;

    async Task IPanelLifecycle.RefreshAsync(CancellationToken ct) => await RefreshAsync(ct);

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
        var response = await _spectrogramClient.GetViewTypesAsync(cancellationToken);

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

        var request = new AdvancedSpectrogramGenerateRequest
        {
          AudioId = SelectedAudioId,
          ViewType = SelectedViewType,
          WindowSize = WindowSize,
          HopLength = HopLength,
          NFFT = NFFT,
          FrequencyRange = (FrequencyMin.HasValue || FrequencyMax.HasValue)
            ? new AdvancedSpectrogramRange { Min = FrequencyMin, Max = FrequencyMax }
            : null,
          TimeRange = (TimeStart.HasValue || TimeEnd.HasValue)
            ? new AdvancedSpectrogramTimeRange { Start = TimeStart, End = TimeEnd }
            : null,
          ColorScheme = SelectedColorScheme,
          ApplyFilters = ApplyFilters,
          Filters = SelectedFilters.ToArray()
        };

        var response = await _spectrogramClient.GenerateSpectrogramAsync(request);

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
        var request = new AdvancedSpectrogramCompareRequest
        {
          AudioIds = ComparisonAudioIds.ToArray(),
          ComparisonType = ComparisonType
        };

        var response = await _spectrogramClient.CompareSpectrogramsAsync(request, cancellationToken);

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

  }

  // Data models
  public class ViewTypeItem : ObservableObject
  {
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;

    public ViewTypeItem(AdvancedSpectrogramViewTypeInfo info)
    {
      Id = info.Id;
      Name = info.Name;
    }
  }
}