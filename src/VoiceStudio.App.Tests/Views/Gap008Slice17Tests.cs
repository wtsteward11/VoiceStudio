using System;
using System.IO;
using System.Reflection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace VoiceStudio.App.Tests.Views;

[TestClass]
public sealed class Gap008Slice17Tests
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
    public void MainWindow_Loaded_tail_starts_status_strip_clock_timer_via_bridge()
    {
        var text = File.ReadAllText(MainWindowPath);
        StringAssert.Contains(text, "_statusStripClockShellBridge");
        StringAssert.Contains(text, "_statusStripClockShellBridge.BeginClockTimer()");
    }

    [TestMethod]
    public void MainWindow_constructed_status_strip_clock_bridge_after_notification_center_bridge()
    {
        var text = File.ReadAllText(MainWindowPath);
        var idxNc = text.IndexOf("MainWindowNotificationCenterShellBridge Created", StringComparison.Ordinal);
        var idxClock = text.IndexOf("MainWindowStatusStripClockShellBridge Created", StringComparison.Ordinal);
        Assert.IsTrue(idxNc >= 0, "Expected notification center shell bridge profiler checkpoint.");
        Assert.IsTrue(idxClock >= 0, "Expected status strip clock shell bridge profiler checkpoint.");
        Assert.IsTrue(idxNc < idxClock, "Clock bridge should construct after notification center bridge.");
    }

    [TestMethod]
    public void MainWindow_lifetime_cleanup_disposes_clock_timer_via_bridge()
    {
        var text = File.ReadAllText(MainWindowPath);
        StringAssert.Contains(text, "DisposeClockTimer = () => _statusStripClockShellBridge.DisposeClockTimer()");
    }

    [TestMethod]
    public void MainWindow_StatusBar_partial_removed_slice18_metrics_on_shell_bridge()
    {
        var statusBarPath = Path.Combine(FindRepoRoot(), "src", "VoiceStudio.App", "MainWindow.StatusBar.cs");
        Assert.IsFalse(
            File.Exists(statusBarPath),
            "Slice 18 removed MainWindow.StatusBar.cs; metrics live on MainWindowStatusStripMetricsShellBridge.");
    }
}
