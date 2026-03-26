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
  /// ViewModel for the StyleTransferView panel - Voice style transfer.
  /// </summary>
  public partial class StyleTransferViewModel : BaseViewModel, IPanelView, IPanelLifecycle
  {
    private readonly IStyleTransferClient _styleTransferClient;
    private readonly IProjectAudioClient _projectAudioClient;
    private readonly IProjectsClient _projectsClient;
    private readonly IProfilesClient _profilesClient;

    public string PanelId => "style-transfer";
    public string DisplayName => ResourceHelper.GetString("Panel.StyleTransfer.DisplayName", "Voice Style Transfer");
    public PanelRegion Region => PanelRegion.Center;

    [ObservableProperty]
    private string? sourceAudioId;

    [ObservableProperty]
    private ObservableCollection<string> availableAudioIds = new();

    [ObservableProperty]
    private string? targetStyleId;

    [ObservableProperty]
    private ObservableCollection<string> availableVoiceProfiles = new();

    [ObservableProperty]
    private ObservableCollection<StyleTransferPresetItem> stylePresets = new();

    [ObservableProperty]
    private StyleTransferPresetItem? selectedPreset;

    [ObservableProperty]
    private double transferStrength = 0.8;

    [ObservableProperty]
    private bool preserveContent = true;

    [ObservableProperty]
    private bool preserveEmotion;

    [ObservableProperty]
    private ObservableCollection<StyleTransferJobItem> jobs = new();

    [ObservableProperty]
    private StyleTransferJobItem? selectedJob;

    public StyleTransferViewModel(IViewModelContext context, IStyleTransferClient styleTransferClient, IProjectAudioClient projectAudioClient, IProjectsClient projectsClient, IProfilesClient profilesClient)
        : base(context)
    {
      _styleTransferClient = styleTransferClient ?? throw new ArgumentNullException(nameof(styleTransferClient));
      _projectAudioClient = projectAudioClient ?? throw new ArgumentNullException(nameof(projectAudioClient));
      _projectsClient = projectsClient ?? throw new ArgumentNullException(nameof(projectsClient));
      _profilesClient = profilesClient ?? throw new ArgumentNullException(nameof(profilesClient));

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
      LoadPresetsCommand = new EnhancedAsyncRelayCommand(async (ct) =>
      {
        using var profiler = PerformanceProfiler.StartCommand("LoadPresets");
        await LoadPresetsCommandAsync(ct);
      }, () => !IsLoading);
      CreateTransferCommand = new EnhancedAsyncRelayCommand(async (ct) =>
      {
        using var profiler = PerformanceProfiler.StartCommand("CreateTransfer");
        await CreateTransferAsync(ct);
      }, () => !string.IsNullOrEmpty(SourceAudioId) && !string.IsNullOrEmpty(TargetStyleId) && !IsLoading);
      LoadJobsCommand = new EnhancedAsyncRelayCommand(async (ct) =>
      {
        using var profiler = PerformanceProfiler.StartCommand("LoadJobs");
        await LoadJobsAsync(ct);
      }, () => !IsLoading);
      DeleteJobCommand = new EnhancedAsyncRelayCommand(async (ct) =>
      {
        using var profiler = PerformanceProfiler.StartCommand("DeleteJob");
        await DeleteJobAsync(ct);
      }, () => SelectedJob != null && !IsLoading);
      RefreshCommand = new EnhancedAsyncRelayCommand(async (ct) =>
      {
        using var profiler = PerformanceProfiler.StartCommand("Refresh");
        await RefreshAsync(ct);
      }, () => !IsLoading);
    }

    /// <inheritdoc />
    public Task OnActivatedAsync(CancellationToken cancellationToken = default)
    {
      return RefreshAsync(cancellationToken);
    }

    /// <inheritdoc />
    public Task OnDeactivatedAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    public IAsyncRelayCommand LoadAudioFilesCommand { get; }
    public IAsyncRelayCommand LoadVoiceProfilesCommand { get; }
    public IAsyncRelayCommand LoadPresetsCommand { get; }
    public IAsyncRelayCommand CreateTransferCommand { get; }
    public IAsyncRelayCommand LoadJobsCommand { get; }
    public IAsyncRelayCommand DeleteJobCommand { get; }
    public IAsyncRelayCommand RefreshCommand { get; }

    partial void OnSelectedPresetChanged(StyleTransferPresetItem? value)
    {
      if (value != null)
      {
        TargetStyleId = value.VoiceProfileId;
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

    private async Task LoadPresetsCommandAsync(CancellationToken cancellationToken)
    {
      IsLoading = true;
      ErrorMessage = null;

      try
      {
        var presets = await _styleTransferClient.GetPresetsAsync(cancellationToken);

        if (presets != null)
        {
          StylePresets.Clear();
          foreach (var preset in presets)
          {
            StylePresets.Add(new StyleTransferPresetItem(preset));
          }
        }
      }
      catch (OperationCanceledException)
      {
        return; // User cancelled
      }
      catch (Exception ex)
      {
        await HandleErrorAsync(ex, "LoadPresets");
      }
      finally
      {
        IsLoading = false;
      }
    }

    private async Task CreateTransferAsync(CancellationToken cancellationToken)
    {
      if (string.IsNullOrEmpty(SourceAudioId))
      {
        ErrorMessage = ResourceHelper.GetString("StyleTransfer.SourceAudioRequired", "Source audio must be selected");
        return;
      }

      if (string.IsNullOrEmpty(TargetStyleId))
      {
        ErrorMessage = ResourceHelper.GetString("StyleTransfer.TargetStyleRequired", "Target style must be selected");
        return;
      }

      IsLoading = true;
      ErrorMessage = null;

      try
      {
        var request = new StyleTransferCreateRequest
        {
          SourceAudioId = SourceAudioId!,
          TargetStyleId = TargetStyleId!,
          TransferStrength = TransferStrength,
          PreserveContent = PreserveContent,
          PreserveEmotion = PreserveEmotion,
          OutputFormat = "wav"
        };

        var job = await _styleTransferClient.CreateTransferAsync(request, cancellationToken);

        if (job != null)
        {
          var jobItem = new StyleTransferJobItem(job);
          Jobs.Insert(0, jobItem);
          SelectedJob = jobItem;
          StatusMessage = ResourceHelper.FormatString("StyleTransfer.StyleTransferCreated", job.JobId);
        }
      }
      catch (OperationCanceledException)
      {
        return; // User cancelled
      }
      catch (Exception ex)
      {
        await HandleErrorAsync(ex, "CreateTransfer");
      }
      finally
      {
        IsLoading = false;
      }
    }

    private async Task LoadJobsAsync(CancellationToken cancellationToken)
    {
      IsLoading = true;
      ErrorMessage = null;

      try
      {
        var jobs = await _styleTransferClient.GetJobsAsync(cancellationToken);

        if (jobs != null)
        {
          Jobs.Clear();
          foreach (var job in jobs.OrderByDescending(j => j.Created))
          {
            Jobs.Add(new StyleTransferJobItem(job));
          }
        }
      }
      catch (OperationCanceledException)
      {
        return; // User cancelled
      }
      catch (Exception ex)
      {
        await HandleErrorAsync(ex, "LoadJobs");
      }
      finally
      {
        IsLoading = false;
      }
    }

    private async Task DeleteJobAsync(CancellationToken cancellationToken)
    {
      if (SelectedJob == null)
      {
        ErrorMessage = ResourceHelper.GetString("StyleTransfer.NoJobSelected", "No job selected");
        return;
      }

      IsLoading = true;
      ErrorMessage = null;

      try
      {
        await _styleTransferClient.DeleteJobAsync(SelectedJob.JobId, cancellationToken);

        Jobs.Remove(SelectedJob);
        SelectedJob = null;
        StatusMessage = ResourceHelper.GetString("StyleTransfer.JobDeleted", "Job deleted");
      }
      catch (OperationCanceledException)
      {
        return; // User cancelled
      }
      catch (Exception ex)
      {
        await HandleErrorAsync(ex, "DeleteJob");
      }
      finally
      {
        IsLoading = false;
      }
    }

    public async Task RefreshAsync(CancellationToken cancellationToken)
    {
      await LoadAudioFilesAsync(cancellationToken);
      await LoadVoiceProfilesAsync(cancellationToken);
      await LoadPresetsCommandAsync(cancellationToken);
      await LoadJobsAsync(cancellationToken);
      StatusMessage = ResourceHelper.GetString("StyleTransfer.Refreshed", "Refreshed");
    }
  }

  // UI item models
  public class StyleTransferJobItem : ObservableObject
  {
    public string JobId { get; set; }
    public string SourceAudioId { get; set; }
    public string TargetStyleId { get; set; }
    public double TransferStrength { get; set; }
    public string Status { get; set; }
    public double Progress { get; set; }
    public string? OutputAudioId { get; set; }
    public string? ErrorMessage { get; set; }
    public string Created { get; set; }
    public string? Completed { get; set; }
    public string ProgressDisplay => $"{Progress:P0}";
    public string StatusDisplay => Status.ToUpper();

    public StyleTransferJobItem(StyleTransferJobResponse job)
    {
      JobId = job.JobId;
      SourceAudioId = job.SourceAudioId;
      TargetStyleId = job.TargetStyleId;
      TransferStrength = job.TransferStrength;
      Status = job.Status;
      Progress = job.Progress;
      OutputAudioId = job.OutputAudioId;
      ErrorMessage = job.ErrorMessage;
      Created = job.Created;
      Completed = job.Completed;
    }
  }

  public class StyleTransferPresetItem : ObservableObject
  {
    public string PresetId { get; set; }
    public string Name { get; set; }
    public string? Description { get; set; }
    public string? VoiceProfileId { get; set; }
    public Dictionary<string, object> StyleCharacteristics { get; set; }

    public StyleTransferPresetItem(StyleTransferPresetResponse preset)
    {
      PresetId = preset.PresetId;
      Name = preset.Name;
      Description = preset.Description;
      VoiceProfileId = preset.VoiceProfileId;
      StyleCharacteristics = preset.StyleCharacteristics;
    }
  }
}