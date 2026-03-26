using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using VoiceStudio.App.Logging;
using VoiceStudio.App.Utilities;
using VoiceStudio.Core.Exceptions;
using VoiceStudio.Core.Gateways;

namespace VoiceStudio.App.Services.Gateways
{
  /// <summary>
  /// HTTP transport layer with retry, circuit breaker, and correlation tracking.
  /// Extracted from BackendClient for reuse by domain-specific gateways.
  /// </summary>
  public sealed class BackendTransport : IBackendTransport, IDisposable
  {
    private readonly HttpClient _httpClient;
    private readonly JsonSerializerOptions _jsonOptions;
    private readonly CircuitBreaker _circuitBreaker;
    private readonly int _maxRetries;
    private readonly int _retryDelayMs;
    private readonly int _connectionCheckIntervalSeconds;

    private bool _isConnected = true;
    private DateTime _lastConnectionCheck = DateTime.MinValue;
    private bool _disposed;

    public bool IsConnected => _isConnected;
    public event EventHandler<bool>? ConnectionStatusChanged;

    /// <summary>
    /// Initializes a new instance of BackendTransport.
    /// </summary>
    /// <param name="httpClient">The HTTP client to use.</param>
    /// <param name="jsonOptions">JSON serialization options.</param>
    /// <param name="maxRetries">Maximum retry attempts (default: 3).</param>
    /// <param name="retryDelayMs">Initial retry delay in milliseconds (default: 1000).</param>
    /// <param name="circuitBreakerThreshold">Failure threshold for circuit breaker (default: 5).</param>
    /// <param name="circuitBreakerTimeout">Timeout for circuit breaker reset (default: 30 seconds).</param>
    /// <param name="connectionCheckIntervalSeconds">Interval for connection checks (default: 30).</param>
    public BackendTransport(
        HttpClient httpClient,
        JsonSerializerOptions? jsonOptions = null,
        int maxRetries = 3,
        int retryDelayMs = 1000,
        int circuitBreakerThreshold = 5,
        TimeSpan? circuitBreakerTimeout = null,
        int connectionCheckIntervalSeconds = 30)
    {
      _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
      _jsonOptions = jsonOptions ?? JsonSerializerOptionsFactory.BackendApi;
      _maxRetries = maxRetries;
      _retryDelayMs = retryDelayMs;
      _connectionCheckIntervalSeconds = connectionCheckIntervalSeconds;
      _circuitBreaker = new CircuitBreaker(
          circuitBreakerThreshold,
          circuitBreakerTimeout ?? TimeSpan.FromSeconds(30));
    }

    public async Task<GatewayResult<T>> GetAsync<T>(string path, CancellationToken cancellationToken = default)
    {
      return await ExecuteAsync(async () =>
      {
        var response = await _httpClient.GetAsync(path, cancellationToken);
        return await HandleResponseAsync<T>(response, cancellationToken);
      }, cancellationToken);
    }

    public async Task<GatewayResult<TResponse>> PostAsync<TRequest, TResponse>(
        string path,
        TRequest body,
        CancellationToken cancellationToken = default)
    {
      return await ExecuteAsync(async () =>
      {
        var response = await _httpClient.PostAsJsonAsync(path, body, _jsonOptions, cancellationToken);
        return await HandleResponseAsync<TResponse>(response, cancellationToken);
      }, cancellationToken);
    }

    public async Task<GatewayResult<bool>> PostAsync<TRequest>(
        string path,
        TRequest body,
        CancellationToken cancellationToken = default)
    {
      return await ExecuteAsync(async () =>
      {
        var response = await _httpClient.PostAsJsonAsync(path, body, _jsonOptions, cancellationToken);
        if (response.IsSuccessStatusCode)
        {
          return GatewayResult<bool>.Ok(true, GetCorrelationId(response));
        }
        var error = await CreateErrorFromResponseAsync(response, cancellationToken);
        return GatewayResult<bool>.Fail(error, GetCorrelationId(response));
      }, cancellationToken);
    }

    public async Task<GatewayResult<TResponse>> PutAsync<TRequest, TResponse>(
        string path,
        TRequest body,
        CancellationToken cancellationToken = default)
    {
      return await ExecuteAsync(async () =>
      {
        var response = await _httpClient.PutAsJsonAsync(path, body, _jsonOptions, cancellationToken);
        return await HandleResponseAsync<TResponse>(response, cancellationToken);
      }, cancellationToken);
    }

    public async Task<GatewayResult<bool>> DeleteAsync(string path, CancellationToken cancellationToken = default)
    {
      return await ExecuteAsync(async () =>
      {
        var response = await _httpClient.DeleteAsync(path, cancellationToken);
        if (response.IsSuccessStatusCode)
        {
          return GatewayResult<bool>.Ok(true, GetCorrelationId(response));
        }
        var error = await CreateErrorFromResponseAsync(response, cancellationToken);
        return GatewayResult<bool>.Fail(error, GetCorrelationId(response));
      }, cancellationToken);
    }

    public async Task<GatewayResult<TResponse>> UploadAsync<TResponse>(
        string path,
        Stream fileStream,
        string fileName,
        string fieldName = "file",
        string contentType = "application/octet-stream",
        Action<long, long>? progress = null,
        CancellationToken cancellationToken = default)
    {
      return await ExecuteAsync(async () =>
      {
        using var content = new MultipartFormDataContent();
        var streamContent = progress != null
            ? new StreamContent(new ProgressStream(fileStream, progress))
            : new StreamContent(fileStream);
        streamContent.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        content.Add(streamContent, fieldName, fileName);

        var response = await _httpClient.PostAsync(path, content, cancellationToken);
        return await HandleResponseAsync<TResponse>(response, cancellationToken);
      }, cancellationToken);
    }

    public async Task<GatewayResult<bool>> DownloadAsync(
        string path,
        Stream destinationStream,
        Action<long, long>? progress = null,
        CancellationToken cancellationToken = default)
    {
      return await ExecuteAsync(async () =>
      {
        var response = await _httpClient.GetAsync(path, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
          var error = await CreateErrorFromResponseAsync(response, cancellationToken);
          return GatewayResult<bool>.Fail(error, GetCorrelationId(response));
        }

        var totalBytes = response.Content.Headers.ContentLength ?? -1;
        var bytesRead = 0L;
        var buffer = new byte[8192];

        using var contentStream = await response.Content.ReadAsStreamAsync(cancellationToken);
        int read;
        while ((read = await contentStream.ReadAsync(buffer, 0, buffer.Length, cancellationToken)) > 0)
        {
          await destinationStream.WriteAsync(buffer, 0, read, cancellationToken);
          bytesRead += read;
          progress?.Invoke(bytesRead, totalBytes);
        }

        return GatewayResult<bool>.Ok(true, GetCorrelationId(response));
      }, cancellationToken);
    }

    public async Task<GatewayResult<Stream>> GetStreamAsync(string path, CancellationToken cancellationToken = default)
    {
      return await ExecuteAsync(async () =>
      {
        var response = await _httpClient.GetAsync(path, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
          var error = await CreateErrorFromResponseAsync(response, cancellationToken);
          return GatewayResult<Stream>.Fail(error, GetCorrelationId(response));
        }

        var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        return GatewayResult<Stream>.Ok(stream, GetCorrelationId(response));
      }, cancellationToken);
    }

    public void ResetCircuitBreaker()
    {
      _circuitBreaker.Reset();
    }

    private async Task<GatewayResult<T>> HandleResponseAsync<T>(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
      var correlationId = GetCorrelationId(response);

      if (response.IsSuccessStatusCode)
      {
        try
        {
          var data = await response.Content.ReadFromJsonAsync<T>(_jsonOptions, cancellationToken);
          if (data is null)
          {
            return GatewayResult<T>.Fail(
                GatewayError.ServerError("Response deserialization returned null"),
                correlationId);
          }
          return GatewayResult<T>.Ok(data, correlationId);
        }
        catch (JsonException ex)
        {
          ErrorLogger.LogWarning($"JSON deserialization failed: {ex.Message}", "BackendTransport");
          return GatewayResult<T>.Fail(
              new GatewayError("DESERIALIZATION_ERROR", ex.Message, null, false),
              correlationId);
        }
      }

      var error = await CreateErrorFromResponseAsync(response, cancellationToken);
      return GatewayResult<T>.Fail(error, correlationId);
    }

    private async Task<GatewayError> CreateErrorFromResponseAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
      var statusCode = (int)response.StatusCode;
      var parsed = await StandardErrorResponseParser.ParseAsync(response, _jsonOptions, cancellationToken);

      var errorCode = parsed.ErrorCode ?? statusCode switch
      {
        400 => "VALIDATION_ERROR",
        401 => "AUTHENTICATION_FAILED",
        404 => "NOT_FOUND",
        422 => "VALIDATION_ERROR",
        >= 500 => "SERVER_ERROR",
        _ => "UNKNOWN_ERROR"
      };

      return new GatewayError(
          errorCode,
          parsed.Message,
          statusCode,
          parsed.IsRetryable,
          parsed.RecoverySuggestion,
          parsed.Details,
          parsed.RequestId,
          parsed.Timestamp,
          parsed.Path);
    }

    private async Task<GatewayResult<T>> ExecuteAsync<T>(
        Func<Task<GatewayResult<T>>> operation,
        CancellationToken cancellationToken)
    {
      await UpdateConnectionStatusAsync(cancellationToken);

      try
      {
        return await _circuitBreaker.ExecuteAsync(async () =>
        {
          return await RetryHelper.ExecuteWithExponentialBackoffAsync(
              operation,
              maxRetries: _maxRetries,
              initialDelayMs: _retryDelayMs,
              maxDelayMs: 10000,
              cancellationToken: cancellationToken);
        });
      }
      catch (BackendException ex)
      {
        await UpdateConnectionStatusAsync(cancellationToken);
        return GatewayResult<T>.Fail(ex);
      }
      catch (HttpRequestException)
      {
        SetConnected(false);
        return GatewayResult<T>.Fail(new GatewayError(
            "BACKEND_UNAVAILABLE",
            "Unable to connect to the backend server. Please check your connection.",
            null,
            true,
            "Ensure the backend is running and try again."));
      }
      catch (TaskCanceledException ex) when (!ex.CancellationToken.IsCancellationRequested)
      {
        SetConnected(false);
        return GatewayResult<T>.Fail(new GatewayError(
            "TIMEOUT",
            "The request timed out. Please check your network connection.",
            null,
            true));
      }
      catch (Exception ex)
      {
        ErrorLogger.LogError($"Unexpected error in transport: {ex.Message}", "BackendTransport");
        return GatewayResult<T>.Fail(new GatewayError(
            "UNEXPECTED_ERROR",
            ex.Message,
            null,
            false));
      }
    }

    private async Task UpdateConnectionStatusAsync(CancellationToken cancellationToken)
    {
      var now = DateTime.UtcNow;
      if ((now - _lastConnectionCheck).TotalSeconds < _connectionCheckIntervalSeconds)
        return;

      _lastConnectionCheck = now;

      try
      {
        var response = await _httpClient.GetAsync("/api/health", cancellationToken);
        SetConnected(response.IsSuccessStatusCode);
      }
      catch
      {
        SetConnected(false);
      }
    }

    private void SetConnected(bool connected)
    {
      if (_isConnected != connected)
      {
        _isConnected = connected;
        ConnectionStatusChanged?.Invoke(this, connected);
      }
    }

    private static string GetCorrelationId(HttpResponseMessage response)
    {
      if (response.Headers.TryGetValues("X-Correlation-Id", out var values))
      {
        using var enumerator = values.GetEnumerator();
        if (enumerator.MoveNext())
          return enumerator.Current;
      }
      return Guid.NewGuid().ToString("N");
    }

    public void Dispose()
    {
      if (!_disposed)
      {
        // Note: We don't dispose the HttpClient as it may be shared
        _disposed = true;
      }
    }

    /// <summary>
    /// Progress-reporting stream wrapper.
    /// </summary>
    private sealed class ProgressStream : Stream
    {
      private readonly Stream _inner;
      private readonly Action<long, long> _progress;
      private long _bytesRead;

      public ProgressStream(Stream inner, Action<long, long> progress)
      {
        _inner = inner;
        _progress = progress;
      }

      public override bool CanRead => _inner.CanRead;
      public override bool CanSeek => _inner.CanSeek;
      public override bool CanWrite => _inner.CanWrite;
      public override long Length => _inner.Length;
      public override long Position
      {
        get => _inner.Position;
        set => _inner.Position = value;
      }

      public override void Flush() => _inner.Flush();
      public override long Seek(long offset, SeekOrigin origin) => _inner.Seek(offset, origin);
      public override void SetLength(long value) => _inner.SetLength(value);
      public override void Write(byte[] buffer, int offset, int count) => _inner.Write(buffer, offset, count);

      public override int Read(byte[] buffer, int offset, int count)
      {
        var read = _inner.Read(buffer, offset, count);
        _bytesRead += read;
        _progress(_bytesRead, Length);
        return read;
      }
    }
  }
}
