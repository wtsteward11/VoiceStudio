using System;
using System.IO;
using System.Reflection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace VoiceStudio.App.Tests.Views;

[TestClass]
public sealed class Gap008Slice19Tests
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
    public void MainWindow_Loaded_wires_StatusBarCoordinator_via_coordinator_shell_bridge()
    {
        var text = File.ReadAllText(MainWindowPath);
        StringAssert.Contains(text, "_statusBarCoordinatorShellBridge");
        StringAssert.Contains(text, "_statusBarCoordinatorShellBridge.ResolveAttachSubscribe(");
    }

    [TestMethod]
    public void MainWindow_shell_loaded_bootstrap_StartBackendHealthMonitoring_uses_coordinator_shell_bridge()
    {
        var text = File.ReadAllText(MainWindowPath);
        StringAssert.Contains(text, "StartBackendHealthMonitoring = () =>");
        StringAssert.Contains(text, "_statusBarCoordinatorShellBridge.StartBackendHealthMonitoring(_statusBarCoordinator)");
    }

    [TestMethod]
    public void MainWindow_coordinator_shell_bridge_wiring_after_metrics_BeginMetricsTimer()
    {
        var text = File.ReadAllText(MainWindowPath);
        var idxMetrics = text.IndexOf("_statusStripMetricsShellBridge.BeginMetricsTimer()", StringComparison.Ordinal);
        var idxBridge = text.IndexOf("_statusBarCoordinatorShellBridge.ResolveAttachSubscribe(", StringComparison.Ordinal);
        Assert.IsTrue(idxMetrics >= 0, "Expected metrics BeginMetricsTimer call.");
        Assert.IsTrue(idxBridge >= 0, "Expected coordinator shell bridge ResolveAttachSubscribe.");
        Assert.IsTrue(
            idxMetrics < idxBridge,
            "Metrics timer should start before StatusBarCoordinator shell wiring (startup-truth ordering).");
    }

    [TestMethod]
    public void MainWindow_constructed_coordinator_shell_bridge_after_metrics_bridge()
    {
        var text = File.ReadAllText(MainWindowPath);
        var idxMetrics = text.IndexOf("MainWindowStatusStripMetricsShellBridge Created", StringComparison.Ordinal);
        var idxCoord = text.IndexOf("MainWindowStatusBarCoordinatorShellBridge Created", StringComparison.Ordinal);
        Assert.IsTrue(idxMetrics >= 0, "Expected metrics shell bridge profiler checkpoint.");
        Assert.IsTrue(idxCoord >= 0, "Expected coordinator shell bridge profiler checkpoint.");
        Assert.IsTrue(idxMetrics < idxCoord, "Coordinator shell bridge should construct after metrics shell bridge.");
    }
}
