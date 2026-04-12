using System;
using System.IO;
using System.Linq;
using System.Reflection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using VoiceStudio.App.Services;

namespace VoiceStudio.App.Tests.Views;

/// <summary>
/// GAP-067 slice 7: cold-start timing artifact, lazy recents, deferred init registration, status bar monitoring deferral.
/// </summary>
[TestClass]
public sealed class Gap067Slice7Tests
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

  [TestMethod]
  public void ColdStartTimingArtifact_MinimalJson_PassesSchemaValidation()
  {
    var json = ColdStartTimingCollector.BuildMinimalValidArtifactJsonForTests();
    Assert.IsTrue(ColdStartTimingCollector.ValidateArtifactJson(json, out var err), err);
  }

  [TestMethod]
  public void DeferredServiceInitializer_DefaultIncludesExpectedServices()
  {
    var names = DeferredServiceInitializer.GetDefaultRegisteredServiceNames().ToArray();
    CollectionAssert.Contains(names, "PluginDiscovery");
    CollectionAssert.Contains(names, "RecentProjectsWarmup");
    CollectionAssert.Contains(names, "CrashRecoveryCheck");
    CollectionAssert.Contains(names, "BackendHealthCheck");
    CollectionAssert.Contains(names, "StartupDiagnostics");
  }

  [TestMethod]
  public void RecentProjectsService_DoesNotLoadInConstructor()
  {
    var svc = new RecentProjectsService();
    Assert.IsFalse(svc.IsRecentDataLoaded, "Ctor must not perform sync file load.");
    svc.EnsureRecentDataLoaded();
    Assert.IsTrue(svc.IsRecentDataLoaded);
  }

  [TestMethod]
  public void StatusBarCoordinator_Subscribe_DoesNotCallStartBackendMonitoring()
  {
    var path = Path.Combine(FindRepoRoot(), "src", "VoiceStudio.App", "Services", "StatusBarCoordinator.cs");
    Assert.IsTrue(File.Exists(path), "Expected StatusBarCoordinator.cs at " + path);
    var text = File.ReadAllText(path);
    var sub = text.IndexOf("public void Subscribe(", StringComparison.Ordinal);
    var next = text.IndexOf("public void StartBackendHealthMonitoring", sub, StringComparison.Ordinal);
    Assert.IsTrue(sub >= 0 && next > sub, "Expected Subscribe before StartBackendHealthMonitoring.");
    var subscribeRegion = text.Substring(sub, next - sub);
    Assert.IsFalse(
      subscribeRegion.Contains("StartBackendMonitoring()", StringComparison.Ordinal),
      "Subscribe must not call StartBackendMonitoring; defer via StartBackendHealthMonitoring.");
  }

  [TestMethod]
  public void JumpListService_HasDeferredInitialRebuild()
  {
    var path = Path.Combine(FindRepoRoot(), "src", "VoiceStudio.App", "Services", "JumpListService.cs");
    Assert.IsTrue(File.Exists(path), "Expected JumpListService.cs at " + path);
    var text = File.ReadAllText(path);
    StringAssert.Contains(text, "ScheduleInitialRebuildAfterDelay");
    StringAssert.Contains(text, "DispatcherQueuePriority.Low");
  }
}
