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

// Architecture wave (2026-03-20): HTTP policy lives in BackendClientHttpPipeline.cs (PR-1). Feature endpoints remain here.
// Inventory: docs/design/BACKENDCLIENT_TRANSPORT_EXTRACTION_INVENTORY.md — PR-2 may dedupe with Gateways/BackendTransport.cs.

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

  public partial class BackendClient : IBackendClient, IDisposable
  {
    private readonly HttpClient _httpClient;
    private readonly BackendClientConfig _config;
    private readonly JsonSerializerOptions _jsonOptions;
    private readonly BackendClientHttpPipeline _pipeline;

    private readonly IRequestCoordinator _requestCoordinator;

    private IWebSocketService? _webSocketService;
    public IWebSocketService? WebSocketService => _webSocketService;

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
    /// <param name="requestMetrics">Optional service for per-endpoint request counting.</param>
    /// <param name="requestCoordinator">Shared request coordinator for profiles/engines (required when created via DI).</param>
    /// <param name="gracefulDegradation">Optional service to clear degraded mode on successful responses.</param>
    /// <param name="innerHandler">Optional inner HTTP handler for testing; when null, uses HttpClientHandler.</param>
    public BackendClient(BackendClientConfig config, ICorrelationIdProvider? correlationProvider, IRequestMetricsService? requestMetrics = null, IRequestCoordinator? requestCoordinator = null, GracefulDegradationService? gracefulDegradation = null, HttpMessageHandler? innerHandler = null)
    {
      _config = config ?? throw new ArgumentNullException(nameof(config));
      _requestCoordinator = requestCoordinator ?? new RequestCoordinator();

      // Handler chain: DegradedModeClearHandler (outer) -> RequestMetricsHandler -> CorrelationIdHandler -> innerHandler (inner)
      var httpHandler = innerHandler ?? new HttpClientHandler();
      var correlationHandler = correlationProvider != null
        ? new CorrelationIdHandler(httpHandler, correlationProvider)
        : new CorrelationIdHandler(httpHandler);
      var metricsOrCorrelation = requestMetrics != null
        ? new RequestMetricsHandler(requestMetrics, correlationHandler)
        : (HttpMessageHandler)correlationHandler;
      var rootHandler = new DegradedModeClearHandler(gracefulDegradation, metricsOrCorrelation);
      _httpClient = new HttpClient(rootHandler)
      {
        BaseAddress = new Uri(config.BaseUrl),
        Timeout = config.RequestTimeout
      };

      // Use centralized JSON options for consistent snake_case serialization
      _jsonOptions = JsonSerializerOptionsFactory.BackendApi;

      _pipeline = new BackendClientHttpPipeline(_httpClient, _jsonOptions);

      InitializeWebSocket(config);
    }

    /// <summary>
    /// PR-3: Constructor that uses shared <see cref="BackendHttpContext"/> for HTTP transport.
    /// Used when PluginHealthClient and BackendClient share the same pipeline.
    /// </summary>
    internal BackendClient(BackendHttpContext httpContext, BackendClientConfig config, IRequestCoordinator? requestCoordinator = null)
    {
      _config = config ?? throw new ArgumentNullException(nameof(config));
      _requestCoordinator = requestCoordinator ?? new RequestCoordinator();
      _httpClient = httpContext.HttpClient;
      _jsonOptions = JsonSerializerOptionsFactory.BackendApi;
      _pipeline = httpContext.Pipeline;

      InitializeWebSocket(config);
    }

    private void InitializeWebSocket(BackendClientConfig config)
    {
      if (string.IsNullOrEmpty(config.WebSocketUrl))
        return;

      var wsUrl = config.WebSocketUrl;
      if (wsUrl.StartsWith("http://", StringComparison.Ordinal))
        wsUrl = wsUrl.Replace("http://", "ws://");
      else if (wsUrl.StartsWith("https://", StringComparison.Ordinal))
        wsUrl = wsUrl.Replace("https://", "wss://");

      if (!wsUrl.EndsWith("/realtime", StringComparison.Ordinal) && !wsUrl.EndsWith("/realtime/", StringComparison.Ordinal))
        wsUrl = wsUrl.TrimEnd('/') + "/realtime";

      _webSocketService = new WebSocketService(wsUrl);
    }

    /// <summary>
    /// Gets the base address of the backend API.
    /// </summary>
    public System.Uri? BaseAddress => _httpClient?.BaseAddress;

    public Task<TResponse> SendRequestAsync<TRequest, TResponse>(
        string endpoint,
        TRequest request,
        CancellationToken cancellationToken = default)
    {
      return _pipeline.SendRequestAsync<TRequest, TResponse>(endpoint, request, cancellationToken);
    }

    /// <summary>
    /// Generic request helper method with HTTP method support.
    /// </summary>
    public Task<TResponse?> SendRequestAsync<TRequest, TResponse>(
        string endpoint,
        TRequest? request,
        System.Net.Http.HttpMethod method,
        CancellationToken cancellationToken = default) where TResponse : class
    {
      return _pipeline.SendRequestAsync<TRequest, TResponse>(endpoint, request, method, cancellationToken);
    }

    public async Task<TResponse> SendMcpOperationAsync<TRequest, TResponse>(
        string operation,
        TRequest payload,
        CancellationToken cancellationToken = default)
    {
      // MCP bridge endpoint
      return await SendRequestAsync<TRequest, TResponse>($"/api/mcp/{operation}", payload, cancellationToken);
    }

    /// <summary>
    /// Static health probe for UI self-test. Does not require a BackendClient instance.
    /// </summary>
    /// <param name="baseUrl">Backend base URL (e.g. same host as spawned uvicorn, typically http://127.0.0.1:8000).</param>
    /// <param name="timeoutMs">Timeout in milliseconds. Default 3000.</param>
    /// <returns>True if GET /api/health returns success.</returns>
    public static async Task<bool> TryCheckHealthAsync(string baseUrl, int timeoutMs = 3000)
    {
      if (string.IsNullOrWhiteSpace(baseUrl))
        return false;
      var url = baseUrl.TrimEnd('/') + "/api/health";
      try
      {
        using var cts = new CancellationTokenSource(timeoutMs);
        using var handler = new HttpClientHandler();
        using var probeClient = new HttpClient(handler)
        {
          Timeout = TimeSpan.FromMilliseconds(timeoutMs)
        };
        var response = await probeClient.GetAsync(url, cts.Token).ConfigureAwait(false);
        return response.IsSuccessStatusCode;
      }
      catch (Exception)
      {
        return false;
      }
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
          throw await _pipeline.CreateExceptionFromResponseAsync(response);
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
          throw await _pipeline.CreateExceptionFromResponseAsync(response);
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
          throw await _pipeline.CreateExceptionFromResponseAsync(response);
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

    public async Task<Stream> GetAudioStreamAsync(string audioId, CancellationToken cancellationToken = default)
    {
      return await ExecuteWithRetryAsync(async () =>
      {
        var response = await _httpClient.GetAsync($"/api/audio/file/{audioId}", cancellationToken);

        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
          response = await _httpClient.GetAsync($"/api/voice/audio/{audioId}", cancellationToken);

        if (!response.IsSuccessStatusCode)
          throw await _pipeline.CreateExceptionFromResponseAsync(response);

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
          throw await _pipeline.CreateExceptionFromResponseAsync(response);
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
          throw await _pipeline.CreateExceptionFromResponseAsync(response);
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
          throw await _pipeline.CreateExceptionFromResponseAsync(response);
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
          throw await _pipeline.CreateExceptionFromResponseAsync(response);
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
          throw await _pipeline.CreateExceptionFromResponseAsync(response);
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
          throw await _pipeline.CreateExceptionFromResponseAsync(response);
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
          throw await _pipeline.CreateExceptionFromResponseAsync(response);
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
          throw await _pipeline.CreateExceptionFromResponseAsync(response);
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
          throw await _pipeline.CreateExceptionFromResponseAsync(response);
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
          throw await _pipeline.CreateExceptionFromResponseAsync(response);
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
          throw await _pipeline.CreateExceptionFromResponseAsync(response);
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
          throw await _pipeline.CreateExceptionFromResponseAsync(response);
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
          throw await _pipeline.CreateExceptionFromResponseAsync(response);
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
          throw await _pipeline.CreateExceptionFromResponseAsync(response);
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
          throw await _pipeline.CreateExceptionFromResponseAsync(response);
        }

        return await response.Content.ReadFromJsonAsync<AudioTrack>(_jsonOptions, cancellationToken)
                  ?? throw new BackendDeserializationException("Failed to deserialize track");
      });
    }

    public async Task<AudioTrack> UpdateTrackAsync(
        string projectId,
        string trackId,
        string? name = null,
        string? engine = null,
        bool? isMuted = null,
        bool? isSolo = null,
        CancellationToken cancellationToken = default)
    {
      return await ExecuteWithRetryAsync(async () =>
      {
        var request = new Dictionary<string, object?>();
        if (name != null) request["name"] = name;
        if (engine != null) request["engine"] = engine;
        if (isMuted.HasValue) request["is_muted"] = isMuted.Value;
        if (isSolo.HasValue) request["is_solo"] = isSolo.Value;

        var response = await _httpClient.PutAsJsonAsync($"/api/projects/{projectId}/tracks/{trackId}", request, _jsonOptions, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
          throw await _pipeline.CreateExceptionFromResponseAsync(response);
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
          id = string.IsNullOrWhiteSpace(clip.Id) ? null : clip.Id,
          name = clip.Name,
          profile_id = clip.ProfileId,
          audio_id = clip.AudioId,
          audio_url = clip.AudioUrl,
          duration_seconds = clip.Duration.TotalSeconds,
          start_time = clip.StartTime,
          source_start_seconds = clip.SourceStartSeconds,
          fade_in_seconds = clip.FadeInSeconds,
          fade_out_seconds = clip.FadeOutSeconds,
          engine = clip.Engine,
          quality_score = clip.QualityScore,
          derived_from_clip_id = string.IsNullOrWhiteSpace(clip.DerivedFromClipId) ? null : clip.DerivedFromClipId,
        };

        var response = await _httpClient.PostAsJsonAsync($"/api/projects/{projectId}/tracks/{trackId}/clips", request, _jsonOptions, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
          throw await _pipeline.CreateExceptionFromResponseAsync(response);
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
          SourceStartSeconds = backendClip.SourceStartSeconds,
          FadeInSeconds = backendClip.FadeInSeconds,
          FadeOutSeconds = backendClip.FadeOutSeconds,
          Engine = backendClip.Engine,
          QualityScore = backendClip.QualityScore,
          DerivedFromClipId = string.IsNullOrWhiteSpace(backendClip.DerivedFromClipId) ? null : backendClip.DerivedFromClipId,
        };
      });
    }

    public async Task<AudioClip> UpdateClipAsync(
        string projectId,
        string trackId,
        string clipId,
        string? name = null,
        double? startTime = null,
        string? audioId = null,
        string? audioUrl = null,
        double? durationSeconds = null,
        double? sourceStartSeconds = null,
        double? fadeInSeconds = null,
        double? fadeOutSeconds = null,
        string? derivedFromClipId = null,
        CancellationToken cancellationToken = default)
    {
      return await ExecuteWithRetryAsync(async () =>
      {
        var request = new Dictionary<string, object?>();
        if (name != null) request["name"] = name;
        if (startTime.HasValue) request["start_time"] = startTime.Value;
        if (audioId != null) request["audio_id"] = audioId;
        if (audioUrl != null) request["audio_url"] = audioUrl;
        if (durationSeconds.HasValue) request["duration_seconds"] = durationSeconds.Value;
        if (sourceStartSeconds.HasValue) request["source_start_seconds"] = sourceStartSeconds.Value;
        if (fadeInSeconds.HasValue) request["fade_in_seconds"] = fadeInSeconds.Value;
        if (fadeOutSeconds.HasValue) request["fade_out_seconds"] = fadeOutSeconds.Value;
        if (derivedFromClipId != null)
          request["derived_from_clip_id"] = string.IsNullOrWhiteSpace(derivedFromClipId) ? null : derivedFromClipId;

        var response = await _httpClient.PutAsJsonAsync($"/api/projects/{projectId}/tracks/{trackId}/clips/{clipId}", request, _jsonOptions, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
          throw await _pipeline.CreateExceptionFromResponseAsync(response);
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
          SourceStartSeconds = backendClip.SourceStartSeconds,
          FadeInSeconds = backendClip.FadeInSeconds,
          FadeOutSeconds = backendClip.FadeOutSeconds,
          Engine = backendClip.Engine,
          QualityScore = backendClip.QualityScore,
          DerivedFromClipId = string.IsNullOrWhiteSpace(backendClip.DerivedFromClipId) ? null : backendClip.DerivedFromClipId,
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
      public double SourceStartSeconds { get; set; }
      public double FadeInSeconds { get; set; }
      public double FadeOutSeconds { get; set; }
      public string? Engine { get; set; }
      public double? QualityScore { get; set; }
      public string? DerivedFromClipId { get; set; }
    }

    private Task<T> ExecuteWithRetryAsync<T>(Func<Task<T>> operation) => _pipeline.ExecuteWithRetryAsync(operation);

    // Batch processing (effect preset methods extracted to IEffectChainClient PR-12)
    public async Task<BatchJob> CreateBatchJobAsync(BatchJobRequest request, CancellationToken cancellationToken = default)
    {
      return await ExecuteWithRetryAsync(async () =>
      {
        var json = JsonSerializer.Serialize(request, _jsonOptions);
        var content = new StringContent(json, Encoding.UTF8, "application/json");
        var response = await _httpClient.PostAsync("/api/batch/jobs", content, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
          throw await _pipeline.CreateExceptionFromResponseAsync(response);
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
          throw await _pipeline.CreateExceptionFromResponseAsync(response);
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
          throw await _pipeline.CreateExceptionFromResponseAsync(response);
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
          throw await _pipeline.CreateExceptionFromResponseAsync(response);
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
          throw await _pipeline.CreateExceptionFromResponseAsync(response);
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
          throw await _pipeline.CreateExceptionFromResponseAsync(response);
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
          throw await _pipeline.CreateExceptionFromResponseAsync(response);
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
          throw await _pipeline.CreateExceptionFromResponseAsync(response);
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
          throw await _pipeline.CreateExceptionFromResponseAsync(response);
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
          throw await _pipeline.CreateExceptionFromResponseAsync(response);
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
          throw await _pipeline.CreateExceptionFromResponseAsync(response);
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
          throw await _pipeline.CreateExceptionFromResponseAsync(response);
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
          throw await _pipeline.CreateExceptionFromResponseAsync(response);
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
          throw await _pipeline.CreateExceptionFromResponseAsync(response);
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
          throw await _pipeline.CreateExceptionFromResponseAsync(response);
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
          throw await _pipeline.CreateExceptionFromResponseAsync(response);
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
          throw await _pipeline.CreateExceptionFromResponseAsync(response);
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
          throw await _pipeline.CreateExceptionFromResponseAsync(response);
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
          throw await _pipeline.CreateExceptionFromResponseAsync(response);
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
          throw await _pipeline.CreateExceptionFromResponseAsync(response);
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
          throw await _pipeline.CreateExceptionFromResponseAsync(response);
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
          throw await _pipeline.CreateExceptionFromResponseAsync(response);
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
          throw await _pipeline.CreateExceptionFromResponseAsync(response);
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
          throw await _pipeline.CreateExceptionFromResponseAsync(response);
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
          throw await _pipeline.CreateExceptionFromResponseAsync(response);
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
          throw await _pipeline.CreateExceptionFromResponseAsync(response);
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
          throw await _pipeline.CreateExceptionFromResponseAsync(response);
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
          throw await _pipeline.CreateExceptionFromResponseAsync(response);
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
          throw await _pipeline.CreateExceptionFromResponseAsync(response);
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
          throw await _pipeline.CreateExceptionFromResponseAsync(response);
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
          throw await _pipeline.CreateExceptionFromResponseAsync(response);
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
          throw await _pipeline.CreateExceptionFromResponseAsync(response);
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
          throw await _pipeline.CreateExceptionFromResponseAsync(response);
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
          throw await _pipeline.CreateExceptionFromResponseAsync(response);
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
          throw await _pipeline.CreateExceptionFromResponseAsync(response);
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
          throw await _pipeline.CreateExceptionFromResponseAsync(response);
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
          throw await _pipeline.CreateExceptionFromResponseAsync(response);
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
          throw await _pipeline.CreateExceptionFromResponseAsync(response);
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
          throw await _pipeline.CreateExceptionFromResponseAsync(response);
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
          throw await _pipeline.CreateExceptionFromResponseAsync(response);
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
          throw await _pipeline.CreateExceptionFromResponseAsync(response);
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
          throw await _pipeline.CreateExceptionFromResponseAsync(response);
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
          throw await _pipeline.CreateExceptionFromResponseAsync(response);
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
          throw await _pipeline.CreateExceptionFromResponseAsync(response);
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
          throw await _pipeline.CreateExceptionFromResponseAsync(response);
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
          throw await _pipeline.CreateExceptionFromResponseAsync(response);
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
          throw await _pipeline.CreateExceptionFromResponseAsync(response);
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
          throw await _pipeline.CreateExceptionFromResponseAsync(response);
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
          throw await _pipeline.CreateExceptionFromResponseAsync(response);
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
          throw await _pipeline.CreateExceptionFromResponseAsync(response);
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
          throw await _pipeline.CreateExceptionFromResponseAsync(response);
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
          throw await _pipeline.CreateExceptionFromResponseAsync(response);
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
          throw await _pipeline.CreateExceptionFromResponseAsync(response);
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
          throw await _pipeline.CreateExceptionFromResponseAsync(response);
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
          throw await _pipeline.CreateExceptionFromResponseAsync(response);
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
    public Task<T?> GetAsync<T>(string endpoint, CancellationToken cancellationToken = default) where T : class
    {
      return _pipeline.GetAsync<T>(endpoint, cancellationToken);
    }

    /// <summary>
    /// Generic POST request helper method.
    /// </summary>
    public Task<TResponse> PostAsync<TRequest, TResponse>(
        string endpoint,
        TRequest request,
        CancellationToken cancellationToken = default)
    {
      return _pipeline.PostAsync<TRequest, TResponse>(endpoint, request, cancellationToken);
    }

    /// <summary>
    /// Generic POST request helper method (void response).
    /// </summary>
    public Task PostAsync<TRequest>(
        string endpoint,
        TRequest request,
        CancellationToken cancellationToken = default)
    {
      return _pipeline.PostAsync(endpoint, request, cancellationToken);
    }

    /// <summary>
    /// Generic PUT request helper method.
    /// </summary>
    public Task<TResponse> PutAsync<TRequest, TResponse>(
        string endpoint,
        TRequest request,
        CancellationToken cancellationToken = default)
    {
      return _pipeline.PutAsync<TRequest, TResponse>(endpoint, request, cancellationToken);
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
          throw await _pipeline.CreateExceptionFromResponseAsync(response);
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
          throw await _pipeline.CreateExceptionFromResponseAsync(response);
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

    // Backup and restore — use IBackupRestoreClient (PR-14)

    // Settings management
    public async Task<SettingsData> GetSettingsAsync(CancellationToken cancellationToken = default)
    {
      return await ExecuteWithRetryAsync(async () =>
      {
        var response = await _httpClient.GetAsync("/api/settings", cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
          throw await _pipeline.CreateExceptionFromResponseAsync(response);
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
          throw await _pipeline.CreateExceptionFromResponseAsync(response);
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
          throw await _pipeline.CreateExceptionFromResponseAsync(response);
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
          throw await _pipeline.CreateExceptionFromResponseAsync(response);
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
          throw await _pipeline.CreateExceptionFromResponseAsync(response);
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

    // Emotion preset management
    public async Task<List<EmotionPreset>> GetEmotionPresetsAsync(CancellationToken cancellationToken = default)
    {
      return await ExecuteWithRetryAsync(async () =>
      {
        var response = await _httpClient.GetAsync("/api/emotion/preset/list", cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
          throw await _pipeline.CreateExceptionFromResponseAsync(response);
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
          throw await _pipeline.CreateExceptionFromResponseAsync(response);
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
          throw await _pipeline.CreateExceptionFromResponseAsync(response);
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
          throw await _pipeline.CreateExceptionFromResponseAsync(response);
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
          throw await _pipeline.CreateExceptionFromResponseAsync(response);
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
          throw await _pipeline.CreateExceptionFromResponseAsync(response);
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
          throw await _pipeline.CreateExceptionFromResponseAsync(response);
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
          throw await _pipeline.CreateExceptionFromResponseAsync(response);
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
          throw await _pipeline.CreateExceptionFromResponseAsync(response);
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
          throw await _pipeline.CreateExceptionFromResponseAsync(response);
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
          throw await _pipeline.CreateExceptionFromResponseAsync(response);
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
          throw await _pipeline.CreateExceptionFromResponseAsync(response);
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
          throw await _pipeline.CreateExceptionFromResponseAsync(response);
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
          throw await _pipeline.CreateExceptionFromResponseAsync(response);
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
          throw await _pipeline.CreateExceptionFromResponseAsync(response);
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
          throw await _pipeline.CreateExceptionFromResponseAsync(response);
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
          throw await _pipeline.CreateExceptionFromResponseAsync(response);
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
          throw await _pipeline.CreateExceptionFromResponseAsync(response);
        }

        return await response.Content.ReadFromJsonAsync<QualityInsightsResponse>(_jsonOptions, cancellationToken)
                  ?? throw new BackendDeserializationException("Failed to deserialize quality insights response");
      });
    }

    // ========== Pipeline API (Phase 22) ==========
    // GetPipelineProvidersAsync, ProcessPipelineAsync extracted to IPipelineConversationClient (PR-13)

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

        // Per-upload timeout without mutating shared HttpClient.Timeout (PR-1 footgun fix).
        CancellationToken uploadToken = cancellationToken;
        CancellationTokenSource? linkedTimeout = null;
        if (timeout is { } t && t > TimeSpan.Zero)
        {
          linkedTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
          linkedTimeout.CancelAfter(t);
          uploadToken = linkedTimeout.Token;
        }

        try
        {
          var response = await _httpClient.PostAsync(endpoint, content, uploadToken);
          response.EnsureSuccessStatusCode();

          var responseJson = await response.Content.ReadAsStringAsync(uploadToken);
          return JsonSerializer.Deserialize<TResponse>(responseJson, _jsonOptions);
        }
        finally
        {
          linkedTimeout?.Dispose();
        }
      }
      catch (Exception ex)
      {
        System.Diagnostics.Debug.WriteLine($"File upload failed for {endpoint}: {ex.Message}");
        ErrorLogger.LogError($"File upload failed for {endpoint}: {ex.Message}", "BackendClient.UploadFilesWithProgressAsync");
        throw;
      }
    }

    // Plugin Health Dashboard — PR-3: moved to PluginHealthClient; use IPluginHealthClient.

    public void Dispose()
    {
      _webSocketService?.Dispose();
      _httpClient?.Dispose();
      // CircuitBreaker doesn't implement IDisposable - no cleanup needed
    }
  }
}