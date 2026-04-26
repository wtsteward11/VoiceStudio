using System;
using System.IO;
using System.Reflection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace VoiceStudio.App.Tests.Services;

[TestClass]
public sealed class MainWindowPanelRegionFocusShellBridgeTests
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
        Path.Combine(FindRepoRoot(), "src", "VoiceStudio.App", "Services", "MainWindowPanelRegionFocusShellBridge.cs");

    [TestMethod]
    public void Panel_region_focus_bridge_does_not_reference_other_mainwindow_shell_bridge_types()
    {
        var text = File.ReadAllText(BridgePath);
        Assert.IsFalse(text.Contains("MainWindowPanelDockShellBridge", StringComparison.Ordinal));
        Assert.IsFalse(text.Contains("MainWindowNavigationShellBridge", StringComparison.Ordinal));
        Assert.IsFalse(text.Contains("MainWindowStartupOverlayShellBridge", StringComparison.Ordinal));
        Assert.IsFalse(text.Contains("MainWindowSearchOverlayShellBridge", StringComparison.Ordinal));
        Assert.IsFalse(text.Contains("MainWindowPanelQuickSwitchShellBridge", StringComparison.Ordinal));
    }
}
