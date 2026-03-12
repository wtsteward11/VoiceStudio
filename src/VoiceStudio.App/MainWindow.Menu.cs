using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Diagnostics;
using System.Linq;
using VoiceStudio.Core.Panels;

namespace VoiceStudio.App
{
    public sealed partial class MainWindow
    {
        private void InitializeMenuBar()
        {
            var host = FindInContent<ContentControl>("MenuBarHost");
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

            // Use direct method calls so critical file operations never silently no-op
            item.Items.Add(CreateMenuItem("New Project", CreateNewProject, "Ctrl+N"));
            item.Items.Add(CreateMenuItem("Open Project", OpenProject, "Ctrl+O"));
            item.Items.Add(CreateMenuItem("Save Project", SaveProject, "Ctrl+S"));
            item.Items.Add(new MenuFlyoutSeparator());
            item.Items.Add(CreateMenuItem("Import Audio File...", ImportAudioFile, "Ctrl+I"));

            item.Items.Add(new MenuFlyoutSeparator());
            if (_recentProjectsSubMenu != null)
            {
                item.Items.Add(_recentProjectsSubMenu);
                item.Items.Add(new MenuFlyoutSeparator());
            }
            item.Items.Add(CreateMenuItem("Exit", () => Close()));
            return item;
        }

        private MenuBarItem BuildEditMenu()
        {
            var item = new MenuBarItem { Title = "Edit" };
            item.Items.Add(CreateMenuItem("Undo", ExecuteUndo));
            item.Items.Add(CreateMenuItem("Redo", ExecuteRedo));
            return item;
        }

        private MenuBarItem BuildViewMenu()
        {
            var item = new MenuBarItem { Title = "View" };

            // Navigation shortcuts - use fallback-aware CreateNavMenuItem so panels always open
            item.Items.Add(CreateNavMenuItem("Studio", "nav.studio", "Timeline", PanelRegion.Center, "NavStudio", "Ctrl+1"));
            item.Items.Add(CreateNavMenuItem("Library", "nav.library", "Library", PanelRegion.Left, "NavLibrary", "Ctrl+2"));
            item.Items.Add(CreateNavMenuItem("Profiles", "nav.profiles", "Profiles", PanelRegion.Left, "NavProfiles", "Ctrl+3"));
            item.Items.Add(CreateNavMenuItem("Effects", "nav.effects", "EffectsMixer", PanelRegion.Right, "NavEffects", "Ctrl+4"));
            item.Items.Add(CreateNavMenuItem("Settings", "nav.settings", "Settings", PanelRegion.Right, "NavSettings", "Ctrl+,"));
            item.Items.Add(new MenuFlyoutSeparator());
            if (_commandRouter != null)
            {
                item.Items.Add(CreateCommandMenuItem("Go Back", "nav.back", "Alt+Left"));
                item.Items.Add(CreateCommandMenuItem("Go Forward", "nav.forward", "Alt+Right"));
                item.Items.Add(new MenuFlyoutSeparator());
            }

            if (_toggleMiniTimelineMenuItem != null)
            {
                item.Items.Add(_toggleMiniTimelineMenuItem);
            }
            item.Items.Add(CreateMenuItem("Global Search", ShowGlobalSearch));
            return item;
        }

        private MenuBarItem BuildModulesMenu()
        {
            var item = new MenuBarItem { Title = "Modules" };

            var descriptors = UnifiedPanelRegistry.GetAllDescriptors()
              .Where(d => d.IsVisible)
              .Where(d => d.Maturity != PanelMaturity.Deprecated)
              .Where(d => d.Maturity != PanelMaturity.Experimental || GetShowExperimentalPanels())
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
                    subItem.Items.Add(CreateMenuItem(displayName, () => _ = OpenPanelByIdAsync(panelId, region)));
                }
                item.Items.Add(subItem);
            }

            return item;
        }

        private MenuBarItem BuildPlaybackMenu()
        {
            var item = new MenuBarItem { Title = "Playback" };

            if (_commandRouter != null)
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
                item.Items.Add(CreateMenuItem("Play/Pause", TogglePlayback));
                item.Items.Add(CreateMenuItem("Stop", StopPlayback));
                item.Items.Add(CreateMenuItem("Record", ToggleRecording));
            }

            return item;
        }

        private MenuBarItem BuildToolsMenu()
        {
            var item = new MenuBarItem { Title = "Tools" };
            if (_customizeToolbarMenuItem != null)
            {
                item.Items.Add(_customizeToolbarMenuItem);
            }
            if (_manageWorkspacesMenuItem != null)
            {
                item.Items.Add(_manageWorkspacesMenuItem);
            }
            if (_checkForUpdatesMenuItem != null)
            {
                item.Items.Add(_checkForUpdatesMenuItem);
            }
            if (_keyboardShortcutsMenuItem != null)
            {
                item.Items.Add(_keyboardShortcutsMenuItem);
            }
            return item;
        }

        private MenuBarItem BuildAiMenu()
        {
            var item = new MenuBarItem { Title = "AI" };
            item.Items.Add(CreateMenuItem(
                "AI Mixing & Mastering",
                () => _ = OpenPanelByIdAsync("AIMixingMastering")));
            item.Items.Add(CreateMenuItem(
                "Ensemble Synthesis",
                () => _ = OpenPanelByIdAsync("EnsembleSynthesis")));
            return item;
        }

        private MenuBarItem BuildHelpMenu()
        {
            var item = new MenuBarItem { Title = "Help" };
            item.Items.Add(CreateMenuItem("Documentation Folder", OpenDocumentationFolder));
            item.Items.Add(CreateMenuItem("About VoiceStudio", ShowAboutDialog));
            return item;
        }

        private MenuFlyoutItem CreateMenuItem(string text, Action action, string? shortcut = null)
        {
            var item = new MenuFlyoutItem { Text = text };
            if (!string.IsNullOrEmpty(shortcut))
                item.KeyboardAcceleratorTextOverride = shortcut;
            item.Click += (_, __) => action();
            return item;
        }

        /// <summary>
        /// Creates a nav menu item that uses ExecuteNavCommand (fallback to OpenPanelByIdAsync when command fails).
        /// </summary>
        private MenuFlyoutItem CreateNavMenuItem(string text, string commandId, string fallbackPanelId, PanelRegion fallbackRegion, string buttonName, string? shortcut = null)
        {
            var item = new MenuFlyoutItem { Text = text };
            if (!string.IsNullOrEmpty(shortcut))
                item.KeyboardAcceleratorTextOverride = shortcut;
            item.Click += (_, __) => ExecuteNavCommand(commandId, fallbackPanelId, fallbackRegion, buttonName);
            return item;
        }

        /// <summary>
        /// Creates a menu item wired to a registry command.
        /// </summary>
        private MenuFlyoutItem CreateCommandMenuItem(string text, string commandId, string? shortcut = null)
        {
            var item = new MenuFlyoutItem { Text = text };

            // Add keyboard accelerator hint if provided
            if (!string.IsNullOrEmpty(shortcut))
            {
                item.KeyboardAcceleratorTextOverride = shortcut;
            }

            if (_commandRouter != null)
            {
                _commandRouter.WireMenuItem(item, commandId);
            }
            else
            {
                // Fallback - just log that command router isn't available
                item.Click += (_, __) => Debug.WriteLine($"[MainWindow] Command '{commandId}' unavailable - no CommandRouter");
            }

            return item;
        }
    }
}
