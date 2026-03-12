using System.Threading;
using System.Threading.Tasks;
using VoiceStudio.Core.Models;
using VoiceStudio.Core.Services;

namespace VoiceStudio.App.Services
{
  /// <summary>
  /// Timeline clip CRUD service. Delegates to IBackendClient for clip create/delete.
  /// </summary>
  public sealed class TimelineClipService : ITimelineClipService
  {
    private readonly IBackendClient _backend;

    public TimelineClipService(IBackendClient backend)
    {
      _backend = backend ?? throw new System.ArgumentNullException(nameof(backend));
    }

    public Task<AudioClip> CreateClipAsync(string projectId, string trackId, AudioClip clip, CancellationToken cancellationToken = default)
      => _backend.CreateClipAsync(projectId, trackId, clip, cancellationToken);

    public Task<bool> DeleteClipAsync(string projectId, string trackId, string clipId, CancellationToken cancellationToken = default)
      => _backend.DeleteClipAsync(projectId, trackId, clipId, cancellationToken);
  }
}
