using System;
using System.IO;
using System.Reflection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace VoiceStudio.App.Tests.Views;

[TestClass]
public sealed class Gap008Slice29Tests
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
    public void MainWindow_delegates_ExecuteUndo_and_ExecuteRedo_to_Slice_29_bridge()
    {
        var text = File.ReadAllText(MainWindowPath);
        StringAssert.Contains(text, "_editUndoRedoShellBridge");
        StringAssert.Contains(text, "_editUndoRedoShellBridge.ExecuteUndo");
        StringAssert.Contains(text, "_editUndoRedoShellBridge.ExecuteRedo");
    }

    [TestMethod]
    public void MainWindow_creates_EditUndoRedo_ShellBridge_immediately_after_HelpAbout_bridge()
    {
        var text = File.ReadAllText(MainWindowPath);
        var idxEdit = text.IndexOf("new MainWindowEditUndoRedoShellBridge", StringComparison.Ordinal);
        var idxHelp = text.IndexOf("new MainWindowHelpAboutShellBridge", StringComparison.Ordinal);
        Assert.IsTrue(idxEdit >= 0, "MainWindowEditUndoRedoShellBridge construction expected.");
        Assert.IsTrue(idxHelp >= 0, "MainWindowHelpAboutShellBridge construction expected.");
        Assert.IsTrue(
            idxEdit > idxHelp,
            "Edit/Undo-Redo shell bridge should be constructed after the Help/About shell bridge.");
    }

    [TestMethod]
    public void RegisterKeyboardShortcuts_routes_edit_undo_redo_through_Slice_29_bridge()
    {
        var text = File.ReadAllText(MainWindowPath);
        var regIdx = text.IndexOf("void RegisterKeyboardShortcuts", StringComparison.Ordinal);
        Assert.IsTrue(regIdx >= 0, "RegisterKeyboardShortcuts expected.");
        var sliceBlock = text.Substring(regIdx, Math.Min(3000, text.Length - regIdx));
        var editUndoKeyIdx = sliceBlock.IndexOf("\"edit.undo\"", StringComparison.Ordinal);
        var editRedoKeyIdx = sliceBlock.IndexOf("\"edit.redo\"", StringComparison.Ordinal);
        Assert.IsTrue(editUndoKeyIdx >= 0, "edit.undo registration expected inside RegisterKeyboardShortcuts.");
        Assert.IsTrue(editRedoKeyIdx >= 0, "edit.redo registration expected inside RegisterKeyboardShortcuts.");
        StringAssert.Contains(sliceBlock, "_editUndoRedoShellBridge.ExecuteUndo");
        StringAssert.Contains(sliceBlock, "_editUndoRedoShellBridge.ExecuteRedo");
    }
}
