using System;
using System.IO;
using System.Reflection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace VoiceStudio.App.Tests.Views;

[TestClass]
public sealed class Gap008Slice44Tests
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

    private static string WorkspacesPartialPath =>
        Path.Combine(FindRepoRoot(), "src", "VoiceStudio.App", "MainWindow.Workspaces.cs");

    [TestMethod]
    public void MainWindow_declares_manage_workspaces_menu_item_shell_bridge_field()
    {
        var text = File.ReadAllText(MainWindowPath);
        StringAssert.Contains(text, "_manageWorkspacesMenuItemShellBridge");
        StringAssert.Contains(text, "new MainWindowManageWorkspacesMenuItemShellBridge(");
    }

    [TestMethod]
    public void MainWindow_ctor_instantiates_manage_workspaces_menu_item_bridge_after_check_for_updates_and_before_keyboard_shortcuts()
    {
        var text = File.ReadAllText(MainWindowPath);
        var idxCheckForUpdates = text.IndexOf("_checkForUpdatesMenuItemShellBridge = new MainWindowCheckForUpdatesMenuItemShellBridge", StringComparison.Ordinal);
        var idxManageWs = text.IndexOf("_manageWorkspacesMenuItemShellBridge = new MainWindowManageWorkspacesMenuItemShellBridge", StringComparison.Ordinal);
        var idxKeyboard = text.IndexOf("_keyboardShortcutsShellBridge = new MainWindowKeyboardShortcutsShellBridge", StringComparison.Ordinal);
        Assert.IsTrue(idxCheckForUpdates >= 0);
        Assert.IsTrue(idxManageWs > idxCheckForUpdates);
        Assert.IsTrue(idxKeyboard > idxManageWs);
    }

    [TestMethod]
    public void MainWindow_and_workspaces_partials_have_no_private_ManageWorkspaces_Click_handler()
    {
        var main = File.ReadAllText(MainWindowPath);
        Assert.IsFalse(
            main.Contains("ManageWorkspaces_Click", StringComparison.Ordinal),
            "Slice 44: handler name must not appear on MainWindow.xaml.cs.");

        var workspaces = File.ReadAllText(WorkspacesPartialPath);
        Assert.IsFalse(
            workspaces.Contains("ManageWorkspaces_Click", StringComparison.Ordinal),
            "Slice 44: handler must be removed from MainWindow.Workspaces.cs.");
    }

    [TestMethod]
    public void MainWindow_manage_workspaces_menu_item_click_attaches_to_shell_bridge()
    {
        var text = File.ReadAllText(MainWindowPath);
        StringAssert.Contains(text, "_manageWorkspacesMenuItem.Click += _manageWorkspacesMenuItemShellBridge.OnManageWorkspacesMenuItemClick");
    }
}
