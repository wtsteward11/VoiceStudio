using System;
using System.IO;
using System.Reflection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using VoiceStudio.App.Services;

namespace VoiceStudio.App.Tests.Services;

[TestClass]
public sealed class MainWindowPanelDockShellBridgeTests
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

    private static string PanelDockBridgePath =>
        Path.Combine(FindRepoRoot(), "src", "VoiceStudio.App", "Services", "MainWindowPanelDockShellBridge.cs");

    [TestMethod]
    public void Panel_dock_bridge_does_not_reference_nav_preview_or_quick_switch()
    {
        var text = File.ReadAllText(PanelDockBridgePath);
        Assert.IsFalse(text.Contains("MainWindowNavigationShellBridge", StringComparison.Ordinal));
        Assert.IsFalse(text.Contains("MainWindowPanelPreviewShellBridge", StringComparison.Ordinal));
        Assert.IsFalse(text.Contains("MainWindowPanelQuickSwitchShellBridge", StringComparison.Ordinal));
        Assert.IsFalse(text.Contains("ISearchOverlayCoordinator", StringComparison.Ordinal));
    }
}
