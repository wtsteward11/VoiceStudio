using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using VoiceStudio.App.Logging;
// using VoiceStudio.App.Services.Persistence;  // Commented - namespace missing
using VoiceStudio.Core.Models;
using VoiceStudio.Core.Services;

namespace VoiceStudio.App.Services.Stores
{
  /// <summary>
  /// Centralized store for project-related state.
  /// Implements React/TypeScript projectStore pattern in C#.
  /// </summary>
  public partial class ProjectStore : ObservableObject
  {
    private readonly IBackendClient _backendClient;
    private readonly IProjectsClient _projectsClient;
    private readonly StateCacheService? _stateCacheService;
    private readonly IProjectRepository? _projectRepository;

    [ObservableProperty]
    private ObservableCollection<Project> projects = new();

    [ObservableProperty]
    private Project? currentProject;

    [ObservableProperty]
    private Project? selectedProject;

    [ObservableProperty]
    private bool isLoading;

    [ObservableProperty]
    private string? errorMessage;

    [ObservableProperty]
    private DateTime? lastUpdated;

    /// <summary>
    /// Indicates whether the current project has unsaved changes.
    /// </summary>
    [ObservableProperty]
    private bool isDirty;

    /// <summary>
    /// Timestamp of the last successful save operation.
    /// </summary>
    [ObservableProperty]
    private DateTime? lastSaved;

    public ProjectStore(
        IBackendClient backendClient,
        IProjectsClient projectsClient,
        StateCacheService? stateCacheService = null,
        IProjectRepository? projectRepository = null)
    {
      _backendClient = backendClient ?? throw new ArgumentNullException(nameof(backendClient));
      _projectsClient = projectsClient ?? throw new ArgumentNullException(nameof(projectsClient));
      _stateCacheService = stateCacheService;
      _projectRepository = projectRepository;
    }

    /// <summary>
    /// Loads all projects.
    /// </summary>
    public async Task LoadProjectsAsync()
    {
      try
      {
        IsLoading = true;
        ErrorMessage = null;

        // Try to load from cache first
        if (_stateCacheService != null)
        {
          var cached = await _stateCacheService.GetCachedStateAsync<ObservableCollection<Project>>("projects");
          if (cached != null)
          {
            Projects = cached;
            IsLoading = false;
            // Still fetch from backend in background to update
            _ = RefreshProjectsAsync();
            return;
          }
        }

        await RefreshProjectsAsync();
      }
      catch (Exception ex)
      {
        ErrorMessage = $"Failed to load projects: {ex.Message}";
      }
      finally
      {
        IsLoading = false;
      }
    }

    /// <summary>
    /// Refreshes projects from backend.
    /// </summary>
    public async Task RefreshProjectsAsync()
    {
      try
      {
        if (_projectRepository != null)
        {
          var localProjects = await _projectRepository.ListProjectsAsync();
          Projects = new ObservableCollection<Project>(
              localProjects.Select(MapMetadataToProject));

          LastUpdated = DateTime.UtcNow;

          if (_stateCacheService != null)
          {
            await _stateCacheService.CacheStateAsync("projects", Projects);
          }

          if (Projects.Count > 0)
          {
            return;
          }
        }

        var projectsArray = await _projectsClient.GetProjectsAsync();

        Projects.Clear();
        if (projectsArray != null)
        {
          foreach (var project in projectsArray)
          {
            Projects.Add(project);
          }
        }

        LastUpdated = DateTime.UtcNow;

        // Cache the result
        if (_stateCacheService != null)
        {
          await _stateCacheService.CacheStateAsync("projects", Projects);
        }
      }
      catch (Exception ex)
      {
        ErrorMessage = $"Failed to refresh projects: {ex.Message}";
      }
    }

    /// <summary>
    /// Loads a specific project and sets it as current.
    /// </summary>
    public async Task LoadProjectAsync(string projectId)
    {
      try
      {
        IsLoading = true;
        ErrorMessage = null;

        Project? project = null;
        if (_projectRepository != null)
        {
          try
          {
            var localProjects = await _projectRepository.ListProjectsAsync();
            var localMatch = localProjects.FirstOrDefault(p => p.ProjectId == projectId);
            if (localMatch != null)
            {
              // OpenAsync returns Project directly
              project = await _projectRepository.OpenAsync(localMatch.ProjectId);
            }
          }
          catch (Exception ex)
      {
        ErrorLogger.LogWarning($"Best effort operation failed: {ex.Message}", "ProjectStore.LoadProjectAsync");
      }
        }

        project ??= await _backendClient.GetProjectAsync(projectId);
        if (project != null)
        {
          CurrentProject = project;

          // Update in projects list if it exists
          var existing = Projects.FirstOrDefault(p => p.Id == projectId);
          if (existing != null)
          {
            var index = Projects.IndexOf(existing);
            Projects[index] = project;
          }
          else
          {
            Projects.Add(project);
          }

          LastUpdated = DateTime.UtcNow;
        }
      }
      catch (Exception ex)
      {
        ErrorMessage = $"Failed to load project: {ex.Message}";
      }
      finally
      {
        IsLoading = false;
      }
    }

    /// <summary>
    /// Adds a project to the store.
    /// </summary>
    public void AddProject(Project project)
    {
      if (!Projects.Any(p => p.Id == project.Id))
      {
        Projects.Add(project);
        LastUpdated = DateTime.UtcNow;
      }
    }

    /// <summary>
    /// Removes a project from the store.
    /// </summary>
    public void RemoveProject(string projectId)
    {
      var project = Projects.FirstOrDefault(p => p.Id == projectId);
      if (project != null)
      {
        Projects.Remove(project);

        // Clear current project if it was removed
        if (CurrentProject?.Id == projectId)
        {
          CurrentProject = null;
        }

        LastUpdated = DateTime.UtcNow;
      }
    }

    /// <summary>
    /// Updates a project in the store.
    /// </summary>
    public void UpdateProject(Project project)
    {
      var existing = Projects.FirstOrDefault(p => p.Id == project.Id);
      if (existing != null)
      {
        var index = Projects.IndexOf(existing);
        Projects[index] = project;

        // Update current project if it's the same
        if (CurrentProject?.Id == project.Id)
        {
          CurrentProject = project;
        }

        LastUpdated = DateTime.UtcNow;
      }
    }

    /// <summary>
    /// Sets the current project.
    /// </summary>
    public void SetCurrentProject(string projectId)
    {
      CurrentProject = Projects.FirstOrDefault(p => p.Id == projectId);
    }

    /// <summary>
    /// Clears all project state.
    /// </summary>
    public void Clear()
    {
      Projects.Clear();
      CurrentProject = null;
      SelectedProject = null;
      LastUpdated = null;
      IsDirty = false;
      LastSaved = null;
    }

    /// <summary>
    /// Marks the current project as having unsaved changes.
    /// </summary>
    public void MarkDirty()
    {
      IsDirty = true;
    }

    /// <summary>
    /// Records a successful save operation, resetting dirty state.
    /// </summary>
    public void RecordSave()
    {
      IsDirty = false;
      LastSaved = DateTime.UtcNow;
    }

    private static Project MapMetadataToProject(ProjectMetadata metadata)
    {
      return new Project
      {
        Id = metadata.ProjectId,
        Name = metadata.Name,
        Description = string.Empty,
        CreatedAt = metadata.CreatedAt.ToUniversalTime().ToString("o"),
        UpdatedAt = metadata.ModifiedAt.ToUniversalTime().ToString("o")
      };
    }

    private static Project? MapDataToProject(ProjectData? data)
    {
      if (data == null) return null;
      return new Project
      {
        Id = data.ProjectId,
        Name = data.Name,
        Description = string.Empty,
        CreatedAt = data.CreatedAt.ToUniversalTime().ToString("o"),
        UpdatedAt = data.ModifiedAt.ToUniversalTime().ToString("o")
      };
    }
  }
}