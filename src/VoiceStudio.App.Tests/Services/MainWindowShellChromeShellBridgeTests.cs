using System;
using System.IO;
using System.Reflection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace VoiceStudio.App.Tests.Services;

[TestClass]
public sealed class MainWindowShellChromeShellBridgeTests
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
        Path.Combine(FindRepoRoot(), "src", "VoiceStudio.App", "Services", "MainWindowShellChromeShellBridge.cs");

    [TestMethod]
    public void Shell_chrome_bridge_does_not_reference_other_Gap008_shell_navigation_or_import_bridges()
    {
        var text = File.ReadAllText(BridgePath);
        Assert.IsFalse(text.Contains("MainWindowImportWorkflowShellBridge", StringComparison.Ordinal));
        Assert.IsFalse(text.Contains("MainWindowNavigationShellBridge", StringComparison.Ordinal));
        Assert.IsFalse(text.Contains("IBackendClient", StringComparison.Ordinal));
    }

    [TestMethod]
    public void Shell_chrome_bridge_constructor_validates_window_and_root_grid()
    {
        var text = File.ReadAllText(BridgePath);
        StringAssert.Contains(text, "ArgumentNullException.ThrowIfNull(window)");
        StringAssert.Contains(text, "ArgumentNullException.ThrowIfNull(rootGrid)");
    }

    [TestMethod]
    public void Unsubscribe_is_idempotent_when_no_theme_handler()
    {
        var text = File.ReadAllText(BridgePath);
        StringAssert.Contains(text, "if (_shellThemeChangedHandler == null)");
    }
}
