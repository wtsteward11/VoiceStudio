// VoiceStudio — GAP-008 Slice 45: View → Toggle Mini Timeline menu item wiring (forwards to Slice 20 shell).

using System;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using VoiceStudio.App.Controls;
using VoiceStudio.Core.Panels;
using VoiceStudio.Core.Services;

namespace VoiceStudio.App.Services;

/// <summary>
/// Wires View → Toggle Mini Timeline menu item to
/// <see cref="MainWindowMenuToolActivationShellBridge"/> without keeping a private click handler on MainWindow.
/// </summary>
public sealed class MainWindowToggleMiniTimelineMenuItemShellBridge
{
    private readonly MainWindowMenuToolActivationShellBridge _menuToolActivationShellBridge;
    private readonly Func<bool> _getIsMiniTimelineVisible;
    private readonly Action<bool> _setIsMiniTimelineVisible;
    private readonly Func<PanelHost?> _getBottomPanelHost;
    private readonly Func<string, PanelRegion?, Task<bool>> _openPanelByIdAsync;
    private readonly Action _refreshMenuItemText;
    private readonly Func<IToastNotificationService?> _tryGetToast;

    public MainWindowToggleMiniTimelineMenuItemShellBridge(
        MainWindowMenuToolActivationShellBridge menuToolActivationShellBridge,
        Func<bool> getIsMiniTimelineVisible,
        Action<bool> setIsMiniTimelineVisible,
        Func<PanelHost?> getBottomPanelHost,
        Func<string, PanelRegion?, Task<bool>> openPanelByIdAsync,
        Action refreshMenuItemText,
        Func<IToastNotificationService?> tryGetToast)
    {
        ArgumentNullException.ThrowIfNull(menuToolActivationShellBridge);
        ArgumentNullException.ThrowIfNull(getIsMiniTimelineVisible);
        ArgumentNullException.ThrowIfNull(setIsMiniTimelineVisible);
        ArgumentNullException.ThrowIfNull(getBottomPanelHost);
        ArgumentNullException.ThrowIfNull(openPanelByIdAsync);
        ArgumentNullException.ThrowIfNull(refreshMenuItemText);
        ArgumentNullException.ThrowIfNull(tryGetToast);

        _menuToolActivationShellBridge = menuToolActivationShellBridge;
        _getIsMiniTimelineVisible = getIsMiniTimelineVisible;
        _setIsMiniTimelineVisible = setIsMiniTimelineVisible;
        _getBottomPanelHost = getBottomPanelHost;
        _openPanelByIdAsync = openPanelByIdAsync;
        _refreshMenuItemText = refreshMenuItemText;
        _tryGetToast = tryGetToast;
    }

    public async void OnToggleMiniTimelineMenuItemClick(object sender, RoutedEventArgs e)
    {
        await RunFlowAsync().ConfigureAwait(true);
    }

    public Task RunFlowAsync() =>
        _menuToolActivationShellBridge.RunToggleMiniTimelineAsync(
            _getIsMiniTimelineVisible,
            _setIsMiniTimelineVisible,
            _getBottomPanelHost,
            _openPanelByIdAsync,
            _refreshMenuItemText,
            _tryGetToast);
}
