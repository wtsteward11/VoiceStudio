using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using VoiceStudio.Core.Models;

namespace VoiceStudio.Core.Services
{
  /// <summary>
  /// Timeline track CRUD facade. Provides a focused seam for track list/create/update/delete operations,
  /// delegating to the backend transport. Use this instead of IBackendClient for track operations
  /// in Timeline panel and AudioStore to reduce coupling and enable test isolation.
  /// </summary>
  /// <remarks>
  /// GetTracksAsync returns tracks ordered by TrackNumber ascending, then by Name.
  /// CreateTrackAsync: when name is null or whitespace, generates "Track 1", "Track 2", etc. from existing count.
  /// </remarks>
  public interface ITimelineTrackService
  {
    /// <summary>Returns tracks for the project, ordered by TrackNumber ascending then by Name.</summary>
    Task<List<AudioTrack>> GetTracksAsync(string projectId, CancellationToken cancellationToken = default);
    /// <summary>Creates a track. When name is null/whitespace, generates "Track N" from existing count.</summary>
    Task<AudioTrack> CreateTrackAsync(string projectId, string? name, string? engine = null, CancellationToken cancellationToken = default);
    Task<AudioTrack> UpdateTrackAsync(string projectId, string trackId, string? name = null, string? engine = null, CancellationToken cancellationToken = default);
    Task<bool> DeleteTrackAsync(string projectId, string trackId, CancellationToken cancellationToken = default);
  }
}
