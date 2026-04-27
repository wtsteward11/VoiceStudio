using System;
using System.IO;
using System.Reflection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace VoiceStudio.App.Tests.Views;

[TestClass]
public sealed class Gap008Slice39Tests
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
    public void MainWindow_declares_smoke_startup_mode_bridge_field()
    {
        var text = File.ReadAllText(MainWindowPath);
        StringAssert.Contains(text, "_smokeStartupModeShellBridge");
        StringAssert.Contains(text, "new MainWindowSmokeStartupModeShellBridge(");
    }

    [TestMethod]
    public void MainWindow_ctor_instantiates_smoke_bridge_before_panel_region_focus_and_coordinator()
    {
        var text = File.ReadAllText(MainWindowPath);
        var idxSmoke = text.IndexOf("_smokeStartupModeShellBridge = new MainWindowSmokeStartupModeShellBridge", StringComparison.Ordinal);
        var idxPanelQuick = text.IndexOf("_panelQuickSwitchShellBridge = new MainWindowPanelQuickSwitchShellBridge", StringComparison.Ordinal);
        var idxRegionFocus = text.IndexOf("_panelRegionFocusShellBridge = new MainWindowPanelRegionFocusShellBridge", StringComparison.Ordinal);
        var idxCoord = text.IndexOf("_shellNavigationCoordinator = new ShellNavigationCoordinator", StringComparison.Ordinal);
        Assert.IsTrue(idxSmoke >= 0);
        Assert.IsTrue(idxPanelQuick > idxSmoke);
        Assert.IsTrue(idxRegionFocus > idxSmoke);
        Assert.IsTrue(idxCoord > idxSmoke);
    }

    [TestMethod]
    public void MainWindow_does_not_define_private_static_IsSafeStartupMode_or_IsGateCSmokeMode()
    {
        var text = File.ReadAllText(MainWindowPath);
        Assert.IsFalse(
            text.Contains("private static bool IsSafeStartupMode", StringComparison.Ordinal),
            "Safe-startup probe should live on MainWindowSmokeStartupModeShellBridge.");
        Assert.IsFalse(
            text.Contains("private static bool IsGateCSmokeMode", StringComparison.Ordinal),
            "Gate-C smoke probe should live on MainWindowSmokeStartupModeShellBridge.");
    }

    [TestMethod]
    public void MainWindow_wires_startup_welcome_bridge_from_smoke_instance()
    {
        var text = File.ReadAllText(MainWindowPath);
        StringAssert.Contains(text, "_startupWelcomeActivationShellBridge = new MainWindowStartupWelcomeActivationShellBridge(");
        StringAssert.Contains(text, "_smokeStartupModeShellBridge.IsGateCSmokeMode");
        StringAssert.Contains(text, "_smokeStartupModeShellBridge.IsSafeStartupMode");
    }
}
