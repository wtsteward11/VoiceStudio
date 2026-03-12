using System.Threading;
using System.Threading.Tasks;
using VoiceStudio.Core.Models;

namespace VoiceStudio.Core.Services
{
  /// <summary>
  /// Timeline clip CRUD facade. Provides a focused seam for clip create/delete operations,
  /// delegating to the backend transport. Use this instead of IBackendClient for clip
  /// operations in Timeline panel to reduce coupling and enable test isolation.
  /// </summary>
  public interface ITimelineClipService
  {
    Task<AudioClip> CreateClipAsync(string projectId, string trackId, AudioClip clip, CancellationToken cancellationToken = default);
    Task<bool> DeleteClipAsync(string projectId, string trackId, string clipId, CancellationToken cancellationToken = default);
  }
}
