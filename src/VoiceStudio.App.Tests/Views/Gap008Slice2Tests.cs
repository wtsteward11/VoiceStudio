using System;
using System.IO;
using System.Reflection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace VoiceStudio.App.Tests.Views;

[TestClass]
public sealed class Gap008Slice2Tests
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

    private static string BridgePath =>
        Path.Combine(FindRepoRoot(), "src", "VoiceStudio.App", "Services", "MainWindowNavigationShellBridge.cs");

    [TestMethod]
    public void MainWindow_Wires_Navigation_Subscription_Through_NavShellBridge()
    {
        var text = File.ReadAllText(MainWindowPath);
        StringAssert.Contains(text, "_navShellBridge.AttachNavigationService(navigationService)");
        Assert.IsFalse(
            text.Contains("navigationService.NavigationChanged += OnNavigationChanged", StringComparison.Ordinal),
            "Navigation subscription must not wire directly to removed MainWindow handler.");
    }

    [TestMethod]
    public void MainWindow_Cleanup_Detaches_Navigation_Service()
    {
        var text = File.ReadAllText(MainWindowPath);
        StringAssert.Contains(text, "_navShellBridge.DetachNavigationService()");
    }

    [TestMethod]
    public void MainWindow_ExecuteNavCommandAsync_Delegates_To_NavShellBridge()
    {
        var text = File.ReadAllText(MainWindowPath);
        var regionStart = text.IndexOf("#region Navigation Button Click Handlers", StringComparison.Ordinal);
        Assert.IsTrue(regionStart >= 0);
        var region = text.Substring(regionStart, Math.Min(1200, text.Length - regionStart));
        StringAssert.Contains(region, "_navShellBridge.ExecuteNavCommandAsync");
    }

    [TestMethod]
    public void MainWindowNavigationShellBridge_Subscribes_NavigationChanged_And_Forwards_Execute_To_Coordinator()
    {
        var text = File.ReadAllText(BridgePath);
        StringAssert.Contains(text, "NavigationChanged +=");
        StringAssert.Contains(text, "_shell.ExecuteNavCommandAsync");
        StringAssert.Contains(text, "_shell.ResolvePanelIdAlias");
        StringAssert.Contains(text, "OpenPanelByIdAsync");
    }

    [TestMethod]
    public void MainWindowNavigationShellBridge_SetActiveNavButton_Pins_Eight_Rail_Names()
    {
        var text = File.ReadAllText(BridgePath);
        foreach (var name in new[]
                 {
                     "\"NavStudio\"", "\"NavProfiles\"", "\"NavLibrary\"", "\"NavEffects\"", "\"NavTrain\"",
                     "\"NavAnalyze\"", "\"NavSettings\"", "\"NavLogs\""
                 })
        {
            StringAssert.Contains(text, name);
        }
    }

    [TestMethod]
    public void MainWindow_OnNavigationChanged_Core_Uses_Unknown_Panel_Debug_Line_On_Bridge()
    {
        var text = File.ReadAllText(BridgePath);
        StringAssert.Contains(text, "Unknown panel ID in navigation");
    }
}
