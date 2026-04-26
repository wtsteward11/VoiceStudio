using System;
using System.IO;
using System.Reflection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace VoiceStudio.App.Tests.Views;

[TestClass]
public sealed class Gap008Slice14Tests
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
    public void MainWindow_Loaded_bootstrap_hooks_delegate_file_activation_to_slice14_bridge()
    {
        var text = File.ReadAllText(MainWindowPath);
        StringAssert.Contains(text, "_fileActivationShellBridge");
        StringAssert.Contains(text, "TryDispatchPendingFileActivation = () => _fileActivationShellBridge.TryDispatchPendingFileActivation()");
    }

    [TestMethod]
    public void MainWindow_constructed_file_activation_bridge_after_project_workflow_coordinator()
    {
        var text = File.ReadAllText(MainWindowPath);
        var idxCoordinator = text.IndexOf("ProjectWorkflowCoordinator Created", StringComparison.Ordinal);
        var idxFile = text.IndexOf("MainWindowFileActivationShellBridge Created", StringComparison.Ordinal);
        Assert.IsTrue(idxCoordinator >= 0, "Expected project workflow coordinator profiler checkpoint.");
        Assert.IsTrue(idxFile >= 0, "Expected file activation shell bridge profiler checkpoint.");
        Assert.IsTrue(idxCoordinator < idxFile, "File activation bridge should construct after coordinator exists.");
    }

    [TestMethod]
    public void MainWindow_does_not_embed_private_file_activation_dispatch_methods()
    {
        var text = File.ReadAllText(MainWindowPath);
        Assert.IsFalse(
            text.Contains("private void TryDispatchPendingFileActivation", StringComparison.Ordinal),
            "File activation dispatch should live on MainWindowFileActivationShellBridge, not MainWindow.");
        Assert.IsFalse(
            text.Contains("RunFileActivationPendingAsync", StringComparison.Ordinal),
            "RunFileActivationPendingAsync should not remain on MainWindow.");
    }

    [TestMethod]
    public void MainWindow_retains_jump_list_dispatch_on_window_not_absorbed_by_file_activation_bridge()
    {
        var text = File.ReadAllText(MainWindowPath);
        StringAssert.Contains(text, "TryDispatchPendingJumpListActivation");
        StringAssert.Contains(text, "_jumpListDispatchShellBridge.TryDispatchPendingJumpListActivation()");
        Assert.IsFalse(
            text.Contains("TryDispatchPendingJumpListActivation = () => _fileActivationShellBridge", StringComparison.Ordinal),
            "Jump-list dispatch must not be wired to the file activation bridge.");
    }
}
