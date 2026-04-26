using System;
using System.IO;
using System.Reflection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace VoiceStudio.App.Tests.Views;

[TestClass]
public sealed class Gap008Slice33Tests
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

    private static string MainWindowPath =>
        Path.Combine(FindRepoRoot(), "src", "VoiceStudio.App", "MainWindow.xaml.cs");

    [TestMethod]
    public void MainWindow_workspace_splitter_uses_Slice_33_bridge()
    {
        var text = File.ReadAllText(MainWindowPath);
        StringAssert.Contains(text, "_workspaceSplitterShellBridge");
        StringAssert.Contains(text, "new MainWindowWorkspaceSplitterShellBridge(");
    }

    [TestMethod]
    public void MainWindow_creates_WorkspaceSplitter_ShellBridge_immediately_after_layout_save_Debouncer()
    {
        var text = File.ReadAllText(MainWindowPath);
        var idxW = text.IndexOf("new MainWindowWorkspaceSplitterShellBridge", StringComparison.Ordinal);
        var idxD = text.IndexOf("_layoutSaveDebouncer = new Debouncer", StringComparison.Ordinal);
        Assert.IsTrue(idxW >= 0, "MainWindowWorkspaceSplitterShellBridge construction expected.");
        Assert.IsTrue(idxD >= 0, "Debouncer construction expected.");
        Assert.IsTrue(
            idxW > idxD,
            "Workspace splitter shell bridge should be constructed after the layout save debouncer.");
    }
}
