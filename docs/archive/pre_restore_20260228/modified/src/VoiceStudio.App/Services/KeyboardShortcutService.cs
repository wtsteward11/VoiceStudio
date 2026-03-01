using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using Windows.System;
using Windows.UI.Core;
using VoiceStudio.App.Logging;

namespace VoiceStudio.App.Services;

/// <summary>
/// Keyboard shortcut management service.
/// 
/// Phase 15.1: Keyboard-First Workflow
/// Phase 5.0: Service Unification - Now implements IUnifiedKeyboardService.
/// Provides comprehensive keyboard shortcut handling for power users.
/// </summary>
public class KeyboardShortcutService : IUnifiedKeyboardService
{
    private readonly Dictionary<string, ShortcutBinding> _shortcuts;
    private readonly Dictionary<string, List<Action>> _handlers;
    private readonly Dictionary<string, List<Func<Task>>> _asyncHandlers;
    private readonly HashSet<string> _customizedShortcuts;
    private readonly Dictionary<string, (VirtualKey Key, VirtualKeyModifiers Modifiers)> _defaultBindings;
    private bool _isEnabled = true;

    public event EventHandler<ShortcutExecutedEventArgs>? ShortcutExecuted;
    
    /// <summary>
    /// GAP-B23/B24: Event fired when a shortcut conflict is detected during registration.
    /// </summary>
    public event EventHandler<ShortcutConflictEventArgs>? ConflictDetected;

    public KeyboardShortcutService()
    {
        _shortcuts = new Dictionary<string, ShortcutBinding>();
        _handlers = new Dictionary<string, List<Action>>();
        _asyncHandlers = new Dictionary<string, List<Func<Task>>>();
        _customizedShortcuts = new HashSet<string>();
        _defaultBindings = new Dictionary<string, (VirtualKey, VirtualKeyModifiers)>();

        RegisterDefaultShortcuts();
    }

    /// <summary>
    /// Gets all registered shortcuts.
    /// </summary>
    public IReadOnlyDictionary<string, ShortcutBinding> Shortcuts => _shortcuts;

    #region GAP-B08/B09: Context Detection

    // Track whether a modal dialog is currently open
    private bool _isModalDialogOpen;

    /// <summary>
    /// Sets the modal dialog state. Call this when opening/closing ContentDialogs.
    /// </summary>
    public void SetModalDialogOpen(bool isOpen)
    {
        _isModalDialogOpen = isOpen;
    }

    /// <summary>
    /// Gets the current shortcut context based on focus state.
    /// Higher priority contexts win when multiple shortcuts match.
    /// </summary>
    public ShortcutContext GetCurrentContext()
    {
        // Modal dialogs take precedence
        if (IsModalDialogOpen())
            return ShortcutContext.Modal;

        // Check if focused element accepts text input
        if (IsTextInputFocused())
            return ShortcutContext.TextEditing;

        // Check if focus is in a panel
        if (GetFocusedPanel() != null)
            return ShortcutContext.Panel;

        return ShortcutContext.Global;
    }

    /// <summary>
    /// Checks if a modal dialog is currently open.
    /// </summary>
    private bool IsModalDialogOpen()
    {
        return _isModalDialogOpen;
    }

    /// <summary>
    /// Checks if the focused element is a text input control.
    /// </summary>
    private bool IsTextInputFocused()
    {
        try
        {
            var focusedElement = Microsoft.UI.Xaml.Input.FocusManager.GetFocusedElement(null);
            return focusedElement is Microsoft.UI.Xaml.Controls.TextBox
                || focusedElement is Microsoft.UI.Xaml.Controls.RichEditBox
                || focusedElement is Microsoft.UI.Xaml.Controls.PasswordBox
                || focusedElement is Microsoft.UI.Xaml.Controls.AutoSuggestBox;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Gets the currently focused panel name, or null if none.
    /// </summary>
    private string? GetFocusedPanel()
    {
        try
        {
            var focusedElement = Microsoft.UI.Xaml.Input.FocusManager.GetFocusedElement(null) as DependencyObject;
            if (focusedElement == null)
                return null;

            // Walk up the visual tree looking for a panel identifier
            var current = focusedElement;
            while (current != null)
            {
                if (current is FrameworkElement fe && !string.IsNullOrEmpty(fe.Name) && fe.Name.EndsWith("Panel"))
                {
                    return fe.Name;
                }
                current = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetParent(current);
            }
            return null;
        }
        catch
        {
            return null;
        }
    }

    #endregion

    /// <summary>
    /// Register default keyboard shortcuts.
    /// </summary>
    private void RegisterDefaultShortcuts()
    {
        // File operations
        RegisterShortcut("file.new", VirtualKey.N, VirtualKeyModifiers.Control, "New Project");
        RegisterShortcut("file.open", VirtualKey.O, VirtualKeyModifiers.Control, "Open Project");
        RegisterShortcut("file.save", VirtualKey.S, VirtualKeyModifiers.Control, "Save Project");
        RegisterShortcut("file.saveAs", VirtualKey.S, VirtualKeyModifiers.Control | VirtualKeyModifiers.Shift, "Save As");
        RegisterShortcut("file.import", VirtualKey.I, VirtualKeyModifiers.Control, "Import Audio");
        RegisterShortcut("file.export", VirtualKey.E, VirtualKeyModifiers.Control | VirtualKeyModifiers.Shift, "Export Audio");

        // Edit operations
        RegisterShortcut("edit.undo", VirtualKey.Z, VirtualKeyModifiers.Control, "Undo");
        RegisterShortcut("edit.redo", VirtualKey.Y, VirtualKeyModifiers.Control, "Redo");
        RegisterShortcut("edit.cut", VirtualKey.X, VirtualKeyModifiers.Control, "Cut");
        RegisterShortcut("edit.copy", VirtualKey.C, VirtualKeyModifiers.Control, "Copy");
        RegisterShortcut("edit.paste", VirtualKey.V, VirtualKeyModifiers.Control, "Paste");
        RegisterShortcut("edit.selectAll", VirtualKey.A, VirtualKeyModifiers.Control, "Select All");
        // GAP-B08/B09: Delete at Global context, overridden by timeline when panel focused
        RegisterShortcut("edit.delete", VirtualKey.Delete, VirtualKeyModifiers.None, "Delete", ShortcutContext.Global);
        RegisterShortcut("timeline.deleteClip", VirtualKey.Delete, VirtualKeyModifiers.None, "Delete Clip", ShortcutContext.Panel);

        // Playback controls
        RegisterShortcut("playback.play", VirtualKey.Space, VirtualKeyModifiers.None, "Play/Pause");
        // GAP-B08/B09: Escape at Global context, overridden by modal dialogs
        RegisterShortcut("playback.stop", VirtualKey.Escape, VirtualKeyModifiers.None, "Stop", ShortcutContext.Global);
        RegisterShortcut("dialog.close", VirtualKey.Escape, VirtualKeyModifiers.None, "Close Dialog", ShortcutContext.Modal);
        RegisterShortcut("playback.record", VirtualKey.R, VirtualKeyModifiers.Control, "Record");
        RegisterShortcut("playback.loop", VirtualKey.L, VirtualKeyModifiers.Control, "Toggle Loop");
        RegisterShortcut("playback.rewind", VirtualKey.Home, VirtualKeyModifiers.None, "Go to Start");
        RegisterShortcut("playback.forward", VirtualKey.End, VirtualKeyModifiers.None, "Go to End");
        RegisterShortcut("playback.stepBack", VirtualKey.Left, VirtualKeyModifiers.None, "Step Back");
        RegisterShortcut("playback.stepForward", VirtualKey.Right, VirtualKeyModifiers.None, "Step Forward");

        // Synthesis
        RegisterShortcut("synthesis.generate", VirtualKey.Enter, VirtualKeyModifiers.Control, "Generate Audio");
        RegisterShortcut("synthesis.preview", VirtualKey.P, VirtualKeyModifiers.Control, "Preview Voice");
        RegisterShortcut("synthesis.regenerate", VirtualKey.R, VirtualKeyModifiers.Control | VirtualKeyModifiers.Shift, "Regenerate");

        // View controls
        RegisterShortcut("view.zoomIn", VirtualKey.Add, VirtualKeyModifiers.Control, "Zoom In");
        RegisterShortcut("view.zoomOut", VirtualKey.Subtract, VirtualKeyModifiers.Control, "Zoom Out");
        RegisterShortcut("view.zoomFit", VirtualKey.Number0, VirtualKeyModifiers.Control, "Zoom to Fit");
        RegisterShortcut("view.fullscreen", VirtualKey.F11, VirtualKeyModifiers.None, "Toggle Fullscreen");

        // Panel navigation
        RegisterShortcut("panel.synthesis", VirtualKey.Number1, VirtualKeyModifiers.Control, "Synthesis Panel");
        RegisterShortcut("panel.library", VirtualKey.Number2, VirtualKeyModifiers.Control, "Library Panel");
        RegisterShortcut("panel.profiles", VirtualKey.Number3, VirtualKeyModifiers.Control, "Profiles Panel");
        RegisterShortcut("panel.effects", VirtualKey.Number4, VirtualKeyModifiers.Control, "Effects Panel");
        RegisterShortcut("panel.settings", (VirtualKey)188, VirtualKeyModifiers.Control, "Settings"); // 188 = comma key

        // GAP-E02: Panel region focus and cycling
        RegisterShortcut("panel.cycleNext", VirtualKey.Tab, VirtualKeyModifiers.Control, "Cycle to Next Panel");
        RegisterShortcut("panel.cyclePrevious", VirtualKey.Tab, VirtualKeyModifiers.Control | VirtualKeyModifiers.Shift, "Cycle to Previous Panel");
        RegisterShortcut("panel.focusLeft", VirtualKey.Number1, VirtualKeyModifiers.Control | VirtualKeyModifiers.Menu, "Focus Left Panel");
        RegisterShortcut("panel.focusCenter", VirtualKey.Number2, VirtualKeyModifiers.Control | VirtualKeyModifiers.Menu, "Focus Center Panel");
        RegisterShortcut("panel.focusRight", VirtualKey.Number3, VirtualKeyModifiers.Control | VirtualKeyModifiers.Menu, "Focus Right Panel");
        RegisterShortcut("panel.focusBottom", VirtualKey.Number4, VirtualKeyModifiers.Control | VirtualKeyModifiers.Menu, "Focus Bottom Panel");

        // Tools
        RegisterShortcut("tools.commandPalette", VirtualKey.P, VirtualKeyModifiers.Control | VirtualKeyModifiers.Shift, "Command Palette");
        RegisterShortcut("tools.search", VirtualKey.F, VirtualKeyModifiers.Control, "Search");
        RegisterShortcut("tools.help", VirtualKey.F1, VirtualKeyModifiers.None, "Help");
    }

    /// <summary>
    /// Register a keyboard shortcut with Global context (interface implementation).
    /// </summary>
    public void RegisterShortcut(
        string commandId,
        VirtualKey key,
        VirtualKeyModifiers modifiers,
        string description)
    {
        RegisterShortcut(commandId, key, modifiers, description, ShortcutContext.Global);
    }

    /// <summary>
    /// Register a keyboard shortcut with context priority.
    /// </summary>
    /// <param name="commandId">The command identifier.</param>
    /// <param name="key">The key.</param>
    /// <param name="modifiers">The key modifiers.</param>
    /// <param name="description">Human-readable description.</param>
    /// <param name="context">GAP-B08/B09: Context priority for conflict resolution.</param>
    public void RegisterShortcut(
        string commandId,
        VirtualKey key,
        VirtualKeyModifiers modifiers,
        string description,
        ShortcutContext context)
    {
        _shortcuts[commandId] = new ShortcutBinding
        {
            CommandId = commandId,
            Key = key,
            Modifiers = modifiers,
            Description = description,
            IsDefault = !_customizedShortcuts.Contains(commandId),
            Context = context,
        };
    }

    /// <summary>
    /// Register a keyboard shortcut with an action handler and context priority.
    /// </summary>
    public void RegisterShortcut(
        string commandId,
        VirtualKey key,
        VirtualKeyModifiers modifiers,
        Action handler,
        string description,
        ShortcutContext context = ShortcutContext.Global)
    {
        RegisterShortcut(commandId, key, modifiers, description, context);
        RegisterHandler(commandId, handler);
    }

    /// <summary>
    /// Try to handle a key down event. Returns true if handled.
    /// </summary>
    public bool TryHandleKeyDown(VirtualKey key, VirtualKeyModifiers modifiers)
    {
        return HandleKeyDown(key, modifiers);
    }

    /// <summary>
    /// Register a handler for a shortcut.
    /// </summary>
    public void RegisterHandler(string commandId, Action handler)
    {
        if (!_handlers.ContainsKey(commandId))
        {
            _handlers[commandId] = new List<Action>();
        }
        _handlers[commandId].Add(handler);
    }

    /// <summary>
    /// Unregister a handler.
    /// </summary>
    public void UnregisterHandler(string commandId, Action handler)
    {
        if (_handlers.TryGetValue(commandId, out var handlers))
        {
            handlers.Remove(handler);
        }
    }

    /// <summary>
    /// Handle keyboard input with context-priority resolution.
    /// GAP-B08/B09: When multiple shortcuts match the same key, the highest priority
    /// context that is applicable wins.
    /// </summary>
    public bool HandleKeyDown(VirtualKey key, VirtualKeyModifiers modifiers)
    {
        if (!_isEnabled)
            return false;

        var currentContext = GetCurrentContext();

        // GAP-B08/B09: Find all matching shortcuts, ordered by context priority (highest first)
        var candidates = _shortcuts.Values
            .Where(s => s.Key == key && s.Modifiers == modifiers)
            .OrderByDescending(s => s.Context)
            .ToList();

        // Execute the highest priority shortcut that applies to the current context
        // A shortcut applies if its context level is <= the current context level
        var binding = candidates.FirstOrDefault(s => s.Context <= currentContext);

        if (binding != null)
        {
            ErrorLogger.LogDebug($"Executing '{binding.CommandId}' (context: {binding.Context}, current: {currentContext})", "KeyboardShortcuts");
            ExecuteShortcut(binding.CommandId);
            return true;
        }

        return false;
    }

    /// <summary>
    /// Execute a shortcut by command ID.
    /// </summary>
    public void ExecuteShortcut(string commandId)
    {
        if (_handlers.TryGetValue(commandId, out var handlers))
        {
            foreach (var handler in handlers)
            {
                try
                {
                    handler();
                }
                catch (Exception ex)
                {
                    ErrorLogger.LogWarning($"Shortcut handler error: {ex.Message}", "KeyboardShortcutService");
                }
            }

            ShortcutExecuted?.Invoke(this, new ShortcutExecutedEventArgs(commandId));
        }
    }

    /// <summary>
    /// Customize a shortcut.
    /// </summary>
    public void CustomizeShortcut(
        string commandId,
        VirtualKey key,
        VirtualKeyModifiers modifiers)
    {
        if (_shortcuts.TryGetValue(commandId, out var binding))
        {
            binding.Key = key;
            binding.Modifiers = modifiers;
            binding.IsDefault = false;
            _customizedShortcuts.Add(commandId);
        }
    }

    /// <summary>
    /// Reset a shortcut to default.
    /// </summary>
    public void ResetToDefault(string commandId)
    {
        _customizedShortcuts.Remove(commandId);
        RegisterDefaultShortcuts();
    }

    /// <summary>
    /// Reset all shortcuts to defaults.
    /// </summary>
    public void ResetAllToDefaults()
    {
        _customizedShortcuts.Clear();
        _shortcuts.Clear();
        RegisterDefaultShortcuts();
    }

    /// <summary>
    /// GAP-B23/B24: Register a shortcut with conflict detection.
    /// </summary>
    /// <param name="commandId">Command identifier.</param>
    /// <param name="key">Key binding.</param>
    /// <param name="modifiers">Key modifiers.</param>
    /// <param name="description">Human-readable description.</param>
    /// <param name="context">Context priority.</param>
    /// <param name="allowOverwrite">If true, overwrites existing conflicting shortcut.</param>
    /// <returns>True if registration succeeded, false if conflict blocked it.</returns>
    public bool TryRegisterShortcut(
        string commandId,
        VirtualKey key,
        VirtualKeyModifiers modifiers,
        string description,
        ShortcutContext context = ShortcutContext.Global,
        bool allowOverwrite = false)
    {
        var conflict = CheckForConflict(commandId, key, modifiers);
        if (conflict != null)
        {
            if (!allowOverwrite)
            {
            ErrorLogger.LogWarning($"Conflict: {commandId} vs {conflict.ConflictingCommandId}", "Shortcuts");
                ConflictDetected?.Invoke(this, new ShortcutConflictEventArgs(commandId, conflict));
                return false;
            }
            
            // Remove conflicting shortcut when overwrite allowed
            UnregisterShortcut(conflict.ConflictingCommandId);
            ErrorLogger.LogDebug($"Overwrote conflicting shortcut: {conflict.ConflictingCommandId}", "Shortcuts");
        }

        // Proceed with registration
        RegisterShortcut(commandId, key, modifiers, description, context);
        return true;
    }

    /// <summary>
    /// GAP-B23/B24: Set a custom shortcut with conflict resolution and auto-save.
    /// </summary>
    /// <param name="commandId">Command identifier.</param>
    /// <param name="key">New key binding.</param>
    /// <param name="modifiers">New key modifiers.</param>
    /// <returns>True if customization succeeded.</returns>
    public async Task<bool> SetCustomShortcutAsync(
        string commandId,
        VirtualKey key,
        VirtualKeyModifiers modifiers)
    {
        // Check for conflicts, allowing overwrite for user customizations
        var conflict = CheckForConflict(commandId, key, modifiers);
        if (conflict != null)
        {
            // For user customizations, we allow overwrite but log the conflict
            ErrorLogger.LogDebug($"User customization overwrites {conflict.ConflictingCommandId}", "Shortcuts");
            UnregisterShortcut(conflict.ConflictingCommandId);
        }

        // Get existing binding or create new
        if (_shortcuts.TryGetValue(commandId, out var binding))
        {
            binding.Key = key;
            binding.Modifiers = modifiers;
            binding.IsDefault = false;
            _customizedShortcuts.Add(commandId);
            
            // Auto-save customizations
            await SaveCustomShortcutsAsync();
            return true;
        }
        
        return false;
    }

    /// <summary>
    /// GAP-B23/B24: Initialize the service by loading saved customizations.
    /// Call this after construction to restore user preferences.
    /// </summary>
    public async Task InitializeAsync()
    {
        // Load user customizations (overrides defaults registered in constructor)
        await LoadCustomShortcutsAsync();
        ErrorLogger.LogDebug($"Initialized with {_customizedShortcuts.Count} custom shortcuts", "Shortcuts");
    }

    /// <summary>
    /// Get all shortcuts.
    /// </summary>
    public IEnumerable<ShortcutBinding> GetAllShortcuts() => _shortcuts.Values;

    /// <summary>
    /// Get shortcuts by category.
    /// </summary>
    public IEnumerable<ShortcutBinding> GetShortcutsByCategory(string category)
    {
        return _shortcuts.Values.Where(s => s.CommandId.StartsWith(category + "."));
    }

    /// <summary>
    /// Get shortcut display string.
    /// </summary>
    public string GetShortcutDisplayString(string commandId)
    {
        if (_shortcuts.TryGetValue(commandId, out var binding))
        {
            return binding.GetDisplayString();
        }
        return string.Empty;
    }

    /// <summary>
    /// Enable or disable shortcuts.
    /// </summary>
    public bool IsEnabled
    {
        get => _isEnabled;
        set => _isEnabled = value;
    }

    /// <summary>
    /// Check if a shortcut conflicts with another.
    /// </summary>
    public bool HasConflict(VirtualKey key, VirtualKeyModifiers modifiers, string excludeCommandId)
    {
        return _shortcuts.Values.Any(s =>
            s.Key == key &&
            s.Modifiers == modifiers &&
            s.CommandId != excludeCommandId);
    }

    /// <summary>
    /// Export shortcuts to JSON.
    /// </summary>
    public string ExportToJson()
    {
        var customized = _shortcuts
            .Where(kvp => _customizedShortcuts.Contains(kvp.Key))
            .ToDictionary(
                kvp => kvp.Key,
                kvp => new { key = (int)kvp.Value.Key, modifiers = (int)kvp.Value.Modifiers }
            );

        return System.Text.Json.JsonSerializer.Serialize(customized);
    }

    /// <summary>
    /// Import shortcuts from JSON.
    /// </summary>
    public void ImportFromJson(string json)
    {
        try
        {
            var customized = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, ShortcutImport>>(json);
            if (customized != null)
            {
                foreach (var kvp in customized)
                {
                    CustomizeShortcut(kvp.Key, (VirtualKey)kvp.Value.Key, (VirtualKeyModifiers)kvp.Value.Modifiers);
                }
            }
        }
        catch (Exception ex)
        {
            ErrorLogger.LogWarning($"Failed to import shortcuts: {ex.Message}", "KeyboardShortcutService");
        }
    }

    private class ShortcutImport
    {
        public int Key { get; set; }
        public int Modifiers { get; set; }
    }

    #region IUnifiedKeyboardService Implementation

    /// <summary>
    /// Registers an async handler for a shortcut.
    /// </summary>
    public void RegisterHandler(string commandId, Func<Task> handler)
    {
        if (!_asyncHandlers.ContainsKey(commandId))
        {
            _asyncHandlers[commandId] = new List<Func<Task>>();
        }
        _asyncHandlers[commandId].Add(handler);
    }

    /// <summary>
    /// Unregisters a shortcut.
    /// </summary>
    public void UnregisterShortcut(string commandId)
    {
        _shortcuts.Remove(commandId);
        _handlers.Remove(commandId);
        _asyncHandlers.Remove(commandId);
        _customizedShortcuts.Remove(commandId);
    }

    /// <summary>
    /// Updates a shortcut binding.
    /// </summary>
    public void UpdateShortcut(string commandId, VirtualKey key, VirtualKeyModifiers modifiers)
    {
        CustomizeShortcut(commandId, key, modifiers);
    }

    /// <summary>
    /// Resets a shortcut to its default binding.
    /// </summary>
    public void ResetShortcut(string commandId)
    {
        ResetToDefault(commandId);
    }

    /// <summary>
    /// Resets all shortcuts to defaults.
    /// </summary>
    public void ResetAllShortcuts()
    {
        ResetAllToDefaults();
    }

    /// <summary>
    /// Checks for conflicts with a proposed binding.
    /// </summary>
    public ShortcutConflict? CheckForConflict(string commandId, VirtualKey key, VirtualKeyModifiers modifiers)
    {
        var conflicting = _shortcuts.Values.FirstOrDefault(s =>
            s.Key == key &&
            s.Modifiers == modifiers &&
            s.CommandId != commandId);

        if (conflicting != null)
        {
            return new ShortcutConflict
            {
                ConflictingCommandId = conflicting.CommandId,
                ExistingBinding = conflicting
            };
        }

        return null;
    }

    /// <summary>
    /// Gets shortcuts that have been customized.
    /// </summary>
    public IEnumerable<string> GetCustomizedShortcuts() => _customizedShortcuts;

    /// <summary>
    /// Handles a key press event.
    /// </summary>
    public bool HandleKeyPress(VirtualKey key, VirtualKeyModifiers modifiers)
    {
        return HandleKeyDown(key, modifiers);
    }

    /// <summary>
    /// Handles a key press event asynchronously with context-priority resolution.
    /// GAP-B08/B09: Uses the same priority logic as HandleKeyDown.
    /// </summary>
    public async Task<bool> HandleKeyPressAsync(VirtualKey key, VirtualKeyModifiers modifiers)
    {
        if (!_isEnabled)
            return false;

        var currentContext = GetCurrentContext();

        // GAP-B08/B09: Find all matching shortcuts, ordered by context priority (highest first)
        var candidates = _shortcuts.Values
            .Where(s => s.Key == key && s.Modifiers == modifiers)
            .OrderByDescending(s => s.Context)
            .ToList();

        // Execute the highest priority shortcut that applies to the current context
        var binding = candidates.FirstOrDefault(s => s.Context <= currentContext);

        if (binding != null)
        {
            ErrorLogger.LogDebug($"Async executing '{binding.CommandId}' (context: {binding.Context}, current: {currentContext})", "KeyboardShortcuts");
            await ExecuteShortcutAsync(binding.CommandId);
            return true;
        }

        return false;
    }

    private async Task ExecuteShortcutAsync(string commandId)
    {
        // Execute sync handlers
        if (_handlers.TryGetValue(commandId, out var handlers))
        {
            foreach (var handler in handlers)
            {
                try { handler(); }
                catch (Exception ex)
                {
                    ErrorLogger.LogWarning($"Shortcut handler error: {ex.Message}", "KeyboardShortcutService");
                }
            }
        }

        // Execute async handlers
        if (_asyncHandlers.TryGetValue(commandId, out var asyncHandlers))
        {
            foreach (var handler in asyncHandlers)
            {
                try { await handler(); }
                catch (Exception ex)
                {
                    ErrorLogger.LogWarning($"Async shortcut handler error: {ex.Message}", "KeyboardShortcutService");
                }
            }
        }

        ShortcutExecuted?.Invoke(this, new ShortcutExecutedEventArgs(commandId));
    }

    /// <summary>
    /// Gets a shortcut by command ID.
    /// </summary>
    public ShortcutBinding? GetShortcut(string commandId)
    {
        return _shortcuts.TryGetValue(commandId, out var binding) ? binding : null;
    }

    /// <summary>
    /// Gets all shortcut categories.
    /// </summary>
    public IEnumerable<string> GetCategories()
    {
        return _shortcuts.Values
            .Select(s => s.CommandId.Split('.').FirstOrDefault() ?? "General")
            .Distinct()
            .OrderBy(c => c);
    }

    /// <summary>
    /// Searches shortcuts by description.
    /// </summary>
    public IEnumerable<ShortcutBinding> SearchShortcuts(string query)
    {
        var lowerQuery = query.ToLowerInvariant();
        return _shortcuts.Values.Where(s =>
            s.Description.ToLowerInvariant().Contains(lowerQuery) ||
            s.CommandId.ToLowerInvariant().Contains(lowerQuery));
    }

    /// <summary>
    /// Saves custom shortcuts to persistent storage.
    /// </summary>
    public async Task SaveCustomShortcutsAsync()
    {
        try
        {
            var settingsDir = System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "VoiceStudio");
            System.IO.Directory.CreateDirectory(settingsDir);
            var path = System.IO.Path.Combine(settingsDir, "shortcuts.json");
            await System.IO.File.WriteAllTextAsync(path, ExportToJson());
        }
        catch (Exception ex)
        {
            ErrorLogger.LogWarning($"Failed to save shortcuts: {ex.Message}", "KeyboardShortcutService");
        }
    }

    /// <summary>
    /// Loads custom shortcuts from persistent storage.
    /// </summary>
    public async Task LoadCustomShortcutsAsync()
    {
        try
        {
            var settingsDir = System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "VoiceStudio");
            var path = System.IO.Path.Combine(settingsDir, "shortcuts.json");
            
            if (System.IO.File.Exists(path))
            {
                var json = await System.IO.File.ReadAllTextAsync(path);
                ImportFromJson(json);
            }
        }
        catch (Exception ex)
        {
            ErrorLogger.LogWarning($"Failed to load shortcuts: {ex.Message}", "KeyboardShortcutService");
        }
    }

    #endregion
}

/// <summary>
/// GAP-B08/B09: Context priority for shortcut conflict resolution.
/// Higher values take precedence when multiple shortcuts match the same key combination.
/// </summary>
public enum ShortcutContext
{
    /// <summary>Global shortcuts (lowest priority, always active).</summary>
    Global = 0,
    
    /// <summary>Panel-specific shortcuts (active when panel has focus).</summary>
    Panel = 10,
    
    /// <summary>Modal dialog shortcuts (dialogs take precedence over panels).</summary>
    Modal = 20,
    
    /// <summary>Text editing shortcuts (highest priority, active when text input focused).</summary>
    TextEditing = 30,
}

/// <summary>
/// Keyboard shortcut binding.
/// </summary>
public class ShortcutBinding
{
    public string CommandId { get; set; } = string.Empty;
    public VirtualKey Key { get; set; }
    public VirtualKeyModifiers Modifiers { get; set; }
    public string Description { get; set; } = string.Empty;
    public bool IsDefault { get; set; } = true;
    
    /// <summary>
    /// GAP-B08/B09: Context priority for conflict resolution.
    /// Default is Global (active everywhere, lowest priority).
    /// </summary>
    public ShortcutContext Context { get; set; } = ShortcutContext.Global;

    public string GetDisplayString()
    {
        var parts = new List<string>();

        if (Modifiers.HasFlag(VirtualKeyModifiers.Control))
            parts.Add("Ctrl");
        if (Modifiers.HasFlag(VirtualKeyModifiers.Shift))
            parts.Add("Shift");
        if (Modifiers.HasFlag(VirtualKeyModifiers.Menu))
            parts.Add("Alt");
        if (Modifiers.HasFlag(VirtualKeyModifiers.Windows))
            parts.Add("Win");

        parts.Add(GetKeyDisplayName(Key));

        return string.Join("+", parts);
    }

    private static string GetKeyDisplayName(VirtualKey key)
    {
        return key switch
        {
            VirtualKey.Add => "+",
            VirtualKey.Subtract => "-",
            VirtualKey.Space => "Space",
            VirtualKey.Delete => "Del",
            VirtualKey.Escape => "Esc",
            VirtualKey.Enter => "Enter",
            VirtualKey.Number0 => "0",
            VirtualKey.Number1 => "1",
            VirtualKey.Number2 => "2",
            VirtualKey.Number3 => "3",
            VirtualKey.Number4 => "4",
            VirtualKey.Number5 => "5",
            VirtualKey.Number6 => "6",
            VirtualKey.Number7 => "7",
            VirtualKey.Number8 => "8",
            VirtualKey.Number9 => "9",
            _ => key.ToString(),
        };
    }
}

/// <summary>
/// Event args for shortcut execution.
/// </summary>
public class ShortcutExecutedEventArgs : EventArgs
{
    public string CommandId { get; }

    public ShortcutExecutedEventArgs(string commandId)
    {
        CommandId = commandId;
    }
}

/// <summary>
/// GAP-B23/B24: Event args for shortcut conflict detection.
/// </summary>
public class ShortcutConflictEventArgs : EventArgs
{
    /// <summary>
    /// The command ID that was being registered.
    /// </summary>
    public string CommandId { get; }
    
    /// <summary>
    /// The conflict information.
    /// </summary>
    public ShortcutConflict Conflict { get; }

    public ShortcutConflictEventArgs(string commandId, ShortcutConflict conflict)
    {
        CommandId = commandId;
        Conflict = conflict;
    }
}
