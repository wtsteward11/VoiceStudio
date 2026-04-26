using System;
using System.IO;
using System.Reflection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace VoiceStudio.App.Tests.Views;

[TestClass]
public sealed class Gap008Slice3Tests
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

    private static string LoadedTailBootstrapPath =>
        Path.Combine(FindRepoRoot(), "src", "VoiceStudio.App", "Services", "MainWindowLoadedTailBootstrap.cs");

    [TestMethod]
    public void MainWindow_Loaded_Order_ShellBootstrap_Before_Debug_Before_Tail()
    {
        var text = File.ReadAllText(MainWindowPath);
        var runIdx = text.IndexOf("MainWindowShellLoadedBootstrap.RunAsync", StringComparison.Ordinal);
        Assert.IsTrue(runIdx >= 0);
        var afterRun = text.Substring(runIdx);
        var debugIdx = afterRun.IndexOf("#if DEBUG", StringComparison.Ordinal);
        var tailIdx = afterRun.IndexOf("MainWindowLoadedTailBootstrap.Run", StringComparison.Ordinal);
        var transportMarkerIdx = afterRun.IndexOf("_transportShortcutCoordinator", StringComparison.Ordinal);
        var panelIdx = afterRun.IndexOf("RunPanelInitWhenReadyAsync", StringComparison.Ordinal);
        Assert.IsTrue(debugIdx > 0, "Expected DEBUG preprocessor block after shell bootstrap RunAsync.");
        Assert.IsTrue(tailIdx > debugIdx, "Loaded tail bootstrap must follow DEBUG block start.");
        Assert.IsTrue(transportMarkerIdx > debugIdx, "Transport coordinator must follow DEBUG block.");
        Assert.IsTrue(panelIdx > transportMarkerIdx, "Panel init must follow transport attach block.");
    }

    [TestMethod]
    public void MainWindow_Loaded_Attach_Before_RunPanelInitWhenReadyAsync_In_Same_Lambda_Tail()
    {
        var text = File.ReadAllText(MainWindowPath);
        var tailIdx = text.IndexOf("MainWindowLoadedTailBootstrap.Run", StringComparison.Ordinal);
        Assert.IsTrue(tailIdx >= 0);
        var tailBlock = text.Substring(tailIdx, Math.Min(2200, text.Length - tailIdx));
        var attachIdx = tailBlock.IndexOf(".Attach(", StringComparison.Ordinal);
        var panelIdx = tailBlock.IndexOf("RunPanelInitWhenReadyAsync", StringComparison.Ordinal);
        Assert.IsTrue(attachIdx >= 0 && panelIdx > attachIdx, "Attach must appear before RunPanelInitWhenReadyAsync in tail hooks.");
    }

    [TestMethod]
    public void MainWindow_Single_ContentFe_Loaded_Subscription()
    {
        var text = File.ReadAllText(MainWindowPath);
        var count = text.Split("contentFE.Loaded +=", StringSplitOptions.None).Length - 1;
        Assert.AreEqual(1, count, "Expected exactly one contentFE.Loaded subscription.");
    }

    [TestMethod]
    public void MainWindow_Cleanup_Detaches_TransportShortcutCoordinator()
    {
        var text = File.ReadAllText(MainWindowPath);
        StringAssert.Contains(text, "_transportShortcutCoordinator?.Detach()");
    }

    [TestMethod]
    public void MainWindowLoadedTailBootstrap_Defines_Ordered_Run()
    {
        var text = File.ReadAllText(LoadedTailBootstrapPath);
        StringAssert.Contains(text, "public static void Run");
        var iAttach = text.IndexOf("hooks.RunTransportAttachAndAssign", StringComparison.Ordinal);
        var iPanel = text.IndexOf("hooks.RunPanelInitFireAndForget", StringComparison.Ordinal);
        Assert.IsTrue(iAttach >= 0 && iPanel > iAttach, "Run must invoke transport hook before panel-init hook.");
    }
}
