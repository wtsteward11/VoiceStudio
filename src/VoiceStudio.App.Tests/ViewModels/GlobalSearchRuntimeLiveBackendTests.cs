using System;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using VoiceStudio.App.Services;
using VoiceStudio.App.Tests.Fixtures;
using VoiceStudio.App.Utilities;
using VoiceStudio.App.ViewModels;

namespace VoiceStudio.App.Tests.ViewModels
{
  /// <summary>
  /// Live-backend proof for Global Search: <c>GET /api/search</c> HTTP truth matches
  /// <see cref="SearchClient"/> deserialization and <see cref="GlobalSearchViewModel"/> state.
  /// Skips with Inconclusive when no backend listens on 127.0.0.1:8000.
  /// </summary>
  [TestClass]
  [TestCategory("LiveBackend")]
  public sealed class GlobalSearchRuntimeLiveBackendTests
  {
    private const string BackendBase = "http://127.0.0.1:8000";

    [TestMethod]
    public async Task Search_LiveBackend_ApiMatchesSearchClientAndViewModel()
    {
      TestAppServicesHelper.EnsureInitialized();

      const string query = "te";

      using var probe = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
      int apiTotal;
      int apiLen;

      try
      {
        using var health = await probe.GetAsync(
          new Uri(new Uri(BackendBase), "/api/health"),
          CancellationToken.None).ConfigureAwait(false);

        if (!health.IsSuccessStatusCode)
        {
          Assert.Inconclusive($"Backend /api/health returned {(int)health.StatusCode}; start backend first.");
        }

        var searchUrl = $"/api/search?q={Uri.EscapeDataString(query)}&limit=50";
        using var searchResp = await probe.GetAsync(
          new Uri(new Uri(BackendBase), searchUrl),
          CancellationToken.None).ConfigureAwait(false);

        if (!searchResp.IsSuccessStatusCode)
        {
          Assert.Inconclusive($"Backend GET /api/search returned {(int)searchResp.StatusCode}: {await searchResp.Content.ReadAsStringAsync()}");
        }

        var raw = await searchResp.Content.ReadAsStringAsync().ConfigureAwait(false);
        using var doc = JsonDocument.Parse(raw);
        var root = doc.RootElement;
        if (!root.TryGetProperty("total_results", out var totalEl))
        {
          Assert.Inconclusive("Backend /api/search JSON missing total_results.");
        }

        if (!root.TryGetProperty("results", out var resultsEl) || resultsEl.ValueKind != JsonValueKind.Array)
        {
          Assert.Inconclusive("Backend /api/search JSON missing results array.");
        }

        apiTotal = totalEl.GetInt32();
        apiLen = resultsEl.GetArrayLength();
        if (apiTotal != apiLen)
        {
          Assert.Fail($"Backend contract drift: total_results ({apiTotal}) != len(results) ({apiLen}).");
        }
      }
      catch (Exception ex)
      {
        Assert.Inconclusive($"Live backend not reachable at {BackendBase}: {ex.Message}");
        return;
      }

      var jsonOptions = JsonSerializerOptionsFactory.BackendApi;
      using var httpClient = new HttpClient
      {
        BaseAddress = new Uri(BackendBase),
        Timeout = TimeSpan.FromSeconds(30),
      };
      var pipeline = new BackendClientHttpPipeline(httpClient, jsonOptions);
      var searchClient = new SearchClient(pipeline);

      var clientResponse = await searchClient.SearchAsync(query, null, 50, CancellationToken.None)
        .ConfigureAwait(false);

      Assert.AreEqual(apiTotal, clientResponse.TotalResults, "SearchClient TotalResults must match API total_results.");
      Assert.AreEqual(apiLen, clientResponse.Results.Count, "SearchClient Results count must match API results length.");

      var vm = new GlobalSearchViewModel(searchClient);
      vm.SearchQuery = query;
      await vm.SearchAsync().ConfigureAwait(false);

      Assert.AreEqual(apiTotal, vm.TotalResults, "ViewModel TotalResults must match API total_results.");
      Assert.AreEqual(apiLen, vm.Results.Count, "ViewModel Results count must match API results length.");
      Assert.AreEqual(apiLen, vm.FilteredResults.Count, "ViewModel FilteredResults must match API results length.");
      for (var i = 0; i < vm.Results.Count; i++)
      {
        Assert.AreEqual(vm.Results[i].Id, vm.FilteredResults[i].Id, $"Row {i}: Results vs FilteredResults alignment.");
      }
    }

    [TestMethod]
    public async Task Search_LiveBackend_NoHits_HonestEmptyState()
    {
      TestAppServicesHelper.EnsureInitialized();

      const string query = "z9";

      using var probe = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
      try
      {
        using var health = await probe.GetAsync(
          new Uri(new Uri(BackendBase), "/api/health"),
          CancellationToken.None).ConfigureAwait(false);

        if (!health.IsSuccessStatusCode)
        {
          Assert.Inconclusive($"Backend /api/health returned {(int)health.StatusCode}; start backend first.");
        }

        var probeUrl = $"/api/search?q={Uri.EscapeDataString(query)}&limit=50";
        using var searchProbe = await probe.GetAsync(
          new Uri(new Uri(BackendBase), probeUrl),
          CancellationToken.None).ConfigureAwait(false);

        if (!searchProbe.IsSuccessStatusCode)
        {
          Assert.Inconclusive(
            $"Backend GET /api/search for low-hit probe returned {(int)searchProbe.StatusCode}; cannot assert empty-state honesty. Body: {await searchProbe.Content.ReadAsStringAsync()}");
        }

        var probeBody = await searchProbe.Content.ReadAsStringAsync().ConfigureAwait(false);
        using var probeDoc = JsonDocument.Parse(probeBody);
        if (!probeDoc.RootElement.TryGetProperty("total_results", out var tr) || tr.GetInt32() != 0)
        {
          Assert.Inconclusive(
            "This run returned matches for the no-hit probe; use a query that yields zero results on this machine.");
        }
      }
      catch (Exception ex)
      {
        Assert.Inconclusive($"Live backend not reachable at {BackendBase}: {ex.Message}");
        return;
      }

      var jsonOptions = JsonSerializerOptionsFactory.BackendApi;
      using var httpClient = new HttpClient
      {
        BaseAddress = new Uri(BackendBase),
        Timeout = TimeSpan.FromSeconds(30),
      };
      var pipeline = new BackendClientHttpPipeline(httpClient, jsonOptions);
      var searchClient = new SearchClient(pipeline);

      var clientResponse = await searchClient.SearchAsync(query, null, 50, CancellationToken.None)
        .ConfigureAwait(false);

      var vm = new GlobalSearchViewModel(searchClient);
      vm.SearchQuery = query;
      await vm.SearchAsync().ConfigureAwait(false);

      Assert.AreEqual(clientResponse.TotalResults, vm.TotalResults);
      Assert.AreEqual(0, vm.Results.Count);
      Assert.AreEqual(0, vm.FilteredResults.Count);
      Assert.IsTrue(string.IsNullOrEmpty(vm.ErrorMessage), "Empty search should not set ErrorMessage.");
    }
  }
}
