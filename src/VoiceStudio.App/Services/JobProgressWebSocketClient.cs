using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using VoiceStudio.Core.Models;
using VoiceStudio.Core.Services;
using VoiceStudio.App.Logging;

namespace VoiceStudio.App.Services
{
  /// <summary>
  /// Specialized WebSocket client for job progress updates.
  /// Implements React/TypeScript jobProgressClient pattern in C#.
  /// </summary>
  /// <remarks>
  /// GAP-009: Implements IJobProgressClient for DI compatibility.
  /// </remarks>
  public class JobProgressWebSocketClient : IJobProgressClient
  {
    private readonly IWebSocketService _webSocketService;
    private readonly JsonSerializerOptions _jsonOptions;
    private bool _isSubscribed;
    private bool _disposed;

    /// <summary>
    /// Event fired when job progress is updated.
    /// </summary>
    public event EventHandler<JobProgressUpdate>? ProgressUpdated;

    /// <summary>
    /// Event fired when a job status changes.
    /// </summary>
    public event EventHandler<JobStatusUpdate>? StatusChanged;

    /// <summary>
    /// Event fired when a job completes.
    /// </summary>
    public event EventHandler<JobCompletedUpdate>? JobCompleted;

    /// <summary>
    /// Event fired when a job fails.
    /// </summary>
    public event EventHandler<JobFailedUpdate>? JobFailed;

    public JobProgressWebSocketClient(IWebSocketService webSocketService)
    {
      _webSocketService = webSocketService ?? throw new ArgumentNullException(nameof(webSocketService));
      _jsonOptions = new JsonSerializerOptions
      {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
      };

      // Subscribe to WebSocket messages
      _webSocketService.MessageReceived += OnWebSocketMessageReceived;
    }

    /// <summary>
    /// Connects and subscribes to job progress updates.
    /// </summary>
    public async Task ConnectAsync(CancellationToken cancellationToken = default)
    {
      if (_disposed)
        throw new ObjectDisposedException(nameof(JobProgressWebSocketClient));

      if (!_webSocketService.IsConnected)
      {
        await _webSocketService.ConnectAsync(new[] { "batch", "training" }, cancellationToken);
      }

      if (!_isSubscribed)
      {
        await _webSocketService.SubscribeAsync("batch");
        await _webSocketService.SubscribeAsync("training");
        _isSubscribed = true;
      }
    }

    /// <summary>
    /// Gets whether the WebSocket client is connected and subscribed.
    /// </summary>
    public bool IsConnected => _webSocketService.IsConnected && _isSubscribed;

    /// <summary>
    /// Unsubscribes from job progress updates.
    /// </summary>
    public async Task DisconnectAsync()
    {
      if (_isSubscribed)
      {
        try
        {
          await _webSocketService.UnsubscribeAsync("batch");
          await _webSocketService.UnsubscribeAsync("training");
        }
        catch (Exception ex)
      {
        ErrorLogger.LogWarning($"Best effort operation failed: {ex.Message}", "JobProgressWebSocketClient.DisconnectAsync");
      }
        _isSubscribed = false;
      }
    }

    private void OnWebSocketMessageReceived(object? sender, WebSocketMessage message)
    {
      if (_disposed)
        return;

      // Only process job-related topics
      if (message.Topic != "batch" && message.Topic != "training")
        return;

      try
      {
        var payloadJson = JsonSerializer.Serialize(message.Payload, _jsonOptions);
        using var doc = JsonDocument.Parse(payloadJson);
        var root = doc.RootElement;

        // Determine message type
        var messageType = root.TryGetProperty("type", out var typeProp)
            ? typeProp.GetString()
            : null;

        messageType = string.IsNullOrWhiteSpace(messageType) ? "progress" : messageType;

        switch (messageType.ToLowerInvariant())
        {
          case "progress":
            HandleProgressUpdate(root);
            break;
          case "status":
            HandleStatusUpdate(root);
            break;
          case "completed":
            HandleJobCompleted(root);
            break;
          case "failed":
            HandleJobFailed(root);
            break;
        }
      }
      catch (Exception ex)
      {
        // Log error but don't throw
        System.Diagnostics.Debug.WriteLine($"Failed to process job progress message: {ex.Message}");
      }
    }

    private void HandleProgressUpdate(JsonElement root)
    {
      try
      {
        var update = JsonSerializer.Deserialize<JobProgressUpdate>(root.GetRawText(), _jsonOptions);
        if (update != null)
        {
          ProgressUpdated?.Invoke(this, update);
        }
      }
      catch (Exception ex)
      {
        ErrorLogger.LogWarning($"Best effort operation failed: {ex.Message}", "JobProgressWebSocketClient.HandleProgressUpdate");
      }
    }

    private void HandleStatusUpdate(JsonElement root)
    {
      try
      {
        var update = JsonSerializer.Deserialize<JobStatusUpdate>(root.GetRawText(), _jsonOptions);
        if (update != null)
        {
          StatusChanged?.Invoke(this, update);
        }
      }
      catch (Exception ex)
      {
        ErrorLogger.LogWarning($"Best effort operation failed: {ex.Message}", "JobProgressWebSocketClient.HandleStatusUpdate");
      }
    }

    private void HandleJobCompleted(JsonElement root)
    {
      try
      {
        var update = JsonSerializer.Deserialize<JobCompletedUpdate>(root.GetRawText(), _jsonOptions);
        if (update != null)
        {
          JobCompleted?.Invoke(this, update);
        }
      }
      catch (Exception ex)
      {
        ErrorLogger.LogWarning($"Best effort operation failed: {ex.Message}", "JobProgressWebSocketClient.HandleJobCompleted");
      }
    }

    private void HandleJobFailed(JsonElement root)
    {
      try
      {
        var update = JsonSerializer.Deserialize<JobFailedUpdate>(root.GetRawText(), _jsonOptions);
        if (update != null)
        {
          JobFailed?.Invoke(this, update);
        }
      }
      catch (Exception ex)
      {
        ErrorLogger.LogWarning($"Best effort operation failed: {ex.Message}", "JobProgressWebSocketClient.HandleJobFailed");
      }
    }

    public void Dispose()
    {
      if (!_disposed)
      {
        _disposed = true;
        _webSocketService.MessageReceived -= OnWebSocketMessageReceived;
        DisconnectAsync().GetAwaiter().GetResult();
      }
    }
  }

  /// <summary>
  /// Job progress update message.
  /// Handles both camelCase (JobId) and snake_case (job_id, batch_id) from backend.
  /// </summary>
  public class JobProgressUpdate
  {
    private string _jobId = string.Empty;
    private string _jobType = string.Empty;

    /// <summary>
    /// Job identifier. Accepts job_id, batch_id, or JobId from backend.
    /// </summary>
    public string JobId
    {
      get => _jobId;
      set => _jobId = value;
    }

    /// <summary>
    /// Alternative property for batch_id from backend.
    /// </summary>
    [System.Text.Json.Serialization.JsonPropertyName("batch_id")]
    public string? BatchId
    {
      get => null;
      set
      {
        if (!string.IsNullOrEmpty(value) && string.IsNullOrEmpty(_jobId))
          _jobId = value;
      }
    }

    /// <summary>
    /// Alternative property for snake_case job_id from backend.
    /// </summary>
    [System.Text.Json.Serialization.JsonPropertyName("job_id")]
    public string? JobIdSnakeCase
    {
      get => null;
      set
      {
        if (!string.IsNullOrEmpty(value) && string.IsNullOrEmpty(_jobId))
          _jobId = value;
      }
    }

    /// <summary>
    /// Job type (batch, training, synthesis, etc.).
    /// </summary>
    public string JobType
    {
      get => _jobType;
      set => _jobType = value ?? string.Empty;
    }

    /// <summary>
    /// Alternative property for snake_case job_type from backend.
    /// </summary>
    [System.Text.Json.Serialization.JsonPropertyName("job_type")]
    public string? JobTypeSnakeCase
    {
      get => null;
      set
      {
        if (!string.IsNullOrEmpty(value) && string.IsNullOrEmpty(_jobType))
          _jobType = value;
      }
    }

    public double Progress { get; set; } // 0.0 to 1.0
    public string? Status { get; set; }
    public string? Message { get; set; }
    public DateTime Timestamp { get; set; }
  }

  /// <summary>
  /// Job status update message.
  /// Handles both camelCase and snake_case from backend.
  /// </summary>
  public class JobStatusUpdate
  {
    private string _jobId = string.Empty;
    private string _jobType = string.Empty;

    public string JobId
    {
      get => _jobId;
      set => _jobId = value;
    }

    [System.Text.Json.Serialization.JsonPropertyName("job_id")]
    public string? JobIdSnakeCase
    {
      get => null;
      set
      {
        if (!string.IsNullOrEmpty(value) && string.IsNullOrEmpty(_jobId))
          _jobId = value;
      }
    }

    public string JobType
    {
      get => _jobType;
      set => _jobType = value ?? string.Empty;
    }

    [System.Text.Json.Serialization.JsonPropertyName("job_type")]
    public string? JobTypeSnakeCase
    {
      get => null;
      set
      {
        if (!string.IsNullOrEmpty(value) && string.IsNullOrEmpty(_jobType))
          _jobType = value;
      }
    }

    public string Status { get; set; } = string.Empty; // "pending", "running", "paused", "cancelled"
    public string? Message { get; set; }
    public DateTime Timestamp { get; set; }
  }

  /// <summary>
  /// Job completed update message.
  /// Handles both camelCase and snake_case from backend.
  /// </summary>
  public class JobCompletedUpdate
  {
    private string _jobId = string.Empty;
    private string _jobType = string.Empty;

    public string JobId
    {
      get => _jobId;
      set => _jobId = value;
    }

    [System.Text.Json.Serialization.JsonPropertyName("job_id")]
    public string? JobIdSnakeCase
    {
      get => null;
      set
      {
        if (!string.IsNullOrEmpty(value) && string.IsNullOrEmpty(_jobId))
          _jobId = value;
      }
    }

    public string JobType
    {
      get => _jobType;
      set => _jobType = value ?? string.Empty;
    }

    [System.Text.Json.Serialization.JsonPropertyName("job_type")]
    public string? JobTypeSnakeCase
    {
      get => null;
      set
      {
        if (!string.IsNullOrEmpty(value) && string.IsNullOrEmpty(_jobType))
          _jobType = value;
      }
    }

    public string? ResultId { get; set; }

    [System.Text.Json.Serialization.JsonPropertyName("result_id")]
    public string? ResultIdSnakeCase
    {
      get => null;
      set
      {
        if (!string.IsNullOrEmpty(value) && string.IsNullOrEmpty(ResultId))
          ResultId = value;
      }
    }

    public string? Message { get; set; }
    public DateTime Timestamp { get; set; }
  }

  /// <summary>
  /// Job failed update message.
  /// Handles both camelCase and snake_case from backend.
  /// </summary>
  public class JobFailedUpdate
  {
    private string _jobId = string.Empty;
    private string _jobType = string.Empty;

    public string JobId
    {
      get => _jobId;
      set => _jobId = value;
    }

    [System.Text.Json.Serialization.JsonPropertyName("job_id")]
    public string? JobIdSnakeCase
    {
      get => null;
      set
      {
        if (!string.IsNullOrEmpty(value) && string.IsNullOrEmpty(_jobId))
          _jobId = value;
      }
    }

    public string JobType
    {
      get => _jobType;
      set => _jobType = value ?? string.Empty;
    }

    [System.Text.Json.Serialization.JsonPropertyName("job_type")]
    public string? JobTypeSnakeCase
    {
      get => null;
      set
      {
        if (!string.IsNullOrEmpty(value) && string.IsNullOrEmpty(_jobType))
          _jobType = value;
      }
    }

    public string Error { get; set; } = string.Empty;
    public string? Details { get; set; }
    public DateTime Timestamp { get; set; }
  }
}