using System;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using VoiceStudio.App.Controls;
using VoiceStudio.App.Services;
using VoiceStudio.Core.Panels;
using VoiceStudio.Core.Services;

namespace VoiceStudio.App.Tests.Services;

[TestClass]
public sealed class MainWindowToggleMiniTimelineMenuItemShellBridgeTests
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
        Path.Combine(FindRepoRoot(), "src", "VoiceStudio.App", "Services", "MainWindowToggleMiniTimelineMenuItemShellBridge.cs");

    [TestMethod]
    public void Menu_item_shell_bridge_forwards_via_RunToggleMiniTimelineAsync()
    {
        var text = File.ReadAllText(BridgePath);
        StringAssert.Contains(text, "RunToggleMiniTimelineAsync");
    }

    [TestMethod]
    public void Menu_item_shell_bridge_ctor_rejects_null_menu_tool_activation_bridge()
    {
        Assert.ThrowsException<ArgumentNullException>(() =>
            _ = new MainWindowToggleMiniTimelineMenuItemShellBridge(
                null!,
                () => false,
                _ => { },
                () => null,
                (_, _) => Task.FromResult(false),
                () => { },
                () => null));
    }

    [TestMethod]
    public void Menu_item_shell_bridge_ctor_rejects_null_get_is_mini_timeline_visible()
    {
        var inner = new MainWindowMenuToolActivationShellBridge();
        Assert.ThrowsException<ArgumentNullException>(() =>
            _ = new MainWindowToggleMiniTimelineMenuItemShellBridge(
                inner,
                null!,
                _ => { },
                () => null,
                (_, _) => Task.FromResult(false),
                () => { },
                () => null));
    }

    [TestMethod]
    public void Menu_item_shell_bridge_ctor_rejects_null_set_is_mini_timeline_visible()
    {
        var inner = new MainWindowMenuToolActivationShellBridge();
        Assert.ThrowsException<ArgumentNullException>(() =>
            _ = new MainWindowToggleMiniTimelineMenuItemShellBridge(
                inner,
                () => false,
                null!,
                () => null,
                (_, _) => Task.FromResult(false),
                () => { },
                () => null));
    }

    [TestMethod]
    public void Menu_item_shell_bridge_ctor_rejects_null_get_bottom_panel_host()
    {
        var inner = new MainWindowMenuToolActivationShellBridge();
        Assert.ThrowsException<ArgumentNullException>(() =>
            _ = new MainWindowToggleMiniTimelineMenuItemShellBridge(
                inner,
                () => false,
                _ => { },
                null!,
                (_, _) => Task.FromResult(false),
                () => { },
                () => null));
    }

    [TestMethod]
    public void Menu_item_shell_bridge_ctor_rejects_null_open_panel_by_id_async()
    {
        var inner = new MainWindowMenuToolActivationShellBridge();
        Assert.ThrowsException<ArgumentNullException>(() =>
            _ = new MainWindowToggleMiniTimelineMenuItemShellBridge(
                inner,
                () => false,
                _ => { },
                () => null,
                null!,
                () => { },
                () => null));
    }

    [TestMethod]
    public void Menu_item_shell_bridge_ctor_rejects_null_refresh_menu_item_text()
    {
        var inner = new MainWindowMenuToolActivationShellBridge();
        Assert.ThrowsException<ArgumentNullException>(() =>
            _ = new MainWindowToggleMiniTimelineMenuItemShellBridge(
                inner,
                () => false,
                _ => { },
                () => null,
                (_, _) => Task.FromResult(false),
                null!,
                () => null));
    }

    [TestMethod]
    public void Menu_item_shell_bridge_ctor_rejects_null_try_get_toast()
    {
        var inner = new MainWindowMenuToolActivationShellBridge();
        Assert.ThrowsException<ArgumentNullException>(() =>
            _ = new MainWindowToggleMiniTimelineMenuItemShellBridge(
                inner,
                () => false,
                _ => { },
                () => null,
                (_, _) => Task.FromResult(false),
                () => { },
                null!));
    }

    [TestMethod]
    public void Menu_item_shell_bridge_source_does_not_reference_SwitchToPanel()
    {
        var text = File.ReadAllText(BridgePath);
        Assert.IsFalse(text.Contains("SwitchToPanel", StringComparison.Ordinal));
    }
}
