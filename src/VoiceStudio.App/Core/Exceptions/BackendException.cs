using System;

namespace VoiceStudio.Core.Exceptions
{
  /// <summary>
  /// Base exception for backend-related errors.
  /// Supports standardized error response format from backend API (StandardErrorResponse).
  /// </summary>
  public class BackendException : Exception
  {
    public int? StatusCode { get; set; }
    public string? ErrorCode { get; set; }
    public bool IsRetryable { get; set; }
    public string? RecoverySuggestion { get; set; }
    public string? RequestId { get; set; }
    public System.Text.Json.JsonElement? Details { get; set; }
    
    /// <summary>
    /// Gets or sets the timestamp when the error occurred (ISO 8601 format).
    /// Maps to backend timestamp field.
    /// </summary>
    public string? Timestamp { get; set; }
    
    /// <summary>
    /// Gets or sets the API path where the error occurred.
    /// Maps to backend path field.
    /// </summary>
    public string? Path { get; set; }

    public BackendException(string message) : base(message)
    {
      IsRetryable = false;
    }

    public BackendException(string message, Exception innerException)
        : base(message, innerException)
    {
      IsRetryable = false;
    }

    public BackendException(string message, int? statusCode, string? errorCode = null, bool isRetryable = false)
        : base(message)
    {
      StatusCode = statusCode;
      ErrorCode = errorCode;
      IsRetryable = isRetryable;
    }

    public BackendException(string message, int? statusCode, Exception innerException, string? errorCode = null, bool isRetryable = false)
        : base(message, innerException)
    {
      StatusCode = statusCode;
      ErrorCode = errorCode;
      IsRetryable = isRetryable;
    }

    public BackendException(
        string message,
        int? statusCode,
        string? errorCode = null,
        bool isRetryable = false,
        string? recoverySuggestion = null,
        string? requestId = null,
        System.Text.Json.JsonElement? details = null,
        string? timestamp = null,
        string? path = null)
        : base(message)
    {
      StatusCode = statusCode;
      ErrorCode = errorCode;
      IsRetryable = isRetryable;
      RecoverySuggestion = recoverySuggestion;
      RequestId = requestId;
      Details = details;
      Timestamp = timestamp;
      Path = path;
    }

    public BackendException() : base()
    {
    }
  }

  /// <summary>
  /// Exception thrown when the backend server is unavailable or unreachable.
  /// </summary>
  public class BackendUnavailableException : BackendException
  {
    public BackendUnavailableException(string message)
        : base(message, null, "BACKEND_UNAVAILABLE", isRetryable: true)
    {
    }

    public BackendUnavailableException(string message, Exception innerException)
        : base(message, null, innerException, "BACKEND_UNAVAILABLE", isRetryable: true)
    {
    }

    public BackendUnavailableException(string message, int? statusCode, string? errorCode = null, bool isRetryable = false) : base(message, statusCode, errorCode, isRetryable)
    {
    }

    public BackendUnavailableException(string message, int? statusCode, Exception innerException, string? errorCode = null, bool isRetryable = false) : base(message, statusCode, innerException, errorCode, isRetryable)
    {
    }

    public BackendUnavailableException(string message, int? statusCode, string? errorCode = null, bool isRetryable = false, string? recoverySuggestion = null, string? requestId = null, System.Text.Json.JsonElement? details = null) : base(message, statusCode, errorCode, isRetryable, recoverySuggestion, requestId, details)
    {
    }
  }

  /// <summary>
  /// Exception thrown when a request times out.
  /// </summary>
  public class BackendTimeoutException : BackendException
  {
    public BackendTimeoutException(string message)
        : base(message, null, "BACKEND_TIMEOUT", isRetryable: true)
    {
    }

    public BackendTimeoutException(string message, Exception innerException)
        : base(message, null, innerException, "BACKEND_TIMEOUT", isRetryable: true)
    {
    }

    public BackendTimeoutException(string message, int? statusCode, string? errorCode = null, bool isRetryable = false) : base(message, statusCode, errorCode, isRetryable)
    {
    }

    public BackendTimeoutException(string message, int? statusCode, Exception innerException, string? errorCode = null, bool isRetryable = false) : base(message, statusCode, innerException, errorCode, isRetryable)
    {
    }

    public BackendTimeoutException(string message, int? statusCode, string? errorCode = null, bool isRetryable = false, string? recoverySuggestion = null, string? requestId = null, System.Text.Json.JsonElement? details = null) : base(message, statusCode, errorCode, isRetryable, recoverySuggestion, requestId, details)
    {
    }
  }

  /// <summary>
  /// Exception thrown when authentication fails.
  /// </summary>
  public class BackendAuthenticationException : BackendException
  {
    public BackendAuthenticationException(string message)
        : base(message, 401, "AUTHENTICATION_FAILED", isRetryable: false)
    {
    }

    public BackendAuthenticationException(string message, Exception innerException) : base(message, innerException)
    {
    }

    public BackendAuthenticationException(string message, int? statusCode, string? errorCode = null, bool isRetryable = false) : base(message, statusCode, errorCode, isRetryable)
    {
    }

    public BackendAuthenticationException(string message, int? statusCode, Exception innerException, string? errorCode = null, bool isRetryable = false) : base(message, statusCode, innerException, errorCode, isRetryable)
    {
    }

    public BackendAuthenticationException(string message, int? statusCode, string? errorCode = null, bool isRetryable = false, string? recoverySuggestion = null, string? requestId = null, System.Text.Json.JsonElement? details = null) : base(message, statusCode, errorCode, isRetryable, recoverySuggestion, requestId, details)
    {
    }
  }

  /// <summary>
  /// Thrown when the backend requires voice/profile consent before synthesis or related operations (HTTP 403).
  /// </summary>
  public class ConsentRequiredException : BackendException
  {
    public ConsentRequiredException(string message)
        : base(message, 403, "CONSENT_REQUIRED", isRetryable: false)
    {
    }

    public ConsentRequiredException(string message, Exception innerException)
        : base(message, 403, innerException, "CONSENT_REQUIRED", isRetryable: false)
    {
    }
  }

  /// <summary>
  /// Exception thrown when a resource is not found.
  /// </summary>
  public class BackendNotFoundException : BackendException
  {
    public BackendNotFoundException(string message)
        : base(message, 404, "RESOURCE_NOT_FOUND", isRetryable: false)
    {
    }

    public BackendNotFoundException(string message, Exception innerException)
        : base(message, 404, innerException, "RESOURCE_NOT_FOUND", isRetryable: false)
    {
    }

    public BackendNotFoundException(string resourceType, string resourceId)
        : base($"{resourceType} '{resourceId}' was not found.", 404, "RESOURCE_NOT_FOUND", isRetryable: false)
    {
    }

    public BackendNotFoundException(string message, int? statusCode, string? errorCode = null, bool isRetryable = false) : base(message, statusCode, errorCode, isRetryable)
    {
    }

    public BackendNotFoundException(string message, int? statusCode, Exception innerException, string? errorCode = null, bool isRetryable = false) : base(message, statusCode, innerException, errorCode, isRetryable)
    {
    }

    public BackendNotFoundException(string message, int? statusCode, string? errorCode = null, bool isRetryable = false, string? recoverySuggestion = null, string? requestId = null, System.Text.Json.JsonElement? details = null) : base(message, statusCode, errorCode, isRetryable, recoverySuggestion, requestId, details)
    {
    }
  }

  /// <summary>
  /// Exception thrown when a request is invalid.
  /// </summary>
  public class BackendValidationException : BackendException
  {
    public BackendValidationException(string message)
        : base(message, 400, "VALIDATION_ERROR", isRetryable: false)
    {
    }

    public BackendValidationException(string message, Exception innerException)
        : base(message, 400, innerException, "VALIDATION_ERROR", isRetryable: false)
    {
    }

    public BackendValidationException(string message, int? statusCode, string? errorCode = null, bool isRetryable = false) : base(message, statusCode, errorCode, isRetryable)
    {
    }

    public BackendValidationException(string message, int? statusCode, Exception innerException, string? errorCode = null, bool isRetryable = false) : base(message, statusCode, innerException, errorCode, isRetryable)
    {
    }

    public BackendValidationException(string message, int? statusCode, string? errorCode = null, bool isRetryable = false, string? recoverySuggestion = null, string? requestId = null, System.Text.Json.JsonElement? details = null) : base(message, statusCode, errorCode, isRetryable, recoverySuggestion, requestId, details)
    {
    }
  }

  /// <summary>
  /// Exception thrown when the server returns an error.
  /// </summary>
  public class BackendServerException : BackendException
  {
    public BackendServerException(string message, int statusCode)
        : base(message, statusCode, "SERVER_ERROR", isRetryable: statusCode >= 500)
    {
    }

    public BackendServerException(string message, int statusCode, Exception innerException)
        : base(message, statusCode, innerException, "SERVER_ERROR", isRetryable: statusCode >= 500)
    {
    }

    public BackendServerException(string message) : base(message)
    {
    }

    public BackendServerException(string message, Exception innerException) : base(message, innerException)
    {
    }

    public BackendServerException(string message, int? statusCode, string? errorCode = null, bool isRetryable = false) : base(message, statusCode, errorCode, isRetryable)
    {
    }

    public BackendServerException(string message, int? statusCode, Exception innerException, string? errorCode = null, bool isRetryable = false) : base(message, statusCode, innerException, errorCode, isRetryable)
    {
    }

    public BackendServerException(string message, int? statusCode, string? errorCode = null, bool isRetryable = false, string? recoverySuggestion = null, string? requestId = null, System.Text.Json.JsonElement? details = null) : base(message, statusCode, errorCode, isRetryable, recoverySuggestion, requestId, details)
    {
    }
  }

  /// <summary>
  /// Exception thrown when deserialization fails.
  /// </summary>
  public class BackendDeserializationException : BackendException
  {
    public BackendDeserializationException(string message)
        : base(message, null, "DESERIALIZATION_ERROR", isRetryable: false)
    {
    }

    public BackendDeserializationException(string message, Exception innerException)
        : base(message, null, innerException, "DESERIALIZATION_ERROR", isRetryable: false)
    {
    }

    public BackendDeserializationException(string message, int? statusCode, string? errorCode = null, bool isRetryable = false) : base(message, statusCode, errorCode, isRetryable)
    {
    }

    public BackendDeserializationException(string message, int? statusCode, Exception innerException, string? errorCode = null, bool isRetryable = false) : base(message, statusCode, innerException, errorCode, isRetryable)
    {
    }

    public BackendDeserializationException(string message, int? statusCode, string? errorCode = null, bool isRetryable = false, string? recoverySuggestion = null, string? requestId = null, System.Text.Json.JsonElement? details = null) : base(message, statusCode, errorCode, isRetryable, recoverySuggestion, requestId, details)
    {
    }
  }
}