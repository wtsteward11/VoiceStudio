# Command Palette Guide

> **Version**: 1.1.0  
> **Last Updated**: 2026-03-10  
> **Status**: Active

## Overview

VoiceStudio includes a command palette (**Ctrl+P**) for quick access to commands, panels, themes, and navigation. This guide documents the command system architecture and how to add new commands.

## Activation

- **Keyboard:** `Ctrl+P` (global shortcut, registered in MainWindow)
- **Placeholder:** Search box shows "Search commands and panels (Ctrl+P)"

## Architecture

```
┌──────────────────────────────────────────────────────────┐
│              CommandPaletteWindow / CommandPaletteView    │
│  ┌────────────────────────────────────────────────────┐  │
│  │  🔍 Search commands and panels (Ctrl+P)            │  │
│  └────────────────────────────────────────────────────┘  │
│  ┌────────────────────────────────────────────────────┐  │
│  │  > Open Voice Synthesis        [panel]              │  │
│  │  > Theme: Dark                 [theme]             │  │
│  │  > New Project                 Ctrl+N  [registry]  │  │
│  └────────────────────────────────────────────────────┘  │
└──────────────────────────────────────────────────────────┘
                           │
                           ▼
               ┌─────────────────────┐
               │ CommandPaletteService│
               │ + CommandPaletteVM   │
               └─────────────────────┘
                           │
            ┌──────────────┴──────────────┐
            ▼                             ▼
    ┌───────────────┐             ┌───────────────────────┐
    │ IPanelRegistry│             │ IUnifiedCommandRegistry│
    │ (panels)      │             │ (file, profile, etc.)   │
    └───────────────┘             └───────────────────────┘
```

## Command Sources

The palette aggregates two sources:

1. **Panel Registry** — All visible panels from `IPanelRegistry.GetAllDescriptors()`. Each becomes an `open:<panelId>` item.
2. **Unified Command Registry** — Commands registered via `IUnifiedCommandRegistry` (file, profile, playback, settings, etc.).

Built-in actions (theme, density, help) are hardcoded in `CommandPaletteViewModel.LoadDefaultItems()`.

## IUnifiedCommandRegistry

```csharp
public interface IUnifiedCommandRegistry
{
    void Register(CommandDescriptor descriptor, ISyncCommandHandler handler);
    void Register(CommandDescriptor descriptor, IAsyncCommandHandler handler);
    void Register(CommandDescriptor descriptor, Action<object?> execute, Func<object?, bool>? canExecute = null);
    bool Unregister(string commandId);
    Task ExecuteAsync(string commandId, object? parameter = null, CancellationToken ct = default);
    bool CanExecute(string commandId, object? parameter = null);
    ICommand? GetCommand(string commandId);
    CommandDescriptor? GetDescriptor(string commandId);
    IReadOnlyList<CommandDescriptor> GetAllCommands();
    IReadOnlyList<CommandDescriptor> GetCommandsByCategory(string category);
    IReadOnlyList<string> GetCategories();
    bool IsRegistered(string commandId);
    // ... status tracking, events
}
```

## CommandDescriptor

```csharp
public sealed class CommandDescriptor
{
    public required string Id { get; init; }
    public required string Title { get; init; }
    public string? Description { get; init; }
    public string Category { get; init; } = "General";
    public string? Icon { get; init; }
    public string? KeyboardShortcut { get; init; }
    public bool IsEnabled { get; init; } = true;
    public bool BypassBusy { get; init; } = false;
}
```

## Registering Commands

### Via Command Handlers (Bootstrapper)

Commands are registered in `CommandHandlerBootstrapper` via handlers:

- **FileOperationsHandler** — file.new, file.open, file.save, etc.
- **ProfileOperationsHandler** — profile.create, profile.duplicate, etc.
- **PlaybackOperationsHandler** — playback.play, playback.pause, etc.
- **SettingsOperationsHandler** — settings.open
- **NavigationHandler** — nav.* commands

Example:

```csharp
_registry.Register(
    new CommandDescriptor
    {
        Id = "file.new",
        Title = "New Project",
        Description = "Create a new project",
        Category = "file",
        KeyboardShortcut = "Ctrl+N"
    },
    async (_, ct) => await _fileOps.NewProjectAsync(ct)
);
```

### Adding a Panel to the Palette

Panels appear automatically if registered in `IPanelRegistry` with `IsVisible=true` and `Maturity != Deprecated`. Use `Keywords` on `PanelDescriptor` for search.

## Command Categories

| Category | Description | Examples |
|----------|-------------|----------|
| File | File operations | New, Open, Save, Export |
| Profile | Profile operations | Create, Duplicate, Switch |
| Playback | Playback controls | Play, Pause, Stop |
| Settings | Settings and preferences | Open Settings |
| Navigation | Navigation | Panel switching, Tool Catalog |
| Theme | Theme and density | Dark, Light, Sci-Fi, Compact, Comfort |
| System | System actions | Show keybindings |

## Action Types

Commands use `Id` prefixes for routing:

| Prefix | Action | Example |
|--------|--------|---------|
| `open:` | Open panel | `open:voice_synthesis` |
| `theme:` | Apply theme | `theme:Dark` |
| `density:` | Apply layout density | `density:Compact` |
| `help:` | Show help view | `help:keymap` |
| (registry) | Execute via IUnifiedCommandRegistry | `file.new` |

## Implementation Files

| File | Purpose |
|------|---------|
| `CommandPaletteService.cs` | Shows palette window, handles PanelOpenRequested, HelpViewRequested, theme/density |
| `CommandPaletteViewModel.cs` | Loads items from PanelRegistry + UnifiedCommandRegistry, applies filter |
| `CommandPaletteView.xaml` | Search box + results list |
| `CommandPaletteWindow.xaml` | Host window |
| `UnifiedCommandRegistry.cs` | Command registration and execution |
| `CommandHandlerBootstrapper.cs` | Wires handlers to registry at startup |

## Search Algorithm

`CommandPaletteViewModel` filters by `FilterText` against `CommandItem.SearchText` (which includes `Title` and `Keywords`). Filtering is case-insensitive substring matching.

## Keyboard Shortcuts

- **Ctrl+P** — Open command palette (MainWindow)
- **Escape** — Close palette
- **Up/Down** — Navigate results
- **Enter** — Execute selected command

## Best Practices

1. **Use descriptive titles** — Commands should be self-explanatory
2. **Provide shortcuts** — Common commands should have `KeyboardShortcut` set
3. **Include descriptions** — Help users understand what the command does
4. **Categorize properly** — Use `file`, `profile`, `playback`, `settings`, `nav` for registry commands
5. **Handle errors** — Commands should handle exceptions gracefully

## Related Documentation

- [Command Palette Usage](../design/COMMAND_PALETTE_USAGE.md)
- [Keyboard Shortcuts](../user/KEYBOARD_SHORTCUTS.md)
- [Unified Command Architecture ADR](../architecture/decisions/ADR-028-unified-command-architecture.md)
