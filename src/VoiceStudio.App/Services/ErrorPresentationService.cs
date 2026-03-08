using System;
using VoiceStudio.App.Logging;
using VoiceStudio.Core.Services;

namespace VoiceStudio.App.Services
{
  /// <summary>
  /// Implementation of error presentation service that decides the best way to present errors.
  /// Also owns the <see cref="BackendConnectionMonitor"/> and centralises all
  /// backend-connectivity toast suppression so that at most ONE offline toast appears per session.
  /// </summary>
  public class ErrorPresentationService : IErrorPresentationService
  {
    private readonly IErrorDialogService? _errorDialogService;
    private readonly IErrorLoggingService? _errorLoggingService;

    private bool _backendOfflineToastShown;
    private BackendConnectionMonitor? _monitor;

    private ToastNotificationService? ToastNotificationService => AppServices.TryGetToastNotificationService();

    /// <summary>
    /// True when the monitor reports the backend as unreachable.
    /// </summary>
    public static bool IsBackendOffline { get; private set; }

    /// <summary>
    /// Raised on the thread-pool when backend reachability changes.
    /// Argument is true when backend is reachable, false when offline.
    /// </summary>
    public static event EventHandler<bool>? BackendReachabilityChanged;

    public ErrorPresentationService(
        IErrorDialogService? errorDialogService = null,
        IErrorLoggingService? errorLoggingService = null)
    {
      _errorDialogService = errorDialogService;
      _errorLoggingService = errorLoggingService;
    }

    /// <summary>
    /// Creates a <see cref="BackendConnectionMonitor"/>, subscribes to its events,
    /// and starts the health-check loop.  Safe to call more than once (no-ops on repeat).
    /// </summary>
    public void StartBackendMonitoring()
    {
      if (_monitor != null)
        return;

      IBackendClient? client;
      try { client = AppServices.GetBackendClient(); }
      catch (InvalidOperationException) { return; }

      var toast = ToastNotificationService;
      if (toast != null)
        toast.SuppressTransientBackendErrors = true;

      _monitor = new BackendConnectionMonitor(client);
      _monitor.Connected += OnBackendConnected;
      _monitor.Disconnected += OnBackendDisconnected;
      _monitor.StartMonitoring();
    }

    private void OnBackendDisconnected(object? sender, EventArgs e)
    {
      if (IsBackendOffline)
        return;

      IsBackendOffline = true;
      _backendOfflineToastShown = true;

      var toast = ToastNotificationService;
      if (toast != null)
      {
        toast.SuppressTransientBackendErrors = true;
        toast.ShowWarning("Backend is offline. Reconnecting\u2026", "Connection Lost");
      }

      BackendReachabilityChanged?.Invoke(this, false);
    }

    private void OnBackendConnected(object? sender, EventArgs e)
    {
      if (!IsBackendOffline)
        return;

      IsBackendOffline = false;
      _backendOfflineToastShown = false;

      var toast = ToastNotificationService;
      if (toast != null)
      {
        toast.SuppressTransientBackendErrors = false;
        toast.ShowSuccess("Backend reconnected.", "Connection Restored");
      }

      BackendReachabilityChanged?.Invoke(this, true);
    }

    // ── ShowError (Exception) ─────────────────────────────────────

    public void ShowError(Exception exception, string context, ErrorPresentationType type = ErrorPresentationType.Toast)
    {
      if (exception == null)
        return;

      _errorLoggingService?.LogError(exception, context);

      if (type == ErrorPresentationType.Toast)
        type = DeterminePresentationType(exception);

      switch (type)
      {
        case ErrorPresentationType.Toast:
          ShowErrorToast(exception, context);
          break;
        case ErrorPresentationType.Dialog:
          ShowErrorDialog(exception, context);
          break;
        case ErrorPresentationType.Inline:
          ShowErrorToast(exception, context);
          break;
      }
    }

    // ── ShowError (string) ────────────────────────────────────────

    public void ShowError(string message, string context, ErrorPresentationType type = ErrorPresentationType.Toast)
    {
      if (string.IsNullOrWhiteSpace(message))
        return;

      _errorLoggingService?.LogWarning(message, context);

      if (_backendOfflineToastShown && IsConnectivityMessage(message))
        return;

      switch (type)
      {
        case ErrorPresentationType.Toast:
          ToastNotificationService?.ShowError(message, "Error");
          break;
        case ErrorPresentationType.Dialog:
          _ = _errorDialogService?.ShowErrorAsync(message, "Error", context);
          break;
        case ErrorPresentationType.Inline:
          ToastNotificationService?.ShowError(message, "Error");
          break;
      }
    }

    // ── Private helpers ───────────────────────────────────────────

    private ErrorPresentationType DeterminePresentationType(Exception exception)
    {
      if (IsCriticalError(exception))
        return ErrorPresentationType.Dialog;
      if (IsTransientError(exception))
        return ErrorPresentationType.Toast;
      return ErrorPresentationType.Toast;
    }

    private static bool IsCriticalError(Exception exception)
    {
      return exception is
          System.Security.SecurityException or
          System.UnauthorizedAccessException or
          System.IO.IOException or
          OutOfMemoryException;
    }

    private static bool IsTransientError(Exception exception)
    {
      return exception is
          System.Net.Http.HttpRequestException or
          System.TimeoutException or
          System.Threading.Tasks.TaskCanceledException or
          VoiceStudio.Core.Exceptions.BackendUnavailableException or
          VoiceStudio.Core.Exceptions.BackendTimeoutException;
    }

    private void ShowErrorToast(Exception exception, string _)
    {
      if (IsBackendConnectivityError(exception))
      {
        if (_backendOfflineToastShown)
          return;
        _backendOfflineToastShown = true;
      }
      var userMessage = GetUserFriendlyMessage(exception);
      ToastNotificationService?.ShowError(userMessage, GetErrorTitle(exception));
    }

    private static bool IsBackendConnectivityError(Exception exception)
    {
      return exception is
          VoiceStudio.Core.Exceptions.BackendUnavailableException or
          VoiceStudio.Core.Exceptions.BackendTimeoutException or
          System.Net.Http.HttpRequestException;
    }

    /// <summary>
    /// Returns true when <paramref name="message"/> looks like a transient backend error
    /// (connectivity, rate-limit, or burst-404) that should be suppressed while the
    /// offline/degraded toast is already visible.
    /// </summary>
    private static bool IsConnectivityMessage(string message)
    {
      var m = message.AsSpan();
      return m.Contains("unable to connect".AsSpan(), StringComparison.OrdinalIgnoreCase)
          || m.Contains("connection refused".AsSpan(), StringComparison.OrdinalIgnoreCase)
          || m.Contains("No connection could be made".AsSpan(), StringComparison.OrdinalIgnoreCase)
          || m.Contains("rate limit".AsSpan(), StringComparison.OrdinalIgnoreCase)
          || m.Contains("too many requests".AsSpan(), StringComparison.OrdinalIgnoreCase)
          || m.Contains("requests per second".AsSpan(), StringComparison.OrdinalIgnoreCase)
          || m.Contains("requested resource was not found".AsSpan(), StringComparison.OrdinalIgnoreCase)
          || (m.Contains("backend".AsSpan(), StringComparison.OrdinalIgnoreCase)
              && (m.Contains("connect".AsSpan(), StringComparison.OrdinalIgnoreCase)
                  || m.Contains("unavailable".AsSpan(), StringComparison.OrdinalIgnoreCase)
                  || m.Contains("not running".AsSpan(), StringComparison.OrdinalIgnoreCase)));
    }

    public void ResetBackendToastGuard()
    {
      _backendOfflineToastShown = false;
    }

    private void ShowErrorDialog(Exception exception, string context)
    {
      _ = _errorDialogService?.ShowErrorAsync(exception, GetErrorTitle(exception), context);
    }

    private static string GetUserFriendlyMessage(Exception exception)
    {
      return exception switch
      {
        VoiceStudio.Core.Exceptions.BackendUnavailableException => "Unable to connect to the backend. Please check your connection and try again.",
        VoiceStudio.Core.Exceptions.BackendTimeoutException => "The request timed out. Please try again.",
        VoiceStudio.Core.Exceptions.BackendAuthenticationException => "Authentication failed. Please check your credentials.",
        VoiceStudio.Core.Exceptions.BackendNotFoundException => "The requested resource was not found.",
        VoiceStudio.Core.Exceptions.BackendValidationException => "Validation failed. Please check your input.",
        System.Net.Http.HttpRequestException => "Network error occurred. Please check your connection.",
        System.TimeoutException => "The operation timed out. Please try again.",
        OutOfMemoryException => "Insufficient memory. Please close other applications and try again.",
        _ => exception.Message
      };
    }

    private static string GetErrorTitle(Exception exception)
    {
      return exception switch
      {
        VoiceStudio.Core.Exceptions.BackendUnavailableException => "Connection Error",
        VoiceStudio.Core.Exceptions.BackendTimeoutException => "Timeout Error",
        VoiceStudio.Core.Exceptions.BackendAuthenticationException => "Authentication Error",
        VoiceStudio.Core.Exceptions.BackendNotFoundException => "Not Found",
        VoiceStudio.Core.Exceptions.BackendValidationException => "Validation Error",
        System.Net.Http.HttpRequestException => "Network Error",
        System.TimeoutException => "Timeout Error",
        OutOfMemoryException => "Memory Error",
        _ => "Error"
      };
    }
  }
}
