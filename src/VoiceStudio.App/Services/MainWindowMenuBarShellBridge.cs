using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using VoiceStudio.Core.Panels;
using VoiceStudio.Core.Services;

namespace VoiceStudio.App.Services;

/// <summary>
/// Top-level <see cref="MenuBar"/> tree for MainWindow (File/Edit/View/…/Help). Phase 0 keeps MenuBar
/// in code to avoid XAML compiler issues; menu item instances that wire MainWindow event handlers
/// are supplied by the host.
/// </summary>
public sealed class MainWindowMenuBarShellBridge
{
    private readonly Func<ContentControl?> _getMenuBarHost;
    private readonly IPanelRegistry _panelRegistry;
    private readonly MainWindowMenuBarCommandCallbacks _cb;
    private readonly MainWindowMenuBarShellWire _wire;

    public MainWindowMenuBarShellBridge(
        Func<ContentControl?> getMenuBarHost,
        IPanelRegistry panelRegistry,
        MainWindowMenuBarShellWire wire,
        MainWindowMenuBarCommandCallbacks callbacks)
    {
        _getMenuBarHost = getMenuBarHost ?? throw new ArgumentNullException(nameof(getMenuBarHost));
        _panelRegistry = panelRegistry ?? throw new ArgumentNullException(nameof(panelRegistry));
        _wire = wire ?? throw new ArgumentNullException(nameof(wire));
        _cb = callbacks ?? throw new ArgumentNullException(nameof(callbacks));
    }

    public void InitializeMenuBar()
    {
        var host = _getMenuBarHost();
        if (host == null)
        {
            return;
        }

        var menuBar = new MenuBar
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Center
        };

        menuBar.Items.Add(BuildFileMenu());
        menuBar.Items.Add(BuildEditMenu());
        menuBar.Items.Add(BuildViewMenu());
        menuBar.Items.Add(BuildModulesMenu());
        menuBar.Items.Add(BuildPlaybackMenu());
        menuBar.Items.Add(BuildToolsMenu());
        menuBar.Items.Add(BuildAiMenu());
        menuBar.Items.Add(BuildHelpMenu());

        host.Content = menuBar;
    }

    private MenuBarItem BuildFileMenu()
    {
        var item = new MenuBarItem { Title = "File" };

        item.Items.Add(CreateMenuItem("New Project", _cb.NewProject, "Ctrl+N"));
        item.Items.Add(CreateMenuItem("Open Project", _cb.OpenProject, "Ctrl+O"));
        item.Items.Add(CreateMenuItem("Save Project", _cb.SaveProject, "Ctrl+S"));
        item.Items.Add(new MenuFlyoutSeparator());
        item.Items.Add(CreateMenuItem("Import Audio File...", _cb.ImportAudioFile, "Ctrl+I"));

        item.Items.Add(new MenuFlyoutSeparator());
        if (_wire.RecentProjectsSubMenu != null)
        {
            item.Items.Add(_wire.RecentProjectsSubMenu);
            item.Items.Add(new MenuFlyoutSeparator());
        }

        item.Items.Add(CreateMenuItem("Exit", _cb.CloseWindow));
        return item;
    }

    private MenuBarItem BuildEditMenu()
    {
        var item = new MenuBarItem { Title = "Edit" };
        item.Items.Add(CreateMenuItem("Undo", _cb.ExecuteUndo));
        item.Items.Add(CreateMenuItem("Redo", _cb.ExecuteRedo));
        return item;
    }

    private MenuBarItem BuildViewMenu()
    {
        var item = new MenuBarItem { Title = "View" };

        item.Items.Add(CreateNavMenuItem("Studio", "nav.studio", "Timeline", PanelRegion.Center, "NavStudio", "Ctrl+1"));
        item.Items.Add(CreateNavMenuItem("Library", "nav.library", "Library", PanelRegion.Left, "NavLibrary", "Ctrl+2"));
        item.Items.Add(CreateNavMenuItem("Profiles", "nav.profiles", "Profiles", PanelRegion.Left, "NavProfiles", "Ctrl+3"));
        item.Items.Add(CreateNavMenuItem("Effects", "nav.effects", "EffectsMixer", PanelRegion.Right, "NavEffects", "Ctrl+4"));
        item.Items.Add(CreateNavMenuItem("Settings", "nav.settings", "Settings", PanelRegion.Right, "NavSettings", "Ctrl+,"));
        item.Items.Add(new MenuFlyoutSeparator());
        if (_wire.CommandRouter != null)
        {
            item.Items.Add(CreateCommandMenuItem("Go Back", "nav.back", "Alt+Left"));
            item.Items.Add(CreateCommandMenuItem("Go Forward", "nav.forward", "Alt+Right"));
            item.Items.Add(new MenuFlyoutSeparator());
        }

        if (_wire.ToggleMiniTimelineMenuItem != null)
        {
            item.Items.Add(_wire.ToggleMiniTimelineMenuItem);
        }

        item.Items.Add(CreateMenuItem("Global Search", _cb.ShowGlobalSearch));
        return item;
    }

    private MenuBarItem BuildModulesMenu()
    {
        var item = new MenuBarItem { Title = "Modules" };

        var descriptors = _panelRegistry
            .GetAllDescriptors()
            .Where(d => d.IsVisible)
            .Where(d => d.Maturity != PanelMaturity.Deprecated)
            .Where(d => d.Maturity != PanelMaturity.Experimental || _cb.GetShowExperimentalPanels())
            .Where(d => !string.IsNullOrEmpty(d.MenuCategory))
            .OrderBy(d => d.MenuCategory)
            .ThenBy(d => d.DisplayName)
            .ToList();

        var grouped = descriptors.GroupBy(d => d.MenuCategory!);

        foreach (var group in grouped)
        {
            var subItem = new MenuFlyoutSubItem { Text = group.Key };
            foreach (var descriptor in group)
            {
                var panelId = descriptor.PanelId;
                var region = descriptor.DefaultRegion;
                var displayName = descriptor.DisplayName;
                subItem.Items.Add(CreateMenuItem(displayName, () => _ = _cb.OpenPanelByIdAsync(panelId, region)));
            }

            item.Items.Add(subItem);
        }

        return item;
    }

    private MenuBarItem BuildPlaybackMenu()
    {
        var item = new MenuBarItem { Title = "Playback" };

        if (_wire.CommandRouter != null)
        {
            item.Items.Add(CreateCommandMenuItem("Play/Pause", "playback.toggle", "Space"));
            item.Items.Add(CreateCommandMenuItem("Stop", "playback.stop"));
            item.Items.Add(new MenuFlyoutSeparator());
            item.Items.Add(CreateCommandMenuItem("Record", "playback.record", "R"));
            item.Items.Add(new MenuFlyoutSeparator());
            item.Items.Add(CreateCommandMenuItem("Rewind", "playback.rewind", "Home"));
            item.Items.Add(CreateCommandMenuItem("Fast Forward", "playback.forward", "End"));
            item.Items.Add(CreateCommandMenuItem("Step Back", "playback.stepBack", "Left"));
            item.Items.Add(CreateCommandMenuItem("Step Forward", "playback.stepForward", "Right"));
        }
        else
        {
            item.Items.Add(CreateMenuItem("Play/Pause", _cb.TogglePlayback));
            item.Items.Add(CreateMenuItem("Stop", _cb.StopPlayback));
            item.Items.Add(CreateMenuItem("Record", _cb.ToggleRecording));
        }

        return item;
    }

    private MenuBarItem BuildToolsMenu()
    {
        var item = new MenuBarItem { Title = "Tools" };
        if (_wire.CustomizeToolbarMenuItem != null)
        {
            item.Items.Add(_wire.CustomizeToolbarMenuItem);
        }

        if (_wire.ManageWorkspacesMenuItem != null)
        {
            item.Items.Add(_wire.ManageWorkspacesMenuItem);
        }

        if (_wire.CheckForUpdatesMenuItem != null)
        {
            item.Items.Add(_wire.CheckForUpdatesMenuItem);
        }

        if (_wire.KeyboardShortcutsMenuItem != null)
        {
            item.Items.Add(_wire.KeyboardShortcutsMenuItem);
        }

        return item;
    }

    private MenuBarItem BuildAiMenu()
    {
        var item = new MenuBarItem { Title = "AI" };
        item.Items.Add(CreateMenuItem(
            "AI Mixing & Mastering",
            () => _ = _cb.OpenPanelByIdAsync("AIMixingMastering", null)));
        item.Items.Add(CreateMenuItem(
            "Ensemble Synthesis",
            () => _ = _cb.OpenPanelByIdAsync("EnsembleSynthesis", null)));
        return item;
    }

    private MenuBarItem BuildHelpMenu()
    {
        var item = new MenuBarItem { Title = "Help" };
        item.Items.Add(CreateMenuItem("Documentation Folder", _cb.OpenDocumentationFolder));
        item.Items.Add(CreateMenuItem("About VoiceStudio", _cb.ShowAboutDialog));
        return item;
    }

    private static MenuFlyoutItem CreateMenuItem(string text, Action action, string? shortcut = null)
    {
        var item = new MenuFlyoutItem { Text = text };
        if (!string.IsNullOrEmpty(shortcut))
        {
            item.KeyboardAcceleratorTextOverride = shortcut;
        }

        item.Click += (_, __) => action();
        return item;
    }

    private MenuFlyoutItem CreateNavMenuItem(
        string text,
        string commandId,
        string fallbackPanelId,
        PanelRegion fallbackRegion,
        string buttonName,
        string? shortcut = null)
    {
        var item = new MenuFlyoutItem { Text = text };
        if (!string.IsNullOrEmpty(shortcut))
        {
            item.KeyboardAcceleratorTextOverride = shortcut;
        }

        item.Click += (_, __) => _cb.ExecuteNavCommand(commandId, fallbackPanelId, fallbackRegion, buttonName);
        return item;
    }

    private MenuFlyoutItem CreateCommandMenuItem(string text, string commandId, string? shortcut = null)
    {
        var item = new MenuFlyoutItem { Text = text };

        if (!string.IsNullOrEmpty(shortcut))
        {
            item.KeyboardAcceleratorTextOverride = shortcut;
        }

        if (_wire.CommandRouter != null)
        {
            _wire.CommandRouter.WireMenuItem(item, commandId);
        }
        else
        {
            item.Click += (_, __) => Debug.WriteLine(
                $"[MainWindow] Command '{commandId}' unavailable - no CommandRouter");
        }

        return item;
    }
}

/// <summary>Delegates for menu item actions; host must supply all; tests may use no-op stubs where safe.</summary>
public sealed class MainWindowMenuBarCommandCallbacks
{
    public required Action NewProject { get; init; }
    public required Action OpenProject { get; init; }
    public required Action SaveProject { get; init; }
    public required Action ImportAudioFile { get; init; }
    public required Action CloseWindow { get; init; }
    public required Action ExecuteUndo { get; init; }
    public required Action ExecuteRedo { get; init; }
    public required Action ShowGlobalSearch { get; init; }
    public required Action<string, string, PanelRegion, string> ExecuteNavCommand { get; init; }
    public required Func<string, PanelRegion?, Task<bool>> OpenPanelByIdAsync { get; init; }
    public required Action OpenDocumentationFolder { get; init; }
    public required Action ShowAboutDialog { get; init; }
    public required Action TogglePlayback { get; init; }
    public required Action StopPlayback { get; init; }
    public required Action ToggleRecording { get; init; }
    public required Func<bool> GetShowExperimentalPanels { get; init; }
}

/// <summary>Pre-built flyout items and <see cref="CommandRouter"/> reference used when assembling the menu bar.</summary>
public sealed class MainWindowMenuBarShellWire
{
    public required MenuFlyoutSubItem? RecentProjectsSubMenu { get; init; }
    public required CommandRouter? CommandRouter { get; init; }
    public required MenuFlyoutItem? ToggleMiniTimelineMenuItem { get; init; }
    public required MenuFlyoutItem? CustomizeToolbarMenuItem { get; init; }
    public required MenuFlyoutItem? ManageWorkspacesMenuItem { get; init; }
    public required MenuFlyoutItem? CheckForUpdatesMenuItem { get; init; }
    public required MenuFlyoutItem? KeyboardShortcutsMenuItem { get; init; }
}
