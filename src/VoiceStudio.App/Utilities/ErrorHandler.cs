using System;
using System.Net.Http;
using System.Threading.Tasks;
using System.Diagnostics;
using VoiceStudio.Core.Exceptions;

namespace VoiceStudio.App.Utilities
{
  /// <summary>
  /// Centralized error handling utility for consistent error processing across the application.
  /// </summary>
  public static class ErrorHandler
  {
    /// <summary>
    /// Processes an exception and returns a user-friendly error message.
    /// </summary>
    public static string GetUserFriendlyMessage(Exception ex)
    {
      if (ex == null)
        return "An unknown error occurred.";

      // Log the full exception for debugging
      Debug.WriteLine($"Error: {ex.GetType().Name} - {ex.Message}");
      Debug.WriteLine($"Stack Trace: {ex.StackTrace}");

      return ex switch
      {
        // BackendException types (most specific first)
        BackendUnavailableException bex => bex.Message,
        BackendTimeoutException bex => bex.Message,
        BackendAuthenticationException bex => bex.Message,
        BackendNotFoundException bex => bex.Message,
        BackendValidationException bex => bex.Message,
        BackendServerException bex => bex.Message,
        BackendDeserializationException bex => bex.Message,
        BackendException bex => bex.Message,

        // Standard .NET exceptions
        HttpRequestException httpEx => GetHttpErrorMessage(httpEx),
        TaskCanceledException => "The operation was cancelled or timed out. Please try again.",
        TimeoutException => "The operation timed out. Please check your connection and try again.",
        ArgumentNullException argEx => $"Missing required parameter: {argEx.ParamName ?? "unknown"}",
        ArgumentException argEx => $"Invalid parameter: {argEx.Message}",
        InvalidOperationException invOpEx => invOpEx.Message,
        UnauthorizedAccessException => "You don't have permission to perform this operation.",
        _ => $"An error occurred: {ex.Message}"
      };
    }

    /// <summary>
    /// Gets a user-friendly message for HTTP exceptions.
    /// </summary>
    private static string GetHttpErrorMessage(HttpRequestException ex)
    {
      // Check if there's an inner exception with status code
      if (ex.Data.Contains("StatusCode"))
      {
        var statusCode = ex.Data["StatusCode"]?.ToString();
        return statusCode switch
        {
          "400" => "Invalid request. Please check your input and try again.",
          "401" => "Authentication failed. Please check your credentials.",
          "403" => "Access denied. You don't have permission for this operation.",
          "404" => "The requested resource was not found.",
          "408" => "Request timed out. Please try again.",
          "429" => "Too many requests. Please wait a moment and try again.",
          "500" => "Server error. Please try again later.",
          "502" => "Bad gateway. The server is temporarily unavailable.",
          "503" => "Service unavailable. The server is temporarily down.",
          "504" => "Gateway timeout. Please try again later.",
          _ => $"Network error ({statusCode}): {ex.Message}"
        };
      }

      // Check for connection issues
      if (ex.Message.Contains("connection", StringComparison.OrdinalIgnoreCase) ||
          ex.Message.Contains("network", StringComparison.OrdinalIgnoreCase) ||
          ex.Message.Contains("refused", StringComparison.OrdinalIgnoreCase))
      {
        return "Cannot connect to the server. Please check your connection and ensure the backend is running.";
      }

      return $"Network error: {ex.Message}";
    }

    /// <summary>
    /// Determines if an exception represents a transient error that might succeed on retry.
    /// </summary>
    public static bool IsTransientError(Exception ex)
    {
      return ex switch
      {
        BackendException bex => bex.IsRetryable,
        HttpRequestException httpEx =>
            httpEx.Message.Contains("timeout", StringComparison.OrdinalIgnoreCase) ||
            httpEx.Message.Contains("connection", StringComparison.OrdinalIgnoreCase) ||
            (httpEx.Data.Contains("StatusCode") &&
             httpEx.Data["StatusCode"]?.ToString() is "408" or "429" or "502" or "503" or "504"),
        TaskCanceledException => true,
        TimeoutException => true,
        _ => false
      };
    }

    /// <summary>
    /// Gets a suggestion for error recovery with actionable steps.
    /// </summary>
    public static string GetRecoverySuggestion(Exception ex)
    {
      return ex switch
      {
        BackendUnavailableException => "Make sure the backend server is running and accessible. Check if the server is started and the URL is correct.",
        BackendTimeoutException => "Check your network connection and try again. The operation may take longer than expected. Consider reducing the request size.",
        BackendAuthenticationException => "Please check your credentials and try again. If the problem persists, you may need to re-authenticate.",
        BackendNotFoundException => "The requested resource may have been deleted or moved. Please verify the resource exists and try again.",
        BackendValidationException => "Please check your input and try again. Review the error details for specific validation issues.",
        BackendServerException bex when bex.StatusCode >= 500 => "The server encountered an error. Please try again in a moment. If the problem persists, check the server logs.",
        BackendServerException bex when bex.StatusCode == 429 => "Too many requests. Please wait a few seconds before trying again. Consider reducing the request frequency.",
        HttpRequestException httpEx when httpEx.Message.Contains("connection", StringComparison.OrdinalIgnoreCase) =>
            "Make sure the backend server is running and accessible. Verify the server URL and network connectivity.",
        HttpRequestException httpEx when httpEx.Data.Contains("StatusCode") && httpEx.Data["StatusCode"]?.ToString() == "429" =>
            "Wait a few seconds before trying again. The server is rate-limiting requests.",
        TaskCanceledException => "The operation took too long. Try again with a simpler request or check your network connection.",
        TimeoutException => "Check your network connection and try again. The server may be overloaded or your connection may be slow.",
        _ => "Please try again. If the problem persists, check the logs for more details or contact support."
      };
    }

    /// <summary>
    /// Gets a detailed error message with recovery suggestion.
    /// </summary>
    public static string GetDetailedErrorMessage(Exception ex)
    {
      var message = GetUserFriendlyMessage(ex);
      var suggestion = GetRecoverySuggestion(ex);

      return $"{message}\n\nSuggestion: {suggestion}";
    }

    // IsTransientError is already defined above (line 89) - duplicate removed

    /// <summary>
    /// Logs an error with full context for debugging.
    /// </summary>
    public static void LogError(Exception ex, string context = "")
    {
      var contextMsg = string.IsNullOrWhiteSpace(context) ? "" : $" [{context}]";
      Debug.WriteLine($"[ERROR{contextMsg}] {ex.GetType().Name}: {ex.Message}");
      if (ex.InnerException != null)
      {
        Debug.WriteLine($"  Inner Exception: {ex.InnerException.GetType().Name}: {ex.InnerException.Message}");
      }
      Debug.WriteLine($"  Stack Trace: {ex.StackTrace}");
    }
  }
}