using System;
using System.IO;
using System.Reflection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace VoiceStudio.App.Tests.Views;

[TestClass]
public sealed class Gap008Slice31Tests
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
    public void MainWindow_ImportAudioFile_delegates_to_Slice_31_import_workflow_bridge()
    {
        var text = File.ReadAllText(MainWindowPath);
        StringAssert.Contains(text, "_importWorkflowShellBridge");
        StringAssert.Contains(text, "_importWorkflowShellBridge.ImportAudioFile(");
    }

    [TestMethod]
    public void MainWindow_creates_ImportWorkflow_ShellBridge_immediately_after_GlobalTransport_bridge()
    {
        var text = File.ReadAllText(MainWindowPath);
        var idxI = text.IndexOf("new MainWindowImportWorkflowShellBridge", StringComparison.Ordinal);
        var idxG = text.IndexOf("new MainWindowGlobalTransportShellBridge", StringComparison.Ordinal);
        Assert.IsTrue(idxI >= 0, "MainWindowImportWorkflowShellBridge construction expected.");
        Assert.IsTrue(idxG >= 0, "MainWindowGlobalTransportShellBridge construction expected.");
        Assert.IsTrue(
            idxI > idxG,
            "Import workflow shell bridge should be constructed after the global transport shell bridge.");
    }
}
