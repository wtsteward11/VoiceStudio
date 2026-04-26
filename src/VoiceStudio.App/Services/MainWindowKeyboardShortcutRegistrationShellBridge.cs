using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using VoiceStudio.App.Controls;
using VoiceStudio.Core.Panels;
using Windows.System;

namespace VoiceStudio.App.Services;

/// <summary>
/// GAP-008 Slice 36: registers global <see cref="KeyboardShortcutService"/> shortcuts from ctor.
/// Help dialog flow remains <see cref="MainWindowKeyboardShortcutsShellBridge"/> (Slice 21).
/// </summary>
public sealed class MainWindowKeyboardShortcutRegistrationShellBridge
{
    public void Register(KeyboardShortcutService keyboardShortcutService, MainWindowKeyboardShortcutRegistrationDependencies deps)
    {
        ArgumentNullException.ThrowIfNull(keyboardShortcutService);
        ArgumentNullException.ThrowIfNull(deps);

        // File operations
        keyboardShortcutService.RegisterShortcut(
            "file.new",
            VirtualKey.N,
            VirtualKeyModifiers.Control,
            deps.CreateNewProject,
            "New Project");

        keyboardShortcutService.RegisterShortcut(
            "file.open",
            VirtualKey.O,
            VirtualKeyModifiers.Control,
            deps.OpenProject,
            "Open Project");

        keyboardShortcutService.RegisterShortcut(
            "file.save",
            VirtualKey.S,
            VirtualKeyModifiers.Control,
            deps.SaveProject,
            "Save Project");

        keyboardShortcutService.RegisterShortcut(
            "file.import",
            VirtualKey.I,
            VirtualKeyModifiers.Control,
            deps.ImportAudioFile,
            "Import Audio");

        // Edit operations
        keyboardShortcutService.RegisterShortcut(
            "edit.undo",
            VirtualKey.Z,
            VirtualKeyModifiers.Control,
            deps.ExecuteUndo,
            "Undo");

        keyboardShortcutService.RegisterShortcut(
            "edit.redo",
            VirtualKey.Y,
            VirtualKeyModifiers.Control,
            deps.ExecuteRedo,
            "Redo");

        // Navigation
        keyboardShortcutService.RegisterShortcut(
            "nav.commandpalette",
            VirtualKey.P,
            VirtualKeyModifiers.Control,
            deps.ShowCommandPalette,
            "Command Palette");

        keyboardShortcutService.RegisterShortcut(
            "nav.toolcatalog",
            VirtualKey.T,
            VirtualKeyModifiers.Control | VirtualKeyModifiers.Shift,
            deps.ShowToolCatalog,
            "Tool Catalog");

        keyboardShortcutService.RegisterShortcut(
            "nav.globalsearch",
            VirtualKey.K,
            VirtualKeyModifiers.Control,
            deps.ShowGlobalSearch,
            "Global Search");

        // Zoom
        keyboardShortcutService.RegisterShortcut(
            "zoom.in",
            VirtualKey.Add,
            VirtualKeyModifiers.Control,
            () => deps.GlobalTransport.ZoomIn(deps.GetCenterPanelHost),
            "Zoom In");

        keyboardShortcutService.RegisterShortcut(
            "zoom.out",
            VirtualKey.Subtract,
            VirtualKeyModifiers.Control,
            () => deps.GlobalTransport.ZoomOut(deps.GetCenterPanelHost),
            "Zoom Out");

        keyboardShortcutService.RegisterShortcut(
            "zoom.reset",
            VirtualKey.Number0,
            VirtualKeyModifiers.Control,
            () => deps.GlobalTransport.ResetZoom(deps.GetCenterPanelHost),
            "Reset Zoom");

        keyboardShortcutService.RegisterShortcut(
            "help.shortcuts",
            VirtualKey.F1,
            VirtualKeyModifiers.Shift,
            deps.TriggerHelpKeyboardShortcutsFromShortcut,
            "Keyboard Shortcuts");

        keyboardShortcutService.RegisterShortcut(
            "help.shortcuts.alt",
            (VirtualKey)191,
            VirtualKeyModifiers.Shift,
            deps.TriggerHelpKeyboardShortcutsFromShortcut,
            "Keyboard Shortcuts (?)");

        deps.RegisterPanelQuickSwitchGroup();

        keyboardShortcutService.RegisterShortcut(
            "panel.cycleNext",
            VirtualKey.Tab,
            VirtualKeyModifiers.Control,
            deps.PanelRegionFocus.CyclePanelNext,
            "Cycle to Next Panel");

        keyboardShortcutService.RegisterShortcut(
            "panel.cyclePrevious",
            VirtualKey.Tab,
            VirtualKeyModifiers.Control | VirtualKeyModifiers.Shift,
            deps.PanelRegionFocus.CyclePanelPrevious,
            "Cycle to Previous Panel");

        keyboardShortcutService.RegisterShortcut(
            "panel.focusLeft",
            VirtualKey.Number1,
            VirtualKeyModifiers.Control | VirtualKeyModifiers.Menu,
            () => deps.PanelRegionFocus.FocusPanelRegion(PanelRegion.Left),
            "Focus Left Panel");

        keyboardShortcutService.RegisterShortcut(
            "panel.focusCenter",
            VirtualKey.Number2,
            VirtualKeyModifiers.Control | VirtualKeyModifiers.Menu,
            () => deps.PanelRegionFocus.FocusPanelRegion(PanelRegion.Center),
            "Focus Center Panel");

        keyboardShortcutService.RegisterShortcut(
            "panel.focusRight",
            VirtualKey.Number3,
            VirtualKeyModifiers.Control | VirtualKeyModifiers.Menu,
            () => deps.PanelRegionFocus.FocusPanelRegion(PanelRegion.Right),
            "Focus Right Panel");

        keyboardShortcutService.RegisterShortcut(
            "panel.focusBottom",
            VirtualKey.Number4,
            VirtualKeyModifiers.Control | VirtualKeyModifiers.Menu,
            () => deps.PanelRegionFocus.FocusPanelRegion(PanelRegion.Bottom),
            "Focus Bottom Panel");
    }
}

/// <summary>
/// Captures MainWindow-owned delegates for global shortcut registration (Slice 36).
/// </summary>
public sealed class MainWindowKeyboardShortcutRegistrationDependencies
{
    public MainWindowKeyboardShortcutRegistrationDependencies(
        Action createNewProject,
        Action openProject,
        Action saveProject,
        Action importAudioFile,
        Action executeUndo,
        Action executeRedo,
        Action showCommandPalette,
        Action showToolCatalog,
        Action showGlobalSearch,
        Func<PanelHost?> getCenterPanelHost,
        MainWindowGlobalTransportShellBridge globalTransport,
        MainWindowPanelRegionFocusShellBridge panelRegionFocus,
        Action triggerHelpKeyboardShortcutsFromShortcut,
        Action registerPanelQuickSwitchGroup)
    {
        CreateNewProject = createNewProject ?? throw new ArgumentNullException(nameof(createNewProject));
        OpenProject = openProject ?? throw new ArgumentNullException(nameof(openProject));
        SaveProject = saveProject ?? throw new ArgumentNullException(nameof(saveProject));
        ImportAudioFile = importAudioFile ?? throw new ArgumentNullException(nameof(importAudioFile));
        ExecuteUndo = executeUndo ?? throw new ArgumentNullException(nameof(executeUndo));
        ExecuteRedo = executeRedo ?? throw new ArgumentNullException(nameof(executeRedo));
        ShowCommandPalette = showCommandPalette ?? throw new ArgumentNullException(nameof(showCommandPalette));
        ShowToolCatalog = showToolCatalog ?? throw new ArgumentNullException(nameof(showToolCatalog));
        ShowGlobalSearch = showGlobalSearch ?? throw new ArgumentNullException(nameof(showGlobalSearch));
        GetCenterPanelHost = getCenterPanelHost ?? throw new ArgumentNullException(nameof(getCenterPanelHost));
        GlobalTransport = globalTransport ?? throw new ArgumentNullException(nameof(globalTransport));
        PanelRegionFocus = panelRegionFocus ?? throw new ArgumentNullException(nameof(panelRegionFocus));
        TriggerHelpKeyboardShortcutsFromShortcut = triggerHelpKeyboardShortcutsFromShortcut
            ?? throw new ArgumentNullException(nameof(triggerHelpKeyboardShortcutsFromShortcut));
        RegisterPanelQuickSwitchGroup = registerPanelQuickSwitchGroup
            ?? throw new ArgumentNullException(nameof(registerPanelQuickSwitchGroup));
    }

    public Action CreateNewProject { get; }
    public Action OpenProject { get; }
    public Action SaveProject { get; }
    public Action ImportAudioFile { get; }
    public Action ExecuteUndo { get; }
    public Action ExecuteRedo { get; }
    public Action ShowCommandPalette { get; }
    public Action ShowToolCatalog { get; }
    public Action ShowGlobalSearch { get; }
    public Func<PanelHost?> GetCenterPanelHost { get; }
    public MainWindowGlobalTransportShellBridge GlobalTransport { get; }
    public MainWindowPanelRegionFocusShellBridge PanelRegionFocus { get; }
    public Action TriggerHelpKeyboardShortcutsFromShortcut { get; }
    public Action RegisterPanelQuickSwitchGroup { get; }
}
