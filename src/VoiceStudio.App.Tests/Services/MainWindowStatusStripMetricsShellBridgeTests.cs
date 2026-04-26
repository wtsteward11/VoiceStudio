using System;
using System.IO;
using System.Reflection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using VoiceStudio.App.Services;
using VoiceStudio.Core.Services;

namespace VoiceStudio.App.Tests.Services;

[TestClass]
public sealed class MainWindowStatusStripMetricsShellBridgeTests
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

    private static string BridgeSourcePath =>
        Path.Combine(FindRepoRoot(), "src", "VoiceStudio.App", "Services", "MainWindowStatusStripMetricsShellBridge.cs");

    [TestMethod]
    public void Constructor_throws_when_any_dependency_accessor_is_null()
    {
        Assert.ThrowsException<ArgumentNullException>(() =>
            new MainWindowStatusStripMetricsShellBridge(
                getCpuText: null!,
                () => null,
                () => null,
                () => null,
                () => null,
                () => null));
        Assert.ThrowsException<ArgumentNullException>(() =>
            new MainWindowStatusStripMetricsShellBridge(
                () => null,
                getGpuText: null!,
                () => null,
                () => null,
                () => null,
                () => null));
        Assert.ThrowsException<ArgumentNullException>(() =>
            new MainWindowStatusStripMetricsShellBridge(
                () => null,
                () => null,
                getRamText: null!,
                () => null,
                () => null,
                () => null));
        Assert.ThrowsException<ArgumentNullException>(() =>
            new MainWindowStatusStripMetricsShellBridge(
                () => null,
                () => null,
                () => null,
                getLatencyText: null!,
                () => null,
                () => null));
        Assert.ThrowsException<ArgumentNullException>(() =>
            new MainWindowStatusStripMetricsShellBridge(
                () => null,
                () => null,
                () => null,
                () => null,
                getHealthClient: null!,
                () => null));
        Assert.ThrowsException<ArgumentNullException>(() =>
            new MainWindowStatusStripMetricsShellBridge(
                () => null,
                () => null,
                () => null,
                () => null,
                () => null,
                getTelemetryClient: null!));
    }

    [TestMethod]
    public void StopMetricsTimer_when_never_started_does_not_throw()
    {
        var bridge = new MainWindowStatusStripMetricsShellBridge(
            () => null,
            () => null,
            () => null,
            () => null,
            () => null,
            () => null);
        bridge.StopMetricsTimer();
        bridge.StopMetricsTimer();
    }

    [TestMethod]
    public void Bridge_source_excludes_unrelated_notification_center_bridge_type_name()
    {
        var text = File.ReadAllText(BridgeSourcePath);
        Assert.IsFalse(
            text.Contains("MainWindowNotificationCenterShellBridge", StringComparison.Ordinal),
            "Metrics bridge must not reference unrelated shell bridge types.");
    }

    [TestMethod]
    public void Bridge_source_excludes_rhvoice_path_segment()
    {
        var text = File.ReadAllText(BridgeSourcePath);
        Assert.IsFalse(
            text.Contains("engines/audio/rhvoice", StringComparison.OrdinalIgnoreCase),
            "RHVoice path frozen out of Slice 18 bridge source.");
    }
}
