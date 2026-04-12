using System;
using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using VoiceStudio.App.Services;

namespace VoiceStudio.App.Tests.Services;

[TestClass]
public sealed class ShellProgressCoordinatorSeamTests
{
  private sealed class RecordingTaskbarProgress : ITaskbarProgressService
  {
    public List<string> Calls { get; } = new();

    public void SetWindowHandle(IntPtr hwnd) => Calls.Add($"SetWindowHandle:{hwnd}");

    public void SetNormal(double progress01) => Calls.Add($"SetNormal:{progress01:R}");

    public void SetIndeterminate() => Calls.Add("SetIndeterminate");

    public void SetError() => Calls.Add("SetError");

    public void Clear() => Calls.Add("Clear");

    public void Dispose() => Calls.Add("Dispose");
  }

  [TestMethod]
  public void ReportProgress_FirstSource_CallsSetNormal()
  {
    var taskbar = new RecordingTaskbarProgress();
    var coord = new ShellProgressCoordinator(taskbar);

    coord.ReportProgress("a", 0.42);

    StringAssert.Contains(string.Join(";", taskbar.Calls), "SetNormal:0.42");
  }

  [TestMethod]
  public void ReportComplete_CurrentSource_CallsClear()
  {
    var taskbar = new RecordingTaskbarProgress();
    var coord = new ShellProgressCoordinator(taskbar);

    coord.ReportProgress("a", 0.1);
    coord.ReportComplete("a");

    Assert.IsTrue(taskbar.Calls.Contains("Clear"));
  }

  [TestMethod]
  public void ReportError_CurrentSource_CallsSetError_ThenClear()
  {
    var taskbar = new RecordingTaskbarProgress();
    var coord = new ShellProgressCoordinator(taskbar);

    coord.ReportProgress("a", 0.2);
    coord.ReportError("a");

    var joined = string.Join(";", taskbar.Calls);
    Assert.IsTrue(joined.Contains("SetError"), joined);
    Assert.IsTrue(joined.Contains("Clear"), joined);
  }

  [TestMethod]
  public void SecondSource_WhileFirstActive_IsQueued_NotImmediatelyApplied()
  {
    var taskbar = new RecordingTaskbarProgress();
    var coord = new ShellProgressCoordinator(taskbar);

    coord.ReportProgress("first", 0.1);
    coord.ReportProgress("second", 0.99);

    var joined = string.Join(";", taskbar.Calls);
    Assert.IsFalse(joined.Contains("SetNormal:0.99"), "Second source must not drive taskbar while first is foreground.");
  }

  [TestMethod]
  public void AfterFirstComplete_PendingSecondSource_BecomesForeground()
  {
    var taskbar = new RecordingTaskbarProgress();
    var coord = new ShellProgressCoordinator(taskbar);

    coord.ReportProgress("first", 0.1);
    coord.ReportProgress("second", 0.5);
    coord.ReportComplete("first");

    var joined = string.Join(";", taskbar.Calls);
    Assert.IsTrue(joined.Contains("Clear"), joined);
    Assert.IsTrue(joined.Contains("SetNormal:0.5"), joined);
  }

  [TestMethod]
  public void ReportCancelled_CurrentSource_PromotesNextPending()
  {
    var taskbar = new RecordingTaskbarProgress();
    var coord = new ShellProgressCoordinator(taskbar);

    coord.ReportProgress("first", 0.2);
    coord.ReportProgress("second", 0.6);
    coord.ReportCancelled("first");

    var joined = string.Join(";", taskbar.Calls);
    Assert.IsTrue(joined.Contains("Clear"), joined);
    Assert.IsTrue(joined.Contains("SetNormal:0.6"), joined);
  }
}
