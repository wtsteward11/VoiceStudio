using System;
using System.IO;
using System.Linq;
using System.Reflection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace VoiceStudio.App.Tests.Views;

[TestClass]
public sealed class Gap067Slice3Tests
{
  private static string FindRepoRoot()
  {
    foreach (var start in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory, Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? "" })
    {
      if (string.IsNullOrEmpty(start))
      {
        continue;
      }

      var dir = new DirectoryInfo(start);
      for (var i = 0; i < 16 && dir != null; i++, dir = dir.Parent)
      {
        var sln = Path.Combine(dir.FullName, "VoiceStudio.sln");
        if (File.Exists(sln))
        {
          return dir.FullName;
        }
      }
    }

    throw new InvalidOperationException("VoiceStudio.sln not found.");
  }

  private static string TaskbarProgressInteropPath =>
    Path.Combine(FindRepoRoot(), "src", "VoiceStudio.App", "Interop", "TaskbarProgressInterop.cs");

  private static string TaskbarProgressServicePath =>
    Path.Combine(FindRepoRoot(), "src", "VoiceStudio.App", "Services", "TaskbarProgressService.cs");

  private static string ITaskbarProgressServicePath =>
    Path.Combine(FindRepoRoot(), "src", "VoiceStudio.App", "Services", "ITaskbarProgressService.cs");

  private static string IShellProgressPublisherPath =>
    Path.Combine(FindRepoRoot(), "src", "VoiceStudio.App", "Services", "IShellProgressPublisher.cs");

  private static string ShellProgressCoordinatorPath =>
    Path.Combine(FindRepoRoot(), "src", "VoiceStudio.App", "Services", "ShellProgressCoordinator.cs");

  private static string AppServicesPath =>
    Path.Combine(FindRepoRoot(), "src", "VoiceStudio.App", "Services", "AppServices.cs");

  private static string MainWindowPath =>
    Path.Combine(FindRepoRoot(), "src", "VoiceStudio.App", "MainWindow.xaml.cs");

  [TestMethod]
  public void TaskbarProgressInterop_Declares_ITaskbarList3_Type()
  {
    var text = File.ReadAllText(TaskbarProgressInteropPath);
    StringAssert.Contains(text, "ITaskbarList3");
  }

  [TestMethod]
  public void TaskbarProgressInterop_Declares_TBPFLAG_Enum_With_Expected_Values()
  {
    var text = File.ReadAllText(TaskbarProgressInteropPath);
    StringAssert.Contains(text, "TbpFlag");
    StringAssert.Contains(text, "NoProgress");
    StringAssert.Contains(text, "Indeterminate");
    StringAssert.Contains(text, "Normal");
    StringAssert.Contains(text, "Error");
  }

  [TestMethod]
  public void TaskbarProgressInterop_Declares_CLSID_Constant()
  {
    var text = File.ReadAllText(TaskbarProgressInteropPath);
    StringAssert.Contains(text, "ClsidTaskbarList");
    StringAssert.Contains(text, "56FDF344-FD6D-11d0-958A-006097C9A090");
  }

  [TestMethod]
  public void TaskbarProgressInterop_Declares_SetProgressState_Method()
  {
    var text = File.ReadAllText(TaskbarProgressInteropPath);
    StringAssert.Contains(text, "SetProgressState");
  }

  [TestMethod]
  public void TaskbarProgressInterop_Declares_SetProgressValue_Method()
  {
    var text = File.ReadAllText(TaskbarProgressInteropPath);
    StringAssert.Contains(text, "SetProgressValue");
  }

  [TestMethod]
  public void TaskbarProgressService_Implements_ITaskbarProgressService()
  {
    var text = File.ReadAllText(TaskbarProgressServicePath);
    StringAssert.Contains(text, "TaskbarProgressService");
    StringAssert.Contains(text, "ITaskbarProgressService");
  }

  [TestMethod]
  public void IShellProgressPublisher_Declares_Expected_Methods()
  {
    var text = File.ReadAllText(IShellProgressPublisherPath);
    StringAssert.Contains(text, "ReportProgress");
    StringAssert.Contains(text, "ReportIndeterminate");
    StringAssert.Contains(text, "ReportError");
    StringAssert.Contains(text, "ReportComplete");
    StringAssert.Contains(text, "ReportCancelled");
  }

  [TestMethod]
  public void ShellProgressCoordinator_Implements_IShellProgressPublisher()
  {
    var text = File.ReadAllText(ShellProgressCoordinatorPath);
    StringAssert.Contains(text, "ShellProgressCoordinator");
    StringAssert.Contains(text, "IShellProgressPublisher");
  }

  [TestMethod]
  public void AppServices_Has_TryGetTaskbarProgressService_Accessor()
  {
    var text = File.ReadAllText(AppServicesPath);
    StringAssert.Contains(text, "TryGetTaskbarProgressService");
  }

  [TestMethod]
  public void TranscribeViewModel_Constructor_Accepts_IShellProgressPublisher()
  {
    var vmType = typeof(VoiceStudio.App.Views.Panels.TranscribeViewModel);
    var ctor = vmType.GetConstructors().Single();
    var hasShell = ctor.GetParameters().Any(p => p.ParameterType == typeof(VoiceStudio.App.Services.IShellProgressPublisher));
    Assert.IsTrue(hasShell, "TranscribeViewModel should accept optional IShellProgressPublisher.");
  }

  [TestMethod]
  public void TimelineViewModel_Constructor_Accepts_IShellProgressPublisher()
  {
    var vmType = typeof(VoiceStudio.App.Views.Panels.TimelineViewModel);
    var ctor = vmType.GetConstructors().Single();
    var hasShell = ctor.GetParameters().Any(p => p.ParameterType == typeof(VoiceStudio.App.Services.IShellProgressPublisher));
    Assert.IsTrue(hasShell, "TimelineViewModel should accept optional IShellProgressPublisher.");
  }

  [TestMethod]
  public void MainWindow_Declares_WireTaskbarProgressShell()
  {
    var text = File.ReadAllText(MainWindowPath);
    StringAssert.Contains(text, "WireTaskbarProgressShell");
    StringAssert.Contains(text, "TryGetTaskbarProgressService");
  }
}
