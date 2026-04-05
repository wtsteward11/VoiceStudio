using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using VoiceStudio.App.Utilities;
using VoiceStudio.Core.Exceptions;

namespace VoiceStudio.App.Services
{
  /// <summary>
  /// Implementation of IErrorDialogService for displaying user-friendly error dialogs.
  /// XamlRoot must be set at app startup (MainWindow Loaded) for modal dialogs to work.
  /// When XamlRoot is null, degrades to toast to avoid "XamlRoot must be explicitly set for unparented popup".
  /// </summary>
  public class ErrorDialogService : IErrorDialogService
  {
    private readonly IErrorLoggingService? _errorLoggingService;
    private static int _startupPendingDialogAttempts;
    private static int _startupPendingDialogSuppressed;
    private static int _startupPendingDialogShown;

    /// <summary>Canonical XamlRoot for dialogs. Set by MainWindow in Loaded.</summary>
    public static Microsoft.UI.Xaml.XamlRoot? Root { get; set; }

    public ErrorDialogService(IErrorLoggingService? errorLoggingService = null)
    {
      _errorLoggingService = errorLoggingService;
    }

    public static void ResetStartupDialogDiagnostics()
    {
      Interlocked.Exchange(ref _startupPendingDialogAttempts, 0);
      Interlocked.Exchange(ref _startupPendingDialogSuppressed, 0);
      Interlocked.Exchange(ref _startupPendingDialogShown, 0);
    }

    public static StartupDialogDiagnostics GetStartupDialogDiagnostics()
    {
      return new StartupDialogDiagnostics(
        Interlocked.CompareExchange(ref _startupPendingDialogAttempts, 0, 0),
        Interlocked.CompareExchange(ref _startupPendingDialogSuppressed, 0, 0),
        Interlocked.CompareExchange(ref _startupPendingDialogShown, 0, 0));
    }

    public async Task ShowErrorAsync(Exception exception, string? title = null, string? context = null)
    {
      if (exception == null)
        return;

      if (ShouldRouteToStartupAuthority())
      {
        Interlocked.Increment(ref _startupPendingDialogAttempts);
        Interlocked.Increment(ref _startupPendingDialogSuppressed);
        _errorLoggingService?.LogWarning(
          $"Suppressed modal error dialog during startup authority window. Title={title ?? "(null)"}, Context={context ?? "(null)"}, Exception={exception.GetType().Name}: {exception.Message}",
          "Startup.Gating");
        return;
      }

      // 429 rate limit: route through ErrorPresentationService for deduplication
      if (ErrorHandler.IsRateLimitException(exception))
      {
        _errorLoggingService?.LogError(exception, context ?? string.Empty);
        AppServices.TryGetErrorPresentationService()?.ShowError(exception, context ?? string.Empty);
        return;
      }

      // Log the error
      _errorLoggingService?.LogError(exception, context ?? string.Empty);

      var userMessage = ErrorHandler.GetUserFriendlyMessage(exception);
      var recoverySuggestion = ErrorHandler.GetRecoverySuggestion(exception);
      var dialogTitle = title ?? GetErrorTitle(exception);

      await ShowErrorDialogAsync(dialogTitle, userMessage, recoverySuggestion, exception);
    }

    public async Task ShowErrorAsync(string message, string? title = null, string? recoverySuggestion = null)
    {
      if (string.IsNullOrWhiteSpace(message))
        return;

      if (ShouldRouteToStartupAuthority())
      {
        Interlocked.Increment(ref _startupPendingDialogAttempts);
        Interlocked.Increment(ref _startupPendingDialogSuppressed);
        _errorLoggingService?.LogWarning(
          $"Suppressed modal string error dialog during startup authority window. Title={title ?? "(null)"}, Message={message}",
          "Startup.Gating");
        return;
      }

      _errorLoggingService?.LogWarning(message, "User Error");

      await ShowErrorDialogAsync(title ?? "Error", message, recoverySuggestion);
    }

    public async Task ShowWarningAsync(string message, string? title = null)
    {
      if (string.IsNullOrWhiteSpace(message))
        return;

      _errorLoggingService?.LogWarning(message);

      var root = GetXamlRoot();
      if (root == null)
      {
        AppServices.TryGetToastNotificationService()?.ShowWarning(message, title ?? "Warning");
        return;
      }

      var dialog = new ContentDialog
      {
        Title = title ?? "Warning",
        Content = message,
        PrimaryButtonText = "OK",
        XamlRoot = root
      };

      await dialog.ShowAsync();
    }

    public async Task ShowInfoAsync(string message, string? title = null)
    {
      if (string.IsNullOrWhiteSpace(message))
        return;

      var root = GetXamlRoot();
      if (root == null)
      {
        AppServices.TryGetToastNotificationService()?.ShowInfo(message, title ?? "Information");
        return;
      }

      var dialog = new ContentDialog
      {
        Title = title ?? "Information",
        Content = message,
        PrimaryButtonText = "OK",
        XamlRoot = root
      };

      await dialog.ShowAsync();
    }

    private async Task ShowErrorDialogAsync(string title, string message, string? recoverySuggestion, Exception? exception = null)
    {
      var stackPanel = new StackPanel { Spacing = 12 };

      // Error icon and message
      var headerPanel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };

      var errorIcon = new TextBlock
      {
        Text = "⚠️",
        FontSize = 24,
        VerticalAlignment = VerticalAlignment.Top,
        Margin = new Microsoft.UI.Xaml.Thickness(0, 0, 0, 0)
      };
      headerPanel.Children.Add(errorIcon);

      var messageText = new TextBlock
      {
        Text = message,
        TextWrapping = Microsoft.UI.Xaml.TextWrapping.Wrap,
        FontSize = 14,
        Foreground = Application.Current.Resources["VSQ.Text.PrimaryBrush"] as Microsoft.UI.Xaml.Media.Brush ?? new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.White),
        VerticalAlignment = VerticalAlignment.Center
      };
      headerPanel.Children.Add(messageText);
      stackPanel.Children.Add(headerPanel);

      // Recovery suggestion with styled container
      if (!string.IsNullOrWhiteSpace(recoverySuggestion))
      {
        var warnBrush = Application.Current.Resources["VSQ.Warn.Brush"] as Microsoft.UI.Xaml.Media.SolidColorBrush;
        var warnColor = warnBrush?.Color ?? Microsoft.UI.ColorHelper.FromArgb(255, 255, 181, 64);

        var suggestionContainer = new Border
        {
          Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(30, warnColor.R, warnColor.G, warnColor.B)),
          BorderBrush = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(100, warnColor.R, warnColor.G, warnColor.B)),
          BorderThickness = new Microsoft.UI.Xaml.Thickness(1),
          CornerRadius = new Microsoft.UI.Xaml.CornerRadius(4),
          Padding = new Microsoft.UI.Xaml.Thickness(12, 8, 12, 8),
          Margin = new Microsoft.UI.Xaml.Thickness(0, 8, 0, 0)
        };

        var suggestionStack = new StackPanel { Spacing = 4 };

        var suggestionHeader = new TextBlock
        {
          Text = "💡 Suggestion:",
          FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
          FontSize = 12,
          Foreground = warnBrush ?? new Microsoft.UI.Xaml.Media.SolidColorBrush(warnColor)
        };
        suggestionStack.Children.Add(suggestionHeader);

        var suggestionText = new TextBlock
        {
          Text = recoverySuggestion,
          TextWrapping = Microsoft.UI.Xaml.TextWrapping.Wrap,
          FontSize = 12,
          Foreground = Application.Current.Resources["VSQ.Text.PrimaryBrush"] as Microsoft.UI.Xaml.Media.Brush ?? new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(230, 255, 255, 255)),
          LineHeight = 18
        };
        suggestionStack.Children.Add(suggestionText);

        suggestionContainer.Child = suggestionStack;
        stackPanel.Children.Add(suggestionContainer);
      }

      var root = GetXamlRoot();
      if (root == null)
      {
        _errorLoggingService?.LogError(new InvalidOperationException($"XamlRoot not set; cannot show modal. Title: {title}, Message: {message}"), "ErrorDialog");
        AppServices.TryGetToastNotificationService()?.ShowError(message, title);
        return;
      }

      if (ShouldRouteToStartupAuthority())
      {
        Interlocked.Increment(ref _startupPendingDialogAttempts);
        Interlocked.Increment(ref _startupPendingDialogSuppressed);
        _errorLoggingService?.LogWarning(
          $"Suppressed modal error dialog render during startup authority window. Title={title}, Message={message}",
          "Startup.Gating");
        return;
      }

      var dialog = new ContentDialog
      {
        Title = title,
        Content = stackPanel,
        PrimaryButtonText = "OK",
        XamlRoot = root,
        DefaultButton = ContentDialogButton.Primary
      };

      Interlocked.Increment(ref _startupPendingDialogShown);

      // Add retry button for transient errors
      if (exception != null && ErrorHandler.IsTransientError(exception))
      {
        dialog.SecondaryButtonText = "Retry";
      }

      var result = await dialog.ShowAsync();

      // Return retry indication if secondary button was clicked
      if (result == ContentDialogResult.Secondary && exception != null)
      {
        // Note: This is a simple implementation. In a real scenario, you might want
        // to return a value or use a callback to handle retry logic.
      }
    }

    private static bool ShouldRouteToStartupAuthority()
    {
      try
      {
        var startupState = AppServices.GetService<IStartupStateService>();
        if (startupState == null)
        {
          return false;
        }

        return startupState.CurrentState == StartupState.Starting
            || startupState.CurrentState == StartupState.BackendStarting
            || startupState.CurrentState == StartupState.BackendFailed;
      }
      catch
      {
        return false;
      }
    }

    private string GetErrorTitle(Exception exception)
    {
      return exception switch
      {
        BackendUnavailableException => "Connection Error",
        BackendTimeoutException => "Timeout Error",
        BackendAuthenticationException => "Authentication Error",
        BackendNotFoundException => "Not Found",
        BackendValidationException => "Validation Error",
        BackendServerException => "Server Error",
        BackendDeserializationException => "Data Processing Error",
        BackendException => "Backend Error",
        _ => "Error"
      };
    }

    private XamlRoot? GetXamlRoot()
    {
      if (Root != null)
        return Root;
      if (App.MainWindowInstance?.Content is FrameworkElement fe)
        return fe.XamlRoot;
      return null;
    }
  }

  public readonly record struct StartupDialogDiagnostics(
    int StartupPendingDialogAttempts,
    int StartupPendingDialogSuppressed,
    int StartupPendingDialogShown);
}