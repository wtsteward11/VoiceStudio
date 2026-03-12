using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using VoiceStudio.Core.Models;
using VoiceStudio.Core.Services;

namespace VoiceStudio.App.Services
{
  /// <summary>
  /// Authoritative projects transport boundary. Owns project-specific list-key policy,
  /// canonical cache invalidation, and project transport semantics.
  /// Consumers must use IProjectsClient for project workflows, not IBackendClient.
  /// </summary>
  public sealed class ProjectsClient : IProjectsClient
  {
    /// <summary>
    /// Canonical cache key for projects list. Used for single-flight, TTL, and invalidation.
    /// </summary>
    public const string ProjectsListKey = "projects:list";

    private readonly IBackendClient _backend;
    private readonly IRequestCoordinator _coordinator;

    public ProjectsClient(IBackendClient backend, IRequestCoordinator coordinator)
    {
      _backend = backend ?? throw new System.ArgumentNullException(nameof(backend));
      _coordinator = coordinator ?? throw new System.ArgumentNullException(nameof(coordinator));
    }

    public Task<List<Project>> GetProjectsAsync(CancellationToken cancellationToken = default)
      => _backend.GetProjectsAsync(cancellationToken);

    public Task<Project> GetProjectAsync(string projectId, CancellationToken cancellationToken = default)
      => _backend.GetProjectAsync(projectId, cancellationToken);

    public async Task<Project> CreateProjectAsync(string name, string? description = null, CancellationToken cancellationToken = default)
    {
      var result = await _backend.CreateProjectAsync(name, description, cancellationToken).ConfigureAwait(false);
      InvalidateProjectsCache();
      return result;
    }

    public async Task<Project> UpdateProjectAsync(
      string projectId,
      string? name = null,
      string? description = null,
      List<string>? voiceProfileIds = null,
      CancellationToken cancellationToken = default)
    {
      var result = await _backend.UpdateProjectAsync(projectId, name, description, voiceProfileIds, cancellationToken).ConfigureAwait(false);
      InvalidateProjectsCache();
      return result;
    }

    public async Task<bool> DeleteProjectAsync(string projectId, CancellationToken cancellationToken = default)
    {
      var result = await _backend.DeleteProjectAsync(projectId, cancellationToken).ConfigureAwait(false);
      if (result)
        InvalidateProjectsCache();
      return result;
    }

    /// <summary>
    /// Invalidates the projects list cache. Called after create/update/delete so the next
    /// GetProjectsAsync refetches from the backend. ProjectsClient owns this semantics.
    /// </summary>
    public void InvalidateProjectsCache()
    {
      _coordinator.Invalidate(ProjectsListKey);
    }
  }
}
