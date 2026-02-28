using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using VoiceStudio.App.Services;
using VoiceStudio.Core.Models;
using VoiceStudio.Core.Services;

namespace VoiceStudio.App.Tests.ViewModels
{
  /// <summary>
  /// Test backend client that returns controlled Search responses for GlobalSearchViewModel tests.
  /// All other backend operations use the real BackendClient implementation.
  /// </summary>
  public sealed class MockBackendClient : BackendClient, IBackendClient
  {
    public SearchResponse? SearchResponse { get; set; }
    public Exception? SearchException { get; set; }
    public TaskCompletionSource<bool>? SearchBlocker { get; set; }
    public int SearchCallCount { get; private set; }
    public string? LastSearchQuery { get; private set; }

    public MockBackendClient()
        : base(new BackendClientConfig
        {
          BaseUrl = "http://localhost:8001",
          WebSocketUrl = string.Empty,
          RequestTimeout = TimeSpan.FromSeconds(30)
        })
    {
    }

    async Task<SearchResponse> IBackendClient.SearchAsync(string query, string? types, int limit, CancellationToken cancellationToken)
    {
      SearchCallCount++;
      LastSearchQuery = query;

      // Ensure asynchronous behavior so ViewModel loading states can be observed in tests.
      await Task.Yield();

      // Optional test hook: block completion until released by the test.
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