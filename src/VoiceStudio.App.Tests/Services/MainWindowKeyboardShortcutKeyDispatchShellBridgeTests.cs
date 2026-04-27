using System;
using System.IO;
using System.Reflection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using VoiceStudio.App.Services;

namespace VoiceStudio.App.Tests.Services;

[TestClass]
public sealed class MainWindowKeyboardShortcutKeyDispatchShellBridgeTests
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
        Path.Combine(FindRepoRoot(), "src", "VoiceStudio.App", "Services", "MainWindowKeyboardShortcutKeyDispatchShellBridge.cs");

    [TestMethod]
    public void Key_dispatch_bridge_does_not_reference_keyboard_shortcut_registration_bridge_type_name()
    {
        var text = File.ReadAllText(BridgePath);
        Assert.IsFalse(
            text.Contains("MainWindowKeyboardShortcutRegistrationShellBridge", StringComparison.Ordinal),
            "Anti-creep: Slice 36 registration bridge must not appear in Slice 38 dispatch-only bridge.");
    }

    [TestMethod]
    public void Key_dispatch_bridge_source_does_not_call_RegisterShortcut()
    {
        var text = File.ReadAllText(BridgePath);
        Assert.IsFalse(
            text.Contains("RegisterShortcut", StringComparison.Ordinal),
            "Dispatch bridge must not perform shortcut registration.");
    }

    [TestMethod]
    public void TryHandleKeyDown_throws_when_keyboard_shortcut_service_null()
    {
        var bridge = new MainWindowKeyboardShortcutKeyDispatchShellBridge();
        var ex = Assert.ThrowsException<ArgumentNullException>(() =>
            bridge.TryHandleKeyDown(null!, null!));
        Assert.AreEqual("keyboardShortcutService", ex.ParamName);
    }

    [TestMethod]
    public void TryHandleKeyDown_throws_when_key_routed_event_args_null()
    {
        var bridge = new MainWindowKeyboardShortcutKeyDispatchShellBridge();
        var service = new KeyboardShortcutService();
        var ex = Assert.ThrowsException<ArgumentNullException>(() =>
            bridge.TryHandleKeyDown(service, null!));
        Assert.AreEqual("e", ex.ParamName);
    }
}
