#nullable enable

using System;
using System.IO;
using System.Reflection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using VoiceStudio.App.Services;

namespace VoiceStudio.App.Tests.Services;

/// <summary>
/// GAP-014: proves the legacy parallel workspace DI stack and deprecated WorkspaceManager type
/// are not part of the live shell authority surface (PanelStateService + MainWindow orchestration).
/// </summary>
[TestClass]
public sealed class WorkspaceAuthoritySeamTests
{
  [TestMethod]
  public void AppServices_DoesNotExposeLegacyWorkspaceStackAccessors()
  {
    var t = typeof(AppServices);
    Assert.IsNull(t.GetMethod("GetWorkspaceService", BindingFlags.Public | BindingFlags.Static));
    Assert.IsNull(t.GetMethod("TryGetWorkspaceService", BindingFlags.Public | BindingFlags.Static));
    Assert.IsNull(t.GetMethod("GetLayoutService", BindingFlags.Public | BindingFlags.Static));
    Assert.IsNull(t.GetMethod("TryGetLayoutService", BindingFlags.Public | BindingFlags.Static));
  }

  [TestMethod]
  public void AppServices_Source_DoesNotRegisterLegacyWorkspaceDi()
  {
    var root = FindRepositoryRoot();
    var path = Path.Combine(root, "src", "VoiceStudio.App", "Services", "AppServices.cs");
    Assert.IsTrue(File.Exists(path), $"Expected {path}");
    var text = File.ReadAllText(path);
    Assert.IsFalse(text.Contains("AddSingleton<IWorkspaceService", StringComparison.Ordinal), "IWorkspaceService must not be registered in DI.");
    Assert.IsFalse(text.Contains("AddSingleton<ILayoutService", StringComparison.Ordinal), "ILayoutService must not be registered in DI.");
  }

  [TestMethod]
  public void WorkspaceManager_DeprecatedTypeFile_Removed()
  {
    var root = FindRepositoryRoot();
    var path = Path.Combine(root, "src", "VoiceStudio.App", "Features", "Workspaces", "WorkspaceManager.cs");
    Assert.IsFalse(File.Exists(path), $"Deprecated {path} must be removed (GAP-014).");
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

    var dir = new DirectoryInfo(asmDir);
    for (var d = dir; d != null; d = d.Parent)
    {
      if (File.Exists(Path.Combine(d.FullName, "VoiceStudio.sln")))
        return d.FullName;
    }

    throw new InvalidOperationException($"Could not locate VoiceStudio.sln starting from {asmDir}.");
  }
}
