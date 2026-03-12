using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VoiceStudio.Core.Panels;
using VoiceStudio.Core.Services;
using VoiceStudio.App.Services;
using VoiceStudio.App.Services.UndoableActions;
using VoiceStudio.App.Utilities;
using VoiceStudio.App.Logging;

namespace VoiceStudio.App.ViewModels
{
  /// <summary>
  /// ViewModel for the SceneBuilderView panel - Scene composition editor.
  /// </summary>
  public partial class SceneBuilderViewModel : BaseViewModel, IPanelView
  {
    private readonly IBackendClient _backendClient;
    private readonly UndoRedoService? _undoRedoService;
    private CancellationTokenSource? _searchDebounceCts;
    private const int SearchDebounceMs = 300;

    public string PanelId => "scene-builder";
    public string DisplayName => ResourceHelper.GetString("Panel.SceneBuilder.DisplayName", "Scene Builder");
    public PanelRegion Region => PanelRegion.Center;

    [ObservableProperty]
    private ObservableCollection<SceneItem> scenes = new();

    [ObservableProperty]
    private SceneItem? selectedScene;

    [ObservableProperty]
    private string? selectedProjectId;

    [ObservableProperty]
    private string searchQuery = string.Empty;

    [ObservableProperty]
    private ObservableCollection<string> availableProjects = new();

    public SceneBuilderViewModel(IViewModelContext context, IBackendClient backendClient)
        : base(context)
    {
      _backendClient = backendClient ?? throw new ArgumentNullException(nameof(backendClient));

      // Get undo/redo service (may be null if not initialized)
      try
      {
        _undoRedoService = AppServices.TryGetUndoRedoService();
      }
      catch
      {
        // Service may not be initialized yet - that's okay
        _undoRedoService = null;
      }

      LoadScenesCommand = new EnhancedAsyncRelayCommand(async (ct) =>
      {
        using var profiler = PerformanceProfiler.StartCommand("LoadScenes");
        await LoadScenesAsync(ct);
      }, () => !IsLoading);
      CreateSceneCommand = new EnhancedAsyncRelayCommand(async (ct) =>
      {
        using var profiler = PerformanceProfiler.StartCommand("CreateScene");
        await CreateSceneAsync(ct);
      }, () => !string.IsNullOrEmpty(SelectedProjectId) && !IsLoading);
      UpdateSceneCommand = new EnhancedAsyncRelayCommand<SceneItem>(async (scene, ct) =>
      {
        using var profiler = PerformanceProfiler.StartCommand("UpdateScene");
        await UpdateSceneAsync(scene, ct);
      }, (scene) => scene != null && !IsLoading);
      DeleteSceneCommand = new EnhancedAsyncRelayCommand<SceneItem>(async (scene, ct) =>
      {
        using var profiler = PerformanceProfiler.StartCommand("DeleteScene");
        await DeleteSceneAsync(scene, ct);
      }, (scene) => scene != null && !IsLoading);
      ApplySceneCommand = new EnhancedAsyncRelayCommand<SceneItem>(async (scene, ct) =>
      {
        using var profiler = PerformanceProfiler.StartCommand("ApplyScene");
        await ApplySceneAsync(scene, ct);
      }, (scene) => scene != null && !string.IsNullOrEmpty(SelectedProjectId) && !IsLoading);
      RefreshCommand = new EnhancedAsyncRelayCommand(async (ct) =>
      {
        using var profiler = PerformanceProfiler.StartCommand("Refresh");
        await RefreshAsync(ct);
      }, () => !IsLoading);

      // Load initial data
      _ = LoadScenesAsync(CancellationToken.None);
    }

    public IAsyncRelayCommand LoadScenesCommand { get; }
    public IAsyncRelayCommand CreateSceneCommand { get; }
    public IAsyncRelayCommand<SceneItem> UpdateSceneCommand { get; }
    public IAsyncRelayCommand<SceneItem> DeleteSceneCommand { get; }
    public IAsyncRelayCommand<SceneItem> ApplySceneCommand { get; }
    public IAsyncRelayCommand RefreshCommand { get; }

    private async Task LoadScenesAsync(CancellationToken cancellationToken)
    {
      IsLoading = true;
      ErrorMessage = null;

      try
      {
        var queryParams = new System.Collections.Specialized.NameValueCollection();
        if (!string.IsNullOrEmpty(SelectedProjectId))
          queryParams.Add("project_id", SelectedProjectId);
        if (!string.IsNullOrEmpty(SearchQuery))
          queryParams.Add("search", SearchQuery);

        var queryString = string.Join("&",
            queryParams.AllKeys.SelectMany(key =>
                queryParams.GetValues(key)?.Select(value => $"{key}={Uri.EscapeDataString(value)}") ?? Array.Empty<string>()
            )
        );

        var url = "/api/scenes";
        if (!string.IsNullOrEmpty(queryString))
          url += $"?{queryString}";

        var scenes = await _backendClient.SendRequestAsync<object, Scene[]>(
            url,
            null,
            System.Net.Http.HttpMethod.Get,
            cancellationToken
        );

        Scenes.Clear();
        if (scenes != null)
        {
          foreach (var scene in scenes)
          {
            Scenes.Add(new SceneItem(scene));
          }
        }
      }
      catch (OperationCanceledException)
      {
        return; // User cancelled
      }
      catch (Exception ex)
      {
        await HandleErrorAsync(ex, "LoadScenes");
      }
      finally
      {
        IsLoading = false;
      }
    }

    private async Task CreateSceneAsync(CancellationToken cancellationToken)
    {
      if (string.IsNullOrEmpty(SelectedProjectId))
      {
        ErrorMessage = ResourceHelper.GetString("SceneBuilder.ProjectRequired", "Project must be selected");
        return;
      }

      IsLoading = true;
      ErrorMessage = null;

      try
      {
        var request = new
        {
          name = ResourceHelper.GetString("SceneBuilder.NewSceneName", "New Scene"),
          description = "",
          project_id = SelectedProjectId,
          tags = Array.Empty<string>()
        };

        var created = await _backendClient.SendRequestAsync<object, Scene>(
            "/api/scenes",
            request,
            System.Net.Http.HttpMethod.Post,
            cancellationToken
        );

        if (created != null)
        {
          var sceneItem = new SceneItem(created);
          Scenes.Add(sceneItem);
          SelectedScene = sceneItem;
          StatusMessage = ResourceHelper.GetString("SceneBuilder.SceneCreated", "Scene created");

          // Register undo action
          if (_undoRedoService != null)
          {
            var action = new CreateSceneAction(
                Scenes,
                _backendClient,
                sceneItem,
                onUndo: (s) =>
                {
                  if (SelectedScene == s)
                  {
                    SelectedScene = Scenes.FirstOrDefault();
                  }
                },
                onRedo: (s) => SelectedScene = s);
            _undoRedoService.RegisterAction(action);
          }
        }
      }
      catch (OperationCanceledException)
      {
        return; // User cancelled
      }
      catch (Exception ex)
      {
        await HandleErrorAsync(ex, "CreateScene");
      }
      finally
      {
        IsLoading = false;
      }
    }

    private async Task UpdateSceneAsync(SceneItem? scene, CancellationToken cancellationToken)
    {
      if (scene == null)
        return;

      IsLoading = true;
      ErrorMessage = null;

      try
      {
        var request = new
        {
          name = scene.Name,
          description = scene.Description,
          tracks = scene.Tracks.Select(t => new
          {
            id = t.Id,
            name = t.Name,
            track_number = t.TrackNumber,
            clips = t.Clips,
            effects = t.Effects,
            automation = t.Automation
          }).ToArray(),
          master_effects = scene.MasterEffects,
          duration = scene.Duration,
          tags = scene.Tags
        };

        var updated = await _backendClient.SendRequestAsync<object, Scene>(
            $"/api/scenes/{scene.Id}",
            request,
            System.Net.Http.HttpMethod.Put,
            cancellationToken
        );

        if (updated != null)
        {
          scene.UpdateFrom(updated);
        }

        await LoadScenesAsync(cancellationToken);
        StatusMessage = ResourceHelper.GetString("SceneBuilder.SceneUpdated", "Scene updated");
      }
      catch (OperationCanceledException)
      {
        return; // User cancelled
      }
      catch (Exception ex)
      {
        await HandleErrorAsync(ex, "UpdateScene");
      }
      finally
      {
        IsLoading = false;
      }
    }

    private async Task DeleteSceneAsync(SceneItem? scene, CancellationToken cancellationToken)
    {
      if (scene == null)
        return;

      IsLoading = true;
      ErrorMessage = null;

      try
      {
        await _backendClient.SendRequestAsync<object, object>(
            $"/api/scenes/{scene.Id}",
            null,
            System.Net.Http.HttpMethod.Delete,
            cancellationToken
        );

        var originalIndex = Scenes.IndexOf(scene);
        Scenes.Remove(scene);
        var previousSelected = SelectedScene;
        if (SelectedScene == scene)
        {
          SelectedScene = null;
        }
        StatusMessage = ResourceHelper.GetString("SceneBuilder.SceneDeleted", "Scene deleted");

        // Register undo action
        if (_undoRedoService != null)
        {
          var action = new DeleteSceneAction(
              Scenes,
              _backendClient,
              scene,
              originalIndex,
              onUndo: (s) => SelectedScene = s,
              onRedo: (s) =>
              {
                if (SelectedScene == s)
                {
                  SelectedScene = Scenes.FirstOrDefault();
                }
              });
          _undoRedoService.RegisterAction(action);
        }
      }
      catch (OperationCanceledException)
      {
        return; // User cancelled
      }
      catch (Exception ex)
      {
        await HandleErrorAsync(ex, "DeleteScene");
      }
      finally
      {
        IsLoading = false;
      }
    }

    private async Task ApplySceneAsync(SceneItem? scene, CancellationToken cancellationToken = default)
    {
      if (scene == null || string.IsNullOrEmpty(SelectedProjectId))
      {
        ErrorMessage = ResourceHelper.GetString("SceneBuilder.SceneAndProjectRequired", "Scene and project must be selected");
        return;
      }

      try
      {
        IsLoading = true;
        ErrorMessage = null;

        var response = await _backendClient.SendRequestAsync<object, SceneApplyResponse>(
            $"/api/scenes/{scene.Id}/apply?target_project_id={Uri.EscapeDataString(SelectedProjectId)}",
            null,
            System.Net.Http.HttpMethod.Post,
            cancellationToken
        );

        StatusMessage = response?.Message ?? ResourceHelper.GetString("SceneBuilder.SceneApplied", "Scene applied");
      }
      catch (Exception ex)
      {
        ErrorMessage = ResourceHelper.FormatString("SceneBuilder.ApplySceneFailed", ex.Message);
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
        await LoadScenesAsync(cancellationToken);
        StatusMessage = ResourceHelper.GetString("SceneBuilder.ScenesRefreshed", "Scenes refreshed");
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

    partial void OnSelectedProjectIdChanged(string? value)
    {
      _ = LoadScenesAsync(CancellationToken.None);
    }

    partial void OnSearchQueryChanged(string value)
    {
      _searchDebounceCts?.Cancel();
      _searchDebounceCts = new CancellationTokenSource();
      var cts = _searchDebounceCts;
      _ = Task.Run(async () =>
      {
        try
        {
          await Task.Delay(SearchDebounceMs, cts.Token);
          Dispatcher.TryEnqueue(() => _ = LoadScenesAsync(cts.Token));
        }
        catch (Exception ex)
      {
        ErrorLogger.LogWarning($"Best effort operation failed: {ex.Message}", "SceneBuilderViewModel.OnSearchQueryChanged");
      }
      });
    }

    // Response models
    private class SceneApplyResponse
    {
      public bool Success { get; set; }
      public string Message { get; set; } = string.Empty;
    }
  }

  // Data models
  public class Scene
  {
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string ProjectId { get; set; } = string.Empty;
    public System.Collections.Generic.List<SceneTrack> Tracks { get; set; } = new();
    public System.Collections.Generic.List<System.Collections.Generic.Dictionary<string, object>> MasterEffects { get; set; } = new();
    public double Duration { get; set; }
    public string Created { get; set; } = string.Empty;
    public string Modified { get; set; } = string.Empty;
    public System.Collections.Generic.List<string> Tags { get; set; } = new();
  }

  public class SceneTrack
  {
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public int TrackNumber { get; set; }
    public System.Collections.Generic.List<System.Collections.Generic.Dictionary<string, object>> Clips { get; set; } = new();
    public System.Collections.Generic.List<System.Collections.Generic.Dictionary<string, object>> Effects { get; set; } = new();
    public System.Collections.Generic.List<System.Collections.Generic.Dictionary<string, object>> Automation { get; set; } = new();
  }

  public class SceneItem : ObservableObject
  {
    public string Id { get; set; }
    public string Name { get; set; }
    public string? Description { get; set; }
    public string ProjectId { get; set; }
    public System.Collections.Generic.List<SceneTrackItem> Tracks { get; set; }
    public System.Collections.Generic.List<System.Collections.Generic.Dictionary<string, object>> MasterEffects { get; set; }
    public double Duration { get; set; }
    public string Created { get; set; }
    public string Modified { get; set; }
    public System.Collections.Generic.List<string> Tags { get; set; }
    public int TrackCount => Tracks?.Count ?? 0;
    public int EffectCount => MasterEffects?.Count ?? 0;

    public SceneItem(Scene scene)
    {
      Id = scene.Id;
      Name = scene.Name;
      Description = scene.Description;
      ProjectId = scene.ProjectId;
      Tracks = scene.Tracks.Select(t => new SceneTrackItem(t)).ToList();
      MasterEffects = scene.MasterEffects;
      Duration = scene.Duration;
      Created = scene.Created;
      Modified = scene.Modified;
      Tags = scene.Tags;
    }

    public void UpdateFrom(Scene scene)
    {
      Name = scene.Name;
      Description = scene.Description;
      Tracks = scene.Tracks.Select(t => new SceneTrackItem(t)).ToList();
      MasterEffects = scene.MasterEffects;
      Duration = scene.Duration;
      Modified = scene.Modified;
      Tags = scene.Tags;
      OnPropertyChanged(nameof(Name));
      OnPropertyChanged(nameof(Description));
      OnPropertyChanged(nameof(Tracks));
      OnPropertyChanged(nameof(TrackCount));
      OnPropertyChanged(nameof(MasterEffects));
      OnPropertyChanged(nameof(EffectCount));
      OnPropertyChanged(nameof(Duration));
      OnPropertyChanged(nameof(Modified));
      OnPropertyChanged(nameof(Tags));
    }
  }

  public class SceneTrackItem : ObservableObject
  {
    public string Id { get; set; }
    public string Name { get; set; }
    public int TrackNumber { get; set; }
    public System.Collections.Generic.List<System.Collections.Generic.Dictionary<string, object>> Clips { get; set; }
    public System.Collections.Generic.List<System.Collections.Generic.Dictionary<string, object>> Effects { get; set; }
    public System.Collections.Generic.List<System.Collections.Generic.Dictionary<string, object>> Automation { get; set; }
    public int ClipCount => Clips?.Count ?? 0;

    public SceneTrackItem(SceneTrack track)
    {
      Id = track.Id;
      Name = track.Name;
      TrackNumber = track.TrackNumber;
      Clips = track.Clips;
      Effects = track.Effects;
      Automation = track.Automation;
    }
  }
}