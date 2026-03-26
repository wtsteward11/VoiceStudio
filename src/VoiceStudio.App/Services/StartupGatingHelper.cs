using System;
using System.Threading.Tasks;

namespace VoiceStudio.App.Services;

/// <summary>
/// Helper for startup gating logic. Used by MainWindow transport handlers and tests (Round 4).
/// See docs/design/STARTUP_ORCHESTRATION_HARDENING_PLAN.md.
/// </summary>
public static class StartupGatingHelper
{
    /// <summary>
    /// Returns true when transport playback should be blocked (backend not ready).
    /// </summary>
    public static bool ShouldBlockTransportPlayback(IStartupStateService startupState)
        => !startupState.IsReady;

    /// <summary>
    /// Returns true when panel initialization should be deferred until backend ready.
    /// </summary>
    public static bool ShouldDeferPanelInit(IStartupStateService startupState)
        => !startupState.IsReady;

    /// <summary>
    /// Waits for backend ready (or failed/degraded) then runs the init action. Testable helper for RunPanelInitWhenReadyAsync (Round 5 Task 3).
    /// Completes on BackendReady, Degraded, or BackendFailed to avoid deadlock.
    /// </summary>
    public static async Task WaitForBackendReadyThenAsync(IStartupStateService startupState, Func<Task> initAction)
    {
        if (startupState == null) throw new ArgumentNullException(nameof(startupState));
        if (initAction == null) throw new ArgumentNullException(nameof(initAction));

        if (startupState.IsReady)
        {
            await initAction().ConfigureAwait(true);
            return;
        }

        var tcs = new TaskCompletionSource<bool>();
        void Handler(object? s, StartupStateChangedEventArgs e)
        {
            if (e.NewState == StartupState.BackendReady || e.NewState == StartupState.Degraded || e.NewState == StartupState.BackendFailed)
            {
                startupState.StateChanged -= Handler;
                tcs.TrySetResult(true);
            }
        }
        startupState.StateChanged += Handler;
        if (startupState.IsReady)
        {
            startupState.StateChanged -= Handler;
            await initAction().ConfigureAwait(true);
            return;
        }
        await tcs.Task.ConfigureAwait(true);
        await initAction().ConfigureAwait(true);
    }
}
