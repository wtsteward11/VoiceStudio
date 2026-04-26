using System;

namespace VoiceStudio.App.Services;

/// <summary>
/// Hooks for the post-DEBUG Loaded tail (GAP-008 Slice 3). MainWindow supplies closures;
/// this type performs only ordered invocation — no service location here.
/// </summary>
public readonly record struct MainWindowLoadedTailHooks
{
    /// <summary>Assigns MainWindow transport field and calls <see cref="TransportShortcutCoordinator.Attach"/>.</summary>
    public required Action RunTransportAttachAndAssign { get; init; }

    /// <summary>Starts deferred panel init (historically fire-and-forget: caller uses <c>_ = Task</c> inside).</summary>
    public required Action RunPanelInitFireAndForget { get; init; }
}

/// <summary>
/// Ordered Loaded tail after shell bootstrap and optional DEBUG block (ADR-047: Loaded-only).
/// </summary>
public static class MainWindowLoadedTailBootstrap
{
    /// <summary>
    /// Run transport shortcut attach then panel-init trigger. Caller must invoke from <c>contentFE.Loaded</c> only,
    /// after <see cref="MainWindowShellLoadedBootstrap"/> and any DEBUG-only block.
    /// </summary>
    public static void Run(MainWindowLoadedTailHooks hooks)
    {
        ArgumentNullException.ThrowIfNull(hooks.RunTransportAttachAndAssign);
        ArgumentNullException.ThrowIfNull(hooks.RunPanelInitFireAndForget);

        hooks.RunTransportAttachAndAssign();
        hooks.RunPanelInitFireAndForget();
    }
}
