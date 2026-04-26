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

    [TestMethod]
    public void MainWindow_toolbar_customize_handler_delegates_to_toolbar_customization_bridge()
    {
        var text = File.ReadAllText(MainWindowPath);
        StringAssert.Contains(text, "_toolbarCustomizationShellBridge");
        StringAssert.Contains(text, "_toolbarCustomizationShellBridge.ShowCustomizationDialogAsync");
    }

    [TestMethod]
    public void MainWindow_CustomizeToolbar_menu_handler_entry_point_unchanged()
    {
        var text = File.ReadAllText(MainWindowPath);
        StringAssert.Contains(text, "CustomizeToolbarMenuItem_Click");
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
