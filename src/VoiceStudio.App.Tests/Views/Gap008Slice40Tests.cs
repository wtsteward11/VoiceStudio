using System;
using System.IO;
using System.Reflection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace VoiceStudio.App.Tests.Views;

[TestClass]
public sealed class Gap008Slice40Tests
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
    public void MainWindow_declares_window_activated_logging_bridge_field()
    {
        var text = File.ReadAllText(MainWindowPath);
        StringAssert.Contains(text, "_windowActivatedLoggingShellBridge");
        StringAssert.Contains(text, "new MainWindowWindowActivatedLoggingShellBridge(");
    }

    [TestMethod]
    public void MainWindow_ctor_instantiates_logging_bridge_after_startup_welcome_and_before_overlay()
    {
        var text = File.ReadAllText(MainWindowPath);
        var idxWelcome = text.IndexOf("_startupWelcomeActivationShellBridge = new MainWindowStartupWelcomeActivationShellBridge", StringComparison.Ordinal);
        var idxLog = text.IndexOf("_windowActivatedLoggingShellBridge = new MainWindowWindowActivatedLoggingShellBridge", StringComparison.Ordinal);
        var idxOverlay = text.IndexOf("_startupOverlayShellBridge = new MainWindowStartupOverlayShellBridge", StringComparison.Ordinal);
        Assert.IsTrue(idxWelcome >= 0);
        Assert.IsTrue(idxLog > idxWelcome);
        Assert.IsTrue(idxOverlay > idxLog);
    }

    [TestMethod]
    public void MainWindow_Activated_forwards_through_window_activated_logging_bridge()
    {
        var text = File.ReadAllText(MainWindowPath);
        StringAssert.Contains(text, "_windowActivatedLoggingShellBridge");
        StringAssert.Contains(text, "RunActivatedAsync");
        StringAssert.Contains(text, "_startupWelcomeActivationShellBridge.HandleActivatedAsync(this, e)");
    }

    [TestMethod]
    public void MainWindow_Activated_has_no_inline_try_catch_around_HandleActivatedAsync()
    {
        var text = File.ReadAllText(MainWindowPath);
        const string marker = "private async void MainWindow_Activated";
        var idxActivated = text.IndexOf(marker, StringComparison.Ordinal);
        Assert.IsTrue(idxActivated >= 0, "Expected explicit MainWindow_Activated handler.");
        var tail = text.AsSpan(idxActivated);
        var idxHandle = tail.IndexOf("HandleActivatedAsync", StringComparison.Ordinal);
        Assert.IsTrue(idxHandle >= 0);
        var windowTail = tail[..idxHandle];
        Assert.IsFalse(
            windowTail.Contains("try", StringComparison.Ordinal) && windowTail.Contains("catch", StringComparison.Ordinal),
            "Slice 40: try/catch must live in MainWindowWindowActivatedLoggingShellBridge, not MainWindow_Activated.");
    }
}
