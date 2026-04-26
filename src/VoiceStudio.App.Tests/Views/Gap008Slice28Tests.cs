using System;
using System.IO;
using System.Reflection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace VoiceStudio.App.Tests.Views;

[TestClass]
public sealed class Gap008Slice28Tests
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

    private static string HelpAboutBridgePath =>
        Path.Combine(FindRepoRoot(), "src", "VoiceStudio.App", "Services", "MainWindowHelpAboutShellBridge.cs");

    [TestMethod]
    public void MainWindow_delegates_Help_Documentation_and_About_to_slice28_bridge()
    {
        var text = File.ReadAllText(MainWindowPath);
        StringAssert.Contains(text, "_helpAboutShellBridge");
        StringAssert.Contains(text, "_helpAboutShellBridge.OpenDocumentationFolder");
        StringAssert.Contains(text, "_helpAboutShellBridge.ShowAboutDialogAsync");
    }

    [TestMethod]
    public void MainWindow_creates_HelpAbout_ShellBridge_after_KeyboardShortcuts_bridge()
    {
        var text = File.ReadAllText(MainWindowPath);
        var idxHelp = text.IndexOf("new MainWindowHelpAboutShellBridge", StringComparison.Ordinal);
        var idxKb = text.IndexOf("new MainWindowKeyboardShortcutsShellBridge", StringComparison.Ordinal);
        Assert.IsTrue(idxHelp >= 0, "MainWindowHelpAboutShellBridge construction expected.");
        Assert.IsTrue(idxKb >= 0, "MainWindowKeyboardShortcutsShellBridge construction expected.");
        Assert.IsTrue(
            idxHelp > idxKb,
            "Help/About shell bridge should be constructed after the keyboard shortcuts shell bridge.");
    }

    [TestMethod]
    public void OpenDocumentationFolder_and_About_content_live_in_Slice_28_bridge_file()
    {
        var mw = File.ReadAllText(MainWindowPath);
        Assert.IsFalse(
            mw.Contains("VoiceStudio Quantum+", StringComparison.Ordinal),
            "About title string should not remain in MainWindow — see MainWindowHelpAboutShellBridge.");
        var bridge = File.ReadAllText(HelpAboutBridgePath);
        StringAssert.Contains(bridge, "VoiceStudio Quantum+");
        StringAssert.Contains(bridge, "OpenDocumentationFolder");
    }
}
