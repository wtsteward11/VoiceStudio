using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using VoiceStudio.Core.Models;
using VoiceStudio.Core.Services;

namespace VoiceStudio.App.Services;

/// <summary>
/// GAP-042 Slice 2: resolves the timeline track id that receives a single-mic capture, using context + track list authority.
/// </summary>
public static class RecordingTrackTargetResolver
{
    /// <summary>
    /// Primary: <see cref="IContextManager.ActiveTimelinePrimaryTrackId"/> when it exists on the project track list.
    /// Fallback: first track from <see cref="ITimelineTrackService.GetTracksAsync"/> (ordered).
    /// </summary>
    public static async Task<(bool ok, string? trackId, string? error)> ResolveRecordableTrackAsync(
        string? projectId,
        IContextManager? context,
        ITimelineTrackService? trackService,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(projectId))
            return (false, null, "No active project.");

        if (trackService == null)
            return (false, null, "Timeline track service is not available.");

        var tracks = await trackService.GetTracksAsync(projectId!, cancellationToken).ConfigureAwait(false);
        if (tracks.Count == 0)
            return (false, null, "No timeline tracks in this project. Create a track before recording.");

        var primary = context?.ActiveTimelinePrimaryTrackId;
        if (!string.IsNullOrWhiteSpace(primary))
        {
            var match = tracks.FirstOrDefault(t => string.Equals(t.Id, primary, StringComparison.Ordinal));
            if (match != null)
                return (true, match.Id, null);
        }

        return (true, tracks[0].Id, null);
    }
}
