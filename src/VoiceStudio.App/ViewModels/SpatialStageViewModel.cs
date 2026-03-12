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
using SpatialConfigModel = VoiceStudio.App.ViewModels.SpatialStageViewModel.SpatialConfig;

namespace VoiceStudio.App.ViewModels
{
  /// <summary>
  /// ViewModel for the SpatialStageView panel - Spatial audio positioning.
  /// </summary>
  public partial class SpatialStageViewModel : BaseViewModel, IPanelView
  {
    private readonly IBackendClient _backendClient;
    private readonly IProjectsClient _projectsClient;

    public string PanelId => "spatial-stage";
    public string DisplayName => ResourceHelper.GetString("Panel.SpatialStage.DisplayName", "Spatial Audio");
    public PanelRegion Region => PanelRegion.Center;

    [ObservableProperty]
    private ObservableCollection<SpatialConfigItem> configs = new();

    [ObservableProperty]
    private SpatialConfigItem? selectedConfig;

    [ObservableProperty]
    private string configName = string.Empty;

    [ObservableProperty]
    private string? selectedAudioId;

    [ObservableProperty]
    private ObservableCollection<string> availableAudioIds = new();

    [ObservableProperty]
    private double positionX;

    [ObservableProperty]
    private double positionY;

    [ObservableProperty]
    private double positionZ;

    [ObservableProperty]
    private double distance = 1.0;

    [ObservableProperty]
    private double roomSize = 1.0;

    [ObservableProperty]
    private double reverbAmount;

    [ObservableProperty]
    private double occlusion;

    [ObservableProperty]
    private bool enableDoppler;

    [ObservableProperty]
    private bool enableHrtf = true;

    public SpatialStageViewModel(IViewModelContext context, IBackendClient backendClient, IProjectsClient projectsClient)
        : base(context)
    {
      _backendClient = backendClient ?? throw new ArgumentNullException(nameof(backendClient));
      _projectsClient = projectsClient ?? throw new ArgumentNullException(nameof(projectsClient));

      LoadConfigsCommand = new AsyncRelayCommand(LoadConfigsAsync);
      CreateConfigCommand = new AsyncRelayCommand(CreateConfigAsync);
      UpdateConfigCommand = new AsyncRelayCommand(UpdateConfigAsync);
      DeleteConfigCommand = new AsyncRelayCommand(DeleteConfigAsync);
      ApplySpatialCommand = new AsyncRelayCommand(ApplySpatialAsync);
      PreviewSpatialCommand = new AsyncRelayCommand(PreviewSpatialAsync);
      LoadAudioFilesCommand = new AsyncRelayCommand(LoadAudioFilesAsync);
      RefreshCommand = new AsyncRelayCommand(RefreshAsync);

      // Load initial data
      _ = LoadConfigsAsync(CancellationToken.None);
      _ = LoadAudioFilesAsync(CancellationToken.None);
    }

    public IAsyncRelayCommand LoadConfigsCommand { get; }
    public IAsyncRelayCommand CreateConfigCommand { get; }
    public IAsyncRelayCommand UpdateConfigCommand { get; }
    public IAsyncRelayCommand DeleteConfigCommand { get; }
    public IAsyncRelayCommand ApplySpatialCommand { get; }
    public IAsyncRelayCommand PreviewSpatialCommand { get; }
    public IAsyncRelayCommand LoadAudioFilesCommand { get; }
    public IAsyncRelayCommand RefreshCommand { get; }

    partial void OnSelectedConfigChanged(SpatialConfigItem? value)
    {
      if (value != null)
      {
        ConfigName = value.Name;
        SelectedAudioId = value.AudioId;
        PositionX = value.PositionX;
        PositionY = value.PositionY;
        PositionZ = value.PositionZ;
        Distance = value.Distance;
        RoomSize = value.RoomSize;
        ReverbAmount = value.ReverbAmount;
        Occlusion = value.Occlusion;
        EnableDoppler = value.EnableDoppler;
        EnableHrtf = value.EnableHrtf;
      }
    }

    private async Task LoadConfigsAsync(CancellationToken cancellationToken)
    {
      IsLoading = true;
      ErrorMessage = null;

      try
      {
        var configs = await _backendClient.SendRequestAsync<object, SpatialConfig[]>(
            "/api/spatial-audio/configs",
            null,
            System.Net.Http.HttpMethod.Get,
            cancellationToken
        );

        if (configs != null)
        {
          Configs.Clear();
          foreach (var config in configs)
          {
            Configs.Add(new SpatialConfigItem(config));
          }
        }
      }
      catch (OperationCanceledException)
      {
        return; // User cancelled
      }
      catch (Exception ex)
      {
        await HandleErrorAsync(ex, "LoadConfigs");
      }
      finally
      {
        IsLoading = false;
      }
    }

    private async Task CreateConfigAsync(CancellationToken cancellationToken)
    {
      if (string.IsNullOrWhiteSpace(ConfigName))
      {
        ErrorMessage = ResourceHelper.GetString("SpatialStage.ConfigNameRequired", "Config name is required");
        return;
      }

      if (string.IsNullOrEmpty(SelectedAudioId))
      {
        ErrorMessage = ResourceHelper.GetString("SpatialStage.AudioRequired", "Audio must be selected");
        return;
      }

      IsLoading = true;
      ErrorMessage = null;

      try
      {
        var request = new
        {
          name = ConfigName,
          audio_id = SelectedAudioId,
          x = PositionX,
          y = PositionY,
          z = PositionZ,
          distance = Distance,
          room_size = RoomSize,
          reverb_amount = ReverbAmount,
          occlusion = Occlusion,
          doppler = EnableDoppler,
          hrtf = EnableHrtf
        };

        var config = await _backendClient.SendRequestAsync<object, SpatialConfig>(
            "/api/spatial-audio/configs",
            request,
            System.Net.Http.HttpMethod.Post,
            cancellationToken
        );

        if (config != null)
        {
          var configItem = new SpatialConfigItem(config);
          Configs.Add(configItem);
          SelectedConfig = configItem;
          StatusMessage = ResourceHelper.GetString("SpatialStage.ConfigCreated", "Config created");
        }
      }
      catch (OperationCanceledException)
      {
        return; // User cancelled
      }
      catch (Exception ex)
      {
        await HandleErrorAsync(ex, "CreateConfig");
      }
      finally
      {
        IsLoading = false;
      }
    }

    private async Task UpdateConfigAsync(CancellationToken cancellationToken)
    {
      if (SelectedConfig == null)
      {
        ErrorMessage = ResourceHelper.GetString("SpatialStage.NoConfigSelected", "No config selected");
        return;
      }

      IsLoading = true;
      ErrorMessage = null;

      try
      {
        var request = new
        {
          name = ConfigName,
          audio_id = SelectedAudioId,
          x = PositionX,
          y = PositionY,
          z = PositionZ,
          distance = Distance,
          room_size = RoomSize,
          reverb_amount = ReverbAmount,
          occlusion = Occlusion,
          doppler = EnableDoppler,
          hrtf = EnableHrtf
        };

        var config = await _backendClient.SendRequestAsync<object, SpatialConfig>(
            $"/api/spatial-audio/configs/{Uri.EscapeDataString(SelectedConfig.ConfigId)}",
            request,
            System.Net.Http.HttpMethod.Put,
            cancellationToken
        );

        if (config != null)
        {
          var index = Configs.IndexOf(SelectedConfig);
          var updatedItem = new SpatialConfigItem(config);
          Configs[index] = updatedItem;
          SelectedConfig = updatedItem;
          StatusMessage = ResourceHelper.GetString("SpatialStage.ConfigUpdated", "Config updated");
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

    private async Task DeleteConfigAsync(CancellationToken cancellationToken)
    {
      if (SelectedConfig == null)
      {
        ErrorMessage = ResourceHelper.GetString("SpatialStage.NoConfigSelected", "No config selected");
        return;
      }

      IsLoading = true;
      ErrorMessage = null;

      try
      {
        await _backendClient.SendRequestAsync<object, object>(
            $"/api/spatial-audio/configs/{Uri.EscapeDataString(SelectedConfig.ConfigId)}",
            null,
            System.Net.Http.HttpMethod.Delete,
            cancellationToken
        );

        Configs.Remove(SelectedConfig);
        SelectedConfig = null;
        StatusMessage = ResourceHelper.GetString("SpatialStage.ConfigDeleted", "Config deleted");
      }
      catch (OperationCanceledException)
      {
        return; // User cancelled
      }
      catch (Exception ex)
      {
        await HandleErrorAsync(ex, "DeleteConfig");
      }
      finally
      {
        IsLoading = false;
      }
    }

    private async Task ApplySpatialAsync(CancellationToken cancellationToken)
    {
      if (SelectedConfig == null)
      {
        ErrorMessage = ResourceHelper.GetString("SpatialStage.NoConfigSelected", "No config selected");
        return;
      }

      IsLoading = true;
      ErrorMessage = null;

      try
      {
        var request = new
        {
          config_id = SelectedConfig.ConfigId,
          output_format = "wav"
        };

        var response = await _backendClient.SendRequestAsync<object, SpatialApplyResponse>(
            "/api/spatial-audio/apply",
            request,
            System.Net.Http.HttpMethod.Post,
            cancellationToken
        );

        if (response != null)
        {
          StatusMessage = ResourceHelper.FormatString("SpatialStage.SpatialAudioApplied", response.AudioId);
        }
      }
      catch (OperationCanceledException)
      {
        return; // User cancelled
      }
      catch (Exception ex)
      {
        await HandleErrorAsync(ex, "ApplySpatial");
      }
      finally
      {
        IsLoading = false;
      }
    }

    private async Task PreviewSpatialAsync(CancellationToken cancellationToken)
    {
      if (string.IsNullOrEmpty(SelectedAudioId))
      {
        ErrorMessage = ResourceHelper.GetString("SpatialStage.AudioRequired", "Audio must be selected");
        return;
      }

      IsLoading = true;
      ErrorMessage = null;

      try
      {
        var response = await _backendClient.SendRequestAsync<object, SpatialPreviewResponse>(
            $"/api/spatial-audio/preview?audio_id={Uri.EscapeDataString(SelectedAudioId)}&x={PositionX}&y={PositionY}&z={PositionZ}&distance={Distance}",
            null,
            System.Net.Http.HttpMethod.Post,
            cancellationToken
        );

        if (response != null)
        {
          StatusMessage = ResourceHelper.GetString("SpatialStage.PreviewAvailable", "Preview available");
        }
      }
      catch (OperationCanceledException)
      {
        return; // User cancelled
      }
      catch (Exception ex)
      {
        await HandleErrorAsync(ex, "PreviewSpatial");
      }
      finally
      {
        IsLoading = false;
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
          var audioFiles = await _backendClient.ListProjectAudioAsync(project.Id, cancellationToken);
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

    private async Task RefreshAsync(CancellationToken cancellationToken)
    {
      await LoadConfigsAsync(cancellationToken);
      await LoadAudioFilesAsync(cancellationToken);
      StatusMessage = ResourceHelper.GetString("SpatialStage.Refreshed", "Refreshed");
    }

    // Response models
    public class SpatialConfig
    {
      public string ConfigId { get; set; } = string.Empty;
      public string Name { get; set; } = string.Empty;
      public string AudioId { get; set; } = string.Empty;
      public SpatialPosition Position { get; set; } = new();
      public double RoomSize { get; set; }
      public double ReverbAmount { get; set; }
      public double Occlusion { get; set; }
      public bool Doppler { get; set; }
      public bool Hrtf { get; set; }
    }

    public class SpatialPosition
    {
      public double X { get; set; }
      public double Y { get; set; }
      public double Z { get; set; }
      public double Distance { get; set; }
    }

    private class SpatialApplyResponse
    {
      public string AudioId { get; set; } = string.Empty;
      public string ConfigApplied { get; set; } = string.Empty;
      public string Message { get; set; } = string.Empty;
    }

    private class SpatialPreviewResponse
    {
      public string PreviewUrl { get; set; } = string.Empty;
      public SpatialPosition Position { get; set; } = new();
      public string Message { get; set; } = string.Empty;
    }
  }

  // Data models
  public class SpatialConfigItem : ObservableObject
  {
    public string ConfigId { get; set; }
    public string Name { get; set; }
    public string AudioId { get; set; }
    public double PositionX { get; set; }
    public double PositionY { get; set; }
    public double PositionZ { get; set; }
    public double Distance { get; set; }
    public double RoomSize { get; set; }
    public double ReverbAmount { get; set; }
    public double Occlusion { get; set; }
    public bool EnableDoppler { get; set; }
    public bool EnableHrtf { get; set; }
    public string PositionDisplay => ResourceHelper.FormatString("SpatialStage.PositionDisplay", PositionX, PositionY, PositionZ);

    public SpatialConfigItem(SpatialConfigModel config)
    {
      ConfigId = config.ConfigId;
      Name = config.Name;
      AudioId = config.AudioId;
      PositionX = config.Position.X;
      PositionY = config.Position.Y;
      PositionZ = config.Position.Z;
      Distance = config.Position.Distance;
      RoomSize = config.RoomSize;
      ReverbAmount = config.ReverbAmount;
      Occlusion = config.Occlusion;
      EnableDoppler = config.Doppler;
      EnableHrtf = config.Hrtf;
    }
  }
}