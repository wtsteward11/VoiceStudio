using System;
using System.IO;
using System.Reflection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace VoiceStudio.App.Tests.Views;

[TestClass]
public sealed class Gap008Slice11Tests
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

    private static string Slice11BridgePath =>
        Path.Combine(FindRepoRoot(), "src", "VoiceStudio.App", "Services", "MainWindowStartupWelcomeActivationShellBridge.cs");

    [TestMethod]
    public void MainWindow_Activated_delegates_to_startup_welcome_activation_bridge()
    {
        var text = File.ReadAllText(MainWindowPath);
        StringAssert.Contains(text, "_startupWelcomeActivationShellBridge");
        StringAssert.Contains(text, "HandleActivatedAsync(this, e)");
    }

    [TestMethod]
    public void MainWindow_constructed_startup_welcome_activation_bridge_before_workflow()
    {
        var text = File.ReadAllText(MainWindowPath);
        var idxBridge = text.IndexOf("StartupWelcomeActivationShellBridge Created", StringComparison.Ordinal);
        var idxWorkflow = text.IndexOf("MainWindowProjectWorkflowBridge Created", StringComparison.Ordinal);
        Assert.IsTrue(idxBridge >= 0, "Expected bridge profiler checkpoint.");
        Assert.IsTrue(idxWorkflow >= 0, "Expected workflow profiler checkpoint.");
        Assert.IsTrue(idxBridge < idxWorkflow, "Startup/welcome bridge should construct before project workflow bridge.");
    }

    [TestMethod]
    public void MainWindowStartupWelcomeActivationShellBridge_excludes_forbidden_slice11_creep_identifiers()
    {
        var text = File.ReadAllText(Slice11BridgePath);
        Assert.IsFalse(text.Contains("MainWindowToolCatalogShellBridge", StringComparison.Ordinal), "Slice 11 bridge must not reference tool catalog.");
        Assert.IsFalse(text.Contains("MainWindowCommandPaletteShellBridge", StringComparison.Ordinal));
        Assert.IsFalse(text.Contains("MainWindowToolbarCommandShellBridge", StringComparison.Ordinal));
        Assert.IsFalse(text.Contains("MainWindowSearchOverlayShellBridge", StringComparison.Ordinal));
        Assert.IsFalse(text.Contains("MainWindowProjectWorkflowBridge", StringComparison.Ordinal));
        Assert.IsFalse(text.Contains("MainWindowShellLoadedBootstrap", StringComparison.Ordinal));
        Assert.IsFalse(text.Contains("rhvoice", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(text.Contains("engines/audio/rhvoice", StringComparison.OrdinalIgnoreCase));
    }

    [TestMethod]
    public void MainWindowStartupWelcomeActivationShellBridge_preserves_XamlRoot_gate_for_welcome()
    {
        var text = File.ReadAllText(Slice11BridgePath);
        StringAssert.Contains(text, "window.Content?.XamlRoot is not null");
    }
}
