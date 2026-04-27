// VoiceStudio — GAP-008 Slice 43: View → Check for Updates… menu item wiring (forwards to Slice 20 shell).

using System;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using VoiceStudio.Core.Services;

namespace VoiceStudio.App.Services;

/// <summary>
/// Wires View → Check for Updates… menu item to
/// <see cref="MainWindowMenuToolActivationShellBridge"/> without keeping a private handler on MainWindow.
/// </summary>
public sealed class MainWindowCheckForUpdatesMenuItemShellBridge
{
    private readonly MainWindowMenuToolActivationShellBridge _menuToolActivationShellBridge;
    private readonly Func<IViewModelContext> _getViewModelContext;
    private readonly IUpdateService _updateService;
    private readonly Func<IErrorDialogService> _getErrorDialogService;

    public MainWindowCheckForUpdatesMenuItemShellBridge(
        MainWindowMenuToolActivationShellBridge menuToolActivationShellBridge,
        Func<IViewModelContext> getViewModelContext,
        IUpdateService updateService,
        Func<IErrorDialogService> getErrorDialogService)
    {
        ArgumentNullException.ThrowIfNull(menuToolActivationShellBridge);
        ArgumentNullException.ThrowIfNull(getViewModelContext);
        ArgumentNullException.ThrowIfNull(updateService);
        ArgumentNullException.ThrowIfNull(getErrorDialogService);

        _menuToolActivationShellBridge = menuToolActivationShellBridge;
        _getViewModelContext = getViewModelContext;
        _updateService = updateService;
        _getErrorDialogService = getErrorDialogService;
    }

    public async void OnCheckForUpdatesMenuItemClick(object sender, RoutedEventArgs e)
    {
        await RunFlowAsync().ConfigureAwait(true);
    }

    public Task RunFlowAsync() =>
        _menuToolActivationShellBridge.RunCheckForUpdatesAsync(
            _getViewModelContext,
            _updateService,
            _getErrorDialogService);
}
