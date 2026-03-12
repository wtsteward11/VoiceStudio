using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using VoiceStudio.Core.Models;

namespace VoiceStudio.Core.Services
{
  /// <summary>
  /// Project audio facade. Provides a focused seam for project audio list/get/save operations,
  /// delegating to the backend transport. Use this instead of IBackendClient for project audio
  /// operations to reduce coupling and enable test isolation.
  /// </summary>
  public interface IProjectAudioClient
  {
    Task<List<ProjectAudioFile>> ListProjectAudioAsync(string projectId, CancellationToken cancellationToken = default);
    Task<Stream> GetProjectAudioAsync(string projectId, string filename, CancellationToken cancellationToken = default);
    Task<ProjectAudioFile> SaveAudioToProjectAsync(string projectId, string audioId, string? filename = null, CancellationToken cancellationToken = default);
  }
}
