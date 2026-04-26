using System;
using System.IO;
using System.Reflection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace VoiceStudio.App.Tests.Views;

[TestClass]
public sealed class Gap008Slice32Tests
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
    public void MainWindow_Shell_chrome_uses_Slice_32_bridge_for_backdrop_and_titlebar()
    {
        var text = File.ReadAllText(MainWindowPath);
        StringAssert.Contains(text, "_shellChromeShellBridge");
        StringAssert.Contains(text, "new MainWindowShellChromeShellBridge(");
    }

    [TestMethod]
    public void MainWindow_creates_ShellChrome_ShellBridge_immediately_after_ImportWorkflow_bridge()
    {
        var text = File.ReadAllText(MainWindowPath);
        var idxC = text.IndexOf("new MainWindowShellChromeShellBridge", StringComparison.Ordinal);
        var idxI = text.IndexOf("new MainWindowImportWorkflowShellBridge", StringComparison.Ordinal);
        Assert.IsTrue(idxC >= 0, "MainWindowShellChromeShellBridge construction expected.");
        Assert.IsTrue(idxI >= 0, "MainWindowImportWorkflowShellBridge construction expected.");
        Assert.IsTrue(
            idxC > idxI,
            "Shell chrome shell bridge should be constructed after the import workflow shell bridge.");
    }
}
