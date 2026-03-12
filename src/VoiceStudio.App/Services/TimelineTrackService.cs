using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using VoiceStudio.Core.Models;
using VoiceStudio.Core.Services;

namespace VoiceStudio.App.Services
{
  /// <summary>
  /// Timeline track CRUD service. Delegates to IBackendClient for track operations.
  /// Applies policy: default track naming when name is null/whitespace; ordered results by TrackNumber.
  /// </summary>
  public sealed class TimelineTrackService : ITimelineTrackService
  {
    private readonly IBackendClient _backend;

    public TimelineTrackService(IBackendClient backend)
    {
      _backend = backend ?? throw new System.ArgumentNullException(nameof(backend));
    }

    /// <inheritdoc />
    public async Task<List<AudioTrack>> GetTracksAsync(string projectId, CancellationToken cancellationToken = default)
    {
      var tracks = await _backend.GetTracksAsync(projectId, cancellationToken).ConfigureAwait(false);
      return tracks
          .OrderBy(t => t.TrackNumber)
          .ThenBy(t => t.Name ?? string.Empty)
          .ToList();
    }

    /// <inheritdoc />
    public async Task<AudioTrack> CreateTrackAsync(string projectId, string? name, string? engine = null, CancellationToken cancellationToken = default)
    {
      var effectiveName = !string.IsNullOrWhiteSpace(name)
          ? name
          : await GenerateDefaultTrackNameAsync(projectId, cancellationToken).ConfigureAwait(false);
      return await _backend.CreateTrackAsync(projectId, effectiveName, engine, cancellationToken).ConfigureAwait(false);
    }

    private async Task<string> GenerateDefaultTrackNameAsync(string projectId, CancellationToken cancellationToken)
    {
      var tracks = await _backend.GetTracksAsync(projectId, cancellationToken).ConfigureAwait(false);
      var nextNumber = tracks.Count > 0
          ? (tracks.Max(t => t.TrackNumber) + 1)
          : 1;
      return $"Track {nextNumber}";
    }

    public Task<AudioTrack> UpdateTrackAsync(string projectId, string trackId, string? name = null, string? engine = null, CancellationToken cancellationToken = default)
      => _backend.UpdateTrackAsync(projectId, trackId, name, engine, cancellationToken);

    public Task<bool> DeleteTrackAsync(string projectId, string trackId, CancellationToken cancellationToken = default)
      => _backend.DeleteTrackAsync(projectId, trackId, cancellationToken);
  }
}
