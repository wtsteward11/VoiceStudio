using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using VoiceStudio.App.Services;

namespace VoiceStudio.App.Tests.Services;

[TestClass]
public sealed class MainWindowLifetimeCleanupShellBridgeTests
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
        Path.Combine(FindRepoRoot(), "src", "VoiceStudio.App", "Services", "MainWindowLifetimeCleanupShellBridge.cs");

    private static MainWindowClosedPreludeChannels MakePrelude(Action? stop = null, Action? cancel = null, Action? save = null, Action? mark = null) =>
        new MainWindowClosedPreludeChannels
        {
            StopStatusBarTimer = stop ?? (() => { }),
            CancelLayoutSaveDebouncer = cancel ?? (() => { }),
            SaveWorkspaceLayout = save ?? (() => { }),
            TryMarkCleanShutdown = mark ?? (() => { }),
        };

    private sealed class DisposeState
    {
        public bool Disposed;
        public int DisposeClockCalls;
    }

    private static MainWindowLifetimeCleanupCoreChannels MakeCore(
        DisposeState state,
        Action? onDisposeClock = null)
    {
        var noop = () => { };
        Action disposeClock = onDisposeClock ?? (() => state.DisposeClockCalls++);
        return new MainWindowLifetimeCleanupCoreChannels
        {
            GetDisposed = () => state.Disposed,
            SetDisposed = () => { state.Disposed = true; },
            DisposeClockTimer = disposeClock,
            DisposePreviewHideTimer = noop,
            CancelDebouncerAndSaveWorkspace = noop,
            UnsubscribeContentKeyDown = noop,
            UnsubscribeWindowActivated = noop,
            UnsubscribeWindowClosed = noop,
            UnsubscribeWorkspaceProfileChanged = noop,
            DetachNavigationService = noop,
            UnsubscribeStartupOverlay = noop,
            DisposeSessionLifecycle = noop,
            DetachTransportShortcutsAndClear = noop,
            UnsubscribeStatusBarCoordinator = noop,
            DisposeJumpListServiceBestEffort = noop,
            DisposeTaskbarProgressServiceBestEffort = noop,
            CleanupNotificationCenterViewModel = noop,
            CleanupGlobalTransportEvents = noop,
            UnsubscribeShellChromeEvents = noop,
        };
    }

    [TestMethod]
    public void Bridge_ctor_rejects_null_prelude()
    {
        var state = new DisposeState();
        var core = MakeCore(state);
        Assert.ThrowsException<ArgumentNullException>(() => new MainWindowLifetimeCleanupShellBridge(null!, core));
    }

    [TestMethod]
    public void Bridge_ctor_rejects_null_core()
    {
        var prelude = MakePrelude();
        Assert.ThrowsException<ArgumentNullException>(() => new MainWindowLifetimeCleanupShellBridge(prelude, null!));
    }

    [TestMethod]
    public void Bridge_ctor_rejects_null_stop_status_bar_timer_in_prelude()
    {
        var state = new DisposeState();
        var prelude = new MainWindowClosedPreludeChannels
        {
            StopStatusBarTimer = null!,
            CancelLayoutSaveDebouncer = () => { },
            SaveWorkspaceLayout = () => { },
            TryMarkCleanShutdown = () => { },
        };
        var core = MakeCore(state);
        Assert.ThrowsException<ArgumentNullException>(() => new MainWindowLifetimeCleanupShellBridge(prelude, core));
    }

    [TestMethod]
    public void OnClosedPrelude_invokes_actions_in_order()
    {
        var order = new List<string>();
        var prelude = new MainWindowClosedPreludeChannels
        {
            StopStatusBarTimer = () => order.Add("stop"),
            CancelLayoutSaveDebouncer = () => order.Add("cancel"),
            SaveWorkspaceLayout = () => order.Add("save"),
            TryMarkCleanShutdown = () => order.Add("mark"),
        };
        var state = new DisposeState();
        var bridge = new MainWindowLifetimeCleanupShellBridge(prelude, MakeCore(state));
        bridge.OnClosedPrelude();
        CollectionAssert.AreEqual(new[] { "stop", "cancel", "save", "mark" }, order);
    }

    [TestMethod]
    public void RunCleanupCore_second_call_is_idempotent_for_channel_steps()
    {
        var state = new DisposeState();
        var core = MakeCore(state);
        var bridge = new MainWindowLifetimeCleanupShellBridge(MakePrelude(), core);
        bridge.RunCleanupCore();
        bridge.RunCleanupCore();
        Assert.AreEqual(1, state.DisposeClockCalls, "DisposeClockTimer should run once.");
        Assert.IsTrue(state.Disposed);
    }

    [TestMethod]
    public void MainWindowLifetimeCleanupShellBridge_excludes_forbidden_slice13_creep_identifiers()
    {
        var text = File.ReadAllText(BridgePath);
        Assert.IsFalse(text.Contains("MainWindowToolCatalogShellBridge", StringComparison.Ordinal));
        Assert.IsFalse(text.Contains("TryDispatchPendingJumpListActivation", StringComparison.Ordinal));
        Assert.IsFalse(text.Contains("TryDispatchPendingFileActivation", StringComparison.Ordinal));
        Assert.IsFalse(text.Contains("WireNotificationCenter", StringComparison.Ordinal));
        Assert.IsFalse(text.Contains("rhvoice", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(text.Contains("engines/audio/rhvoice", StringComparison.OrdinalIgnoreCase));
    }
}
