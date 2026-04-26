using System;
using System.IO;
using System.Reflection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace VoiceStudio.App.Tests.Views;

[TestClass]
public sealed class Gap008Slice36Tests
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
    public void MainWindow_uses_Slice_36_keyboard_shortcut_registration_bridge_field()
    {
        var text = File.ReadAllText(MainWindowPath);
        StringAssert.Contains(text, "_keyboardShortcutRegistrationShellBridge");
        StringAssert.Contains(text, "new MainWindowKeyboardShortcutRegistrationShellBridge(");
    }

    [TestMethod]
    public void MainWindow_ctor_calls_registration_after_recent_projects_bridge_before_menu_items()
    {
        var text = File.ReadAllText(MainWindowPath);
        var idxRecent = text.IndexOf("MainWindowRecentProjectsMenuPopulationShellBridge Created", StringComparison.Ordinal);
        var idxRegister = text.IndexOf("_keyboardShortcutRegistrationShellBridge.Register", StringComparison.Ordinal);
        var idxMenu = text.IndexOf("Menu Items Created", StringComparison.Ordinal);
        Assert.IsTrue(idxRecent >= 0, "Expected recent projects bridge checkpoint.");
        Assert.IsTrue(idxRegister > idxRecent, "Registration should follow recent projects bridge.");
        Assert.IsTrue(idxMenu > idxRegister, "Registration should precede Menu Items Created.");
    }

    [TestMethod]
    public void MainWindow_has_no_private_RegisterKeyboardShortcuts_method()
    {
        var text = File.ReadAllText(MainWindowPath);
        Assert.IsFalse(
            text.Contains("private void RegisterKeyboardShortcuts", StringComparison.Ordinal),
            "Registration should live in MainWindowKeyboardShortcutRegistrationShellBridge.");
    }
}
