using System;
using System.Threading.Tasks;
using Microsoft.UI.Xaml.Controls;

namespace VoiceStudio.App.Services;

/// <summary>
/// GAP-008 Slice 22: builds the File → Recent Projects flyout subtree only.
/// Does not implement Pin/Unpin/Clear/Open — delegates to workflow + mutation bridges (Slice 4 + Slice 5).
/// </summary>
public sealed class MainWindowRecentProjectsMenuPopulationShellBridge
{
    private readonly Func<string, string, Task> _openRecentProjectAsync;
    private readonly Func<string, Task> _pinRecentProjectAsync;
    private readonly Func<string, Task> _unpinRecentProjectAsync;
    private readonly Func<string, Task> _removeFromRecentListAsync;
    private readonly Func<Task> _clearRecentProjectsAsync;

    public MainWindowRecentProjectsMenuPopulationShellBridge(
        Func<string, string, Task> openRecentProjectAsync,
        Func<string, Task> pinRecentProjectAsync,
        Func<string, Task> unpinRecentProjectAsync,
        Func<string, Task> removeFromRecentListAsync,
        Func<Task> clearRecentProjectsAsync)
    {
        ArgumentNullException.ThrowIfNull(openRecentProjectAsync);
        ArgumentNullException.ThrowIfNull(pinRecentProjectAsync);
        ArgumentNullException.ThrowIfNull(unpinRecentProjectAsync);
        ArgumentNullException.ThrowIfNull(removeFromRecentListAsync);
        ArgumentNullException.ThrowIfNull(clearRecentProjectsAsync);
        _openRecentProjectAsync = openRecentProjectAsync;
        _pinRecentProjectAsync = pinRecentProjectAsync;
        _unpinRecentProjectAsync = unpinRecentProjectAsync;
        _removeFromRecentListAsync = removeFromRecentListAsync;
        _clearRecentProjectsAsync = clearRecentProjectsAsync;
    }

    /// <summary>
    /// Rebuilds the recent-projects submenu from service state. No-op when submenu or service is null.
    /// </summary>
    public void Populate(MenuFlyoutSubItem? recentProjectsSubMenu, RecentProjectsService? recentProjectsService)
    {
        if (recentProjectsSubMenu == null || recentProjectsService == null)
        {
            return;
        }

        recentProjectsSubMenu.Items.Clear();

        var allProjects = recentProjectsService.AllProjects;

        if (allProjects.Count == 0)
        {
            recentProjectsSubMenu.Items.Add(new MenuFlyoutItem
            {
                Text = "No recent projects",
                IsEnabled = false
            });
            return;
        }

        var pinnedProjects = recentProjectsService.PinnedProjects;
        if (pinnedProjects.Count > 0)
        {
            foreach (var project in pinnedProjects)
            {
                var subMenu = new MenuFlyoutSubItem
                {
                    Text = $"📌 {project.Name}"
                };
                var openItem = new MenuFlyoutItem
                {
                    Text = "Open",
                    Tag = project.Path
                };
                var pathCopy = project.Path;
                var nameCopy = project.Name;
                openItem.Click += async (_, _) =>
                    await _openRecentProjectAsync(pathCopy, nameCopy).ConfigureAwait(true);
                subMenu.Items.Add(openItem);
                subMenu.Items.Add(new MenuFlyoutSeparator());

                var unpinItem = new MenuFlyoutItem
                {
                    Text = "Unpin",
                    Tag = project.Path
                };
                var unpinPath = project.Path;
                unpinItem.Click += async (_, _) =>
                    await _unpinRecentProjectAsync(unpinPath).ConfigureAwait(true);
                subMenu.Items.Add(unpinItem);

                recentProjectsSubMenu.Items.Add(subMenu);
            }

            if (recentProjectsService.RecentProjects.Count > 0)
            {
                recentProjectsSubMenu.Items.Add(new MenuFlyoutSeparator());
            }
        }

        foreach (var project in recentProjectsService.RecentProjects)
        {
            var subMenu = new MenuFlyoutSubItem
            {
                Text = project.Name
            };
            var openItem2 = new MenuFlyoutItem
            {
                Text = "Open",
                Tag = project.Path
            };
            var rPath = project.Path;
            var rName = project.Name;
            openItem2.Click += async (_, _) =>
                await _openRecentProjectAsync(rPath, rName).ConfigureAwait(true);
            subMenu.Items.Add(openItem2);
            subMenu.Items.Add(new MenuFlyoutSeparator());

            var pinItem = new MenuFlyoutItem
            {
                Text = "Pin",
                Tag = project.Path
            };
            var pinPath = project.Path;
            pinItem.Click += async (_, _) =>
                await _pinRecentProjectAsync(pinPath).ConfigureAwait(true);
            subMenu.Items.Add(pinItem);

            var removeItem = new MenuFlyoutItem
            {
                Text = "Remove from list",
                Tag = project.Path
            };
            var remPath = project.Path;
            removeItem.Click += async (_, _) =>
                await _removeFromRecentListAsync(remPath).ConfigureAwait(true);
            subMenu.Items.Add(removeItem);

            recentProjectsSubMenu.Items.Add(subMenu);
        }

        if (allProjects.Count > 0)
        {
            recentProjectsSubMenu.Items.Add(new MenuFlyoutSeparator());
            var clearItem = new MenuFlyoutItem
            {
                Text = "Clear Recent Projects"
            };
            clearItem.Click += async (_, _) =>
                await _clearRecentProjectsAsync().ConfigureAwait(true);
            recentProjectsSubMenu.Items.Add(clearItem);
        }
    }
}
