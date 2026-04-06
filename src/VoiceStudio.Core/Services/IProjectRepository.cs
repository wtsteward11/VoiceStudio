using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using VoiceStudio.Core.Models;

namespace VoiceStudio.Core.Services
{
  /// <summary>
  /// Repository pattern for project persistence (VS-0004, VS-0015).
  /// Abstracts storage implementation (SQLite, JSON, etc.).
  /// </summary>
  /// <remarks>
  /// Implementations: SqliteProjectRepository, JsonProjectRepository.
  /// Used by ProjectStore for durable project CRUD.
  /// </remarks>
  public interface IProjectRepository
  {
    /// <summary>
    /// Get all projects (metadata only for efficiency).
    /// </summary>
    Task<IReadOnlyList<ProjectMetadata>> GetAllMetadataAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Get full project by ID.
    /// </summary>
    Task<Project?> GetByIdAsync(string projectId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Save project (create if new, update if exists).
    /// </summary>
    /// <returns>Saved project with updated metadata (Modified timestamp, etc.)</returns>
    Task<Project> SaveAsync(Project project, CancellationToken cancellationToken = default);

    /// <summary>
    /// Delete project by ID.
    /// </summary>
    /// <returns>True if deleted, false if not found</returns>
    Task<bool> DeleteAsync(string projectId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Check if project exists.
    /// </summary>
    Task<bool> ExistsAsync(string projectId, CancellationToken cancellationToken = default);

    /// <summary>
    /// List all projects (alias for GetAllMetadataAsync for compatibility).
    /// </summary>
    Task<IReadOnlyList<ProjectMetadata>> ListProjectsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Open a project by ID (alias for GetByIdAsync for compatibility).
    /// </summary>
    Task<Project?> OpenAsync(string projectId, CancellationToken cancellationToken = default);

    /// <summary>
    /// GAP-045: persist last Timeline subtitle overlay transcription id for a project (local JSON).
    /// </summary>
    Task SaveLastSubtitleTranscriptionIdAsync(string projectId, string? transcriptionId, CancellationToken cancellationToken = default);

    /// <summary>
    /// GAP-045: read persisted last subtitle transcription id for a project.
    /// </summary>
    Task<string?> GetLastSubtitleTranscriptionIdAsync(string projectId, CancellationToken cancellationToken = default);
  }
}