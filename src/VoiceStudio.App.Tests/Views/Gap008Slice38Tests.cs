using System;
using System.IO;
using System.Reflection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace VoiceStudio.App.Tests.Views;

[TestClass]
public sealed class Gap008Slice38Tests
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
    public void MainWindow_uses_Slice_38_keyboard_key_dispatch_bridge_field()
    {
        var text = File.ReadAllText(MainWindowPath);
        StringAssert.Contains(text, "_keyboardShortcutKeyDispatchShellBridge");
        StringAssert.Contains(text, "new MainWindowKeyboardShortcutKeyDispatchShellBridge(");
    }

    [TestMethod]
    public void MainWindow_ctor_instantiates_key_dispatch_bridge_after_RegisterAll_before_menu_items()
    {
        var text = File.ReadAllText(MainWindowPath);
        var idxRegisterAll = text.IndexOf("_panelQuickSwitchShortcutRegistrationShellBridge.RegisterAll", StringComparison.Ordinal);
        var idxDispatchNew = text.IndexOf("_keyboardShortcutKeyDispatchShellBridge = new MainWindowKeyboardShortcutKeyDispatchShellBridge", StringComparison.Ordinal);
        var idxMenu = text.IndexOf("Menu Items Created", StringComparison.Ordinal);
        Assert.IsTrue(idxRegisterAll >= 0, "Expected RegisterAll call.");
        Assert.IsTrue(idxDispatchNew > idxRegisterAll, "Key dispatch bridge should follow panel quick-switch RegisterAll.");
        Assert.IsTrue(idxMenu > idxDispatchNew, "Key dispatch bridge should precede Menu Items Created.");
    }

    [TestMethod]
    public void MainWindow_KeyDown_forwards_to_dispatch_bridge_without_inline_modifier_assembly()
    {
        var text = File.ReadAllText(MainWindowPath);
        StringAssert.Contains(text, "_keyboardShortcutKeyDispatchShellBridge.TryHandleKeyDown(_keyboardShortcutService, e)");
        Assert.IsFalse(
            text.Contains("var modifiers = VirtualKeyModifiers.None", StringComparison.Ordinal),
            "Modifier assembly should live in MainWindowKeyboardShortcutKeyDispatchShellBridge.");
    }
}
