using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using VoiceStudio.Core.Models;

namespace VoiceStudio.App.Services;

/// <summary>
/// Local search provider for settings/navigation affordances.
/// </summary>
public sealed class SettingsSearchProvider : ILocalSearchProvider
{
    private static readonly (string Id, string Title, string Description)[] SettingIndex =
    {
        ("settings.theme", "Theme", "Configure application theme and appearance"),
        ("settings.audio", "Audio Settings", "Configure playback, recording, and monitoring"),
        ("settings.engine", "Engine Settings", "Configure synthesis/transcription engines"),
        ("settings.shortcuts", "Keyboard Shortcuts", "View and customize keyboard shortcuts"),
        ("settings.workspace", "Workspace", "Manage workspace layouts and panel arrangement"),
        ("settings.performance", "Performance", "Tune rendering and runtime performance")
    };

    public Task<IReadOnlyList<SearchResultItem>> SearchAsync(string query, int limit, CancellationToken cancellationToken = default)
    {
        var normalized = query.Trim();
        if (string.IsNullOrWhiteSpace(normalized))
            return Task.FromResult<IReadOnlyList<SearchResultItem>>(Array.Empty<SearchResultItem>());

        var results = SettingIndex
            .Where(item =>
                item.Title.Contains(normalized, StringComparison.OrdinalIgnoreCase) ||
                item.Description.Contains(normalized, StringComparison.OrdinalIgnoreCase) ||
                item.Id.Contains(normalized, StringComparison.OrdinalIgnoreCase))
            .Take(limit)
            .Select(item => new SearchResultItem
            {
                Id = item.Id,
                Type = "setting",
                Title = item.Title,
                Description = item.Description,
                PanelId = "settings",
                Preview = item.Description,
                Metadata = new Dictionary<string, object> { ["source"] = "local-settings-index" }
            })
            .ToList();

        return Task.FromResult<IReadOnlyList<SearchResultItem>>(results);
    }
}
