#nullable enable

using System;
using System.IO;
using System.Reflection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using VoiceStudio.App.Services;

namespace VoiceStudio.App.Tests.Services;

/// <summary>
/// GAP-070: shell / user-preference persistence boundary — deterministic restore markers, merge-save gate, GAP-014 DI relapse guard.
/// </summary>
[TestClass]
public sealed class ShellPersistenceAuthoritySeamTests
{
  [TestMethod]
  public void MainWindow_Workspaces_Source_ContainsGap070RestoreOrderMarkers()
  {
    var root = FindRepositoryRoot();
    var path = Path.Combine(root, "src", "VoiceStudio.App", "MainWindow.Workspaces.cs");
    Assert.IsTrue(File.Exists(path), $"Expected {path}");
    var text = File.ReadAllText(path);
    var i1 = text.IndexOf("GAP-070-order-1", StringComparison.Ordinal);
    var i2 = text.IndexOf("GAP-070-order-2", StringComparison.Ordinal);
    Assert.IsTrue(i1 >= 0, "GAP-070-order-1 marker missing from MainWindow.Workspaces.cs");
    Assert.IsTrue(i2 >= 0, "GAP-070-order-2 marker missing from MainWindow.Workspaces.cs");
    Assert.IsTrue(i1 < i2, "Restore order markers out of sequence (order-1 must precede order-2).");
  }

  [TestMethod]
  public void PanelStateService_Source_SerializesWorkspaceSettingsSave()
  {
    var root = FindRepositoryRoot();
    var path = Path.Combine(root, "src", "VoiceStudio.App", "Services", "PanelStateService.cs");
    Assert.IsTrue(File.Exists(path), $"Expected {path}");
    var text = File.ReadAllText(path);
    Assert.IsTrue(text.Contains("_workspaceSettingsSaveGate", StringComparison.Ordinal), "GAP-070 merge-save gate field missing.");
    Assert.IsTrue(text.Contains("SaveCurrentWorkspaceAsync", StringComparison.Ordinal));
    Assert.IsTrue(text.Contains("WaitAsync", StringComparison.Ordinal), "SaveCurrentWorkspaceAsync must WaitAsync on the save gate.");
  }

  [TestMethod]
  public void AppServices_Source_StillBlocksLegacyWorkspaceDi_Gap014Gap070()
  {
    var t = typeof(AppServices);
    Assert.IsNull(t.GetMethod("GetWorkspaceService", BindingFlags.Public | BindingFlags.Static));
    Assert.IsNull(t.GetMethod("TryGetWorkspaceService", BindingFlags.Public | BindingFlags.Static));
    Assert.IsNull(t.GetMethod("GetLayoutService", BindingFlags.Public | BindingFlags.Static));
    Assert.IsNull(t.GetMethod("TryGetLayoutService", BindingFlags.Public | BindingFlags.Static));

    var root = FindRepositoryRoot();
    var path = Path.Combine(root, "src", "VoiceStudio.App", "Services", "AppServices.cs");
    var src = File.ReadAllText(path);
    Assert.IsFalse(src.Contains("AddSingleton<IWorkspaceService", StringComparison.Ordinal));
    Assert.IsFalse(src.Contains("AddSingleton<ILayoutService", StringComparison.Ordinal));
  }

  [TestMethod]
  public void PanelStateService_ImplementsUnifiedWorkspaceContract()
  {
    Assert.IsTrue(typeof(IUnifiedWorkspaceService).IsAssignableFrom(typeof(PanelStateService)));
  }

  private static string FindRepositoryRoot()
  {
    var asmDir = Path.GetDirectoryName(typeof(PanelStateService).Assembly.Location);
    if (string.IsNullOrEmpty(asmDir))
      throw new InvalidOperationException("PanelStateService assembly location is empty.");

    for (var d = new DirectoryInfo(asmDir); d != null; d = d.Parent)
    {
      if (File.Exists(Path.Combine(d.FullName, "VoiceStudio.sln")))
        return d.FullName;
    }

    throw new InvalidOperationException($"Could not locate VoiceStudio.sln starting from {asmDir}.");
  }
}
