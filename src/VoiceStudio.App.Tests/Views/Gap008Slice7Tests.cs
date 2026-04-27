using System;
using System.IO;
using System.Reflection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace VoiceStudio.App.Tests.Views;

[TestClass]
public sealed class Gap008Slice7Tests
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

    private static string ToolbarBridgePath =>
        Path.Combine(FindRepoRoot(), "src", "VoiceStudio.App", "Services", "MainWindowToolbarCustomizationShellBridge.cs");

    private static string CustomizeToolbarMenuItemBridgePath =>
        Path.Combine(FindRepoRoot(), "src", "VoiceStudio.App", "Services", "MainWindowCustomizeToolbarMenuItemShellBridge.cs");

    [TestMethod]
    public void MainWindow_toolbar_customize_handler_delegates_to_toolbar_customization_bridge()
    {
        var text = File.ReadAllText(MainWindowPath);
        StringAssert.Contains(text, "_toolbarCustomizationShellBridge");
        StringAssert.Contains(text, "_customizeToolbarMenuItemShellBridge");
        var menuBridgeText = File.ReadAllText(CustomizeToolbarMenuItemBridgePath);
        StringAssert.Contains(menuBridgeText, "ShowCustomizationDialogAsync");
    }

    [TestMethod]
    public void MainWindow_customize_toolbar_menu_click_attaches_to_menu_item_shell_bridge()
    {
        var text = File.ReadAllText(MainWindowPath);
        StringAssert.Contains(text, "_customizeToolbarMenuItem.Click += _customizeToolbarMenuItemShellBridge.OnCustomizeToolbarMenuItemClick");
    }

    [TestMethod]
    public void MainWindowToolbarCustomizationShellBridge_excludes_forbidden_slice7_creep_identifiers()
    {
        var text = File.ReadAllText(ToolbarBridgePath);
        Assert.IsFalse(text.Contains("CommandPalette", StringComparison.Ordinal), "Slice 7 bridge must not reference command palette.");
        Assert.IsFalse(text.Contains("ShowCommandPalette", StringComparison.Ordinal));
        Assert.IsFalse(text.Contains("GlobalSearch", StringComparison.Ordinal));
        Assert.IsFalse(text.Contains("_searchOverlayShellBridge", StringComparison.Ordinal));
        Assert.IsFalse(text.Contains("OpenPanelByIdAsync", StringComparison.Ordinal));
    }
}
