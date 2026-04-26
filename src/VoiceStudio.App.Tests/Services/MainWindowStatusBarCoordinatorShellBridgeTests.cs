using System;
using System.IO;
using System.Reflection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using VoiceStudio.App.Services;

namespace VoiceStudio.App.Tests.Services;

[TestClass]
public sealed class MainWindowStatusBarCoordinatorShellBridgeTests
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

    private static string BridgePath =>
        Path.Combine(FindRepoRoot(), "src", "VoiceStudio.App", "Services", "MainWindowStatusBarCoordinatorShellBridge.cs");

    [TestMethod]
    public void ResolveAttachSubscribe_throws_when_resolveCoordinator_null()
    {
        var bridge = new MainWindowStatusBarCoordinatorShellBridge();
        Assert.ThrowsException<ArgumentNullException>(() =>
            bridge.ResolveAttachSubscribe(
                null!,
                null!,
                _ => null,
                null!,
                null,
                null));
    }

    [TestMethod]
    public void Coordinator_shell_bridge_source_excludes_rhvoice_path_segment()
    {
        var text = File.ReadAllText(BridgePath);
        Assert.IsFalse(
            text.Contains("engines/audio/rhvoice", StringComparison.OrdinalIgnoreCase),
            "Slice 19 coordinator shell bridge must not reference RHVoice engine path.");
    }

    [TestMethod]
    public void Coordinator_shell_bridge_source_excludes_unrelated_bridge_type_names()
    {
        var text = File.ReadAllText(BridgePath);
        Assert.IsFalse(text.Contains("MainWindowStatusStripClockShellBridge", StringComparison.Ordinal));
        Assert.IsFalse(text.Contains("MainWindowStatusStripMetricsShellBridge", StringComparison.Ordinal));
        Assert.IsFalse(text.Contains("MainWindowNotificationCenterShellBridge", StringComparison.Ordinal));
    }
}
