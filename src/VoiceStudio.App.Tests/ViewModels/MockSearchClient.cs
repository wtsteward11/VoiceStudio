using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using VoiceStudio.Core.Models;
using VoiceStudio.Core.Services;

namespace VoiceStudio.App.Tests.ViewModels
{
  /// <summary>
  /// Test search client that returns controlled Search responses for GlobalSearchViewModel tests.
  /// </summary>
  public sealed class MockSearchClient : ISearchClient
  {
    public SearchResponse? SearchResponse { get; set; }
    public Exception? SearchException { get; set; }
    public TaskCompletionSource<bool>? SearchBlocker { get; set; }
    public int SearchCallCount { get; private set; }
    public string? LastSearchQuery { get; private set; }

    public async Task<SearchResponse> SearchAsync(string query, string? types, int limit, CancellationToken cancellationToken)
    {
      SearchCallCount++;
      LastSearchQuery = query;

      await Task.Yield();

      var blocker = SearchBlocker;
      if (blocker != null)
      {
        await blocker.Task;
      }

      if (SearchException != null)
      {
        throw SearchException;
      }

      return SearchResponse ?? new SearchResponse
      {
        Results = new List<SearchResultItem>(),
        TotalResults = 0,
        ResultsByType = new Dictionary<string, int>()
      };
    }
  }
}
