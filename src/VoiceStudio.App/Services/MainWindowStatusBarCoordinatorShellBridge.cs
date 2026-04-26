// VoiceStudio — GAP-008 Slice 19: MainWindow shell wiring into StatusBarCoordinator only (no coordinator logic).

using System;
using Microsoft.UI.Dispatching;
using VoiceStudio.Core.Services;

namespace VoiceStudio.App.Services;

/// <summary>
/// Delegates <see cref="StatusBarCoordinator"/> resolve, <see cref="StatusBarCoordinator.Attach"/>,
/// <see cref="StatusBarCoordinator.Subscribe"/>, and post-loaded <see cref="StatusBarCoordinator.StartBackendHealthMonitoring"/>.
/// Coordinator implementation stays in <see cref="StatusBarCoordinator"/>.
/// </summary>
public sealed class MainWindowStatusBarCoordinatorShellBridge
{
    /// <summary>
    /// Resolves the coordinator from DI; if non-null, attaches and subscribes. Returns the instance for MainWindow to retain.
    /// </summary>
    public StatusBarCoordinator? ResolveAttachSubscribe(
        Func<StatusBarCoordinator?> resolveCoordinator,
        DispatcherQueue dispatcherQueue,
        Func<string, object?> findNameOnContent,
        IContextManager contextManager,
        StatusBarActivityService? activityService,
        GracefulDegradationService? gracefulDegradation)
    {
        ArgumentNullException.ThrowIfNull(resolveCoordinator);
        ArgumentNullException.ThrowIfNull(dispatcherQueue);
        ArgumentNullException.ThrowIfNull(findNameOnContent);
        ArgumentNullException.ThrowIfNull(contextManager);

        var coordinator = resolveCoordinator();
        if (coordinator is null)
        {
            return null;
        }

        coordinator.Attach(dispatcherQueue, findNameOnContent);
        coordinator.Subscribe(contextManager, activityService, gracefulDegradation);
        return coordinator;
    }

    /// <summary>
    /// GAP-067 slice 7 prelude: start backend monitoring after shell visible (post-bootstrap).
    /// </summary>
    public void StartBackendHealthMonitoring(StatusBarCoordinator? coordinator)
    {
        coordinator?.StartBackendHealthMonitoring();
    }
}
