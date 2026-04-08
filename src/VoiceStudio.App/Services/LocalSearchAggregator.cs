using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using VoiceStudio.Core.Models;
using VoiceStudio.Core.Services;

namespace VoiceStudio.App.Services;

/// <summary>
/// Combines backend search with local providers so global search remains useful offline.
/// </summary>
public sealed class LocalSearchAggregator : IGlobalSearchService
{
    private readonly ISearchClient _searchClient;
    private readonly IReadOnlyList<ILocalSearchProvider> _localProviders;

    public LocalSearchAggregator(ISearchClient searchClient, IEnumerable<ILocalSearchProvider> localProviders)
    {
        _searchClient = searchClient ?? throw new ArgumentNullException(nameof(searchClient));
        _localProviders = (localProviders ?? throw new ArgumentNullException(nameof(localProviders))).ToList();
    }

    public async Task<SearchResponse> SearchAsync(string query, string? types = null, int limit = 50, CancellationToken cancellationToken = default)
    {
        var localResults = await CollectLocalResultsAsync(query, limit, cancellationToken);

        SearchResponse backendResponse;
        try
        {
            backendResponse = await _searchClient.SearchAsync(query, types, limit, cancellationToken);
        }
        catch
        {
            backendResponse = new SearchResponse { Query = query };
        }

        var mergedResults = backendResponse.Results
            .Concat(localResults)
            .GroupBy(r => r.Id, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .Take(limit)
            .ToList();

        var resultsByType = mergedResults
            .GroupBy(r => r.Type ?? "unknown", StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);

        return new SearchResponse
        {
            Query = query,
            Results = mergedResults,
            TotalResults = mergedResults.Count,
            ResultsByType = resultsByType
        };
    }

    private async Task<List<SearchResultItem>> CollectLocalResultsAsync(string query, int limit, CancellationToken cancellationToken)
    {
        var tasks = _localProviders
            .Select(provider => provider.SearchAsync(query, limit, cancellationToken))
            .ToArray();

        var results = await Task.WhenAll(tasks);
        return results.SelectMany(r => r).Take(limit).ToList();
    }
}
