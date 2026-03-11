using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using VoiceStudio.App.Services;
using VoiceStudio.Core.Services;

namespace VoiceStudio.App.Tests.Services
{
  /// <summary>
  /// Integration tests for IRequestCoordinator with real BackendClient.
  /// Verifies that concurrent calls coalesce to a single HTTP request and that
  /// RequestMetricsService.GetSnapshot() reflects coordination (low counts).
  /// </summary>
  [TestClass]
  public class RequestCoordinatorIntegrationTests
  {
    /// <summary>
    /// Mock HTTP handler that returns valid JSON for /api/profiles and /api/engines/list,
    /// and records each request path for assertion.
    /// </summary>
    private sealed class MockBackendHandler : HttpMessageHandler
    {
      private readonly Dictionary<string, int> _requestCountByPath = new();
      private readonly object _lock = new();

      public IReadOnlyDictionary<string, int> RequestCounts
      {
        get
        {
          lock (_lock)
          {
            return new Dictionary<string, int>(_requestCountByPath);
          }
        }
      }

      protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
      {
        var path = request.RequestUri?.AbsolutePath ?? "(empty)";

        lock (_lock)
        {
          _requestCountByPath.TryGetValue(path, out var count);
          _requestCountByPath[path] = count + 1;
        }

        var response = path switch
        {
          "/api/profiles" => new HttpResponseMessage(HttpStatusCode.OK)
          {
            Content = new StringContent("{\"items\":[]}")
          },
          "/api/engines/list" => new HttpResponseMessage(HttpStatusCode.OK)
          {
            Content = new StringContent("{\"engines\":[{\"id\":\"xtts\",\"name\":\"XTTs\"}]}")
          },
          _ => new HttpResponseMessage(HttpStatusCode.NotFound)
        };

        response.Content.Headers.ContentType =
          new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");

        return await Task.FromResult(response).ConfigureAwait(false);
      }
    }

    /// <summary>
    /// Verifies that concurrent GetProfilesAsync calls coalesce to a single HTTP request
    /// and that RequestMetricsService.GetSnapshot() shows /api/profiles: 1.
    /// </summary>
    [TestMethod]
    public async Task GetProfilesAsync_ConcurrentCalls_CoalescesToSingleHttpRequest_AndGetSnapshotShowsOne()
    {
      var mockHandler = new MockBackendHandler();
      var metrics = new RequestMetricsService();
      var coordinator = new RequestCoordinator();
      var correlationProvider = new CorrelationIdProvider();

      var config = new BackendClientConfig
      {
        BaseUrl = "http://localhost:8000",
        WebSocketUrl = string.Empty,
        RequestTimeout = TimeSpan.FromSeconds(30)
      };

      using var client = new BackendClient(
        config,
        correlationProvider,
        metrics,
        coordinator,
        mockHandler);

      await Task.WhenAll(
        client.GetProfilesAsync(),
        client.GetProfilesAsync(),
        client.GetProfilesAsync()).ConfigureAwait(false);

      var snapshot = metrics.GetSnapshot();
      var profilesCount = snapshot.TryGetValue("/api/profiles", out var c) ? c : 0;

      Assert.AreEqual(1, profilesCount,
        "RequestMetricsService.GetSnapshot() should show /api/profiles: 1 when 3 concurrent GetProfilesAsync calls coalesce");

      Assert.AreEqual(1, mockHandler.RequestCounts.TryGetValue("/api/profiles", out var reqCount) ? reqCount : 0,
        "Only 1 HTTP request to /api/profiles should have been made");
    }

    /// <summary>
    /// Verifies that concurrent GetEnginesAsync calls coalesce to a single HTTP request
    /// and that RequestMetricsService.GetSnapshot() shows /api/engines: 1.
    /// </summary>
    [TestMethod]
    public async Task GetEnginesAsync_ConcurrentCalls_CoalescesToSingleHttpRequest_AndGetSnapshotShowsOne()
    {
      var mockHandler = new MockBackendHandler();
      var metrics = new RequestMetricsService();
      var coordinator = new RequestCoordinator();
      var correlationProvider = new CorrelationIdProvider();

      var config = new BackendClientConfig
      {
        BaseUrl = "http://localhost:8000",
        WebSocketUrl = string.Empty,
        RequestTimeout = TimeSpan.FromSeconds(30)
      };

      using var client = new BackendClient(
        config,
        correlationProvider,
        metrics,
        coordinator,
        mockHandler);

      await Task.WhenAll(
        client.GetEnginesAsync(),
        client.GetEnginesAsync(),
        client.GetEnginesAsync()).ConfigureAwait(false);

      var snapshot = metrics.GetSnapshot();
      var enginesCount = snapshot.TryGetValue("/api/engines", out var c) ? c : 0;

      Assert.AreEqual(1, enginesCount,
        "RequestMetricsService.GetSnapshot() should show /api/engines: 1 when 3 concurrent GetEnginesAsync calls coalesce");

      Assert.AreEqual(1, mockHandler.RequestCounts.TryGetValue("/api/engines/list", out var reqCount) ? reqCount : 0,
        "Only 1 HTTP request to /api/engines/list should have been made");
    }

    /// <summary>
    /// Verifies that profiles and engines both show low counts when loaded concurrently
    /// (simulating multi-panel startup).
    /// </summary>
    [TestMethod]
    public async Task MultiPanelLoad_ProfilesAndEngines_GetSnapshotShowsLowCounts()
    {
      var mockHandler = new MockBackendHandler();
      var metrics = new RequestMetricsService();
      var coordinator = new RequestCoordinator();
      var correlationProvider = new CorrelationIdProvider();

      var config = new BackendClientConfig
      {
        BaseUrl = "http://localhost:8000",
        WebSocketUrl = string.Empty,
        RequestTimeout = TimeSpan.FromSeconds(30)
      };

      using var client = new BackendClient(
        config,
        correlationProvider,
        metrics,
        coordinator,
        mockHandler);

      await Task.WhenAll(
        client.GetProfilesAsync(),
        client.GetEnginesAsync(),
        client.GetProfilesAsync(),
        client.GetEnginesAsync()).ConfigureAwait(false);

      var snapshot = metrics.GetSnapshot();
      var profilesCount = snapshot.TryGetValue("/api/profiles", out var pc) ? pc : 0;
      var enginesCount = snapshot.TryGetValue("/api/engines", out var ec) ? ec : 0;

      Assert.IsTrue(profilesCount <= 1, $"Expected /api/profiles count <= 1, got {profilesCount}");
      Assert.IsTrue(enginesCount <= 1, $"Expected /api/engines count <= 1, got {enginesCount}");
    }
  }
}
