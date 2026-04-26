using System;
using System.Diagnostics;
using System.Threading.Tasks;

namespace VoiceStudio.App.Services;

/// <summary>
/// GAP-008 Slice 15: consume taskbar jump-list pending activation after startup is ready.
/// Loaded schedule + taskbar HWND wiring lives in <see cref="MainWindowJumpListTaskbarProgressShellBridge"/> (GAP-008 Slice 12).
/// </summary>
public sealed class MainWindowJumpListDispatchShellBridge
{
    private readonly Func<IProjectWorkflowCoordinator?> _getCoordinator;
    private readonly Func<IStartupStateService> _getStartupStateService;
    private readonly Func<IToastNotificationService?> _getToast;

    public MainWindowJumpListDispatchShellBridge(
        Func<IProjectWorkflowCoordinator?> getCoordinator,
        Func<IStartupStateService> getStartupStateService,
        Func<IToastNotificationService?> getToast)
    {
        _getCoordinator = getCoordinator ?? throw new ArgumentNullException(nameof(getCoordinator));
        _getStartupStateService = getStartupStateService ?? throw new ArgumentNullException(nameof(getStartupStateService));
        _getToast = getToast ?? throw new ArgumentNullException(nameof(getToast));
    }

    /// <summary>
    /// GAP-067: consume jump list activation after startup is ready (project workflow coordinator).
    /// </summary>
    public void TryDispatchPendingJumpListActivation()
    {
        var pending = JumpListActivation.TryConsumePending();
        if (pending == null)
        {
            return;
        }

        var coordinator = _getCoordinator();
        if (coordinator == null)
        {
            return;
        }

        var startup = _getStartupStateService();

        void Run()
        {
            _ = RunJumpListPendingAsync(pending, coordinator);
        }

        if (startup.IsReady)
        {
            Run();
            return;
        }

        void Handler(object? s, StartupStateChangedEventArgs e)
        {
            if (!startup.IsReady)
            {
                return;
            }

            startup.StateChanged -= Handler;
            Run();
        }

        startup.StateChanged += Handler;
    }

    private async Task RunJumpListPendingAsync(JumpListPendingAction pending, IProjectWorkflowCoordinator coordinator)
    {
        try
        {
            switch (pending.Kind)
            {
                case JumpListPendingKind.NewProject:
                    await coordinator.CreateNewProjectAsync().ConfigureAwait(true);
                    break;
                case JumpListPendingKind.OpenDialog:
                    await coordinator.OpenProjectAsync().ConfigureAwait(true);
                    break;
                case JumpListPendingKind.OpenProject:
                    if (!string.IsNullOrWhiteSpace(pending.ProjectPath))
                    {
                        var name = System.IO.Path.GetFileNameWithoutExtension(pending.ProjectPath);
                        await coordinator.OpenRecentProjectAsync(pending.ProjectPath!, name).ConfigureAwait(true);
                    }

                    break;
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[JumpList] Activation failed: {ex}");
            _getToast()?.ShowError(ex.Message, "Jump list");
        }
    }
}
