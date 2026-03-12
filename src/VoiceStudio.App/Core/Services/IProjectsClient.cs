using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using VoiceStudio.Core.Models;

namespace VoiceStudio.Core.Services
{
  /// <summary>
  /// Projects domain client facade. Provides a focused seam for project CRUD operations,
  /// delegating to the backend transport. Use this instead of IBackendClient for project
  /// operations to reduce coupling and enable test isolation.
  /// </summary>
  public interface IProjectsClient
  {
    Task<List<Project>> GetProjectsAsync(CancellationToken cancellationToken = default);
    Task<Project> GetProjectAsync(string projectId, CancellationToken cancellationToken = default);
    Task<Project> CreateProjectAsync(string name, string? description = null, CancellationToken cancellationToken = default);
    Task<Project> UpdateProjectAsync(
      string projectId,
      string? name = null,
      string? description = null,
      List<string>? voiceProfileIds = null,
      CancellationToken cancellationToken = default);
    Task<bool> DeleteProjectAsync(string projectId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Invalidates the projects list cache. Call after create/update/delete so the next
    /// GetProjectsAsync refetches from the backend.
    /// </summary>
    void InvalidateProjectsCache();
  }
}
