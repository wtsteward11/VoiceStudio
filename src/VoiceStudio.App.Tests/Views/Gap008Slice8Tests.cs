using System;
using System.IO;
using System.Reflection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace VoiceStudio.App.Tests.Views;

[TestClass]
public sealed class Gap008Slice8Tests
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

    private static string CommandPaletteBridgePath =>
        Path.Combine(FindRepoRoot(), "src", "VoiceStudio.App", "Services", "MainWindowCommandPaletteShellBridge.cs");

    [TestMethod]
    public void MainWindow_ShowCommandPalette_delegates_to_command_palette_bridge()
    {
        var text = File.ReadAllText(MainWindowPath);
        StringAssert.Contains(text, "_commandPaletteShellBridge");
        StringAssert.Contains(text, "_commandPaletteShellBridge.Show");
    }

    [TestMethod]
    public void MainWindow_nav_commandpalette_still_targets_ShowCommandPalette()
    {
        var text = File.ReadAllText(MainWindowPath);
        StringAssert.Contains(text, "nav.commandpalette");
        StringAssert.Contains(text, "() => ShowCommandPalette()");
    }

    [TestMethod]
    public void MainWindowCommandPaletteShellBridge_excludes_forbidden_slice8_creep_identifiers()
    {
        var text = File.ReadAllText(CommandPaletteBridgePath);
        Assert.IsFalse(text.Contains("ToolbarCustomization", StringComparison.Ordinal), "Slice 8 bridge must not reference toolbar customization.");
        Assert.IsFalse(text.Contains("MainWindowToolbarCustomizationShellBridge", StringComparison.Ordinal));
        Assert.IsFalse(text.Contains("GlobalSearch", StringComparison.Ordinal));
        Assert.IsFalse(text.Contains("_searchOverlayShellBridge", StringComparison.Ordinal));
        Assert.IsFalse(text.Contains("OpenPanelByIdAsync", StringComparison.Ordinal));
    }
}
