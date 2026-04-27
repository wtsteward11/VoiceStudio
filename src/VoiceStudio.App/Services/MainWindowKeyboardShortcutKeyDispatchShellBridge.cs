using System;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml.Input;
using VoiceStudio.App.Utilities;
using Windows.System;
using Windows.UI.Core;

namespace VoiceStudio.App.Services;

/// <summary>
/// GAP-008 Slice 38: routes root window KeyDown to <see cref="KeyboardShortcutService.TryHandleKeyDown"/>
/// with the same modifier bitmask rules previously implemented on <c>MainWindow</c>.
/// </summary>
public sealed class MainWindowKeyboardShortcutKeyDispatchShellBridge
{
    /// <summary>
    /// Attempts to handle a key down using the keyboard shortcut service. Sets <paramref name="e"/>.Handled when a shortcut matches.
    /// </summary>
    /// <param name="keyboardShortcutService">The shortcut service that owns registered bindings.</param>
    /// <param name="e">The key routed event from the window.</param>
    /// <returns><see langword="true"/> if the event was handled by the shortcut service.</returns>
    public bool TryHandleKeyDown(KeyboardShortcutService keyboardShortcutService, KeyRoutedEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(keyboardShortcutService);
        ArgumentNullException.ThrowIfNull(e);

        var modifiers = VirtualKeyModifiers.None;
        if (InputHelper.IsControlPressed())
        {
            modifiers |= VirtualKeyModifiers.Control;
        }

        if (InputHelper.IsShiftPressed())
        {
            modifiers |= VirtualKeyModifiers.Shift;
        }

        var altState = InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Menu);
        if ((altState & CoreVirtualKeyStates.Down) == CoreVirtualKeyStates.Down)
        {
            modifiers |= VirtualKeyModifiers.Menu;
        }

        if (keyboardShortcutService.TryHandleKeyDown(e.Key, modifiers))
        {
            e.Handled = true;
            return true;
        }

        return false;
    }
}
