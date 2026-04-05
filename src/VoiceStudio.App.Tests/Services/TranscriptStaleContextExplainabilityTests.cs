using Microsoft.VisualStudio.TestTools.UnitTesting;
using VoiceStudio.App.Services;
using VoiceStudio.Core.Transcription;

namespace VoiceStudio.App.Tests.Services;

[TestClass]
public sealed class TranscriptStaleContextExplainabilityTests
{
  [TestMethod]
  public void JumpResolverFailure_InvalidInput_UsesReason()
  {
    var r = TranscriptSegmentTargetResolution.Failure(
        TranscriptSegmentTargetResolutionKind.InvalidInput,
        "tr1",
        0,
        1,
        "Transcription id and segment id are required.");
    var msg = TranscriptStaleContextExplainability.JumpResolverFailure(r);
    StringAssert.StartsWith(msg, TranscriptStaleContextExplainability.JumpPrefix);
    StringAssert.Contains(msg, "Transcription id and segment id");
  }

  [TestMethod]
  public void JumpResolverFailure_NoTimelineProject_IsExplicit()
  {
    var r = TranscriptSegmentTargetResolution.Failure(
        TranscriptSegmentTargetResolutionKind.NoTimelineProject,
        "tr1",
        0,
        1,
        "ignored");
    var msg = TranscriptStaleContextExplainability.JumpResolverFailure(r);
    StringAssert.Contains(msg, "timeline project");
  }

  [TestMethod]
  public void JumpResolverFailure_Unlinked_ReusesResolverReason()
  {
    var r = TranscriptSegmentTargetResolution.Failure(
        TranscriptSegmentTargetResolutionKind.Unlinked,
        "tr1",
        0,
        1,
        "The linked clip no longer exists in the project.");
    var msg = TranscriptStaleContextExplainability.JumpResolverFailure(r);
    StringAssert.Contains(msg, "linked clip");
  }

  [TestMethod]
  public void JumpResolverFailure_Ambiguous_IsExplicit()
  {
    var r = TranscriptSegmentTargetResolution.Failure(
        TranscriptSegmentTargetResolutionKind.AmbiguousMultipleClips,
        "tr1",
        0,
        1,
        "Multiple clips");
    var msg = TranscriptStaleContextExplainability.JumpResolverFailure(r);
    StringAssert.Contains(msg, "multiple clips link");
  }

  [TestMethod]
  public void RetryConstants_UseBlockedPrefix()
  {
    StringAssert.StartsWith(TranscriptStaleContextExplainability.RetryProjectMismatch, TranscriptStaleContextExplainability.RetryPrefix);
    StringAssert.StartsWith(TranscriptStaleContextExplainability.RetryClipMismatch, TranscriptStaleContextExplainability.RetryPrefix);
  }
}
