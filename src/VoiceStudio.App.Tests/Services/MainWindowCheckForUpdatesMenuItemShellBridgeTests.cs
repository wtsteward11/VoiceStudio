using System;
using System.IO;
using System.Reflection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using VoiceStudio.App.Services;

namespace VoiceStudio.App.Tests.Services;

[TestClass]
public sealed class MainWindowCheckForUpdatesMenuItemShellBridgeTests
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
        Path.Combine(FindRepoRoot(), "src", "VoiceStudio.App", "Services", "MainWindowCheckForUpdatesMenuItemShellBridge.cs");

    [TestMethod]
    public void Menu_item_shell_bridge_does_not_declare_Update_Check_Failed_title()
    {
        var text = File.ReadAllText(BridgePath);
        Assert.IsFalse(
            text.Contains("Update Check Failed", StringComparison.Ordinal),
            "Anti-creep: error title belongs in MainWindowMenuToolActivationShellBridge (Slice 20).");
    }

    [TestMethod]
    public void Menu_item_shell_bridge_forwards_via_RunCheckForUpdatesAsync()
    {
        var text = File.ReadAllText(BridgePath);
        StringAssert.Contains(text, "RunCheckForUpdatesAsync");
    }

    [TestMethod]
    public void Menu_item_shell_bridge_ctor_rejects_null_menu_tool_activation_bridge()
    {
        var update = new Mock<IUpdateService>(MockBehavior.Loose).Object;
        Assert.ThrowsException<ArgumentNullException>(() =>
            _ = new MainWindowCheckForUpdatesMenuItemShellBridge(
                null!,
                () => null!,
                update,
                () => null!));
    }

    [TestMethod]
    public void Menu_item_shell_bridge_ctor_rejects_null_get_view_model_context()
    {
        var inner = new MainWindowMenuToolActivationShellBridge();
        var update = new Mock<IUpdateService>(MockBehavior.Loose).Object;
        Assert.ThrowsException<ArgumentNullException>(() =>
            _ = new MainWindowCheckForUpdatesMenuItemShellBridge(
                inner,
                null!,
                update,
                () => null!));
    }

    [TestMethod]
    public void Menu_item_shell_bridge_ctor_rejects_null_update_service()
    {
        var inner = new MainWindowMenuToolActivationShellBridge();
        Assert.ThrowsException<ArgumentNullException>(() =>
            _ = new MainWindowCheckForUpdatesMenuItemShellBridge(
                inner,
                () => null!,
                null!,
                () => null!));
    }

    [TestMethod]
    public void Menu_item_shell_bridge_ctor_rejects_null_get_error_dialog_service()
    {
        var inner = new MainWindowMenuToolActivationShellBridge();
        var update = new Mock<IUpdateService>(MockBehavior.Loose).Object;
        Assert.ThrowsException<ArgumentNullException>(() =>
            _ = new MainWindowCheckForUpdatesMenuItemShellBridge(
                inner,
                () => null!,
                update,
                null!));
    }

    [TestMethod]
    public void Menu_item_shell_bridge_source_does_not_reference_SwitchToPanel()
    {
        var text = File.ReadAllText(BridgePath);
        Assert.IsFalse(text.Contains("SwitchToPanel", StringComparison.Ordinal));
    }
}
