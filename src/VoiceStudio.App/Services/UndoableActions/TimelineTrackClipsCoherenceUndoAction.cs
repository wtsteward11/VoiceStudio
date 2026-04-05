using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using VoiceStudio.App.UseCases;
using VoiceStudio.Core.Models;
using VoiceStudio.Core.Services;

namespace VoiceStudio.App.Services.UndoableActions;

/// <summary>
/// GAP-012 / successor Phase 1: single user-visible undo authority for bounded timeline edits on one track.
/// Restores persisted project clips and re-imports the backend mix graph via <see cref="ITimelineUseCase.ImportProjectTimelineAsync"/>.
/// Does not call <c>POST /api/timeline/undo</c>; backend in-memory undo stacks may still reflect API mutations (see execution row).
/// </summary>
public sealed class TimelineTrackClipsCoherenceUndoAction : IUndoableAction
{
  private readonly IBackendClient _backend;
  private readonly ITimelineUseCase _timelineUseCase;
  private readonly IProjectSessionDirtyState? _sessionDirty;
  private readonly string _projectId;
  private readonly string _trackId;
  private readonly AudioTrack _track;
  private readonly IReadOnlyList<AudioClip> _before;
  private readonly IReadOnlyList<AudioClip> _after;
  private readonly IErrorLoggingService? _log;
  private readonly Action? _onMutated;
  private readonly Project? _projectForLinkHygiene;
  private readonly IClipTranscriptLinkageService? _linkage;

  public TimelineTrackClipsCoherenceUndoAction(
      IBackendClient backend,
      ITimelineUseCase timelineUseCase,
      IProjectSessionDirtyState? sessionDirty,
      string projectId,
      string trackId,
      AudioTrack track,
      IEnumerable<AudioClip> beforeSnapshot,
      IEnumerable<AudioClip> afterSnapshot,
      string actionName,
      IErrorLoggingService? log = null,
      Action? onMutated = null,
      Project? projectForLinkHygiene = null,
      IClipTranscriptLinkageService? linkage = null)
  {
    _backend = backend ?? throw new ArgumentNullException(nameof(backend));
    _timelineUseCase = timelineUseCase ?? throw new ArgumentNullException(nameof(timelineUseCase));
    _sessionDirty = sessionDirty;
    _projectId = projectId ?? throw new ArgumentNullException(nameof(projectId));
    _trackId = trackId ?? throw new ArgumentNullException(nameof(trackId));
    _track = track ?? throw new ArgumentNullException(nameof(track));
    _before = (beforeSnapshot ?? throw new ArgumentNullException(nameof(beforeSnapshot))).Select(Clone).ToList();
    _after = (afterSnapshot ?? throw new ArgumentNullException(nameof(afterSnapshot))).Select(Clone).ToList();
    ActionName = actionName ?? throw new ArgumentNullException(nameof(actionName));
    _log = log;
    _onMutated = onMutated;
    _projectForLinkHygiene = projectForLinkHygiene;
    _linkage = linkage;
  }

  public string ActionName { get; }

  public void Undo() => ApplyTarget(_before);

  public void Redo() => ApplyTarget(_after);

  /// <summary>Deep copy fields used for project CRUD and GAP-037 timeline semantics.</summary>
  /// <param name="c">Source clip; must not be null.</param>
  public static AudioClip Clone(AudioClip c)
  {
    ArgumentNullException.ThrowIfNull(c);
    return new AudioClip
    {
      Id = c.Id,
      Name = c.Name,
      ProfileId = c.ProfileId,
      AudioId = c.AudioId,
      AudioUrl = c.AudioUrl,
      StartTime = c.StartTime,
      Duration = c.Duration,
      SourceStartSeconds = c.SourceStartSeconds,
      FadeInSeconds = c.FadeInSeconds,
      FadeOutSeconds = c.FadeOutSeconds,
      Engine = c.Engine,
      QualityScore = c.QualityScore,
      TranscriptTruth = c.TranscriptTruth,
      WaveformSamples = c.WaveformSamples != null ? new List<float>(c.WaveformSamples) : null,
      DerivedFromClipId = string.IsNullOrWhiteSpace(c.DerivedFromClipId) ? null : c.DerivedFromClipId,
    };
  }

  private void ApplyTarget(IReadOnlyList<AudioClip> target)
  {
    var originalIds = (_track.Clips ?? new List<AudioClip>()).Select(x => x.Id).ToHashSet(StringComparer.Ordinal);
    var targetIds = target.Select(x => x.Id).ToHashSet(StringComparer.Ordinal);

    foreach (var id in originalIds.Except(targetIds, StringComparer.Ordinal))
    {
      try
      {
        _linkage?.RemoveLinksByClipId(_projectForLinkHygiene, id);
        _ = _backend.DeleteClipAsync(_projectId, _trackId, id, CancellationToken.None)
            .ConfigureAwait(false)
            .GetAwaiter()
            .GetResult();
      }
      catch (Exception ex)
      {
        _log?.LogError(ex, "TimelineTrackClipsCoherenceUndoAction.DeleteClip");
        throw;
      }
    }

    foreach (var clip in target)
    {
      try
      {
        if (originalIds.Contains(clip.Id))
        {
          _ = _backend
              .UpdateClipAsync(
                  _projectId,
                  _trackId,
                  clip.Id,
                  name: clip.Name,
                  startTime: clip.StartTime,
                  durationSeconds: clip.Duration.TotalSeconds,
                  sourceStartSeconds: clip.SourceStartSeconds,
                  fadeInSeconds: clip.FadeInSeconds,
                  fadeOutSeconds: clip.FadeOutSeconds,
                  audioId: string.IsNullOrWhiteSpace(clip.AudioId) ? null : clip.AudioId,
                  audioUrl: string.IsNullOrWhiteSpace(clip.AudioUrl) ? null : clip.AudioUrl,
                  derivedFromClipId: string.IsNullOrWhiteSpace(clip.DerivedFromClipId) ? null : clip.DerivedFromClipId,
                  cancellationToken: CancellationToken.None)
              .ConfigureAwait(false)
              .GetAwaiter()
              .GetResult();
        }
        else
        {
          var created = _backend
              .CreateClipAsync(_projectId, _trackId, Clone(clip), CancellationToken.None)
              .ConfigureAwait(false)
              .GetAwaiter()
              .GetResult();
          var fromId = string.IsNullOrWhiteSpace(clip.DerivedFromClipId) ? null : clip.DerivedFromClipId;
          if (_linkage != null && _projectForLinkHygiene != null && !string.IsNullOrEmpty(fromId))
            _linkage.CopyTranscriptLinksToNewClip(_projectForLinkHygiene, fromId, created.Id);
        }
      }
      catch (Exception ex)
      {
        _log?.LogError(ex, "TimelineTrackClipsCoherenceUndoAction.UpsertClip");
        throw;
      }
    }

    _track.Clips ??= new List<AudioClip>();
    _track.Clips.Clear();
    foreach (var c in target)
      _track.Clips.Add(Clone(c));

    _sessionDirty?.MarkProjectDirty("timeline_tracks");
    if (_linkage != null && _projectForLinkHygiene != null)
      _sessionDirty?.MarkProjectDirty("clip_transcript_links");

    try
    {
      _timelineUseCase
          .ImportProjectTimelineAsync(_projectId, CancellationToken.None)
          .ConfigureAwait(false)
          .GetAwaiter()
          .GetResult();
    }
    catch (Exception ex)
    {
      _log?.LogError(ex, "TimelineTrackClipsCoherenceUndoAction.ImportProjectTimeline");
      throw;
    }

    _onMutated?.Invoke();
  }
}
