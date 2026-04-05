using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using VoiceStudio.App.Core.Services;
using VoiceStudio.Core.Events;
using VoiceStudio.Core.Models;
using VoiceStudio.Core.Services;

namespace VoiceStudio.App.Services;

/// <summary>
/// GAP-045: one canonical refresh path — <see cref="ITranscriptionClient.TranscribeAudioAsync"/> + deterministic linkage rebuild via <see cref="IClipTranscriptLinkageService.UpsertLinksForTranscription"/>.
/// </summary>
public sealed class TranscriptTruthRefreshCoordinator : ITranscriptTruthRefreshCoordinator
{
  private readonly ITranscriptionClient _transcription;
  private readonly IClipTranscriptLinkageService _linkage;
  private readonly IProjectSessionDirtyState? _dirty;
  private readonly IEventAggregator? _events;
  private readonly IErrorLoggingService? _log;

  public TranscriptTruthRefreshCoordinator(
      ITranscriptionClient transcription,
      IClipTranscriptLinkageService linkage,
      IProjectSessionDirtyState? dirty = null,
      IEventAggregator? events = null,
      IErrorLoggingService? log = null)
  {
    _transcription = transcription ?? throw new ArgumentNullException(nameof(transcription));
    _linkage = linkage ?? throw new ArgumentNullException(nameof(linkage));
    _dirty = dirty;
    _events = events;
    _log = log;
  }

  /// <inheritdoc />
  public async Task<string?> TryRefreshStaleTranscriptForClipAsync(
      Project project,
      string trackId,
      string clipId,
      string engine,
      string? language,
      bool wordTimestamps,
      bool diarization,
      bool useVad,
      string sourcePanelId,
      string? projectId,
      CancellationToken cancellationToken = default)
  {
    if (project == null || string.IsNullOrWhiteSpace(trackId) || string.IsNullOrWhiteSpace(clipId))
      return "Project, track, and clip are required for transcript refresh.";

    var clip = FindClip(project, clipId);
    if (clip == null)
      return "That clip is not on the active project model.";

    if (clip.TranscriptTruth == TranscriptTruthState.RefreshInProgress)
      return "Transcript refresh is already in progress for this clip.";

    if (clip.TranscriptTruth != TranscriptTruthState.StaleAfterClipRegeneration)
      return "This clip does not require transcript refresh (transcript is not marked stale).";

    if (string.IsNullOrWhiteSpace(clip.AudioId))
      return "Clip has no audio id; cannot transcribe.";

    clip.TranscriptTruth = TranscriptTruthState.RefreshInProgress;
    Publish(sourcePanelId, project.Id, trackId, clipId, TranscriptTruthState.RefreshInProgress, "Transcript refresh in progress…");

    try
    {
      var request = new TranscriptionRequest
      {
        AudioId = clip.AudioId,
        Engine = engine,
        Language = string.Equals(language, "auto", StringComparison.OrdinalIgnoreCase) ? null : language,
        WordTimestamps = wordTimestamps,
        Diarization = diarization,
        UseVad = useVad,
      };

      var transcription = await _transcription
          .TranscribeAudioAsync(request, projectId, cancellationToken)
          .ConfigureAwait(false);

      if (transcription == null || string.IsNullOrWhiteSpace(transcription.Id))
      {
        clip.TranscriptTruth = TranscriptTruthState.StaleAfterClipRegeneration;
        Publish(sourcePanelId, project.Id, trackId, clipId, TranscriptTruthState.StaleAfterClipRegeneration, "Transcript refresh failed: no transcript id returned.");
        return "Transcript refresh failed: backend returned no transcript id.";
      }

      if (!string.Equals(transcription.AudioId, clip.AudioId, StringComparison.Ordinal))
      {
        clip.TranscriptTruth = TranscriptTruthState.StaleAfterClipRegeneration;
        _log?.LogError(
            new InvalidOperationException(
                $"TranscriptTruthRefresh audio id mismatch: clip={clip.AudioId} tx={transcription.AudioId}"),
            "TranscriptTruthRefreshMismatch");
        Publish(sourcePanelId, project.Id, trackId, clipId, TranscriptTruthState.StaleAfterClipRegeneration, "Transcript refresh failed: audio id mismatch.");
        return "Transcript refresh failed: transcript audio does not match this clip.";
      }

      _linkage.RemoveLinksByClipId(project, clipId);

      var segments = transcription.Segments;
      var inputs = (segments ?? Enumerable.Empty<TranscriptionSegment>())
          .Select(s => new TranscriptionSegmentLinkInput(
              string.IsNullOrWhiteSpace(s.Id) ? string.Empty : s.Id!,
              s.Start,
              s.End))
          .ToList();

      _linkage.UpsertLinksForTranscription(project, transcription.Id, transcription.AudioId, inputs);
      clip.TranscriptTruth = TranscriptTruthState.Current;
      _dirty?.MarkProjectDirty("transcript_truth_refresh");

      if (_events != null && segments != null && segments.Count > 0)
      {
        var subtitleSegments = segments
            .Select(s => new SubtitleSegment(
                s.Start,
                s.End,
                s.Text,
                string.IsNullOrWhiteSpace(s.Id) ? null : s.Id))
            .ToList();
        _events.Publish(
            new TranscriptionCompletedEvent(
                sourcePanelId,
                transcription.AudioId,
                transcription.Id,
                transcription.Text,
                subtitleSegments,
                TimeSpan.FromSeconds(transcription.Duration),
                string.IsNullOrWhiteSpace(transcription.Language) ? "en" : transcription.Language));
      }

      Publish(sourcePanelId, project.Id, trackId, clipId, TranscriptTruthState.Current, "Transcript refreshed and linkage rebuilt.");
      return null;
    }
    catch (OperationCanceledException)
    {
      clip.TranscriptTruth = TranscriptTruthState.StaleAfterClipRegeneration;
      Publish(sourcePanelId, project.Id, trackId, clipId, TranscriptTruthState.StaleAfterClipRegeneration, "Transcript refresh cancelled.");
      throw;
    }
    catch (Exception ex)
    {
      _log?.LogError(ex, "TranscriptTruthRefresh");
      clip.TranscriptTruth = TranscriptTruthState.StaleAfterClipRegeneration;
      var msg = $"Transcript refresh failed: {ex.Message}";
      Publish(sourcePanelId, project.Id, trackId, clipId, TranscriptTruthState.StaleAfterClipRegeneration, msg);
      return msg;
    }
  }

  private void Publish(
      string sourcePanelId,
      string projectId,
      string trackId,
      string clipId,
      TranscriptTruthState state,
      string? message) =>
      _events?.Publish(
          new TranscriptTruthStateChangedEvent(
              sourcePanelId,
              projectId,
              trackId,
              clipId,
              state,
              message));

  private static AudioClip? FindClip(Project project, string clipId)
  {
    foreach (var track in project.Tracks ?? Enumerable.Empty<AudioTrack>())
    {
      var c = track.Clips?.FirstOrDefault(x => string.Equals(x.Id, clipId, StringComparison.Ordinal));
      if (c != null)
        return c;
    }

    return null;
  }
}
