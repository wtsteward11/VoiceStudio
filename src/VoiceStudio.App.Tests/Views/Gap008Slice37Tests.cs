using System;
using System.IO;
using System.Reflection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace VoiceStudio.App.Tests.Views;

[TestClass]
public sealed class Gap008Slice37Tests
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
    public void MainWindow_uses_Slice_37_panel_quick_switch_shortcut_registration_bridge_field()
    {
        var text = File.ReadAllText(MainWindowPath);
        StringAssert.Contains(text, "_panelQuickSwitchShortcutRegistrationShellBridge");
        StringAssert.Contains(text, "new MainWindowPanelQuickSwitchShortcutRegistrationShellBridge(");
    }

    [TestMethod]
    public void MainWindow_ctor_calls_panel_quick_switch_RegisterAll_after_keyboard_registration_before_menu_items()
    {
        var text = File.ReadAllText(MainWindowPath);
        var idxRegister = text.IndexOf("_keyboardShortcutRegistrationShellBridge.Register", StringComparison.Ordinal);
        var idxPanelQuick = text.IndexOf("_panelQuickSwitchShortcutRegistrationShellBridge.RegisterAll", StringComparison.Ordinal);
        var idxMenu = text.IndexOf("Menu Items Created", StringComparison.Ordinal);
        Assert.IsTrue(idxRegister >= 0, "Expected keyboard registration bridge Register call.");
        Assert.IsTrue(idxPanelQuick > idxRegister, "Panel quick-switch registration should follow keyboard registration.");
        Assert.IsTrue(idxMenu > idxPanelQuick, "Panel quick-switch registration should precede Menu Items Created.");
    }

    [TestMethod]
    public void MainWindow_has_no_private_RegisterPanelQuickSwitchShortcut_method()
    {
        var text = File.ReadAllText(MainWindowPath);
        Assert.IsFalse(
            text.Contains("private void RegisterPanelQuickSwitchShortcut", StringComparison.Ordinal),
            "Panel quick-switch shortcut registration should live in MainWindowPanelQuickSwitchShortcutRegistrationShellBridge.");
    }
}
