using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using VoiceStudio.App.Services;

namespace VoiceStudio.App.Tests.Services;

[TestClass]
public sealed class WorkspaceRestoreFailureToastSuppressorTests
{
  [TestInitialize]
  public void Setup() => WorkspaceRestoreFailureToastSuppressor.ResetForTests();

  [TestMethod]
  public void ShouldSuppressDuplicate_second_identical_toast_within_window_is_suppressed()
  {
    var t0 = new DateTime(2026, 4, 27, 12, 0, 0, DateTimeKind.Utc);
    Assert.IsFalse(WorkspaceRestoreFailureToastSuppressor.ShouldSuppressDuplicate("T", "M", t0, TimeSpan.FromSeconds(5)));
    Assert.IsTrue(WorkspaceRestoreFailureToastSuppressor.ShouldSuppressDuplicate("T", "M", t0.AddSeconds(1), TimeSpan.FromSeconds(5)));
  }

  [TestMethod]
  public void ShouldSuppressDuplicate_different_message_is_not_suppressed()
  {
    var t0 = new DateTime(2026, 4, 27, 12, 0, 0, DateTimeKind.Utc);
    Assert.IsFalse(WorkspaceRestoreFailureToastSuppressor.ShouldSuppressDuplicate("T", "M1", t0, TimeSpan.FromSeconds(5)));
    Assert.IsFalse(WorkspaceRestoreFailureToastSuppressor.ShouldSuppressDuplicate("T", "M2", t0.AddSeconds(1), TimeSpan.FromSeconds(5)));
  }
}
