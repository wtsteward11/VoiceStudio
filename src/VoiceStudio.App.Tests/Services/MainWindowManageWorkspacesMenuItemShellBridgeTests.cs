using System;
using System.IO;
using System.Reflection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using VoiceStudio.App.Services;

namespace VoiceStudio.App.Tests.Services;

[TestClass]
public sealed class MainWindowManageWorkspacesMenuItemShellBridgeTests
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
        Path.Combine(FindRepoRoot(), "src", "VoiceStudio.App", "Services", "MainWindowManageWorkspacesMenuItemShellBridge.cs");

    [TestMethod]
    public void Menu_item_shell_bridge_forwards_via_RunManageWorkspacesAsync()
    {
        var text = File.ReadAllText(BridgePath);
        StringAssert.Contains(text, "RunManageWorkspacesAsync");
    }

    [TestMethod]
    public void Menu_item_shell_bridge_ctor_rejects_null_menu_tool_activation_bridge()
    {
        Assert.ThrowsException<ArgumentNullException>(() =>
            _ = new MainWindowManageWorkspacesMenuItemShellBridge(
                null!,
                () => null,
                () => null));
    }

    [TestMethod]
    public void Menu_item_shell_bridge_ctor_rejects_null_get_xaml_root()
    {
        var inner = new MainWindowMenuToolActivationShellBridge();
        Assert.ThrowsException<ArgumentNullException>(() =>
            _ = new MainWindowManageWorkspacesMenuItemShellBridge(
                inner,
                null!,
                () => null));
    }

    [TestMethod]
    public void Menu_item_shell_bridge_ctor_rejects_null_try_get_toast()
    {
        var inner = new MainWindowMenuToolActivationShellBridge();
        Assert.ThrowsException<ArgumentNullException>(() =>
            _ = new MainWindowManageWorkspacesMenuItemShellBridge(
                inner,
                () => null,
                null!));
    }

    [TestMethod]
    public void Menu_item_shell_bridge_source_does_not_reference_SwitchToPanel()
    {
        var text = File.ReadAllText(BridgePath);
        Assert.IsFalse(text.Contains("SwitchToPanel", StringComparison.Ordinal));
    }

    [TestMethod]
    public void Menu_item_shell_bridge_source_does_not_instantiate_WorkspaceManagerDialog()
    {
        var text = File.ReadAllText(BridgePath);
        Assert.IsFalse(
            text.Contains("WorkspaceManagerDialog", StringComparison.Ordinal),
            "Anti-creep: dialog construction belongs in MainWindowMenuToolActivationShellBridge (Slice 20).");
    }
}
