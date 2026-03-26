using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using VoiceStudio.App.Logging;
using VoiceStudio.App.Utilities;
using VoiceStudio.Core.Exceptions;

namespace VoiceStudio.App.Services
{
  /// <summary>
  /// HTTP policy core for <see cref="BackendClient"/>: retry, circuit breaker, connection bookkeeping,
  /// error mapping, and generic JSON request helpers. Extracted PR-1 — throw-based contract preserved.
  /// </summary>
  internal sealed class BackendClientHttpPipeline
  {
    private const int MaxRetries = 3;
    private const int RetryDelayMs = 1000;
    private const int ConnectionCheckIntervalSeconds = 5;

    private readonly HttpClient _httpClient;
    private readonly JsonSerializerOptions _jsonOptions;
    private readonly CircuitBreaker _circuitBreaker;

    private bool _isConnected = true;
    private DateTime _lastConnectionCheck = DateTime.MinValue;

    public BackendClientHttpPipeline(HttpClient httpClient, JsonSerializerOptions jsonOptions)
    {
      _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
      _jsonOptions = jsonOptions ?? throw new ArgumentNullException(nameof(jsonOptions));
      _circuitBreaker = new CircuitBreaker(failureThreshold: 5, timeout: TimeSpan.FromSeconds(30));
    }

    public bool IsConnected => _isConnected;

    public CircuitState CircuitState => _circuitBreaker.State;

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

    public async Task<T> ExecuteWithRetryAsync<T>(Func<Task<T>> operation, int maxRetries = MaxRetries)
    {
      await UpdateConnectionStatusAsync();

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
        await UpdateConnectionStatusAsync();

        if (ex is BackendException)
        {
          throw;
        }

        if (ex is HttpRequestException httpEx)
        {
          _isConnected = false;
          throw new BackendUnavailableException(
              "Unable to connect to the backend server. Please check your connection and ensure the backend is running.",
              httpEx);
        }

        if (ex is TaskCanceledException timeoutEx && !timeoutEx.CancellationToken.IsCancellationRequested)
        {
          _isConnected = false;
          throw new BackendTimeoutException(
              "The request timed out. Please check your network connection and try again.",
              timeoutEx);
        }

        throw;
      }
    }

    private async Task UpdateConnectionStatusAsync()
    {
      var now = DateTime.UtcNow;
      if ((now - _lastConnectionCheck).TotalSeconds < ConnectionCheckIntervalSeconds)
        return;

      _lastConnectionCheck = now;

      try
      {
        var response = await _httpClient.GetAsync("/api/health", CancellationToken.None);
        _isConnected = response.IsSuccessStatusCode;
      }
      catch (Exception)
      {
        _isConnected = false;
      }
    }

    public async Task<BackendException> CreateExceptionFromResponseAsync(HttpResponseMessage response)
    {
      var parsed = await StandardErrorResponseParser.ParseAsync(response, _jsonOptions, CancellationToken.None);

      BackendException exception = (int)response.StatusCode switch
      {
        400 => new BackendValidationException(parsed.Message),
        401 => new BackendAuthenticationException(parsed.Message),
        404 => new BackendNotFoundException(parsed.Message),
        422 => new BackendValidationException(parsed.Message),
        >= 500 => new BackendServerException(parsed.Message, (int)response.StatusCode),
        _ => new BackendServerException(parsed.Message, (int)response.StatusCode)
      };

      exception.ErrorCode = parsed.ErrorCode;
      exception.RequestId = parsed.RequestId;
      exception.Timestamp = parsed.Timestamp;
      exception.Path = parsed.Path;
      exception.RecoverySuggestion = parsed.RecoverySuggestion;
      exception.IsRetryable = parsed.IsRetryable;

      return exception;
    }

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

    public async Task<TResponse?> SendRequestAsync<TRequest, TResponse>(
        string endpoint,
        TRequest? request,
        HttpMethod method,
        CancellationToken cancellationToken = default) where TResponse : class
    {
      return await ExecuteWithRetryAsync(async () =>
      {
        HttpResponseMessage response;

        if (method == HttpMethod.Get)
        {
          response = await _httpClient.GetAsync(endpoint, cancellationToken);
        }
        else if (method == HttpMethod.Post)
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
        else if (method == HttpMethod.Put)
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
        else if (method == HttpMethod.Delete)
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

        if (method == HttpMethod.Delete && response.Content.Headers.ContentLength == 0)
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

    /// <summary>
    /// GET endpoint returning raw string (e.g. JSON/CSV export). PR-3: used by PluginHealthClient.ExportMetricsAsync.
    /// </summary>
    public async Task<string> GetStringAsync(string endpoint, CancellationToken cancellationToken = default)
    {
      return await ExecuteWithRetryAsync(async () =>
      {
        var response = await _httpClient.GetAsync(endpoint, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
          throw await CreateExceptionFromResponseAsync(response);
        }

        return await response.Content.ReadAsStringAsync(cancellationToken);
      });
    }

    /// <summary>
    /// GET endpoint returning raw stream (e.g. binary download). PR-14: used by BackupRestoreClient.DownloadBackupAsync.
    /// </summary>
    public async Task<Stream> GetStreamAsync(string endpoint, CancellationToken cancellationToken = default)
    {
      return await ExecuteWithRetryAsync(async () =>
      {
        var response = await _httpClient.GetAsync(endpoint, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
          throw await CreateExceptionFromResponseAsync(response);
        }

        return await response.Content.ReadAsStreamAsync(cancellationToken);
      });
    }

    /// <summary>
    /// POST multipart form with file stream; returns deserialized JSON. PR-14: used by BackupRestoreClient.UploadBackupAsync.
    /// </summary>
    public async Task<T?> PostMultipartAsync<T>(
        string endpoint,
        Stream fileStream,
        string formFieldName,
        string fileName,
        IReadOnlyDictionary<string, string>? queryParams = null,
        CancellationToken cancellationToken = default) where T : class
    {
      return await ExecuteWithRetryAsync(async () =>
      {
        var streamContent = new StreamContent(fileStream);
        using var content = new MultipartFormDataContent();
        content.Add(streamContent, formFieldName, fileName);

        var url = endpoint;
        if (queryParams != null && queryParams.Count > 0)
        {
          var qs = string.Join("&", queryParams.Select(kv => $"{Uri.EscapeDataString(kv.Key)}={Uri.EscapeDataString(kv.Value)}"));
          url += (endpoint.Contains("?", StringComparison.Ordinal) ? "&" : "?") + qs;
        }

        var response = await _httpClient.PostAsync(url, content, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
          throw await CreateExceptionFromResponseAsync(response);
        }

        return await response.Content.ReadFromJsonAsync<T>(_jsonOptions, cancellationToken);
      });
    }

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

    public async Task PostAsync<TRequest>(
        string endpoint,
        TRequest request,
        CancellationToken cancellationToken = default)
    {
      await ExecuteWithRetryAsync(async () =>
      {
        var response = await _httpClient.PostAsJsonAsync(endpoint, request, _jsonOptions, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
          throw await CreateExceptionFromResponseAsync(response);
        }

        return true;
      });
    }

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
  }
}
