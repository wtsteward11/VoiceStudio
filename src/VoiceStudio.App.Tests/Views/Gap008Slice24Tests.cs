using System;
using System.IO;
using System.Reflection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace VoiceStudio.App.Tests.Views;

[TestClass]
public sealed class Gap008Slice24Tests
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

    private static string QuickSwitchBridgePath =>
        Path.Combine(FindRepoRoot(), "src", "VoiceStudio.App", "Services", "MainWindowPanelQuickSwitchShellBridge.cs");

    [TestMethod]
    public void MainWindow_delegates_panel_quick_switch_to_slice24_bridge()
    {
        var text = File.ReadAllText(MainWindowPath);
        StringAssert.Contains(text, "_panelQuickSwitchShellBridge");
        StringAssert.Contains(text, "_panelQuickSwitchShellBridge.ShowPanelQuickSwitchIndicator");
    }

    [TestMethod]
    public void MainWindow_ShellNavigationCoordinator_uses_slice24_bridge_for_quick_switch()
    {
        var text = File.ReadAllText(MainWindowPath);
        var idxNewBridge = text.IndexOf("_panelQuickSwitchShellBridge = new MainWindowPanelQuickSwitchShellBridge", StringComparison.Ordinal);
        var idxNav = text.IndexOf("new ShellNavigationCoordinator", StringComparison.Ordinal);
        var idxShowAfterNav = text.IndexOf("_panelQuickSwitchShellBridge.ShowPanelQuickSwitchIndicator", idxNav, StringComparison.Ordinal);
        Assert.IsTrue(idxNewBridge >= 0, "MainWindowPanelQuickSwitchShellBridge construction expected.");
        Assert.IsTrue(idxNav >= 0, "ShellNavigationCoordinator construction expected.");
        Assert.IsTrue(idxShowAfterNav >= 0, "Slice 24 ShowPanelQuickSwitchIndicator must be passed to ShellNavigationCoordinator (Slice 27 also injects the same method for focus bridge — use occurrence after nav ctor).");
        Assert.IsTrue(idxNewBridge < idxNav, "Quick-switch bridge must be assigned before ShellNavigationCoordinator.");
        Assert.IsTrue(idxShowAfterNav > idxNav, "Show delegate for navigation must be inside the ShellNavigationCoordinator construction (after 'new ShellNavigationCoordinator').");
    }

    [TestMethod]
    public void MainWindow_does_not_inline_new_Popup_for_panel_quick_switch()
    {
        var text = File.ReadAllText(MainWindowPath);
        Assert.IsFalse(
            text.Contains("new Popup", StringComparison.Ordinal),
            "Panel quick-switch Popup must be owned by MainWindowPanelQuickSwitchShellBridge, not MainWindow.");
    }

    [TestMethod]
    public void MainWindow_lifetime_cleanup_disposes_quick_switch_hide_timer_via_bridge()
    {
        var text = File.ReadAllText(MainWindowPath);
        StringAssert.Contains(text, "DisposeQuickSwitchHideTimer = () => _panelQuickSwitchShellBridge.DisposeQuickSwitchHideTimer()");
    }

    [TestMethod]
    public void Panel_quick_switch_bridge_does_not_reference_navigation_or_search_coordinators()
    {
        var text = File.ReadAllText(QuickSwitchBridgePath);
        Assert.IsFalse(text.Contains("MainWindowNavigationShellBridge", StringComparison.Ordinal));
        Assert.IsFalse(text.Contains("ISearchOverlayCoordinator", StringComparison.Ordinal));
        Assert.IsFalse(text.Contains("IProjectWorkflowCoordinator", StringComparison.Ordinal));
    }

    [TestMethod]
    public void MainWindow_FocusPanelRegion_and_SwitchToPanel_region_use_bridge_for_indicator()
    {
        var text = File.ReadAllText(MainWindowPath);
        StringAssert.Contains(text, "private void SwitchToPanel");
        Assert.IsFalse(
            text.Contains("private void FocusPanelRegion", StringComparison.Ordinal),
            "FocusPanelRegion is Slice 27 — MainWindowPanelRegionFocusShellBridge; not a private method on MainWindow.");
        var idxSwitch = text.IndexOf("private void SwitchToPanel", StringComparison.Ordinal);
        Assert.IsTrue(idxSwitch >= 0);
        var tailFromSwitch = text.Substring(idxSwitch, Math.Min(2000, text.Length - idxSwitch));
        StringAssert.Contains(tailFromSwitch, "_panelQuickSwitchShellBridge.ShowPanelQuickSwitchIndicator");
        StringAssert.Contains(text, "new MainWindowPanelRegionFocusShellBridge");
        StringAssert.Contains(
            text,
            "(name, region, host) => _panelQuickSwitchShellBridge.ShowPanelQuickSwitchIndicator",
            "Panel region focus (Slice 27) injects the Slice 24 quick-switch indicator as a host callback.");
    }
}
