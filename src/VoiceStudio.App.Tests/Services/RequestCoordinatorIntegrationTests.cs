using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using VoiceStudio.App.Services;
using VoiceStudio.App.UseCases;
using VoiceStudio.Core.Models;
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
        var method = request.Method.Method;
        var key = $"{method} {path}";

        lock (_lock)
        {
          _requestCountByPath.TryGetValue(key, out var count);
          _requestCountByPath[key] = count + 1;
          _requestCountByPath.TryGetValue(path, out var pathCount);
          _requestCountByPath[path] = pathCount + 1;
        }

        var response = (path, request.Method) switch
        {
          (_, _) when request.Method == HttpMethod.Post && path == "/api/profiles" => new HttpResponseMessage(HttpStatusCode.OK)
          {
            Content = new StringContent("{\"id\":\"new-1\",\"name\":\"New Profile\",\"language\":\"en\"}")
          },
          ("/api/profiles", _) => new HttpResponseMessage(HttpStatusCode.OK)
          {
            Content = new StringContent("{\"items\":[]}")
          },
          (_, _) when request.Method == HttpMethod.Post && path == "/api/projects" => new HttpResponseMessage(HttpStatusCode.OK)
          {
            Content = new StringContent("{\"id\":\"proj-1\",\"name\":\"New Project\",\"description\":null}")
          },
          ("/api/projects", _) => new HttpResponseMessage(HttpStatusCode.OK)
          {
            Content = new StringContent("{\"items\":[]}")
          },
          ("/api/engines/list", _) => new HttpResponseMessage(HttpStatusCode.OK)
          {
            Content = new StringContent("{\"engines\":[{\"id\":\"xtts\",\"name\":\"XTTs\"}]}")
          },
          (_, _) when path.Contains("/tracks") && !path.Contains("/clips") && request.Method == HttpMethod.Get =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
              Content = new StringContent("[{\"id\":\"t1\",\"name\":\"Track 1\",\"clips\":[]}]")
            },
          (_, _) when path.Contains("/clips") && request.Method == HttpMethod.Post =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
              Content = new StringContent("{\"id\":\"c1\",\"name\":\"Clip 1\",\"profileId\":\"p1\",\"audioId\":\"a1\",\"durationSeconds\":1.0,\"startTime\":0.0}")
            },
          (_, _) when path.Contains("/clips") && request.Method == HttpMethod.Delete =>
            new HttpResponseMessage(HttpStatusCode.OK),
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
        gracefulDegradation: null,
        innerHandler: mockHandler);

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
        gracefulDegradation: null,
        innerHandler: mockHandler);

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
        gracefulDegradation: null,
        innerHandler: mockHandler);

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

    /// <summary>
    /// Verifies that CreateProfileAsync invalidates the profiles cache, so the next GetProfilesAsync
    /// hits the backend instead of returning stale cached data.
    /// </summary>
    [TestMethod]
    public async Task CreateProfile_InvalidatesCache_NextGetProfilesRefetches()
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
        gracefulDegradation: null,
        innerHandler: mockHandler);

      var profiles1 = await client.GetProfilesAsync().ConfigureAwait(false);
      Assert.IsNotNull(profiles1);

      await client.CreateProfileAsync("New Profile").ConfigureAwait(false);

      var profiles2 = await client.GetProfilesAsync().ConfigureAwait(false);
      Assert.IsNotNull(profiles2);

      var getCount = mockHandler.RequestCounts.TryGetValue("GET /api/profiles", out var gc) ? gc : 0;
      Assert.AreEqual(2, getCount,
        "GetProfilesAsync should be called twice: once before create, once after (cache invalidated by create).");
    }

    /// <summary>
    /// CI-capable proof: ProfilesUseCase.ListAsync (used by ProfilesViewModel.LoadProfilesAsync) coalesces
    /// concurrent calls to a bounded number of HTTP requests. Asserts /api/profiles count <= 2.
    /// </summary>
    [TestMethod]
    public async Task ProfilesUseCase_ConcurrentListAsync_CoalescesToBoundedRequests()
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
        gracefulDegradation: null,
        innerHandler: mockHandler);

      var profilesClient = new ProfilesClient(client, coordinator);
      var useCase = new ProfilesUseCase(profilesClient);

      await Task.WhenAll(
        useCase.ListAsync(),
        useCase.ListAsync(),
        useCase.ListAsync()).ConfigureAwait(false);

      var snapshot = metrics.GetSnapshot();
      var profilesCount = snapshot.TryGetValue("/api/profiles", out var c) ? c : 0;

      Assert.IsTrue(profilesCount <= 2,
        $"ProfilesUseCase.ListAsync (LoadProfilesAsync path) should coalesce to <= 2 requests, got {profilesCount}");
    }

    /// <summary>
    /// Scenario: open Profiles → load → create profile → refresh; assert bounded request counts.
    /// Fail if: panel loading fans out duplicate fetches, create causes unnecessary reloads,
    /// or idle refresh re-hits stable list endpoints too often.
    /// </summary>
    [TestMethod]
    public async Task ProfilesPanelScenario_LoadCreateRefresh_BoundedRequestCounts()
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
        gracefulDegradation: null,
        innerHandler: mockHandler);

      // Simulate: open panel (load profiles + engines)
      await Task.WhenAll(
        client.GetProfilesAsync(),
        client.GetEnginesAsync()).ConfigureAwait(false);

      // Create profile (invalidates cache)
      await client.CreateProfileAsync("New Profile").ConfigureAwait(false);

      // Refresh (next GetProfiles should hit backend; create invalidated cache)
      await client.GetProfilesAsync().ConfigureAwait(false);

      var snapshot = metrics.GetSnapshot();
      var profilesCount = snapshot.TryGetValue("/api/profiles", out var pc) ? pc : 0;
      var enginesCount = snapshot.TryGetValue("/api/engines", out var ec) ? ec : 0;

      Assert.IsTrue(profilesCount <= 3,
        $"Profile load + create + refresh should not storm; got /api/profiles: {profilesCount}");
      Assert.IsTrue(enginesCount <= 1,
        $"Engines load should coalesce; got /api/engines: {enginesCount}");

      var getProfilesCount = mockHandler.RequestCounts.TryGetValue("GET /api/profiles", out var gc) ? gc : 0;
      Assert.AreEqual(2, getProfilesCount,
        "GetProfilesAsync should be called twice: once on load, once after create (cache invalidated).");
    }

    /// <summary>
    /// Verifies that concurrent GetProjectsAsync calls coalesce to a single HTTP request.
    /// </summary>
    [TestMethod]
    public async Task GetProjectsAsync_ConcurrentCalls_Coalesce()
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
        gracefulDegradation: null,
        innerHandler: mockHandler);

      await Task.WhenAll(
        client.GetProjectsAsync(),
        client.GetProjectsAsync(),
        client.GetProjectsAsync()).ConfigureAwait(false);

      var getCount = mockHandler.RequestCounts.TryGetValue("/api/projects", out var c) ? c : 0;
      Assert.AreEqual(1, getCount,
        "GetProjectsAsync concurrent calls should coalesce to a single HTTP request");
    }

    /// <summary>
    /// Verifies that CreateProjectAsync invalidates the projects cache, so the next GetProjectsAsync refetches.
    /// </summary>
    [TestMethod]
    public async Task CreateProject_InvalidatesCache_NextGetProjectsRefetches()
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
        gracefulDegradation: null,
        innerHandler: mockHandler);

      var projects1 = await client.GetProjectsAsync().ConfigureAwait(false);
      Assert.IsNotNull(projects1);

      await client.CreateProjectAsync("New Project").ConfigureAwait(false);

      var projects2 = await client.GetProjectsAsync().ConfigureAwait(false);
      Assert.IsNotNull(projects2);

      var getCount = mockHandler.RequestCounts.TryGetValue("GET /api/projects", out var gc) ? gc : 0;
      Assert.AreEqual(2, getCount,
        "GetProjectsAsync should be called twice: once before create, once after (cache invalidated by create).");
    }

    /// <summary>
    /// Scenario: Timeline panel refresh + load profiles. Simulates RefreshAsync (LoadProjects) and LoadProfiles.
    /// Asserts bounded request counts for /api/projects and /api/profiles.
    /// </summary>
    [TestMethod]
    public async Task TimelinePanelScenario_RefreshLoadProfiles_BoundedRequestCounts()
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
        gracefulDegradation: null,
        innerHandler: mockHandler);

      // Simulate Timeline refresh flow: projects (RefreshAsync) + profiles (LoadProfilesCommand)
      await Task.WhenAll(
        client.GetProjectsAsync(),
        client.GetProfilesAsync()).ConfigureAwait(false);

      var snapshot = metrics.GetSnapshot();
      var projectsCount = snapshot.TryGetValue("/api/projects", out var pc) ? pc : 0;
      var profilesCount = snapshot.TryGetValue("/api/profiles", out var prc) ? prc : 0;

      Assert.IsTrue(projectsCount <= 2,
        $"Timeline refresh (GetProjectsAsync) should be bounded; got /api/projects: {projectsCount}");
      Assert.IsTrue(profilesCount <= 2,
        $"Timeline LoadProfiles should be bounded; got /api/profiles: {profilesCount}");
    }

    /// <summary>
    /// Scenario: open Timeline, load projects, select project, load tracks, perform one clip action (create).
    /// Asserts bounded request counts for stable reads. Clip CRUD goes through ITimelineClipService (BackendClient).
    /// </summary>
    [TestMethod]
    public async Task TimelinePanelScenario_LoadProjectsSelectProjectLoadTracksCreateClip_BoundedRequestCounts()
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
        gracefulDegradation: null,
        innerHandler: mockHandler);

      // Simulate: load projects, select project, load tracks, create clip
      var projects = await client.GetProjectsAsync().ConfigureAwait(false);
      Assert.IsNotNull(projects);

      var projectId = projects.Count > 0 ? projects[0].Id : "proj-1";
      var tracks = await client.GetTracksAsync(projectId).ConfigureAwait(false);
      Assert.IsNotNull(tracks);

      var trackId = tracks.Count > 0 ? tracks[0].Id : "t1";
      var clip = new AudioClip
      {
        Id = "temp-1",
        Name = "Test Clip",
        ProfileId = "p1",
        AudioId = "a1",
        Duration = TimeSpan.FromSeconds(1.0),
        StartTime = 0.0
      };

      await client.CreateClipAsync(projectId, trackId, clip).ConfigureAwait(false);

      var snapshot = metrics.GetSnapshot();
      var projectsCount = snapshot.TryGetValue("/api/projects", out var pc) ? pc : 0;
      var tracksPath = $"/api/projects/{projectId}/tracks";
      var tracksCount = snapshot.TryGetValue(tracksPath, out var tc) ? tc : 0;

      Assert.IsTrue(projectsCount <= 2,
        $"Timeline load projects should be bounded; got /api/projects: {projectsCount}");
      Assert.IsTrue(tracksCount <= 2,
        $"Timeline load tracks should be bounded; got {tracksPath}: {tracksCount}");
    }
  }
}
