using Microsoft.VisualStudio.TestTools.UnitTesting;
using VoiceStudio.Core.Models;
using VoiceStudio.Core.Services;

namespace VoiceStudio.App.Tests.Services;

[TestClass]
public sealed class TranscriptionJobOutcomeClassifierTests
{
  [TestMethod]
  public void RealCompleted_WhenStatusCompletedAndRealPerformed()
  {
    var r = new TranscriptionJobResponse
    {
      Status = "completed",
      IsSimulated = false,
      RealTranscriptionPerformed = true,
      Transcript = new TranscriptionResponse { Id = "t1" },
    };
    Assert.AreEqual(TranscriptionJobOutcome.RealCompleted, TranscriptionJobOutcomeClassifier.Classify(r));
  }

  [TestMethod]
  public void SimulatedCompleted_WhenStatusCompletedAndIsSimulated()
  {
    var r = new TranscriptionJobResponse
    {
      Status = "completed",
      IsSimulated = true,
      RealTranscriptionPerformed = true,
      Transcript = new TranscriptionResponse { Id = "t1" },
    };
    Assert.AreEqual(TranscriptionJobOutcome.SimulatedCompleted, TranscriptionJobOutcomeClassifier.Classify(r));
  }

  [TestMethod]
  public void Unavailable_WhenStatusUnavailable_BlockerPreserved()
  {
    var r = new TranscriptionJobResponse { Status = "unavailable", Blocker = "no engine" };
    var o = TranscriptionJobOutcomeClassifier.Classify(r);
    Assert.AreEqual(TranscriptionJobOutcome.Unavailable, o);
    Assert.AreEqual("no engine", r.Blocker);
  }

  [TestMethod]
  public void Failed_WhenStatusFailed_BlockerPreserved()
  {
    var r = new TranscriptionJobResponse { Status = "failed", Blocker = "boom" };
    var o = TranscriptionJobOutcomeClassifier.Classify(r);
    Assert.AreEqual(TranscriptionJobOutcome.Failed, o);
    Assert.AreEqual("boom", r.Blocker);
  }

  [TestMethod]
  public void InvalidCompleted_WhenTranscriptIsNull()
  {
    var r = new TranscriptionJobResponse { Status = "completed", IsSimulated = false, RealTranscriptionPerformed = true, Transcript = null };
    Assert.AreEqual(TranscriptionJobOutcome.InvalidCompleted, TranscriptionJobOutcomeClassifier.Classify(r));
  }

  [TestMethod]
  public void SimulatedCompleted_WhenCompletedTranscriptIdOnly_IsSimulated()
  {
    var r = new TranscriptionJobResponse
    {
      Status = "completed",
      TranscriptId = "t-hydrate",
      IsSimulated = true,
      RealTranscriptionPerformed = false,
      Transcript = null,
    };
    Assert.AreEqual(TranscriptionJobOutcome.SimulatedCompleted, TranscriptionJobOutcomeClassifier.Classify(r));
  }

  [TestMethod]
  public void RealCompleted_WhenCompletedTranscriptIdOnly_RealPerformed()
  {
    var r = new TranscriptionJobResponse
    {
      Status = "completed",
      TranscriptId = "t-hydrate",
      IsSimulated = false,
      RealTranscriptionPerformed = true,
      Transcript = null,
    };
    Assert.AreEqual(TranscriptionJobOutcome.RealCompleted, TranscriptionJobOutcomeClassifier.Classify(r));
  }

  [TestMethod]
  public void Unavailable_IsNotTreatedAsSuccess()
  {
    var r = new TranscriptionJobResponse { Status = "unavailable", Mode = "unavailable", Blocker = "x" };
    var o = TranscriptionJobOutcomeClassifier.Classify(r);
    Assert.AreNotEqual(TranscriptionJobOutcome.RealCompleted, o);
    Assert.AreNotEqual(TranscriptionJobOutcome.SimulatedCompleted, o);
  }
}
