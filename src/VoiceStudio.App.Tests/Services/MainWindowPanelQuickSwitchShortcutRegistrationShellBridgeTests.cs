using System;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using VoiceStudio.App.Services;

namespace VoiceStudio.App.Tests.Services;

[TestClass]
public sealed class MainWindowPanelQuickSwitchShortcutRegistrationShellBridgeTests
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
        Path.Combine(FindRepoRoot(), "src", "VoiceStudio.App", "Services", "MainWindowPanelQuickSwitchShortcutRegistrationShellBridge.cs");

    [TestMethod]
    public void Panel_quick_switch_registration_bridge_does_not_reference_slice_24_indicator_bridge_type_name()
    {
        var text = File.ReadAllText(BridgePath);
        Assert.IsFalse(
            text.Contains("MainWindowPanelQuickSwitchShellBridge", StringComparison.Ordinal),
            "Anti-creep: Slice 24 indicator/timers bridge must not appear in Slice 37 registration-only bridge.");
    }

    [TestMethod]
    public void RegisterAll_throws_when_keyboard_shortcut_service_null()
    {
        var bridge = new MainWindowPanelQuickSwitchShortcutRegistrationShellBridge();
        var ex = Assert.ThrowsException<ArgumentNullException>(() =>
            bridge.RegisterAll(null!, _ => "t", (_, _) => Task.FromResult(true)));
        Assert.IsTrue(
            string.Equals(ex.ParamName, "keyboardShortcutService", StringComparison.Ordinal)
            || string.Equals(ex.ParamName, "service", StringComparison.Ordinal),
            $"Unexpected ParamName: {ex.ParamName}");
    }

    [TestMethod]
    public void RegisterAll_throws_when_get_panel_title_null()
    {
        var bridge = new MainWindowPanelQuickSwitchShortcutRegistrationShellBridge();
        var service = new KeyboardShortcutService();
        _ = Assert.ThrowsException<ArgumentNullException>(() =>
            bridge.RegisterAll(service, null!, (_, _) => Task.FromResult(true)));
    }

    [TestMethod]
    public void RegisterAll_throws_when_open_panel_null()
    {
        var bridge = new MainWindowPanelQuickSwitchShortcutRegistrationShellBridge();
        var service = new KeyboardShortcutService();
        _ = Assert.ThrowsException<ArgumentNullException>(() =>
            bridge.RegisterAll(service, _ => "t", null!));
    }
}
