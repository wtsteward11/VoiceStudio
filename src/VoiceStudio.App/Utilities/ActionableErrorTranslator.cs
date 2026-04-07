using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using VoiceStudio.Core.Exceptions;
using VoiceStudio.Core.Models;

namespace VoiceStudio.App.Utilities
{
  /// <summary>
  /// GAP-064: Single authority for mapping transport/backend exceptions to actionable user copy.
  /// </summary>
  public static class ActionableErrorTranslator
  {
    /// <summary>
    /// When synthesis or SSML preview succeeds but SSML was stripped with warnings, returns a warning notice; otherwise null.
    /// </summary>
    public static ActionableErrorInfo? BuildSsmlHandlingUserNotice(SsmlHandlingDiagnostics? handling)
    {
      if (handling == null)
        return null;
      if (!string.Equals(handling.Action, "stripped_warned", StringComparison.OrdinalIgnoreCase))
        return null;

      var warnings = handling.Warnings ?? new List<string>();
      var secondary = warnings.Count > 0
          ? string.Join(Environment.NewLine, warnings.Where(static w => !string.IsNullOrWhiteSpace(w)))
          : null;

      return new ActionableErrorInfo
      {
        Title = "SSML adjusted",
        PrimaryMessage =
            "Some SSML markup was removed or normalized because the selected engine does not fully support it. Playback still uses the adjusted text.",
        SecondaryDetail = secondary,
        RecommendedAction =
            "Use a compatible engine, remove unsupported tags, or continue with plain text if the result sounds correct.",
        Severity = ActionableErrorSeverity.Warning,
        Class = ActionableErrorClass.CapabilityUnsupported,
        IsRetryable = false,
        Warnings = warnings.Count > 0 ? warnings : null
      };
    }

    /// <summary>
    /// GAP-050: When synthesis succeeded and emotion preset prosody authority reported skips or warnings, returns a warning notice; otherwise null.
    /// </summary>
    public static ActionableErrorInfo? BuildProsodyHandlingUserNotice(ProsodyHandlingDiagnosticsDto? handling)
    {
      if (handling == null)
        return null;

      var warnings = handling.Warnings ?? new List<string>();
      var skipped = handling.SkippedOperations ?? new List<Dictionary<string, string>>();
      var hasWarnings = warnings.Count > 0;
      var hasSkipped = skipped.Count > 0;
      if (!hasWarnings && !hasSkipped)
        return null;

      var secondaryParts = new List<string>();
      if (hasWarnings)
        secondaryParts.AddRange(warnings.Where(static w => !string.IsNullOrWhiteSpace(w)));
      if (hasSkipped)
      {
        foreach (var row in skipped)
        {
          if (row.TryGetValue("operation", out var op) && row.TryGetValue("reason", out var reason))
            secondaryParts.Add($"{op}: {reason}");
        }
      }

      var secondary = secondaryParts.Count > 0
          ? string.Join(Environment.NewLine, secondaryParts)
          : null;

      return new ActionableErrorInfo
      {
        Title = "Prosody / preset",
        PrimaryMessage =
            "The emotion preset was applied with some limits or adjustments. Playback uses the returned audio.",
        SecondaryDetail = secondary,
        RecommendedAction =
            "Review warnings above; try a different preset or engine if the result is not acceptable.",
        Severity = ActionableErrorSeverity.Warning,
        Class = ActionableErrorClass.CapabilityUnsupported,
        IsRetryable = false,
        Warnings = hasWarnings ? warnings : null
      };
    }

    /// <summary>
    /// GAP-050: Single combined warning body for SSML + prosody + preset-apply failure (one toast).
    /// </summary>
    public static ActionableErrorInfo? BuildSynthesisCapabilityCombinedNotice(
        SsmlHandlingDiagnostics? ssml,
        ProsodyHandlingDiagnosticsDto? prosody,
        string? emotionPresetApplyFailureMessage)
    {
      var ssmlNotice = BuildSsmlHandlingUserNotice(ssml);
      var prosodyNotice = BuildProsodyHandlingUserNotice(prosody);
      var hasFailure = !string.IsNullOrWhiteSpace(emotionPresetApplyFailureMessage);

      if (ssmlNotice == null && prosodyNotice == null && !hasFailure)
        return null;

      var blocks = new List<string>();
      if (ssmlNotice != null)
      {
        blocks.Add(string.IsNullOrWhiteSpace(ssmlNotice.SecondaryDetail)
            ? ssmlNotice.PrimaryMessage
            : $"{ssmlNotice.PrimaryMessage}{Environment.NewLine}{ssmlNotice.SecondaryDetail}");
      }

      if (prosodyNotice != null)
      {
        blocks.Add(string.IsNullOrWhiteSpace(prosodyNotice.SecondaryDetail)
            ? prosodyNotice.PrimaryMessage
            : $"{prosodyNotice.PrimaryMessage}{Environment.NewLine}{prosodyNotice.SecondaryDetail}");
      }

      if (hasFailure)
        blocks.Add(emotionPresetApplyFailureMessage!.Trim());

      return new ActionableErrorInfo
      {
        Title = "Synthesis note",
        PrimaryMessage = string.Join($"{Environment.NewLine}{Environment.NewLine}", blocks),
        SecondaryDetail = null,
        RecommendedAction = "Review the notes above and retry or adjust settings if needed.",
        Severity = ActionableErrorSeverity.Warning,
        Class = ActionableErrorClass.CapabilityUnsupported,
        IsRetryable = false
      };
    }

    /// <summary>
    /// Maps an exception to actionable presentation metadata for the given operation.
    /// </summary>
    public static ActionableErrorInfo Translate(
        Exception? ex,
        ActionableOperationContext context = ActionableOperationContext.General)
    {
      if (ex == null)
      {
        return Unknown(
            context,
            "An unknown error occurred.",
            "Try again. If the problem continues, check logs or restart the app.",
            retryable: false);
      }

      var root = Unwrap(ex);

      if (root is OperationCanceledException or TaskCanceledException)
      {
        return new ActionableErrorInfo
        {
          Title = TitleFor(context, ActionableErrorClass.TransientRetryable),
          PrimaryMessage = "The operation was cancelled or timed out.",
          RecommendedAction = "Retry if you did not intend to cancel.",
          Severity = ActionableErrorSeverity.Info,
          Class = ActionableErrorClass.TransientRetryable,
          IsRetryable = true
        };
      }

      if (root is HttpRequestException httpEx)
        return MapHttpRequestException(httpEx, context);

      if (root is BackendException bex)
        return MapBackendException(bex, context);

      if (root is TimeoutException)
      {
        return new ActionableErrorInfo
        {
          Title = TitleFor(context, ActionableErrorClass.TransientRetryable),
          PrimaryMessage = "The operation timed out.",
          SecondaryDetail = null,
          RecommendedAction = "Check your connection and try again. Reduce request size if applicable.",
          Severity = ActionableErrorSeverity.Error,
          Class = ActionableErrorClass.TransientRetryable,
          IsRetryable = true
        };
      }

      if (root is ArgumentNullException argNull)
      {
        return new ActionableErrorInfo
        {
          Title = TitleFor(context, ActionableErrorClass.ValidationInput),
          PrimaryMessage = $"Missing required value: {argNull.ParamName ?? "parameter"}.",
          RecommendedAction = "Fill in all required fields and try again.",
          Severity = ActionableErrorSeverity.Error,
          Class = ActionableErrorClass.ValidationInput,
          IsRetryable = false
        };
      }

      if (root is ArgumentException argEx)
      {
        return new ActionableErrorInfo
        {
          Title = TitleFor(context, ActionableErrorClass.ValidationInput),
          PrimaryMessage = string.IsNullOrWhiteSpace(argEx.Message) ? "Invalid input." : argEx.Message,
          RecommendedAction = "Correct the input and try again.",
          Severity = ActionableErrorSeverity.Error,
          Class = ActionableErrorClass.ValidationInput,
          IsRetryable = false
        };
      }

      if (root is UnauthorizedAccessException)
      {
        return new ActionableErrorInfo
        {
          Title = TitleFor(context, ActionableErrorClass.ValidationInput),
          PrimaryMessage = "You don't have permission for this operation.",
          RecommendedAction = "Check account permissions or run as an allowed user.",
          Severity = ActionableErrorSeverity.Error,
          Class = ActionableErrorClass.ValidationInput,
          IsRetryable = false
        };
      }

      if (root is OutOfMemoryException)
      {
        return new ActionableErrorInfo
        {
          Title = "Memory Error",
          PrimaryMessage = "The application ran out of memory.",
          RecommendedAction = "Close other applications and try again with a smaller request.",
          Severity = ActionableErrorSeverity.Error,
          Class = ActionableErrorClass.Unknown,
          IsRetryable = false
        };
      }

      // Fallback: never surface raw exception type strings as the only hint
      var fallback = string.IsNullOrWhiteSpace(root.Message)
          ? "Something went wrong."
          : root.Message;
      return Unknown(
          context,
          fallback,
          "Try again. If the problem persists, check logs or contact support.",
          retryable: false);
    }

    private static Exception Unwrap(Exception ex)
    {
      if (ex is InvalidOperationException && ex.InnerException is BackendException be)
        return be;
      return ex;
    }

    private static ActionableErrorInfo MapHttpRequestException(HttpRequestException httpEx, ActionableOperationContext context)
    {
      if (httpEx.Data.Contains("StatusCode"))
      {
        var code = httpEx.Data["StatusCode"]?.ToString();
        var (primary, cls, retry) = code switch
        {
          "400" => ("The server could not process this request. Check your input.", ActionableErrorClass.ValidationInput, false),
          "401" => ("Authentication failed.", ActionableErrorClass.ValidationInput, false),
          "403" => ("Access was denied for this operation.", ActionableErrorClass.ValidationInput, false),
          "404" => ("The requested resource was not found.", ActionableErrorClass.ValidationInput, false),
          "408" => ("The request timed out.", ActionableErrorClass.TransientRetryable, true),
          "422" => ("The input could not be validated. Fix SSML or text and try again.", ActionableErrorClass.ValidationInput, false),
          "429" => ("Too many requests. Please wait briefly and try again.", ActionableErrorClass.TransientRetryable, true),
          "500" => ("The server reported an error. Try again in a moment.", ActionableErrorClass.Unknown, true),
          "502" => ("The service is temporarily unreachable. Try again shortly.", ActionableErrorClass.EnvironmentUnavailable, true),
          "503" => ("The service is temporarily unavailable. Try again shortly.", ActionableErrorClass.EnvironmentUnavailable, true),
          "504" => ("The gateway timed out. Try again.", ActionableErrorClass.TransientRetryable, true),
          _ => ("We couldn't complete the request. Check that VoiceStudio is running and your network is working.", ActionableErrorClass.EnvironmentUnavailable, true)
        };

        return new ActionableErrorInfo
        {
          Title = TitleFor(context, cls),
          PrimaryMessage = primary,
          SecondaryDetail = null,
          RecommendedAction = RetryHint(retry),
          Severity = ActionableErrorSeverity.Error,
          Class = cls,
          IsRetryable = retry
        };
      }

      if (httpEx.Message.Contains("connection", StringComparison.OrdinalIgnoreCase) ||
          httpEx.Message.Contains("network", StringComparison.OrdinalIgnoreCase) ||
          httpEx.Message.Contains("refused", StringComparison.OrdinalIgnoreCase))
      {
        return new ActionableErrorInfo
        {
          Title = TitleFor(context, ActionableErrorClass.EnvironmentUnavailable),
          PrimaryMessage = "Cannot connect to VoiceStudio. Ensure the backend is running and reachable.",
          RecommendedAction = "Start the backend or check firewall and URL settings, then retry.",
          Severity = ActionableErrorSeverity.Error,
          Class = ActionableErrorClass.EnvironmentUnavailable,
          IsRetryable = true
        };
      }

      return new ActionableErrorInfo
      {
        Title = TitleFor(context, ActionableErrorClass.EnvironmentUnavailable),
        PrimaryMessage = "A network error occurred. Check connectivity and that the backend is running.",
        RecommendedAction = "Verify the backend process and network, then try again.",
        Severity = ActionableErrorSeverity.Error,
        Class = ActionableErrorClass.EnvironmentUnavailable,
        IsRetryable = true
      };
    }

    private static ActionableErrorInfo MapBackendException(BackendException bex, ActionableOperationContext context)
    {
      var code = bex.ErrorCode?.ToUpperInvariant();
      var status = bex.StatusCode;

      if (bex is BackendValidationException || status is 400 or 422 ||
          code is "VALIDATION_ERROR" or "INVALID_INPUT")
      {
        var primary = string.IsNullOrWhiteSpace(bex.Message)
            ? "The request was invalid."
            : bex.Message;
        var secondary = string.IsNullOrWhiteSpace(bex.RecoverySuggestion) ? null : bex.RecoverySuggestion;
        return new ActionableErrorInfo
        {
          Title = TitleFor(context, ActionableErrorClass.ValidationInput),
          PrimaryMessage = primary,
          SecondaryDetail = secondary,
          RecommendedAction = string.IsNullOrWhiteSpace(bex.RecoverySuggestion)
              ? "Review your text, SSML, and selections, then try again."
              : bex.RecoverySuggestion!,
          Severity = ActionableErrorSeverity.Error,
          Class = ActionableErrorClass.ValidationInput,
          IsRetryable = false
        };
      }

      if (bex is BackendNotFoundException || status == 404 ||
          code is "RESOURCE_NOT_FOUND" or "PROFILE_NOT_FOUND")
      {
        return new ActionableErrorInfo
        {
          Title = TitleFor(context, ActionableErrorClass.ValidationInput),
          PrimaryMessage = string.IsNullOrWhiteSpace(bex.Message)
              ? NotFoundPrimary(context)
              : bex.Message,
          SecondaryDetail = string.IsNullOrWhiteSpace(bex.RecoverySuggestion) ? null : bex.RecoverySuggestion,
          RecommendedAction = string.IsNullOrWhiteSpace(bex.RecoverySuggestion)
              ? "Pick an existing profile or engine and try again."
              : bex.RecoverySuggestion!,
          Severity = ActionableErrorSeverity.Error,
          Class = ActionableErrorClass.ValidationInput,
          IsRetryable = false
        };
      }

      if (bex is BackendAuthenticationException || status == 401 || code == "AUTHENTICATION_FAILED")
      {
        return new ActionableErrorInfo
        {
          Title = TitleFor(context, ActionableErrorClass.ValidationInput),
          PrimaryMessage = string.IsNullOrWhiteSpace(bex.Message)
              ? "Authentication failed."
              : bex.Message,
          RecommendedAction = "Check credentials and try again.",
          Severity = ActionableErrorSeverity.Error,
          Class = ActionableErrorClass.ValidationInput,
          IsRetryable = false
        };
      }

      if (status == 403 || code == "AUTHORIZATION_FAILED")
      {
        return new ActionableErrorInfo
        {
          Title = TitleFor(context, ActionableErrorClass.ValidationInput),
          PrimaryMessage = string.IsNullOrWhiteSpace(bex.Message)
              ? "You are not allowed to perform this operation."
              : bex.Message,
          RecommendedAction = "Check permissions or upgrade access if required.",
          Severity = ActionableErrorSeverity.Error,
          Class = ActionableErrorClass.ValidationInput,
          IsRetryable = false
        };
      }

      if (bex is BackendUnavailableException ||
          bex is BackendTimeoutException ||
          status == 503 ||
          code is "SERVICE_UNAVAILABLE" or "BACKEND_UNAVAILABLE" or "BACKEND_TIMEOUT")
      {
        return new ActionableErrorInfo
        {
          Title = TitleFor(context, ActionableErrorClass.EnvironmentUnavailable),
          PrimaryMessage = string.IsNullOrWhiteSpace(bex.Message)
              ? "VoiceStudio backend is unavailable or not responding."
              : bex.Message,
          SecondaryDetail = string.IsNullOrWhiteSpace(bex.RecoverySuggestion) ? null : bex.RecoverySuggestion,
          RecommendedAction = string.IsNullOrWhiteSpace(bex.RecoverySuggestion)
              ? "Ensure the backend is running, then retry."
              : bex.RecoverySuggestion!,
          Severity = ActionableErrorSeverity.Error,
          Class = ActionableErrorClass.EnvironmentUnavailable,
          IsRetryable = bex.IsRetryable || true
        };
      }

      if (status is 429 or 502 or 504 or 408 || (bex is BackendServerException se && se.StatusCode is 429 or 502 or 504 or 408))
      {
        return new ActionableErrorInfo
        {
          Title = TitleFor(context, ActionableErrorClass.TransientRetryable),
          PrimaryMessage = string.IsNullOrWhiteSpace(bex.Message)
              ? "The service is busy or temporarily unavailable."
              : bex.Message,
          SecondaryDetail = string.IsNullOrWhiteSpace(bex.RecoverySuggestion) ? null : bex.RecoverySuggestion,
          RecommendedAction = string.IsNullOrWhiteSpace(bex.RecoverySuggestion)
              ? "Wait a moment and try again."
              : bex.RecoverySuggestion!,
          Severity = ActionableErrorSeverity.Error,
          Class = ActionableErrorClass.TransientRetryable,
          IsRetryable = true
        };
      }

      if (bex is BackendDeserializationException)
      {
        return new ActionableErrorInfo
        {
          Title = TitleFor(context, ActionableErrorClass.Unknown),
          PrimaryMessage = "The server response could not be read.",
          RecommendedAction = "Try again. If it persists, update the app or check backend version compatibility.",
          Severity = ActionableErrorSeverity.Error,
          Class = ActionableErrorClass.Unknown,
          IsRetryable = true
        };
      }

      if (bex is BackendServerException bse && bse.StatusCode >= 500)
      {
        return new ActionableErrorInfo
        {
          Title = TitleFor(context, ActionableErrorClass.Unknown),
          PrimaryMessage = string.IsNullOrWhiteSpace(bex.Message)
              ? "The server encountered an error."
              : bex.Message,
          SecondaryDetail = string.IsNullOrWhiteSpace(bex.RecoverySuggestion) ? null : bex.RecoverySuggestion,
          RecommendedAction = string.IsNullOrWhiteSpace(bex.RecoverySuggestion)
              ? "Try again shortly. If it continues, check server logs."
              : bex.RecoverySuggestion!,
          Severity = ActionableErrorSeverity.Error,
          Class = ActionableErrorClass.Unknown,
          IsRetryable = bse.IsRetryable || true
        };
      }

      // Remaining BackendException
      return new ActionableErrorInfo
      {
        Title = TitleFor(context, ActionableErrorClass.Unknown),
        PrimaryMessage = string.IsNullOrWhiteSpace(bex.Message)
            ? "An error occurred while talking to VoiceStudio."
            : bex.Message,
        SecondaryDetail = string.IsNullOrWhiteSpace(bex.RecoverySuggestion) ? null : bex.RecoverySuggestion,
        RecommendedAction = string.IsNullOrWhiteSpace(bex.RecoverySuggestion)
            ? "Try again. If the problem persists, check logs."
            : bex.RecoverySuggestion!,
        Severity = ActionableErrorSeverity.Error,
        Class = ActionableErrorClass.Unknown,
        IsRetryable = bex.IsRetryable
      };
    }

    private static string NotFoundPrimary(ActionableOperationContext context)
    {
      return context == ActionableOperationContext.VoiceSynthesize
          ? "Profile or engine was not found."
          : "The requested item was not found.";
    }

    private static ActionableErrorInfo Unknown(
        ActionableOperationContext context,
        string primary,
        string recovery,
        bool retryable)
    {
      return new ActionableErrorInfo
      {
        Title = TitleFor(context, ActionableErrorClass.Unknown),
        PrimaryMessage = primary,
        RecommendedAction = recovery,
        Severity = ActionableErrorSeverity.Error,
        Class = ActionableErrorClass.Unknown,
        IsRetryable = retryable
      };
    }

    private static string TitleFor(ActionableOperationContext context, ActionableErrorClass cls)
    {
      return context switch
      {
        ActionableOperationContext.VoiceSynthesize => "Synthesis failed",
        ActionableOperationContext.SSMLPreview => "Preview failed",
        ActionableOperationContext.SSMLValidate => "SSML validation failed",
        _ => cls switch
        {
          ActionableErrorClass.ValidationInput => "Invalid input",
          ActionableErrorClass.EnvironmentUnavailable => "Service unavailable",
          ActionableErrorClass.TransientRetryable => "Temporary issue",
          _ => "Error"
        }
      };
    }

    private static string RetryHint(bool retryable)
      => retryable ? "Wait a moment and try again." : "Fix the issue and try again.";
  }
}
