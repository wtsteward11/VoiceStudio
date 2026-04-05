using Microsoft.VisualStudio.TestTools.UnitTesting;
using VoiceStudio.App.Services;

namespace VoiceStudio.App.Tests.Services;

[TestClass]
public sealed class TranscriptApplyJobStatusMapperTests
{
  [TestMethod]
  public void MapToOperator_Pending_IsQueued()
  {
    Assert.AreEqual(TranscriptApplyOperatorJobStatus.Queued, TranscriptApplyJobStatusMapper.MapToOperator("pending"));
  }

  [TestMethod]
  public void MapToOperator_CompletedJob_IsRunning_UntilSessionSucceeds()
  {
    Assert.AreEqual(TranscriptApplyOperatorJobStatus.Running, TranscriptApplyJobStatusMapper.MapToOperator("completed"));
  }

  [TestMethod]
  public void MapToOperator_SessionSucceeded_IsSucceeded()
  {
    Assert.AreEqual(TranscriptApplyOperatorJobStatus.Succeeded, TranscriptApplyJobStatusMapper.MapToOperator("session_succeeded"));
  }

  [TestMethod]
  public void MapToOperator_Timeout_IsFailed()
  {
    Assert.AreEqual(TranscriptApplyOperatorJobStatus.Failed, TranscriptApplyJobStatusMapper.MapToOperator("timeout"));
  }

  [TestMethod]
  public void MapToOperator_Cancelled_IsFailed()
  {
    Assert.AreEqual(TranscriptApplyOperatorJobStatus.Failed, TranscriptApplyJobStatusMapper.MapToOperator("cancelled"));
  }

  [TestMethod]
  public void MapToOperator_ApplyFailed_IsFailed()
  {
    Assert.AreEqual(TranscriptApplyOperatorJobStatus.Failed, TranscriptApplyJobStatusMapper.MapToOperator("apply_failed"));
  }

  [TestMethod]
  public void BuildStatusMessage_Completed_IndicatesApplying()
  {
    var r = new TranscriptRegenerationJobProgressReport
    {
      OperationCorrelationId = "op",
      BackendStatus = "completed",
    };
    var msg = TranscriptApplyJobStatusMapper.BuildStatusMessage(
        r,
        TranscriptApplyOperatorJobStatus.Running);
    StringAssert.Contains(msg, "timeline");
  }
}
