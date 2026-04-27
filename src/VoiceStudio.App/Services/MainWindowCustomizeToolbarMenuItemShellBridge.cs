// VoiceStudio — GAP-008 Slice 42: View → Customize Toolbar… menu item wiring (forwards to Slice 7 shell).

using System;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;

namespace VoiceStudio.App.Services;

/// <summary>
/// Wires View → Customize Toolbar… menu item to
/// <see cref="MainWindowToolbarCustomizationShellBridge"/> without keeping a private handler on MainWindow.
/// </summary>
public sealed class MainWindowCustomizeToolbarMenuItemShellBridge
{
    private readonly MainWindowToolbarCustomizationShellBridge _toolbarCustomizationShellBridge;

    public MainWindowCustomizeToolbarMenuItemShellBridge(
        MainWindowToolbarCustomizationShellBridge toolbarCustomizationShellBridge)
    {
        ArgumentNullException.ThrowIfNull(toolbarCustomizationShellBridge);
        _toolbarCustomizationShellBridge = toolbarCustomizationShellBridge;
    }

    public async void OnCustomizeToolbarMenuItemClick(object sender, RoutedEventArgs e)
    {
        await RunFlowAsync().ConfigureAwait(true);
    }

    public Task RunFlowAsync() =>
        _toolbarCustomizationShellBridge.ShowCustomizationDialogAsync();
}
