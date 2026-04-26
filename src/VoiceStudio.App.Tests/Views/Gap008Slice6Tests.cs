using System;
using System.IO;
using System.Reflection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace VoiceStudio.App.Tests.Views;

[TestClass]
public sealed class Gap008Slice6Tests
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

    private static string ShellBridgePath =>
        Path.Combine(FindRepoRoot(), "src", "VoiceStudio.App", "Services", "MainWindowSearchOverlayShellBridge.cs");

    [TestMethod]
    public void MainWindow_search_overlay_handlers_delegate_to_shell_bridge()
    {
        var text = File.ReadAllText(MainWindowPath);
        StringAssert.Contains(text, "_searchOverlayShellBridge");
        StringAssert.Contains(text, "_searchOverlayShellBridge.Show");
        StringAssert.Contains(text, "_searchOverlayShellBridge.OnNavigateRequestedAsync");
        StringAssert.Contains(text, "_searchOverlayShellBridge.OnOverlayTappedForDismiss");
        StringAssert.Contains(text, "_searchOverlayShellBridge.EnsureGlobalSearchOverlayCollapsed");
    }

    [TestMethod]
    public void MainWindow_GlobalSearch_navigate_handler_forwards_to_shell_bridge()
    {
        var text = File.ReadAllText(MainWindowPath);
        StringAssert.Contains(text, "GlobalSearchView_NavigateRequested");
        StringAssert.Contains(text, "await _searchOverlayShellBridge.OnNavigateRequestedAsync");
    }

    [TestMethod]
    public void MainWindowSearchOverlayShellBridge_excludes_toolbar_identifiers()
    {
        var text = File.ReadAllText(ShellBridgePath);
        Assert.IsFalse(text.Contains("CustomizableToolbar", StringComparison.Ordinal), "Slice 6 shell bridge must not reference toolbar.");
        Assert.IsFalse(text.Contains("CustomizeToolbar", StringComparison.Ordinal), "Slice 6 shell bridge must not reference customize-toolbar.");
    }

    [TestMethod]
    public void MainWindowSearchOverlayShellBridge_collapsed_path_uses_framework_element_pattern()
    {
        var text = File.ReadAllText(ShellBridgePath);
        StringAssert.Contains(text, "TryCollapseGlobalSearchOverlayIfFrameworkElement");
        StringAssert.Contains(text, "is not FrameworkElement");
    }
}
