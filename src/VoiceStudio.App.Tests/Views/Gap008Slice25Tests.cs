using System;
using System.IO;
using System.Reflection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace VoiceStudio.App.Tests.Views;

[TestClass]
public sealed class Gap008Slice25Tests
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

    private static string PanelDockBridgePath =>
        Path.Combine(FindRepoRoot(), "src", "VoiceStudio.App", "Services", "MainWindowPanelDockShellBridge.cs");

    [TestMethod]
    public void MainWindow_delegates_panel_dock_to_slice25_bridge()
    {
        var text = File.ReadAllText(MainWindowPath);
        StringAssert.Contains(text, "_panelDockShellBridge");
        StringAssert.Contains(text, "_panelDockShellBridge.OnPanelDockRequested");
    }

    [TestMethod]
    public void MainWindow_creates_PanelDockBridge_after_NavigationShellBridge()
    {
        var text = File.ReadAllText(MainWindowPath);
        var idxNewBridge = text.IndexOf("_panelDockShellBridge = new MainWindowPanelDockShellBridge", StringComparison.Ordinal);
        var idxNavBridge = text.IndexOf("new MainWindowNavigationShellBridge", StringComparison.Ordinal);
        Assert.IsTrue(idxNewBridge >= 0, "MainWindowPanelDockShellBridge construction expected.");
        Assert.IsTrue(idxNavBridge >= 0, "MainWindowNavigationShellBridge construction expected.");
        Assert.IsTrue(idxNewBridge > idxNavBridge, "Panel-dock bridge must be constructed after MainWindowNavigationShellBridge (OpenPanelByIdAsync seam).");
    }

    [TestMethod]
    public void MainWindow_does_not_inline_AnimatePanelDock_or_CompletePanelDockAsync()
    {
        var text = File.ReadAllText(MainWindowPath);
        Assert.IsFalse(
            text.Contains("private void AnimatePanelDock", StringComparison.Ordinal),
            "AnimatePanelDock must live in MainWindowPanelDockShellBridge.");
        Assert.IsFalse(
            text.Contains("private async Task CompletePanelDockAsync", StringComparison.Ordinal),
            "CompletePanelDockAsync must live in MainWindowPanelDockShellBridge.");
    }

    [TestMethod]
    public void MainWindow_wires_PanelHost_OnPanelDockRequested_to_bridge_method()
    {
        var text = File.ReadAllText(MainWindowPath);
        StringAssert.Contains(text, "OnPanelDockRequested += _panelDockShellBridge.OnPanelDockRequested");
    }

    [TestMethod]
    public void MainWindow_passes_openPanelAndLayout_to_panel_dock_bridge_ctor()
    {
        var text = File.ReadAllText(MainWindowPath);
        StringAssert.Contains(text, "OpenPanelByIdAsync,");
        StringAssert.Contains(text, "_layoutSaveDebouncer?.Invoke()");
    }

    [TestMethod]
    public void Panel_dock_bridge_owns_dock_storyboard_not_MainWindow()
    {
        var mw = File.ReadAllText(MainWindowPath);
        Assert.IsFalse(
            mw.Contains("new DoubleAnimation", StringComparison.Ordinal) && mw.Contains("Panel Swapped", StringComparison.Ordinal),
            "Dock fade DoubleAnimation and swap toast strings must not remain in MainWindow.xaml.cs.");
        var bridge = File.ReadAllText(PanelDockBridgePath);
        StringAssert.Contains(bridge, "new DoubleAnimation");
        StringAssert.Contains(bridge, "Panel Swapped");
    }
}
