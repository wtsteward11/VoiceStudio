using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using VoiceStudio.Core.Models;
using VoiceStudio.Core.Services;
using VoiceStudio.App.Logging;

namespace VoiceStudio.App.Services
{
  /// <summary>
  /// JSON file-based implementation of IProjectRepository.
  /// Stores projects as individual JSON files in a projects directory.
  /// </summary>
  /// <remarks>
  /// Local-first design: all data stored on disk, no cloud dependencies.
  /// Thread-safe via SemaphoreSlim for concurrent access.
  /// </remarks>
  public sealed class JsonProjectRepository : IProjectRepository
  {
    /// <summary>
    /// Bump when persisted JSON shape changes incompatibly; see GOV-VOICESTUDIO-PERSISTENCE-FOUNDATION-01.
    /// </summary>
    public const int CurrentPersistedProjectSchemaVersion = 2;

    private readonly string _projectsDirectory;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private readonly JsonSerializerOptions _jsonOptions;

    private static void EnsureReadableSchemaVersion(Project project)
    {
      var v = project.PersistedProjectSchemaVersion;
      if (v > CurrentPersistedProjectSchemaVersion)
      {
        throw new InvalidDataException(
          $"Project file schema version {v} is newer than this app supports (max {CurrentPersistedProjectSchemaVersion}).");
      }
    }

    public JsonProjectRepository(string? projectsDirectory = null)
    {
      _projectsDirectory = projectsDirectory ??
          Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "VoiceStudio", "Projects");

      Directory.CreateDirectory(_projectsDirectory);

      _jsonOptions = new JsonSerializerOptions
      {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
      };
    }

    private string GetProjectPath(string projectId) =>
        Path.Combine(_projectsDirectory, $"{projectId}.json");

    public async Task<IReadOnlyList<ProjectMetadata>> GetAllMetadataAsync(CancellationToken cancellationToken = default)
    {
      await _lock.WaitAsync(cancellationToken);
      try
      {
        var metadataList = new List<ProjectMetadata>();
        foreach (var file in Directory.GetFiles(_projectsDirectory, "*.json"))
        {
          try
          {
            var json = await File.ReadAllTextAsync(file, cancellationToken);
            var project = JsonSerializer.Deserialize<Project>(json, _jsonOptions);

            if (project != null)
            {
              metadataList.Add(MapToMetadata(project, file));
            }
          }
          catch (Exception ex)
          {
            ErrorLogger.LogWarning($"Best effort operation failed: {ex.Message}", "JsonProjectRepository.Task");
          }
        }

        return metadataList.OrderByDescending(m => m.Modified).ToList();
      }
      finally
      {
        _lock.Release();
      }
    }

    public async Task<Project?> GetByIdAsync(string projectId, CancellationToken cancellationToken = default)
    {
      if (string.IsNullOrEmpty(projectId))
        return null;

      await _lock.WaitAsync(cancellationToken);
      try
      {
        var path = GetProjectPath(projectId);
        if (!File.Exists(path))
          return null;

        var json = await File.ReadAllTextAsync(path, cancellationToken);
        var project = JsonSerializer.Deserialize<Project>(json, _jsonOptions);
        if (project == null)
          return null;
        EnsureReadableSchemaVersion(project);
        return project;
      }
      finally
      {
        _lock.Release();
      }
    }

    public async Task<Project> SaveAsync(Project project, CancellationToken cancellationToken = default)
    {
      if (project == null)
        throw new ArgumentNullException(nameof(project));

      if (string.IsNullOrEmpty(project.Id))
        project.Id = Guid.NewGuid().ToString("N");

      project.UpdatedAt = DateTime.UtcNow.ToString("o");

      if (string.IsNullOrEmpty(project.CreatedAt))
        project.CreatedAt = project.UpdatedAt;

      project.PersistedProjectSchemaVersion = CurrentPersistedProjectSchemaVersion;

      await _lock.WaitAsync(cancellationToken);
      try
      {
        var path = GetProjectPath(project.Id);
        var json = JsonSerializer.Serialize(project, _jsonOptions);
        await File.WriteAllTextAsync(path, json, cancellationToken);

        return project;
      }
      finally
      {
        _lock.Release();
      }
    }

    public async Task<bool> DeleteAsync(string projectId, CancellationToken cancellationToken = default)
    {
      if (string.IsNullOrEmpty(projectId))
        return false;

      await _lock.WaitAsync(cancellationToken);
      try
      {
        var path = GetProjectPath(projectId);
        if (!File.Exists(path))
          return false;

        File.Delete(path);
        return true;
      }
      finally
      {
        _lock.Release();
      }
    }

    public async Task<bool> ExistsAsync(string projectId, CancellationToken cancellationToken = default)
    {
      if (string.IsNullOrEmpty(projectId))
        return false;

      await _lock.WaitAsync(cancellationToken);
      try
      {
        return File.Exists(GetProjectPath(projectId));
      }
      finally
      {
        _lock.Release();
      }
    }

    /// <summary>
    /// Alias for GetAllMetadataAsync for compatibility.
    /// </summary>
    public Task<IReadOnlyList<ProjectMetadata>> ListProjectsAsync(CancellationToken cancellationToken = default)
        => GetAllMetadataAsync(cancellationToken);

    /// <summary>
    /// Alias for GetByIdAsync for compatibility.
    /// </summary>
    public Task<Project?> OpenAsync(string projectId, CancellationToken cancellationToken = default)
        => GetByIdAsync(projectId, cancellationToken);

    /// <inheritdoc />
    public async Task SaveLastSubtitleTranscriptionIdAsync(string projectId, string? transcriptionId, CancellationToken cancellationToken = default)
    {
      if (string.IsNullOrEmpty(projectId))
        return;

      await _lock.WaitAsync(cancellationToken);
      try
      {
        var path = GetProjectPath(projectId);
        if (!File.Exists(path))
          return;

        var json = await File.ReadAllTextAsync(path, cancellationToken);
        var project = JsonSerializer.Deserialize<Project>(json, _jsonOptions);
        if (project == null)
          return;
        EnsureReadableSchemaVersion(project);

        project.LastSubtitleTranscriptionId = string.IsNullOrWhiteSpace(transcriptionId) ? null : transcriptionId;
        project.UpdatedAt = DateTime.UtcNow.ToString("o");
        project.PersistedProjectSchemaVersion = CurrentPersistedProjectSchemaVersion;

        var outJson = JsonSerializer.Serialize(project, _jsonOptions);
        await File.WriteAllTextAsync(path, outJson, cancellationToken);
      }
      finally
      {
        _lock.Release();
      }
    }

    /// <inheritdoc />
    public async Task<string?> GetLastSubtitleTranscriptionIdAsync(string projectId, CancellationToken cancellationToken = default)
    {
      if (string.IsNullOrEmpty(projectId))
        return null;

      var project = await GetByIdAsync(projectId, cancellationToken).ConfigureAwait(false);
      var id = project?.LastSubtitleTranscriptionId;
      return string.IsNullOrWhiteSpace(id) ? null : id;
    }

    private static ProjectMetadata MapToMetadata(Project project, string filePath)
    {
      DateTime.TryParse(project.CreatedAt, out var created);
      DateTime.TryParse(project.UpdatedAt, out var modified);

      var fileInfo = new FileInfo(filePath);

      return new ProjectMetadata
      {
        Id = project.Id,
        Name = project.Name,
        Description = project.Description,
        Created = created,
        Modified = modified,
        SizeBytes = fileInfo.Length,
        TrackCount = project.Tracks?.Count ?? 0,
        ProfileCount = project.VoiceProfileIds?.Count ?? 0
      };
    }
  }
}