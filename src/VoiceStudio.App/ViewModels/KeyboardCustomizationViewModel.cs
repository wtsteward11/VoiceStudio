using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Windows.System;
using VoiceStudio.App.Services;

namespace VoiceStudio.App.ViewModels;

/// <summary>
/// GAP-065: UI state for customizing keyboard shortcuts (browse, rebind, reset, conflict surface).
/// </summary>
public partial class KeyboardCustomizationViewModel : ObservableObject, IDisposable
{
    private readonly IUnifiedKeyboardService _keyboard;
    private readonly IDialogService _dialog;
    private bool _disposed;

    /// <summary>All shortcut rows (source of truth for conflict updates).</summary>
    public ObservableCollection<ShortcutBindingItem> AllItems { get; } = new();

    /// <summary>Rows visible for current search.</summary>
    public ObservableCollection<ShortcutBindingItem> FilteredItems { get; } = new();

    [ObservableProperty]
    private string _searchQuery = string.Empty;

    [ObservableProperty]
    private string? _editingCommandId;

    /// <summary>True when the user is capturing a new chord for <see cref="EditingCommandId"/>.</summary>
    public bool IsChordCaptureActive => !string.IsNullOrEmpty(EditingCommandId);

    partial void OnEditingCommandIdChanged(string? value) => OnPropertyChanged(nameof(IsChordCaptureActive));

    public KeyboardCustomizationViewModel(IUnifiedKeyboardService keyboard, IDialogService dialog)
    {
        _keyboard = keyboard ?? throw new ArgumentNullException(nameof(keyboard));
        _dialog = dialog ?? throw new ArgumentNullException(nameof(dialog));
        _keyboard.ConflictDetected += OnConflictDetected;
    }

    partial void OnSearchQueryChanged(string value) => RebuildFilter();

    public void RefreshShortcuts()
    {
        AllItems.Clear();
        foreach (var binding in _keyboard.Shortcuts.Values.OrderBy(b => b.CommandId))
        {
            AllItems.Add(new ShortcutBindingItem(binding));
        }

        RebuildFilter();
    }

    private void RebuildFilter()
    {
        FilteredItems.Clear();
        var q = SearchQuery.Trim().ToLowerInvariant();
        foreach (var item in AllItems)
        {
            if (string.IsNullOrEmpty(q) ||
                item.Description.ToLowerInvariant().Contains(q) ||
                item.CommandId.ToLowerInvariant().Contains(q))
            {
                FilteredItems.Add(item);
            }
        }
    }

    private void OnConflictDetected(object? sender, ShortcutConflictEventArgs e)
    {
        ApplyConflictToRow(e.CommandId, e.Conflict.ConflictingCommandId);
    }

    private void ApplyConflictToRow(string commandId, string conflictingCommandId)
    {
        var row = AllItems.FirstOrDefault(i => i.CommandId == commandId);
        if (row == null)
        {
            return;
        }

        row.HasConflict = true;
        row.ConflictDescription = $"Also used by {conflictingCommandId}";
    }

    [RelayCommand]
    private void StartEdit(string? commandId)
    {
        EditingCommandId = commandId;
        foreach (var item in AllItems)
        {
            item.IsEditing = item.CommandId == commandId;
        }
    }

    [RelayCommand]
    private void CancelEdit()
    {
        EditingCommandId = null;
        foreach (var item in AllItems)
        {
            item.IsEditing = false;
        }
    }

    public async Task CommitChordAsync(string commandId, VirtualKey key, VirtualKeyModifiers modifiers)
    {
        ShortcutConflictEventArgs? conflictArgs = null;
        void CaptureConflict(object? sender, ShortcutConflictEventArgs e)
        {
            if (e.CommandId == commandId)
            {
                conflictArgs = e;
            }
        }

        _keyboard.ConflictDetected += CaptureConflict;
        try
        {
            await _keyboard.SetCustomShortcutAsync(commandId, key, modifiers).ConfigureAwait(true);
        }
        finally
        {
            _keyboard.ConflictDetected -= CaptureConflict;
        }

        CancelEdit();
        RefreshShortcuts();
        if (conflictArgs != null)
        {
            ApplyConflictToRow(commandId, conflictArgs.Conflict.ConflictingCommandId);
        }
    }

    [RelayCommand]
    private void ResetBinding(string? commandId)
    {
        if (string.IsNullOrEmpty(commandId))
        {
            return;
        }

        _keyboard.ResetShortcut(commandId);
        RefreshShortcuts();
    }

    [RelayCommand]
    private async Task ResetAllAsync()
    {
        var ok = await _dialog.ShowConfirmationAsync(
            "Reset all shortcuts",
            "Reset every keyboard shortcut to factory defaults? Customizations will be removed.",
            "Reset all",
            "Cancel").ConfigureAwait(true);
        if (!ok)
        {
            return;
        }

        _keyboard.ResetAllShortcuts();
        RefreshShortcuts();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _keyboard.ConflictDetected -= OnConflictDetected;
    }
}

/// <summary>
/// One row in the customization list (GAP-065).
/// </summary>
public sealed partial class ShortcutBindingItem : ObservableObject
{
    public string CommandId { get; }
    public string Description { get; }
    public string Category { get; }

    [ObservableProperty]
    private string _displayChord;

    [ObservableProperty]
    private bool _hasConflict;

    [ObservableProperty]
    private string? _conflictDescription;

    [ObservableProperty]
    private bool _isEditing;

    public ShortcutBindingItem(ShortcutBinding binding)
    {
        CommandId = binding.CommandId;
        Description = binding.Description;
        Category = binding.CommandId.Split('.').FirstOrDefault() ?? "General";
        _displayChord = binding.GetDisplayString();
    }
}
