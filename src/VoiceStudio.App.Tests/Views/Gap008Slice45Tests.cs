using System;
using System.IO;
using System.Reflection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace VoiceStudio.App.Tests.Views;

[TestClass]
public sealed class Gap008Slice45Tests
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
    public void MainWindow_declares_toggle_mini_timeline_menu_item_shell_bridge_field()
    {
        var text = File.ReadAllText(MainWindowPath);
        StringAssert.Contains(text, "_toggleMiniTimelineMenuItemShellBridge");
        StringAssert.Contains(text, "new MainWindowToggleMiniTimelineMenuItemShellBridge(");
    }

    [TestMethod]
    public void MainWindow_ctor_instantiates_toggle_mini_timeline_bridge_after_manage_workspaces_and_before_keyboard_shortcuts()
    {
        var text = File.ReadAllText(MainWindowPath);
        var idxManageWs = text.IndexOf("_manageWorkspacesMenuItemShellBridge = new MainWindowManageWorkspacesMenuItemShellBridge", StringComparison.Ordinal);
        var idxToggleMini = text.IndexOf("_toggleMiniTimelineMenuItemShellBridge = new MainWindowToggleMiniTimelineMenuItemShellBridge", StringComparison.Ordinal);
        var idxKeyboard = text.IndexOf("_keyboardShortcutsShellBridge = new MainWindowKeyboardShortcutsShellBridge", StringComparison.Ordinal);
        Assert.IsTrue(idxManageWs >= 0);
        Assert.IsTrue(idxToggleMini > idxManageWs);
        Assert.IsTrue(idxKeyboard > idxToggleMini);
    }

    [TestMethod]
    public void MainWindow_has_no_private_ToggleMiniTimelineMenuItem_Click_handler()
    {
        var text = File.ReadAllText(MainWindowPath);
        Assert.IsFalse(
            text.Contains("ToggleMiniTimelineMenuItem_Click", StringComparison.Ordinal),
            "Slice 45: handler name must not appear on MainWindow.xaml.cs.");
    }

    [TestMethod]
    public void MainWindow_toggle_mini_timeline_menu_item_click_attaches_to_shell_bridge()
    {
        var text = File.ReadAllText(MainWindowPath);
        StringAssert.Contains(text, "_toggleMiniTimelineMenuItem.Click += _toggleMiniTimelineMenuItemShellBridge.OnToggleMiniTimelineMenuItemClick");
    }
}
