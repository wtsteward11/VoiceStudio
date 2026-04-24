using System;
using System.IO;
using System.Reflection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace VoiceStudio.App.Tests.Views;

[TestClass]
public sealed class Gap008Slice1Tests
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

    private static string BootstrapPath =>
        Path.Combine(FindRepoRoot(), "src", "VoiceStudio.App", "Services", "MainWindowShellLoadedBootstrap.cs");

    [TestMethod]
    public void MainWindow_Loaded_Delegates_To_MainWindowShellLoadedBootstrap_RunAsync()
    {
        var text = File.ReadAllText(MainWindowPath);
        StringAssert.Contains(text, "MainWindowShellLoadedBootstrap.RunAsync");
        StringAssert.Contains(text, "new MainWindowLoadedBootstrapHooks");
    }

    [TestMethod]
    public void MainWindow_Loaded_Hooks_Set_ErrorDialog_Root_Via_Delegate_Before_Other_Wiring()
    {
        var text = File.ReadAllText(MainWindowPath);
        var runIdx = text.IndexOf("MainWindowShellLoadedBootstrap.RunAsync", StringComparison.Ordinal);
        Assert.IsTrue(runIdx >= 0, "Expected RunAsync call in MainWindow.");
        var block = text.Substring(runIdx, Math.Min(2500, text.Length - runIdx));
        var setRootIdx = block.IndexOf("SetErrorDialogRoot", StringComparison.Ordinal);
        var wireNotifIdx = block.IndexOf("WireNotificationCenter", StringComparison.Ordinal);
        Assert.IsTrue(setRootIdx >= 0 && wireNotifIdx > setRootIdx, "SetErrorDialogRoot must appear before WireNotificationCenter in hooks object.");
        StringAssert.Contains(block, "ErrorDialogService.Root = root");
    }

    [TestMethod]
    public void MainWindowShellLoadedBootstrap_RunAsync_Orders_ErrorRoot_Before_Wire_And_Mica_After_Async_Inits()
    {
        var text = File.ReadAllText(BootstrapPath);
        StringAssert.Contains(text, "public static async Task RunAsync");

        static int FirstIndex(string src, string needle) =>
            src.IndexOf(needle, StringComparison.Ordinal);

        var iSet = FirstIndex(text, "hooks.SetErrorDialogRoot");
        var iWire = FirstIndex(text, "hooks.WireNotificationCenter");
        var iTheme = FirstIndex(text, "hooks.InitializeThemeAsync");
        var iKeys = FirstIndex(text, "hooks.InitializeKeyboardShortcutsAsync");
        var iMica = FirstIndex(text, "hooks.ApplyMicaBackdrop");
        var iTitle = FirstIndex(text, "hooks.InitializeCustomTitleBar");

        Assert.IsTrue(iSet >= 0 && iWire > iSet, "WireNotificationCenter after SetErrorDialogRoot.");
        Assert.IsTrue(iTheme > iWire, "Theme after shell wiring.");
        Assert.IsTrue(iKeys > iTheme, "Keyboard shortcuts after theme.");
        Assert.IsTrue(iMica > iKeys, "Mica after keyboard init.");
        Assert.IsTrue(iTitle > iMica, "Title bar after Mica.");
    }

    [TestMethod]
    public void MainWindow_Loaded_Still_Contains_Transport_And_Panel_Init_After_Bootstrap_Block()
    {
        var text = File.ReadAllText(MainWindowPath);
        var runIdx = text.IndexOf("MainWindowShellLoadedBootstrap.RunAsync", StringComparison.Ordinal);
        Assert.IsTrue(runIdx >= 0);
        var afterRun = text.Substring(runIdx);
        var debugIdx = afterRun.IndexOf("#if DEBUG", StringComparison.Ordinal);
        var transportIdx = afterRun.IndexOf("_transportShortcutCoordinator", StringComparison.Ordinal);
        var panelIdx = afterRun.IndexOf("RunPanelInitWhenReadyAsync", StringComparison.Ordinal);
        Assert.IsTrue(debugIdx > 0, "Expected DEBUG block after RunAsync.");
        Assert.IsTrue(transportIdx > debugIdx, "Transport coordinator attach must follow DEBUG block.");
        Assert.IsTrue(panelIdx > transportIdx, "Panel init must follow transport attach.");
    }
}
