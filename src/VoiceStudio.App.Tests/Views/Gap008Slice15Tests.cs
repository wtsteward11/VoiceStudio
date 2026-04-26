using System;
using System.IO;
using System.Reflection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace VoiceStudio.App.Tests.Views;

[TestClass]
public sealed class Gap008Slice15Tests
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
    public void MainWindow_Loaded_bootstrap_hooks_delegate_jump_list_dispatch_to_slice15_bridge()
    {
        var text = File.ReadAllText(MainWindowPath);
        StringAssert.Contains(text, "_jumpListDispatchShellBridge");
        StringAssert.Contains(text, "TryDispatchPendingJumpListActivation = () => _jumpListDispatchShellBridge.TryDispatchPendingJumpListActivation()");
    }

    [TestMethod]
    public void MainWindow_constructed_jump_list_dispatch_bridge_after_file_activation_bridge()
    {
        var text = File.ReadAllText(MainWindowPath);
        var idxFile = text.IndexOf("MainWindowFileActivationShellBridge Created", StringComparison.Ordinal);
        var idxJump = text.IndexOf("MainWindowJumpListDispatchShellBridge Created", StringComparison.Ordinal);
        Assert.IsTrue(idxFile >= 0, "Expected file activation bridge profiler checkpoint.");
        Assert.IsTrue(idxJump >= 0, "Expected jump list dispatch bridge profiler checkpoint.");
        Assert.IsTrue(idxFile < idxJump, "Jump list dispatch bridge should construct after file activation bridge.");
    }

    [TestMethod]
    public void MainWindow_does_not_embed_private_jump_list_pending_dispatch_methods()
    {
        var text = File.ReadAllText(MainWindowPath);
        Assert.IsFalse(
            text.Contains("private void TryDispatchPendingJumpListActivation", StringComparison.Ordinal),
            "Jump list pending dispatch should live on MainWindowJumpListDispatchShellBridge.");
        Assert.IsFalse(
            text.Contains("RunJumpListPendingAsync", StringComparison.Ordinal),
            "RunJumpListPendingAsync should not remain on MainWindow.");
    }

    [TestMethod]
    public void MainWindow_retains_file_activation_delegate_separate_from_jump_list()
    {
        var text = File.ReadAllText(MainWindowPath);
        StringAssert.Contains(text, "_fileActivationShellBridge.TryDispatchPendingFileActivation()");
    }
}
