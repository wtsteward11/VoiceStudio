using System;
using System.IO;
using System.Reflection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace VoiceStudio.App.Tests.Views;

[TestClass]
public sealed class Gap008Slice10Tests
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

    private static string ToolCatalogBridgePath =>
        Path.Combine(FindRepoRoot(), "src", "VoiceStudio.App", "Services", "MainWindowToolCatalogShellBridge.cs");

    private static string KeyboardShortcutRegistrationBridgePath =>
        Path.Combine(FindRepoRoot(), "src", "VoiceStudio.App", "Services", "MainWindowKeyboardShortcutRegistrationShellBridge.cs");

    [TestMethod]
    public void MainWindow_ShowToolCatalogAsync_delegates_to_tool_catalog_bridge()
    {
        var text = File.ReadAllText(MainWindowPath);
        StringAssert.Contains(text, "_toolCatalogShellBridge");
        StringAssert.Contains(text, "_toolCatalogShellBridge.RunShowAsync");
    }

    [TestMethod]
    public void MainWindow_nav_toolcatalog_still_targets_ShowToolCatalogAsync()
    {
        var mw = File.ReadAllText(MainWindowPath);
        StringAssert.Contains(mw, "() => { _ = ShowToolCatalogAsync(); }");
        StringAssert.Contains(mw, "_keyboardShortcutRegistrationShellBridge.Register");
        var reg = File.ReadAllText(KeyboardShortcutRegistrationBridgePath);
        StringAssert.Contains(reg, "nav.toolcatalog");
        StringAssert.Contains(reg, "deps.ShowToolCatalog");
    }

    [TestMethod]
    public void MainWindowToolCatalogShellBridge_excludes_forbidden_slice10_creep_identifiers()
    {
        var text = File.ReadAllText(ToolCatalogBridgePath);
        Assert.IsFalse(text.Contains("CommandPalette", StringComparison.Ordinal), "Slice 10 bridge must not reference command palette.");
        Assert.IsFalse(text.Contains("MainWindowCommandPaletteShellBridge", StringComparison.Ordinal));
        Assert.IsFalse(text.Contains("CommandPaletteService", StringComparison.Ordinal));
        Assert.IsFalse(text.Contains("GlobalSearch", StringComparison.Ordinal));
        Assert.IsFalse(text.Contains("_searchOverlayShellBridge", StringComparison.Ordinal));
        Assert.IsFalse(text.Contains("ToolbarCustomization", StringComparison.Ordinal));
        Assert.IsFalse(text.Contains("MainWindowToolbarCustomizationShellBridge", StringComparison.Ordinal));
        Assert.IsFalse(text.Contains("MainWindowToolbarCommandShellBridge", StringComparison.Ordinal));
        Assert.IsFalse(text.Contains("IToolbarShellImportFromToolbar", StringComparison.Ordinal));
        Assert.IsFalse(text.Contains("MainWindowInstance", StringComparison.Ordinal));
    }
}
