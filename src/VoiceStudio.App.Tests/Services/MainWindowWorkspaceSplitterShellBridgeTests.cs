using System;
using System.IO;
using System.Reflection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace VoiceStudio.App.Tests.Services;

[TestClass]
public sealed class MainWindowWorkspaceSplitterShellBridgeTests
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

    private static string BridgePath =>
        Path.Combine(FindRepoRoot(), "src", "VoiceStudio.App", "Services", "MainWindowWorkspaceSplitterShellBridge.cs");

    [TestMethod]
    public void Workspace_splitter_bridge_does_not_reference_unrelated_Gap008_shell_bridges()
    {
        var text = File.ReadAllText(BridgePath);
        Assert.IsFalse(text.Contains("MainWindowImportWorkflowShellBridge", StringComparison.Ordinal));
        Assert.IsFalse(text.Contains("MainWindowShellChromeShellBridge", StringComparison.Ordinal));
        Assert.IsFalse(text.Contains("IBackendClient", StringComparison.Ordinal));
    }

    [TestMethod]
    public void Workspace_splitter_bridge_ctor_validates_findName_and_layout_save()
    {
        var text = File.ReadAllText(BridgePath);
        StringAssert.Contains(text, "ArgumentNullException.ThrowIfNull(findNameOnContent)");
        StringAssert.Contains(text, "ArgumentNullException.ThrowIfNull(requestLayoutSaveOnPointerRelease)");
    }

    [TestMethod]
    public void Workspace_splitter_bridge_resolves_WorkspaceGrid_by_name()
    {
        var text = File.ReadAllText(BridgePath);
        StringAssert.Contains(text, "WorkspaceGrid");
        StringAssert.Contains(text, "VerticalSplitter1");
    }
}
