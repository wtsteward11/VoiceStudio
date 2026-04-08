using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using VoiceStudio.Core.Models;

namespace VoiceStudio.App.Services;

/// <summary>
/// Provides client-side search results to augment backend global search.
/// </summary>
public interface ILocalSearchProvider
{
    Task<IReadOnlyList<SearchResultItem>> SearchAsync(string query, int limit, CancellationToken cancellationToken = default);
}
