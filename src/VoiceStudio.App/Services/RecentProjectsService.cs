using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using VoiceStudio.App.Helpers;
using VoiceStudio.App.Logging;

namespace VoiceStudio.App.Services
{
  /// <summary>
  /// Service for managing recent projects history.
  /// Implements IDEA 16: Recent Projects Quick Access.
  /// </summary>
  public class RecentProjectsService : ObservableObject
  {
    private const string SettingsKey = "RecentProjects";
    private const int MaxRecentProjects = 10;
    private const int MaxPinnedProjects = 3;
    private readonly List<RecentProject> _recentProjects = new();
    private readonly List<RecentProject> _pinnedProjects = new();
    private readonly object _loadSync = new();
    private bool _recentDataLoaded;

    /// <summary>
    /// Gets the list of recent projects (excluding pinned).
    /// </summary>
    public IReadOnlyList<RecentProject> RecentProjects
    {
      get
      {
        EnsureRecentDataLoaded();
        return _recentProjects.AsReadOnly();
      }
    }

    /// <summary>
    /// Gets the list of pinned projects.
    /// </summary>
    public IReadOnlyList<RecentProject> PinnedProjects
    {
      get
      {
        EnsureRecentDataLoaded();
        return _pinnedProjects.AsReadOnly();
      }
    }

    /// <summary>
    /// Gets all projects (pinned first, then recent).
    /// </summary>
    public IReadOnlyList<RecentProject> AllProjects
    {
      get
      {
        EnsureRecentDataLoaded();
        var all = new List<RecentProject>(_pinnedProjects);
        all.AddRange(_recentProjects);
        return all.AsReadOnly();
      }
    }

    /// <summary>
    /// GAP-067 slice 7: loads persisted recents on first access (not in ctor) to keep shell ctor off the critical path.
    /// </summary>
    /// <summary>True after first successful load attempt (including empty file).</summary>
    public bool IsRecentDataLoaded
    {
      get
      {
        lock (_loadSync)
        {
          return _recentDataLoaded;
        }
      }
    }

    public void EnsureRecentDataLoaded()
    {
      lock (_loadSync)
      {
        if (_recentDataLoaded)
        {
          return;
        }

        LoadRecentProjectsCore();
        _recentDataLoaded = true;
        OnPropertyChanged(nameof(RecentProjects));
        OnPropertyChanged(nameof(PinnedProjects));
        OnPropertyChanged(nameof(AllProjects));
      }
    }

    public RecentProjectsService()
    {
    }

    /// <summary>
    /// Adds or updates a project in the recent projects list.
    /// </summary>
    public async Task AddRecentProjectAsync(string projectPath, string projectName)
    {
      if (string.IsNullOrEmpty(projectPath))
        return;

      EnsureRecentDataLoaded();

      // Remove if already exists
      var existing = _recentProjects.FirstOrDefault(p => p.Path == projectPath);
      if (existing != null)
      {
        _recentProjects.Remove(existing);
      }

      // Check if pinned
      var pinned = _pinnedProjects.FirstOrDefault(p => p.Path == projectPath);
      if (pinned != null)
      {
        // Update pinned project
        pinned.LastAccessed = DateTime.Now;
        pinned.Name = projectName;
        await SaveRecentProjectsAsync();
        OnPropertyChanged(nameof(PinnedProjects));
        OnPropertyChanged(nameof(AllProjects));
        return;
      }

      // Add new recent project
      var project = new RecentProject
      {
        Name = projectName,
        Path = projectPath,
        LastAccessed = DateTime.Now,
        IsPinned = false
      };

      _recentProjects.Insert(0, project);

      // Limit to max recent projects
      if (_recentProjects.Count > MaxRecentProjects)
      {
        _recentProjects.RemoveRange(MaxRecentProjects, _recentProjects.Count - MaxRecentProjects);
      }

      await SaveRecentProjectsAsync();
      OnPropertyChanged(nameof(RecentProjects));
      OnPropertyChanged(nameof(AllProjects));
    }

    /// <summary>
    /// Pins a project to the top of the list.
    /// </summary>
    public async Task PinProjectAsync(string projectPath)
    {
      EnsureRecentDataLoaded();

      if (_pinnedProjects.Count >= MaxPinnedProjects)
        throw new InvalidOperationException($"Cannot pin more than {MaxPinnedProjects} projects");

      // Remove from recent if exists
      var fromRecent = _recentProjects.FirstOrDefault(p => p.Path == projectPath);
      if (fromRecent != null)
      {
        _recentProjects.Remove(fromRecent);
      }

      // Check if already pinned
      if (_pinnedProjects.Any(p => p.Path == projectPath))
        return;

      // Add to pinned
      var project = fromRecent ?? new RecentProject
      {
        Name = System.IO.Path.GetFileNameWithoutExtension(projectPath),
        Path = projectPath,
        LastAccessed = DateTime.Now,
        IsPinned = true
      };
      project.IsPinned = true;

      _pinnedProjects.Add(project);
      await SaveRecentProjectsAsync();
      OnPropertyChanged(nameof(PinnedProjects));
      OnPropertyChanged(nameof(RecentProjects));
      OnPropertyChanged(nameof(AllProjects));
    }

    /// <summary>
    /// Unpins a project.
    /// </summary>
    public async Task UnpinProjectAsync(string projectPath)
    {
      EnsureRecentDataLoaded();

      var pinned = _pinnedProjects.FirstOrDefault(p => p.Path == projectPath);
      if (pinned == null)
        return;

      _pinnedProjects.Remove(pinned);
      pinned.IsPinned = false;
      pinned.LastAccessed = DateTime.Now;

      // Add to recent (at the top)
      _recentProjects.Insert(0, pinned);

      // Limit recent projects
      if (_recentProjects.Count > MaxRecentProjects)
      {
        _recentProjects.RemoveRange(MaxRecentProjects, _recentProjects.Count - MaxRecentProjects);
      }

      await SaveRecentProjectsAsync();
      OnPropertyChanged(nameof(PinnedProjects));
      OnPropertyChanged(nameof(RecentProjects));
      OnPropertyChanged(nameof(AllProjects));
    }

    /// <summary>
    /// Removes a project from recent projects.
    /// </summary>
    public async Task RemoveRecentProjectAsync(string projectPath)
    {
      EnsureRecentDataLoaded();

      var project = _recentProjects.FirstOrDefault(p => p.Path == projectPath);
      if (project != null)
      {
        _recentProjects.Remove(project);
        await SaveRecentProjectsAsync();
        OnPropertyChanged(nameof(RecentProjects));
        OnPropertyChanged(nameof(AllProjects));
      }
    }

    /// <summary>
    /// Clears all recent projects (keeps pinned).
    /// </summary>
    public async Task ClearRecentProjectsAsync()
    {
      EnsureRecentDataLoaded();

      _recentProjects.Clear();
      await SaveRecentProjectsAsync();
      OnPropertyChanged(nameof(RecentProjects));
      OnPropertyChanged(nameof(AllProjects));
    }

    /// <summary>
    /// Loads recent projects from local settings.
    /// </summary>
    private void LoadRecentProjectsCore()
    {
      try
      {
        // Use UnpackagedSettingsHelper for file-based settings (works for both packaged and unpackaged apps)
        var json = UnpackagedSettingsHelper.GetValue<string>(SettingsKey, null);
        if (!string.IsNullOrEmpty(json))
        {
          var projects = JsonSerializer.Deserialize<List<RecentProject>>(json);
          if (projects != null)
          {
            _pinnedProjects.Clear();
            _recentProjects.Clear();

            foreach (var project in projects)
            {
              if (project.IsPinned)
              {
                _pinnedProjects.Add(project);
              }
              else
              {
                _recentProjects.Add(project);
              }
            }

            // Sort by last accessed
            _pinnedProjects.Sort((a, b) => b.LastAccessed.CompareTo(a.LastAccessed));
            _recentProjects.Sort((a, b) => b.LastAccessed.CompareTo(a.LastAccessed));
          }
        }
      }
      catch (Exception ex)
      {
        ErrorLogger.LogWarning($"Recent projects load failed: {ex.Message}", "RecentProjectsService.LoadRecentProjectsCore");
        _recentProjects.Clear();
        _pinnedProjects.Clear();
      }
    }

    /// <summary>
    /// Saves recent projects to local settings.
    /// </summary>
    private Task SaveRecentProjectsAsync()
    {
      try
      {
        var allProjects = new List<RecentProject>(_pinnedProjects);
        allProjects.AddRange(_recentProjects);

        var json = JsonSerializer.Serialize(allProjects);
        // Use UnpackagedSettingsHelper for file-based settings (works for both packaged and unpackaged apps)
        UnpackagedSettingsHelper.SetValue(SettingsKey, json);
      }
      catch (Exception ex)
      {
        ErrorLogger.LogWarning($"Best effort operation failed: {ex.Message}", "RecentProjectsService.SaveRecentProjectsAsync");
      }

      return Task.CompletedTask;
    }
  }

  /// <summary>
  /// Represents a recent project entry.
  /// </summary>
  public class RecentProject
  {
    public string Name { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;
    public DateTime LastAccessed { get; set; }
    public bool IsPinned { get; set; }
  }
}