using System;
using System.IO;
using System.Reflection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace VoiceStudio.App.Tests.Views;

[TestClass]
public sealed class Gap008Slice13Tests
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
    public void MainWindow_closed_and_cleanup_delegate_to_lifetime_cleanup_bridge()
    {
        var text = File.ReadAllText(MainWindowPath);
        StringAssert.Contains(text, "_lifetimeCleanupShellBridge");
        StringAssert.Contains(text, "_lifetimeCleanupShellBridge.OnClosedPrelude()");
        StringAssert.Contains(text, "_lifetimeCleanupShellBridge.RunCleanupCore()");
    }

    [TestMethod]
    public void MainWindow_finalizer_forwards_cleanup_through_bridge()
    {
        var text = File.ReadAllText(MainWindowPath);
        StringAssert.Contains(text, "~MainWindow()");
        StringAssert.Contains(text, "_lifetimeCleanupShellBridge.RunCleanupCore()");
    }

    [TestMethod]
    public void MainWindow_constructed_lifetime_cleanup_bridge_after_jump_list_bridge()
    {
        var text = File.ReadAllText(MainWindowPath);
        var idxLifetime = text.IndexOf("LifetimeCleanupShellBridge Created", StringComparison.Ordinal);
        var idxJump = text.IndexOf("MainWindowJumpListTaskbarProgressShellBridge Created", StringComparison.Ordinal);
        Assert.IsTrue(idxJump >= 0, "Expected jump list / taskbar bridge profiler checkpoint.");
        Assert.IsTrue(idxLifetime >= 0, "Expected lifetime cleanup bridge profiler checkpoint.");
        Assert.IsTrue(idxJump < idxLifetime, "Lifetime cleanup bridge should construct after jump list / taskbar bridge.");
    }

    [TestMethod]
    public void MainWindow_closed_invokes_on_closed_prelude_then_cleanup_only()
    {
        var text = File.ReadAllText(MainWindowPath);
        var closedIdx = text.IndexOf("private void MainWindow_Closed", StringComparison.Ordinal);
        Assert.IsTrue(closedIdx >= 0);
        var closedBlock = text.AsSpan(closedIdx, Math.Min(400, text.Length - closedIdx)).ToString();
        StringAssert.Contains(closedBlock, "_lifetimeCleanupShellBridge.OnClosedPrelude()");
        StringAssert.Contains(closedBlock, "Cleanup()");
    }

    [TestMethod]
    public void MainWindow_cleanup_body_forwards_only_run_cleanup_core()
    {
        var text = File.ReadAllText(MainWindowPath);
        var cleanupIdx = text.IndexOf("private void Cleanup()", StringComparison.Ordinal);
        Assert.IsTrue(cleanupIdx >= 0);
        var cleanupBlock = text.AsSpan(cleanupIdx, Math.Min(200, text.Length - cleanupIdx)).ToString();
        StringAssert.Contains(cleanupBlock, "_lifetimeCleanupShellBridge.RunCleanupCore()");
        Assert.IsFalse(
            cleanupBlock.Contains("OnClosedPrelude", StringComparison.Ordinal),
            "Cleanup() must not invoke OnClosedPrelude; prelude is Closed-only.");
    }

    [TestMethod]
    public void MainWindow_finalizer_calls_cleanup_so_prelude_not_duplicated_on_gc_path()
    {
        var text = File.ReadAllText(MainWindowPath);
        StringAssert.Contains(text, "~MainWindow()");
        var finIdx = text.IndexOf("~MainWindow()", StringComparison.Ordinal);
        Assert.IsTrue(finIdx >= 0);
        var finBlock = text.AsSpan(finIdx, Math.Min(120, text.Length - finIdx)).ToString();
        StringAssert.Contains(finBlock, "Cleanup()");
        Assert.IsFalse(
            finBlock.Contains("OnClosedPrelude", StringComparison.Ordinal),
            "Finalizer must not call OnClosedPrelude; only Cleanup -> RunCleanupCore.");
    }
}
