using System;
using System.IO;
using System.Reflection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace VoiceStudio.App.Tests.Views;

[TestClass]
public sealed class Gap008Slice21Tests
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

    private static string CheckForUpdatesMenuItemShellBridgePath =>
        Path.Combine(FindRepoRoot(), "src", "VoiceStudio.App", "Services", "MainWindowCheckForUpdatesMenuItemShellBridge.cs");

    [TestMethod]
    public void MainWindow_declares_keyboard_shortcuts_shell_bridge_field()
    {
        var text = File.ReadAllText(MainWindowPath);
        StringAssert.Contains(text, "_keyboardShortcutsShellBridge");
        StringAssert.Contains(text, "MainWindowKeyboardShortcutsShellBridge");
    }

    [TestMethod]
    public void MainWindow_keyboard_shortcuts_menu_item_uses_slice41_bridge_forwarding_slice21_shell()
    {
        var text = File.ReadAllText(MainWindowPath);
        StringAssert.Contains(text, "_keyboardShortcutsMenuItemShellBridge");
        StringAssert.Contains(text, "OnKeyboardShortcutsMenuItemClick");
        StringAssert.Contains(text, "_keyboardShortcutsShellBridge");
    }

    [TestMethod]
    public void MainWindow_keyboard_shortcuts_shell_bridge_constructed_after_menu_tool_activation_bridge()
    {
        var text = File.ReadAllText(MainWindowPath);
        var idxMenu = text.IndexOf("MainWindowMenuToolActivationShellBridge Created", StringComparison.Ordinal);
        var idxKb = text.IndexOf("MainWindowKeyboardShortcutsShellBridge Created", StringComparison.Ordinal);
        Assert.IsTrue(idxMenu >= 0, "Expected menu/tool activation shell bridge profiler checkpoint.");
        Assert.IsTrue(idxKb >= 0, "Expected keyboard shortcuts shell bridge profiler checkpoint.");
        Assert.IsTrue(idxMenu < idxKb, "Keyboard shortcuts bridge should construct after menu/tool activation bridge.");
    }

    [TestMethod]
    public void MainWindow_CheckForUpdates_does_not_reference_keyboard_shortcuts_bridge()
    {
        var text = File.ReadAllText(CheckForUpdatesMenuItemShellBridgePath);
        Assert.IsFalse(
            text.Contains("_keyboardShortcutsShellBridge", StringComparison.Ordinal),
            "Check for Updates menu wiring (Slice 43) must not couple to keyboard shortcuts shell.");
    }
}
