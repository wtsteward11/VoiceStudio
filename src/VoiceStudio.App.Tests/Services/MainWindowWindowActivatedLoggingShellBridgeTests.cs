using System;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using VoiceStudio.App.Services;

namespace VoiceStudio.App.Tests.Services;

[TestClass]
public sealed class MainWindowWindowActivatedLoggingShellBridgeTests
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
        Path.Combine(FindRepoRoot(), "src", "VoiceStudio.App", "Services", "MainWindowWindowActivatedLoggingShellBridge.cs");

    [TestMethod]
    public void Window_activated_logging_bridge_does_not_reference_startup_welcome_activation_type()
    {
        var text = File.ReadAllText(BridgePath);
        Assert.IsFalse(
            text.Contains("MainWindowStartupWelcomeActivationShellBridge", StringComparison.Ordinal),
            "Anti-creep: Slice 11 welcome bridge must not appear in Slice 40 logging shell.");
    }

    [TestMethod]
    public void Window_activated_logging_bridge_source_uses_ErrorLogger_and_stable_scope()
    {
        var text = File.ReadAllText(BridgePath);
        StringAssert.Contains(text, "ErrorLogger.LogWarning");
        StringAssert.Contains(text, "MainWindow.MainWindow_Activated");
    }

    [TestMethod]
    public async Task RunActivatedAsync_invokes_inner_task()
    {
        var bridge = new MainWindowWindowActivatedLoggingShellBridge();
        var invoked = false;
        await bridge.RunActivatedAsync(() =>
        {
            invoked = true;
            return Task.CompletedTask;
        }).ConfigureAwait(true);

        Assert.IsTrue(invoked);
    }

    [TestMethod]
    public async Task RunActivatedAsync_completes_without_throwing_when_inner_throws()
    {
        var bridge = new MainWindowWindowActivatedLoggingShellBridge();
        await bridge.RunActivatedAsync(() => Task.FromException(new InvalidOperationException("unit-test"))).ConfigureAwait(true);
    }

    [TestMethod]
    public void Window_activated_logging_bridge_does_not_reference_SwitchToPanel()
    {
        var text = File.ReadAllText(BridgePath);
        Assert.IsFalse(text.Contains("SwitchToPanel", StringComparison.Ordinal), "Obsolete panel path must not leak into logging shell.");
    }
}
