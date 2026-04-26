using System;
using System.IO;
using System.Reflection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using VoiceStudio.App.Services;
using VoiceStudio.Core.Panels;

namespace VoiceStudio.App.Tests.Services;

[TestClass]
public sealed class MainWindowToolCatalogPanelHostChromeShellBridgeTests
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
        Path.Combine(FindRepoRoot(), "src", "VoiceStudio.App", "Services", "MainWindowToolCatalogPanelHostChromeShellBridge.cs");

    [TestMethod]
    public void Tool_catalog_panel_chrome_bridge_does_not_reference_menu_bar_bridge()
    {
        var text = File.ReadAllText(BridgePath);
        Assert.IsFalse(
            text.Contains("MainWindowMenuBarShellBridge", StringComparison.Ordinal),
            "Anti-creep: menu bar is Slice 34.");
    }

    [TestMethod]
    public void Tool_catalog_panel_chrome_bridge_mentions_PanelHost_and_PanelRegion()
    {
        var text = File.ReadAllText(BridgePath);
        StringAssert.Contains(text, "PanelHost");
        StringAssert.Contains(text, "PanelRegion");
    }

    [TestMethod]
    public void Apply_does_not_throw_when_host_not_found()
    {
        var bridge = new MainWindowToolCatalogPanelHostChromeShellBridge();
        bridge.Apply(
            PanelRegion.Left,
            "T",
            null,
            static _ => null);
    }

    [TestMethod]
    public void Apply_rejects_null_findNameOnContent()
    {
        var bridge = new MainWindowToolCatalogPanelHostChromeShellBridge();
        _ = Assert.ThrowsException<ArgumentNullException>(() => bridge.Apply(PanelRegion.Left, "t", "i", null!));
    }
}
