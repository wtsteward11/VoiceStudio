// Phase 5.0: Service Unification
// Task 5.0.4: Unified Keyboard Service Interface
// This interface unifies KeyboardShortcutService and Features/PowerUser/ShortcutManager
//
// Note: ShortcutBinding and ShortcutExecutedEventArgs are defined in KeyboardShortcutService.cs

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Windows.System;
using Windows.UI.Core;

namespace VoiceStudio.App.Services;

/// <summary>
/// Shortcut conflict information.
/// </summary>
public class ShortcutConflict
{
    public string ConflictingCommandId { get; set; } = string.Empty;
    public ShortcutBinding ExistingBinding { get; set; } = new();
}

/// <summary>
/// Unified keyboard service interface combining shortcut management
/// and command execution.
/// </summary>
public interface IUnifiedKeyboardService
{
    #region Properties

    /// <summary>
    /// Gets whether keyboard shortcuts are enabled.
    /// </summary>
    bool IsEnabled { get; set; }

    /// <summary>
    /// Gets all registered shortcuts.
    /// </summary>
    IReadOnlyDictionary<string, ShortcutBinding> Shortcuts { get; }

    #endregion

    #region Shortcut Registration

    /// <summary>
    /// Registers a keyboard shortcut.
    /// </summary>
    void RegisterShortcut(string commandId, VirtualKey key, VirtualKeyModifiers modifiers, string description);

    /// <summary>
    /// Registers a handler for a shortcut.
    /// </summary>
    void RegisterHandler(string commandId, Action handler);

    /// <summary>
    /// Registers an async handler for a shortcut.
    /// </summary>
    void RegisterHandler(string commandId, Func<Task> handler);

    /// <summary>
    /// Unregisters a shortcut.
    /// </summary>
    void UnregisterShortcut(string commandId);

    /// <summary>
    /// Unregisters a handler.
    /// </summary>
    void UnregisterHandler(string commandId, Action handler);

    #endregion

    #region Shortcut Customization

    /// <summary>
    /// Updates a shortcut binding.
    /// </summary>
    void UpdateShortcut(string commandId, VirtualKey key, VirtualKeyModifiers modifiers);

    /// <summary>
    /// Resets a shortcut to its default binding.
    /// </summary>
    void ResetShortcut(string commandId);

    /// <summary>
    /// Resets all shortcuts to defaults.
    /// </summary>
    void ResetAllShortcuts();

    /// <summary>
    /// Checks for conflicts with a proposed binding.
    /// </summary>
    ShortcutConflict? CheckForConflict(string commandId, VirtualKey key, VirtualKeyModifiers modifiers);

    /// <summary>
    /// Gets shortcuts that have been customized.
    /// </summary>
    IEnumerable<string> GetCustomizedShortcuts();

    #endregion

    #region Key Handling

    /// <summary>
    /// Handles a key press event.
    /// </summary>
    bool HandleKeyPress(VirtualKey key, VirtualKeyModifiers modifiers);

    /// <summary>
    /// Handles a key press event asynchronously.
    /// </summary>
    Task<bool> HandleKeyPressAsync(VirtualKey key, VirtualKeyModifiers modifiers);

    #endregion

    #region Query

    /// <summary>
    /// Gets a shortcut by command ID.
    /// </summary>
    ShortcutBinding? GetShortcut(string commandId);

    /// <summary>
    /// Gets shortcuts by category.
    /// </summary>
    IEnumerable<ShortcutBinding> GetShortcutsByCategory(string category);

    /// <summary>
    /// Gets all shortcut categories.
    /// </summary>
    IEnumerable<string> GetCategories();

    /// <summary>
    /// Searches shortcuts by description.
    /// </summary>
    IEnumerable<ShortcutBinding> SearchShortcuts(string query);

    #endregion

    #region Persistence

    /// <summary>
    /// Saves custom shortcuts to persistent storage.
    /// </summary>
    Task SaveCustomShortcutsAsync();

    /// <summary>
    /// Loads custom shortcuts from persistent storage.
    /// </summary>
    Task LoadCustomShortcutsAsync();

    #endregion

    #region Events

    /// <summary>
    /// Event raised when a shortcut is executed.
    /// </summary>
    event EventHandler<ShortcutExecutedEventArgs>? ShortcutExecuted;

    #endregion
}
