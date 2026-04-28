using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using VoiceStudio.Core.Models;
using VoiceStudio.Core.Services;

namespace VoiceStudio.App.Services;

/// <summary>
/// Creates timeline clips for synthesized audio using <see cref="ITimelineTrackService"/> +
/// <see cref="ITimelineClipService"/> and <see cref="IContextManager"/> project/track authority.
/// </summary>
public sealed class GeneratedAudioTimelineService : IGeneratedAudioTimelineService
{
  private const string SourceLabel = "Voice Synthesis";

  private readonly IContextManager _context;
  private readonly ITimelineTrackService _trackService;
  private readonly ITimelineClipService _clipService;
  private readonly IErrorLoggingService? _log;

  public GeneratedAudioTimelineService(
      IContextManager context,
      ITimelineTrackService trackService,
      ITimelineClipService clipService,
      IErrorLoggingService? log = null)
  {
    _context = context ?? throw new ArgumentNullException(nameof(context));
    _trackService = trackService ?? throw new ArgumentNullException(nameof(trackService));
    _clipService = clipService ?? throw new ArgumentNullException(nameof(clipService));
    _log = log;
  }

  /// <inheritdoc />
  public async Task<GeneratedAudioTimelineResult> AddGeneratedClipAsync(
      GeneratedAudioTimelineRequest request,
      CancellationToken cancellationToken = default)
  {
    if (request == null)
      throw new ArgumentNullException(nameof(request));

    if (string.IsNullOrWhiteSpace(request.AudioId))
    {
      return new GeneratedAudioTimelineResult(
          false,
          GeneratedAudioTimelineKind.Unavailable,
          "No synthesized audio id is available. Run synthesis first.",
          null,
          null,
          null);
    }

    var projectId = _context.ActiveProjectId;
    if (string.IsNullOrWhiteSpace(projectId))
    {
      return new GeneratedAudioTimelineResult(
          false,
          GeneratedAudioTimelineKind.Unavailable,
          "No active project. Open or create a project, then add to timeline.",
          null,
          null,
          null);
    }

    var profileId = request.ProfileId?.Trim();
    if (string.IsNullOrWhiteSpace(profileId))
    {
      return new GeneratedAudioTimelineResult(
          false,
          GeneratedAudioTimelineKind.Unavailable,
          "No voice profile is selected. Choose a profile in Voice Synthesis or Profiles, then retry.",
          null,
          null,
          null);
    }

    try
    {
      var resolve = await RecordingTrackTargetResolver.ResolveRecordableTrackAsync(
              projectId,
              _context,
              _trackService,
              cancellationToken)
          .ConfigureAwait(false);
      if (!resolve.ok || string.IsNullOrWhiteSpace(resolve.trackId))
      {
        return new GeneratedAudioTimelineResult(
            false,
            GeneratedAudioTimelineKind.Unavailable,
            resolve.error ?? "Timeline is not ready for this project.",
            projectId,
            null,
            null);
      }

      var tracks = await _trackService.GetTracksAsync(projectId, cancellationToken).ConfigureAwait(false);
      var targetTrack = tracks.FirstOrDefault(t => string.Equals(t.Id, resolve.trackId, StringComparison.Ordinal));
      if (targetTrack == null)
      {
        return new GeneratedAudioTimelineResult(
            false,
            GeneratedAudioTimelineKind.Unavailable,
            "Could not resolve the target timeline track. Reload the project or create a track.",
            projectId,
            null,
            null);
      }

      var startSeconds = ComputeAppendStartSeconds(targetTrack);
      var clipName = BuildClipName(request);

      var clip = new AudioClip
      {
        Id = Guid.NewGuid().ToString(),
        Name = clipName,
        ProfileId = profileId,
        AudioId = request.AudioId,
        AudioUrl = request.AudioPathOrUrl?.Trim() ?? string.Empty,
        Duration = request.Duration < TimeSpan.Zero ? TimeSpan.Zero : request.Duration,
        StartTime = startSeconds,
        SourceStartSeconds = 0,
        Engine = string.IsNullOrWhiteSpace(request.Engine) ? null : request.Engine.Trim(),
        QualityScore = request.QualityScore,
        DerivedFromClipId = string.IsNullOrWhiteSpace(request.LibraryAssetId) ? null : request.LibraryAssetId.Trim(),
      };

      var persisted = await _clipService.CreateClipAsync(projectId, targetTrack.Id, clip, cancellationToken)
          .ConfigureAwait(false);

      return new GeneratedAudioTimelineResult(
          true,
          GeneratedAudioTimelineKind.Added,
          null,
          projectId,
          targetTrack.Id,
          persisted.Id);
    }
    catch (OperationCanceledException)
    {
      throw;
    }
    catch (Exception ex)
    {
      _log?.LogError(ex, "GeneratedAudioTimelineService.AddGeneratedClip");
      return new GeneratedAudioTimelineResult(
          false,
          GeneratedAudioTimelineKind.Failed,
          ex.Message,
          projectId,
          null,
          null);
    }
  }

  private static double ComputeAppendStartSeconds(AudioTrack track)
  {
    if (track.Clips == null || track.Clips.Count == 0)
      return 0;
    return track.Clips.Max(c => c.EndTime);
  }

  private static string BuildClipName(GeneratedAudioTimelineRequest request)
  {
    var profile = string.IsNullOrWhiteSpace(request.ProfileName) ? "Profile" : request.ProfileName.Trim();
    var stamp = request.GeneratedAtLocal.ToString("g", System.Globalization.CultureInfo.CurrentCulture);
    var tail = string.IsNullOrWhiteSpace(request.TextPreview)
        ? string.Empty
        : ": " + TrimPreview(request.TextPreview!, 40);
    return $"{SourceLabel} · {profile} · {stamp}{tail}";
  }

  private static string TrimPreview(string text, int maxChars)
  {
    var oneLine = text.Replace('\r', ' ').Replace('\n', ' ').Trim();
    if (oneLine.Length <= maxChars)
      return oneLine;
    return string.Concat(oneLine.AsSpan(0, maxChars), "…");
  }
}
