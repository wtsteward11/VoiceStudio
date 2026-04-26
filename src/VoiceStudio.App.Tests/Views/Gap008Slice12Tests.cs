using System;
using System.IO;
using System.Reflection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace VoiceStudio.App.Tests.Views;

[TestClass]
public sealed class Gap008Slice12Tests
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
    public void MainWindow_Loaded_bootstrap_hooks_delegate_jump_list_and_taskbar_to_slice12_bridge()
    {
        var text = File.ReadAllText(MainWindowPath);
        StringAssert.Contains(text, "_jumpListTaskbarProgressShellBridge");
        StringAssert.Contains(text, "WireJumpListShell = () => _jumpListTaskbarProgressShellBridge.WireJumpList()");
        StringAssert.Contains(text, "WireTaskbarProgressShell = () => _jumpListTaskbarProgressShellBridge.WireTaskbarProgress()");
    }

    [TestMethod]
    public void MainWindow_constructed_jump_list_taskbar_bridge_before_startup_welcome_bridge()
    {
        var text = File.ReadAllText(MainWindowPath);
        var idxJump = text.IndexOf("MainWindowJumpListTaskbarProgressShellBridge Created", StringComparison.Ordinal);
        var idxWelcome = text.IndexOf("StartupWelcomeActivationShellBridge Created", StringComparison.Ordinal);
        Assert.IsTrue(idxJump >= 0, "Expected jump list / taskbar bridge profiler checkpoint.");
        Assert.IsTrue(idxWelcome >= 0, "Expected startup welcome bridge profiler checkpoint.");
        Assert.IsTrue(idxJump < idxWelcome, "Jump list / taskbar bridge should construct before startup/welcome bridge.");
    }
}
