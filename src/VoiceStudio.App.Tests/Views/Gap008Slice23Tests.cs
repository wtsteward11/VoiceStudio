using System;
using System.IO;
using System.Reflection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace VoiceStudio.App.Tests.Views;

[TestClass]
public sealed class Gap008Slice23Tests
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

    private static string PanelPreviewBridgePath =>
        Path.Combine(FindRepoRoot(), "src", "VoiceStudio.App", "Services", "MainWindowPanelPreviewShellBridge.cs");

    [TestMethod]
    public void MainWindow_delegates_nav_panel_preview_to_slice23_bridge()
    {
        var text = File.ReadAllText(MainWindowPath);
        StringAssert.Contains(text, "_panelPreviewShellBridge");
        StringAssert.Contains(text, "_panelPreviewShellBridge.OnNavButtonPointerEntered");
        StringAssert.Contains(text, "_panelPreviewShellBridge.OnNavButtonPointerExited");
    }

    [TestMethod]
    public void MainWindow_NavButton_Pointer_handlers_are_thin_forwards_only()
    {
        var text = File.ReadAllText(MainWindowPath);
        var idx = text.IndexOf("NavButton_PointerEntered", StringComparison.Ordinal);
        Assert.IsTrue(idx >= 0);
        var slice = text.Substring(idx, Math.Min(500, text.Length - idx));
        Assert.IsFalse(slice.Contains("new PanelPreviewPopup", StringComparison.Ordinal),
            "PanelPreviewPopup construction must live in panel preview shell bridge, not MainWindow.");
        Assert.IsFalse(slice.Contains("GetPanelInfoForButton", StringComparison.Ordinal),
            "GetPanelInfoForButton must live in panel preview shell bridge.");
    }

    [TestMethod]
    public void Panel_preview_bridge_does_not_reference_navigation_or_search_coordinators()
    {
        var text = File.ReadAllText(PanelPreviewBridgePath);
        Assert.IsFalse(text.Contains("MainWindowNavigationShellBridge", StringComparison.Ordinal));
        Assert.IsFalse(text.Contains("ISearchOverlayCoordinator", StringComparison.Ordinal));
        Assert.IsFalse(text.Contains("IProjectWorkflowCoordinator", StringComparison.Ordinal));
    }

    [TestMethod]
    public void MainWindow_lifetime_cleanup_still_disposes_preview_hide_timer_via_bridge()
    {
        var text = File.ReadAllText(MainWindowPath);
        StringAssert.Contains(text, "DisposePreviewHideTimer = () => _panelPreviewShellBridge.DisposePreviewHideTimer()");
    }

    [TestMethod]
    public void Panel_preview_bridge_CreatePreviewContent_pins_Timeline_stack_spine()
    {
        var text = File.ReadAllText(PanelPreviewBridgePath);
        StringAssert.Contains(text, "CreatePreviewContent");
        StringAssert.Contains(text, "case \"Timeline\":");
        StringAssert.Contains(text, "new StackPanel");
    }
}
