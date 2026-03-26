using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VoiceStudio.Core.Panels;
using VoiceStudio.Core.Services;
using VoiceStudio.App.Utilities;

namespace VoiceStudio.App.ViewModels
{
  /// <summary>
  /// ViewModel for the SpatialAudioView panel - 3D audio positioning and spatialization.
  /// </summary>
  public partial class SpatialAudioViewModel : BaseViewModel, IPanelView
  {
    private readonly ISpatialAudioClient _spatialAudioClient;

    public string PanelId => PanelIds.SpatialAudio;
    public string DisplayName => ResourceHelper.GetString("Panel.SpatialAudio.DisplayName", "Spatial Audio");
    public PanelRegion Region => PanelRegion.Right;

    [ObservableProperty]
    private string? audioId;

    [ObservableProperty]
    private float positionX;

    [ObservableProperty]
    private float positionY;

    [ObservableProperty]
    private float positionZ;

    [ObservableProperty]
    private float distance = 1.0f;

    [ObservableProperty]
    private float roomSize = 1.0f;

    [ObservableProperty]
    private string selectedMaterial = "concrete";

    [ObservableProperty]
    private ObservableCollection<string> availableMaterials = new() { "concrete", "wood", "carpet", "metal", "glass", "fabric", "outdoor" };

    [ObservableProperty]
    private float reverbAmount;

    [ObservableProperty]
    private bool enableDoppler;

    [ObservableProperty]
    private bool enableHRTF = true;

    [ObservableProperty]
    private string? selectedPreset = "None";

    [ObservableProperty]
    private ObservableCollection<string> availablePresets = new() { "None", "Small Room", "Concert Hall", "Outdoor", "Studio", "Cathedral" };

    [ObservableProperty]
    private string? processedAudioId;

    [ObservableProperty]
    private string? processedAudioUrl;

    [ObservableProperty]
    private bool isPreviewing;

    public SpatialAudioViewModel(IViewModelContext context, ISpatialAudioClient spatialAudioClient)
        : base(context)
    {
      _spatialAudioClient = spatialAudioClient ?? throw new ArgumentNullException(nameof(spatialAudioClient));

      SetPositionCommand = new AsyncRelayCommand(SetPositionAsync, () => !string.IsNullOrWhiteSpace(AudioId));
      ConfigureEnvironmentCommand = new AsyncRelayCommand(ConfigureEnvironmentAsync);
      ProcessAudioCommand = new AsyncRelayCommand(ProcessAudioAsync, () => !string.IsNullOrWhiteSpace(AudioId));
      PreviewAudioCommand = new AsyncRelayCommand(PreviewAudioAsync, () => !string.IsNullOrWhiteSpace(AudioId));
      ApplyPresetCommand = new AsyncRelayCommand(ApplyPresetAsync);
      ResetCommand = new AsyncRelayCommand(ResetAsync);
    }

    public IAsyncRelayCommand SetPositionCommand { get; }
    public IAsyncRelayCommand ConfigureEnvironmentCommand { get; }
    public IAsyncRelayCommand ProcessAudioCommand { get; }
    public IAsyncRelayCommand PreviewAudioCommand { get; }
    public IAsyncRelayCommand ApplyPresetCommand { get; }
    public IAsyncRelayCommand ResetCommand { get; }

    partial void OnAudioIdChanged(string? value)
    {
      SetPositionCommand.NotifyCanExecuteChanged();
      ProcessAudioCommand.NotifyCanExecuteChanged();
      PreviewAudioCommand.NotifyCanExecuteChanged();
    }

    partial void OnSelectedPresetChanged(string? value)
    {
      if (!string.IsNullOrWhiteSpace(value) && value != "None")
      {
        _ = ApplyPresetAsync();
      }
    }

    private async Task SetPositionAsync()
    {
      if (string.IsNullOrWhiteSpace(AudioId))
      {
        return;
      }

      try
      {
        IsLoading = true;
        ErrorMessage = null;

        var request = new SpatialPositionRequest
        {
          AudioId = AudioId,
          X = PositionX,
          Y = PositionY,
          Z = PositionZ,
          Distance = Distance
        };

        var response = await _spatialAudioClient.SetPositionAsync(request);

        if (response != null)
        {
          StatusMessage = ResourceHelper.FormatString("SpatialAudio.PositionSet", PositionX, PositionY, PositionZ, Distance);
        }
      }
      catch (Exception ex)
      {
        ErrorMessage = ResourceHelper.FormatString("SpatialAudio.SetPositionFailed", ex.Message);
      }
      finally
      {
        IsLoading = false;
      }
    }

    private async Task ConfigureEnvironmentAsync()
    {
      try
      {
        IsLoading = true;
        ErrorMessage = null;

        var request = new SpatialEnvironmentRequest
        {
          RoomSize = RoomSize,
          Material = SelectedMaterial,
          ReverbAmount = ReverbAmount,
          Doppler = EnableDoppler
        };

        var response = await _spatialAudioClient.ConfigureEnvironmentAsync(request);

        if (response != null)
        {
          StatusMessage = ResourceHelper.FormatString("SpatialAudio.EnvironmentConfigured", SelectedMaterial, RoomSize);
        }
      }
      catch (Exception ex)
      {
        ErrorMessage = ResourceHelper.FormatString("SpatialAudio.ConfigureEnvironmentFailed", ex.Message);
      }
      finally
      {
        IsLoading = false;
      }
    }

    private async Task ProcessAudioAsync()
    {
      if (string.IsNullOrWhiteSpace(AudioId))
      {
        return;
      }

      try
      {
        IsLoading = true;
        ErrorMessage = null;

        var request = new SpatialProcessRequest
        {
          AudioId = AudioId,
          Position = new SpatialPositionData
          {
            X = PositionX,
            Y = PositionY,
            Z = PositionZ,
            Distance = Distance
          },
          Environment = new SpatialEnvironmentData
          {
            RoomSize = RoomSize,
            Material = SelectedMaterial,
            ReverbAmount = ReverbAmount,
            Doppler = EnableDoppler
          }
        };

        var response = await _spatialAudioClient.ProcessAudioAsync(request);

        if (response != null)
        {
          ProcessedAudioId = response.ProcessedAudioId;
          ProcessedAudioUrl = response.ProcessedAudioUrl;
          StatusMessage = ResourceHelper.GetString("SpatialAudio.ProcessingCompleted", "Spatial audio processing completed");
        }
      }
      catch (Exception ex)
      {
        ErrorMessage = ResourceHelper.FormatString("SpatialAudio.ProcessAudioFailed", ex.Message);
      }
      finally
      {
        IsLoading = false;
      }
    }

    private async Task PreviewAudioAsync()
    {
      if (string.IsNullOrWhiteSpace(AudioId))
      {
        return;
      }

      try
      {
        IsPreviewing = true;
        ErrorMessage = null;

        var response = await _spatialAudioClient.PreviewAsync(AudioId, PositionX, PositionY, PositionZ, Distance);

        StatusMessage = ResourceHelper.GetString("SpatialAudio.PreviewStarted", "Preview started (requires spatial audio libraries)");
      }
      catch (Exception ex)
      {
        ErrorMessage = ResourceHelper.FormatString("SpatialAudio.PreviewAudioFailed", ex.Message);
      }
      finally
      {
        IsPreviewing = false;
      }
    }

    private async Task ApplyPresetAsync()
    {
      if (string.IsNullOrWhiteSpace(SelectedPreset) || SelectedPreset == "None")
      {
        return;
      }

      try
      {
        // Apply preset values
        switch (SelectedPreset)
        {
          case "Small Room":
            RoomSize = 0.5f;
            SelectedMaterial = "concrete";
            ReverbAmount = 0.3f;
            EnableDoppler = false;
            break;
          case "Concert Hall":
            RoomSize = 3.0f;
            SelectedMaterial = "wood";
            ReverbAmount = 0.8f;
            EnableDoppler = false;
            break;
          case "Outdoor":
            RoomSize = 10.0f;
            SelectedMaterial = "outdoor";
            ReverbAmount = 0.1f;
            EnableDoppler = true;
            break;
          case "Studio":
            RoomSize = 0.3f;
            SelectedMaterial = "fabric";
            ReverbAmount = 0.1f;
            EnableDoppler = false;
            break;
          case "Cathedral":
            RoomSize = 5.0f;
            SelectedMaterial = "concrete";
            ReverbAmount = 0.9f;
            EnableDoppler = false;
            break;
        }

        await ConfigureEnvironmentAsync();
        StatusMessage = ResourceHelper.FormatString("SpatialAudio.PresetApplied", SelectedPreset);
      }
      catch (Exception ex)
      {
        ErrorMessage = ResourceHelper.FormatString("SpatialAudio.ApplyPresetFailed", ex.Message);
      }
    }

    private Task ResetAsync()
    {
      PositionX = 0.0f;
      PositionY = 0.0f;
      PositionZ = 0.0f;
      Distance = 1.0f;
      RoomSize = 1.0f;
      SelectedMaterial = "concrete";
      ReverbAmount = 0.0f;
      EnableDoppler = false;
      SelectedPreset = "None";
      StatusMessage = ResourceHelper.GetString("SpatialAudio.ResetToDefaults", "Reset to defaults");

      return Task.CompletedTask;
    }
  }
}