using System;
using System.IO;
using System.Reflection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace VoiceStudio.App.Tests.Views;

[TestClass]
public sealed class Gap008Slice35Tests
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
    public void MainWindow_uses_Slice_35_tool_catalog_panel_host_chrome_bridge_field()
    {
        var text = File.ReadAllText(MainWindowPath);
        StringAssert.Contains(text, "_toolCatalogPanelHostChromeShellBridge");
        StringAssert.Contains(text, "new MainWindowToolCatalogPanelHostChromeShellBridge(");
    }

    [TestMethod]
    public void MainWindow_WireToolCatalogHandlers_uses_ToolCatalogPanelHostChrome_apply()
    {
        var text = File.ReadAllText(MainWindowPath);
        StringAssert.Contains(text, "WireToolCatalogHandlers");
        StringAssert.Contains(text, "_toolCatalogPanelHostChromeShellBridge");
        StringAssert.Contains(text, "FindNameOnContent");
    }

    [TestMethod]
    public void MainWindow_has_no_stale_private_ApplyToolCatalogPanelHostChrome_method()
    {
        var text = File.ReadAllText(MainWindowPath);
        Assert.IsFalse(
            text.Contains("private void ApplyToolCatalogPanelHostChrome", StringComparison.Ordinal),
            "Apply logic should live in MainWindowToolCatalogPanelHostChromeShellBridge.");
    }
}
