// GOV-VOICESTUDIO-EDIT-APPLY-STALE-CONTEXT-EXPLAINABILITY-01 — deterministic operator copy for fail-closed retry / context jump.
using VoiceStudio.Core.Transcription;

namespace VoiceStudio.App.Services;

/// <summary>
/// Centralized prefixes and messages so context-jump and retry branches stay consistent.
/// </summary>
public static class TranscriptStaleContextExplainability
{
  public const string JumpPrefix = "Jump blocked:";
  public const string RetryPrefix = "Retry blocked:";

  public static string JumpNoTranscriptionId =>
      $"{JumpPrefix} this row has no transcription id.";

  public static string JumpProjectMismatch =>
      $"{JumpPrefix} active project differs from the project recorded for this row.";

  public static string JumpTranscriptionNotInSessionList =>
      $"{JumpPrefix} transcription for this row is not in the current session list.";

  public static string JumpNoSegmentTarget =>
      $"{JumpPrefix} this row does not identify a segment to open.";

  public static string JumpSegmentNotInTranscription =>
      $"{JumpPrefix} linked segment is no longer present in the selected transcription.";

  public static string JumpResolverNotRegistered =>
      $"{JumpPrefix} clip linkage resolver is not available.";

  public static string JumpClipMismatchRowVsResolve =>
      $"{JumpPrefix} resolved clip no longer matches the clip recorded by this row.";

  public static string JumpResolverFailure(TranscriptSegmentTargetResolution r)
  {
    return r.Kind switch
    {
      TranscriptSegmentTargetResolutionKind.InvalidInput =>
          string.IsNullOrWhiteSpace(r.Reason)
              ? $"{JumpPrefix} transcription and segment identifiers are required."
              : $"{JumpPrefix} {r.Reason}",
      TranscriptSegmentTargetResolutionKind.NoTimelineProject =>
          $"{JumpPrefix} no timeline project is active; open or select a project and try again.",
      TranscriptSegmentTargetResolutionKind.Unlinked =>
          string.IsNullOrWhiteSpace(r.Reason)
              ? $"{JumpPrefix} this segment is not linked to a timeline clip."
              : $"{JumpPrefix} {r.Reason}",
      TranscriptSegmentTargetResolutionKind.AmbiguousMultipleClips =>
          $"{JumpPrefix} multiple clips link this segment; narrow linkage to one clip before jumping.",
      _ =>
          string.IsNullOrWhiteSpace(r.Reason)
              ? $"{JumpPrefix} could not resolve this segment to the timeline."
              : $"{JumpPrefix} {r.Reason}",
    };
  }

  public static string RetryAnotherRegenerationInProgress =>
      $"{RetryPrefix} another regeneration is already in progress.";

  public static string RetryNoTranscriptionSelected =>
      $"{RetryPrefix} select the same transcription as this job, then try again.";

  public static string RetryTranscriptionMismatch =>
      $"{RetryPrefix} active transcription no longer matches the original attempt.";

  public static string RetryProjectMismatch =>
      $"{RetryPrefix} active project differs from the project captured when this job failed.";

  public static string RetrySegmentMissing =>
      $"{RetryPrefix} the segment for this job is no longer present in the transcription.";

  public static string RetryRangeTimingInvalidated =>
      $"{RetryPrefix} segment timing changed; edit and apply again from the transcript.";

  public static string RetryResolverUnavailable =>
      $"{RetryPrefix} clip linkage resolver is not available.";

  public static string RetrySegmentIdMissing =>
      $"{RetryPrefix} segment id is missing for this retry.";

  public static string RetryClipMismatch =>
      $"{RetryPrefix} resolved clip no longer matches the clip recorded for this job.";
}
