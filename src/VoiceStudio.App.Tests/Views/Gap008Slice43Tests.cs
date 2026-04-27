using System;
using System.IO;
using System.Reflection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace VoiceStudio.App.Tests.Views;

[TestClass]
public sealed class Gap008Slice43Tests
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
    public void MainWindow_declares_check_for_updates_menu_item_shell_bridge_field()
    {
        var text = File.ReadAllText(MainWindowPath);
        StringAssert.Contains(text, "_checkForUpdatesMenuItemShellBridge");
        StringAssert.Contains(text, "new MainWindowCheckForUpdatesMenuItemShellBridge(");
    }

    [TestMethod]
    public void MainWindow_ctor_instantiates_check_for_updates_menu_item_bridge_after_menu_tool_activation_and_before_keyboard_shortcuts()
    {
        var text = File.ReadAllText(MainWindowPath);
        var idxMenuTool = text.IndexOf("_menuToolActivationShellBridge = new MainWindowMenuToolActivationShellBridge", StringComparison.Ordinal);
        var idxCheckForUpdates = text.IndexOf("_checkForUpdatesMenuItemShellBridge = new MainWindowCheckForUpdatesMenuItemShellBridge", StringComparison.Ordinal);
        var idxKeyboard = text.IndexOf("_keyboardShortcutsShellBridge = new MainWindowKeyboardShortcutsShellBridge", StringComparison.Ordinal);
        Assert.IsTrue(idxMenuTool >= 0);
        Assert.IsTrue(idxCheckForUpdates > idxMenuTool);
        Assert.IsTrue(idxKeyboard > idxCheckForUpdates);
    }

    [TestMethod]
    public void MainWindow_has_no_private_CheckForUpdatesMenuItem_Click_handler()
    {
        var text = File.ReadAllText(MainWindowPath);
        Assert.IsFalse(
            text.Contains("CheckForUpdatesMenuItem_Click", StringComparison.Ordinal),
            "Slice 43: handler name must live on MainWindowCheckForUpdatesMenuItemShellBridge only.");
    }

    [TestMethod]
    public void MainWindow_check_for_updates_menu_item_click_attaches_to_shell_bridge()
    {
        var text = File.ReadAllText(MainWindowPath);
        StringAssert.Contains(text, "_checkForUpdatesMenuItem.Click += _checkForUpdatesMenuItemShellBridge.OnCheckForUpdatesMenuItemClick");
    }
}
