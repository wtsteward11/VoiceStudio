using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using VoiceStudio.App.Services;

namespace VoiceStudio.App.Tests.Services;

[TestClass]
public sealed class MainWindowEditUndoRedoShellBridgeTests
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
        Path.Combine(FindRepoRoot(), "src", "VoiceStudio.App", "Services", "MainWindowEditUndoRedoShellBridge.cs");

    [TestMethod]
    public void Edit_Undo_Redo_bridge_does_not_reference_other_mainwindow_shell_bridge_types()
    {
        var text = File.ReadAllText(BridgePath);
        Assert.IsFalse(text.Contains("MainWindowHelpAboutShellBridge", StringComparison.Ordinal));
        Assert.IsFalse(text.Contains("MainWindowKeyboardShortcutsShellBridge", StringComparison.Ordinal));
        Assert.IsFalse(text.Contains("IBackendClient", StringComparison.Ordinal));
    }

    [TestMethod]
    public void ExecuteUndo_invokes_Undo_on_service_when_CanUndo()
    {
        var service = new UndoRedoService();
        var noop = new NoopUndoableAction("t1");
        service.RegisterAction(noop);
        var bridge = new MainWindowEditUndoRedoShellBridge();
        bridge.ExecuteUndo(
            () => service,
            (_, _) => { });
        Assert.AreEqual(0, service.UndoCount, "Stack should be empty after undo.");
        Assert.IsTrue(noop.DidUndo);
    }

    [TestMethod]
    public void ExecuteUndo_logs_and_does_not_throw_when_getService_throws()
    {
        var errors = new List<Exception>();
        var bridge = new MainWindowEditUndoRedoShellBridge();
        bridge.ExecuteUndo(
            () => throw new InvalidOperationException("get"),
            (ex, _) => errors.Add(ex));
        Assert.AreEqual(1, errors.Count);
        StringAssert.Contains(errors[0].Message, "get");
    }

    [TestMethod]
    public void ExecuteRedo_invokes_Redo_on_service_when_CanRedo()
    {
        var service = new UndoRedoService();
        var a = new NoopUndoableAction("r1");
        service.RegisterAction(a);
        service.Undo();
        a.ResetFlags();
        var bridge = new MainWindowEditUndoRedoShellBridge();
        bridge.ExecuteRedo(
            () => service,
            (_, _) => { });
        Assert.IsTrue(a.DidRedo);
    }

    private sealed class NoopUndoableAction : IUndoableAction
    {
        public NoopUndoableAction(string name) => ActionName = name;
        public string ActionName { get; }
        public bool DidUndo { get; private set; }
        public bool DidRedo { get; private set; }

        public void ResetFlags()
        {
            DidUndo = false;
            DidRedo = false;
        }

        public void Undo() => DidUndo = true;
        public void Redo() => DidRedo = true;
    }
}
