using System;
using System.Net.Http;
using System.Threading.Tasks;
using System.Diagnostics;
using VoiceStudio.Core.Exceptions;
using VoiceStudio.Core.Models;

namespace VoiceStudio.App.Utilities
{
  /// <summary>
  /// Centralized error handling utility for consistent error processing across the application.
  /// GAP-064: User-facing copy for synthesis/SSML and general paths flows through <see cref="ActionableErrorTranslator"/>.
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

      Debug.WriteLine($"Error: {ex.GetType().Name} - {ex.Message}");
      Debug.WriteLine($"Stack Trace: {ex.StackTrace}");

      return ActionableErrorTranslator.Translate(ex, ActionableOperationContext.General).PrimaryMessage;
    }

    /// <summary>
    /// GAP-064: User-facing primary message for a specific operation (synthesis, SSML preview, etc.).
    /// </summary>
    public static string GetUserFriendlyMessage(Exception ex, ActionableOperationContext context)
    {
      if (ex == null)
        return "An unknown error occurred.";

      Debug.WriteLine($"Error: {ex.GetType().Name} - {ex.Message}");
      Debug.WriteLine($"Stack Trace: {ex.StackTrace}");

      return ActionableErrorTranslator.Translate(ex, context).PrimaryMessage;
    }

    /// <summary>
    /// Returns true when the exception indicates backend stress (429, 502, 503, 504,
    /// BackendUnavailableException, BackendTimeoutException, timeouts).
    /// Used to enter degraded mode and show persistent banner instead of toast spray.
    /// </summary>
    public static bool IsBackendStressException(Exception? ex)
    {
      if (ex == null)
        return false;
      if (ex is BackendServerException bex && (bex.StatusCode == 429 || bex.StatusCode == 502 || bex.StatusCode == 503 || bex.StatusCode == 504))
        return true;
      if (ex is BackendException be && be.StatusCode is 429 or 502 or 503 or 504)
        return true;
      if (ex is HttpRequestException httpEx && httpEx.Data.Contains("StatusCode"))
      {
        var code = httpEx.Data["StatusCode"]?.ToString();
        if (code is "429" or "502" or "503" or "504")
          return true;
      }
      return ex is
          BackendUnavailableException or
          BackendTimeoutException or
          TimeoutException or
          TaskCanceledException;
    }

    /// <summary>
    /// Returns true when the exception indicates HTTP 429 (rate limit).
    /// Used to show non-blocking toast instead of modal.
    /// </summary>
    public static bool IsRateLimitException(Exception? ex)
    {
      if (ex == null)
        return false;
      if (ex is BackendServerException bex && bex.StatusCode == 429)
        return true;
      if (ex is HttpRequestException httpEx && httpEx.Data.Contains("StatusCode") && httpEx.Data["StatusCode"]?.ToString() == "429")
        return true;
      if (ex is BackendException be && be.StatusCode == 429)
        return true;
      return false;
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
      return ActionableErrorTranslator.Translate(ex, ActionableOperationContext.General).RecommendedAction;
    }

    /// <summary>
    /// Gets a detailed error message with recovery suggestion.
    /// </summary>
    public static string GetDetailedErrorMessage(Exception ex)
    {
      var info = ActionableErrorTranslator.Translate(ex, ActionableOperationContext.General);
      var suggestion = info.RecommendedAction;
      if (!string.IsNullOrWhiteSpace(info.SecondaryDetail))
        return $"{info.PrimaryMessage}\n\n{info.SecondaryDetail}\n\nSuggestion: {suggestion}";
      return $"{info.PrimaryMessage}\n\nSuggestion: {suggestion}";
    }

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
