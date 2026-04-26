using System;
using System.IO;
using System.Reflection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace VoiceStudio.App.Tests.Views;

[TestClass]
public sealed class Gap008Slice30Tests
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
    public void MainWindow_delegates_TogglePlayback_Stop_ToggleRecording_to_Slice_30_bridge()
    {
        var text = File.ReadAllText(MainWindowPath);
        StringAssert.Contains(text, "_globalTransportShellBridge");
        StringAssert.Contains(text, "_globalTransportShellBridge.TogglePlaybackAsync");
        StringAssert.Contains(text, "_globalTransportShellBridge.StopPlayback");
        StringAssert.Contains(text, "_globalTransportShellBridge.ToggleRecordingAsync");
    }

    [TestMethod]
    public void MainWindow_creates_GlobalTransport_ShellBridge_immediately_after_EditUndoRedo_bridge()
    {
        var text = File.ReadAllText(MainWindowPath);
        var idxG = text.IndexOf("new MainWindowGlobalTransportShellBridge", StringComparison.Ordinal);
        var idxE = text.IndexOf("new MainWindowEditUndoRedoShellBridge", StringComparison.Ordinal);
        Assert.IsTrue(idxG >= 0, "MainWindowGlobalTransportShellBridge construction expected.");
        Assert.IsTrue(idxE >= 0, "MainWindowEditUndoRedoShellBridge construction expected.");
        Assert.IsTrue(
            idxG > idxE,
            "Global transport shell bridge should be constructed after the Edit/Undo-Redo shell bridge.");
    }

    [TestMethod]
    public void RegisterKeyboardShortcuts_routes_zoom_through_Slice_30_bridge()
    {
        var text = File.ReadAllText(MainWindowPath);
        var regIdx = text.IndexOf("void RegisterKeyboardShortcuts", StringComparison.Ordinal);
        Assert.IsTrue(regIdx >= 0, "RegisterKeyboardShortcuts expected.");
        var sliceBlock = text.Substring(regIdx, Math.Min(8000, text.Length - regIdx));
        var zIn = sliceBlock.IndexOf("\"zoom.in\"", StringComparison.Ordinal);
        Assert.IsTrue(zIn >= 0, "zoom.in expected.");
        StringAssert.Contains(sliceBlock, "_globalTransportShellBridge.ZoomIn");
        StringAssert.Contains(sliceBlock, "_globalTransportShellBridge.ZoomOut");
        StringAssert.Contains(sliceBlock, "_globalTransportShellBridge.ResetZoom");
    }

    [TestMethod]
    public void TransportShortcutCoordinator_attach_uses_Slice_30_bridge_for_recording_panel()
    {
        var text = File.ReadAllText(MainWindowPath);
        StringAssert.Contains(text, "_globalTransportShellBridge.OpenRecordingPanelFromTransportShortcut");
    }
}
