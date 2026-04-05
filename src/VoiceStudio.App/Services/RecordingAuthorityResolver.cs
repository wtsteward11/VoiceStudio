using System.Threading;
using System.Threading.Tasks;

namespace VoiceStudio.App.Services;

/// <summary>
/// Ctrl+R / command-path resolution: same track + input availability policy as the Recording panel (GAP-035).
/// </summary>
public static class RecordingAuthorityResolver
{
  public static async Task<RecordingAuthorityResolution> ResolveForCommandPathAsync(CancellationToken cancellationToken = default)
  {
    var ctx = AppServices.TryGetContextManager();
    var projectId = ctx?.ActiveProjectId;
    var trackSvc = AppServices.TryGetTimelineTrackService();
    var (ok, trackId, err) = await RecordingTrackTargetResolver
        .ResolveRecordableTrackAsync(projectId, ctx, trackSvc, cancellationToken)
        .ConfigureAwait(false);
    if (!ok)
      return RecordingAuthorityResolution.Fail(err ?? "Cannot resolve recording target track.");

    var recordingClient = AppServices.TryGetRecordingClient();
    if (recordingClient == null)
      return RecordingAuthorityResolution.Fail("Recording client is not available.");

    var commandState = AppServices.TryGetRecordingInputCommandState();
    var inputId = commandState?.SelectedInputSourceId;
    if (string.IsNullOrWhiteSpace(inputId))
    {
      return RecordingAuthorityResolution.Fail(
          "Select a microphone in the Recording panel before using Ctrl+R.");
    }

    var availability = AppServices.TryGetRecordingDeviceAvailabilityService();
    if (availability != null)
      await availability.RefreshAsync(cancellationToken).ConfigureAwait(false);

    var (resOk, _, resErr) = await RecordingInputDeviceResolver.TryResolveAsync(
            recordingClient,
            availability,
            inputId,
            cancellationToken)
        .ConfigureAwait(false);
    if (!resOk)
      return RecordingAuthorityResolution.Fail(resErr ?? "Selected microphone is not available for recording.");

    return RecordingAuthorityResolution.Ok(trackId!, inputId);
  }
}
