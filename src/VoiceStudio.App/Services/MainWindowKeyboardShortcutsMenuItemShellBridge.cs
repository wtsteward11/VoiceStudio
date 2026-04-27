// VoiceStudio — GAP-008 Slice 41: Help → Keyboard Shortcuts menu item wiring (forwards to Slice 21 shell).

using System;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using VoiceStudio.App.ViewModels;

namespace VoiceStudio.App.Services;

/// <summary>
/// Wires Help → Keyboard Shortcuts menu item and shortcut registration callback to
/// <see cref="MainWindowKeyboardShortcutsShellBridge"/> without keeping a private handler on MainWindow.
/// </summary>
public sealed class MainWindowKeyboardShortcutsMenuItemShellBridge
{
    private readonly MainWindowKeyboardShortcutsShellBridge _keyboardShortcutsShellBridge;
    private readonly Func<XamlRoot?> _getXamlRoot;
    private readonly Func<KeyboardCustomizationViewModel> _getKeyboardCustomizationViewModel;
    private readonly Func<IToastNotificationService?> _getToastForError;

    public MainWindowKeyboardShortcutsMenuItemShellBridge(
        MainWindowKeyboardShortcutsShellBridge keyboardShortcutsShellBridge,
        Func<XamlRoot?> getXamlRoot,
        Func<KeyboardCustomizationViewModel> getKeyboardCustomizationViewModel,
        Func<IToastNotificationService?> getToastForError)
    {
        ArgumentNullException.ThrowIfNull(keyboardShortcutsShellBridge);
        ArgumentNullException.ThrowIfNull(getXamlRoot);
        ArgumentNullException.ThrowIfNull(getKeyboardCustomizationViewModel);
        ArgumentNullException.ThrowIfNull(getToastForError);

        _keyboardShortcutsShellBridge = keyboardShortcutsShellBridge;
        _getXamlRoot = getXamlRoot;
        _getKeyboardCustomizationViewModel = getKeyboardCustomizationViewModel;
        _getToastForError = getToastForError;
    }

    public async void OnKeyboardShortcutsMenuItemClick(object sender, RoutedEventArgs e)
    {
        await RunFlowAsync().ConfigureAwait(true);
    }

    public Task RunFlowAsync() =>
        _keyboardShortcutsShellBridge.RunKeyboardShortcutsMenuFlowAsync(
            _getXamlRoot,
            _getKeyboardCustomizationViewModel,
            _getToastForError);
}
