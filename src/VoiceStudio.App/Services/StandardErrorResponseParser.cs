using System;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using VoiceStudio.App.Logging;
using VoiceStudio.App.Utilities;

namespace VoiceStudio.App.Services
{
  /// <summary>
  /// Shared parser for backend StandardErrorResponse JSON. Used by BackendClientHttpPipeline
  /// and BackendTransport to avoid duplicate error-parsing logic (PR-2 dedupe).
  /// </summary>
  internal static class StandardErrorResponseParser
  {
    /// <summary>
    /// Parsed result from a backend error response. Callers map to BackendException or GatewayError.
    /// </summary>
    internal sealed record ParsedErrorResponse(
      string Message,
      string? ErrorCode,
      string? RequestId,
      string? Timestamp,
      string? Path,
      string? RecoverySuggestion,
      bool IsRetryable,
      JsonElement? Details);

    /// <summary>
    /// Parses an HTTP error response into a structured DTO. Handles malformed JSON by
    /// using truncated content as the message.
    /// </summary>
    public static async Task<ParsedErrorResponse> ParseAsync(
      HttpResponseMessage response,
      JsonSerializerOptions? jsonOptions = null,
      CancellationToken cancellationToken = default)
    {
      var statusCode = (int)response.StatusCode;
      string? message = null;
      string? errorCode = null;
      string? requestId = null;
      string? timestamp = null;
      string? path = null;
      string? recoverySuggestion = null;
      JsonElement? details = null;

      try
      {
        var content = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!string.IsNullOrEmpty(content))
        {
          try
          {
            var errorJson = JsonSerializer.Deserialize<JsonElement>(content, jsonOptions ?? JsonSerializerOptionsFactory.BackendApi);
            if (errorJson.TryGetProperty("message", out var messageProp))
              message = messageProp.GetString();
            if (errorJson.TryGetProperty("error", out var errorProp) && errorProp.ValueKind == JsonValueKind.String)
              message = errorProp.GetString() ?? message;
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
            if (errorJson.TryGetProperty("details", out var detailsProp))
              details = detailsProp;
          }
          catch (JsonException)
          {
            message = content.Length > 200 ? content[..200] + "..." : content;
          }
        }
      }
      catch (Exception ex)
      {
        ErrorLogger.LogWarning($"Best effort error parsing failed: {ex.Message}", "StandardErrorResponseParser");
      }

      message ??= GetDefaultMessage(statusCode);
      var isRetryable = statusCode >= 500 || statusCode == 429;

      return new ParsedErrorResponse(
        message,
        errorCode,
        requestId,
        timestamp,
        path,
        recoverySuggestion,
        isRetryable,
        details);
    }

    private static string GetDefaultMessage(int statusCode) => statusCode switch
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
  }
}
