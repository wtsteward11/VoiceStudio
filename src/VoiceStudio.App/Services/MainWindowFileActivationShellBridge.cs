using System;
using System.Diagnostics;
using System.Threading.Tasks;
using VoiceStudio.Core.Panels;

namespace VoiceStudio.App.Services;

/// <summary>
/// GAP-008 Slice 14: consume shell file-association / argv pending activation after startup is ready.
/// </summary>
public sealed class MainWindowFileActivationShellBridge
{
    private readonly Func<IProjectWorkflowCoordinator?> _getCoordinator;
    private readonly Func<IStartupStateService> _getStartupStateService;
    private readonly Func<IToastNotificationService?> _getToast;
    private readonly Func<IShellNavigationCoordinator?> _getShellNavigation;

    public MainWindowFileActivationShellBridge(
        Func<IProjectWorkflowCoordinator?> getCoordinator,
        Func<IStartupStateService> getStartupStateService,
        Func<IToastNotificationService?> getToast,
        Func<IShellNavigationCoordinator?> getShellNavigation)
    {
        _getCoordinator = getCoordinator ?? throw new ArgumentNullException(nameof(getCoordinator));
        _getStartupStateService = getStartupStateService ?? throw new ArgumentNullException(nameof(getStartupStateService));
        _getToast = getToast ?? throw new ArgumentNullException(nameof(getToast));
        _getShellNavigation = getShellNavigation ?? throw new ArgumentNullException(nameof(getShellNavigation));
    }

    /// <summary>
    /// GAP-067 slice 4: consume shell file-association argv after startup is ready.
    /// </summary>
    public void TryDispatchPendingFileActivation()
    {
        var pending = FileActivation.TryConsumePending();
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
            _ = RunFileActivationPendingAsync(pending, coordinator);
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

    private async Task RunFileActivationPendingAsync(FileActivationPendingAction pending, IProjectWorkflowCoordinator coordinator)
    {
        try
        {
            switch (pending.Kind)
            {
                case FileActivationKind.OpenProject:
                    await coordinator.OpenProjectByPathAsync(pending.FilePath).ConfigureAwait(true);
                    break;
                case FileActivationKind.ImportProject:
                    _getToast()?.ShowInfo(
                        "Collaboration bundle open from shell is not fully supported yet. Use File > Open, or import from the collaboration workflow.",
                        "File activation");
                    await coordinator.OpenProjectAsync().ConfigureAwait(true);
                    break;
                case FileActivationKind.ImportProfile:
                    _getToast()?.ShowInfo(
                        "Profile import from a .vprofile file is not available from shell yet. Use the Profiles panel.",
                        "File activation");
                    var nav = _getShellNavigation();
                    if (nav != null)
                    {
                        await nav.OpenPanelByIdAsync("Profiles", PanelRegion.Left).ConfigureAwait(true);
                    }

                    break;
                case FileActivationKind.Unknown:
                    break;
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[FileActivation] Dispatch failed: {ex}");
            _getToast()?.ShowError(ex.Message, "File activation");
        }
    }
}
