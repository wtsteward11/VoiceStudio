using System;
using System.Collections.Generic;
using System.Linq;
using VoiceStudio.App.Core.Services;
using VoiceStudio.Core.Events;
using VoiceStudio.Core.Models;
using VoiceStudio.Core.Services;

namespace VoiceStudio.App.Services.UndoableActions;

/// <summary>
/// GAP-046: undo/redo for transcript-driven segment regeneration — restores prior clip audio + transcript linkage on undo.
/// </summary>
public sealed class TranscriptClipAudioReplaceUndoAction : IUndoableAction
{
  private readonly IBackendClient _backend;
  private readonly IClipTranscriptLinkageService _linkage;
  private readonly Project _project;
  private readonly string _projectId;
  private readonly string _trackId;
  private readonly string _clipId;
  private readonly string _prevAudioId;
  private readonly string _prevAudioUrl;
  private readonly double _prevDurationSeconds;
  private readonly string _newAudioId;
  private readonly string _newAudioUrl;
  private readonly double _newDurationSeconds;
  private readonly IReadOnlyList<ClipTranscriptLink> _savedLinks;
  private readonly IProjectSessionDirtyState? _dirty;
  private readonly IEventAggregator? _eventAggregator;
  private readonly string _sourcePanelId;
  private readonly IErrorLoggingService? _log;

  public TranscriptClipAudioReplaceUndoAction(
      IBackendClient backend,
      IClipTranscriptLinkageService linkage,
      Project project,
      string projectId,
      string trackId,
      string clipId,
      string prevAudioId,
      string prevAudioUrl,
      double prevDurationSeconds,
      string newAudioId,
      string newAudioUrl,
      double newDurationSeconds,
      IReadOnlyList<ClipTranscriptLink> savedLinks,
      IProjectSessionDirtyState? dirty = null,
      IEventAggregator? eventAggregator = null,
      string sourcePanelId = "",
      IErrorLoggingService? log = null)
  {
    _backend = backend ?? throw new ArgumentNullException(nameof(backend));
    _linkage = linkage ?? throw new ArgumentNullException(nameof(linkage));
    _project = project ?? throw new ArgumentNullException(nameof(project));
    _projectId = projectId ?? throw new ArgumentNullException(nameof(projectId));
    _trackId = trackId ?? throw new ArgumentNullException(nameof(trackId));
    _clipId = clipId ?? throw new ArgumentNullException(nameof(clipId));
    _prevAudioId = prevAudioId ?? string.Empty;
    _prevAudioUrl = prevAudioUrl ?? string.Empty;
    _prevDurationSeconds = prevDurationSeconds;
    _newAudioId = newAudioId ?? string.Empty;
    _newAudioUrl = newAudioUrl ?? string.Empty;
    _newDurationSeconds = newDurationSeconds;
    _savedLinks = savedLinks ?? Array.Empty<ClipTranscriptLink>();
    _dirty = dirty;
    _eventAggregator = eventAggregator;
    _sourcePanelId = sourcePanelId ?? string.Empty;
    _log = log;
  }

  public string ActionName => "Regenerate transcript segment (clip audio)";

  public void Undo()
  {
    try
    {
      _backend
          .UpdateClipAsync(
              _projectId,
              _trackId,
              _clipId,
              audioId: string.IsNullOrWhiteSpace(_prevAudioId) ? null : _prevAudioId,
              audioUrl: string.IsNullOrWhiteSpace(_prevAudioUrl) ? null : _prevAudioUrl,
              durationSeconds: _prevDurationSeconds,
              cancellationToken: CancellationToken.None)
          .ConfigureAwait(false)
          .GetAwaiter()
          .GetResult();
    }
    catch (Exception ex)
    {
      _log?.LogError(ex, "TranscriptClipAudioReplaceUndo");
      throw;
    }

    foreach (var link in _savedLinks)
      _linkage.AddOrUpdateLink(_project, CloneLink(link));

    ApplyLocalClip(_prevAudioId, _prevAudioUrl, _prevDurationSeconds);
    SetTranscriptTruthOnClip(TranscriptTruthState.Current);
    _dirty?.MarkProjectDirty("transcript_segment_regenerate_undo");
    _eventAggregator?.Publish(
        new TranscriptTruthStateChangedEvent(
            _sourcePanelId,
            _projectId,
            _trackId,
            _clipId,
            TranscriptTruthState.Current,
            "Undo restored clip audio and transcript linkage."));
    _eventAggregator?.Publish(
        new ClipAudioArtifactReplacedEvent(
            _sourcePanelId,
            _projectId,
            _trackId,
            _clipId,
            _prevAudioId,
            _prevAudioUrl,
            _prevDurationSeconds));
  }

  public void Redo()
  {
    try
    {
      _backend
          .UpdateClipAsync(
              _projectId,
              _trackId,
              _clipId,
              audioId: string.IsNullOrWhiteSpace(_newAudioId) ? null : _newAudioId,
              audioUrl: string.IsNullOrWhiteSpace(_newAudioUrl) ? null : _newAudioUrl,
              durationSeconds: _newDurationSeconds,
              cancellationToken: CancellationToken.None)
          .ConfigureAwait(false)
          .GetAwaiter()
          .GetResult();
    }
    catch (Exception ex)
    {
      _log?.LogError(ex, "TranscriptClipAudioReplaceRedo");
      throw;
    }

    _linkage.RemoveLinksByClipId(_project, _clipId);
    ApplyLocalClip(_newAudioId, _newAudioUrl, _newDurationSeconds);
    SetTranscriptTruthOnClip(TranscriptTruthState.StaleAfterClipRegeneration);
    _dirty?.MarkProjectDirty("transcript_segment_regenerate_redo");
    _eventAggregator?.Publish(
        new TranscriptTruthStateChangedEvent(
            _sourcePanelId,
            _projectId,
            _trackId,
            _clipId,
            TranscriptTruthState.StaleAfterClipRegeneration,
            "Redo re-applied new clip audio; transcript linkage removed (stale)."));
    _eventAggregator?.Publish(
        new ClipAudioArtifactReplacedEvent(
            _sourcePanelId,
            _projectId,
            _trackId,
            _clipId,
            _newAudioId,
            _newAudioUrl,
            _newDurationSeconds));
  }

  private void ApplyLocalClip(string audioId, string audioUrl, double durationSeconds)
  {
    var clip = FindClip(_project, _clipId);
    if (clip == null)
      return;
    clip.AudioId = audioId;
    clip.AudioUrl = audioUrl ?? string.Empty;
    clip.Duration = TimeSpan.FromSeconds(durationSeconds);
  }

  private void SetTranscriptTruthOnClip(TranscriptTruthState state)
  {
    var clip = FindClip(_project, _clipId);
    if (clip == null)
      return;
    clip.TranscriptTruth = state;
  }

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

  private static ClipTranscriptLink CloneLink(ClipTranscriptLink l) =>
      new()
      {
        ClipId = l.ClipId,
        TranscriptionId = l.TranscriptionId,
        AudioId = l.AudioId,
        SegmentIds = l.SegmentIds != null ? new List<string>(l.SegmentIds) : new List<string>(),
      };
}
