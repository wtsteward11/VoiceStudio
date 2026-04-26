using System;
using System.IO;
using System.Reflection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace VoiceStudio.App.Tests.Views;

[TestClass]
public sealed class Gap008Slice26Tests
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

    private static string StartupOverlayBridgePath =>
        Path.Combine(FindRepoRoot(), "src", "VoiceStudio.App", "Services", "MainWindowStartupOverlayShellBridge.cs");

    [TestMethod]
    public void MainWindow_delegates_startup_overlay_to_slice26_bridge()
    {
        var text = File.ReadAllText(MainWindowPath);
        StringAssert.Contains(text, "_startupOverlayShellBridge");
        StringAssert.Contains(text, "_startupOverlayShellBridge.ApplyStartupOverlay");
        StringAssert.Contains(text, "_startupOverlayShellBridge.OnStartupStateChanged");
    }

    [TestMethod]
    public void MainWindow_creates_StartupOverlayShellBridge_after_welcome_bridge()
    {
        var text = File.ReadAllText(MainWindowPath);
        var idxOverlay = text.IndexOf("new MainWindowStartupOverlayShellBridge", StringComparison.Ordinal);
        var idxWelcome = text.IndexOf("new MainWindowStartupWelcomeActivationShellBridge", StringComparison.Ordinal);
        Assert.IsTrue(idxOverlay >= 0, "MainWindowStartupOverlayShellBridge construction expected.");
        Assert.IsTrue(idxWelcome >= 0, "MainWindowStartupWelcomeActivationShellBridge construction expected.");
        Assert.IsTrue(
            idxOverlay > idxWelcome,
            "Startup overlay shell bridge should be constructed after the welcome-activation shell bridge (same startup cluster).");
    }

    [TestMethod]
    public void MainWindow_does_not_contain_UpdateStartupOverlay_symbol()
    {
        var text = File.ReadAllText(MainWindowPath);
        Assert.IsFalse(
            text.Contains("UpdateStartupOverlay", StringComparison.Ordinal),
            "UpdateStartupOverlay must be removed; use MainWindowStartupOverlayShellBridge.ApplyStartupOverlay. (Toasts may still use similar copy.)");
    }

    [TestMethod]
    public void MainWindow_StartupRetry_Click_delegates_to_slice26_bridge()
    {
        var text = File.ReadAllText(MainWindowPath);
        StringAssert.Contains(text, "StartupRetryButton_Click");
        StringAssert.Contains(text, "_startupOverlayShellBridge.OnRetryButtonClickAsync");
    }

    [TestMethod]
    public void Startup_overlay_bridge_file_contains_VoiceStudio_services_string()
    {
        var text = File.ReadAllText(StartupOverlayBridgePath);
        StringAssert.Contains(text, "Starting VoiceStudio services");
    }

    [TestMethod]
    public void MainWindow_does_not_define_private_UpdateStartupOverlay_method()
    {
        var text = File.ReadAllText(MainWindowPath);
        Assert.IsFalse(
            text.Contains("private void UpdateStartupOverlay", StringComparison.Ordinal),
            "UpdateStartupOverlay must be replaced by MainWindowStartupOverlayShellBridge.");
    }
}
