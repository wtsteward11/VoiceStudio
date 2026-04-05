using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using VoiceStudio.App.Services;

namespace VoiceStudio.App.Tests.Services;

[TestClass]
public sealed class MultitrackRecoveryOperatorCopyTests
{
  [TestMethod]
  public void ContinuationGuidance_NullOrNoFailures_ReturnsNull()
  {
    Assert.IsNull(MultitrackRecoveryOperatorCopy.ContinuationGuidanceAfterRestore(null));
    var ok = new MultitrackRecoveryPayload
    {
      Legs = new List<MultitrackRecoveryLegRecord>
      {
        new()
        {
          TrackId = "t1",
          InputSourceId = "m1",
          Status = MultitrackRecoveryLegStatus.Completed,
        },
      },
    };
    Assert.IsNull(MultitrackRecoveryOperatorCopy.ContinuationGuidanceAfterRestore(ok));
  }

  [TestMethod]
  public void ContinuationGuidance_WithFailedLeg_ReturnsGuidance()
  {
    var p = new MultitrackRecoveryPayload
    {
      Legs = new List<MultitrackRecoveryLegRecord>
      {
        new()
        {
          TrackId = "t1",
          InputSourceId = "m1",
          Status = MultitrackRecoveryLegStatus.Completed,
        },
        new()
        {
          TrackId = "t2",
          InputSourceId = "m2",
          Status = MultitrackRecoveryLegStatus.Failed,
        },
      },
    };
    var g = MultitrackRecoveryOperatorCopy.ContinuationGuidanceAfterRestore(p);
    Assert.IsNotNull(g);
    StringAssert.Contains(g, "Recording panel");
  }
}
