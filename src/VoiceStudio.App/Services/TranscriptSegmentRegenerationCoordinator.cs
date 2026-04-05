using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using VoiceStudio.App.Core.Models;
using VoiceStudio.App.Core.Services;
using VoiceStudio.App.Services.UndoableActions;
using VoiceStudio.Core.Events;
using VoiceStudio.Core.Models;
using VoiceStudio.Core.Services;
using VoiceStudio.Core.Transcription;

namespace VoiceStudio.App.Services;

/// <summary>
/// GAP-046: orchestrates segment regen job, clip apply, linkage removal, undo, and timeline sync event.
/// </summary>
public sealed class TranscriptSegmentRegenerationCoordinator
{
  private readonly ITranscriptRegenerationClient _regen;
  private readonly IJobProgressApiClient _jobs;
  private readonly IBackendClient _backend;
  private readonly ITranscriptionClient? _transcriptionClient;
  private readonly IClipTranscriptLinkageService _linkage;
  private readonly ITimelineSelectedProjectGate _gate;
  private readonly ITranscriptSegmentTargetResolver _resolver;
  private readonly IProjectSessionDirtyState? _dirty;
  private readonly UndoRedoService? _undo;
  private readonly IEventAggregator? _eventAggregator;
  private readonly IErrorLoggingService? _log;

  public TranscriptSegmentRegenerationCoordinator(
      ITranscriptRegenerationClient regen,
      IJobProgressApiClient jobs,
      IBackendClient backend,
      IClipTranscriptLinkageService linkage,
      ITimelineSelectedProjectGate gate,
      ITranscriptSegmentTargetResolver resolver,
      IProjectSessionDirtyState? dirty = null,
      UndoRedoService? undo = null,
      IEventAggregator? eventAggregator = null,
      IErrorLoggingService? log = null,
      ITranscriptionClient? transcriptionClient = null)
  {
    _regen = regen ?? throw new ArgumentNullException(nameof(regen));
    _jobs = jobs ?? throw new ArgumentNullException(nameof(jobs));
    _backend = backend ?? throw new ArgumentNullException(nameof(backend));
    _transcriptionClient = transcriptionClient;
    _linkage = linkage ?? throw new ArgumentNullException(nameof(linkage));
    _gate = gate ?? throw new ArgumentNullException(nameof(gate));
    _resolver = resolver ?? throw new ArgumentNullException(nameof(resolver));
    _dirty = dirty;
    _undo = undo;
    _eventAggregator = eventAggregator;
    _log = log;
  }

  /// <summary>
  /// Runs regeneration for the selected transcription segment. Returns operator-truth message only (no exceptions for expected failures).
  /// Optional <paramref name="jobProgress"/> + <paramref name="operationCorrelationId"/> emit lifecycle updates for Transcribe job status UI.
  /// </summary>
  public async Task<string?> TryExecuteAsync(
      TranscriptionResponse transcription,
      TranscriptionSegment segment,
      string sourcePanelId,
      string? replacementText,
      CancellationToken cancellationToken = default,
      IProgress<TranscriptRegenerationJobProgressReport>? jobProgress = null,
      string? operationCorrelationId = null,
      int? rangeEndInclusiveIndex = null)
  {
    if (transcription == null || segment == null)
      return "Select a transcription and segment first.";
    var project = _gate.SelectedProject;
    if (project == null || string.IsNullOrWhiteSpace(project.Id))
      return "No timeline project is active.";

    var r = _resolver.Resolve(transcription.Id, segment.Id, segment.Start, segment.End);
    if (r.Kind != TranscriptSegmentTargetResolutionKind.Resolved
        || string.IsNullOrWhiteSpace(r.ClipId)
        || string.IsNullOrWhiteSpace(r.TrackId))
      return r.Reason ?? "Could not resolve this segment to the timeline.";

    var clip = FindClip(project, r.ClipId);
    if (clip == null)
      return "The linked clip is not present on the project model; reload the timeline or save the project.";

    var start = new RegenerateSegmentStartRequest
    {
      ProjectId = project.Id,
      TrackId = r.TrackId,
      ClipId = r.ClipId,
      TranscriptionId = transcription.Id,
      SegmentId = segment.Id,
      ReplacementText = replacementText,
      ProfileId = string.IsNullOrWhiteSpace(clip.ProfileId) ? null : clip.ProfileId,
    };

    RegenerateSegmentJobStartResponse? accepted;
    try
    {
      accepted = await _regen.StartRegenerateSegmentAsync(start, cancellationToken).ConfigureAwait(false);
    }
    catch (Exception ex)
    {
      _log?.LogError(ex, "TranscriptRegenerationStart");
      return $"Regeneration could not start: {ex.Message}";
    }

    if (accepted == null || string.IsNullOrWhiteSpace(accepted.JobId))
      return "Regeneration did not return a job id.";

    ReportJobProgress(
        operationCorrelationId,
        jobProgress,
        accepted.JobId,
        string.IsNullOrWhiteSpace(accepted.Status) ? "pending" : accepted.Status,
        0,
        null,
        null);

    var terminal = await WaitForTerminalJobAsync(
            accepted.JobId,
            operationCorrelationId,
            jobProgress,
            cancellationToken)
        .ConfigureAwait(false);
    if (terminal == null)
      return "Regeneration timed out while waiting for the synthesis job.";

    if (string.Equals(terminal.Status, "failed", StringComparison.OrdinalIgnoreCase))
      return string.IsNullOrWhiteSpace(terminal.ErrorMessage)
          ? "Regeneration failed."
          : terminal.ErrorMessage;

    if (!string.Equals(terminal.Status, "completed", StringComparison.OrdinalIgnoreCase))
      return $"Regeneration ended with unexpected status: {terminal.Status}";

    var newAudioId = terminal.ResultId;
    if (string.IsNullOrWhiteSpace(newAudioId))
      return "Regeneration completed without an audio result id.";

    if (!TryGetMetadataString(terminal.Metadata, "audio_url", out var newUrl))
      newUrl = $"/api/voice/audio/{newAudioId}";
    if (!TryGetMetadataDouble(terminal.Metadata, "duration_seconds", out var newDur))
      newDur = clip.Duration.TotalSeconds;

    var prevAudioId = clip.AudioId;
    var prevUrl = clip.AudioUrl;
    var prevDur = clip.Duration.TotalSeconds;
    var savedLinks = _linkage
        .GetLinksForClip(project, r.ClipId)
        .Select(CopyLink)
        .Where(l => l != null)
        .Cast<ClipTranscriptLink>()
        .ToList();

    try
    {
      await _backend
          .UpdateClipAsync(
              project.Id,
              r.TrackId,
              r.ClipId,
              audioId: newAudioId,
              audioUrl: newUrl,
              durationSeconds: newDur,
              cancellationToken: cancellationToken)
          .ConfigureAwait(false);
    }
    catch (Exception ex)
    {
      _log?.LogError(ex, "TranscriptRegenApplyClip");
      ReportJobProgress(
          operationCorrelationId,
          jobProgress,
          accepted.JobId,
          "apply_failed",
          0,
          null,
          ex.Message);
      return $"Regeneration succeeded but applying the new audio to the clip failed: {ex.Message}";
    }

    var persistenceMessage = await TryPersistUpdatedTranscriptionAsync(
            transcription,
            segment,
            replacementText,
            rangeEndInclusiveIndex,
            cancellationToken)
        .ConfigureAwait(false);

    _linkage.RemoveLinksByClipId(project, r.ClipId);
    clip.AudioId = newAudioId;
    clip.AudioUrl = newUrl ?? string.Empty;
    clip.Duration = TimeSpan.FromSeconds(newDur);
    clip.TranscriptTruth = TranscriptTruthState.StaleAfterClipRegeneration;

    _dirty?.MarkProjectDirty("transcript_segment_regenerate");
    _eventAggregator?.Publish(
        new ClipAudioArtifactReplacedEvent(
            sourcePanelId,
            project.Id,
            r.TrackId,
            r.ClipId,
            newAudioId,
            newUrl ?? string.Empty,
            newDur));
    _eventAggregator?.Publish(
        new TranscriptTruthStateChangedEvent(
            sourcePanelId,
            project.Id,
            r.TrackId,
            r.ClipId,
            TranscriptTruthState.StaleAfterClipRegeneration,
            "Clip audio was replaced; transcript linkage was removed. Use Refresh transcript linkage when ready."));

    if (_undo != null)
    {
      var action = new TranscriptClipAudioReplaceUndoAction(
          _backend,
          _linkage,
          project,
          project.Id,
          r.TrackId,
          r.ClipId,
          prevAudioId,
          prevUrl ?? string.Empty,
          prevDur,
          newAudioId,
          newUrl ?? string.Empty,
          newDur,
          savedLinks,
          _dirty,
          _eventAggregator,
          sourcePanelId,
          _log);
      _undo.RegisterAction(action);
    }

    ReportJobProgress(
        operationCorrelationId,
        jobProgress,
        accepted.JobId,
        "session_succeeded",
        1,
        null,
        null);
    return persistenceMessage;
  }

  private static void ReportJobProgress(
      string? operationCorrelationId,
      IProgress<TranscriptRegenerationJobProgressReport>? jobProgress,
      string? jobId,
      string backendStatus,
      double progress,
      string? currentStep,
      string? errorMessage)
  {
    if (jobProgress == null || string.IsNullOrWhiteSpace(operationCorrelationId))
      return;
    jobProgress.Report(
        new TranscriptRegenerationJobProgressReport
        {
          OperationCorrelationId = operationCorrelationId,
          JobId = jobId,
          BackendStatus = backendStatus,
          Progress = progress,
          CurrentStep = currentStep,
          ErrorMessage = errorMessage,
        });
  }

  private static ClipTranscriptLink CopyLink(ClipTranscriptLink l) =>
      new()
      {
        ClipId = l.ClipId,
        TranscriptionId = l.TranscriptionId,
        AudioId = l.AudioId,
        SegmentIds = l.SegmentIds != null ? new List<string>(l.SegmentIds) : new List<string>(),
      };

  private async Task<string?> TryPersistUpdatedTranscriptionAsync(
      TranscriptionResponse transcription,
      TranscriptionSegment anchorSegment,
      string? replacementText,
      int? rangeEndInclusiveIndex,
      CancellationToken cancellationToken)
  {
    if (_transcriptionClient == null
        || string.IsNullOrWhiteSpace(transcription.Id)
        || transcription.Segments == null)
      return null;

    var updatedSegments = BuildUpdatedSegmentsForPersistence(
        transcription,
        anchorSegment,
        replacementText,
        rangeEndInclusiveIndex);
    if (updatedSegments == null)
      return null;

    try
    {
      var persisted = await _transcriptionClient
          .UpdateTranscriptionTextAsync(
              transcription.Id,
              BuildTranscriptionText(updatedSegments),
              updatedSegments,
              cancellationToken)
          .ConfigureAwait(false);

      if (persisted != null)
      {
        transcription.Text = persisted.Text;
        transcription.Segments = persisted.Segments;
      }

      return null;
    }
    catch (Exception ex)
    {
      _log?.LogError(ex, "TranscriptPersistAfterRegeneration");
      return $"Regeneration succeeded, but transcript persistence failed: {ex.Message}";
    }
  }

  private static List<TranscriptionSegment>? BuildUpdatedSegmentsForPersistence(
      TranscriptionResponse transcription,
      TranscriptionSegment anchorSegment,
      string? replacementText,
      int? rangeEndInclusiveIndex)
  {
    var trimmed = (replacementText ?? string.Empty).Trim();
    if (string.IsNullOrWhiteSpace(trimmed) || transcription.Segments == null)
      return null;

    var sourceSegments = transcription.Segments;
    var startIdx = sourceSegments.FindIndex(s => string.Equals(s.Id, anchorSegment.Id, StringComparison.Ordinal));
    if (startIdx < 0)
      return null;

    var endIdx = startIdx;
    if (rangeEndInclusiveIndex is int rangeEnd
        && rangeEnd >= startIdx
        && rangeEnd < sourceSegments.Count)
      endIdx = rangeEnd;

    var copy = new List<TranscriptionSegment>(sourceSegments.Count);
    for (var i = 0; i < sourceSegments.Count; i++)
    {
      var existing = sourceSegments[i];
      if (i < startIdx || i > endIdx)
      {
        copy.Add(existing);
        continue;
      }

      copy.Add(new TranscriptionSegment
      {
        Id = existing.Id,
        Start = existing.Start,
        End = existing.End,
        Text = i == startIdx ? trimmed : string.Empty,
        Words = existing.Words,
      });
    }

    return copy;
  }

  private static string BuildTranscriptionText(IReadOnlyList<TranscriptionSegment> segments)
  {
    var merged = new List<string>(segments.Count);
    foreach (var segment in segments)
    {
      var text = (segment.Text ?? string.Empty).Trim();
      if (!string.IsNullOrWhiteSpace(text))
        merged.Add(text);
    }

    return string.Join(" ", merged);
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

  private async Task<Job?> WaitForTerminalJobAsync(
      string jobId,
      string? operationCorrelationId,
      IProgress<TranscriptRegenerationJobProgressReport>? jobProgress,
      CancellationToken cancellationToken)
  {
    var deadline = DateTime.UtcNow.AddMinutes(4);
    var lastSig = (string?)null;
    while (DateTime.UtcNow < deadline)
    {
      cancellationToken.ThrowIfCancellationRequested();
      var j = await _jobs.GetJobAsync(jobId, cancellationToken).ConfigureAwait(false);
      if (j != null)
      {
        var isTerminal = string.Equals(j.Status, "completed", StringComparison.OrdinalIgnoreCase)
            || string.Equals(j.Status, "failed", StringComparison.OrdinalIgnoreCase)
            || string.Equals(j.Status, "cancelled", StringComparison.OrdinalIgnoreCase);
        var sig = $"{j.Status}|{j.Progress}|{j.CurrentStep}|{j.ErrorMessage}";
        if (isTerminal || !string.Equals(sig, lastSig, StringComparison.Ordinal))
        {
          lastSig = sig;
          ReportJobProgress(
              operationCorrelationId,
              jobProgress,
              jobId,
              j.Status ?? string.Empty,
              j.Progress,
              j.CurrentStep,
              j.ErrorMessage);
        }

        if (isTerminal)
          return j;
      }

      await Task.Delay(350, cancellationToken).ConfigureAwait(false);
    }

    ReportJobProgress(
        operationCorrelationId,
        jobProgress,
        jobId,
        "timeout",
        0,
        null,
        "Regeneration timed out while waiting for the synthesis job.");
    return null;
  }

  private static bool TryGetMetadataDouble(IReadOnlyDictionary<string, object>? meta, string key, out double value)
  {
    value = 0;
    if (meta == null || !meta.TryGetValue(key, out var o) || o == null)
      return false;
    if (o is JsonElement je && je.ValueKind == JsonValueKind.Number)
    {
      value = je.GetDouble();
      return true;
    }

    if (o is double d)
    {
      value = d;
      return true;
    }

    if (o is float f)
    {
      value = f;
      return true;
    }

    return double.TryParse(o.ToString(), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out value);
  }

  private static bool TryGetMetadataString(IReadOnlyDictionary<string, object>? meta, string key, out string? value)
  {
    value = null;
    if (meta == null || !meta.TryGetValue(key, out var o) || o == null)
      return false;
    if (o is JsonElement je && je.ValueKind == JsonValueKind.String)
    {
      value = je.GetString();
      return !string.IsNullOrWhiteSpace(value);
    }

    value = o.ToString();
    return !string.IsNullOrWhiteSpace(value);
  }
}
