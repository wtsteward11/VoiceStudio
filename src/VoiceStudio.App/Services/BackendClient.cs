using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using VoiceStudio.Core.Models;
using VoiceStudio.Core.Services;
using VoiceStudio.Core.Exceptions;
using VoiceStudio.App.Utilities;
using VoiceStudio.App.Logging;
using VoiceStudio.App.Core.Models;
// Generated client types available for migration - see docs/developer/API_MIGRATION_GUIDE.md
using Generated = VoiceStudio.App.Services.Generated;

// Type aliases to resolve ambiguity with local types in VoiceStudio.App.Services namespace
using Macro = VoiceStudio.Core.Models.Macro;
using BatchJob = VoiceStudio.Core.Models.BatchJob;

namespace VoiceStudio.App.Services
{
  /// <summary>
  /// HTTP handler that adds X-Correlation-Id and trace headers to all requests.
  /// Implements Phase 5.1.2 trace propagation for distributed tracing.
  /// GAP-I12: Enhanced to extract correlation IDs from responses and set in provider.
  /// </summary>
  internal sealed class CorrelationIdHandler : DelegatingHandler
  {
    private const string CorrelationIdHeader = "X-Correlation-Id";
    private const string TraceIdHeader = "X-Trace-Id";
    private const string SpanIdHeader = "X-Span-Id";
    private const string TraceParentHeader = "traceparent";

    // GAP-I12: Optional correlation provider for setting context from response headers
    private readonly ICorrelationIdProvider? _correlationProvider;

    public CorrelationIdHandler() : base(new HttpClientHandler())
    {
    }

    public CorrelationIdHandler(HttpMessageHandler innerHandler) : base(innerHandler)
    {
    }

    /// <summary>
    /// GAP-I12: Constructor with correlation provider for response header extraction.
    /// </summary>
    public CorrelationIdHandler(ICorrelationIdProvider correlationProvider) : base(new HttpClientHandler())
    {
      _correlationProvider = correlationProvider;
    }

    /// <summary>
    /// GAP-I12: Constructor with both inner handler and correlation provider.
    /// </summary>
    public CorrelationIdHandler(HttpMessageHandler innerHandler, ICorrelationIdProvider correlationProvider) : base(innerHandler)
    {
      _correlationProvider = correlationProvider;
    }

    protected override async Task<HttpResponseMessage> SendAsync(
      HttpRequestMessage request,
      CancellationToken cancellationToken)
    {
      string? correlationId = null;

      // Generate a new correlation ID for this request if not already present
      if (!request.Headers.Contains(CorrelationIdHeader))
      {
        // GAP-I12: Check provider first, then generate new ID
        correlationId = _correlationProvider?.GetCurrentCorrelationId() ?? Guid.NewGuid().ToString("N");
        request.Headers.Add(CorrelationIdHeader, correlationId);

        // GAP-I12: Set in provider if we generated a new one
        if (_correlationProvider != null && _correlationProvider.GetCurrentCorrelationId() == null)
        {
          _correlationProvider.SetCorrelationId(correlationId);
        }
      }
      else
      {
        // Extract correlation ID from existing header
        correlationId = request.Headers.GetValues(CorrelationIdHeader).FirstOrDefault();
      }

      // Add W3C Trace Context header for distributed tracing compatibility
      // Format: version-trace_id-span_id-trace_flags
      if (!request.Headers.Contains(TraceParentHeader))
      {
        var traceId = Guid.NewGuid().ToString("N");
        var spanId = Guid.NewGuid().ToString("N").Substring(0, 16);
        var traceParent = $"00-{traceId}-{spanId}-01";
        request.Headers.Add(TraceParentHeader, traceParent);
        request.Headers.Add(TraceIdHeader, traceId);
        request.Headers.Add(SpanIdHeader, spanId);
      }

      // Use Activity if available for richer tracing context
      var activity = Activity.Current;
      if (activity != null)
      {
        // Override with actual activity trace context
        request.Headers.Remove(TraceParentHeader);
        request.Headers.Remove(TraceIdHeader);
        request.Headers.Remove(SpanIdHeader);

        var activityTraceId = activity.TraceId.ToString();
        var activitySpanId = activity.SpanId.ToString();
        request.Headers.Add(TraceIdHeader, activityTraceId);
        request.Headers.Add(SpanIdHeader, activitySpanId);

        // W3C Trace Context format
        var actTraceParent = $"00-{activityTraceId}-{activitySpanId}-01";
        request.Headers.Add(TraceParentHeader, actTraceParent);
      }

      var response = await base.SendAsync(request, cancellationToken);

      // GAP-I12: Extract correlation context from response headers and set in provider
      if (_correlationProvider != null)
      {
        ExtractAndSetCorrelationContext(response);
      }

      return response;
    }

    /// <summary>
    /// GAP-I12: Extracts correlation, trace, and span IDs from response headers
    /// and sets them in the correlation provider.
    /// </summary>
    private void ExtractAndSetCorrelationContext(HttpResponseMessage response)
    {
      // Extract correlation ID from response (backend may override)
      if (response.Headers.TryGetValues(CorrelationIdHeader, out var correlationValues))
      {
        var responseCorrelationId = correlationValues.FirstOrDefault();
        if (!string.IsNullOrEmpty(responseCorrelationId))
        {
          _correlationProvider!.SetCorrelationId(responseCorrelationId);
        }
      }

      // Extract trace and span IDs
      string? traceId = null;
      string? spanId = null;

      if (response.Headers.TryGetValues(TraceIdHeader, out var traceValues))
      {
        traceId = traceValues.FirstOrDefault();
      }

      if (response.Headers.TryGetValues(SpanIdHeader, out var spanValues))
      {
        spanId = spanValues.FirstOrDefault();
      }

      if (!string.IsNullOrEmpty(traceId) || !string.IsNullOrEmpty(spanId))
      {
        _correlationProvider!.SetTraceContext(traceId, spanId);
      }
    }
  }

  /// <summary>
  /// Stream wrapper that reports read progress via a callback.
  /// Used for tracking file upload progress.
  /// </summary>
  internal class ProgressStream : Stream
  {
    private readonly Stream _baseStream;
    private readonly Action<long, long> _progressCallback;
    private long _bytesRead;

    public ProgressStream(Stream baseStream, Action<long, long> progressCallback)
    {
      _baseStream = baseStream;
      _progressCallback = progressCallback;
    }

    public override bool CanRead => _baseStream.CanRead;
    public override bool CanSeek => _baseStream.CanSeek;
    public override bool CanWrite => _baseStream.CanWrite;
    public override long Length => _baseStream.Length;
    public override long Position
    {
      get => _baseStream.Position;
      set => _baseStream.Position = value;
    }

    public override void Flush() => _baseStream.Flush();
    public override long Seek(long offset, SeekOrigin origin) => _baseStream.Seek(offset, origin);
    public override void SetLength(long value) => _baseStream.SetLength(value);
    public override void Write(byte[] buffer, int offset, int count) => _baseStream.Write(buffer, offset, count);

    public override int Read(byte[] buffer, int offset, int count)
    {
      var bytesRead = _baseStream.Read(buffer, offset, count);
      _bytesRead += bytesRead;
      _progressCallback(_bytesRead, Length);
      return bytesRead;
    }

    protected override void Dispose(bool disposing)
    {
      // Note: We don't dispose the base stream here as it may be used by the caller
      base.Dispose(disposing);
    }
  }

  public class BackendClient : IBackendClient, IDisposable
  {
    private readonly HttpClient _httpClient;
    private readonly BackendClientConfig _config;
    private readonly JsonSerializerOptions _jsonOptions;
    private readonly Utilities.CircuitBreaker _circuitBreaker;
    private const int MaxRetries = 3;
    private const int RetryDelayMs = 1000;

    // Connection status tracking
    private bool _isConnected = true;
    private DateTime _lastConnectionCheck = DateTime.MinValue;
    private const int ConnectionCheckIntervalSeconds = 5;

    public IWebSocketService? WebSocketService { get; }

    /// <summary>
    /// Initializes a new instance of BackendClient without correlation provider.
    /// </summary>
    public BackendClient(BackendClientConfig config) : this(config, null)
    {
    }

    /// <summary>
    /// GAP-I12: Initializes a new instance with optional correlation ID provider
    /// for cross-layer request tracing.
    /// </summary>
    /// <param name="config">Backend client configuration.</param>
    /// <param name="correlationProvider">Optional provider for correlation context.</param>
    public BackendClient(BackendClientConfig config, ICorrelationIdProvider? correlationProvider)
    {
      _config = config ?? throw new ArgumentNullException(nameof(config));

      // Use CorrelationIdHandler to add X-Correlation-Id headers to all requests
      // This enables distributed tracing per Phase 5.1.2
      // GAP-I12: Pass correlation provider for response header extraction
      var handler = correlationProvider != null
        ? new CorrelationIdHandler(correlationProvider)
        : new CorrelationIdHandler();
      _httpClient = new HttpClient(handler)
      {
        BaseAddress = new Uri(config.BaseUrl),
        Timeout = config.RequestTimeout
      };

      // Use centralized JSON options for consistent snake_case serialization
      _jsonOptions = JsonSerializerOptionsFactory.BackendApi;

      // Initialize circuit breaker (5 failures before opening, 30s timeout)
      _circuitBreaker = new Utilities.CircuitBreaker(failureThreshold: 5, timeout: TimeSpan.FromSeconds(30));

      // Initialize WebSocket service if URL is provided
      if (!string.IsNullOrEmpty(config.WebSocketUrl))
      {
        // Convert HTTP URL to WebSocket URL if needed
        var wsUrl = config.WebSocketUrl;
        if (wsUrl.StartsWith("http://"))
        {
          wsUrl = wsUrl.Replace("http://", "ws://");
        }
        else if (wsUrl.StartsWith("https://"))
        {
          wsUrl = wsUrl.Replace("https://", "wss://");
        }

        // Ensure it points to the realtime endpoint
        if (!wsUrl.EndsWith("/realtime") && !wsUrl.EndsWith("/realtime/"))
        {
          wsUrl = wsUrl.TrimEnd('/') + "/realtime";
        }

        WebSocketService = new WebSocketService(wsUrl);
      }
    }

    /// <summary>
    /// Gets the current connection status.
    /// </summary>
    public bool IsConnected => _isConnected;

    /// <summary>
    /// Gets the base address of the backend API.
    /// </summary>
    public System.Uri? BaseAddress => _httpClient?.BaseAddress;

    /// <summary>
    /// Gets the circuit breaker state.
    /// </summary>
    public Utilities.CircuitState CircuitState => _circuitBreaker.State;

    public async Task<TResponse> SendRequestAsync<TRequest, TResponse>(
        string endpoint,
        TRequest request,
        CancellationToken cancellationToken = default)
    {
      return await ExecuteWithRetryAsync(async () =>
      {
        var json = JsonSerializer.Serialize(request, _jsonOptions);
        var content = new StringContent(json, Encoding.UTF8, "application/json");
        var response = await _httpClient.PostAsync(endpoint, content, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
          throw await CreateExceptionFromResponseAsync(response);
        }

        var responseJson = await response.Content.ReadAsStringAsync(cancellationToken);
        try
        {
          return JsonSerializer.Deserialize<TResponse>(responseJson, _jsonOptions)
                    ?? throw new BackendDeserializationException("Failed to deserialize response from backend.");
        }
        catch (JsonException ex)
        {
          throw new BackendDeserializationException(
                    "The backend returned an invalid response format.",
                    ex);
        }
      });
    }

    /// <summary>
    /// Generic request helper method with HTTP method support.
    /// </summary>
    public async Task<TResponse?> SendRequestAsync<TRequest, TResponse>(
        string endpoint,
        TRequest? request,
        System.Net.Http.HttpMethod method,
        CancellationToken cancellationToken = default) where TResponse : class
    {
      return await ExecuteWithRetryAsync(async () =>
      {
        System.Net.Http.HttpResponseMessage response;

        if (method == System.Net.Http.HttpMethod.Get)
        {
          response = await _httpClient.GetAsync(endpoint, cancellationToken);
        }
        else if (method == System.Net.Http.HttpMethod.Post)
        {
          if (request != null)
          {
            response = await _httpClient.PostAsJsonAsync(endpoint, request, _jsonOptions, cancellationToken);
          }
          else
          {
            response = await _httpClient.PostAsync(endpoint, null, cancellationToken);
          }
        }
        else if (method == System.Net.Http.HttpMethod.Put)
        {
          if (request != null)
          {
            response = await _httpClient.PutAsJsonAsync(endpoint, request, _jsonOptions, cancellationToken);
          }
          else
          {
            response = await _httpClient.PutAsync(endpoint, null, cancellationToken);
          }
        }
        else if (method == System.Net.Http.HttpMethod.Delete)
        {
          response = await _httpClient.DeleteAsync(endpoint, cancellationToken);
        }
        else
        {
          throw new NotSupportedException($"HTTP method {method.Method} is not supported");
        }

        if (!response.IsSuccessStatusCode)
        {
          throw await CreateExceptionFromResponseAsync(response);
        }

        // For DELETE requests with no response body, return default
        if (method == System.Net.Http.HttpMethod.Delete && response.Content.Headers.ContentLength == 0)
        {
          return default;
        }

        var responseJson = await response.Content.ReadAsStringAsync(cancellationToken);
        if (string.IsNullOrEmpty(responseJson))
        {
          return default;
        }

        try
        {
          return JsonSerializer.Deserialize<TResponse>(responseJson, _jsonOptions);
        }
        catch (JsonException ex)
        {
          throw new BackendDeserializationException(
                    "The backend returned an invalid response format.",
                    ex);
        }
      });
    }

    public async Task<TResponse> SendMcpOperationAsync<TRequest, TResponse>(
        string operation,
        TRequest payload,
        CancellationToken cancellationToken = default)
    {
      // MCP bridge endpoint
      return await SendRequestAsync<TRequest, TResponse>($"/api/mcp/{operation}", payload, cancellationToken);
    }

    public async Task<bool> CheckHealthAsync(CancellationToken cancellationToken = default)
    {
      try
      {
        var response = await _httpClient.GetAsync("/api/health", cancellationToken);
        _isConnected = response.IsSuccessStatusCode;
        _lastConnectionCheck = DateTime.UtcNow;
        return _isConnected;
      }
      catch (Exception)
      {
        _isConnected = false;
        _lastConnectionCheck = DateTime.UtcNow;
        return false;
      }
    }

    /// <summary>
    /// Expected API version for this client.
    /// </summary>
    public const string ExpectedApiVersion = "v2";

    /// <summary>
    /// Minimum supported API version.
    /// </summary>
    public const string MinimumApiVersion = "v1";

    /// <summary>
    /// Checks API version compatibility with the backend.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Version compatibility result</returns>
    public async Task<ApiVersionCheckResult> CheckApiVersionAsync(CancellationToken cancellationToken = default)
    {
      try
      {
        var response = await _httpClient.GetAsync("/api/version/compatibility", cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
          // Version endpoint not available - assume compatible for backwards compatibility
          return new ApiVersionCheckResult
          {
            IsCompatible = true,
            ServerVersion = "unknown",
            ClientVersion = ExpectedApiVersion,
            Message = "Version endpoint not available. Assuming compatible."
          };
        }

        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        var serverVersion = root.TryGetProperty("server_version", out var sv) ? sv.GetString() ?? "unknown" : "unknown";
        var isCompatible = root.TryGetProperty("compatible", out var compat) && compat.GetBoolean();
        var supportedVersions = new List<string>();

        if (root.TryGetProperty("supported_versions", out var supported) && supported.ValueKind == JsonValueKind.Array)
        {
          foreach (var v in supported.EnumerateArray())
          {
            if (v.ValueKind == JsonValueKind.String)
            {
              supportedVersions.Add(v.GetString() ?? "");
            }
          }
        }

        string? recommendation = null;
        if (root.TryGetProperty("recommendation", out var rec) && rec.ValueKind == JsonValueKind.String)
        {
          recommendation = rec.GetString();
        }

        // Check if our expected version is in supported versions
        var clientVersionSupported = supportedVersions.Contains(ExpectedApiVersion) ||
                                     supportedVersions.Contains(MinimumApiVersion);

        var message = isCompatible ?
          $"API version compatible. Server: {serverVersion}, Client: {ExpectedApiVersion}" :
          $"API version mismatch. Server: {serverVersion}, Client expected: {ExpectedApiVersion}";

        return new ApiVersionCheckResult
        {
          IsCompatible = isCompatible && clientVersionSupported,
          ServerVersion = serverVersion,
          ClientVersion = ExpectedApiVersion,
          SupportedVersions = supportedVersions,
          Message = message,
          Recommendation = recommendation
        };
      }
      catch (Exception ex)
      {
        // Log but don't fail - version check is informational
        return new ApiVersionCheckResult
        {
          IsCompatible = true, // Assume compatible on error for backwards compatibility
          ServerVersion = "unknown",
          ClientVersion = ExpectedApiVersion,
          Message = $"Version check failed: {ex.Message}",
          Error = ex.Message
        };
      }
    }

    /// <summary>
    /// Gets version information from the backend.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>API version information</returns>
    public async Task<ApiVersionInfo?> GetApiVersionInfoAsync(CancellationToken cancellationToken = default)
    {
      try
      {
        var response = await _httpClient.GetAsync("/api/version/", cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
          return null;
        }

        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        var currentVersion = root.TryGetProperty("current_version", out var cv) ? cv.GetString() : null;
        var defaultVersion = root.TryGetProperty("default_version", out var dv) ? dv.GetString() : null;
        var supportedVersions = new List<string>();

        if (root.TryGetProperty("supported_versions", out var supported) && supported.ValueKind == JsonValueKind.Array)
        {
          foreach (var v in supported.EnumerateArray())
          {
            if (v.ValueKind == JsonValueKind.String)
            {
              supportedVersions.Add(v.GetString() ?? "");
            }
          }
        }

        return new ApiVersionInfo
        {
          CurrentVersion = currentVersion ?? "unknown",
          DefaultVersion = defaultVersion ?? "unknown",
          SupportedVersions = supportedVersions
        };
      }
      catch
      {
        return null;
      }
    }

    /// <summary>
    /// Validates API version on startup and logs warnings if there are compatibility issues.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>True if compatible, false if incompatible</returns>
    public async Task<bool> ValidateApiVersionOnStartupAsync(CancellationToken cancellationToken = default)
    {
      var result = await CheckApiVersionAsync(cancellationToken);

      if (!result.IsCompatible)
      {
        // Log warning about version mismatch
        System.Diagnostics.Debug.WriteLine(
          $"[WARNING] API version mismatch: {result.Message}. " +
          $"Recommendation: {result.Recommendation ?? "Update client"}");
        return false;
      }

      if (!string.IsNullOrEmpty(result.Recommendation))
      {
        // Log recommendation even if compatible
        System.Diagnostics.Debug.WriteLine(
          $"[INFO] API version note: {result.Recommendation}");
      }

      return true;
    }

    public async Task<VoiceSynthesisResponse> SynthesizeVoiceAsync(
        VoiceSynthesisRequest request,
        CancellationToken cancellationToken = default)
    {
      return await ExecuteWithRetryAsync(async () =>
      {
        var response = await _httpClient.PostAsJsonAsync(
                  "/api/voice/synthesize",
                  request,
                  _jsonOptions,
                  cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
          throw await CreateExceptionFromResponseAsync(response);
        }

        try
        {
          return await response.Content.ReadFromJsonAsync<VoiceSynthesisResponse>(_jsonOptions, cancellationToken)
                    ?? throw new BackendDeserializationException("Failed to deserialize voice synthesis response.");
        }
        catch (JsonException ex)
        {
          throw new BackendDeserializationException(
                    "The backend returned an invalid response format for voice synthesis.",
                    ex);
        }
      });
    }

    public async Task<VoiceAnalysisResponse> AnalyzeVoiceAsync(
        Stream audioFile,
        string? metrics = null,
        CancellationToken cancellationToken = default)
    {
      return await ExecuteWithRetryAsync(async () =>
      {
        using var content = new MultipartFormDataContent();
        var streamContent = new StreamContent(audioFile);
        streamContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("audio/wav");
        content.Add(streamContent, "audio_file", "audio.wav");

        if (!string.IsNullOrEmpty(metrics))
        {
          content.Add(new StringContent(metrics), "metrics");
        }

        var response = await _httpClient.PostAsync("/api/voice/analyze", content, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
          throw await CreateExceptionFromResponseAsync(response);
        }

        try
        {
          return await response.Content.ReadFromJsonAsync<VoiceAnalysisResponse>(_jsonOptions, cancellationToken)
                    ?? throw new BackendDeserializationException("Failed to deserialize voice analysis response.");
        }
        catch (JsonException ex)
        {
          throw new BackendDeserializationException(
                    "The backend returned an invalid response format for voice analysis.",
                    ex);
        }
      });
    }

    public async Task<VoiceCloneResponse> CloneVoiceAsync(
        Stream referenceAudio,
        VoiceCloneRequest request,
        CancellationToken cancellationToken = default)
    {
      return await ExecuteWithRetryAsync(async () =>
      {
        using var content = new MultipartFormDataContent();
        var streamContent = new StreamContent(referenceAudio);
        streamContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("audio/wav");
        content.Add(streamContent, "reference_audio", "reference.wav");

        if (!string.IsNullOrEmpty(request.Text))
        {
          content.Add(new StringContent(request.Text), "text");
        }
        content.Add(new StringContent(request.Engine), "engine");
        content.Add(new StringContent(request.QualityMode), "quality_mode");

        // Add new advanced parameters
        content.Add(new StringContent(request.EnhanceQuality.ToString().ToLower()), "enhance_quality");
        content.Add(new StringContent(request.UseMultiReference.ToString().ToLower()), "use_multi_reference");
        content.Add(new StringContent(request.UseRvcPostprocessing.ToString().ToLower()), "use_rvc_postprocessing");
        content.Add(new StringContent(request.Language), "language");

        // Add prosody parameters as JSON if provided
        if (request.ProsodyParams?.Count > 0)
        {
          var prosodyJson = System.Text.Json.JsonSerializer.Serialize(request.ProsodyParams);
          content.Add(new StringContent(prosodyJson), "prosody_params");
        }

        if (!string.IsNullOrWhiteSpace(request.ProjectId))
        {
          content.Add(new StringContent(request.ProjectId), "project_id");
        }

        if (!string.IsNullOrWhiteSpace(request.ProfileName))
        {
          content.Add(new StringContent(request.ProfileName), "profile_name");
        }

        var response = await _httpClient.PostAsync("/api/voice/clone", content, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
          throw await CreateExceptionFromResponseAsync(response);
        }

        try
        {
          return await response.Content.ReadFromJsonAsync<VoiceCloneResponse>(_jsonOptions, cancellationToken)
                    ?? throw new BackendDeserializationException("Failed to deserialize voice clone response.");
        }
        catch (JsonException ex)
        {
          throw new BackendDeserializationException(
                    "The backend returned an invalid response format for voice cloning.",
                    ex);
        }
      });
    }

    public async Task<List<VoiceProfile>> GetProfilesAsync(CancellationToken cancellationToken = default)
    {
      return await ExecuteWithRetryAsync(async () =>
      {
        var response = await _httpClient.GetAsync("/api/profiles", cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
          throw await CreateExceptionFromResponseAsync(response);
        }

        // Backend returns paginated response: {"items": [...], "pagination": {...}}
        // Extract the items array from the wrapper
        var jsonString = await response.Content.ReadAsStringAsync(cancellationToken);

        // Detect HTML responses (backend returning error page instead of JSON)
        if (string.IsNullOrWhiteSpace(jsonString))
        {
          throw new BackendDeserializationException(
            "Backend returned empty response for profiles. Verify backend is running.");
        }

        if (jsonString.TrimStart().StartsWith("<"))
        {
          var preview = jsonString.Substring(0, Math.Min(200, jsonString.Length));
          throw new BackendDeserializationException(
            $"Backend returned HTML instead of JSON. This typically means the backend server is not running or returned an error page. Preview: {preview}");
        }

        try
        {
          using var doc = System.Text.Json.JsonDocument.Parse(jsonString);

          if (doc.RootElement.TryGetProperty("items", out var itemsElement))
          {
            return System.Text.Json.JsonSerializer.Deserialize<List<VoiceProfile>>(itemsElement.GetRawText(), _jsonOptions)
                      ?? new List<VoiceProfile>();
          }

          // Fallback: try parsing as direct array for backward compatibility
          return System.Text.Json.JsonSerializer.Deserialize<List<VoiceProfile>>(jsonString, _jsonOptions)
                    ?? new List<VoiceProfile>();
        }
        catch (System.Text.Json.JsonException ex)
        {
          var preview = jsonString.Substring(0, Math.Min(500, jsonString.Length));
          throw new BackendDeserializationException(
            $"Failed to parse profiles response. Ensure backend API is returning valid JSON. Content preview: {preview}", ex);
        }
      });
    }

    public async Task<VoiceProfile> GetProfileAsync(string profileId, CancellationToken cancellationToken = default)
    {
      return await ExecuteWithRetryAsync(async () =>
      {
        var response = await _httpClient.GetAsync($"/api/profiles/{profileId}", cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
          throw await CreateExceptionFromResponseAsync(response);
        }

        return await response.Content.ReadFromJsonAsync<VoiceProfile>(_jsonOptions, cancellationToken)
                  ?? throw new BackendDeserializationException("Failed to deserialize profile");
      });
    }

    public async Task<VoiceProfile> CreateProfileAsync(
        string name,
        string language = "en",
        string? emotion = null,
        List<string>? tags = null,
        CancellationToken cancellationToken = default)
    {
      return await ExecuteWithRetryAsync(async () =>
      {
        var request = new
        {
          name,
          language,
          emotion,
          tags = tags ?? new List<string>()
        };

        var response = await _httpClient.PostAsJsonAsync("/api/profiles", request, _jsonOptions, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
          throw await CreateExceptionFromResponseAsync(response);
        }

        return await response.Content.ReadFromJsonAsync<VoiceProfile>(_jsonOptions, cancellationToken)
                  ?? throw new BackendDeserializationException("Failed to deserialize profile");
      });
    }

    public async Task<VoiceProfile> UpdateProfileAsync(
        string profileId,
        string? name = null,
        string? language = null,
        string? emotion = null,
        List<string>? tags = null,
        CancellationToken cancellationToken = default)
    {
      return await ExecuteWithRetryAsync(async () =>
      {
        var request = new Dictionary<string, object?>();
        if (name != null) request["name"] = name;
        if (language != null) request["language"] = language;
        if (emotion != null) request["emotion"] = emotion;
        if (tags != null) request["tags"] = tags;

        var response = await _httpClient.PutAsJsonAsync(
                  $"/api/profiles/{profileId}",
                  request,
                  _jsonOptions,
                  cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
          throw await CreateExceptionFromResponseAsync(response);
        }

        return await response.Content.ReadFromJsonAsync<VoiceProfile>(_jsonOptions, cancellationToken)
                  ?? throw new BackendDeserializationException("Failed to deserialize profile");
      });
    }

    public async Task<bool> DeleteProfileAsync(string profileId, CancellationToken cancellationToken = default)
    {
      return await ExecuteWithRetryAsync(async () =>
      {
        var response = await _httpClient.DeleteAsync($"/api/profiles/{profileId}", cancellationToken);
        return response.IsSuccessStatusCode;
      });
    }

    public async Task<List<Project>> GetProjectsAsync(CancellationToken cancellationToken = default)
    {
      return await ExecuteWithRetryAsync(async () =>
      {
        var response = await _httpClient.GetAsync("/api/projects", cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
          throw await CreateExceptionFromResponseAsync(response);
        }

        // Backend returns paginated response: {"items": [...], "pagination": {...}}
        // Extract the items array from the wrapper
        var jsonString = await response.Content.ReadAsStringAsync(cancellationToken);
        using var doc = System.Text.Json.JsonDocument.Parse(jsonString);

        if (doc.RootElement.TryGetProperty("items", out var itemsElement))
        {
          return System.Text.Json.JsonSerializer.Deserialize<List<Project>>(itemsElement.GetRawText(), _jsonOptions)
                    ?? new List<Project>();
        }

        // Fallback: try parsing as direct array for backward compatibility
        return System.Text.Json.JsonSerializer.Deserialize<List<Project>>(jsonString, _jsonOptions)
                  ?? new List<Project>();
      });
    }

    public async Task<Project> GetProjectAsync(string projectId, CancellationToken cancellationToken = default)
    {
      return await ExecuteWithRetryAsync(async () =>
      {
        var response = await _httpClient.GetAsync($"/api/projects/{projectId}", cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
          throw await CreateExceptionFromResponseAsync(response);
        }

        return await response.Content.ReadFromJsonAsync<Project>(_jsonOptions, cancellationToken)
                  ?? throw new BackendDeserializationException("Failed to deserialize project");
      });
    }

    public async Task<Project> CreateProjectAsync(
        string name,
        string? description = null,
        CancellationToken cancellationToken = default)
    {
      return await ExecuteWithRetryAsync(async () =>
      {
        var request = new
        {
          name,
          description
        };

        var response = await _httpClient.PostAsJsonAsync("/api/projects", request, _jsonOptions, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
          throw await CreateExceptionFromResponseAsync(response);
        }

        return await response.Content.ReadFromJsonAsync<Project>(_jsonOptions, cancellationToken)
                  ?? throw new BackendDeserializationException("Failed to deserialize project");
      });
    }

    public async Task<Project> UpdateProjectAsync(
        string projectId,
        string? name = null,
        string? description = null,
        List<string>? voiceProfileIds = null,
        CancellationToken cancellationToken = default)
    {
      return await ExecuteWithRetryAsync(async () =>
      {
        var request = new Dictionary<string, object?>();
        if (name != null) request["name"] = name;
        if (description != null) request["description"] = description;
        if (voiceProfileIds != null) request["voice_profile_ids"] = voiceProfileIds;

        var response = await _httpClient.PutAsJsonAsync(
                  $"/api/projects/{projectId}",
                  request,
                  _jsonOptions,
                  cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
          throw await CreateExceptionFromResponseAsync(response);
        }

        return await response.Content.ReadFromJsonAsync<Project>(_jsonOptions, cancellationToken)
                  ?? throw new BackendDeserializationException("Failed to deserialize project");
      });
    }

    public async Task<bool> DeleteProjectAsync(string projectId, CancellationToken cancellationToken = default)
    {
      return await ExecuteWithRetryAsync(async () =>
      {
        var response = await _httpClient.DeleteAsync($"/api/projects/{projectId}", cancellationToken);
        return response.IsSuccessStatusCode;
      });
    }

    public async Task<Stream> GetAudioStreamAsync(string audioId, CancellationToken cancellationToken = default)
    {
      return await ExecuteWithRetryAsync(async () =>
      {
        var response = await _httpClient.GetAsync($"/api/voice/audio/{audioId}", cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
          throw await CreateExceptionFromResponseAsync(response);
        }

        return await response.Content.ReadAsStreamAsync(cancellationToken);
      });
    }

    /// <summary>
    /// Exports an audio file to the specified format.
    /// </summary>
    public async Task<Stream> ExportAudioAsync(
        string source,
        string targetFormat,
        int? sampleRate = null,
        int? channels = null,
        int? bitrateKbps = null,
        bool normalize = false,
        CancellationToken cancellationToken = default)
    {
      return await ExecuteWithRetryAsync(async () =>
      {
        var request = new VoiceStudio.App.Core.Models.AudioExportRequest
        {
          Source = source,
          Format = targetFormat.TrimStart('.').ToLowerInvariant(),
          SampleRate = sampleRate,
          Channels = channels,
          BitrateKbps = bitrateKbps,
          Normalize = normalize
        };

        var jsonContent = new StringContent(
            JsonSerializer.Serialize(request),
            System.Text.Encoding.UTF8,
            "application/json");

        var response = await _httpClient.PostAsync("/api/audio/export", jsonContent, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
          throw await CreateExceptionFromResponseAsync(response);
        }

        return await response.Content.ReadAsStreamAsync(cancellationToken);
      });
    }

    /// <summary>
    /// Gets the list of supported audio formats for import/export.
    /// </summary>
    public async Task<List<VoiceStudio.App.Core.Models.AudioFormatInfo>> GetSupportedAudioFormatsAsync(CancellationToken cancellationToken = default)
    {
      return await ExecuteWithRetryAsync(async () =>
      {
        var response = await _httpClient.GetAsync("/api/audio/formats", cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
          throw await CreateExceptionFromResponseAsync(response);
        }

        var jsonString = await response.Content.ReadAsStringAsync(cancellationToken);
        var result = JsonSerializer.Deserialize<List<VoiceStudio.App.Core.Models.AudioFormatInfo>>(jsonString);
        return result ?? [];
      });
    }

    /// <summary>
    /// Uploads an audio file to the backend for analysis.
    /// </summary>
    /// <param name="filePath">Path to the audio file.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Upload response containing the audio ID.</returns>
    public async Task<AudioUploadResponse> UploadAudioFileAsync(string filePath, CancellationToken cancellationToken = default)
    {
      return await ExecuteWithRetryAsync(async () =>
      {
        using var content = new MultipartFormDataContent();
        using var fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read);
        using var streamContent = new StreamContent(fileStream);

        // Determine content type from extension
        var extension = Path.GetExtension(filePath).ToLowerInvariant();
        var contentType = extension switch
        {
          ".wav" => "audio/wav",
          ".mp3" => "audio/mpeg",
          ".flac" => "audio/flac",
          ".m4a" => "audio/mp4",
          ".ogg" => "audio/ogg",
          ".aac" => "audio/aac",
          _ => "audio/octet-stream"
        };
        streamContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(contentType);

        var fileName = Path.GetFileName(filePath);
        content.Add(streamContent, "file", fileName);

        var response = await _httpClient.PostAsync("/api/audio/upload", content, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
          throw await CreateExceptionFromResponseAsync(response);
        }

        return await response.Content.ReadFromJsonAsync<AudioUploadResponse>(_jsonOptions, cancellationToken)
                  ?? throw new BackendDeserializationException("Failed to deserialize audio upload response");
      });
    }

    public async Task<List<ProjectAudioFile>> ListProjectAudioAsync(string projectId, CancellationToken cancellationToken = default)
    {
      return await ExecuteWithRetryAsync(async () =>
      {
        var response = await _httpClient.GetAsync($"/api/projects/{projectId}/audio", cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
          throw await CreateExceptionFromResponseAsync(response);
        }

        return await response.Content.ReadFromJsonAsync<List<ProjectAudioFile>>(_jsonOptions, cancellationToken)
                  ?? new List<ProjectAudioFile>();
      });
    }

    public async Task<Stream> GetProjectAudioAsync(string projectId, string filename, CancellationToken cancellationToken = default)
    {
      return await ExecuteWithRetryAsync(async () =>
      {
        var response = await _httpClient.GetAsync($"/api/projects/{projectId}/audio/{Uri.EscapeDataString(filename)}", cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
          throw await CreateExceptionFromResponseAsync(response);
        }

        return await response.Content.ReadAsStreamAsync(cancellationToken);
      });
    }

    public async Task<WaveformData> GetWaveformDataAsync(string audioId, int width = 1024, string mode = "peak", CancellationToken cancellationToken = default)
    {
      return await ExecuteWithRetryAsync(async () =>
      {
        var url = $"/api/audio/waveform?audio_id={Uri.EscapeDataString(audioId)}&width={width}&mode={Uri.EscapeDataString(mode)}";
        var response = await _httpClient.GetAsync(url, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
          throw await CreateExceptionFromResponseAsync(response);
        }

        return await response.Content.ReadFromJsonAsync<WaveformData>(_jsonOptions, cancellationToken)
                  ?? throw new BackendDeserializationException("Failed to deserialize waveform data");
      });
    }

    public async Task<SpectrogramData> GetSpectrogramDataAsync(string audioId, int width = 512, int height = 256, CancellationToken cancellationToken = default)
    {
      return await ExecuteWithRetryAsync(async () =>
      {
        var url = $"/api/audio/spectrogram?audio_id={Uri.EscapeDataString(audioId)}&width={width}&height={height}";
        var response = await _httpClient.GetAsync(url, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
          throw await CreateExceptionFromResponseAsync(response);
        }

        return await response.Content.ReadFromJsonAsync<SpectrogramData>(_jsonOptions, cancellationToken)
                  ?? throw new BackendDeserializationException("Failed to deserialize spectrogram data");
      });
    }

    public async Task<AudioMeters> GetAudioMetersAsync(string audioId, CancellationToken cancellationToken = default)
    {
      return await ExecuteWithRetryAsync(async () =>
      {
        var url = $"/api/audio/meters?audio_id={Uri.EscapeDataString(audioId)}";
        var response = await _httpClient.GetAsync(url, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
          throw await CreateExceptionFromResponseAsync(response);
        }

        return await response.Content.ReadFromJsonAsync<AudioMeters>(_jsonOptions, cancellationToken)
                  ?? throw new BackendDeserializationException("Failed to deserialize audio meters");
      });
    }

    public async Task<RadarData> GetRadarDataAsync(string audioId, CancellationToken cancellationToken = default)
    {
      return await ExecuteWithRetryAsync(async () =>
      {
        var url = $"/api/audio/radar?audio_id={Uri.EscapeDataString(audioId)}";
        var response = await _httpClient.GetAsync(url, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
          throw await CreateExceptionFromResponseAsync(response);
        }

        return await response.Content.ReadFromJsonAsync<RadarData>(_jsonOptions, cancellationToken)
                  ?? throw new BackendDeserializationException("Failed to deserialize radar data");
      });
    }

    public async Task<LoudnessData> GetLoudnessDataAsync(string audioId, double windowSize = 0.4, CancellationToken cancellationToken = default)
    {
      return await ExecuteWithRetryAsync(async () =>
      {
        // Backend expects 'block_size' parameter (maps to windowSize)
        var url = $"/api/audio/loudness?audio_id={Uri.EscapeDataString(audioId)}&block_size={windowSize}";
        var response = await _httpClient.GetAsync(url, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
          throw await CreateExceptionFromResponseAsync(response);
        }

        return await response.Content.ReadFromJsonAsync<LoudnessData>(_jsonOptions, cancellationToken)
                  ?? throw new BackendDeserializationException("Failed to deserialize loudness data");
      });
    }

    public async Task<PhaseData> GetPhaseDataAsync(string audioId, double windowSize = 0.1, CancellationToken cancellationToken = default)
    {
      return await ExecuteWithRetryAsync(async () =>
      {
        var url = $"/api/audio/phase?audio_id={Uri.EscapeDataString(audioId)}&window_size={windowSize}";
        var response = await _httpClient.GetAsync(url, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
          throw await CreateExceptionFromResponseAsync(response);
        }

        return await response.Content.ReadFromJsonAsync<PhaseData>(_jsonOptions, cancellationToken)
                  ?? throw new BackendDeserializationException("Failed to deserialize phase data");
      });
    }

    public async Task<ProjectAudioFile> SaveAudioToProjectAsync(
        string projectId,
        string audioId,
        string? filename = null,
        CancellationToken cancellationToken = default)
    {
      return await ExecuteWithRetryAsync(async () =>
      {
        // Build query string for audio_id and optional filename
        var queryParams = new List<string> { $"audio_id={Uri.EscapeDataString(audioId)}" };
        if (!string.IsNullOrEmpty(filename))
        {
          queryParams.Add($"filename={Uri.EscapeDataString(filename)}");
        }

        var url = $"/api/projects/{projectId}/audio/save?{string.Join("&", queryParams)}";
        var response = await _httpClient.PostAsync(url, null, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
          throw await CreateExceptionFromResponseAsync(response);
        }

        // Backend returns dict with filename, url, saved_path
        var result = await response.Content.ReadFromJsonAsync<Dictionary<string, object>>(_jsonOptions, cancellationToken)
                  ?? throw new BackendDeserializationException("Failed to deserialize response");

        // Convert to ProjectAudioFile
        return new ProjectAudioFile
        {
          Filename = result.GetValueOrDefault("filename")?.ToString() ?? string.Empty,
          Url = result.GetValueOrDefault("url")?.ToString() ?? string.Empty,
          SavedPath = result.GetValueOrDefault("saved_path")?.ToString(),
          Size = 0, // Not provided in save response
          Modified = DateTime.UtcNow.ToString("O") // Use current time
        };
      });
    }

    public async Task<List<AudioTrack>> GetTracksAsync(string projectId, CancellationToken cancellationToken = default)
    {
      return await ExecuteWithRetryAsync(async () =>
      {
        var response = await _httpClient.GetAsync($"/api/projects/{projectId}/tracks", cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
          throw await CreateExceptionFromResponseAsync(response);
        }

        return await response.Content.ReadFromJsonAsync<List<AudioTrack>>(_jsonOptions, cancellationToken)
                  ?? throw new BackendDeserializationException("Failed to deserialize tracks");
      });
    }

    public async Task<AudioTrack> GetTrackAsync(string projectId, string trackId, CancellationToken cancellationToken = default)
    {
      return await ExecuteWithRetryAsync(async () =>
      {
        var response = await _httpClient.GetAsync($"/api/projects/{projectId}/tracks/{trackId}", cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
          throw await CreateExceptionFromResponseAsync(response);
        }

        return await response.Content.ReadFromJsonAsync<AudioTrack>(_jsonOptions, cancellationToken)
                  ?? throw new BackendDeserializationException("Failed to deserialize track");
      });
    }

    public async Task<AudioTrack> CreateTrackAsync(string projectId, string name, string? engine = null, CancellationToken cancellationToken = default)
    {
      return await ExecuteWithRetryAsync(async () =>
      {
        var request = new { name, engine };
        var response = await _httpClient.PostAsJsonAsync($"/api/projects/{projectId}/tracks", request, _jsonOptions, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
          throw await CreateExceptionFromResponseAsync(response);
        }

        return await response.Content.ReadFromJsonAsync<AudioTrack>(_jsonOptions, cancellationToken)
                  ?? throw new BackendDeserializationException("Failed to deserialize track");
      });
    }

    public async Task<AudioTrack> UpdateTrackAsync(string projectId, string trackId, string? name = null, string? engine = null, CancellationToken cancellationToken = default)
    {
      return await ExecuteWithRetryAsync(async () =>
      {
        var request = new Dictionary<string, object?>();
        if (name != null) request["name"] = name;
        if (engine != null) request["engine"] = engine;

        var response = await _httpClient.PutAsJsonAsync($"/api/projects/{projectId}/tracks/{trackId}", request, _jsonOptions, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
          throw await CreateExceptionFromResponseAsync(response);
        }

        return await response.Content.ReadFromJsonAsync<AudioTrack>(_jsonOptions, cancellationToken)
                  ?? throw new BackendDeserializationException("Failed to deserialize track");
      });
    }

    public async Task<bool> DeleteTrackAsync(string projectId, string trackId, CancellationToken cancellationToken = default)
    {
      return await ExecuteWithRetryAsync(async () =>
      {
        var response = await _httpClient.DeleteAsync($"/api/projects/{projectId}/tracks/{trackId}", cancellationToken);
        return response.IsSuccessStatusCode;
      });
    }

    public async Task<AudioClip> CreateClipAsync(string projectId, string trackId, AudioClip clip, CancellationToken cancellationToken = default)
    {
      return await ExecuteWithRetryAsync(async () =>
      {
        // Convert AudioClip to backend format
        var request = new
        {
          name = clip.Name,
          profile_id = clip.ProfileId,
          audio_id = clip.AudioId,
          audio_url = clip.AudioUrl,
          duration_seconds = clip.Duration.TotalSeconds,
          start_time = clip.StartTime,
          engine = clip.Engine,
          quality_score = clip.QualityScore
        };

        var response = await _httpClient.PostAsJsonAsync($"/api/projects/{projectId}/tracks/{trackId}/clips", request, _jsonOptions, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
          throw await CreateExceptionFromResponseAsync(response);
        }

        var backendClip = await response.Content.ReadFromJsonAsync<BackendAudioClip>(_jsonOptions, cancellationToken)
                  ?? throw new BackendDeserializationException("Failed to deserialize clip");

        // Convert back to AudioClip
        return new AudioClip
        {
          Id = backendClip.Id,
          Name = backendClip.Name,
          ProfileId = backendClip.ProfileId,
          AudioId = backendClip.AudioId,
          AudioUrl = backendClip.AudioUrl,
          Duration = TimeSpan.FromSeconds(backendClip.DurationSeconds),
          StartTime = backendClip.StartTime,
          Engine = backendClip.Engine,
          QualityScore = backendClip.QualityScore
        };
      });
    }

    public async Task<AudioClip> UpdateClipAsync(string projectId, string trackId, string clipId, string? name = null, double? startTime = null, CancellationToken cancellationToken = default)
    {
      return await ExecuteWithRetryAsync(async () =>
      {
        var request = new Dictionary<string, object?>();
        if (name != null) request["name"] = name;
        if (startTime.HasValue) request["start_time"] = startTime.Value;

        var response = await _httpClient.PutAsJsonAsync($"/api/projects/{projectId}/tracks/{trackId}/clips/{clipId}", request, _jsonOptions, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
          throw await CreateExceptionFromResponseAsync(response);
        }

        var backendClip = await response.Content.ReadFromJsonAsync<BackendAudioClip>(_jsonOptions, cancellationToken)
                  ?? throw new BackendDeserializationException("Failed to deserialize clip");

        // Convert back to AudioClip
        return new AudioClip
        {
          Id = backendClip.Id,
          Name = backendClip.Name,
          ProfileId = backendClip.ProfileId,
          AudioId = backendClip.AudioId,
          AudioUrl = backendClip.AudioUrl,
          Duration = TimeSpan.FromSeconds(backendClip.DurationSeconds),
          StartTime = backendClip.StartTime,
          Engine = backendClip.Engine,
          QualityScore = backendClip.QualityScore
        };
      });
    }

    public async Task<bool> DeleteClipAsync(string projectId, string trackId, string clipId, CancellationToken cancellationToken = default)
    {
      return await ExecuteWithRetryAsync(async () =>
      {
        var response = await _httpClient.DeleteAsync($"/api/projects/{projectId}/tracks/{trackId}/clips/{clipId}", cancellationToken);
        return response.IsSuccessStatusCode;
      });
    }

    // Timeline markers management
    public async Task<List<TimelineMarker>> GetMarkersAsync(string projectId, string? category = null, double? minTime = null, double? maxTime = null, CancellationToken cancellationToken = default)
    {
      var queryParams = new NameValueCollection();
      if (!string.IsNullOrEmpty(category))
        queryParams.Add("category", category);
      if (minTime.HasValue)
        queryParams.Add("min_time", minTime.Value.ToString());
      if (maxTime.HasValue)
        queryParams.Add("max_time", maxTime.Value.ToString());

      var queryString = string.Join("&",
          (queryParams.AllKeys ?? Array.Empty<string>()).SelectMany(key =>
              queryParams.GetValues(key)?.Select(value => $"{key}={Uri.EscapeDataString(value)}") ?? Array.Empty<string>()
          )
      );

      var url = $"/api/projects/{Uri.EscapeDataString(projectId)}/markers";
      if (!string.IsNullOrEmpty(queryString))
        url += $"?{queryString}";

      return await GetAsync<List<TimelineMarker>>(url, cancellationToken) ?? new List<TimelineMarker>();
    }

    public async Task<TimelineMarker> GetMarkerAsync(string projectId, string markerId, CancellationToken cancellationToken = default)
    {
      return await GetAsync<TimelineMarker>($"/api/projects/{Uri.EscapeDataString(projectId)}/markers/{Uri.EscapeDataString(markerId)}", cancellationToken)
          ?? throw new BackendDeserializationException("Failed to deserialize marker");
    }

    public async Task<TimelineMarker> CreateMarkerAsync(string projectId, MarkerCreateRequest request, CancellationToken cancellationToken = default)
    {
      return await PostAsync<MarkerCreateRequest, TimelineMarker>($"/api/projects/{Uri.EscapeDataString(projectId)}/markers", request, cancellationToken);
    }

    public async Task<TimelineMarker> UpdateMarkerAsync(string projectId, string markerId, MarkerUpdateRequest request, CancellationToken cancellationToken = default)
    {
      return await PutAsync<MarkerUpdateRequest, TimelineMarker>($"/api/projects/{Uri.EscapeDataString(projectId)}/markers/{Uri.EscapeDataString(markerId)}", request, cancellationToken);
    }

    public async Task<bool> DeleteMarkerAsync(string projectId, string markerId, CancellationToken cancellationToken = default)
    {
      var response = await SendRequestAsync<object, object>($"/api/projects/{Uri.EscapeDataString(projectId)}/markers/{Uri.EscapeDataString(markerId)}", null, System.Net.Http.HttpMethod.Delete, cancellationToken);
      return response != null;
    }

    // Helper class for backend clip format
    private class BackendAudioClip
    {
      public string Id { get; set; } = string.Empty;
      public string Name { get; set; } = string.Empty;
      public string ProfileId { get; set; } = string.Empty;
      public string AudioId { get; set; } = string.Empty;
      public string AudioUrl { get; set; } = string.Empty;
      public double DurationSeconds { get; set; }
      public double StartTime { get; set; }
      public string? Engine { get; set; }
      public double? QualityScore { get; set; }
    }

    private async Task<T> ExecuteWithRetryAsync<T>(Func<Task<T>> operation, int maxRetries = MaxRetries)
    {
      // Check connection status periodically
      await UpdateConnectionStatusAsync();

      // Execute through circuit breaker with exponential backoff retry
      try
      {
        return await _circuitBreaker.ExecuteAsync(async () =>
            await RetryHelper.ExecuteWithExponentialBackoffAsync<T>(
                operation,
                maxRetries: maxRetries,
                initialDelayMs: RetryDelayMs,
                maxDelayMs: 10000
            )
        );
      }
      catch (Exception ex)
      {
        // Update connection status on failure
        await UpdateConnectionStatusAsync();

        // Convert to appropriate BackendException
        if (ex is BackendException bex)
        {
          throw;
        }
        else if (ex is HttpRequestException httpEx)
        {
          _isConnected = false;
          throw new BackendUnavailableException(
              "Unable to connect to the backend server. Please check your connection and ensure the backend is running.",
              httpEx);
        }
        else if (ex is TaskCanceledException timeoutEx && !timeoutEx.CancellationToken.IsCancellationRequested)
        {
          _isConnected = false;
          throw new BackendTimeoutException(
              "The request timed out. Please check your network connection and try again.",
              timeoutEx);
        }

        throw;
      }
    }

    /// <summary>
    /// Updates the connection status by checking backend health.
    /// </summary>
    private async Task UpdateConnectionStatusAsync()
    {
      // Only check periodically to avoid excessive requests
      var now = DateTime.UtcNow;
      if ((now - _lastConnectionCheck).TotalSeconds < ConnectionCheckIntervalSeconds)
        return;

      _lastConnectionCheck = now;

      try
      {
        var response = await _httpClient.GetAsync("/api/health", CancellationToken.None);
        _isConnected = response.IsSuccessStatusCode;
      }
      catch
      {
        _isConnected = false;
      }
    }

    private async Task<BackendException> CreateExceptionFromResponseAsync(HttpResponseMessage response)
    {
      var statusCode = (int)response.StatusCode;
      string? errorMessage = null;
      string? errorCode = null;
      string? requestId = null;
      string? timestamp = null;
      string? path = null;
      string? recoverySuggestion = null;

      try
      {
        var content = await response.Content.ReadAsStringAsync();
        if (!string.IsNullOrEmpty(content))
        {
          try
          {
            var errorJson = JsonSerializer.Deserialize<JsonElement>(content, _jsonOptions);
            // Parse backend StandardErrorResponse fields
            if (errorJson.TryGetProperty("message", out var messageProp))
              errorMessage = messageProp.GetString();
            if (errorJson.TryGetProperty("error", out var errorProp) && errorProp.ValueKind == JsonValueKind.String)
              errorMessage = errorProp.GetString() ?? errorMessage;
            if (errorJson.TryGetProperty("error_code", out var codeProp))
              errorCode = codeProp.GetString();
            if (errorJson.TryGetProperty("request_id", out var requestIdProp))
              requestId = requestIdProp.GetString();
            if (errorJson.TryGetProperty("timestamp", out var timestampProp))
              timestamp = timestampProp.GetString();
            if (errorJson.TryGetProperty("path", out var pathProp))
              path = pathProp.GetString();
            if (errorJson.TryGetProperty("recovery_suggestion", out var recoverySuggestionProp))
              recoverySuggestion = recoverySuggestionProp.GetString();
          }
          catch
          {
            // If JSON parsing fails, use raw content
            errorMessage = content.Length > 200 ? content.Substring(0, 200) + "..." : content;
          }
        }
      }
      catch (Exception ex)
      {
        ErrorLogger.LogWarning($"Best effort operation failed: {ex.Message}", "BackendAudioClip.Task");
      }

      // Default messages based on status code
      errorMessage ??= statusCode switch
      {
        400 => "Invalid request. Please check your input and try again.",
        401 => "Authentication failed. Please check your credentials.",
        403 => "You don't have permission to perform this action.",
        404 => "The requested resource was not found.",
        409 => "A conflict occurred. The resource may have been modified.",
        422 => "Validation failed. Please check your input.",
        429 => "Too many requests. Please wait a moment and try again.",
        500 => "An internal server error occurred. Please try again later.",
        502 => "Bad gateway. The backend server may be unavailable.",
        503 => "Service unavailable. The backend server is temporarily unavailable.",
        504 => "Gateway timeout. The request took too long to process.",
        _ => $"An error occurred (HTTP {statusCode}). Please try again."
      };

      // Determine retryability
      var isRetryable = statusCode >= 500 || statusCode == 429;
      
      BackendException exception = statusCode switch
      {
        400 => new BackendValidationException(errorMessage),
        401 => new BackendAuthenticationException(errorMessage),
        404 => new BackendNotFoundException(errorMessage),
        422 => new BackendValidationException(errorMessage),
        >= 500 => new BackendServerException(errorMessage, statusCode),
        _ => new BackendServerException(errorMessage, statusCode)
      };

      // Populate additional fields from backend StandardErrorResponse
      exception.ErrorCode = errorCode;
      exception.RequestId = requestId;
      exception.Timestamp = timestamp;
      exception.Path = path;
      exception.RecoverySuggestion = recoverySuggestion;
      exception.IsRetryable = isRetryable;

      return exception;
    }

    // Macro management
    public async Task<List<Macro>> GetMacrosAsync(string? projectId = null, CancellationToken cancellationToken = default)
    {
      return await ExecuteWithRetryAsync(async () =>
      {
        var url = "/api/macros";
        if (!string.IsNullOrEmpty(projectId))
        {
          url += $"?project_id={Uri.EscapeDataString(projectId)}";
        }
        var response = await _httpClient.GetAsync(url, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
          throw await CreateExceptionFromResponseAsync(response);
        }

        return await response.Content.ReadFromJsonAsync<List<Macro>>(_jsonOptions, cancellationToken)
                  ?? throw new BackendDeserializationException("Failed to deserialize macros");
      });
    }

    public async Task<Macro> GetMacroAsync(string macroId, CancellationToken cancellationToken = default)
    {
      return await ExecuteWithRetryAsync(async () =>
      {
        var response = await _httpClient.GetAsync($"/api/macros/{Uri.EscapeDataString(macroId)}", cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
          throw await CreateExceptionFromResponseAsync(response);
        }

        return await response.Content.ReadFromJsonAsync<Macro>(_jsonOptions, cancellationToken)
                  ?? throw new BackendDeserializationException("Failed to deserialize macro");
      });
    }

    public async Task<Macro> CreateMacroAsync(Macro macro, CancellationToken cancellationToken = default)
    {
      return await ExecuteWithRetryAsync(async () =>
      {
        var response = await _httpClient.PostAsJsonAsync("/api/macros", macro, _jsonOptions, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
          throw await CreateExceptionFromResponseAsync(response);
        }

        return await response.Content.ReadFromJsonAsync<Macro>(_jsonOptions, cancellationToken)
                  ?? throw new BackendDeserializationException("Failed to deserialize macro");
      });
    }

    public async Task<Macro> UpdateMacroAsync(string macroId, Macro macro, CancellationToken cancellationToken = default)
    {
      return await ExecuteWithRetryAsync(async () =>
      {
        var response = await _httpClient.PutAsJsonAsync($"/api/macros/{Uri.EscapeDataString(macroId)}", macro, _jsonOptions, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
          throw await CreateExceptionFromResponseAsync(response);
        }

        return await response.Content.ReadFromJsonAsync<Macro>(_jsonOptions, cancellationToken)
                  ?? throw new BackendDeserializationException("Failed to deserialize macro");
      });
    }

    public async Task<bool> DeleteMacroAsync(string macroId, CancellationToken cancellationToken = default)
    {
      return await ExecuteWithRetryAsync(async () =>
      {
        var response = await _httpClient.DeleteAsync($"/api/macros/{Uri.EscapeDataString(macroId)}", cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
          throw await CreateExceptionFromResponseAsync(response);
        }

        return true;
      });
    }

    public async Task<bool> ExecuteMacroAsync(string macroId, CancellationToken cancellationToken = default)
    {
      return await ExecuteWithRetryAsync(async () =>
      {
        var response = await _httpClient.PostAsync($"/api/macros/{Uri.EscapeDataString(macroId)}/execute", null, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
          throw await CreateExceptionFromResponseAsync(response);
        }

        return true;
      });
    }

    public async Task<MacroExecutionStatus> GetMacroExecutionStatusAsync(string macroId, CancellationToken cancellationToken = default)
    {
      return await ExecuteWithRetryAsync(async () =>
      {
        var response = await _httpClient.GetAsync($"/api/macros/{Uri.EscapeDataString(macroId)}/execution-status", cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
          throw await CreateExceptionFromResponseAsync(response);
        }

        return await response.Content.ReadFromJsonAsync<MacroExecutionStatus>(_jsonOptions, cancellationToken)
                  ?? throw new BackendDeserializationException("Failed to deserialize macro execution status");
      });
    }

    // Automation curves
    public async Task<List<AutomationCurve>> GetAutomationCurvesAsync(string trackId, CancellationToken cancellationToken = default)
    {
      return await ExecuteWithRetryAsync(async () =>
      {
        var response = await _httpClient.GetAsync($"/api/macros/automation/{Uri.EscapeDataString(trackId)}", cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
          throw await CreateExceptionFromResponseAsync(response);
        }

        return await response.Content.ReadFromJsonAsync<List<AutomationCurve>>(_jsonOptions, cancellationToken)
                  ?? throw new BackendDeserializationException("Failed to deserialize automation curves");
      });
    }

    public async Task<AutomationCurve> CreateAutomationCurveAsync(AutomationCurve curve, CancellationToken cancellationToken = default)
    {
      return await ExecuteWithRetryAsync(async () =>
      {
        var response = await _httpClient.PostAsJsonAsync("/api/macros/automation", curve, _jsonOptions, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
          throw await CreateExceptionFromResponseAsync(response);
        }

        return await response.Content.ReadFromJsonAsync<AutomationCurve>(_jsonOptions, cancellationToken)
                  ?? throw new BackendDeserializationException("Failed to deserialize automation curve");
      });
    }

    public async Task<AutomationCurve> UpdateAutomationCurveAsync(string curveId, AutomationCurve curve, CancellationToken cancellationToken = default)
    {
      return await ExecuteWithRetryAsync(async () =>
      {
        var response = await _httpClient.PutAsJsonAsync($"/api/macros/automation/{Uri.EscapeDataString(curveId)}", curve, _jsonOptions, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
          throw await CreateExceptionFromResponseAsync(response);
        }

        return await response.Content.ReadFromJsonAsync<AutomationCurve>(_jsonOptions, cancellationToken)
                  ?? throw new BackendDeserializationException("Failed to deserialize automation curve");
      });
    }

    public async Task<bool> DeleteAutomationCurveAsync(string curveId, CancellationToken cancellationToken = default)
    {
      return await ExecuteWithRetryAsync(async () =>
      {
        var response = await _httpClient.DeleteAsync($"/api/macros/automation/{Uri.EscapeDataString(curveId)}", cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
          throw await CreateExceptionFromResponseAsync(response);
        }

        return true;
      });
    }

    // Workflow management (IDEA 33)
    public async Task<List<Workflow>> GetWorkflowsAsync(int skip = 0, int limit = 100, bool enabledOnly = false, CancellationToken cancellationToken = default)
    {
      return await ExecuteWithRetryAsync(async () =>
      {
        var queryParams = new List<string> { $"skip={skip}", $"limit={limit}" };
        if (enabledOnly)
        {
          queryParams.Add("enabled_only=true");
        }
        var url = $"/api/workflows?{string.Join("&", queryParams)}";

        var response = await _httpClient.GetAsync(url, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
          throw await CreateExceptionFromResponseAsync(response);
        }

        return await response.Content.ReadFromJsonAsync<List<Workflow>>(_jsonOptions, cancellationToken)
                  ?? new List<Workflow>();
      });
    }

    public async Task<Workflow> GetWorkflowAsync(string workflowId, CancellationToken cancellationToken = default)
    {
      return await ExecuteWithRetryAsync(async () =>
      {
        var response = await _httpClient.GetAsync($"/api/workflows/{Uri.EscapeDataString(workflowId)}", cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
          throw await CreateExceptionFromResponseAsync(response);
        }

        return await response.Content.ReadFromJsonAsync<Workflow>(_jsonOptions, cancellationToken)
                  ?? throw new BackendDeserializationException("Failed to deserialize workflow");
      });
    }

    public async Task<Workflow> CreateWorkflowAsync(WorkflowCreateRequest request, CancellationToken cancellationToken = default)
    {
      return await ExecuteWithRetryAsync(async () =>
      {
        var response = await _httpClient.PostAsJsonAsync("/api/workflows", request, _jsonOptions, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
          throw await CreateExceptionFromResponseAsync(response);
        }

        return await response.Content.ReadFromJsonAsync<Workflow>(_jsonOptions, cancellationToken)
                  ?? throw new BackendDeserializationException("Failed to deserialize workflow");
      });
    }

    public async Task<Workflow> UpdateWorkflowAsync(string workflowId, WorkflowUpdateRequest request, CancellationToken cancellationToken = default)
    {
      return await ExecuteWithRetryAsync(async () =>
      {
        var response = await _httpClient.PutAsJsonAsync($"/api/workflows/{Uri.EscapeDataString(workflowId)}", request, _jsonOptions, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
          throw await CreateExceptionFromResponseAsync(response);
        }

        return await response.Content.ReadFromJsonAsync<Workflow>(_jsonOptions, cancellationToken)
                  ?? throw new BackendDeserializationException("Failed to deserialize workflow");
      });
    }

    public async Task<bool> DeleteWorkflowAsync(string workflowId, CancellationToken cancellationToken = default)
    {
      return await ExecuteWithRetryAsync(async () =>
      {
        var response = await _httpClient.DeleteAsync($"/api/workflows/{Uri.EscapeDataString(workflowId)}", cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
          throw await CreateExceptionFromResponseAsync(response);
        }

        return true;
      });
    }

    public async Task<WorkflowExecutionResult> ExecuteWorkflowAsync(string workflowId, Dictionary<string, object>? inputData = null, CancellationToken cancellationToken = default)
    {
      return await ExecuteWithRetryAsync(async () =>
      {
        var request = new { workflow_id = workflowId, input_data = inputData };
        var response = await _httpClient.PostAsJsonAsync($"/api/workflows/{Uri.EscapeDataString(workflowId)}/execute", request, _jsonOptions, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
          throw await CreateExceptionFromResponseAsync(response);
        }

        return await response.Content.ReadFromJsonAsync<WorkflowExecutionResult>(_jsonOptions, cancellationToken)
                  ?? throw new BackendDeserializationException("Failed to deserialize workflow execution result");
      });
    }

    // Model management
    public async Task<List<ModelInfo>> GetModelsAsync(string? engine = null, CancellationToken cancellationToken = default)
    {
      return await ExecuteWithRetryAsync(async () =>
      {
        var url = "/api/models";
        if (!string.IsNullOrEmpty(engine))
        {
          url += $"?engine={Uri.EscapeDataString(engine)}";
        }
        var response = await _httpClient.GetAsync(url, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
          throw await CreateExceptionFromResponseAsync(response);
        }

        return await response.Content.ReadFromJsonAsync<List<ModelInfo>>(_jsonOptions, cancellationToken)
                  ?? throw new BackendDeserializationException("Failed to deserialize models");
      });
    }

    public async Task<ModelInfo> GetModelAsync(string engine, string modelName, CancellationToken cancellationToken = default)
    {
      return await ExecuteWithRetryAsync(async () =>
      {
        var response = await _httpClient.GetAsync($"/api/models/{Uri.EscapeDataString(engine)}/{Uri.EscapeDataString(modelName)}", cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
          throw await CreateExceptionFromResponseAsync(response);
        }

        return await response.Content.ReadFromJsonAsync<ModelInfo>(_jsonOptions, cancellationToken)
                  ?? throw new BackendDeserializationException("Failed to deserialize model");
      });
    }

    public async Task<ModelInfo> RegisterModelAsync(string engine, string modelName, string modelPath, string? version = null, Dictionary<string, object>? metadata = null, CancellationToken cancellationToken = default)
    {
      return await ExecuteWithRetryAsync(async () =>
      {
        var request = new
        {
          engine,
          model_name = modelName,
          model_path = modelPath,
          version,
          metadata
        };
        var response = await _httpClient.PostAsJsonAsync("/api/models", request, _jsonOptions, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
          throw await CreateExceptionFromResponseAsync(response);
        }

        return await response.Content.ReadFromJsonAsync<ModelInfo>(_jsonOptions, cancellationToken)
                  ?? throw new BackendDeserializationException("Failed to deserialize model");
      });
    }

    public async Task<ModelVerifyResponse> VerifyModelAsync(string engine, string modelName, CancellationToken cancellationToken = default)
    {
      return await ExecuteWithRetryAsync(async () =>
      {
        var response = await _httpClient.PostAsync($"/api/models/{Uri.EscapeDataString(engine)}/{Uri.EscapeDataString(modelName)}/verify", null, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
          throw await CreateExceptionFromResponseAsync(response);
        }

        return await response.Content.ReadFromJsonAsync<ModelVerifyResponse>(_jsonOptions, cancellationToken)
                  ?? throw new BackendDeserializationException("Failed to deserialize verification response");
      });
    }

    public async Task<ModelInfo> UpdateModelChecksumAsync(string engine, string modelName, CancellationToken cancellationToken = default)
    {
      return await ExecuteWithRetryAsync(async () =>
      {
        var response = await _httpClient.PutAsync($"/api/models/{Uri.EscapeDataString(engine)}/{Uri.EscapeDataString(modelName)}/update-checksum", null, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
          throw await CreateExceptionFromResponseAsync(response);
        }

        return await response.Content.ReadFromJsonAsync<ModelInfo>(_jsonOptions, cancellationToken)
                  ?? throw new BackendDeserializationException("Failed to deserialize model");
      });
    }

    public async Task<bool> DeleteModelAsync(string engine, string modelName, CancellationToken cancellationToken = default)
    {
      return await ExecuteWithRetryAsync(async () =>
      {
        var response = await _httpClient.DeleteAsync($"/api/models/{Uri.EscapeDataString(engine)}/{Uri.EscapeDataString(modelName)}", cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
          throw await CreateExceptionFromResponseAsync(response);
        }

        return true;
      });
    }

    public async Task<Telemetry> GetTelemetryAsync(CancellationToken cancellationToken = default)
    {
      return await ExecuteWithRetryAsync(async () =>
      {
        var response = await _httpClient.GetAsync("/api/engine/telemetry", cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
          throw await CreateExceptionFromResponseAsync(response);
        }

        return await response.Content.ReadFromJsonAsync<Telemetry>(_jsonOptions, cancellationToken)
                  ?? throw new BackendDeserializationException("Failed to deserialize telemetry");
      });
    }

    public async Task<Stream> ExportModelAsync(string engine, string modelName, CancellationToken cancellationToken = default)
    {
      return await ExecuteWithRetryAsync(async () =>
      {
        var response = await _httpClient.GetAsync($"/api/models/{Uri.EscapeDataString(engine)}/{Uri.EscapeDataString(modelName)}/export", cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
          throw await CreateExceptionFromResponseAsync(response);
        }

        return await response.Content.ReadAsStreamAsync(cancellationToken);
      });
    }

    public async Task<ModelInfo> ImportModelAsync(Stream modelArchive, string? engine = null, CancellationToken cancellationToken = default)
    {
      return await ExecuteWithRetryAsync(async () =>
      {
        using var content = new MultipartFormDataContent();
        using var streamContent = new StreamContent(modelArchive);
        content.Add(streamContent, "file", "model.zip");

        var url = "/api/models/import";
        if (!string.IsNullOrEmpty(engine))
        {
          url += $"?engine={Uri.EscapeDataString(engine)}";
        }

        var response = await _httpClient.PostAsync(url, content, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
          throw await CreateExceptionFromResponseAsync(response);
        }

        return await response.Content.ReadFromJsonAsync<ModelInfo>(_jsonOptions, cancellationToken)
                  ?? throw new BackendDeserializationException("Failed to deserialize model");
      });
    }

    public async Task<StorageStats> GetStorageStatsAsync(CancellationToken cancellationToken = default)
    {
      return await ExecuteWithRetryAsync(async () =>
      {
        var response = await _httpClient.GetAsync("/api/models/stats/storage", cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
          throw await CreateExceptionFromResponseAsync(response);
        }

        return await response.Content.ReadFromJsonAsync<StorageStats>(_jsonOptions, cancellationToken)
                  ?? throw new BackendDeserializationException("Failed to deserialize storage stats");
      });
    }

    // Effects chain management
    public async Task<List<EffectChain>> GetEffectChainsAsync(string projectId, CancellationToken cancellationToken = default)
    {
      return await ExecuteWithRetryAsync(async () =>
      {
        var response = await _httpClient.GetAsync($"/api/effects/chains/{Uri.EscapeDataString(projectId)}", cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
          throw await CreateExceptionFromResponseAsync(response);
        }

        var chains = await response.Content.ReadFromJsonAsync<List<EffectChain>>(_jsonOptions, cancellationToken);
        return chains ?? new List<EffectChain>();
      });
    }

    public async Task<EffectChain> GetEffectChainAsync(string projectId, string chainId, CancellationToken cancellationToken = default)
    {
      return await ExecuteWithRetryAsync(async () =>
      {
        var response = await _httpClient.GetAsync($"/api/effects/chains/{Uri.EscapeDataString(projectId)}/{Uri.EscapeDataString(chainId)}", cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
          throw await CreateExceptionFromResponseAsync(response);
        }

        var chain = await response.Content.ReadFromJsonAsync<EffectChain>(_jsonOptions, cancellationToken);
        return chain ?? throw new BackendDeserializationException("Failed to deserialize effect chain");
      });
    }

    public async Task<EffectChain> CreateEffectChainAsync(string projectId, EffectChain chain, CancellationToken cancellationToken = default)
    {
      return await ExecuteWithRetryAsync(async () =>
      {
        var json = JsonSerializer.Serialize(chain, _jsonOptions);
        var content = new StringContent(json, Encoding.UTF8, "application/json");
        var response = await _httpClient.PostAsync($"/api/effects/chains/{Uri.EscapeDataString(projectId)}", content, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
          throw await CreateExceptionFromResponseAsync(response);
        }

        var createdChain = await response.Content.ReadFromJsonAsync<EffectChain>(_jsonOptions, cancellationToken);
        return createdChain ?? throw new BackendDeserializationException("Failed to deserialize effect chain");
      });
    }

    public async Task<EffectChain> UpdateEffectChainAsync(string projectId, string chainId, EffectChain chain, CancellationToken cancellationToken = default)
    {
      return await ExecuteWithRetryAsync(async () =>
      {
        var json = JsonSerializer.Serialize(chain, _jsonOptions);
        var content = new StringContent(json, Encoding.UTF8, "application/json");
        var response = await _httpClient.PutAsync($"/api/effects/chains/{Uri.EscapeDataString(projectId)}/{Uri.EscapeDataString(chainId)}", content, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
          throw await CreateExceptionFromResponseAsync(response);
        }

        var updatedChain = await response.Content.ReadFromJsonAsync<EffectChain>(_jsonOptions, cancellationToken);
        return updatedChain ?? throw new BackendDeserializationException("Failed to deserialize effect chain");
      });
    }

    public async Task<bool> DeleteEffectChainAsync(string projectId, string chainId, CancellationToken cancellationToken = default)
    {
      return await ExecuteWithRetryAsync(async () =>
      {
        var response = await _httpClient.DeleteAsync($"/api/effects/chains/{Uri.EscapeDataString(projectId)}/{Uri.EscapeDataString(chainId)}", cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
          throw await CreateExceptionFromResponseAsync(response);
        }

        // Delete operations return success if status code is 200-299
        return response.IsSuccessStatusCode;
      });
    }

    public async Task<EffectProcessResponse> ProcessAudioWithChainAsync(string projectId, string chainId, string audioId, string? outputFilename = null, CancellationToken cancellationToken = default)
    {
      return await ExecuteWithRetryAsync(async () =>
      {
        var url = $"/api/effects/chains/{Uri.EscapeDataString(projectId)}/{Uri.EscapeDataString(chainId)}/process?audio_id={Uri.EscapeDataString(audioId)}";
        if (!string.IsNullOrWhiteSpace(outputFilename))
        {
          url += $"&output_filename={Uri.EscapeDataString(outputFilename)}";
        }

        var response = await _httpClient.PostAsync(url, null, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
          throw await CreateExceptionFromResponseAsync(response);
        }

        var result = await response.Content.ReadFromJsonAsync<EffectProcessResponse>(_jsonOptions, cancellationToken);
        return result ?? throw new BackendDeserializationException("Failed to deserialize process response");
      });
    }

    // Effect presets
    public async Task<List<EffectPreset>> GetEffectPresetsAsync(string? effectType = null, CancellationToken cancellationToken = default)
    {
      return await ExecuteWithRetryAsync(async () =>
      {
        var url = "/api/effects/presets";
        if (!string.IsNullOrWhiteSpace(effectType))
        {
          url += $"?effect_type={Uri.EscapeDataString(effectType)}";
        }

        var response = await _httpClient.GetAsync(url, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
          throw await CreateExceptionFromResponseAsync(response);
        }

        var presets = await response.Content.ReadFromJsonAsync<List<EffectPreset>>(_jsonOptions, cancellationToken);
        return presets ?? new List<EffectPreset>();
      });
    }

    public async Task<EffectPreset> CreateEffectPresetAsync(EffectPreset preset, CancellationToken cancellationToken = default)
    {
      return await ExecuteWithRetryAsync(async () =>
      {
        var json = JsonSerializer.Serialize(preset, _jsonOptions);
        var content = new StringContent(json, Encoding.UTF8, "application/json");
        var response = await _httpClient.PostAsync("/api/effects/presets", content, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
          throw await CreateExceptionFromResponseAsync(response);
        }

        var createdPreset = await response.Content.ReadFromJsonAsync<EffectPreset>(_jsonOptions, cancellationToken);
        return createdPreset ?? throw new BackendDeserializationException("Failed to deserialize effect preset");
      });
    }

    public async Task<bool> DeleteEffectPresetAsync(string presetId, CancellationToken cancellationToken = default)
    {
      return await ExecuteWithRetryAsync(async () =>
      {
        var response = await _httpClient.DeleteAsync($"/api/effects/presets/{Uri.EscapeDataString(presetId)}", cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
          throw await CreateExceptionFromResponseAsync(response);
        }

        // Delete operations return success if status code is 200-299
        return response.IsSuccessStatusCode;
      });
    }

    // Batch processing
    public async Task<BatchJob> CreateBatchJobAsync(BatchJobRequest request, CancellationToken cancellationToken = default)
    {
      return await ExecuteWithRetryAsync(async () =>
      {
        var json = JsonSerializer.Serialize(request, _jsonOptions);
        var content = new StringContent(json, Encoding.UTF8, "application/json");
        var response = await _httpClient.PostAsync("/api/batch/jobs", content, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
          throw await CreateExceptionFromResponseAsync(response);
        }

        var job = await response.Content.ReadFromJsonAsync<BatchJob>(_jsonOptions, cancellationToken);
        return job ?? throw new BackendDeserializationException("Failed to deserialize batch job");
      });
    }

    public async Task<List<BatchJob>> GetBatchJobsAsync(string? projectId = null, JobStatus? status = null, CancellationToken cancellationToken = default)
    {
      return await ExecuteWithRetryAsync(async () =>
      {
        var url = "/api/batch/jobs?";
        var queryParams = new List<string>();
        if (!string.IsNullOrWhiteSpace(projectId))
          queryParams.Add($"project_id={Uri.EscapeDataString(projectId)}");
        if (status.HasValue)
          queryParams.Add($"status={status.Value.ToString().ToLowerInvariant()}");

        if (queryParams.Count > 0)
          url += string.Join("&", queryParams);
        else
          url = "/api/batch/jobs";

        var response = await _httpClient.GetAsync(url, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
          throw await CreateExceptionFromResponseAsync(response);
        }

        var jobs = await response.Content.ReadFromJsonAsync<List<BatchJob>>(_jsonOptions, cancellationToken);
        return jobs ?? new List<BatchJob>();
      });
    }

    public async Task<BatchJob> GetBatchJobAsync(string jobId, CancellationToken cancellationToken = default)
    {
      return await ExecuteWithRetryAsync(async () =>
      {
        var response = await _httpClient.GetAsync($"/api/batch/jobs/{Uri.EscapeDataString(jobId)}", cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
          throw await CreateExceptionFromResponseAsync(response);
        }

        var job = await response.Content.ReadFromJsonAsync<BatchJob>(_jsonOptions, cancellationToken);
        return job ?? throw new BackendDeserializationException("Failed to deserialize batch job");
      });
    }

    public async Task<bool> DeleteBatchJobAsync(string jobId, CancellationToken cancellationToken = default)
    {
      return await ExecuteWithRetryAsync(async () =>
      {
        var response = await _httpClient.DeleteAsync($"/api/batch/jobs/{Uri.EscapeDataString(jobId)}", cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
          throw await CreateExceptionFromResponseAsync(response);
        }

        // Delete operations return success if status code is 200-299
        return response.IsSuccessStatusCode;
      });
    }

    public async Task<BatchJob> StartBatchJobAsync(string jobId, CancellationToken cancellationToken = default)
    {
      return await ExecuteWithRetryAsync(async () =>
      {
        var response = await _httpClient.PostAsync($"/api/batch/jobs/{Uri.EscapeDataString(jobId)}/start", null, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
          throw await CreateExceptionFromResponseAsync(response);
        }

        var job = await response.Content.ReadFromJsonAsync<BatchJob>(_jsonOptions, cancellationToken);
        return job ?? throw new BackendDeserializationException("Failed to deserialize batch job");
      });
    }

    public async Task<BatchJob> CancelBatchJobAsync(string jobId, CancellationToken cancellationToken = default)
    {
      return await ExecuteWithRetryAsync(async () =>
      {
        var response = await _httpClient.PostAsync($"/api/batch/jobs/{Uri.EscapeDataString(jobId)}/cancel", null, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
          throw await CreateExceptionFromResponseAsync(response);
        }

        var job = await response.Content.ReadFromJsonAsync<BatchJob>(_jsonOptions, cancellationToken);
        return job ?? throw new BackendDeserializationException("Failed to deserialize batch job");
      });
    }

    public async Task<BatchQueueStatus> GetBatchQueueStatusAsync(CancellationToken cancellationToken = default)
    {
      return await ExecuteWithRetryAsync(async () =>
      {
        var response = await _httpClient.GetAsync("/api/batch/queue/status", cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
          throw await CreateExceptionFromResponseAsync(response);
        }

        var status = await response.Content.ReadFromJsonAsync<BatchQueueStatus>(_jsonOptions, cancellationToken);
        return status ?? throw new BackendDeserializationException("Failed to deserialize queue status");
      });
    }

    // Quality-Based Batch Processing endpoints (IDEA 57)
    public async Task<BatchQualityReport> GetBatchJobQualityAsync(string jobId, CancellationToken cancellationToken = default)
    {
      return await ExecuteWithRetryAsync(async () =>
      {
        var response = await _httpClient.GetAsync($"/api/batch/jobs/{Uri.EscapeDataString(jobId)}/quality", cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
          throw await CreateExceptionFromResponseAsync(response);
        }

        return await response.Content.ReadFromJsonAsync<BatchQualityReport>(_jsonOptions, cancellationToken)
                  ?? throw new BackendDeserializationException("Failed to deserialize batch quality report");
      });
    }

    public async Task<BatchQualityReport> GetBatchQualityReportAsync(string jobId, CancellationToken cancellationToken = default)
    {
      return await ExecuteWithRetryAsync(async () =>
      {
        var response = await _httpClient.GetAsync($"/api/batch/jobs/{Uri.EscapeDataString(jobId)}/quality-report", cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
          throw await CreateExceptionFromResponseAsync(response);
        }

        return await response.Content.ReadFromJsonAsync<BatchQualityReport>(_jsonOptions, cancellationToken)
                  ?? throw new BackendDeserializationException("Failed to deserialize batch quality report");
      });
    }

    public async Task<BatchQualityStatistics> GetBatchQualityStatisticsAsync(string? projectId = null, JobStatus? status = null, CancellationToken cancellationToken = default)
    {
      return await ExecuteWithRetryAsync(async () =>
      {
        var queryParams = new List<string>();
        if (!string.IsNullOrWhiteSpace(projectId))
          queryParams.Add($"project_id={Uri.EscapeDataString(projectId)}");
        if (status.HasValue)
          queryParams.Add($"status={status.Value.ToString().ToLowerInvariant()}");

        var url = "/api/batch/quality/statistics";
        if (queryParams.Count > 0)
          url += $"?{string.Join("&", queryParams)}";

        var response = await _httpClient.GetAsync(url, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
          throw await CreateExceptionFromResponseAsync(response);
        }

        return await response.Content.ReadFromJsonAsync<BatchQualityStatistics>(_jsonOptions, cancellationToken)
                  ?? throw new BackendDeserializationException("Failed to deserialize batch quality statistics");
      });
    }

    public async Task<BatchJob> RetryBatchJobWithQualityAsync(string jobId, BatchRetryWithQualityRequest request, CancellationToken cancellationToken = default)
    {
      return await ExecuteWithRetryAsync(async () =>
      {
        var response = await _httpClient.PostAsJsonAsync($"/api/batch/jobs/{Uri.EscapeDataString(jobId)}/retry-with-quality", request, _jsonOptions, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
          throw await CreateExceptionFromResponseAsync(response);
        }

        return await response.Content.ReadFromJsonAsync<BatchJob>(_jsonOptions, cancellationToken)
                  ?? throw new BackendDeserializationException("Failed to deserialize batch job");
      });
    }

    // Transcription
    public async Task<List<SupportedLanguage>> GetSupportedLanguagesAsync(CancellationToken cancellationToken = default)
    {
      return await ExecuteWithRetryAsync(async () =>
      {
        var response = await _httpClient.GetAsync("/api/transcribe/languages", cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
          throw await CreateExceptionFromResponseAsync(response);
        }

        return await response.Content.ReadFromJsonAsync<List<SupportedLanguage>>(_jsonOptions, cancellationToken)
                  ?? throw new BackendDeserializationException("Failed to deserialize supported languages");
      });
    }

    // GAP-CS-003: Dynamic engine discovery
    public async Task<List<TranscriptionEngine>> GetTranscriptionEnginesAsync(CancellationToken cancellationToken = default)
    {
      return await ExecuteWithRetryAsync(async () =>
      {
        var response = await _httpClient.GetAsync("/api/transcribe/engines", cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
          throw await CreateExceptionFromResponseAsync(response);
        }

        return await response.Content.ReadFromJsonAsync<List<TranscriptionEngine>>(_jsonOptions, cancellationToken)
                  ?? throw new BackendDeserializationException("Failed to deserialize transcription engines");
      });
    }

    public async Task<TranscriptionResponse> TranscribeAudioAsync(TranscriptionRequest request, string? projectId = null, CancellationToken cancellationToken = default)
    {
      return await ExecuteWithRetryAsync(async () =>
      {
        var url = "/api/transcribe/";
        if (!string.IsNullOrEmpty(projectId))
        {
          url += $"?project_id={Uri.EscapeDataString(projectId)}";
        }
        var response = await _httpClient.PostAsJsonAsync(url, request, _jsonOptions, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
          throw await CreateExceptionFromResponseAsync(response);
        }

        return await response.Content.ReadFromJsonAsync<TranscriptionResponse>(_jsonOptions, cancellationToken)
                  ?? throw new BackendDeserializationException("Failed to deserialize transcription response");
      });
    }

    public async Task<TranscriptionResponse> GetTranscriptionAsync(string transcriptionId, CancellationToken cancellationToken = default)
    {
      return await ExecuteWithRetryAsync(async () =>
      {
        var response = await _httpClient.GetAsync($"/api/transcribe/{Uri.EscapeDataString(transcriptionId)}", cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
          throw await CreateExceptionFromResponseAsync(response);
        }

        return await response.Content.ReadFromJsonAsync<TranscriptionResponse>(_jsonOptions, cancellationToken)
                  ?? throw new BackendDeserializationException("Failed to deserialize transcription");
      });
    }

    public async Task<List<TranscriptionResponse>> ListTranscriptionsAsync(string? audioId = null, string? projectId = null, CancellationToken cancellationToken = default)
    {
      return await ExecuteWithRetryAsync(async () =>
      {
        var queryParams = new List<string>();
        if (!string.IsNullOrEmpty(audioId))
        {
          queryParams.Add($"audio_id={Uri.EscapeDataString(audioId)}");
        }
        if (!string.IsNullOrEmpty(projectId))
        {
          queryParams.Add($"project_id={Uri.EscapeDataString(projectId)}");
        }
        var url = "/api/transcribe/";
        if (queryParams.Count > 0)
        {
          url += "?" + string.Join("&", queryParams);
        }
        var response = await _httpClient.GetAsync(url, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
          throw await CreateExceptionFromResponseAsync(response);
        }

        return await response.Content.ReadFromJsonAsync<List<TranscriptionResponse>>(_jsonOptions, cancellationToken)
                  ?? throw new BackendDeserializationException("Failed to deserialize transcriptions");
      });
    }

    public async Task<bool> DeleteTranscriptionAsync(string transcriptionId, CancellationToken cancellationToken = default)
    {
      return await ExecuteWithRetryAsync(async () =>
      {
        var response = await _httpClient.DeleteAsync($"/api/transcribe/{Uri.EscapeDataString(transcriptionId)}", cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
          throw await CreateExceptionFromResponseAsync(response);
        }

        return true;
      });
    }

    // Training
    public async Task<TrainingDataset> CreateDatasetAsync(string name, string? description = null, List<string>? audioFiles = null, CancellationToken cancellationToken = default)
    {
      return await ExecuteWithRetryAsync(async () =>
      {
        var requestBody = new
        {
          name,
          description = description ?? (object?)null,
          audio_files = audioFiles ?? new List<string>()
        };

        var response = await _httpClient.PostAsJsonAsync("/api/training/datasets", requestBody, _jsonOptions, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
          throw await CreateExceptionFromResponseAsync(response);
        }

        return await response.Content.ReadFromJsonAsync<TrainingDataset>(_jsonOptions, cancellationToken)
                  ?? throw new BackendDeserializationException("Failed to deserialize dataset");
      });
    }

    public async Task<List<TrainingDataset>> ListDatasetsAsync(CancellationToken cancellationToken = default)
    {
      return await ExecuteWithRetryAsync(async () =>
      {
        var response = await _httpClient.GetAsync("/api/training/datasets", cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
          throw await CreateExceptionFromResponseAsync(response);
        }

        return await response.Content.ReadFromJsonAsync<List<TrainingDataset>>(_jsonOptions, cancellationToken)
                  ?? throw new BackendDeserializationException("Failed to deserialize datasets");
      });
    }

    public async Task<TrainingDataset> GetDatasetAsync(string datasetId, CancellationToken cancellationToken = default)
    {
      return await ExecuteWithRetryAsync(async () =>
      {
        var response = await _httpClient.GetAsync($"/api/training/datasets/{Uri.EscapeDataString(datasetId)}", cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
          throw await CreateExceptionFromResponseAsync(response);
        }

        return await response.Content.ReadFromJsonAsync<TrainingDataset>(_jsonOptions, cancellationToken)
                  ?? throw new BackendDeserializationException("Failed to deserialize dataset");
      });
    }

    public async Task<bool> DeleteDatasetAsync(string datasetId, CancellationToken cancellationToken = default)
    {
      return await ExecuteWithRetryAsync(async () =>
      {
        var response = await _httpClient.DeleteAsync($"/api/training/datasets/{Uri.EscapeDataString(datasetId)}", cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
          throw await CreateExceptionFromResponseAsync(response);
        }

        return response.IsSuccessStatusCode;
      });
    }

    public async Task<TrainingStatus> StartTrainingAsync(TrainingRequest request, CancellationToken cancellationToken = default)
    {
      return await ExecuteWithRetryAsync(async () =>
      {
        var response = await _httpClient.PostAsJsonAsync("/api/training/start", request, _jsonOptions, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
          throw await CreateExceptionFromResponseAsync(response);
        }

        return await response.Content.ReadFromJsonAsync<TrainingStatus>(_jsonOptions, cancellationToken)
                  ?? throw new BackendDeserializationException("Failed to deserialize training status");
      });
    }

    public async Task<TrainingStatus> GetTrainingStatusAsync(string trainingId, CancellationToken cancellationToken = default)
    {
      return await ExecuteWithRetryAsync(async () =>
      {
        var response = await _httpClient.GetAsync($"/api/training/status/{Uri.EscapeDataString(trainingId)}", cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
          throw await CreateExceptionFromResponseAsync(response);
        }

        return await response.Content.ReadFromJsonAsync<TrainingStatus>(_jsonOptions, cancellationToken)
                  ?? throw new BackendDeserializationException("Failed to deserialize training status");
      });
    }

    public async Task<List<TrainingStatus>> ListTrainingJobsAsync(string? profileId = null, string? status = null, CancellationToken cancellationToken = default)
    {
      return await ExecuteWithRetryAsync(async () =>
      {
        var queryParams = new List<string>();
        if (!string.IsNullOrEmpty(profileId))
        {
          queryParams.Add($"profile_id={Uri.EscapeDataString(profileId)}");
        }
        if (!string.IsNullOrEmpty(status))
        {
          queryParams.Add($"status={Uri.EscapeDataString(status)}");
        }
        var url = "/api/training/status";
        if (queryParams.Count > 0)
        {
          url += "?" + string.Join("&", queryParams);
        }
        var response = await _httpClient.GetAsync(url, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
          throw await CreateExceptionFromResponseAsync(response);
        }

        return await response.Content.ReadFromJsonAsync<List<TrainingStatus>>(_jsonOptions, cancellationToken)
                  ?? throw new BackendDeserializationException("Failed to deserialize training jobs");
      });
    }

    public async Task<bool> CancelTrainingAsync(string trainingId, CancellationToken cancellationToken = default)
    {
      return await ExecuteWithRetryAsync(async () =>
      {
        var response = await _httpClient.PostAsync($"/api/training/cancel/{Uri.EscapeDataString(trainingId)}", null, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
          throw await CreateExceptionFromResponseAsync(response);
        }

        return true;
      });
    }

    public async Task<List<TrainingLogEntry>> GetTrainingLogsAsync(string trainingId, int? limit = null, CancellationToken cancellationToken = default)
    {
      return await ExecuteWithRetryAsync(async () =>
      {
        var url = $"/api/training/logs/{Uri.EscapeDataString(trainingId)}";
        if (limit.HasValue)
        {
          url += $"?limit={limit.Value}";
        }
        var response = await _httpClient.GetAsync(url, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
          throw await CreateExceptionFromResponseAsync(response);
        }

        return await response.Content.ReadFromJsonAsync<List<TrainingLogEntry>>(_jsonOptions, cancellationToken)
                  ?? throw new BackendDeserializationException("Failed to deserialize training logs");
      });
    }

    public async Task<bool> DeleteTrainingJobAsync(string trainingId, CancellationToken cancellationToken = default)
    {
      return await ExecuteWithRetryAsync(async () =>
      {
        var response = await _httpClient.DeleteAsync($"/api/training/{Uri.EscapeDataString(trainingId)}", cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
          throw await CreateExceptionFromResponseAsync(response);
        }

        return true;
      });
    }

    // Training quality monitoring (IDEA 54)
    public async Task<List<TrainingQualityMetrics>> GetTrainingQualityHistoryAsync(string trainingId, int? limit = null, CancellationToken cancellationToken = default)
    {
      return await ExecuteWithRetryAsync(async () =>
      {
        var url = $"/api/training/{Uri.EscapeDataString(trainingId)}/quality-history";
        if (limit.HasValue)
        {
          url += $"?limit={limit.Value}";
        }
        var response = await _httpClient.GetAsync(url, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
          throw await CreateExceptionFromResponseAsync(response);
        }

        return await response.Content.ReadFromJsonAsync<List<TrainingQualityMetrics>>(_jsonOptions, cancellationToken)
                  ?? new List<TrainingQualityMetrics>();
      });
    }

    // Multi-engine ensemble synthesis (IDEA 55)
    public async Task<MultiEngineEnsembleResponse> CreateMultiEngineEnsembleAsync(MultiEngineEnsembleRequest request, CancellationToken cancellationToken = default)
    {
      return await ExecuteWithRetryAsync(async () =>
      {
        var response = await _httpClient.PostAsJsonAsync("/api/ensemble/multi-engine", request, _jsonOptions, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
          throw await CreateExceptionFromResponseAsync(response);
        }

        return await response.Content.ReadFromJsonAsync<MultiEngineEnsembleResponse>(_jsonOptions, cancellationToken)
                  ?? throw new BackendDeserializationException("Failed to deserialize multi-engine ensemble response");
      });
    }

    public async Task<MultiEngineEnsembleStatus> GetMultiEngineEnsembleStatusAsync(string jobId, CancellationToken cancellationToken = default)
    {
      return await ExecuteWithRetryAsync(async () =>
      {
        var response = await _httpClient.GetAsync($"/api/ensemble/multi-engine/{Uri.EscapeDataString(jobId)}", cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
          throw await CreateExceptionFromResponseAsync(response);
        }

        return await response.Content.ReadFromJsonAsync<MultiEngineEnsembleStatus>(_jsonOptions, cancellationToken)
                  ?? throw new BackendDeserializationException("Failed to deserialize multi-engine ensemble status");
      });
    }

    // Training (interface aliases)
    public async Task<List<TrainingDataset>> GetTrainingDatasetsAsync(CancellationToken cancellationToken = default)
    {
      return await ListDatasetsAsync(cancellationToken);
    }

    public async Task<TrainingDataset> GetTrainingDatasetAsync(string datasetId, CancellationToken cancellationToken = default)
    {
      return await GetDatasetAsync(datasetId, cancellationToken);
    }

    public async Task<TrainingDataset> CreateTrainingDatasetAsync(string name, string? description = null, List<string>? audioFiles = null, CancellationToken cancellationToken = default)
    {
      return await CreateDatasetAsync(name, description, audioFiles, cancellationToken);
    }

    // Mixer management
    public async Task<MixerState> GetMixerStateAsync(string projectId, CancellationToken cancellationToken = default)
    {
      return await ExecuteWithRetryAsync(async () =>
      {
        var response = await _httpClient.GetAsync($"/api/mixer/state/{Uri.EscapeDataString(projectId)}", cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
          throw await CreateExceptionFromResponseAsync(response);
        }

        return await response.Content.ReadFromJsonAsync<MixerState>(_jsonOptions, cancellationToken)
                  ?? throw new BackendDeserializationException("Failed to deserialize mixer state");
      });
    }

    public async Task<MixerState> UpdateMixerStateAsync(string projectId, MixerState state, CancellationToken cancellationToken = default)
    {
      return await ExecuteWithRetryAsync(async () =>
      {
        var response = await _httpClient.PutAsJsonAsync($"/api/mixer/state/{Uri.EscapeDataString(projectId)}", state, _jsonOptions, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
          throw await CreateExceptionFromResponseAsync(response);
        }

        return await response.Content.ReadFromJsonAsync<MixerState>(_jsonOptions, cancellationToken)
                  ?? throw new BackendDeserializationException("Failed to deserialize mixer state");
      });
    }

    public async Task<MixerState> ResetMixerStateAsync(string projectId, CancellationToken cancellationToken = default)
    {
      return await ExecuteWithRetryAsync(async () =>
      {
        var response = await _httpClient.PostAsync($"/api/mixer/state/{Uri.EscapeDataString(projectId)}/reset", null, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
          throw await CreateExceptionFromResponseAsync(response);
        }

        return await response.Content.ReadFromJsonAsync<MixerState>(_jsonOptions, cancellationToken)
                  ?? throw new BackendDeserializationException("Failed to deserialize mixer state");
      });
    }

    // Mixer sends/returns (interface methods)
    public async Task<MixerSend> CreateMixerSendAsync(string projectId, MixerSend send, CancellationToken cancellationToken = default)
    {
      return await CreateSendAsync(projectId, send, cancellationToken);
    }

    public async Task<MixerSend> UpdateMixerSendAsync(string projectId, string sendId, MixerSend send, CancellationToken cancellationToken = default)
    {
      return await UpdateSendAsync(projectId, sendId, send, cancellationToken);
    }

    public async Task<bool> DeleteMixerSendAsync(string projectId, string sendId, CancellationToken cancellationToken = default)
    {
      return await DeleteSendAsync(projectId, sendId, cancellationToken);
    }

    public async Task<MixerReturn> CreateMixerReturnAsync(string projectId, MixerReturn returnBus, CancellationToken cancellationToken = default)
    {
      return await CreateReturnAsync(projectId, returnBus, cancellationToken);
    }

    public async Task<MixerReturn> UpdateMixerReturnAsync(string projectId, string returnId, MixerReturn returnBus, CancellationToken cancellationToken = default)
    {
      return await UpdateReturnAsync(projectId, returnId, returnBus, cancellationToken);
    }

    public async Task<bool> DeleteMixerReturnAsync(string projectId, string returnId, CancellationToken cancellationToken = default)
    {
      return await DeleteReturnAsync(projectId, returnId, cancellationToken);
    }

    public async Task<MixerSubGroup> CreateMixerSubGroupAsync(string projectId, MixerSubGroup subgroup, CancellationToken cancellationToken = default)
    {
      return await CreateSubGroupAsync(projectId, subgroup, cancellationToken);
    }

    public async Task<MixerSubGroup> UpdateMixerSubGroupAsync(string projectId, string subgroupId, MixerSubGroup subgroup, CancellationToken cancellationToken = default)
    {
      return await UpdateSubGroupAsync(projectId, subgroupId, subgroup, cancellationToken);
    }

    public async Task<bool> DeleteMixerSubGroupAsync(string projectId, string subgroupId, CancellationToken cancellationToken = default)
    {
      return await DeleteSubGroupAsync(projectId, subgroupId, cancellationToken);
    }

    public async Task<MixerMaster> UpdateMixerMasterAsync(string projectId, MixerMaster master, CancellationToken cancellationToken = default)
    {
      return await UpdateMasterAsync(projectId, master, cancellationToken);
    }

    public async Task<List<MixerPreset>> GetMixerPresetsAsync(string projectId, CancellationToken cancellationToken = default)
    {
      return await ListMixerPresetsAsync(projectId, cancellationToken);
    }

    // Mixer sends (implementation)
    public async Task<MixerSend> CreateSendAsync(string projectId, MixerSend send, CancellationToken cancellationToken = default)
    {
      return await ExecuteWithRetryAsync(async () =>
      {
        var response = await _httpClient.PostAsJsonAsync($"/api/mixer/state/{Uri.EscapeDataString(projectId)}/sends", send, _jsonOptions, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
          throw await CreateExceptionFromResponseAsync(response);
        }

        return await response.Content.ReadFromJsonAsync<MixerSend>(_jsonOptions, cancellationToken)
                  ?? throw new BackendDeserializationException("Failed to deserialize mixer send");
      });
    }

    public async Task<MixerSend> UpdateSendAsync(string projectId, string sendId, MixerSend send, CancellationToken cancellationToken = default)
    {
      return await ExecuteWithRetryAsync(async () =>
      {
        var response = await _httpClient.PutAsJsonAsync($"/api/mixer/state/{Uri.EscapeDataString(projectId)}/sends/{Uri.EscapeDataString(sendId)}", send, _jsonOptions, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
          throw await CreateExceptionFromResponseAsync(response);
        }

        return await response.Content.ReadFromJsonAsync<MixerSend>(_jsonOptions, cancellationToken)
                  ?? throw new BackendDeserializationException("Failed to deserialize mixer send");
      });
    }

    public async Task<bool> DeleteSendAsync(string projectId, string sendId, CancellationToken cancellationToken = default)
    {
      return await ExecuteWithRetryAsync(async () =>
      {
        var response = await _httpClient.DeleteAsync($"/api/mixer/state/{Uri.EscapeDataString(projectId)}/sends/{Uri.EscapeDataString(sendId)}", cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
          throw await CreateExceptionFromResponseAsync(response);
        }

        return true;
      });
    }

    // Mixer returns
    public async Task<MixerReturn> CreateReturnAsync(string projectId, MixerReturn returnBus, CancellationToken cancellationToken = default)
    {
      return await ExecuteWithRetryAsync(async () =>
      {
        var response = await _httpClient.PostAsJsonAsync($"/api/mixer/state/{Uri.EscapeDataString(projectId)}/returns", returnBus, _jsonOptions, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
          throw await CreateExceptionFromResponseAsync(response);
        }

        return await response.Content.ReadFromJsonAsync<MixerReturn>(_jsonOptions, cancellationToken)
                  ?? throw new BackendDeserializationException("Failed to deserialize mixer return");
      });
    }

    public async Task<MixerReturn> UpdateReturnAsync(string projectId, string returnId, MixerReturn returnBus, CancellationToken cancellationToken = default)
    {
      return await ExecuteWithRetryAsync(async () =>
      {
        var response = await _httpClient.PutAsJsonAsync($"/api/mixer/state/{Uri.EscapeDataString(projectId)}/returns/{Uri.EscapeDataString(returnId)}", returnBus, _jsonOptions, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
          throw await CreateExceptionFromResponseAsync(response);
        }

        return await response.Content.ReadFromJsonAsync<MixerReturn>(_jsonOptions, cancellationToken)
                  ?? throw new BackendDeserializationException("Failed to deserialize mixer return");
      });
    }

    public async Task<bool> DeleteReturnAsync(string projectId, string returnId, CancellationToken cancellationToken = default)
    {
      return await ExecuteWithRetryAsync(async () =>
      {
        var response = await _httpClient.DeleteAsync($"/api/mixer/state/{Uri.EscapeDataString(projectId)}/returns/{Uri.EscapeDataString(returnId)}", cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
          throw await CreateExceptionFromResponseAsync(response);
        }

        return true;
      });
    }

    // Mixer sub-groups
    public async Task<MixerSubGroup> CreateSubGroupAsync(string projectId, MixerSubGroup subGroup, CancellationToken cancellationToken = default)
    {
      return await ExecuteWithRetryAsync(async () =>
      {
        var response = await _httpClient.PostAsJsonAsync($"/api/mixer/state/{Uri.EscapeDataString(projectId)}/subgroups", subGroup, _jsonOptions, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
          throw await CreateExceptionFromResponseAsync(response);
        }

        return await response.Content.ReadFromJsonAsync<MixerSubGroup>(_jsonOptions, cancellationToken)
                  ?? throw new BackendDeserializationException("Failed to deserialize mixer sub-group");
      });
    }

    public async Task<MixerSubGroup> UpdateSubGroupAsync(string projectId, string subGroupId, MixerSubGroup subGroup, CancellationToken cancellationToken = default)
    {
      return await ExecuteWithRetryAsync(async () =>
      {
        var response = await _httpClient.PutAsJsonAsync($"/api/mixer/state/{Uri.EscapeDataString(projectId)}/subgroups/{Uri.EscapeDataString(subGroupId)}", subGroup, _jsonOptions, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
          throw await CreateExceptionFromResponseAsync(response);
        }

        return await response.Content.ReadFromJsonAsync<MixerSubGroup>(_jsonOptions, cancellationToken)
                  ?? throw new BackendDeserializationException("Failed to deserialize mixer sub-group");
      });
    }

    public async Task<bool> DeleteSubGroupAsync(string projectId, string subGroupId, CancellationToken cancellationToken = default)
    {
      return await ExecuteWithRetryAsync(async () =>
      {
        var response = await _httpClient.DeleteAsync($"/api/mixer/state/{Uri.EscapeDataString(projectId)}/subgroups/{Uri.EscapeDataString(subGroupId)}", cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
          throw await CreateExceptionFromResponseAsync(response);
        }

        return true;
      });
    }

    // Mixer master
    public async Task<MixerMaster> UpdateMasterAsync(string projectId, MixerMaster master, CancellationToken cancellationToken = default)
    {
      return await ExecuteWithRetryAsync(async () =>
      {
        var response = await _httpClient.PutAsJsonAsync($"/api/mixer/state/{Uri.EscapeDataString(projectId)}/master", master, _jsonOptions, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
          throw await CreateExceptionFromResponseAsync(response);
        }

        return await response.Content.ReadFromJsonAsync<MixerMaster>(_jsonOptions, cancellationToken)
                  ?? throw new BackendDeserializationException("Failed to deserialize mixer master");
      });
    }

    // Channel routing
    public async Task<ChannelRouting> UpdateChannelRoutingAsync(string projectId, string channelId, ChannelRouting routing, CancellationToken cancellationToken = default)
    {
      return await ExecuteWithRetryAsync(async () =>
      {
        var response = await _httpClient.PutAsJsonAsync($"/api/mixer/state/{Uri.EscapeDataString(projectId)}/channels/{Uri.EscapeDataString(channelId)}/routing", routing, _jsonOptions, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
          throw await CreateExceptionFromResponseAsync(response);
        }

        return await response.Content.ReadFromJsonAsync<ChannelRouting>(_jsonOptions, cancellationToken)
                  ?? throw new BackendDeserializationException("Failed to deserialize channel routing");
      });
    }

    // Mixer presets
    public async Task<List<MixerPreset>> ListMixerPresetsAsync(string projectId, CancellationToken cancellationToken = default)
    {
      return await ExecuteWithRetryAsync(async () =>
      {
        var response = await _httpClient.GetAsync($"/api/mixer/presets/{Uri.EscapeDataString(projectId)}", cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
          throw await CreateExceptionFromResponseAsync(response);
        }

        return await response.Content.ReadFromJsonAsync<List<MixerPreset>>(_jsonOptions, cancellationToken)
                  ?? throw new BackendDeserializationException("Failed to deserialize mixer presets");
      });
    }

    public async Task<MixerPreset> GetMixerPresetAsync(string projectId, string presetId, CancellationToken cancellationToken = default)
    {
      return await ExecuteWithRetryAsync(async () =>
      {
        var response = await _httpClient.GetAsync($"/api/mixer/presets/{Uri.EscapeDataString(projectId)}/{Uri.EscapeDataString(presetId)}", cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
          throw await CreateExceptionFromResponseAsync(response);
        }

        return await response.Content.ReadFromJsonAsync<MixerPreset>(_jsonOptions, cancellationToken)
                  ?? throw new BackendDeserializationException("Failed to deserialize mixer preset");
      });
    }

    public async Task<MixerPreset> CreateMixerPresetAsync(string projectId, MixerPreset preset, CancellationToken cancellationToken = default)
    {
      return await ExecuteWithRetryAsync(async () =>
      {
        var response = await _httpClient.PostAsJsonAsync($"/api/mixer/presets/{Uri.EscapeDataString(projectId)}", preset, _jsonOptions, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
          throw await CreateExceptionFromResponseAsync(response);
        }

        return await response.Content.ReadFromJsonAsync<MixerPreset>(_jsonOptions, cancellationToken)
                  ?? throw new BackendDeserializationException("Failed to deserialize mixer preset");
      });
    }

    public async Task<MixerPreset> UpdateMixerPresetAsync(string projectId, string presetId, MixerPreset preset, CancellationToken cancellationToken = default)
    {
      return await ExecuteWithRetryAsync(async () =>
      {
        var response = await _httpClient.PutAsJsonAsync($"/api/mixer/presets/{Uri.EscapeDataString(projectId)}/{Uri.EscapeDataString(presetId)}", preset, _jsonOptions, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
          throw await CreateExceptionFromResponseAsync(response);
        }

        return await response.Content.ReadFromJsonAsync<MixerPreset>(_jsonOptions, cancellationToken)
                  ?? throw new BackendDeserializationException("Failed to deserialize mixer preset");
      });
    }

    public async Task<bool> DeleteMixerPresetAsync(string projectId, string presetId, CancellationToken cancellationToken = default)
    {
      return await ExecuteWithRetryAsync(async () =>
      {
        var response = await _httpClient.DeleteAsync($"/api/mixer/presets/{Uri.EscapeDataString(projectId)}/{Uri.EscapeDataString(presetId)}", cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
          throw await CreateExceptionFromResponseAsync(response);
        }

        return true;
      });
    }

    public async Task<MixerState> ApplyMixerPresetAsync(string projectId, string presetId, CancellationToken cancellationToken = default)
    {
      return await ExecuteWithRetryAsync(async () =>
      {
        var response = await _httpClient.PostAsync($"/api/mixer/presets/{Uri.EscapeDataString(projectId)}/{Uri.EscapeDataString(presetId)}/apply", null, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
          throw await CreateExceptionFromResponseAsync(response);
        }

        return await response.Content.ReadFromJsonAsync<MixerState>(_jsonOptions, cancellationToken)
                  ?? throw new BackendDeserializationException("Failed to deserialize mixer state");
      });
    }

    // Video Generation
    public async Task<VideoGenerateResponse> GenerateVideoAsync(
        VideoGenerateRequest request,
        CancellationToken cancellationToken = default)
    {
      return await ExecuteWithRetryAsync(async () =>
      {
        var response = await _httpClient.PostAsJsonAsync(
                  "/api/video/generate",
                  request,
                  _jsonOptions,
                  cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
          throw await CreateExceptionFromResponseAsync(response);
        }

        return await response.Content.ReadFromJsonAsync<VideoGenerateResponse>(_jsonOptions, cancellationToken)
                  ?? throw new BackendDeserializationException("Failed to deserialize video generation response");
      });
    }

    public async Task<VideoUpscaleResponse> UpscaleVideoAsync(
        VideoUpscaleRequest request,
        CancellationToken cancellationToken = default)
    {
      return await ExecuteWithRetryAsync(async () =>
      {
        var response = await _httpClient.PostAsJsonAsync(
                  "/api/video/upscale",
                  request,
                  _jsonOptions,
                  cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
          throw await CreateExceptionFromResponseAsync(response);
        }

        return await response.Content.ReadFromJsonAsync<VideoUpscaleResponse>(_jsonOptions, cancellationToken)
                  ?? throw new BackendDeserializationException("Failed to deserialize video upscale response");
      });
    }

    public async Task<List<string>> ListVideoEnginesAsync(CancellationToken cancellationToken = default)
    {
      return await ExecuteWithRetryAsync(async () =>
      {
        var response = await _httpClient.GetAsync("/api/video/engines/list", cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
          throw await CreateExceptionFromResponseAsync(response);
        }

        var result = await response.Content.ReadFromJsonAsync<VideoEnginesListResponse>(_jsonOptions, cancellationToken)
                  ?? throw new BackendDeserializationException("Failed to deserialize video engines list");

        return result.Engines ?? new List<string>();
      });
    }

    public async Task<Stream> GetVideoAsync(string videoId, CancellationToken cancellationToken = default)
    {
      return await ExecuteWithRetryAsync(async () =>
      {
        var response = await _httpClient.GetAsync($"/api/video/{Uri.EscapeDataString(videoId)}", cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
          throw await CreateExceptionFromResponseAsync(response);
        }

        return await response.Content.ReadAsStreamAsync(cancellationToken);
      });
    }

    public async Task<VoiceConvertResponse> ConvertVoiceAsync(
        VoiceConvertRequest request,
        CancellationToken cancellationToken = default)
    {
      return await ExecuteWithRetryAsync(async () =>
      {
        if (request.AudioData == null || request.AudioData.Length == 0)
        {
          throw new ArgumentException("Audio data is required for voice conversion", nameof(request));
        }

        using var content = new MultipartFormDataContent();

        // Add audio file
        var audioContent = new ByteArrayContent(request.AudioData);
        audioContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("audio/wav");
        content.Add(audioContent, "audio_file", request.AudioFileName ?? "audio.wav");

        // Add engine parameter
        content.Add(new StringContent(request.Engine), "engine");

        // Add target_voice_id if provided
        if (!string.IsNullOrEmpty(request.TargetVoiceId))
        {
          content.Add(new StringContent(request.TargetVoiceId), "target_voice_id");
        }

        var response = await _httpClient.PostAsync("/api/video/voice/convert", content, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
          throw await CreateExceptionFromResponseAsync(response);
        }

        var result = await response.Content.ReadFromJsonAsync<Dictionary<string, object>>(_jsonOptions, cancellationToken)
                  ?? throw new BackendDeserializationException("Failed to deserialize voice conversion response");

        // Map the response dictionary to VoiceConvertResponse
        return new VoiceConvertResponse
        {
          AudioId = result.ContainsKey("audio_id") ? result["audio_id"]?.ToString() ?? string.Empty : string.Empty,
          AudioUrl = result.ContainsKey("audio_url") ? result["audio_url"]?.ToString() ?? string.Empty : string.Empty,
          Format = "wav"
        };
      });
    }

    /// <summary>
    /// Generic GET request helper method.
    /// </summary>
    public async Task<T?> GetAsync<T>(string endpoint, CancellationToken cancellationToken = default) where T : class
    {
      return await ExecuteWithRetryAsync(async () =>
      {
        var response = await _httpClient.GetAsync(endpoint, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
          throw await CreateExceptionFromResponseAsync(response);
        }

        try
        {
          return await response.Content.ReadFromJsonAsync<T>(_jsonOptions, cancellationToken);
        }
        catch (JsonException ex)
        {
          throw new BackendDeserializationException(
                    "The backend returned an invalid response format.",
                    ex);
        }
      });
    }

    /// <summary>
    /// Generic POST request helper method.
    /// </summary>
    public async Task<TResponse> PostAsync<TRequest, TResponse>(
        string endpoint,
        TRequest request,
        CancellationToken cancellationToken = default)
    {
      return await ExecuteWithRetryAsync(async () =>
      {
        var response = await _httpClient.PostAsJsonAsync(endpoint, request, _jsonOptions, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
          throw await CreateExceptionFromResponseAsync(response);
        }

        try
        {
          var result = await response.Content.ReadFromJsonAsync<TResponse>(_jsonOptions, cancellationToken);
          if (result == null)
          {
            throw new BackendDeserializationException("Failed to deserialize response: result was null");
          }
          return result;
        }
        catch (JsonException ex)
        {
          throw new BackendDeserializationException(
                    "The backend returned an invalid response format.",
                    ex);
        }
      });
    }

    /// <summary>
    /// Generic POST request helper method (void response).
    /// </summary>
    public async Task PostAsync<TRequest>(
        string endpoint,
        TRequest request,
        CancellationToken cancellationToken = default)
    {
      await ExecuteWithRetryAsync<bool>(async () =>
      {
        var response = await _httpClient.PostAsJsonAsync(endpoint, request, _jsonOptions, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
          throw await CreateExceptionFromResponseAsync(response);
        }

        return true;
      });
    }

    /// <summary>
    /// Generic PUT request helper method.
    /// </summary>
    public async Task<TResponse> PutAsync<TRequest, TResponse>(
        string endpoint,
        TRequest request,
        CancellationToken cancellationToken = default)
    {
      return await ExecuteWithRetryAsync(async () =>
      {
        var response = await _httpClient.PutAsJsonAsync(endpoint, request, _jsonOptions, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
          throw await CreateExceptionFromResponseAsync(response);
        }

        try
        {
          return await response.Content.ReadFromJsonAsync<TResponse>(_jsonOptions, cancellationToken)
                    ?? throw new BackendDeserializationException("Failed to deserialize response");
        }
        catch (JsonException ex)
        {
          throw new BackendDeserializationException(
                    "The backend returned an invalid response format.",
                    ex);
        }
      });
    }

    // Video Editing
    /// <summary>
    /// Get video information (duration, dimensions, FPS, format).
    /// </summary>
    public async Task<VideoInfo> GetVideoInfoAsync(
        string videoPath,
        CancellationToken cancellationToken = default)
    {
      return await ExecuteWithRetryAsync(async () =>
      {
        var response = await _httpClient.GetAsync(
                  $"/api/video/edit/info?path={Uri.EscapeDataString(videoPath)}",
                  cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
          throw await CreateExceptionFromResponseAsync(response);
        }

        try
        {
          return await response.Content.ReadFromJsonAsync<VideoInfo>(_jsonOptions, cancellationToken)
                    ?? throw new BackendDeserializationException("Failed to deserialize video info");
        }
        catch (JsonException ex)
        {
          throw new BackendDeserializationException(
                    "The backend returned an invalid response format for video info.",
                    ex);
        }
      });
    }

    /// <summary>
    /// Edit video using the video editing API.
    /// </summary>
    public async Task<VideoEditResponse> EditVideoAsync(
        VideoEditRequest request,
        CancellationToken cancellationToken = default)
    {
      return await ExecuteWithRetryAsync(async () =>
      {
        var response = await _httpClient.PostAsJsonAsync(
                  "/api/video/edit",
                  request,
                  _jsonOptions,
                  cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
          throw await CreateExceptionFromResponseAsync(response);
        }

        try
        {
          return await response.Content.ReadFromJsonAsync<VideoEditResponse>(_jsonOptions, cancellationToken)
                    ?? throw new BackendDeserializationException("Failed to deserialize video edit response");
        }
        catch (JsonException ex)
        {
          throw new BackendDeserializationException(
                    "The backend returned an invalid response format for video editing.",
                    ex);
        }
      });
    }

    // Backup and restore
    public async Task<List<BackupInfo>> GetBackupsAsync(CancellationToken cancellationToken = default)
    {
      return await ExecuteWithRetryAsync(async () =>
      {
        var response = await _httpClient.GetAsync("/api/backup", cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
          throw await CreateExceptionFromResponseAsync(response);
        }

        return await response.Content.ReadFromJsonAsync<List<BackupInfo>>(_jsonOptions, cancellationToken)
                  ?? new List<BackupInfo>();
      });
    }

    public async Task<BackupInfo> GetBackupAsync(string backupId, CancellationToken cancellationToken = default)
    {
      return await ExecuteWithRetryAsync(async () =>
      {
        var response = await _httpClient.GetAsync($"/api/backup/{Uri.EscapeDataString(backupId)}", cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
          throw await CreateExceptionFromResponseAsync(response);
        }

        return await response.Content.ReadFromJsonAsync<BackupInfo>(_jsonOptions, cancellationToken)
                  ?? throw new BackendDeserializationException("Failed to deserialize backup info");
      });
    }

    public async Task<BackupInfo> CreateBackupAsync(BackupCreateRequest request, CancellationToken cancellationToken = default)
    {
      return await ExecuteWithRetryAsync(async () =>
      {
        var response = await _httpClient.PostAsJsonAsync("/api/backup", request, _jsonOptions, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
          throw await CreateExceptionFromResponseAsync(response);
        }

        return await response.Content.ReadFromJsonAsync<BackupInfo>(_jsonOptions, cancellationToken)
                  ?? throw new BackendDeserializationException("Failed to deserialize backup info");
      });
    }

    public async Task<Stream> DownloadBackupAsync(string backupId, CancellationToken cancellationToken = default)
    {
      return await ExecuteWithRetryAsync(async () =>
      {
        var response = await _httpClient.GetAsync($"/api/backup/{Uri.EscapeDataString(backupId)}/download", cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
          throw await CreateExceptionFromResponseAsync(response);
        }

        return await response.Content.ReadAsStreamAsync(cancellationToken);
      });
    }

    public async Task<RestoreResponse> RestoreBackupAsync(string backupId, RestoreRequest request, CancellationToken cancellationToken = default)
    {
      return await ExecuteWithRetryAsync(async () =>
      {
        var response = await _httpClient.PostAsJsonAsync($"/api/backup/{Uri.EscapeDataString(backupId)}/restore", request, _jsonOptions, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
          throw await CreateExceptionFromResponseAsync(response);
        }

        return await response.Content.ReadFromJsonAsync<RestoreResponse>(_jsonOptions, cancellationToken)
                  ?? throw new BackendDeserializationException("Failed to deserialize restore response");
      });
    }

    public async Task<BackupInfo> UploadBackupAsync(Stream backupFile, string? name = null, CancellationToken cancellationToken = default)
    {
      return await ExecuteWithRetryAsync(async () =>
      {
        using var content = new MultipartFormDataContent();
        using var streamContent = new StreamContent(backupFile);
        content.Add(streamContent, "file", "backup.zip");

        var url = "/api/backup/upload";
        if (!string.IsNullOrEmpty(name))
        {
          url += $"?name={Uri.EscapeDataString(name)}";
        }

        var response = await _httpClient.PostAsync(url, content, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
          throw await CreateExceptionFromResponseAsync(response);
        }

        return await response.Content.ReadFromJsonAsync<BackupInfo>(_jsonOptions, cancellationToken)
                  ?? throw new BackendDeserializationException("Failed to deserialize backup info");
      });
    }

    public async Task<bool> DeleteBackupAsync(string backupId, CancellationToken cancellationToken = default)
    {
      return await ExecuteWithRetryAsync(async () =>
      {
        var response = await _httpClient.DeleteAsync($"/api/backup/{Uri.EscapeDataString(backupId)}", cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
          throw await CreateExceptionFromResponseAsync(response);
        }

        return true;
      });
    }

    // Settings management
    public async Task<SettingsData> GetSettingsAsync(CancellationToken cancellationToken = default)
    {
      return await ExecuteWithRetryAsync(async () =>
      {
        var response = await _httpClient.GetAsync("/api/settings", cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
          throw await CreateExceptionFromResponseAsync(response);
        }

        return await response.Content.ReadFromJsonAsync<SettingsData>(_jsonOptions, cancellationToken)
                  ?? throw new BackendDeserializationException("Failed to deserialize settings");
      });
    }

    public async Task<T?> GetSettingsCategoryAsync<T>(string category, CancellationToken cancellationToken = default) where T : class
    {
      return await ExecuteWithRetryAsync(async () =>
      {
        var response = await _httpClient.GetAsync($"/api/settings/{Uri.EscapeDataString(category)}", cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
          throw await CreateExceptionFromResponseAsync(response);
        }

        return await response.Content.ReadFromJsonAsync<T>(_jsonOptions, cancellationToken);
      });
    }

    public async Task<SettingsData> SaveSettingsAsync(SettingsData settings, CancellationToken cancellationToken = default)
    {
      return await ExecuteWithRetryAsync(async () =>
      {
        var response = await _httpClient.PostAsJsonAsync("/api/settings", settings, _jsonOptions, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
          throw await CreateExceptionFromResponseAsync(response);
        }

        return await response.Content.ReadFromJsonAsync<SettingsData>(_jsonOptions, cancellationToken)
                  ?? throw new BackendDeserializationException("Failed to deserialize settings");
      });
    }

    public async Task<T> UpdateSettingsCategoryAsync<T>(string category, T categorySettings, CancellationToken cancellationToken = default) where T : class
    {
      return await ExecuteWithRetryAsync(async () =>
      {
        var response = await _httpClient.PutAsJsonAsync($"/api/settings/{Uri.EscapeDataString(category)}", categorySettings, _jsonOptions, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
          throw await CreateExceptionFromResponseAsync(response);
        }

        return await response.Content.ReadFromJsonAsync<T>(_jsonOptions, cancellationToken)
                  ?? throw new BackendDeserializationException("Failed to deserialize settings category");
      });
    }

    public async Task<SettingsData> ResetSettingsAsync(CancellationToken cancellationToken = default)
    {
      return await ExecuteWithRetryAsync(async () =>
      {
        var response = await _httpClient.PostAsync("/api/settings/reset", null, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
          throw await CreateExceptionFromResponseAsync(response);
        }

        return await response.Content.ReadFromJsonAsync<SettingsData>(_jsonOptions, cancellationToken)
                  ?? throw new BackendDeserializationException("Failed to deserialize settings");
      });
    }

    // Helper methods for SettingsService compatibility
    // GetAsync, PostAsync, PutAsync are already defined above - duplicates removed

    // Quality management endpoints
    public async Task<Dictionary<string, QualityPresetInfo>> GetQualityPresetsAsync(CancellationToken cancellationToken = default)
    {
      return await GetAsync<Dictionary<string, QualityPresetInfo>>("/api/quality/presets", cancellationToken)
          ?? new Dictionary<string, QualityPresetInfo>();
    }

    public async Task<QualityPresetInfo> GetQualityPresetAsync(string presetName, CancellationToken cancellationToken = default)
    {
      return await GetAsync<QualityPresetInfo>($"/api/quality/presets/{Uri.EscapeDataString(presetName)}", cancellationToken)
          ?? throw new BackendDeserializationException("Failed to deserialize quality preset");
    }

    public async Task<QualityAnalysisResponse> AnalyzeQualityAsync(QualityAnalysisRequest request, CancellationToken cancellationToken = default)
    {
      return await PostAsync<QualityAnalysisRequest, QualityAnalysisResponse>("/api/quality/analyze", request, cancellationToken);
    }

    public async Task<QualityOptimizationResponse> OptimizeQualityAsync(QualityOptimizationRequest request, CancellationToken cancellationToken = default)
    {
      return await PostAsync<QualityOptimizationRequest, QualityOptimizationResponse>("/api/quality/optimize", request, cancellationToken);
    }

    public async Task<QualityComparisonResponse> CompareQualityAsync(QualityComparisonRequest request, CancellationToken cancellationToken = default)
    {
      return await PostAsync<QualityComparisonRequest, QualityComparisonResponse>("/api/quality/compare", request, cancellationToken);
    }

    public async Task<List<string>> GetEnginesAsync(CancellationToken cancellationToken = default)
    {
      return await ExecuteWithRetryAsync(async () =>
      {
        var response = await _httpClient.GetAsync("/api/engines/list", cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
          throw await CreateExceptionFromResponseAsync(response);
        }

        var result = await response.Content.ReadFromJsonAsync<EnginesListResponse>(_jsonOptions, cancellationToken)
                  ?? throw new BackendDeserializationException("Failed to deserialize engines list");

        return result.Engines ?? new List<string>();
      });
    }

    public async Task<EngineRecommendationResponse> GetEngineRecommendationAsync(EngineRecommendationRequest request, CancellationToken cancellationToken = default)
    {
      // Use POST endpoint for engine recommendations
      const string url = "/api/engines/recommend";
      return await PostAsync<EngineRecommendationRequest, EngineRecommendationResponse>(url, request, cancellationToken)
          ?? throw new BackendDeserializationException("Failed to deserialize engine recommendation");
    }

    public async Task<ABTestResponse> RunABTestAsync(ABTestRequest request, CancellationToken cancellationToken = default)
    {
      const string url = "/api/voice/ab-test";
      return await PostAsync<ABTestRequest, ABTestResponse>(url, request, cancellationToken)
          ?? throw new BackendDeserializationException("Failed to deserialize A/B test response");
    }

    public async Task<BenchmarkResponse> RunBenchmarkAsync(BenchmarkRequest request, CancellationToken cancellationToken = default)
    {
      const string url = "/api/quality/benchmark";
      return await PostAsync<BenchmarkRequest, BenchmarkResponse>(url, request, cancellationToken)
          ?? throw new BackendDeserializationException("Failed to deserialize benchmark response");
    }

    public async Task<SearchResponse> SearchAsync(string query, string? types = null, int limit = 50, CancellationToken cancellationToken = default)
    {
      var queryParams = new List<string> { $"q={Uri.EscapeDataString(query)}", $"limit={limit}" };
      if (!string.IsNullOrEmpty(types))
      {
        queryParams.Add($"types={Uri.EscapeDataString(types)}");
      }

      var url = $"/api/search?{string.Join("&", queryParams)}";
      return await GetAsync<SearchResponse>(url, cancellationToken)
          ?? throw new BackendDeserializationException("Failed to deserialize search response");
    }

    // Emotion preset management
    public async Task<List<EmotionPreset>> GetEmotionPresetsAsync(CancellationToken cancellationToken = default)
    {
      return await ExecuteWithRetryAsync(async () =>
      {
        var response = await _httpClient.GetAsync("/api/emotion/preset/list", cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
          throw await CreateExceptionFromResponseAsync(response);
        }

        var presets = await response.Content.ReadFromJsonAsync<List<EmotionPreset>>(_jsonOptions, cancellationToken);
        return presets ?? new List<EmotionPreset>();
      });
    }

    public async Task<EmotionPreset> GetEmotionPresetAsync(string presetId, CancellationToken cancellationToken = default)
    {
      return await ExecuteWithRetryAsync(async () =>
      {
        var response = await _httpClient.GetAsync($"/api/emotion/preset/{Uri.EscapeDataString(presetId)}", cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
          throw await CreateExceptionFromResponseAsync(response);
        }

        var preset = await response.Content.ReadFromJsonAsync<EmotionPreset>(_jsonOptions, cancellationToken);
        return preset ?? throw new BackendDeserializationException("Failed to deserialize emotion preset");
      });
    }

    public async Task<EmotionPreset> CreateEmotionPresetAsync(EmotionPresetCreateRequest request, CancellationToken cancellationToken = default)
    {
      return await ExecuteWithRetryAsync(async () =>
      {
        var json = JsonSerializer.Serialize(request, _jsonOptions);
        var content = new StringContent(json, Encoding.UTF8, "application/json");
        var response = await _httpClient.PostAsync("/api/emotion/preset/save", content, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
          throw await CreateExceptionFromResponseAsync(response);
        }

        var preset = await response.Content.ReadFromJsonAsync<EmotionPreset>(_jsonOptions, cancellationToken);
        return preset ?? throw new BackendDeserializationException("Failed to deserialize emotion preset");
      });
    }

    public async Task<EmotionPreset> UpdateEmotionPresetAsync(string presetId, EmotionPresetUpdateRequest request, CancellationToken cancellationToken = default)
    {
      return await ExecuteWithRetryAsync(async () =>
      {
        var json = JsonSerializer.Serialize(request, _jsonOptions);
        var content = new StringContent(json, Encoding.UTF8, "application/json");
        var response = await _httpClient.PutAsync($"/api/emotion/preset/{Uri.EscapeDataString(presetId)}", content, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
          throw await CreateExceptionFromResponseAsync(response);
        }

        var preset = await response.Content.ReadFromJsonAsync<EmotionPreset>(_jsonOptions, cancellationToken);
        return preset ?? throw new BackendDeserializationException("Failed to deserialize emotion preset");
      });
    }

    public async Task<bool> DeleteEmotionPresetAsync(string presetId, CancellationToken cancellationToken = default)
    {
      return await ExecuteWithRetryAsync(async () =>
      {
        var response = await _httpClient.DeleteAsync($"/api/emotion/preset/{Uri.EscapeDataString(presetId)}", cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
          throw await CreateExceptionFromResponseAsync(response);
        }

        return true;
      });
    }

    public async Task<List<string>> GetAvailableEmotionsAsync(CancellationToken cancellationToken = default)
    {
      return await ExecuteWithRetryAsync(async () =>
      {
        var response = await _httpClient.GetAsync("/api/emotion/list", cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
          throw await CreateExceptionFromResponseAsync(response);
        }

        var emotions = await response.Content.ReadFromJsonAsync<List<string>>(_jsonOptions, cancellationToken);
        return emotions ?? new List<string>();
      });
    }

    // Quality History endpoints (IDEA 30)
    public async Task<QualityHistoryEntry> StoreQualityHistoryAsync(QualityHistoryRequest request, CancellationToken cancellationToken = default)
    {
      return await PostAsync<QualityHistoryRequest, QualityHistoryEntry>("/api/quality/history", request, cancellationToken)
          ?? throw new BackendDeserializationException("Failed to deserialize quality history entry");
    }

    public async Task<List<QualityHistoryEntry>> GetQualityHistoryAsync(string profileId, int? limit = null, string? startDate = null, string? endDate = null, CancellationToken cancellationToken = default)
    {
      var queryParams = new List<string>();
      if (limit.HasValue)
      {
        queryParams.Add($"limit={limit.Value}");
      }
      if (!string.IsNullOrEmpty(startDate))
      {
        queryParams.Add($"start_date={Uri.EscapeDataString(startDate)}");
      }
      if (!string.IsNullOrEmpty(endDate))
      {
        queryParams.Add($"end_date={Uri.EscapeDataString(endDate)}");
      }

      var url = $"/api/quality/history/{Uri.EscapeDataString(profileId)}";
      if (queryParams.Count > 0)
      {
        url += $"?{string.Join("&", queryParams)}";
      }

      var response = await GetAsync<QualityHistoryResponse>(url, cancellationToken);
      return response?.Entries ?? new List<QualityHistoryEntry>();
    }

    public async Task<QualityTrends> GetQualityTrendsAsync(string profileId, string timeRange = "30d", CancellationToken cancellationToken = default)
    {
      var url = $"/api/quality/history/{Uri.EscapeDataString(profileId)}/trends?time_range={Uri.EscapeDataString(timeRange)}";
      return await GetAsync<QualityTrends>(url, cancellationToken)
          ?? throw new BackendDeserializationException("Failed to deserialize quality trends");
    }

    // Quality Degradation Detection endpoints (IDEA 56)
    public async Task<QualityDegradationResponse?> GetQualityDegradationAsync(string profileId, int timeWindowDays = 7, double degradationThresholdPercent = 10.0, double criticalThresholdPercent = 25.0, CancellationToken cancellationToken = default)
    {
      var url = $"/api/quality/degradation/{Uri.EscapeDataString(profileId)}?time_window_days={timeWindowDays}&degradation_threshold_percent={degradationThresholdPercent}&critical_threshold_percent={criticalThresholdPercent}";
      return await GetAsync<QualityDegradationResponse>(url, cancellationToken);
    }

    public async Task<QualityBaseline?> GetQualityBaselineAsync(string profileId, int timePeriodDays = 30, CancellationToken cancellationToken = default)
    {
      var url = $"/api/quality/baseline/{Uri.EscapeDataString(profileId)}?time_period_days={timePeriodDays}";
      return await GetAsync<QualityBaseline>(url, cancellationToken);
    }

    public async Task<QualityTrend> GetQualityTrendAsync(string profileId, int days = 30, CancellationToken cancellationToken = default)
    {
      // Convert days to time range string (backend expects "7d", "30d", "90d", "1y", "all")
      string timeRange = days switch
      {
        <= 7 => "7d",
        <= 30 => "30d",
        <= 90 => "90d",
        <= 365 => "1y",
        _ => "all"
      };

      // Get full trends data and compute simplified trend
      var trends = await GetQualityTrendsAsync(profileId, timeRange, cancellationToken);

      // Compute simplified trend from full trends data
      QualityTrend trend = QualityTrend.Stable;

      if (trends.Statistics?.Count > 0)
      {
        // Calculate overall trend from quality_score if available
        if (trends.Statistics.TryGetValue("quality_score", out var qualityStats))
        {
          var trendValue = qualityStats.Trend;

          if (trendValue > 0.01)
          {
            trend = QualityTrend.Improving;
          }
          else if (trendValue < -0.01)
          {
            trend = QualityTrend.Degrading;
          }
          else
          {
            trend = QualityTrend.Stable;
          }
        }
      }

      return trend;
    }

    // Quality Dashboard endpoint (IDEA 49)
    public async Task<QualityDashboard> GetQualityDashboardAsync(string? projectId = null, int days = 30, CancellationToken cancellationToken = default)
    {
      var queryParams = new List<string> { $"days={days}" };
      if (!string.IsNullOrEmpty(projectId))
      {
        queryParams.Add($"project_id={Uri.EscapeDataString(projectId)}");
      }

      var url = $"/api/quality/dashboard?{string.Join("&", queryParams)}";
      return await GetAsync<QualityDashboard>(url, cancellationToken)
          ?? throw new BackendDeserializationException("Failed to deserialize quality dashboard");
    }

    // Adaptive Quality Optimization endpoints (IDEA 53)
    public async Task<TextAnalysisResult> AnalyzeTextAsync(string text, string language = "en", CancellationToken cancellationToken = default)
    {
      var request = new TextAnalysisRequest
      {
        Text = text,
        Language = language
      };
      return await PostAsync<TextAnalysisRequest, TextAnalysisResult>("/api/quality/analyze-text", request, cancellationToken)
          ?? throw new BackendDeserializationException("Failed to deserialize text analysis result");
    }

    public async Task<QualityRecommendation> GetQualityRecommendationAsync(string text, string language = "en", List<string>? availableEngines = null, double? targetQuality = null, CancellationToken cancellationToken = default)
    {
      var request = new QualityRecommendationRequest
      {
        Text = text,
        Language = language,
        AvailableEngines = availableEngines,
        TargetQuality = targetQuality
      };
      return await PostAsync<QualityRecommendationRequest, QualityRecommendation>("/api/quality/recommend-quality", request, cancellationToken)
          ?? throw new BackendDeserializationException("Failed to deserialize quality recommendation");
    }

    // Engine-Specific Quality Pipelines endpoints (IDEA 58)
    public async Task<List<string>> ListQualityPipelinePresetsAsync(string engineId, CancellationToken cancellationToken = default)
    {
      var url = $"/api/quality/pipelines/engines/{Uri.EscapeDataString(engineId)}/presets";
      var presets = await GetAsync<List<string>>(url, cancellationToken);
      return presets ?? new List<string>();
    }

    public async Task<PipelineConfiguration?> GetQualityPipelineAsync(string engineId, string presetName, CancellationToken cancellationToken = default)
    {
      return await ExecuteWithRetryAsync(async () =>
      {
        var url = $"/api/quality/pipelines/engines/{Uri.EscapeDataString(engineId)}/presets/{Uri.EscapeDataString(presetName)}";
        var config = await GetAsync<PipelineConfiguration>(url, cancellationToken);

        if (config == null)
        {
          return null;
        }

        // Ensure EngineId is set
        if (string.IsNullOrEmpty(config.EngineId))
        {
          config.EngineId = engineId;
        }

        return config;
      });
    }

    // Legacy method for backward compatibility - converts to QualityPipeline
    public async Task<List<QualityPipeline>> GetQualityPipelinesAsync(string engineId, CancellationToken cancellationToken = default)
    {
      return await ExecuteWithRetryAsync(async () =>
      {
        // Get list of preset names first
        var presetNames = await ListQualityPipelinePresetsAsync(engineId, cancellationToken);

        if (presetNames == null || presetNames.Count == 0)
        {
          return new List<QualityPipeline>();
        }

        // Get each pipeline configuration and convert to QualityPipeline
        var pipelines = new List<QualityPipeline>();
        foreach (var presetName in presetNames)
        {
          try
          {
            var config = await GetQualityPipelineAsync(engineId, presetName, cancellationToken);
            if (config != null)
            {
              // Convert PipelineConfiguration to QualityPipeline
              var steps = new List<PipelineStep>();
              foreach (var stepName in config.Steps)
              {
                var stepParams = new Dictionary<string, object>();
                if (config.Settings.ContainsKey(stepName) && config.Settings[stepName] is Dictionary<string, object> stepDict)
                {
                  stepParams = stepDict;
                }

                steps.Add(new PipelineStep
                {
                  Name = stepName,
                  Enabled = true,
                  Parameters = stepParams
                });
              }

              pipelines.Add(new QualityPipeline
              {
                EngineId = config.EngineId,
                Name = config.PresetName ?? presetName,
                Description = config.Description ?? string.Empty,
                Steps = steps
              });
            }
          }
          catch (Exception ex)
      {
        ErrorLogger.LogWarning($"Best effort operation failed: {ex.Message}", "BackendAudioClip.Task");
      }
        }

        return pipelines;
      });
    }

    public async Task<PreviewPipelineResponse> PreviewQualityPipelineAsync(string audioId, string engineId, string? presetName = null, PipelineConfiguration? pipelineConfig = null, CancellationToken cancellationToken = default)
    {
      return await ExecuteWithRetryAsync(async () =>
      {
        var url = $"/api/quality/pipelines/engines/{Uri.EscapeDataString(engineId)}/preview";
        var request = new PreviewPipelineRequest
        {
          AudioId = audioId,
          EngineId = engineId,
          PresetName = presetName,
          PipelineConfig = pipelineConfig
        };

        var response = await _httpClient.PostAsJsonAsync(url, request, _jsonOptions, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
          throw await CreateExceptionFromResponseAsync(response);
        }

        return await response.Content.ReadFromJsonAsync<PreviewPipelineResponse>(_jsonOptions, cancellationToken)
                  ?? throw new BackendDeserializationException("Failed to deserialize preview pipeline response");
      });
    }

    public async Task<PipelineComparisonResponse> CompareQualityPipelineAsync(string audioId, string engineId, string? presetName = null, CancellationToken cancellationToken = default)
    {
      return await ExecuteWithRetryAsync(async () =>
      {
        var url = $"/api/quality/pipelines/engines/{Uri.EscapeDataString(engineId)}/compare?audio_id={Uri.EscapeDataString(audioId)}&preset_name={Uri.EscapeDataString(presetName ?? "default")}";

        var response = await _httpClient.PostAsync(url, null, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
          throw await CreateExceptionFromResponseAsync(response);
        }

        return await response.Content.ReadFromJsonAsync<PipelineComparisonResponse>(_jsonOptions, cancellationToken)
                  ?? throw new BackendDeserializationException("Failed to deserialize pipeline comparison response");
      });
    }

    // Quality Consistency Monitoring endpoints (IDEA 59)
    public async Task<bool> SetQualityStandardAsync(string projectId, string standardName, CancellationToken cancellationToken = default)
    {
      return await ExecuteWithRetryAsync(async () =>
      {
        const string url = "/api/quality/consistency/standard";
        var request = new
        {
          project_id = projectId,
          standard_name = standardName
        };

        var response = await _httpClient.PostAsJsonAsync(url, request, _jsonOptions, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
          throw await CreateExceptionFromResponseAsync(response);
        }

        // Backend returns {"message": "..."} on success
        var result = await response.Content.ReadFromJsonAsync<Dictionary<string, object>>(_jsonOptions, cancellationToken);
        return result?.ContainsKey("message") == true;
      });
    }

    public async Task<bool> RecordQualityMetricsAsync(string projectId, Dictionary<string, object> metrics, string? profileId = null, string? audioId = null, CancellationToken cancellationToken = default)
    {
      return await ExecuteWithRetryAsync(async () =>
      {
        var url = $"/api/quality/consistency/record?project_id={Uri.EscapeDataString(projectId)}";
        if (!string.IsNullOrEmpty(profileId))
        {
          url += $"&profile_id={Uri.EscapeDataString(profileId)}";
        }
        if (!string.IsNullOrEmpty(audioId))
        {
          url += $"&audio_id={Uri.EscapeDataString(audioId)}";
        }

        var request = new { metrics = metrics };
        var response = await _httpClient.PostAsJsonAsync(url, request, _jsonOptions, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
          throw await CreateExceptionFromResponseAsync(response);
        }

        // Backend returns {"message": "..."} on success
        var result = await response.Content.ReadFromJsonAsync<Dictionary<string, object>>(_jsonOptions, cancellationToken);
        return result?.ContainsKey("message") == true;
      });
    }

    public async Task<QualityConsistencyReport> CheckProjectConsistencyAsync(string projectId, int timePeriodDays = 30, CancellationToken cancellationToken = default)
    {
      return await ExecuteWithRetryAsync(async () =>
      {
        var url = $"/api/quality/consistency/{Uri.EscapeDataString(projectId)}?time_period_days={timePeriodDays}";

        var response = await _httpClient.GetAsync(url, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
          throw await CreateExceptionFromResponseAsync(response);
        }

        return await response.Content.ReadFromJsonAsync<QualityConsistencyReport>(_jsonOptions, cancellationToken)
                  ?? throw new BackendDeserializationException("Failed to deserialize quality consistency report");
      });
    }

    public async Task<AllProjectsConsistencyResponse> CheckAllProjectsConsistencyAsync(int timePeriodDays = 30, CancellationToken cancellationToken = default)
    {
      return await ExecuteWithRetryAsync(async () =>
      {
        var url = $"/api/quality/consistency/all?time_period_days={timePeriodDays}";

        var response = await _httpClient.GetAsync(url, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
          throw await CreateExceptionFromResponseAsync(response);
        }

        return await response.Content.ReadFromJsonAsync<AllProjectsConsistencyResponse>(_jsonOptions, cancellationToken)
                  ?? throw new BackendDeserializationException("Failed to deserialize all projects consistency response");
      });
    }

    public async Task<QualityTrendsResponse> GetProjectQualityTrendsAsync(string projectId, int timePeriodDays = 30, CancellationToken cancellationToken = default)
    {
      return await ExecuteWithRetryAsync(async () =>
      {
        var url = $"/api/quality/consistency/{Uri.EscapeDataString(projectId)}/trends?time_period_days={timePeriodDays}";

        var response = await _httpClient.GetAsync(url, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
          throw await CreateExceptionFromResponseAsync(response);
        }

        return await response.Content.ReadFromJsonAsync<QualityTrendsResponse>(_jsonOptions, cancellationToken)
                  ?? throw new BackendDeserializationException("Failed to deserialize quality trends response");
      });
    }

    // Advanced Quality Metrics Visualization endpoints (IDEA 60)
    public async Task<QualityHeatmapResponse> GetQualityHeatmapAsync(QualityHeatmapRequest request, CancellationToken cancellationToken = default)
    {
      return await ExecuteWithRetryAsync(async () =>
      {
        const string url = "/api/quality/visualization/heatmap";

        var response = await _httpClient.PostAsJsonAsync(url, request, _jsonOptions, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
          throw await CreateExceptionFromResponseAsync(response);
        }

        return await response.Content.ReadFromJsonAsync<QualityHeatmapResponse>(_jsonOptions, cancellationToken)
                  ?? throw new BackendDeserializationException("Failed to deserialize quality heatmap response");
      });
    }

    public async Task<QualityCorrelationResponse> GetQualityCorrelationsAsync(List<Dictionary<string, object>> qualityData, CancellationToken cancellationToken = default)
    {
      return await ExecuteWithRetryAsync(async () =>
      {
        const string url = "/api/quality/visualization/correlations";

        var response = await _httpClient.PostAsJsonAsync(url, qualityData, _jsonOptions, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
          throw await CreateExceptionFromResponseAsync(response);
        }

        return await response.Content.ReadFromJsonAsync<QualityCorrelationResponse>(_jsonOptions, cancellationToken)
                  ?? throw new BackendDeserializationException("Failed to deserialize quality correlation response");
      });
    }

    public async Task<QualityAnomalyResponse> DetectQualityAnomaliesAsync(List<Dictionary<string, object>> qualityData, string metric = "mos_score", double thresholdStd = 2.0, CancellationToken cancellationToken = default)
    {
      return await ExecuteWithRetryAsync(async () =>
      {
        var url = $"/api/quality/visualization/anomalies?metric={Uri.EscapeDataString(metric)}&threshold_std={thresholdStd}";

        var response = await _httpClient.PostAsJsonAsync(url, qualityData, _jsonOptions, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
          throw await CreateExceptionFromResponseAsync(response);
        }

        return await response.Content.ReadFromJsonAsync<QualityAnomalyResponse>(_jsonOptions, cancellationToken)
                  ?? throw new BackendDeserializationException("Failed to deserialize quality anomaly response");
      });
    }

    public async Task<QualityPredictionResponse> PredictQualityAsync(QualityPredictionRequest request, CancellationToken cancellationToken = default)
    {
      return await ExecuteWithRetryAsync(async () =>
      {
        const string url = "/api/quality/visualization/predict";

        var response = await _httpClient.PostAsJsonAsync(url, request, _jsonOptions, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
          throw await CreateExceptionFromResponseAsync(response);
        }

        return await response.Content.ReadFromJsonAsync<QualityPredictionResponse>(_jsonOptions, cancellationToken)
                  ?? throw new BackendDeserializationException("Failed to deserialize quality prediction response");
      });
    }

    public async Task<QualityInsightsResponse> GetQualityInsightsAsync(List<Dictionary<string, object>> qualityData, int timePeriodDays = 30, CancellationToken cancellationToken = default)
    {
      return await ExecuteWithRetryAsync(async () =>
      {
        var url = $"/api/quality/visualization/insights?time_period_days={timePeriodDays}";

        var response = await _httpClient.PostAsJsonAsync(url, qualityData, _jsonOptions, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
          throw await CreateExceptionFromResponseAsync(response);
        }

        return await response.Content.ReadFromJsonAsync<QualityInsightsResponse>(_jsonOptions, cancellationToken)
                  ?? throw new BackendDeserializationException("Failed to deserialize quality insights response");
      });
    }

    // Script editor endpoints
    public async Task<List<Script>> GetScriptsAsync(string? projectId = null, string? search = null, CancellationToken cancellationToken = default)
    {
      var queryParams = new NameValueCollection();
      if (!string.IsNullOrEmpty(projectId))
        queryParams.Add("project_id", projectId);
      if (!string.IsNullOrEmpty(search))
        queryParams.Add("search", search);

      var queryString = string.Join("&",
          (queryParams.AllKeys ?? Array.Empty<string>()).SelectMany(key =>
              queryParams.GetValues(key)?.Select(value => $"{key}={Uri.EscapeDataString(value)}") ?? Array.Empty<string>()
          )
      );

      var url = "/api/script-editor";
      if (!string.IsNullOrEmpty(queryString))
        url += $"?{queryString}";

      return await GetAsync<List<Script>>(url, cancellationToken) ?? new List<Script>();
    }

    public async Task<Script> GetScriptAsync(string scriptId, CancellationToken cancellationToken = default)
    {
      return await GetAsync<Script>($"/api/script-editor/{Uri.EscapeDataString(scriptId)}", cancellationToken)
          ?? throw new BackendDeserializationException("Failed to deserialize script");
    }

    public async Task<Script> CreateScriptAsync(ScriptCreateRequest request, CancellationToken cancellationToken = default)
    {
      return await PostAsync<ScriptCreateRequest, Script>("/api/script-editor", request, cancellationToken);
    }

    public async Task<Script> UpdateScriptAsync(string scriptId, ScriptUpdateRequest request, CancellationToken cancellationToken = default)
    {
      return await PutAsync<ScriptUpdateRequest, Script>($"/api/script-editor/{Uri.EscapeDataString(scriptId)}", request, cancellationToken);
    }

    public async Task<bool> DeleteScriptAsync(string scriptId, CancellationToken cancellationToken = default)
    {
      var response = await SendRequestAsync<object, object>($"/api/script-editor/{Uri.EscapeDataString(scriptId)}", null, System.Net.Http.HttpMethod.Delete, cancellationToken);
      return response != null;
    }

    public async Task<Script> AddSegmentToScriptAsync(string scriptId, ScriptSegment segment, CancellationToken cancellationToken = default)
    {
      return await PostAsync<ScriptSegment, Script>($"/api/script-editor/{Uri.EscapeDataString(scriptId)}/segments", segment, cancellationToken);
    }

    public async Task<bool> RemoveSegmentFromScriptAsync(string scriptId, string segmentId, CancellationToken cancellationToken = default)
    {
      var response = await SendRequestAsync<object, object>($"/api/script-editor/{Uri.EscapeDataString(scriptId)}/segments/{Uri.EscapeDataString(segmentId)}", null, System.Net.Http.HttpMethod.Delete, cancellationToken);
      return response != null;
    }

    public async Task<ScriptSynthesisResponse> SynthesizeScriptAsync(string scriptId, CancellationToken cancellationToken = default)
    {
      return await PostAsync<object, ScriptSynthesisResponse>($"/api/script-editor/{Uri.EscapeDataString(scriptId)}/synthesize", new { }, cancellationToken);
    }

    // ========== Pipeline API (Phase 22) ==========

    /// <summary>
    /// Process text through the voice AI pipeline (LLM → TTS).
    /// </summary>
    /// <param name="request">Pipeline request with text and config.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Pipeline response with generated text and audio.</returns>
    public async Task<VoiceStudio.App.Core.Models.PipelineResponse> ProcessPipelineAsync(VoiceStudio.App.Core.Models.PipelineRequest request, CancellationToken cancellationToken = default)
    {
      return await PostAsync<VoiceStudio.App.Core.Models.PipelineRequest, VoiceStudio.App.Core.Models.PipelineResponse>("/api/pipeline/process", request, cancellationToken);
    }

    /// <summary>
    /// Get available pipeline providers (LLM, STT, TTS engines).
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>List of available providers by type.</returns>
    public async Task<VoiceStudio.App.Core.Models.PipelineProvidersResponse> GetPipelineProvidersAsync(CancellationToken cancellationToken = default)
    {
      return await GetAsync<VoiceStudio.App.Core.Models.PipelineProvidersResponse>("/api/pipeline/providers", cancellationToken)
          ?? new VoiceStudio.App.Core.Models.PipelineProvidersResponse();
    }

    /// <summary>
    /// Get pipeline metrics and usage statistics.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Pipeline metrics.</returns>
    public async Task<PipelineMetricsResponse> GetPipelineMetricsAsync(CancellationToken cancellationToken = default)
    {
      return await GetAsync<PipelineMetricsResponse>("/api/pipeline/metrics", cancellationToken)
          ?? new PipelineMetricsResponse();
    }

    // ========== File Upload with Progress (Phase 11) ==========

    /// <inheritdoc />
    public async Task<TResponse?> UploadFileWithProgressAsync<TResponse>(
        string endpoint,
        string filePath,
        string fileFieldName = "file",
        Dictionary<string, string>? additionalData = null,
        IProgress<double>? progress = null,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default) where TResponse : class
    {
      return await UploadFilesWithProgressAsync<TResponse>(
          endpoint,
          new Dictionary<string, string> { { fileFieldName, filePath } },
          additionalData,
          progress,
          timeout,
          cancellationToken);
    }

    /// <inheritdoc />
    public async Task<TResponse?> UploadFilesWithProgressAsync<TResponse>(
        string endpoint,
        Dictionary<string, string> files,
        Dictionary<string, string>? additionalData = null,
        IProgress<double>? progress = null,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default) where TResponse : class
    {
      try
      {
        // Calculate total size for progress tracking
        long totalSize = 0;
        foreach (var kvp in files)
        {
          var fileInfo = new FileInfo(kvp.Value);
          if (fileInfo.Exists)
          {
            totalSize += fileInfo.Length;
          }
        }

        long uploadedBytes = 0;

        using var content = new MultipartFormDataContent();

        foreach (var kvp in files)
        {
          var filePath = kvp.Value;
          var fieldName = kvp.Key;
          var fileName = Path.GetFileName(filePath);

          await using var fileStream = File.OpenRead(filePath);

          // Create a progress tracking wrapper
          var progressStream = new ProgressStream(fileStream, (bytesRead, _) =>
          {
            uploadedBytes += bytesRead;
            if (totalSize > 0)
            {
              progress?.Report((double)uploadedBytes / totalSize * 100.0);
            }
          });

          var streamContent = new StreamContent(progressStream);

          // Set content type based on extension
          var extension = Path.GetExtension(fileName).ToLowerInvariant();
          var contentType = extension switch
          {
            ".wav" => "audio/wav",
            ".mp3" => "audio/mpeg",
            ".flac" => "audio/flac",
            ".m4a" => "audio/mp4",
            ".ogg" => "audio/ogg",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".png" => "image/png",
            ".gif" => "image/gif",
            ".mp4" => "video/mp4",
            ".avi" => "video/x-msvideo",
            ".mov" => "video/quicktime",
            ".mkv" => "video/x-matroska",
            _ => "application/octet-stream"
          };

          streamContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(contentType);
          content.Add(streamContent, fieldName, fileName);
        }

        // Add additional form data
        if (additionalData != null)
        {
          foreach (var kvp in additionalData)
          {
            content.Add(new StringContent(kvp.Value), kvp.Key);
          }
        }

        // Set timeout
        var originalTimeout = _httpClient.Timeout;
        if (timeout.HasValue)
        {
          _httpClient.Timeout = timeout.Value;
        }

        try
        {
          var response = await _httpClient.PostAsync(endpoint, content, cancellationToken);
          response.EnsureSuccessStatusCode();

          var responseJson = await response.Content.ReadAsStringAsync(cancellationToken);
          return JsonSerializer.Deserialize<TResponse>(responseJson, _jsonOptions);
        }
        finally
        {
          _httpClient.Timeout = originalTimeout;
        }
      }
      catch (Exception ex)
      {
        System.Diagnostics.Debug.WriteLine($"File upload failed for {endpoint}: {ex.Message}");
        ErrorLogger.LogError($"File upload failed for {endpoint}: {ex.Message}", "BackendClient.UploadFilesWithProgressAsync");
        throw;
      }
    }

    // Plugin Health Dashboard endpoints (Phase 4)
    public async Task<PluginHealthDashboardResponse?> GetPluginHealthDashboardAsync(CancellationToken cancellationToken = default)
    {
      try
      {
        return await SendRequestAsync<object?, PluginHealthDashboardResponse>(
          "api/plugins/health/dashboard",
          null,
          HttpMethod.Get,
          cancellationToken);
      }
      catch (Exception ex)
      {
        System.Diagnostics.Debug.WriteLine($"Error getting plugin health dashboard: {ex.Message}");
        ErrorLogger.LogError($"Error getting plugin health dashboard: {ex.Message}", "BackendClient.GetPluginHealthDashboardAsync");
        throw;
      }
    }

    public async Task<PluginMetricsResponse?> GetPluginMetricsAsync(string pluginId, CancellationToken cancellationToken = default)
    {
      try
      {
        var encodedPluginId = Uri.EscapeDataString(pluginId);
        return await SendRequestAsync<object?, PluginMetricsResponse>(
          $"api/plugins/{encodedPluginId}/metrics",
          null,
          HttpMethod.Get,
          cancellationToken);
      }
      catch (Exception ex)
      {
        System.Diagnostics.Debug.WriteLine($"Error getting plugin metrics for {pluginId}: {ex.Message}");
        ErrorLogger.LogError($"Error getting plugin metrics for {pluginId}: {ex.Message}", "BackendClient.GetPluginMetricsAsync");
        throw;
      }
    }

    public async Task<string> ExportPluginMetricsAsync(string format = "json", CancellationToken cancellationToken = default)
    {
      try
      {
        var response = await _httpClient.GetAsync($"api/plugins/metrics/export?format={format}", cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync(cancellationToken);
      }
      catch (Exception ex)
      {
        System.Diagnostics.Debug.WriteLine($"Error exporting plugin metrics: {ex.Message}");
        ErrorLogger.LogError($"Error exporting plugin metrics: {ex.Message}", "BackendClient.ExportPluginMetricsAsync");
        throw;
      }
    }

    public void Dispose()
    {
      WebSocketService?.Dispose();
      _httpClient?.Dispose();
      // CircuitBreaker doesn't implement IDisposable - no cleanup needed
    }
  }
}