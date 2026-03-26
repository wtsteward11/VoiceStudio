using System.Collections.Generic;
using System.Threading;

namespace VoiceStudio.Core.Panels;

/// <summary>
/// Typed navigation context for search results. Replaces stringly-typed itemId/resultType.
/// </summary>
public sealed class SearchNavigationContext
{
    public string ItemId { get; }
    public SearchResultType ResultType { get; }
    public string Title { get; }
    public string PanelId { get; }
    public IReadOnlyDictionary<string, object>? Metadata { get; }
    public CancellationToken CancellationToken { get; }

    public SearchNavigationContext(
        string itemId,
        SearchResultType resultType,
        string title,
        string panelId,
        CancellationToken cancellationToken = default,
        IReadOnlyDictionary<string, object>? metadata = null)
    {
        ItemId = itemId ?? string.Empty;
        ResultType = resultType;
        Title = title ?? string.Empty;
        PanelId = panelId ?? string.Empty;
        CancellationToken = cancellationToken;
        Metadata = metadata;
    }

    /// <summary>
    /// Creates context from a backend SearchResultItem (Id, Type, Title, PanelId, optional Metadata).
    /// </summary>
    public static SearchNavigationContext FromSearchResult(
        string id,
        string type,
        string title,
        string panelId,
        CancellationToken ct = default,
        IReadOnlyDictionary<string, object>? metadata = null)
    {
        var resultType = SearchResultTypeMapper.FromString(type);
        return new SearchNavigationContext(id, resultType, title, panelId, ct, metadata);
    }
}
