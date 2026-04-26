using System;
using System.IO;
using System.Reflection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace VoiceStudio.App.Tests.Views;

[TestClass]
public sealed class Gap008Slice9Tests
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

    private static string ToolbarCommandBridgePath =>
        Path.Combine(FindRepoRoot(), "src", "VoiceStudio.App", "Services", "MainWindowToolbarCommandShellBridge.cs");

    private static string CustomizableToolbarPath =>
        Path.Combine(FindRepoRoot(), "src", "VoiceStudio.App", "Controls", "CustomizableToolbar.xaml.cs");

    [TestMethod]
    public void MainWindow_wires_toolbar_command_shell_bridge_import_handler()
    {
        var text = File.ReadAllText(MainWindowPath);
        StringAssert.Contains(text, "_toolbarCommandShellBridge");
        StringAssert.Contains(text, "WireImportAudioHandler");
        StringAssert.Contains(text, "ImportAudioFile");
    }

    [TestMethod]
    public void CustomizableToolbar_HandleToolbarButtonClick_path_does_not_use_App_MainWindowInstance()
    {
        var text = File.ReadAllText(CustomizableToolbarPath);
        Assert.IsFalse(text.Contains("MainWindowInstance", StringComparison.Ordinal), "Toolbar control must not use App.MainWindowInstance for command dispatch.");
    }

    [TestMethod]
    public void MainWindowToolbarCommandShellBridge_excludes_forbidden_slice9_creep_identifiers()
    {
        var text = File.ReadAllText(ToolbarCommandBridgePath);
        Assert.IsFalse(text.Contains("CommandPalette", StringComparison.Ordinal), "Slice 9 bridge must not reference command palette.");
        Assert.IsFalse(text.Contains("ShowCommandPalette", StringComparison.Ordinal));
        Assert.IsFalse(text.Contains("MainWindowCommandPaletteShellBridge", StringComparison.Ordinal));
        Assert.IsFalse(text.Contains("GlobalSearch", StringComparison.Ordinal));
        Assert.IsFalse(text.Contains("_searchOverlayShellBridge", StringComparison.Ordinal));
        Assert.IsFalse(text.Contains("ToolbarCustomization", StringComparison.Ordinal));
        Assert.IsFalse(text.Contains("MainWindowToolbarCustomizationShellBridge", StringComparison.Ordinal));
        Assert.IsFalse(text.Contains("OpenPanelByIdAsync", StringComparison.Ordinal));
    }
}
