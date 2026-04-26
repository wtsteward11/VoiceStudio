using System;
using System.IO;
using System.Reflection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace VoiceStudio.App.Tests.Views;

[TestClass]
public sealed class Gap008Slice34Tests
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
    public void MainWindow_uses_Slice_34_menu_bar_bridge_field_and_type()
    {
        var text = File.ReadAllText(MainWindowPath);
        StringAssert.Contains(text, "_menuBarShellBridge");
        StringAssert.Contains(text, "new MainWindowMenuBarShellBridge(");
    }

    [TestMethod]
    public void MainWindow_creates_menu_bar_ShellBridge_after_code_behind_menu_items()
    {
        var text = File.ReadAllText(MainWindowPath);
        var idxB = text.IndexOf("new MainWindowMenuBarShellBridge", StringComparison.Ordinal);
        var idxM = text.IndexOf("Menu Items Created", StringComparison.Ordinal);
        Assert.IsTrue(idxB > 0, "MainWindowMenuBarShellBridge construction expected.");
        Assert.IsTrue(idxM > 0, "Menu Items Created checkpoint expected.");
        Assert.IsTrue(
            idxB > idxM,
            "Menu bar shell bridge should be constructed after in-code menu flyout items (Phase 0).");
    }

    [TestMethod]
    public void MainWindow_calls_InitializeMenuBar_on_menu_bar_ShellBridge()
    {
        var text = File.ReadAllText(MainWindowPath);
        StringAssert.Contains(text, "_menuBarShellBridge.InitializeMenuBar(");
    }
}
