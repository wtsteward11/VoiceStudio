using System;
using System.IO;
using System.Reflection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace VoiceStudio.App.Tests.Views;

[TestClass]
public sealed class Gap008Slice27Tests
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

    private static string PanelRegionFocusBridgePath =>
        Path.Combine(FindRepoRoot(), "src", "VoiceStudio.App", "Services", "MainWindowPanelRegionFocusShellBridge.cs");

    private static string KeyboardShortcutRegistrationBridgePath =>
        Path.Combine(FindRepoRoot(), "src", "VoiceStudio.App", "Services", "MainWindowKeyboardShortcutRegistrationShellBridge.cs");

    [TestMethod]
    public void MainWindow_delegates_panel_region_focus_and_cycling_to_slice27_bridge()
    {
        var mw = File.ReadAllText(MainWindowPath);
        StringAssert.Contains(mw, "_panelRegionFocusShellBridge");
        var reg = File.ReadAllText(KeyboardShortcutRegistrationBridgePath);
        StringAssert.Contains(reg, "deps.PanelRegionFocus.CyclePanelNext");
        StringAssert.Contains(reg, "deps.PanelRegionFocus.FocusPanelRegion");
    }

    [TestMethod]
    public void MainWindow_creates_PanelRegionFocusShellBridge_immediately_after_QuickSwitch_bridge()
    {
        var text = File.ReadAllText(MainWindowPath);
        var idxFocus = text.IndexOf("new MainWindowPanelRegionFocusShellBridge", StringComparison.Ordinal);
        var idxQuick = text.IndexOf("new MainWindowPanelQuickSwitchShellBridge", StringComparison.Ordinal);
        Assert.IsTrue(idxFocus >= 0, "MainWindowPanelRegionFocusShellBridge construction expected.");
        Assert.IsTrue(idxQuick >= 0, "MainWindowPanelQuickSwitchShellBridge construction expected.");
        Assert.IsTrue(
            idxFocus > idxQuick,
            "Panel region focus shell bridge should be constructed after the quick-switch shell bridge (indicator delegate wiring).");
    }

    [TestMethod]
    public void MainWindow_does_not_define_private_CyclePanelNext_or_FocusPanelRegion()
    {
        var text = File.ReadAllText(MainWindowPath);
        Assert.IsFalse(
            text.Contains("private void CyclePanelNext", StringComparison.Ordinal),
            "CyclePanelNext must live in MainWindowPanelRegionFocusShellBridge.");
        Assert.IsFalse(
            text.Contains("private void FocusPanelRegion", StringComparison.Ordinal),
            "FocusPanelRegion must live in MainWindowPanelRegionFocusShellBridge.");
    }

    [TestMethod]
    public void MainWindow_does_not_contain_GAP_E02_end_region_marker()
    {
        var text = File.ReadAllText(MainWindowPath);
        Assert.IsFalse(
            text.Contains("GAP-E02", StringComparison.Ordinal),
            "GAP-E02 region body should be removed; see Slice 27 brief.");
    }

    [TestMethod]
    public void Panel_region_focus_bridge_file_documents_GAP008_slice_27()
    {
        var text = File.ReadAllText(PanelRegionFocusBridgePath);
        StringAssert.Contains(text, "GAP-008 Slice 27");
        StringAssert.Contains(text, "VOICESTUDIO_BOUNDED_GAP008_SLICE27_MAINWINDOW_PANEL_REGION_FOCUS_SHELL");
    }
}
