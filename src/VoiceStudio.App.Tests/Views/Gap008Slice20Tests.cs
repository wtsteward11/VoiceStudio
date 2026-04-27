using System;
using System.IO;
using System.Reflection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace VoiceStudio.App.Tests.Views;

[TestClass]
public sealed class Gap008Slice20Tests
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

    private static string KeyboardShortcutsMenuItemBridgePath =>
        Path.Combine(FindRepoRoot(), "src", "VoiceStudio.App", "Services", "MainWindowKeyboardShortcutsMenuItemShellBridge.cs");

    private static string WorkspacesPartialPath =>
        Path.Combine(FindRepoRoot(), "src", "VoiceStudio.App", "MainWindow.Workspaces.cs");

    [TestMethod]
    public void MainWindow_declares_menu_tool_activation_shell_bridge_field()
    {
        var text = File.ReadAllText(MainWindowPath);
        StringAssert.Contains(text, "_menuToolActivationShellBridge");
        StringAssert.Contains(text, "MainWindowMenuToolActivationShellBridge");
    }

    [TestMethod]
    public void MainWindow_CheckForUpdatesMenuItem_Click_delegates_to_menu_tool_activation_bridge()
    {
        var text = File.ReadAllText(MainWindowPath);
        StringAssert.Contains(text, "_checkForUpdatesMenuItemShellBridge");
        StringAssert.Contains(text, "OnCheckForUpdatesMenuItemClick");
        StringAssert.Contains(text, "new MainWindowCheckForUpdatesMenuItemShellBridge(");
        StringAssert.Contains(text, "_menuToolActivationShellBridge");
    }

    [TestMethod]
    public void MainWindow_ToggleMiniTimelineMenuItem_Click_delegates_to_menu_tool_activation_bridge()
    {
        var text = File.ReadAllText(MainWindowPath);
        StringAssert.Contains(text, "ToggleMiniTimelineMenuItem_Click");
        StringAssert.Contains(text, "RunToggleMiniTimelineAsync");
    }

    [TestMethod]
    public void MainWindow_collaboration_handlers_delegate_to_menu_tool_activation_bridge()
    {
        var text = File.ReadAllText(MainWindowPath);
        StringAssert.Contains(text, "CollaboratorsToggleButton_Click");
        StringAssert.Contains(text, "ToggleCollaborationPanelVisibility");
        StringAssert.Contains(text, "CollaborationIndicator_CloseRequested");
        StringAssert.Contains(text, "HideCollaborationPanel");
    }

    [TestMethod]
    public void MainWindow_Workspaces_ManageWorkspaces_Click_delegates_to_menu_tool_activation_bridge()
    {
        var text = File.ReadAllText(WorkspacesPartialPath);
        StringAssert.Contains(text, "ManageWorkspaces_Click");
        StringAssert.Contains(text, "_menuToolActivationShellBridge");
        StringAssert.Contains(text, "RunManageWorkspacesAsync");
    }

    [TestMethod]
    public void MainWindow_menu_tool_activation_bridge_constructed_after_status_bar_coordinator_bridge()
    {
        var text = File.ReadAllText(MainWindowPath);
        var idxCoord = text.IndexOf("MainWindowStatusBarCoordinatorShellBridge Created", StringComparison.Ordinal);
        var idxMenu = text.IndexOf("MainWindowMenuToolActivationShellBridge Created", StringComparison.Ordinal);
        Assert.IsTrue(idxCoord >= 0, "Expected coordinator shell bridge profiler checkpoint.");
        Assert.IsTrue(idxMenu >= 0, "Expected menu/tool activation shell bridge profiler checkpoint.");
        Assert.IsTrue(idxCoord < idxMenu, "Menu/tool activation bridge should construct after status bar coordinator bridge.");
    }

    [TestMethod]
    public void MainWindow_keyboard_shortcuts_menu_item_click_does_not_use_menu_tool_activation_bridge()
    {
        var main = File.ReadAllText(MainWindowPath);
        const string clickWire =
            "_keyboardShortcutsMenuItem.Click += _keyboardShortcutsMenuItemShellBridge.OnKeyboardShortcutsMenuItemClick";
        StringAssert.Contains(main, clickWire);

        var menuItemBridge = File.ReadAllText(KeyboardShortcutsMenuItemBridgePath);
        Assert.IsFalse(
            menuItemBridge.Contains("MainWindowMenuToolActivationShellBridge", StringComparison.Ordinal),
            "Slice 41 menu-item bridge must not reference menu/tool activation bridge (Slice 20 seam boundary).");
    }
}
