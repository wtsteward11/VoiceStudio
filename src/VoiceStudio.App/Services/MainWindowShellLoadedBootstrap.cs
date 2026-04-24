using System;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;

namespace VoiceStudio.App.Services;

/// <summary>
/// Ordered shell bootstrap steps that must run from <see cref="FrameworkElement.Loaded"/> (ADR-047).
/// <see cref="MainWindow"/> supplies hooks so private wiring methods stay on the window partial.
/// </summary>
public readonly record struct MainWindowLoadedBootstrapHooks
{
    public required Action<XamlRoot?> SetErrorDialogRoot { get; init; }

    public required Action WireNotificationCenter { get; init; }

    public required Action WireJumpListShell { get; init; }

    public required Action WireTaskbarProgressShell { get; init; }

    public required Action TryDispatchPendingJumpListActivation { get; init; }

    public required Action TryDispatchPendingFileActivation { get; init; }

    public required Action StartBackendHealthMonitoring { get; init; }

    public required Action EnqueueRecentProjectsMenuRefresh { get; init; }

    public required Action AttachSessionRecoveryHandlers { get; init; }

    public required Func<Task> InitializeThemeAsync { get; init; }

    public required Func<Task> InitializeKeyboardShortcutsAsync { get; init; }

    public required Action ApplyMicaBackdrop { get; init; }

    public required Action InitializeCustomTitleBar { get; init; }
}

public static class MainWindowShellLoadedBootstrap
{
    /// <summary>
    /// Run shell bootstrap after root XamlRoot is valid. Caller must subscribe this from Loaded only.
    /// </summary>
    public static async Task RunAsync(FrameworkElement contentRoot, MainWindowLoadedBootstrapHooks hooks)
    {
        ArgumentNullException.ThrowIfNull(contentRoot);
        hooks.SetErrorDialogRoot(contentRoot.XamlRoot);

        hooks.WireNotificationCenter();
        hooks.WireJumpListShell();
        hooks.WireTaskbarProgressShell();
        hooks.TryDispatchPendingJumpListActivation();
        hooks.TryDispatchPendingFileActivation();

        hooks.StartBackendHealthMonitoring();

        hooks.EnqueueRecentProjectsMenuRefresh();

        hooks.AttachSessionRecoveryHandlers();

        await hooks.InitializeThemeAsync().ConfigureAwait(true);
        await hooks.InitializeKeyboardShortcutsAsync().ConfigureAwait(true);

        hooks.ApplyMicaBackdrop();
        hooks.InitializeCustomTitleBar();
    }
}
