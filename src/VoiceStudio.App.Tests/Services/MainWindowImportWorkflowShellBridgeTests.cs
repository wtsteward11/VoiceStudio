using System;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using VoiceStudio.App.Services;
using VoiceStudio.Core.Services;

namespace VoiceStudio.App.Tests.Services;

[TestClass]
public sealed class MainWindowImportWorkflowShellBridgeTests
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
        Path.Combine(FindRepoRoot(), "src", "VoiceStudio.App", "Services", "MainWindowImportWorkflowShellBridge.cs");

    [TestMethod]
    public void Import_workflow_bridge_does_not_reference_other_mainwindow_shell_bridge_types()
    {
        var text = File.ReadAllText(BridgePath);
        Assert.IsFalse(text.Contains("MainWindowGlobalTransportShellBridge", StringComparison.Ordinal));
        Assert.IsFalse(text.Contains("MainWindowHelpAboutShellBridge", StringComparison.Ordinal));
        Assert.IsFalse(text.Contains("IBackendClient", StringComparison.Ordinal));
    }

    [TestMethod]
    public void When_startup_not_ready_shows_info_toast_and_does_not_call_import()
    {
        var bridge = new MainWindowImportWorkflowShellBridge();
        var info = (msg: (string?)null, title: (string?)null);
        var import = new CapturingImportService();

        bridge.ImportAudioFile(
            () => new NotReadyStartupState(),
            () => import,
            (m, t) =>
            {
                info.msg = m;
                info.title = t;
            },
            () => IntPtr.Zero);

        Assert.AreEqual("Starting VoiceStudio services…", info.msg);
        Assert.AreEqual("Please wait", info.title);
        Assert.IsFalse(import.Called);
    }

    [TestMethod]
    public void When_ready_and_service_present_calls_ImportAudioFileAsync_with_handle()
    {
        var bridge = new MainWindowImportWorkflowShellBridge();
        var import = new CapturingImportService();
        var expected = new IntPtr(0x4a2b);

        bridge.ImportAudioFile(
            () => new ReadyStartupState(),
            () => import,
            (_, _) => { },
            () => expected);

        Assert.IsTrue(import.Called);
        Assert.AreEqual(expected, import.LastHandle);
    }

    [TestMethod]
    public void When_import_service_null_returns_without_throw()
    {
        var bridge = new MainWindowImportWorkflowShellBridge();
        bridge.ImportAudioFile(
            () => new ReadyStartupState(),
            () => null,
            (_, _) => { },
            () => IntPtr.Zero);
    }

    private sealed class NotReadyStartupState : IStartupStateService
    {
        public StartupState CurrentState => StartupState.BackendStarting;
        public string? FailureMessage => null;
        public bool IsReady => false;
        public event EventHandler<StartupStateChangedEventArgs>? StateChanged;

        public void SetBackendStarting() { }
        public void SetBackendReady() { }
        public void SetBackendFailed(string message) { }
        public void SetDegraded() { }
    }

    private sealed class ReadyStartupState : IStartupStateService
    {
        public StartupState CurrentState => StartupState.BackendReady;
        public string? FailureMessage => null;
        public bool IsReady => true;
        public event EventHandler<StartupStateChangedEventArgs>? StateChanged;

        public void SetBackendStarting() { }
        public void SetBackendReady() { }
        public void SetBackendFailed(string message) { }
        public void SetDegraded() { }
    }

    private sealed class CapturingImportService : IImportWorkflowService
    {
        public bool Called;
        public IntPtr LastHandle;

        public Task<bool> ImportAudioFileAsync(IntPtr parentWindowHandle, CancellationToken ct = default)
        {
            Called = true;
            LastHandle = parentWindowHandle;
            return Task.FromResult(false);
        }
    }
}
