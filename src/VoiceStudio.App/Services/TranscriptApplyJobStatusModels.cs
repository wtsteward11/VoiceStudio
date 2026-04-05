using System;
using System.Collections.Generic;
using CommunityToolkit.Mvvm.ComponentModel;

namespace VoiceStudio.App.Services;

/// <summary>
/// Operator-facing transcript apply/regenerate job status (GOV-VOICESTUDIO-EDIT-APPLY-JOB-STATUS-01).
/// </summary>
public enum TranscriptApplyOperatorJobStatus
{
  Queued,
  Running,
  Succeeded,
  Failed,
}

/// <summary>
/// Progress payload from <see cref="TranscriptSegmentRegenerationCoordinator"/> job polling and apply phases.
/// </summary>
public sealed class TranscriptRegenerationJobProgressReport
{
  public required string OperationCorrelationId { get; init; }
  public string? JobId { get; init; }
  public string BackendStatus { get; init; } = string.Empty;
  public double Progress { get; init; }
  public string? CurrentStep { get; init; }
  public string? ErrorMessage { get; init; }
}

/// <summary>
/// Maps backend job status and synthetic coordinator phases to operator labels and messages.
/// </summary>
public static class TranscriptApplyJobStatusMapper
{
  public static TranscriptApplyOperatorJobStatus MapToOperator(string backendStatus)
  {
    var s = (backendStatus ?? string.Empty).Trim().ToLowerInvariant();
    return s switch
    {
      "pending" => TranscriptApplyOperatorJobStatus.Queued,
      "running" or "paused" => TranscriptApplyOperatorJobStatus.Running,
      "completed" => TranscriptApplyOperatorJobStatus.Running,
      "session_succeeded" => TranscriptApplyOperatorJobStatus.Succeeded,
      "failed" or "cancelled" or "timeout" or "apply_failed" => TranscriptApplyOperatorJobStatus.Failed,
      _ => TranscriptApplyOperatorJobStatus.Running,
    };
  }

  public static string FormatOperatorLabel(TranscriptApplyOperatorJobStatus op) =>
      op switch
      {
        TranscriptApplyOperatorJobStatus.Queued => "Queued",
        TranscriptApplyOperatorJobStatus.Running => "Running",
        TranscriptApplyOperatorJobStatus.Succeeded => "Succeeded",
        TranscriptApplyOperatorJobStatus.Failed => "Failed",
        _ => op.ToString(),
      };

  public static string BuildStatusMessage(TranscriptRegenerationJobProgressReport r, TranscriptApplyOperatorJobStatus op)
  {
    var s = (r.BackendStatus ?? string.Empty).Trim().ToLowerInvariant();
    if (s == "session_succeeded")
      return "Regeneration complete; clip updated.";
    if (s == "apply_failed")
      return string.IsNullOrWhiteSpace(r.ErrorMessage)
          ? "Applying new audio to the clip failed."
          : r.ErrorMessage!;
    if (s == "timeout")
      return string.IsNullOrWhiteSpace(r.ErrorMessage)
          ? "Regeneration timed out while waiting for the synthesis job."
          : r.ErrorMessage!;
    if (s == "cancelled")
      return string.IsNullOrWhiteSpace(r.ErrorMessage) ? "Cancelled." : r.ErrorMessage!;
    if (s == "failed")
      return string.IsNullOrWhiteSpace(r.ErrorMessage) ? "Failed." : r.ErrorMessage!;
    if (s == "completed")
      return "Synthesis complete; applying to timeline…";
    if (s == "paused")
    {
      var step = r.CurrentStep;
      return string.IsNullOrWhiteSpace(step) ? "Paused." : $"{step} (paused)";
    }

    if (s == "pending")
      return "Queued for synthesis.";
    if (s == "running")
    {
      var step = r.CurrentStep;
      return string.IsNullOrWhiteSpace(step) ? "Synthesizing…" : step;
    }

    if (!string.IsNullOrWhiteSpace(r.ErrorMessage))
      return r.ErrorMessage!;
    return string.IsNullOrWhiteSpace(r.CurrentStep) ? string.Empty : r.CurrentStep!;
  }
}

/// <summary>One session-visible apply/regenerate job status row (Transcribe panel).</summary>
public sealed partial class TranscriptApplyJobStatusEntry : ObservableObject
{
  /// <summary>Anchor timing tolerance for retry preflight (seconds).</summary>
  public const double RetryAnchorTimingEpsilonSeconds = 1e-6;

  public TranscriptApplyJobStatusEntry(
      string operationId,
      TranscriptEditOperationKind operationKind,
      IReadOnlyList<string> segmentIds,
      string? clipId,
      DateTimeOffset createdUtc,
      string transcriptionId,
      string? projectId,
      string? replacementTextSnapshot,
      int? rangeEndInclusiveIndex,
      double anchorSegmentStart,
      double anchorSegmentEnd)
  {
    OperationId = operationId ?? throw new ArgumentNullException(nameof(operationId));
    OperationKind = operationKind;
    SegmentIds = segmentIds ?? throw new ArgumentNullException(nameof(segmentIds));
    ClipId = clipId;
    CreatedUtc = createdUtc;
    TranscriptionId = transcriptionId ?? throw new ArgumentNullException(nameof(transcriptionId));
    ProjectId = projectId;
    ReplacementTextSnapshot = replacementTextSnapshot;
    RangeEndInclusiveIndex = rangeEndInclusiveIndex;
    AnchorSegmentStart = anchorSegmentStart;
    AnchorSegmentEnd = anchorSegmentEnd;
    operatorStatus = TranscriptApplyOperatorJobStatus.Queued;
    statusMessage = "Starting…";
  }

  public string OperationId { get; }
  public TranscriptEditOperationKind OperationKind { get; }
  public IReadOnlyList<string> SegmentIds { get; }
  public string? ClipId { get; }
  public DateTimeOffset CreatedUtc { get; }

  /// <summary>Transcription that was active when the attempt was created; retry requires the same selection.</summary>
  public string TranscriptionId { get; }

  /// <summary>Timeline project id when the attempt was created; when set, retry requires the same active project.</summary>
  public string? ProjectId { get; }

  /// <summary>Replacement text passed to regeneration; null for audio-only regenerate.</summary>
  public string? ReplacementTextSnapshot { get; }

  /// <summary>Inclusive segment index for multi-segment apply; null for single-segment.</summary>
  public int? RangeEndInclusiveIndex { get; }

  public double AnchorSegmentStart { get; }
  public double AnchorSegmentEnd { get; }

  /// <summary>Operator may retry failed apply/regenerate rows using the frozen snapshot.</summary>
  public bool CanShowRetry =>
      OperatorStatus == TranscriptApplyOperatorJobStatus.Failed
      && OperationKind != TranscriptEditOperationKind.FillerCleanupDraft
      && !string.IsNullOrWhiteSpace(TranscriptionId)
      && SegmentIds.Count > 0
      && !string.IsNullOrWhiteSpace(SegmentIds[0]);

  [ObservableProperty]
  private string? jobId;

  [ObservableProperty]
  private TranscriptApplyOperatorJobStatus operatorStatus;

  [ObservableProperty]
  private string? statusMessage;

  [ObservableProperty]
  private double jobProgress;

  [ObservableProperty]
  private string? currentStep;

  [ObservableProperty]
  private DateTimeOffset? completedUtc;

  public string OperationKindLabel => OperationKind switch
  {
    TranscriptEditOperationKind.RegenerateSegment => "Regenerate",
    TranscriptEditOperationKind.SingleSegmentApply => "Apply (1 segment)",
    TranscriptEditOperationKind.MultiSegmentRangeApply => "Apply (range)",
    TranscriptEditOperationKind.FillerCleanupDraft => "Filler cleanup (draft)",
    _ => OperationKind.ToString(),
  };

  public string SegmentSummary =>
      SegmentIds.Count == 0
          ? "—"
          : SegmentIds.Count <= 2
              ? string.Join(", ", SegmentIds)
              : $"{SegmentIds[0]} +{SegmentIds.Count - 1}";

  public string OperatorStatusLabel => TranscriptApplyJobStatusMapper.FormatOperatorLabel(OperatorStatus);

  public string StatusSummaryLine =>
      $"{OperatorStatusLabel} · {OperationKindLabel} · segments {SegmentSummary}"
      + (string.IsNullOrWhiteSpace(ClipId) ? string.Empty : $" · clip {ClipId}")
      + $" · {(StatusMessage ?? string.Empty)}"
      + (CanShowRetry ? " · Retry available." : string.Empty);

  partial void OnOperatorStatusChanged(TranscriptApplyOperatorJobStatus value)
  {
    OnPropertyChanged(nameof(OperatorStatusLabel));
    OnPropertyChanged(nameof(CanShowRetry));
    OnPropertyChanged(nameof(StatusSummaryLine));
  }

  partial void OnStatusMessageChanged(string? value) => OnPropertyChanged(nameof(StatusSummaryLine));

  partial void OnJobIdChanged(string? value) => OnPropertyChanged(nameof(StatusSummaryLine));

  partial void OnCurrentStepChanged(string? value) => OnPropertyChanged(nameof(StatusSummaryLine));
}
