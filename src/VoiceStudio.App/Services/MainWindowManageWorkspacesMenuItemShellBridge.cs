// VoiceStudio — GAP-008 Slice 44: View → Manage Workspaces… menu item wiring (forwards to Slice 20 shell).

using System;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using VoiceStudio.Core.Services;

namespace VoiceStudio.App.Services;

/// <summary>
/// Wires File → Manage Workspaces… menu item to
/// <see cref="MainWindowMenuToolActivationShellBridge"/> without keeping a private handler on MainWindow.
/// </summary>
public sealed class MainWindowManageWorkspacesMenuItemShellBridge
{
    private readonly MainWindowMenuToolActivationShellBridge _menuToolActivationShellBridge;
    private readonly Func<XamlRoot?> _getXamlRoot;
    private readonly Func<IToastNotificationService?> _tryGetToast;

    public MainWindowManageWorkspacesMenuItemShellBridge(
        MainWindowMenuToolActivationShellBridge menuToolActivationShellBridge,
        Func<XamlRoot?> getXamlRoot,
        Func<IToastNotificationService?> tryGetToast)
    {
        ArgumentNullException.ThrowIfNull(menuToolActivationShellBridge);
        ArgumentNullException.ThrowIfNull(getXamlRoot);
        ArgumentNullException.ThrowIfNull(tryGetToast);

        _menuToolActivationShellBridge = menuToolActivationShellBridge;
        _getXamlRoot = getXamlRoot;
        _tryGetToast = tryGetToast;
    }

    public async void OnManageWorkspacesMenuItemClick(object sender, RoutedEventArgs e)
    {
        await RunFlowAsync().ConfigureAwait(true);
    }

    public Task RunFlowAsync() =>
        _menuToolActivationShellBridge.RunManageWorkspacesAsync(_getXamlRoot, _tryGetToast);
}
