using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using Windows.System;
using Windows.UI.Core;

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
        RegisterShortcut("edit.delete", VirtualKey.Delete, VirtualKeyModifiers.None, "Delete");

        // Playback controls
        RegisterShortcut("playback.play", VirtualKey.Space, VirtualKeyModifiers.None, "Play/Pause");
        RegisterShortcut("playback.stop", VirtualKey.Escape, VirtualKeyModifiers.None, "Stop");
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

        // Tools
        RegisterShortcut("tools.commandPalette", VirtualKey.P, VirtualKeyModifiers.Control | VirtualKeyModifiers.Shift, "Command Palette");
        RegisterShortcut("tools.search", VirtualKey.F, VirtualKeyModifiers.Control, "Search");
        RegisterShortcut("tools.help", VirtualKey.F1, VirtualKeyModifiers.None, "Help");
    }

    /// <summary>
    /// Register a keyboard shortcut.
    /// </summary>
    public void RegisterShortcut(
        string commandId,
        VirtualKey key,
        VirtualKeyModifiers modifiers,
        string description)
    {
        _shortcuts[commandId] = new ShortcutBinding
        {
            CommandId = commandId,
            Key = key,
            Modifiers = modifiers,
            Description = description,
            IsDefault = !_customizedShortcuts.Contains(commandId),
        };
    }

    /// <summary>
    /// Register a keyboard shortcut with an action handler.
    /// </summary>
    public void RegisterShortcut(
        string commandId,
        VirtualKey key,
        VirtualKeyModifiers modifiers,
        Action handler,
        string description)
    {
        RegisterShortcut(commandId, key, modifiers, description);
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
    /// Handle keyboard input.
    /// </summary>
    public bool HandleKeyDown(VirtualKey key, VirtualKeyModifiers modifiers)
    {
        if (!_isEnabled)
            return false;

        var binding = _shortcuts.Values.FirstOrDefault(s =>
            s.Key == key && s.Modifiers == modifiers);

        if (binding != null)
        {
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
                    System.Diagnostics.Debug.WriteLine($"Shortcut handler error: {ex.Message}");
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
            System.Diagnostics.Debug.WriteLine($"Failed to import shortcuts: {ex.Message}");
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
    /// Handles a key press event asynchronously.
    /// </summary>
    public async Task<bool> HandleKeyPressAsync(VirtualKey key, VirtualKeyModifiers modifiers)
    {
        if (!_isEnabled)
            return false;

        var binding = _shortcuts.Values.FirstOrDefault(s =>
            s.Key == key && s.Modifiers == modifiers);

        if (binding != null)
        {
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
                    System.Diagnostics.Debug.WriteLine($"Shortcut handler error: {ex.Message}");
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
                    System.Diagnostics.Debug.WriteLine($"Async shortcut handler error: {ex.Message}");
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
            System.Diagnostics.Debug.WriteLine($"Failed to save shortcuts: {ex.Message}");
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
            System.Diagnostics.Debug.WriteLine($"Failed to load shortcuts: {ex.Message}");
        }
    }

    #endregion
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
