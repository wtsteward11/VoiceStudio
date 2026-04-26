using System;
using System.IO;
using System.Reflection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using VoiceStudio.App.Services;

namespace VoiceStudio.App.Tests.Services;

[TestClass]
public sealed class MainWindowKeyboardShortcutRegistrationShellBridgeTests
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
        Path.Combine(FindRepoRoot(), "src", "VoiceStudio.App", "Services", "MainWindowKeyboardShortcutRegistrationShellBridge.cs");

    [TestMethod]
    public void Registration_bridge_does_not_reference_slice_32_through_35_shell_bridges()
    {
        var text = File.ReadAllText(BridgePath);
        Assert.IsFalse(text.Contains("MainWindowMenuBarShellBridge", StringComparison.Ordinal), "Anti-creep: menu bar is Slice 34.");
        Assert.IsFalse(text.Contains("MainWindowShellChromeShellBridge", StringComparison.Ordinal), "Anti-creep: shell chrome is Slice 32.");
        Assert.IsFalse(text.Contains("MainWindowWorkspaceSplitterShellBridge", StringComparison.Ordinal), "Anti-creep: workspace splitter is Slice 33.");
        Assert.IsFalse(text.Contains("MainWindowToolCatalogPanelHostChromeShellBridge", StringComparison.Ordinal), "Anti-creep: tool catalog panel chrome is Slice 35.");
    }

    [TestMethod]
    public void Register_throws_when_keyboard_shortcut_service_null()
    {
        var bridge = new MainWindowKeyboardShortcutRegistrationShellBridge();
        var ex = Assert.ThrowsException<ArgumentNullException>(() => bridge.Register(null!, null!));
        Assert.IsTrue(
            string.Equals(ex.ParamName, "keyboardShortcutService", StringComparison.Ordinal)
            || string.Equals(ex.ParamName, "service", StringComparison.Ordinal),
            $"Unexpected ParamName: {ex.ParamName}");
    }

    [TestMethod]
    public void Register_throws_when_dependencies_null()
    {
        var bridge = new MainWindowKeyboardShortcutRegistrationShellBridge();
        var service = new KeyboardShortcutService();
        _ = Assert.ThrowsException<ArgumentNullException>(() => bridge.Register(service, null!));
    }
}
