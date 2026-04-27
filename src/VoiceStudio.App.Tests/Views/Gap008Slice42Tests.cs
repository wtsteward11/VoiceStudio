using System;
using System.IO;
using System.Reflection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace VoiceStudio.App.Tests.Views;

[TestClass]
public sealed class Gap008Slice42Tests
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
    public void MainWindow_declares_customize_toolbar_menu_item_shell_bridge_field()
    {
        var text = File.ReadAllText(MainWindowPath);
        StringAssert.Contains(text, "_customizeToolbarMenuItemShellBridge");
        StringAssert.Contains(text, "new MainWindowCustomizeToolbarMenuItemShellBridge(");
    }

    [TestMethod]
    public void MainWindow_ctor_instantiates_menu_item_bridge_after_toolbar_customization_and_before_command_palette()
    {
        var text = File.ReadAllText(MainWindowPath);
        var idxToolbar = text.IndexOf("_toolbarCustomizationShellBridge = new MainWindowToolbarCustomizationShellBridge", StringComparison.Ordinal);
        var idxMenuItem = text.IndexOf("_customizeToolbarMenuItemShellBridge = new MainWindowCustomizeToolbarMenuItemShellBridge", StringComparison.Ordinal);
        var idxPalette = text.IndexOf("_commandPaletteShellBridge = new MainWindowCommandPaletteShellBridge", StringComparison.Ordinal);
        Assert.IsTrue(idxToolbar >= 0);
        Assert.IsTrue(idxMenuItem > idxToolbar);
        Assert.IsTrue(idxPalette > idxMenuItem);
    }

    [TestMethod]
    public void MainWindow_has_no_private_CustomizeToolbarMenuItem_Click_handler()
    {
        var text = File.ReadAllText(MainWindowPath);
        Assert.IsFalse(
            text.Contains("CustomizeToolbarMenuItem_Click", StringComparison.Ordinal),
            "Slice 42: handler name must live on MainWindowCustomizeToolbarMenuItemShellBridge only.");
    }

    [TestMethod]
    public void MainWindow_customize_toolbar_menu_item_click_attaches_to_shell_bridge()
    {
        var text = File.ReadAllText(MainWindowPath);
        StringAssert.Contains(text, "_customizeToolbarMenuItem.Click += _customizeToolbarMenuItemShellBridge.OnCustomizeToolbarMenuItemClick");
    }
}
