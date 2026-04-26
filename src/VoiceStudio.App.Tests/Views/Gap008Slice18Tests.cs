using System;
using System.IO;
using System.Reflection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace VoiceStudio.App.Tests.Views;

[TestClass]
public sealed class Gap008Slice18Tests
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

    private static string MetricsBridgePath =>
        Path.Combine(FindRepoRoot(), "src", "VoiceStudio.App", "Services", "MainWindowStatusStripMetricsShellBridge.cs");

    [TestMethod]
    public void MainWindow_Loaded_tail_starts_metrics_timer_via_metrics_shell_bridge()
    {
        var text = File.ReadAllText(MainWindowPath);
        StringAssert.Contains(text, "_statusStripMetricsShellBridge");
        StringAssert.Contains(text, "_statusStripMetricsShellBridge.BeginMetricsTimer()");
    }

    [TestMethod]
    public void MainWindow_constructed_metrics_bridge_after_clock_bridge()
    {
        var text = File.ReadAllText(MainWindowPath);
        var idxClock = text.IndexOf("MainWindowStatusStripClockShellBridge Created", StringComparison.Ordinal);
        var idxMetrics = text.IndexOf("MainWindowStatusStripMetricsShellBridge Created", StringComparison.Ordinal);
        Assert.IsTrue(idxClock >= 0, "Expected clock shell bridge profiler checkpoint.");
        Assert.IsTrue(idxMetrics >= 0, "Expected metrics shell bridge profiler checkpoint.");
        Assert.IsTrue(idxClock < idxMetrics, "Metrics bridge should construct after clock bridge.");
    }

    [TestMethod]
    public void MainWindow_lifetime_cleanup_stops_metrics_timer_via_bridge()
    {
        var text = File.ReadAllText(MainWindowPath);
        StringAssert.Contains(text, "StopStatusBarTimer = () => _statusStripMetricsShellBridge.StopMetricsTimer()");
    }

    [TestMethod]
    public void MainWindow_still_wires_StatusBarCoordinator_after_metrics_bridge_BeginMetricsTimer()
    {
        var text = File.ReadAllText(MainWindowPath);
        var idxMetrics = text.IndexOf("BeginMetricsTimer()", StringComparison.Ordinal);
        var idxBridge = text.IndexOf("_statusBarCoordinatorShellBridge.ResolveAttachSubscribe(", StringComparison.Ordinal);
        Assert.IsTrue(idxMetrics >= 0, "Expected metrics BeginMetricsTimer call.");
        Assert.IsTrue(idxBridge >= 0, "Expected Slice 19 coordinator shell bridge wiring (Attach/Subscribe delegated).");
        Assert.IsTrue(
            idxMetrics < idxBridge,
            "Metrics timer should start before coordinator shell bridge so startup-truth wiring order stays explicit.");
    }

    [TestMethod]
    public void MainWindow_metrics_bridge_source_excludes_rhvoice_path_segment()
    {
        var text = File.ReadAllText(MetricsBridgePath);
        Assert.IsFalse(
            text.Contains("engines/audio/rhvoice", StringComparison.OrdinalIgnoreCase),
            "Slice 18 metrics bridge must not reference RHVoice engine path.");
    }
}
