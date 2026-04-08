using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using VoiceStudio.App.Core.Commands;
using VoiceStudio.Core.Models;

namespace VoiceStudio.App.Services;

/// <summary>
/// Local search provider for command palette commands.
/// </summary>
public sealed class CommandSearchProvider : ILocalSearchProvider
{
    private readonly IUnifiedCommandRegistry _commandRegistry;

    public CommandSearchProvider(IUnifiedCommandRegistry commandRegistry)
    {
        _commandRegistry = commandRegistry ?? throw new ArgumentNullException(nameof(commandRegistry));
    }

    public Task<IReadOnlyList<SearchResultItem>> SearchAsync(string query, int limit, CancellationToken cancellationToken = default)
    {
        var normalized = query.Trim();
        if (string.IsNullOrWhiteSpace(normalized))
            return Task.FromResult<IReadOnlyList<SearchResultItem>>(Array.Empty<SearchResultItem>());

        var results = _commandRegistry.GetAllCommands()
            .Where(c =>
                c.Id.Contains(normalized, StringComparison.OrdinalIgnoreCase) ||
                c.Title.Contains(normalized, StringComparison.OrdinalIgnoreCase) ||
                (!string.IsNullOrWhiteSpace(c.Description) && c.Description.Contains(normalized, StringComparison.OrdinalIgnoreCase)) ||
                (!string.IsNullOrWhiteSpace(c.Category) && c.Category.Contains(normalized, StringComparison.OrdinalIgnoreCase)))
            .Take(limit)
            .Select(c => new SearchResultItem
            {
                Id = $"command:{c.Id}",
                Type = "command",
                Title = c.Title,
                Description = c.Description,
                PanelId = "command-palette",
                Preview = c.Category,
                Metadata = new Dictionary<string, object>
                {
                    ["command_id"] = c.Id,
                    ["category"] = c.Category
                }
            })
            .ToList();

        return Task.FromResult<IReadOnlyList<SearchResultItem>>(results);
    }
}
