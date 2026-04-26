using System;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using VoiceStudio.App.Services;

namespace VoiceStudio.App.Tests.Services;

[TestClass]
public sealed class MainWindowKeyboardShortcutsShellBridgeTests
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
        Path.Combine(FindRepoRoot(), "src", "VoiceStudio.App", "Services", "MainWindowKeyboardShortcutsShellBridge.cs");

    [TestMethod]
    public async Task RunKeyboardShortcutsMenuFlowAsync_throws_when_getXamlRoot_null_delegate()
    {
        var bridge = new MainWindowKeyboardShortcutsShellBridge();
        await Assert.ThrowsExceptionAsync<ArgumentNullException>(() =>
            bridge.RunKeyboardShortcutsMenuFlowAsync(
                null!,
                () => throw new InvalidOperationException("unreachable"),
                () => null));
    }

    [TestMethod]
    public async Task RunKeyboardShortcutsMenuFlowAsync_throws_when_getKeyboardCustomizationViewModel_null_delegate()
    {
        var bridge = new MainWindowKeyboardShortcutsShellBridge();
        await Assert.ThrowsExceptionAsync<ArgumentNullException>(() =>
            bridge.RunKeyboardShortcutsMenuFlowAsync(
                () => null,
                null!,
                () => null));
    }

    [TestMethod]
    public async Task RunKeyboardShortcutsMenuFlowAsync_throws_when_getToastForError_null_delegate()
    {
        var bridge = new MainWindowKeyboardShortcutsShellBridge();
        await Assert.ThrowsExceptionAsync<ArgumentNullException>(() =>
            bridge.RunKeyboardShortcutsMenuFlowAsync(
                () => null,
                () => throw new InvalidOperationException("unreachable"),
                null!));
    }

    [TestMethod]
    public void Keyboard_shortcuts_shell_bridge_source_excludes_rhvoice_path_segment()
    {
        var text = File.ReadAllText(BridgePath);
        Assert.IsFalse(
            text.Contains("engines/audio/rhvoice", StringComparison.OrdinalIgnoreCase),
            "Slice 21 keyboard shortcuts shell bridge must not reference RHVoice engine path.");
    }

    [TestMethod]
    public void Keyboard_shortcuts_shell_bridge_source_excludes_unrelated_bridge_type_names()
    {
        var text = File.ReadAllText(BridgePath);
        Assert.IsFalse(text.Contains("MainWindowMenuToolActivationShellBridge", StringComparison.Ordinal));
        Assert.IsFalse(text.Contains("MainWindowStatusBarCoordinatorShellBridge", StringComparison.Ordinal));
        Assert.IsFalse(text.Contains("MainWindowCommandPaletteShellBridge", StringComparison.Ordinal));
    }
}
