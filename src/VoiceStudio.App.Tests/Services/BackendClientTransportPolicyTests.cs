using System;
using System.Reflection;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using VoiceStudio.App.Services;
using VoiceStudio.App.Services.Gateways;
using VoiceStudio.App.UseCases;
using VoiceStudio.Core.Exceptions;
using VoiceStudio.Core.Gateways;
using VoiceStudio.Core.Services;
namespace VoiceStudio.App.Tests.Services
{
  /// <summary>
  /// Focused tests for <see cref="BackendClient"/> HTTP policy (retry, error mapping) via
  /// <see cref="BackendClientHttpPipeline"/> without real network.
  /// </summary>
  [TestClass]
  public class BackendClientTransportPolicyTests
  {
    private sealed class TransportTestHandler : HttpMessageHandler
    {
      private readonly Func<HttpRequestMessage, int, HttpResponseMessage> _respond;
      private int _sequence;

      public TransportTestHandler(Func<HttpRequestMessage, int, HttpResponseMessage> respond)
      {
        _respond = respond;
      }

      protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
      {
        var n = Interlocked.Increment(ref _sequence);
        return Task.FromResult(_respond(request, n));
      }
    }

    private static BackendClient CreateClient(HttpMessageHandler inner)
    {
      var config = new BackendClientConfig
      {
        BaseUrl = "http://localhost:8000",
        WebSocketUrl = string.Empty,
        RequestTimeout = TimeSpan.FromSeconds(30)
      };

      return new BackendClient(
        config,
        new CorrelationIdProvider(),
        requestMetrics: null,
        requestCoordinator: new RequestCoordinator(),
        gracefulDegradation: null,
        innerHandler: inner);
    }

    /// <summary>
    /// Creates BackendClient, HealthVersionClient, and ConnectionStatusClient sharing the same pipeline (BackendHttpContext).
    /// Uses reflection because BackendHttpContext and internal constructors are not directly accessible.
    /// </summary>
    private static (BackendClient BackendClient, IHealthVersionClient HealthVersionClient, IConnectionStatusClient ConnectionStatusClient) CreateClientWithSharedContext(HttpMessageHandler inner)
    {
      var config = new BackendClientConfig
      {
        BaseUrl = "http://localhost:8000",
        WebSocketUrl = string.Empty,
        RequestTimeout = TimeSpan.FromSeconds(30)
      };
      var appAssembly = typeof(BackendClient).Assembly;
      var contextType = appAssembly.GetType("VoiceStudio.App.Services.BackendHttpContext")
        ?? throw new InvalidOperationException("BackendHttpContext type not found");
      var context = Activator.CreateInstance(contextType, config, new CorrelationIdProvider(), null, null, inner)
        ?? throw new InvalidOperationException("Failed to create BackendHttpContext");
      var backendCtor = typeof(BackendClient).GetConstructor(
        System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance,
        new[] { contextType, typeof(BackendClientConfig), typeof(IRequestCoordinator) })
        ?? throw new InvalidOperationException("BackendClient(context, config, coordinator) constructor not found");
      var backend = (BackendClient)backendCtor.Invoke(new object[] { context, config, new RequestCoordinator() })!;
      var healthType = appAssembly.GetType("VoiceStudio.App.Services.HealthVersionClient")
        ?? throw new InvalidOperationException("HealthVersionClient type not found");
      var healthCtor = healthType.GetConstructor(
        System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance,
        new[] { contextType })
        ?? throw new InvalidOperationException("HealthVersionClient(context) constructor not found");
      var health = healthCtor.Invoke(new[] { context })
        ?? throw new InvalidOperationException("Failed to create HealthVersionClient");
      var connType = appAssembly.GetType("VoiceStudio.App.Services.ConnectionStatusClient")
        ?? throw new InvalidOperationException("ConnectionStatusClient type not found");
      var connCtor = connType.GetConstructor(
        System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance,
        new[] { contextType })
        ?? throw new InvalidOperationException("ConnectionStatusClient(context) constructor not found");
      var conn = connCtor.Invoke(new[] { context })
        ?? throw new InvalidOperationException("Failed to create ConnectionStatusClient");
      return (backend, (IHealthVersionClient)health, (IConnectionStatusClient)conn);
    }

    private static HttpResponseMessage JsonOk(string json)
    {
      var r = new HttpResponseMessage(HttpStatusCode.OK)
      {
        Content = new StringContent(json)
      };
      r.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");
      return r;
    }

    private static HttpResponseMessage HealthOk() => new(HttpStatusCode.OK);

    [TestMethod]
    public async Task GetAsync_Retryable500_Then200_Succeeds()
    {
      var testCalls = 0;
      var handler = new TransportTestHandler((req, seq) =>
      {
        var path = req.RequestUri?.AbsolutePath ?? "";
        if (path.Contains("/api/health", StringComparison.Ordinal))
          return HealthOk();

        if (path == "/api/transport-test")
        {
          Interlocked.Increment(ref testCalls);
          return testCalls < 2
            ? new HttpResponseMessage(HttpStatusCode.InternalServerError)
            {
              Content = new StringContent("{\"message\":\"transient\"}")
            }
            : JsonOk("{\"status\":\"ok\"}");
        }

        return new HttpResponseMessage(HttpStatusCode.NotFound);
      });

      using var client = CreateClient(handler);
      var body = await client.GetAsync<RetryOkDto>("/api/transport-test", CancellationToken.None).ConfigureAwait(false);
      Assert.IsNotNull(body);
      Assert.AreEqual("ok", body!.Status);
      Assert.AreEqual(2, testCalls, "First 500 should be retried once before success.");
    }

    [TestMethod]
    public async Task GetAsync_401_DoesNotRetryMainRequest()
    {
      var testCalls = 0;
      var handler = new TransportTestHandler((req, seq) =>
      {
        var path = req.RequestUri?.AbsolutePath ?? "";
        if (path.Contains("/api/health", StringComparison.Ordinal))
          return HealthOk();

        if (path == "/api/transport-auth")
        {
          Interlocked.Increment(ref testCalls);
          return new HttpResponseMessage(HttpStatusCode.Unauthorized)
          {
            Content = new StringContent("{\"message\":\"nope\"}")
          };
        }

        return new HttpResponseMessage(HttpStatusCode.NotFound);
      });

      using var client = CreateClient(handler);
      try
      {
        await client.GetAsync<RetryOkDto>("/api/transport-auth", CancellationToken.None).ConfigureAwait(false);
        Assert.Fail("Expected BackendAuthenticationException");
      }
      catch (BackendAuthenticationException)
      {
        Assert.AreEqual(1, testCalls, "Non-retryable 401 must not retry the API call.");
      }
    }

    [TestMethod]
    public async Task GetAsync_422Json_SetsErrorCodeOnBackendValidationException()
    {
      var handler = new TransportTestHandler((req, seq) =>
      {
        var path = req.RequestUri?.AbsolutePath ?? "";
        if (path.Contains("/api/health", StringComparison.Ordinal))
          return HealthOk();

        if (path == "/api/transport-validate")
        {
          return new HttpResponseMessage((HttpStatusCode)422)
          {
            Content = new StringContent("{\"message\":\"bad\",\"error_code\":\"E_BAD\"}")
          };
        }

        return new HttpResponseMessage(HttpStatusCode.NotFound);
      });

      using var client = CreateClient(handler);
      try
      {
        await client.GetAsync<RetryOkDto>("/api/transport-validate", CancellationToken.None).ConfigureAwait(false);
        Assert.Fail("Expected BackendValidationException");
      }
      catch (BackendValidationException ex)
      {
        Assert.AreEqual("E_BAD", ex.ErrorCode);
        Assert.IsFalse(ex.IsRetryable);
      }
    }

    [TestMethod]
    public async Task GetAsync_TaskCanceledWithoutUserCancellation_BecomesBackendTimeoutException()
    {
      var handler = new TransportTestHandler((req, seq) =>
      {
        var path = req.RequestUri?.AbsolutePath ?? "";
        if (path.Contains("/api/health", StringComparison.Ordinal))
          return HealthOk();

        if (path == "/api/transport-slow")
          throw new TaskCanceledException("The operation was canceled.");

        return new HttpResponseMessage(HttpStatusCode.NotFound);
      });

      using var client = CreateClient(handler);
      try
      {
        await client.GetAsync<RetryOkDto>("/api/transport-slow", CancellationToken.None).ConfigureAwait(false);
        Assert.Fail("Expected BackendTimeoutException after retries");
      }
      catch (BackendTimeoutException ex)
      {
        Assert.IsNotNull(ex, "Expected BackendTimeoutException from retry exhaustion");
        // ExecuteWithRetryAsync maps non-user TaskCanceledException to BackendTimeoutException.
      }
    }

    [TestMethod]
    public async Task GetAsync_MalformedJsonErrorBody_FallsBackSafely()
    {
      var handler = new TransportTestHandler((req, seq) =>
      {
        var path = req.RequestUri?.AbsolutePath ?? "";
        if (path.Contains("/api/health", StringComparison.Ordinal))
          return HealthOk();

        if (path == "/api/transport-bad-json")
        {
          return new HttpResponseMessage(HttpStatusCode.InternalServerError)
          {
            Content = new StringContent("not valid json at all")
          };
        }

        return new HttpResponseMessage(HttpStatusCode.NotFound);
      });

      using var client = CreateClient(handler);
      try
      {
        await client.GetAsync<RetryOkDto>("/api/transport-bad-json", CancellationToken.None).ConfigureAwait(false);
        Assert.Fail("Expected BackendServerException");
      }
      catch (BackendServerException ex)
      {
        Assert.IsTrue(ex.Message.Contains("not valid json", StringComparison.OrdinalIgnoreCase),
          "Malformed JSON error body should fall back to truncated content as message.");
      }
    }

    [TestMethod]
    public async Task SendRequestAsync_Delete_EmptyResponseBody_ReturnsDefault()
    {
      var handler = new TransportTestHandler((req, seq) =>
      {
        var path = req.RequestUri?.AbsolutePath ?? "";
        if (path.Contains("/api/health", StringComparison.Ordinal))
          return HealthOk();

        if (path == "/api/transport-delete" && req.Method == HttpMethod.Delete)
        {
          return new HttpResponseMessage(HttpStatusCode.NoContent)
          {
            Content = new StringContent("")
          };
        }

        return new HttpResponseMessage(HttpStatusCode.NotFound);
      });

      using var client = CreateClient(handler);
      var result = await client.SendRequestAsync<object, RetryOkDto?>(
        "/api/transport-delete",
        null,
        HttpMethod.Delete,
        CancellationToken.None).ConfigureAwait(false);
      Assert.IsNull(result, "DELETE with 204 No Content and empty body should return default.");
    }

    [TestMethod]
    public async Task GetAsync_429_IsRetryable()
    {
      var testCalls = 0;
      var handler = new TransportTestHandler((req, seq) =>
      {
        var path = req.RequestUri?.AbsolutePath ?? "";
        if (path.Contains("/api/health", StringComparison.Ordinal))
          return HealthOk();

        if (path == "/api/transport-429")
        {
          Interlocked.Increment(ref testCalls);
          return testCalls < 2
            ? new HttpResponseMessage((HttpStatusCode)429)
            {
              Content = new StringContent("{\"message\":\"rate limit\"}")
            }
            : JsonOk("{\"status\":\"ok\"}");
        }

        return new HttpResponseMessage(HttpStatusCode.NotFound);
      });

      using var client = CreateClient(handler);
      var body = await client.GetAsync<RetryOkDto>("/api/transport-429", CancellationToken.None).ConfigureAwait(false);
      Assert.IsNotNull(body);
      Assert.AreEqual("ok", body!.Status);
      Assert.AreEqual(2, testCalls, "429 is retryable; should retry then succeed.");
    }

    [TestMethod]
    public async Task CheckHealthAsync_FailureThenRecovery_ConnectionStateCorrect()
    {
      var healthCalls = 0;
      var handler = new TransportTestHandler((req, seq) =>
      {
        var path = req.RequestUri?.AbsolutePath ?? "";
        if (path.Contains("/api/health", StringComparison.Ordinal))
        {
          var n = Interlocked.Increment(ref healthCalls);
          return n == 1
            ? new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
            : HealthOk();
        }

        return new HttpResponseMessage(HttpStatusCode.NotFound);
      });

      var (backendClient, healthClient, connectionStatusClient) = CreateClientWithSharedContext(handler);
      using (backendClient)
      {
        var first = await healthClient.CheckHealthAsync(CancellationToken.None).ConfigureAwait(false);
        Assert.IsFalse(first, "First health check should fail.");
        Assert.IsFalse(connectionStatusClient.IsConnected, "IsConnected should be false after failure.");

        var second = await healthClient.CheckHealthAsync(CancellationToken.None).ConfigureAwait(false);
        Assert.IsTrue(second, "Second health check should succeed.");
        Assert.IsTrue(connectionStatusClient.IsConnected, "IsConnected should be true after recovery.");
      }
    }

    private const string CanonicalErrorPayload =
      "{\"message\":\"Validation failed\",\"error_code\":\"E_VALIDATION\",\"request_id\":\"req-123\",\"path\":\"/api/test\",\"recovery_suggestion\":\"Check your input\"}";

    [TestMethod]
    public async Task GetAsync_StandardErrorResponse_BackendClient_MapsAllFields()
    {
      var handler = new TransportTestHandler((req, seq) =>
      {
        var path = req.RequestUri?.AbsolutePath ?? "";
        if (path.Contains("/api/health", StringComparison.Ordinal))
          return HealthOk();

        if (path == "/api/transport-parity")
        {
          return new HttpResponseMessage((HttpStatusCode)422)
          {
            Content = new StringContent(CanonicalErrorPayload)
          };
        }

        return new HttpResponseMessage(HttpStatusCode.NotFound);
      });

      using var client = CreateClient(handler);
      try
      {
        await client.GetAsync<RetryOkDto>("/api/transport-parity", CancellationToken.None).ConfigureAwait(false);
        Assert.Fail("Expected BackendValidationException");
      }
      catch (BackendValidationException ex)
      {
        Assert.AreEqual("Validation failed", ex.Message);
        Assert.AreEqual("E_VALIDATION", ex.ErrorCode);
        Assert.AreEqual("req-123", ex.RequestId);
        Assert.AreEqual("/api/test", ex.Path);
        Assert.AreEqual("Check your input", ex.RecoverySuggestion);
        Assert.IsFalse(ex.IsRetryable);
      }
    }

    [TestMethod]
    public async Task GetAsync_StandardErrorResponse_BackendTransport_MapsAlignedFields()
    {
      var handler = new TransportTestHandler((req, seq) =>
      {
        var path = req.RequestUri?.AbsolutePath ?? "";
        if (path.Contains("/api/health", StringComparison.Ordinal))
          return HealthOk();

        if (path == "/api/transport-parity")
        {
          return new HttpResponseMessage((HttpStatusCode)422)
          {
            Content = new StringContent(CanonicalErrorPayload)
          };
        }

        return new HttpResponseMessage(HttpStatusCode.NotFound);
      });

      using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:8000/") };
      using var transport = new BackendTransport(httpClient);
      var result = await transport.GetAsync<RetryOkDto>("/api/transport-parity", CancellationToken.None).ConfigureAwait(false);

      Assert.IsFalse(result.Success);
      Assert.IsNotNull(result.Error);
      Assert.AreEqual("E_VALIDATION", result.Error!.Code);
      Assert.AreEqual("Validation failed", result.Error.Message);
      Assert.AreEqual("req-123", result.Error.RequestId);
      Assert.AreEqual("/api/test", result.Error.Path);
      Assert.AreEqual("Check your input", result.Error.RecoverySuggestion);
      Assert.IsFalse(result.Error.IsRetryable);
    }

    [TestMethod]
    public async Task GetAsync_MalformedJsonErrorBody_BackendTransport_FallsBackSafely()
    {
      var handler = new TransportTestHandler((req, seq) =>
      {
        var path = req.RequestUri?.AbsolutePath ?? "";
        if (path.Contains("/api/health", StringComparison.Ordinal))
          return HealthOk();

        if (path == "/api/transport-bad-json")
        {
          return new HttpResponseMessage(HttpStatusCode.InternalServerError)
          {
            Content = new StringContent("not valid json at all")
          };
        }

        return new HttpResponseMessage(HttpStatusCode.NotFound);
      });

      using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:8000/") };
      using var transport = new BackendTransport(httpClient);
      var result = await transport.GetAsync<RetryOkDto>("/api/transport-bad-json", CancellationToken.None).ConfigureAwait(false);

      Assert.IsFalse(result.Success);
      Assert.IsNotNull(result.Error);
      Assert.IsTrue(result.Error!.Message.Contains("not valid json", StringComparison.OrdinalIgnoreCase),
        "BackendTransport malformed JSON should fall back to truncated content as message.");
    }

    private const string PartialErrorPayload = "{\"message\":\"Custom validation message\"}";

    [TestMethod]
    public async Task GetAsync_PartialErrorPayload_BackendClient_UsesMessage()
    {
      var handler = new TransportTestHandler((req, seq) =>
      {
        var path = req.RequestUri?.AbsolutePath ?? "";
        if (path.Contains("/api/health", StringComparison.Ordinal))
          return HealthOk();

        if (path == "/api/transport-partial")
        {
          return new HttpResponseMessage((HttpStatusCode)422)
          {
            Content = new StringContent(PartialErrorPayload)
          };
        }

        return new HttpResponseMessage(HttpStatusCode.NotFound);
      });

      using var client = CreateClient(handler);
      try
      {
        await client.GetAsync<RetryOkDto>("/api/transport-partial", CancellationToken.None).ConfigureAwait(false);
        Assert.Fail("Expected BackendValidationException");
      }
      catch (BackendValidationException ex)
      {
        Assert.AreEqual("Custom validation message", ex.Message);
        Assert.IsFalse(ex.IsRetryable);
      }
    }

    [TestMethod]
    public async Task GetAsync_PartialErrorPayload_BackendTransport_UsesMessage()
    {
      var handler = new TransportTestHandler((req, seq) =>
      {
        var path = req.RequestUri?.AbsolutePath ?? "";
        if (path.Contains("/api/health", StringComparison.Ordinal))
          return HealthOk();

        if (path == "/api/transport-partial")
        {
          return new HttpResponseMessage((HttpStatusCode)422)
          {
            Content = new StringContent(PartialErrorPayload)
          };
        }

        return new HttpResponseMessage(HttpStatusCode.NotFound);
      });

      using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:8000/") };
      using var transport = new BackendTransport(httpClient);
      var result = await transport.GetAsync<RetryOkDto>("/api/transport-partial", CancellationToken.None).ConfigureAwait(false);

      Assert.IsFalse(result.Success);
      Assert.IsNotNull(result.Error);
      Assert.AreEqual("Custom validation message", result.Error!.Message);
      Assert.AreEqual("VALIDATION_ERROR", result.Error.Code);
      Assert.IsFalse(result.Error.IsRetryable);
    }

    /// <summary>
    /// Verifies search API path resolves correctly (PR-4).
    /// SearchClient owns /api/search; uses query params q, limit, types.
    /// </summary>
    [TestMethod]
    public async Task SearchAsync_ResolvesCorrectPath()
    {
      string? capturedPath = null;
      string? capturedQuery = null;
      var handler = new TransportTestHandler((req, seq) =>
      {
        capturedPath = req.RequestUri?.AbsolutePath;
        capturedQuery = req.RequestUri?.Query;
        if (capturedPath != null && capturedPath.Contains("/api/health", StringComparison.Ordinal))
          return HealthOk();
        if (capturedPath != null && capturedPath.Contains("/api/search", StringComparison.Ordinal))
          return JsonOk("{\"results\":[],\"total\":0}");
        return new HttpResponseMessage(HttpStatusCode.NotFound);
      });

      var searchClient = CreateSearchClient(handler);
      var result = await searchClient.SearchAsync("test query", types: "profile", limit: 25, CancellationToken.None).ConfigureAwait(false);

      Assert.IsNotNull(result);
      Assert.IsNotNull(capturedPath);
      Assert.IsTrue(capturedPath.Contains("/api/search", StringComparison.Ordinal), $"Expected path to contain /api/search, got: {capturedPath}");
      Assert.IsTrue(capturedQuery != null && capturedQuery.Contains("q=", StringComparison.Ordinal), "Query should include q= param");
      Assert.IsTrue(capturedQuery != null && capturedQuery.Contains("limit=25", StringComparison.Ordinal), "Query should include limit=25");
      Assert.IsTrue(capturedQuery != null && capturedQuery.Contains("types=", StringComparison.Ordinal), "Query should include types= param");
    }

    /// <summary>
    /// Verifies HealthVersionClient.CheckHealthAsync resolves /api/health (PR-5 extraction).
    /// </summary>
    [TestMethod]
    public async Task CheckHealthAsync_ResolvesCorrectPath()
    {
      string? capturedPath = null;
      var handler = new TransportTestHandler((req, seq) =>
      {
        capturedPath = req.RequestUri?.AbsolutePath;
        if (capturedPath != null && capturedPath.Contains("/api/health", StringComparison.Ordinal))
          return HealthOk();
        return new HttpResponseMessage(HttpStatusCode.NotFound);
      });

      var (backendClient, healthClient, _) = CreateClientWithSharedContext(handler);
      using (backendClient)
      {
        var result = await healthClient.CheckHealthAsync(CancellationToken.None).ConfigureAwait(false);
        Assert.IsTrue(result);
        Assert.IsNotNull(capturedPath);
        Assert.IsTrue(capturedPath.Contains("/api/health", StringComparison.Ordinal),
          $"Expected path to contain /api/health, got: {capturedPath}");
      }
    }

    /// <summary>
    /// Verifies plugin API paths resolve correctly (Task 5, PR-3).
    /// PluginHealthClient uses leading-slash convention: /api/plugins/...
    /// </summary>
    [TestMethod]
    public async Task GetPluginHealthDashboardAsync_ResolvesCorrectPath()
    {
      string? capturedPath = null;
      var handler = new TransportTestHandler((req, seq) =>
      {
        capturedPath = req.RequestUri?.AbsolutePath;
        if (capturedPath != null && capturedPath.Contains("/api/health", StringComparison.Ordinal))
          return HealthOk();
        if (capturedPath != null && capturedPath.Contains("/api/plugins/", StringComparison.Ordinal))
          return JsonOk("{\"plugins\":[],\"overall_status\":\"ok\"}");
        return new HttpResponseMessage(HttpStatusCode.NotFound);
      });

      var pluginClient = CreatePluginHealthClient(handler);
      var result = await pluginClient.GetDashboardAsync(CancellationToken.None).ConfigureAwait(false);

      Assert.IsNotNull(result);
      Assert.IsNotNull(capturedPath);
      Assert.IsTrue(capturedPath.Contains("/api/plugins/health/dashboard", StringComparison.Ordinal),
        $"Expected path to contain /api/plugins/health/dashboard, got: {capturedPath}");
    }

    private static ISearchClient CreateSearchClient(HttpMessageHandler inner)
    {
      var httpClient = new HttpClient(inner)
      {
        BaseAddress = new Uri("http://localhost:8000"),
        Timeout = TimeSpan.FromSeconds(30)
      };
      var jsonOptions = VoiceStudio.App.Utilities.JsonSerializerOptionsFactory.BackendApi;
      var appAssembly = typeof(VoiceStudio.App.Services.BackendClient).Assembly;
      var pipelineType = appAssembly.GetType("VoiceStudio.App.Services.BackendClientHttpPipeline")
        ?? throw new InvalidOperationException("BackendClientHttpPipeline type not found");
      var pipeline = Activator.CreateInstance(pipelineType, httpClient, jsonOptions)
        ?? throw new InvalidOperationException("Failed to create BackendClientHttpPipeline");
      var searchType = appAssembly.GetType("VoiceStudio.App.Services.SearchClient")
        ?? throw new InvalidOperationException("SearchClient type not found");
      var searchClient = Activator.CreateInstance(searchType, pipeline)
        ?? throw new InvalidOperationException("Failed to create SearchClient");
      return (ISearchClient)searchClient;
    }

    /// <summary>
    /// Verifies script-editor API path resolves correctly (PR-7).
    /// ScriptEditorClient owns /api/script-editor; uses query params project_id, search.
    /// </summary>
    [TestMethod]
    public async Task GetScriptsAsync_ResolvesCorrectPath()
    {
      string? capturedPath = null;
      string? capturedQuery = null;
      var handler = new TransportTestHandler((req, seq) =>
      {
        capturedPath = req.RequestUri?.AbsolutePath;
        capturedQuery = req.RequestUri?.Query;
        if (capturedPath != null && capturedPath.Contains("/api/health", StringComparison.Ordinal))
          return HealthOk();
        if (capturedPath != null && capturedPath.Contains("/api/script-editor", StringComparison.Ordinal))
          return JsonOk("[]");
        return new HttpResponseMessage(HttpStatusCode.NotFound);
      });

      var scriptClient = CreateScriptEditorClient(handler);
      var result = await scriptClient.GetScriptsAsync(projectId: "proj-1", search: "test", CancellationToken.None).ConfigureAwait(false);

      Assert.IsNotNull(result);
      Assert.IsNotNull(capturedPath);
      Assert.IsTrue(capturedPath.Contains("/api/script-editor", StringComparison.Ordinal), $"Expected path to contain /api/script-editor, got: {capturedPath}");
      Assert.IsTrue(capturedQuery != null && capturedQuery.Contains("project_id=", StringComparison.Ordinal), "Query should include project_id= param");
      Assert.IsTrue(capturedQuery != null && capturedQuery.Contains("search=", StringComparison.Ordinal), "Query should include search= param");
    }

    /// <summary>
    /// Verifies MacroClient.GetMacrosAsync resolves /api/macros (PR-9 extraction).
    /// </summary>
    [TestMethod]
    public async Task GetMacrosAsync_ResolvesCorrectPath()
    {
      string? capturedPath = null;
      string? capturedQuery = null;
      var handler = new TransportTestHandler((req, seq) =>
      {
        capturedPath = req.RequestUri?.AbsolutePath;
        capturedQuery = req.RequestUri?.Query;
        if (capturedPath != null && capturedPath.Contains("/api/health", StringComparison.Ordinal))
          return HealthOk();
        if (capturedPath != null && capturedPath.Contains("/api/macros", StringComparison.Ordinal))
          return JsonOk("[]");
        return new HttpResponseMessage(HttpStatusCode.NotFound);
      });

      var macroClient = CreateMacroClient(handler);
      var result = await macroClient.GetMacrosAsync(projectId: "proj-1", CancellationToken.None).ConfigureAwait(false);

      Assert.IsNotNull(result);
      Assert.IsNotNull(capturedPath);
      Assert.IsTrue(capturedPath.Contains("/api/macros", StringComparison.Ordinal), $"Expected path to contain /api/macros, got: {capturedPath}");
      Assert.IsTrue(capturedQuery != null && capturedQuery.Contains("project_id=", StringComparison.Ordinal), "Query should include project_id= param");
    }

    /// <summary>
    /// Verifies EffectChainClient.GetEffectChainsAsync resolves /api/effects/chains/{projectId} (PR-11 extraction).
    /// </summary>
    [TestMethod]
    public async Task GetEffectChainsAsync_ResolvesCorrectPath()
    {
      string? capturedPath = null;
      var handler = new TransportTestHandler((req, seq) =>
      {
        capturedPath = req.RequestUri?.AbsolutePath;
        if (capturedPath != null && capturedPath.Contains("/api/health", StringComparison.Ordinal))
          return HealthOk();
        if (capturedPath != null && capturedPath.Contains("/api/effects/chains", StringComparison.Ordinal))
          return JsonOk("[]");
        return new HttpResponseMessage(HttpStatusCode.NotFound);
      });

      var effectChainClient = CreateEffectChainClient(handler);
      var result = await effectChainClient.GetEffectChainsAsync(projectId: "proj-effect-1", CancellationToken.None).ConfigureAwait(false);

      Assert.IsNotNull(result);
      Assert.IsNotNull(capturedPath);
      Assert.IsTrue(capturedPath.Contains("/api/effects/chains", StringComparison.Ordinal), $"Expected path to contain /api/effects/chains, got: {capturedPath}");
      Assert.IsTrue(capturedPath.Contains("proj-effect-1", StringComparison.Ordinal), $"Expected path to contain projectId, got: {capturedPath}");
    }

    /// <summary>
    /// GAP-039: ProcessAudioWithChainAsync appends bypass_chain and preview query flags when set.
    /// </summary>
    [TestMethod]
    public async Task ProcessAudioWithChainAsync_IncludesBypassAndPreviewQuery()
    {
      string? capturedUri = null;
      var handler = new TransportTestHandler((req, seq) =>
      {
        capturedUri = req.RequestUri?.ToString();
        if (capturedUri != null && capturedUri.Contains("/api/health", StringComparison.Ordinal))
          return HealthOk();
        if (capturedUri != null && capturedUri.Contains("/api/effects/chains/", StringComparison.Ordinal) && capturedUri.Contains("/process", StringComparison.Ordinal))
          return JsonOk("{\"success\":true,\"output_audio_id\":\"out1\",\"message\":\"ok\"}");
        return new HttpResponseMessage(HttpStatusCode.NotFound);
      });

      var effectChainClient = CreateEffectChainClient(handler);
      var result = await effectChainClient.ProcessAudioWithChainAsync(
          projectId: "p1",
          chainId: "c1",
          audioId: "a1",
          outputFilename: null,
          bypassChain: true,
          preview: true,
          CancellationToken.None).ConfigureAwait(false);

      Assert.IsNotNull(result);
      Assert.IsTrue(result.Success);
      Assert.IsNotNull(capturedUri);
      Assert.IsTrue(capturedUri.Contains("bypass_chain=true", StringComparison.Ordinal), capturedUri);
      Assert.IsTrue(capturedUri.Contains("preview=true", StringComparison.Ordinal), capturedUri);
    }

    /// <summary>
    /// Verifies BackupRestoreClient.GetBackupsAsync resolves /api/backup (PR-14 extraction).
    /// </summary>
    [TestMethod]
    public async Task GetBackupsAsync_ResolvesCorrectPath()
    {
      string? capturedPath = null;
      var handler = new TransportTestHandler((req, seq) =>
      {
        capturedPath = req.RequestUri?.AbsolutePath;
        if (capturedPath != null && capturedPath.Contains("/api/health", StringComparison.Ordinal))
          return HealthOk();
        if (capturedPath != null && capturedPath.Contains("/api/backup", StringComparison.Ordinal) && !capturedPath.Contains("/download", StringComparison.Ordinal))
          return JsonOk("[]");
        return new HttpResponseMessage(HttpStatusCode.NotFound);
      });

      var backupClient = CreateBackupRestoreClient(handler);
      var result = await backupClient.GetBackupsAsync(CancellationToken.None).ConfigureAwait(false);

      Assert.IsNotNull(result);
      Assert.IsNotNull(capturedPath);
      Assert.IsTrue(capturedPath.Contains("/api/backup", StringComparison.Ordinal), $"Expected path to contain /api/backup, got: {capturedPath}");
    }

    /// <summary>
    /// Verifies ModelManagerClient.GetModelsAsync resolves /api/models (PR-15 extraction).
    /// </summary>
    [TestMethod]
    public async Task GetModelsAsync_ResolvesCorrectPath()
    {
      string? capturedPath = null;
      var handler = new TransportTestHandler((req, seq) =>
      {
        capturedPath = req.RequestUri?.AbsolutePath;
        if (capturedPath != null && capturedPath.Contains("/api/health", StringComparison.Ordinal))
          return HealthOk();
        if (capturedPath != null && capturedPath.Contains("/api/models", StringComparison.Ordinal) && !capturedPath.Contains("/export", StringComparison.Ordinal) && !capturedPath.Contains("/import", StringComparison.Ordinal) && !capturedPath.Contains("/stats", StringComparison.Ordinal) && !capturedPath.Contains("/download", StringComparison.Ordinal))
          return JsonOk("[]");
        return new HttpResponseMessage(HttpStatusCode.NotFound);
      });

      var modelClient = CreateModelManagerClient(handler);
      var result = await modelClient.GetModelsAsync(null, CancellationToken.None).ConfigureAwait(false);

      Assert.IsNotNull(result);
      Assert.IsNotNull(capturedPath);
      Assert.IsTrue(capturedPath.Contains("/api/models", StringComparison.Ordinal), $"Expected path to contain /api/models, got: {capturedPath}");
    }

    /// <summary>
    /// Verifies ModelManagerClient.GetModelAsync resolves GET /api/models/{engine}/{modelName} (PR-15 extraction).
    /// </summary>
    [TestMethod]
    public async Task GetModelAsync_ResolvesCorrectPath()
    {
      string? capturedPath = null;
      var handler = new TransportTestHandler((req, seq) =>
      {
        capturedPath = req.RequestUri?.AbsolutePath;
        if (capturedPath != null && capturedPath.Contains("/api/health", StringComparison.Ordinal))
          return HealthOk();
        if (capturedPath != null && capturedPath.Contains("/api/models/xtts/my-model", StringComparison.Ordinal) && !capturedPath.Contains("/verify", StringComparison.Ordinal) && !capturedPath.Contains("/export", StringComparison.Ordinal))
          return JsonOk("{\"engine\":\"xtts\",\"model_name\":\"my-model\",\"version\":\"1.0\",\"model_path\":\"/models/xtts/my-model.pt\"}");
        return new HttpResponseMessage(HttpStatusCode.NotFound);
      });

      var modelClient = CreateModelManagerClient(handler);
      var result = await modelClient.GetModelAsync("xtts", "my-model", CancellationToken.None).ConfigureAwait(false);

      Assert.IsNotNull(result);
      Assert.IsNotNull(capturedPath);
      Assert.IsTrue(capturedPath.Contains("/api/models", StringComparison.Ordinal), $"Expected path to contain /api/models, got: {capturedPath}");
      Assert.IsTrue(capturedPath.Contains("xtts", StringComparison.Ordinal), $"Expected path to contain engine, got: {capturedPath}");
      Assert.IsTrue(capturedPath.Contains("my-model", StringComparison.Ordinal), $"Expected path to contain modelName, got: {capturedPath}");
    }

    /// <summary>
    /// Verifies ModelManagerClient.StartModelDownloadAsync posts to /api/models/download (GAP-043).
    /// </summary>
    [TestMethod]
    public async Task StartModelDownloadAsync_ResolvesCorrectPath()
    {
      string? capturedPath = null;
      HttpMethod? capturedMethod = null;
      var handler = new TransportTestHandler((req, seq) =>
      {
        capturedPath = req.RequestUri?.AbsolutePath;
        capturedMethod = req.Method;
        if (capturedPath != null && capturedPath.Contains("/api/health", StringComparison.Ordinal))
          return HealthOk();
        if (capturedPath != null && capturedPath.Contains("/api/models/download", StringComparison.Ordinal) && req.Method == HttpMethod.Post)
          return JsonOk("{\"job_id\":\"job-dl-1\"}");
        return new HttpResponseMessage(HttpStatusCode.NotFound);
      });

      var modelClient = CreateModelManagerClient(handler);
      var req = new VoiceStudio.Core.Models.ModelDownloadStartRequest
      {
        Url = "https://example.com/m.zip",
        Engine = "xtts_v2",
        ModelName = "m1",
        Version = "1.0",
        ExpectedSha256 = null,
      };
      var result = await modelClient.StartModelDownloadAsync(req, CancellationToken.None).ConfigureAwait(false);

      Assert.IsNotNull(result);
      Assert.AreEqual("job-dl-1", result.JobId);
      Assert.IsNotNull(capturedPath);
      Assert.AreEqual(HttpMethod.Post, capturedMethod);
      Assert.IsTrue(capturedPath.Contains("/api/models/download", StringComparison.Ordinal), $"Expected /api/models/download, got: {capturedPath}");
    }

    /// <summary>
    /// Verifies EffectChainClient.CreateEffectPresetAsync resolves POST /api/effects/presets (PR-12 extraction).
    /// </summary>
    [TestMethod]
    public async Task CreateEffectPresetAsync_ResolvesCorrectPath()
    {
      string? capturedPath = null;
      var handler = new TransportTestHandler((req, seq) =>
      {
        capturedPath = req.RequestUri?.AbsolutePath;
        if (capturedPath != null && capturedPath.Contains("/api/health", StringComparison.Ordinal))
          return HealthOk();
        if (capturedPath != null && capturedPath.Contains("/api/effects/presets", StringComparison.Ordinal) && req.Method == HttpMethod.Post)
          return JsonOk("{\"id\":\"preset-1\",\"name\":\"Test\",\"effect_type\":\"eq\"}");
        return new HttpResponseMessage(HttpStatusCode.NotFound);
      });

      var effectChainClient = CreateEffectChainClient(handler);
      var preset = new VoiceStudio.Core.Models.EffectPreset { Name = "Test", EffectType = "eq" };
      var result = await effectChainClient.CreateEffectPresetAsync(preset, CancellationToken.None).ConfigureAwait(false);

      Assert.IsNotNull(result);
      Assert.IsNotNull(capturedPath);
      Assert.IsTrue(capturedPath.Contains("/api/effects/presets", StringComparison.Ordinal), $"Expected path to contain /api/effects/presets, got: {capturedPath}");
    }

    /// <summary>
    /// Verifies EffectChainClient.DeleteEffectPresetAsync resolves DELETE /api/effects/presets/{id} (PR-12 extraction).
    /// </summary>
    [TestMethod]
    public async Task DeleteEffectPresetAsync_ResolvesCorrectPath()
    {
      string? capturedPath = null;
      var handler = new TransportTestHandler((req, seq) =>
      {
        capturedPath = req.RequestUri?.AbsolutePath;
        if (capturedPath != null && capturedPath.Contains("/api/health", StringComparison.Ordinal))
          return HealthOk();
        if (capturedPath != null && capturedPath.Contains("/api/effects/presets", StringComparison.Ordinal) && req.Method == HttpMethod.Delete)
          return new HttpResponseMessage(HttpStatusCode.NoContent);
        return new HttpResponseMessage(HttpStatusCode.NotFound);
      });

      var effectChainClient = CreateEffectChainClient(handler);
      var result = await effectChainClient.DeleteEffectPresetAsync("preset-delete-123", CancellationToken.None).ConfigureAwait(false);

      Assert.IsTrue(result);
      Assert.IsNotNull(capturedPath);
      Assert.IsTrue(capturedPath.Contains("/api/effects/presets", StringComparison.Ordinal), $"Expected path to contain /api/effects/presets, got: {capturedPath}");
      Assert.IsTrue(capturedPath.Contains("preset-delete-123", StringComparison.Ordinal), $"Expected path to contain presetId, got: {capturedPath}");
    }

    /// <summary>
    /// Verifies PipelineConversationClient.GetPipelineProvidersAsync resolves GET /api/pipeline/providers (PR-13 extraction).
    /// </summary>
    [TestMethod]
    public async Task GetPipelineProvidersAsync_ResolvesCorrectPath()
    {
      string? capturedPath = null;
      var handler = new TransportTestHandler((req, seq) =>
      {
        capturedPath = req.RequestUri?.AbsolutePath;
        if (capturedPath != null && capturedPath.Contains("/api/health", StringComparison.Ordinal))
          return HealthOk();
        if (capturedPath != null && capturedPath.Contains("/api/pipeline/providers", StringComparison.Ordinal))
          return JsonOk("{\"llm_providers\":[],\"tts_providers\":[]}");
        return new HttpResponseMessage(HttpStatusCode.NotFound);
      });

      var pipelineClient = CreatePipelineConversationClient(handler);
      var result = await pipelineClient.GetPipelineProvidersAsync(CancellationToken.None).ConfigureAwait(false);

      Assert.IsNotNull(result);
      Assert.IsNotNull(capturedPath);
      Assert.IsTrue(capturedPath.Contains("/api/pipeline/providers", StringComparison.Ordinal), $"Expected path to contain /api/pipeline/providers, got: {capturedPath}");
    }

    /// <summary>
    /// Verifies PipelineConversationClient.ProcessPipelineAsync resolves POST /api/pipeline/process (PR-13 extraction).
    /// </summary>
    [TestMethod]
    public async Task ProcessPipelineAsync_ResolvesCorrectPath()
    {
      string? capturedPath = null;
      var handler = new TransportTestHandler((req, seq) =>
      {
        capturedPath = req.RequestUri?.AbsolutePath;
        if (capturedPath != null && capturedPath.Contains("/api/health", StringComparison.Ordinal))
          return HealthOk();
        if (capturedPath != null && capturedPath.Contains("/api/pipeline/process", StringComparison.Ordinal) && req.Method == HttpMethod.Post)
          return JsonOk("{\"response\":\"test\",\"audio\":null}");
        return new HttpResponseMessage(HttpStatusCode.NotFound);
      });

      var pipelineClient = CreatePipelineConversationClient(handler);
      var request = new VoiceStudio.App.Core.Models.PipelineRequest { Text = "Hello", Mode = "batch" };
      var result = await pipelineClient.ProcessPipelineAsync(request, CancellationToken.None).ConfigureAwait(false);

      Assert.IsNotNull(result);
      Assert.IsNotNull(capturedPath);
      Assert.IsTrue(capturedPath.Contains("/api/pipeline/process", StringComparison.Ordinal), $"Expected path to contain /api/pipeline/process, got: {capturedPath}");
    }

    /// <summary>
    /// Verifies WorkflowAutomationClient.GetWorkflowsAsync resolves /api/workflows (PR-10 extraction).
    /// </summary>
    [TestMethod]
    public async Task GetWorkflowsAsync_ResolvesCorrectPath()
    {
      string? capturedPath = null;
      string? capturedQuery = null;
      var handler = new TransportTestHandler((req, seq) =>
      {
        capturedPath = req.RequestUri?.AbsolutePath;
        capturedQuery = req.RequestUri?.Query;
        if (capturedPath != null && capturedPath.Contains("/api/health", StringComparison.Ordinal))
          return HealthOk();
        if (capturedPath != null && capturedPath.Contains("/api/workflows", StringComparison.Ordinal))
          return JsonOk("[]");
        return new HttpResponseMessage(HttpStatusCode.NotFound);
      });

      var workflowClient = CreateWorkflowClient(handler);
      var result = await workflowClient.GetWorkflowsAsync(skip: 5, limit: 20, enabledOnly: false, CancellationToken.None).ConfigureAwait(false);

      Assert.IsNotNull(result);
      Assert.IsNotNull(capturedPath);
      Assert.IsTrue(capturedPath.Contains("/api/workflows", StringComparison.Ordinal), $"Expected path to contain /api/workflows, got: {capturedPath}");
      Assert.IsTrue(capturedQuery != null && capturedQuery.Contains("skip=", StringComparison.Ordinal), "Query should include skip= param");
      Assert.IsTrue(capturedQuery != null && capturedQuery.Contains("limit=", StringComparison.Ordinal), "Query should include limit= param");
    }

    /// <summary>
    /// Uses reflection to create MacroClient (internal ctor).
    /// </summary>
    private static IMacroClient CreateMacroClient(HttpMessageHandler inner)
    {
      var httpClient = new HttpClient(inner)
      {
        BaseAddress = new Uri("http://localhost:8000"),
        Timeout = TimeSpan.FromSeconds(30)
      };
      var jsonOptions = VoiceStudio.App.Utilities.JsonSerializerOptionsFactory.BackendApi;
      var appAssembly = typeof(VoiceStudio.App.Services.BackendClient).Assembly;
      var pipelineType = appAssembly.GetType("VoiceStudio.App.Services.BackendClientHttpPipeline")
        ?? throw new InvalidOperationException("BackendClientHttpPipeline type not found");
      var pipeline = Activator.CreateInstance(pipelineType, httpClient, jsonOptions)
        ?? throw new InvalidOperationException("Failed to create BackendClientHttpPipeline");
      var macroType = appAssembly.GetType("VoiceStudio.App.Services.MacroClient")
        ?? throw new InvalidOperationException("MacroClient type not found");
      var macroClient = Activator.CreateInstance(macroType, BindingFlags.NonPublic | BindingFlags.Instance, null, new object[] { pipeline }, null)
        ?? throw new InvalidOperationException("Failed to create MacroClient");
      return (IMacroClient)macroClient;
    }

    /// <summary>
    /// Uses reflection to create PipelineConversationClient (internal ctor). PR-13.
    /// </summary>
    private static IPipelineConversationClient CreatePipelineConversationClient(HttpMessageHandler inner)
    {
      var httpClient = new HttpClient(inner)
      {
        BaseAddress = new Uri("http://localhost:8000"),
        Timeout = TimeSpan.FromSeconds(30)
      };
      var jsonOptions = VoiceStudio.App.Utilities.JsonSerializerOptionsFactory.BackendApi;
      var appAssembly = typeof(VoiceStudio.App.Services.BackendClient).Assembly;
      var pipelineType = appAssembly.GetType("VoiceStudio.App.Services.BackendClientHttpPipeline")
        ?? throw new InvalidOperationException("BackendClientHttpPipeline type not found");
      var pipeline = Activator.CreateInstance(pipelineType, httpClient, jsonOptions)
        ?? throw new InvalidOperationException("Failed to create BackendClientHttpPipeline");
      var pipelineClientType = appAssembly.GetType("VoiceStudio.App.Services.PipelineConversationClient")
        ?? throw new InvalidOperationException("PipelineConversationClient type not found");
      var pipelineClient = Activator.CreateInstance(pipelineClientType, BindingFlags.NonPublic | BindingFlags.Instance, null, new object[] { pipeline, null! }, null)
        ?? throw new InvalidOperationException("Failed to create PipelineConversationClient");
      return (IPipelineConversationClient)pipelineClient;
    }

    /// <summary>
    /// Uses reflection to create WorkflowAutomationClient (internal ctor).
    /// </summary>
    private static IWorkflowAutomationClient CreateWorkflowClient(HttpMessageHandler inner)
    {
      var httpClient = new HttpClient(inner)
      {
        BaseAddress = new Uri("http://localhost:8000"),
        Timeout = TimeSpan.FromSeconds(30)
      };
      var jsonOptions = VoiceStudio.App.Utilities.JsonSerializerOptionsFactory.BackendApi;
      var appAssembly = typeof(VoiceStudio.App.Services.BackendClient).Assembly;
      var pipelineType = appAssembly.GetType("VoiceStudio.App.Services.BackendClientHttpPipeline")
        ?? throw new InvalidOperationException("BackendClientHttpPipeline type not found");
      var pipeline = Activator.CreateInstance(pipelineType, httpClient, jsonOptions)
        ?? throw new InvalidOperationException("Failed to create BackendClientHttpPipeline");
      var workflowType = appAssembly.GetType("VoiceStudio.App.Services.WorkflowAutomationClient")
        ?? throw new InvalidOperationException("WorkflowAutomationClient type not found");
      var workflowClient = Activator.CreateInstance(workflowType, BindingFlags.NonPublic | BindingFlags.Instance, null, new object[] { pipeline }, null)
        ?? throw new InvalidOperationException("Failed to create WorkflowAutomationClient");
      return (IWorkflowAutomationClient)workflowClient;
    }

    /// <summary>
    /// Uses reflection to create EffectChainClient (internal ctor). PR-11.
    /// </summary>
    private static IEffectChainClient CreateEffectChainClient(HttpMessageHandler inner)
    {
      var httpClient = new HttpClient(inner)
      {
        BaseAddress = new Uri("http://localhost:8000"),
        Timeout = TimeSpan.FromSeconds(30)
      };
      var jsonOptions = VoiceStudio.App.Utilities.JsonSerializerOptionsFactory.BackendApi;
      var appAssembly = typeof(VoiceStudio.App.Services.BackendClient).Assembly;
      var pipelineType = appAssembly.GetType("VoiceStudio.App.Services.BackendClientHttpPipeline")
        ?? throw new InvalidOperationException("BackendClientHttpPipeline type not found");
      var pipeline = Activator.CreateInstance(pipelineType, httpClient, jsonOptions)
        ?? throw new InvalidOperationException("Failed to create BackendClientHttpPipeline");
      var effectChainType = appAssembly.GetType("VoiceStudio.App.Services.EffectChainClient")
        ?? throw new InvalidOperationException("EffectChainClient type not found");
      var effectChainClient = Activator.CreateInstance(effectChainType, BindingFlags.NonPublic | BindingFlags.Instance, null, new object[] { pipeline }, null)
        ?? throw new InvalidOperationException("Failed to create EffectChainClient");
      return (IEffectChainClient)effectChainClient;
    }

    /// <summary>
    /// Uses reflection to create BackupRestoreClient (internal ctor). PR-14.
    /// </summary>
    private static IBackupRestoreClient CreateBackupRestoreClient(HttpMessageHandler inner)
    {
      var httpClient = new HttpClient(inner)
      {
        BaseAddress = new Uri("http://localhost:8000"),
        Timeout = TimeSpan.FromSeconds(30)
      };
      var jsonOptions = VoiceStudio.App.Utilities.JsonSerializerOptionsFactory.BackendApi;
      var appAssembly = typeof(VoiceStudio.App.Services.BackendClient).Assembly;
      var pipelineType = appAssembly.GetType("VoiceStudio.App.Services.BackendClientHttpPipeline")
        ?? throw new InvalidOperationException("BackendClientHttpPipeline type not found");
      var pipeline = Activator.CreateInstance(pipelineType, httpClient, jsonOptions)
        ?? throw new InvalidOperationException("Failed to create BackendClientHttpPipeline");
      var backupType = appAssembly.GetType("VoiceStudio.App.Services.BackupRestoreClient")
        ?? throw new InvalidOperationException("BackupRestoreClient type not found");
      var backupClient = Activator.CreateInstance(backupType, BindingFlags.NonPublic | BindingFlags.Instance, null, new object[] { pipeline }, null)
        ?? throw new InvalidOperationException("Failed to create BackupRestoreClient");
      return (IBackupRestoreClient)backupClient;
    }

    /// <summary>
    /// Uses reflection to create ModelManagerClient (internal ctor). PR-15.
    /// </summary>
    private static IModelManagerClient CreateModelManagerClient(HttpMessageHandler inner)
    {
      var httpClient = new HttpClient(inner)
      {
        BaseAddress = new Uri("http://localhost:8000"),
        Timeout = TimeSpan.FromSeconds(30)
      };
      var jsonOptions = VoiceStudio.App.Utilities.JsonSerializerOptionsFactory.BackendApi;
      var appAssembly = typeof(VoiceStudio.App.Services.BackendClient).Assembly;
      var pipelineType = appAssembly.GetType("VoiceStudio.App.Services.BackendClientHttpPipeline")
        ?? throw new InvalidOperationException("BackendClientHttpPipeline type not found");
      var pipeline = Activator.CreateInstance(pipelineType, httpClient, jsonOptions)
        ?? throw new InvalidOperationException("Failed to create BackendClientHttpPipeline");
      var modelType = appAssembly.GetType("VoiceStudio.App.Services.ModelManagerClient")
        ?? throw new InvalidOperationException("ModelManagerClient type not found");
      var modelClient = Activator.CreateInstance(modelType, BindingFlags.NonPublic | BindingFlags.Instance, null, new object[] { pipeline }, null)
        ?? throw new InvalidOperationException("Failed to create ModelManagerClient");
      return (IModelManagerClient)modelClient;
    }

    /// <summary>
    /// Uses reflection to create ScriptEditorClient (internal ctor).
    /// </summary>
    private static IScriptEditorClient CreateScriptEditorClient(HttpMessageHandler inner)
    {
      var httpClient = new HttpClient(inner)
      {
        BaseAddress = new Uri("http://localhost:8000"),
        Timeout = TimeSpan.FromSeconds(30)
      };
      var jsonOptions = VoiceStudio.App.Utilities.JsonSerializerOptionsFactory.BackendApi;
      var appAssembly = typeof(VoiceStudio.App.Services.BackendClient).Assembly;
      var pipelineType = appAssembly.GetType("VoiceStudio.App.Services.BackendClientHttpPipeline")
        ?? throw new InvalidOperationException("BackendClientHttpPipeline type not found");
      var pipeline = Activator.CreateInstance(pipelineType, httpClient, jsonOptions)
        ?? throw new InvalidOperationException("Failed to create BackendClientHttpPipeline");
      var scriptType = appAssembly.GetType("VoiceStudio.App.Services.ScriptEditorClient")
        ?? throw new InvalidOperationException("ScriptEditorClient type not found");
      var scriptClient = Activator.CreateInstance(scriptType, BindingFlags.NonPublic | BindingFlags.Instance, null, new object[] { pipeline }, null)
        ?? throw new InvalidOperationException("Failed to create ScriptEditorClient");
      return (IScriptEditorClient)scriptClient;
    }

    /// <summary>
    /// Uses reflection to create PluginHealthClient (internal type; InternalsVisibleTo fails at compile in this environment).
    /// </summary>
    private static IPluginHealthClient CreatePluginHealthClient(HttpMessageHandler inner)
    {
      var httpClient = new HttpClient(inner)
      {
        BaseAddress = new Uri("http://localhost:8000"),
        Timeout = TimeSpan.FromSeconds(30)
      };
      var jsonOptions = VoiceStudio.App.Utilities.JsonSerializerOptionsFactory.BackendApi;
      var appAssembly = typeof(VoiceStudio.App.Services.BackendClient).Assembly;
      var pipelineType = appAssembly.GetType("VoiceStudio.App.Services.BackendClientHttpPipeline")
        ?? throw new InvalidOperationException("BackendClientHttpPipeline type not found");
      var pipeline = Activator.CreateInstance(pipelineType, httpClient, jsonOptions)
        ?? throw new InvalidOperationException("Failed to create BackendClientHttpPipeline");
      var pluginType = appAssembly.GetType("VoiceStudio.App.Services.PluginHealthClient")
        ?? throw new InvalidOperationException("PluginHealthClient type not found");
      var pluginClient = Activator.CreateInstance(pluginType, pipeline)
        ?? throw new InvalidOperationException("Failed to create PluginHealthClient");
      return (IPluginHealthClient)pluginClient;
    }

    private sealed class RetryOkDto
    {
      public string Status { get; set; } = "";
    }
  }
}
