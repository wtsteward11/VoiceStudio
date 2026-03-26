using System;
using System.Collections.ObjectModel;
using System.Collections.Generic;
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
  /// ViewModel for the VoiceMorphView panel - Voice morphing and blending.
  /// </summary>
  public partial class VoiceMorphViewModel : BaseViewModel, IPanelView, IPanelLifecycle
  {
    private readonly IVoiceMorphClient _voiceMorphClient;
    private readonly IProjectAudioClient _projectAudioClient;
    private readonly IProjectsClient _projectsClient;
    private readonly IProfilesClient _profilesClient;
    private readonly CancellationTokenSource _disposalCts = new();

    public string PanelId => PanelIds.VoiceMorph;
    public string DisplayName => ResourceHelper.GetString("Panel.VoiceMorph.DisplayName", "Voice Morphing");
    public PanelRegion Region => PanelRegion.Center;

    [ObservableProperty]
    private ObservableCollection<MorphConfigItem> configs = new();

    [ObservableProperty]
    private MorphConfigItem? selectedConfig;

    [ObservableProperty]
    private string configName = string.Empty;

    [ObservableProperty]
    private string? selectedSourceAudioId;

    [ObservableProperty]
    private ObservableCollection<string> availableAudioIds = new();

    [ObservableProperty]
    private ObservableCollection<VoiceBlendItem> targetVoices = new();

    [ObservableProperty]
    private VoiceBlendItem? selectedTargetVoice;

    [ObservableProperty]
    private string? selectedVoiceProfileId;

    [ObservableProperty]
    private ObservableCollection<string> availableVoiceProfiles = new();

    [ObservableProperty]
    private double voiceWeight = 0.5;

    [ObservableProperty]
    private double morphStrength = 0.5;

    [ObservableProperty]
    private bool preserveEmotion = true;

    [ObservableProperty]
    private bool preserveProsody = true;

    public VoiceMorphViewModel(IViewModelContext context, IVoiceMorphClient voiceMorphClient, IProjectAudioClient projectAudioClient, IProjectsClient projectsClient, IProfilesClient profilesClient)
        : base(context)
    {
      _voiceMorphClient = voiceMorphClient ?? throw new ArgumentNullException(nameof(voiceMorphClient));
      _projectAudioClient = projectAudioClient ?? throw new ArgumentNullException(nameof(projectAudioClient));
      _projectsClient = projectsClient ?? throw new ArgumentNullException(nameof(projectsClient));
      _profilesClient = profilesClient ?? throw new ArgumentNullException(nameof(profilesClient));

      LoadConfigsCommand = new EnhancedAsyncRelayCommand(async (ct) =>
      {
        using var profiler = PerformanceProfiler.StartCommand("LoadConfigs");
        await LoadConfigsAsync(ct);
      }, () => !IsLoading);
      CreateConfigCommand = new EnhancedAsyncRelayCommand(async (ct) =>
      {
        using var profiler = PerformanceProfiler.StartCommand("CreateConfig");
        await CreateConfigAsync(ct);
      }, () => !IsLoading);
      UpdateConfigCommand = new EnhancedAsyncRelayCommand(async (ct) =>
      {
        using var profiler = PerformanceProfiler.StartCommand("UpdateConfig");
        await UpdateConfigAsync(ct);
      }, () => SelectedConfig != null && !IsLoading);
      DeleteConfigCommand = new EnhancedAsyncRelayCommand(async (ct) =>
      {
        using var profiler = PerformanceProfiler.StartCommand("DeleteConfig");
        await DeleteConfigAsync(ct);
      }, () => SelectedConfig != null && !IsLoading);
      AddTargetVoiceCommand = new EnhancedAsyncRelayCommand(async (ct) =>
      {
        using var profiler = PerformanceProfiler.StartCommand("AddTargetVoice");
        await AddTargetVoiceAsync(ct);
      }, () => !string.IsNullOrEmpty(SelectedVoiceProfileId) && !IsLoading);
      RemoveTargetVoiceCommand = new EnhancedAsyncRelayCommand(async (ct) =>
      {
        using var profiler = PerformanceProfiler.StartCommand("RemoveTargetVoice");
        await RemoveTargetVoiceAsync(ct);
      }, () => SelectedTargetVoice != null && !IsLoading);
      ApplyMorphCommand = new EnhancedAsyncRelayCommand(async (ct) =>
      {
        using var profiler = PerformanceProfiler.StartCommand("ApplyMorph");
        await ApplyMorphAsync(ct);
      }, () => SelectedConfig != null && !IsLoading);
      LoadAudioFilesCommand = new EnhancedAsyncRelayCommand(async (ct) =>
      {
        using var profiler = PerformanceProfiler.StartCommand("LoadAudioFiles");
        await LoadAudioFilesAsync(ct);
      }, () => !IsLoading);
      LoadVoiceProfilesCommand = new EnhancedAsyncRelayCommand(async (ct) =>
      {
        using var profiler = PerformanceProfiler.StartCommand("LoadVoiceProfiles");
        await LoadVoiceProfilesAsync(ct);
      }, () => !IsLoading);
      RefreshCommand = new EnhancedAsyncRelayCommand(async (ct) =>
      {
        using var profiler = PerformanceProfiler.StartCommand("Refresh");
        await RefreshCoreAsync(ct);
      }, () => !IsLoading);
    }

    /// <inheritdoc />
    public async Task OnActivatedAsync(CancellationToken cancellationToken = default)
    {
      using var linked = CancellationTokenSource.CreateLinkedTokenSource(_disposalCts.Token, cancellationToken);
      await LoadConfigsAsync(linked.Token);
      await LoadAudioFilesAsync(linked.Token);
      await LoadVoiceProfilesAsync(linked.Token);
    }

    /// <inheritdoc />
    public Task OnDeactivatedAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    /// <inheritdoc />
    public Task RefreshAsync(CancellationToken cancellationToken = default) => RefreshCoreAsync(cancellationToken);

    protected override void Dispose(bool disposing)
    {
      if (disposing)
      {
        _disposalCts.Cancel();
        _disposalCts.Dispose();
      }
      base.Dispose(disposing);
    }

    public IAsyncRelayCommand LoadConfigsCommand { get; }
    public IAsyncRelayCommand CreateConfigCommand { get; }
    public IAsyncRelayCommand UpdateConfigCommand { get; }
    public IAsyncRelayCommand DeleteConfigCommand { get; }
    public IAsyncRelayCommand AddTargetVoiceCommand { get; }
    public IAsyncRelayCommand RemoveTargetVoiceCommand { get; }
    public IAsyncRelayCommand ApplyMorphCommand { get; }
    public IAsyncRelayCommand LoadAudioFilesCommand { get; }
    public IAsyncRelayCommand LoadVoiceProfilesCommand { get; }
    public IAsyncRelayCommand RefreshCommand { get; }

    partial void OnSelectedConfigChanged(MorphConfigItem? value)
    {
      if (value != null)
      {
        ConfigName = value.Name;
        SelectedSourceAudioId = value.SourceAudioId;
        MorphStrength = value.MorphStrength;
        PreserveEmotion = value.PreserveEmotion;
        PreserveProsody = value.PreserveProsody;
        TargetVoices.Clear();
        foreach (var voice in value.TargetVoices)
        {
          TargetVoices.Add(voice);
        }
      }
    }

    private async Task LoadConfigsAsync(CancellationToken cancellationToken)
    {
      try
      {
        IsLoading = true;
        ErrorMessage = null;

        var configs = await _voiceMorphClient.GetConfigsAsync(cancellationToken).ConfigureAwait(false);

        if (configs != null)
        {
          Configs.Clear();
          foreach (var config in configs)
          {
            Configs.Add(new MorphConfigItem(config));
          }
        }
      }
      catch (Exception ex)
      {
        ErrorMessage = ResourceHelper.FormatString("VoiceMorph.LoadConfigsFailed", ex.Message);
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
        ErrorMessage = ResourceHelper.GetString("VoiceMorph.ConfigNameRequired", "Config name is required");
        return;
      }

      if (string.IsNullOrEmpty(SelectedSourceAudioId))
      {
        ErrorMessage = ResourceHelper.GetString("VoiceMorph.SourceAudioRequired", "Source audio must be selected");
        return;
      }

      if (TargetVoices.Count == 0)
      {
        ErrorMessage = ResourceHelper.GetString("VoiceMorph.TargetVoiceRequired", "At least one target voice is required");
        return;
      }

      IsLoading = true;
      ErrorMessage = null;

      try
      {
        var targetVoices = TargetVoices.Select(v => (v.VoiceProfileId, v.Weight)).ToList();
        var config = await _voiceMorphClient.CreateConfigAsync(
            ConfigName,
            SelectedSourceAudioId!,
            targetVoices,
            MorphStrength,
            PreserveEmotion,
            PreserveProsody,
            cancellationToken
        ).ConfigureAwait(false);

        if (config != null)
        {
          var configItem = new MorphConfigItem(config);
          Configs.Add(configItem);
          SelectedConfig = configItem;
          StatusMessage = ResourceHelper.GetString("VoiceMorph.ConfigCreated", "Config created");
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
        ErrorMessage = ResourceHelper.GetString("VoiceMorph.NoConfigSelected", "No config selected");
        return;
      }

      try
      {
        IsLoading = true;
        ErrorMessage = null;

        var targetVoices = TargetVoices.Select(v => (v.VoiceProfileId, v.Weight)).ToList();
        var config = await _voiceMorphClient.UpdateConfigAsync(
            SelectedConfig.ConfigId,
            ConfigName,
            SelectedSourceAudioId ?? string.Empty,
            targetVoices,
            MorphStrength,
            PreserveEmotion,
            PreserveProsody,
            cancellationToken
        ).ConfigureAwait(false);

        if (config != null)
        {
          var index = Configs.IndexOf(SelectedConfig);
          var updatedItem = new MorphConfigItem(config);
          Configs[index] = updatedItem;
          SelectedConfig = updatedItem;
          StatusMessage = ResourceHelper.GetString("VoiceMorph.ConfigUpdated", "Config updated");
        }
      }
      catch (Exception ex)
      {
        ErrorMessage = ResourceHelper.FormatString("VoiceMorph.UpdateConfigFailed", ex.Message);
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
        ErrorMessage = ResourceHelper.GetString("VoiceMorph.NoConfigSelected", "No config selected");
        return;
      }

      IsLoading = true;
      ErrorMessage = null;

      try
      {
        await _voiceMorphClient.DeleteConfigAsync(SelectedConfig.ConfigId, cancellationToken).ConfigureAwait(false);

        Configs.Remove(SelectedConfig);
        SelectedConfig = null;
        StatusMessage = ResourceHelper.GetString("VoiceMorph.ConfigDeleted", "Config deleted");
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

    private Task AddTargetVoiceAsync(CancellationToken cancellationToken)
    {
      cancellationToken.ThrowIfCancellationRequested();

      if (string.IsNullOrEmpty(SelectedVoiceProfileId))
      {
        ErrorMessage = ResourceHelper.GetString("VoiceMorph.VoiceProfileRequired", "Voice profile must be selected");
        return Task.CompletedTask;
      }

      var blend = new VoiceBlendItem
      {
        VoiceProfileId = SelectedVoiceProfileId,
        Weight = VoiceWeight
      };

      TargetVoices.Add(blend);
      SelectedVoiceProfileId = null;
      VoiceWeight = 0.5;

      return Task.CompletedTask;
    }

    private Task RemoveTargetVoiceAsync(CancellationToken cancellationToken)
    {
      cancellationToken.ThrowIfCancellationRequested();

      if (SelectedTargetVoice == null)
      {
        return Task.CompletedTask;
      }

      TargetVoices.Remove(SelectedTargetVoice);
      SelectedTargetVoice = null;

      return Task.CompletedTask;
    }

    private async Task ApplyMorphAsync(CancellationToken cancellationToken)
    {
      if (SelectedConfig == null)
      {
        ErrorMessage = ResourceHelper.GetString("VoiceMorph.NoConfigSelected", "No config selected");
        return;
      }

      IsLoading = true;
      ErrorMessage = null;

      try
      {
        var response = await _voiceMorphClient.ApplyMorphAsync(SelectedConfig.ConfigId, cancellationToken).ConfigureAwait(false);

        if (response != null)
        {
          StatusMessage = ResourceHelper.FormatString("VoiceMorph.VoiceMorphingApplied", response.AudioId ?? "");
        }
      }
      catch (OperationCanceledException)
      {
        return; // User cancelled
      }
      catch (Exception ex)
      {
        await HandleErrorAsync(ex, "ApplyMorph");
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
      catch (Exception ex)
      {
        ErrorMessage = ResourceHelper.FormatString("VoiceMorph.LoadAudioFilesFailed", ex.Message);
      }
      finally
      {
        IsLoading = false;
      }
    }

    private async Task LoadVoiceProfilesAsync(CancellationToken cancellationToken)
    {
      IsLoading = true;
      ErrorMessage = null;

      try
      {
        var profiles = await _profilesClient.GetProfilesAsync(cancellationToken);

        AvailableVoiceProfiles.Clear();
        foreach (var profile in profiles)
        {
          if (!string.IsNullOrEmpty(profile.Id))
          {
            AvailableVoiceProfiles.Add(profile.Id);
          }
        }
      }
      catch (OperationCanceledException)
      {
        return; // User cancelled
      }
      catch (Exception ex)
      {
        await HandleErrorAsync(ex, "LoadVoiceProfiles");
      }
      finally
      {
        IsLoading = false;
      }
    }

    private async Task RefreshCoreAsync(CancellationToken cancellationToken)
    {
      await LoadConfigsAsync(cancellationToken);
      await LoadAudioFilesAsync(cancellationToken);
      await LoadVoiceProfilesAsync(cancellationToken);
      StatusMessage = ResourceHelper.GetString("VoiceMorph.Refreshed", "Refreshed");
    }

  }

  // Data models
  public class MorphConfigItem : ObservableObject
  {
    public string ConfigId { get; set; }
    public string Name { get; set; }
    public string SourceAudioId { get; set; }
    public ObservableCollection<VoiceBlendItem> TargetVoices { get; set; }
    public double MorphStrength { get; set; }
    public bool PreserveEmotion { get; set; }
    public bool PreserveProsody { get; set; }
    public string OutputFormat { get; set; }
    public string MorphStrengthDisplay => $"{MorphStrength:P0}";
    public string VoiceCountDisplay => $"{TargetVoices.Count} voice(s)";

    public MorphConfigItem(VoiceMorphConfig config)
    {
      ConfigId = config.ConfigId;
      Name = config.Name;
      SourceAudioId = config.SourceAudioId;
      TargetVoices = new ObservableCollection<VoiceBlendItem>(
          (config.TargetVoices ?? Array.Empty<VoiceMorphBlend>()).Select(v => new VoiceBlendItem(v))
      );
      MorphStrength = config.MorphStrength;
      PreserveEmotion = config.PreserveEmotion;
      PreserveProsody = config.PreserveProsody;
      OutputFormat = config.OutputFormat;
    }
  }

  public class VoiceBlendItem : ObservableObject
  {
    public string VoiceProfileId { get; set; }
    public double Weight { get; set; }
    public string WeightDisplay => $"{Weight:P0}";

    public VoiceBlendItem(VoiceMorphBlend blend)
    {
      VoiceProfileId = blend.VoiceProfileId;
      Weight = blend.Weight;
    }

    public VoiceBlendItem()
    {
      VoiceProfileId = string.Empty;
    }
  }
}