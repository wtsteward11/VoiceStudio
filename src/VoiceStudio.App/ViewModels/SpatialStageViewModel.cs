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
  /// ViewModel for the SpatialStageView panel - Spatial audio positioning.
  /// </summary>
  public partial class SpatialStageViewModel : BaseViewModel, IPanelView, IPanelLifecycle
  {
    private readonly ISpatialStageClient _client;
    private readonly IProjectsClient _projectsClient;
    private readonly IProjectAudioClient _projectAudioClient;

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

    public SpatialStageViewModel(IViewModelContext context, ISpatialStageClient client, IProjectsClient projectsClient, IProjectAudioClient projectAudioClient)
        : base(context)
    {
      _client = client ?? throw new ArgumentNullException(nameof(client));
      _projectsClient = projectsClient ?? throw new ArgumentNullException(nameof(projectsClient));
      _projectAudioClient = projectAudioClient ?? throw new ArgumentNullException(nameof(projectAudioClient));

      LoadConfigsCommand = new AsyncRelayCommand(LoadConfigsAsync);
      CreateConfigCommand = new AsyncRelayCommand(CreateConfigAsync);
      UpdateConfigCommand = new AsyncRelayCommand(UpdateConfigAsync);
      DeleteConfigCommand = new AsyncRelayCommand(DeleteConfigAsync);
      ApplySpatialCommand = new AsyncRelayCommand(ApplySpatialAsync);
      PreviewSpatialCommand = new AsyncRelayCommand(PreviewSpatialAsync);
      LoadAudioFilesCommand = new AsyncRelayCommand(LoadAudioFilesAsync);
      RefreshCommand = new AsyncRelayCommand(RefreshAsync);
    }

    public IAsyncRelayCommand LoadConfigsCommand { get; }
    public IAsyncRelayCommand CreateConfigCommand { get; }
    public IAsyncRelayCommand UpdateConfigCommand { get; }
    public IAsyncRelayCommand DeleteConfigCommand { get; }
    public IAsyncRelayCommand ApplySpatialCommand { get; }
    public IAsyncRelayCommand PreviewSpatialCommand { get; }
    public IAsyncRelayCommand LoadAudioFilesCommand { get; }
    public IAsyncRelayCommand RefreshCommand { get; }

    Task IPanelLifecycle.OnActivatedAsync(CancellationToken cancellationToken)
    {
      return RefreshAsync(cancellationToken);
    }

    Task IPanelLifecycle.OnDeactivatedAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    Task IPanelLifecycle.RefreshAsync(CancellationToken cancellationToken)
    {
      return RefreshAsync(cancellationToken);
    }

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
        var list = await _client.GetConfigsAsync(cancellationToken);

        if (list != null)
        {
          Configs.Clear();
          foreach (var config in list)
          {
            Configs.Add(new SpatialConfigItem(config));
          }
        }
      }
      catch (OperationCanceledException)
      {
        return;
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
        var request = new SpatialConfigCreateRequest
        {
          Name = ConfigName,
          AudioId = SelectedAudioId,
          X = PositionX,
          Y = PositionY,
          Z = PositionZ,
          Distance = Distance,
          RoomSize = RoomSize,
          ReverbAmount = ReverbAmount,
          Occlusion = Occlusion,
          Doppler = EnableDoppler,
          Hrtf = EnableHrtf
        };

        var config = await _client.CreateConfigAsync(request, cancellationToken);

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
        return;
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
        var request = new SpatialConfigUpdateRequest
        {
          Name = ConfigName,
          AudioId = SelectedAudioId,
          X = PositionX,
          Y = PositionY,
          Z = PositionZ,
          Distance = Distance,
          RoomSize = RoomSize,
          ReverbAmount = ReverbAmount,
          Occlusion = Occlusion,
          Doppler = EnableDoppler,
          Hrtf = EnableHrtf
        };

        var config = await _client.UpdateConfigAsync(SelectedConfig.ConfigId, request, cancellationToken);

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
        return;
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
        await _client.DeleteConfigAsync(SelectedConfig.ConfigId, cancellationToken);

        Configs.Remove(SelectedConfig);
        SelectedConfig = null;
        StatusMessage = ResourceHelper.GetString("SpatialStage.ConfigDeleted", "Config deleted");
      }
      catch (OperationCanceledException)
      {
        return;
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
        var response = await _client.ApplySpatialAsync(SelectedConfig.ConfigId, "wav", cancellationToken);

        if (response != null)
        {
          StatusMessage = ResourceHelper.FormatString("SpatialStage.SpatialAudioApplied", response.AudioId);
        }
      }
      catch (OperationCanceledException)
      {
        return;
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
        var response = await _client.PreviewSpatialAsync(SelectedAudioId, PositionX, PositionY, PositionZ, Distance, cancellationToken);

        if (response != null)
        {
          StatusMessage = ResourceHelper.GetString("SpatialStage.PreviewAvailable", "Preview available");
        }
      }
      catch (OperationCanceledException)
      {
        return;
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
        return;
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
  }

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

    public SpatialConfigItem(SpatialConfigInfo config)
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
