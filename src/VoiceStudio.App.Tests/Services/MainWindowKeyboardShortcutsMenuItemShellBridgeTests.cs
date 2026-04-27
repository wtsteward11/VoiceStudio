using System;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using VoiceStudio.App.Services;

namespace VoiceStudio.App.Tests.Services;

[TestClass]
public sealed class MainWindowKeyboardShortcutsMenuItemShellBridgeTests
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
        Path.Combine(FindRepoRoot(), "src", "VoiceStudio.App", "Services", "MainWindowKeyboardShortcutsMenuItemShellBridge.cs");

    [TestMethod]
    public void Menu_item_shell_bridge_does_not_declare_keyboard_shortcuts_dialog_title()
    {
        var text = File.ReadAllText(BridgePath);
        Assert.IsFalse(
            text.Contains("Title = \"Keyboard Shortcuts\"", StringComparison.Ordinal),
            "Anti-creep: dialog title belongs in MainWindowKeyboardShortcutsShellBridge (Slice 21).");
    }

    [TestMethod]
    public void Menu_item_shell_bridge_forwards_via_RunKeyboardShortcutsMenuFlowAsync()
    {
        var text = File.ReadAllText(BridgePath);
        StringAssert.Contains(text, "RunKeyboardShortcutsMenuFlowAsync");
    }

    [TestMethod]
    public void Menu_item_shell_bridge_ctor_rejects_null_inner_bridge()
    {
        Assert.ThrowsException<ArgumentNullException>(() =>
            _ = new MainWindowKeyboardShortcutsMenuItemShellBridge(
                null!,
                () => null,
                () => throw new InvalidOperationException("unreachable"),
                () => null));
    }

    [TestMethod]
    public async Task RunFlowAsync_completes_when_xaml_root_missing_inner_catch_without_rethrow()
    {
        var inner = new MainWindowKeyboardShortcutsShellBridge();
        var bridge = new MainWindowKeyboardShortcutsMenuItemShellBridge(
            inner,
            () => null,
            () => throw new InvalidOperationException("vm should not be needed before root check"),
            () => null);

        await bridge.RunFlowAsync().ConfigureAwait(true);
    }

    [TestMethod]
    public void Menu_item_shell_bridge_source_does_not_reference_SwitchToPanel()
    {
        var text = File.ReadAllText(BridgePath);
        Assert.IsFalse(text.Contains("SwitchToPanel", StringComparison.Ordinal));
    }
}
