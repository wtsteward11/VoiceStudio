using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Animation;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Windows.UI;

namespace VoiceStudio.App.Services
{
  /// <summary>
  /// Type of toast notification.
  /// </summary>
  public enum ToastType
  {
    Success,
    Error,
    Warning,
    Info,
    Progress
  }

  /// <summary>
  /// Service for displaying toast notifications.
  /// Implements IDEA 11: Toast Notification System for User Feedback.
  /// </summary>
  public class ToastNotificationService
  {
    private readonly List<ToastNotification> _activeToasts = new();
    private readonly StackPanel _toastContainer;
    private const int MaxVisibleToasts = 4;
    private const int AutoDismissSuccessMs = 3000;
    private const int AutoDismissInfoMs = 5000;
    private const int AutoDismissWarningMs = 5000;

    /// <summary>
    /// When true, <see cref="ShowError"/> silently drops messages that look like
    /// transient backend errors (connectivity, rate-limit, not-found during burst).
    /// Managed by <see cref="ErrorPresentationService"/>.
    /// </summary>
    public bool SuppressTransientBackendErrors { get; set; }

    public ToastNotificationService(StackPanel container)
    {
      _toastContainer = container ?? throw new ArgumentNullException(nameof(container));
    }

    public void ShowSuccess(string message, string? title = null)
    {
      ShowToast(ToastType.Success, message, title, AutoDismissSuccessMs);
    }

    public void ShowError(string message, string? title = null, Action? viewDetailsAction = null)
    {
      if (SuppressTransientBackendErrors && LooksLikeTransientBackendError(message))
        return;
      ShowToast(ToastType.Error, message, title, 0, viewDetailsAction);
    }

    private static bool LooksLikeTransientBackendError(string message)
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

    /// <summary>
    /// Shows an info toast notification.
    /// </summary>
    public void ShowInfo(string message, string? title = null)
    {
      ShowToast(ToastType.Info, message, title, AutoDismissInfoMs);
    }

    /// <summary>
    /// Shows a warning toast notification.
    /// </summary>
    public void ShowWarning(string message, string? title = null)
    {
      ShowToast(ToastType.Warning, message, title, AutoDismissWarningMs);
    }

    /// <summary>
    /// Shows a progress toast notification.
    /// </summary>
    public ToastNotification ShowProgress(string message, string? title = null)
    {
      return ShowToast(ToastType.Progress, message, title, 0, null, true);
    }

    public ToastNotification ShowToast(
        ToastType type,
        string message,
        string? title = null,
        int autoDismissMs = 0,
        Action? action = null,
        bool isProgress = false)
    {
      var toast = new ToastNotification
      {
        Type = type,
        Message = message,
        Title = title,
        Action = action,
        IsProgress = isProgress
      };

      // Create UI element
      var border = new Border
      {
        Background = GetBackgroundBrush(type),
        CornerRadius = new Microsoft.UI.Xaml.CornerRadius(8),
        Padding = new Thickness(16, 12, 16, 12),
        Margin = new Thickness(0, 0, 0, 8),
        MinWidth = 300,
        MaxWidth = 400
      };

      var stackPanel = new StackPanel { Spacing = 4 };

      if (!string.IsNullOrEmpty(title))
      {
        var titleBlock = new TextBlock
        {
          Text = title,
          FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
          FontSize = 14
        };
        stackPanel.Children.Add(titleBlock);
      }

      var messageBlock = new TextBlock
      {
        Text = message,
        FontSize = 13,
        TextWrapping = Microsoft.UI.Xaml.TextWrapping.Wrap
      };
      stackPanel.Children.Add(messageBlock);

      if (isProgress)
      {
        var progressBar = new ProgressBar
        {
          IsIndeterminate = true,
          Height = 4,
          Margin = new Thickness(0, 8, 0, 0)
        };
        stackPanel.Children.Add(progressBar);
        toast.ProgressBar = progressBar;
      }

      if (action != null)
      {
        var button = new Button
        {
          Content = "View Details",
          Margin = new Thickness(0, 8, 0, 0),
          HorizontalAlignment = HorizontalAlignment.Left
        };
        button.Click += (_, _) => action();
        stackPanel.Children.Add(button);
      }

      var dismissButton = new Button
      {
        Content = "✕",
        FontSize = 12,
        Padding = new Thickness(4),
        MinWidth = 24,
        MinHeight = 24,
        HorizontalAlignment = HorizontalAlignment.Right,
        VerticalAlignment = VerticalAlignment.Top,
        Margin = new Thickness(0, -8, -8, 0)
      };
      dismissButton.Click += (_, _) => DismissToast(toast);

      var grid = new Grid();
      grid.Children.Add(stackPanel);
      grid.Children.Add(dismissButton);

      border.Child = grid;
      toast.UIElement = border;

      // Add to container
      _activeToasts.Add(toast);
      _toastContainer.Children.Insert(0, border);

      // Limit visible toasts
      if (_activeToasts.Count > MaxVisibleToasts)
      {
        var oldest = _activeToasts.Last();
        DismissToast(oldest);
      }

      // Animate in (fade-in)
      var fadeIn = new DoubleAnimation
      {
        From = 0,
        To = 1,
        Duration = new Duration(TimeSpan.FromMilliseconds(200))
      };
      border.Opacity = 0;
      Storyboard.SetTarget(fadeIn, border);
      var storyboard = new Storyboard();
      storyboard.Children.Add(fadeIn);
      Storyboard.SetTargetProperty(fadeIn, "Opacity");
      storyboard.Begin();

      // Auto-dismiss if specified
      if (autoDismissMs > 0)
      {
        _ = Task.Delay(autoDismissMs).ContinueWith(_ =>
        {
          App.MainWindowInstance?.DispatcherQueue?.TryEnqueue(() => DismissToast(toast));
        });
      }

      return toast;
    }

    private void DismissToast(ToastNotification toast)
    {
      if (!_activeToasts.Contains(toast) || toast.UIElement == null)
        return;

      _activeToasts.Remove(toast);

      // Animate out (fade-out)
      var fadeOut = new DoubleAnimation
      {
        From = 1,
        To = 0,
        Duration = new Duration(TimeSpan.FromMilliseconds(150))
      };
      Storyboard.SetTarget(fadeOut, toast.UIElement);
      var storyboard = new Storyboard();
      storyboard.Children.Add(fadeOut);
      Storyboard.SetTargetProperty(fadeOut, "Opacity");
      storyboard.Completed += (_, _) => _toastContainer.Children.Remove(toast.UIElement);
      storyboard.Begin();
    }

    private Microsoft.UI.Xaml.Media.Brush GetBackgroundBrush(ToastType type)
    {
      return type switch
      {
        ToastType.Success => new Microsoft.UI.Xaml.Media.SolidColorBrush(Color.FromArgb(255, 46, 160, 67)),
        ToastType.Error => new Microsoft.UI.Xaml.Media.SolidColorBrush(Color.FromArgb(255, 232, 17, 35)),
        ToastType.Warning => new Microsoft.UI.Xaml.Media.SolidColorBrush(Color.FromArgb(255, 255, 185, 0)),
        ToastType.Info => new Microsoft.UI.Xaml.Media.SolidColorBrush(Color.FromArgb(255, 0, 120, 212)),
        ToastType.Progress => new Microsoft.UI.Xaml.Media.SolidColorBrush(Color.FromArgb(255, 50, 50, 50)),
        _ => new Microsoft.UI.Xaml.Media.SolidColorBrush(Color.FromArgb(255, 50, 50, 50))
      };
    }
  }

  /// <summary>
  /// Represents a toast notification.
  /// </summary>
  public class ToastNotification
  {
    public ToastType Type { get; set; }
    public string Message { get; set; } = string.Empty;
    public string? Title { get; set; }
    public Action? Action { get; set; }
    public bool IsProgress { get; set; }
    public UIElement? UIElement { get; set; }
    public ProgressBar? ProgressBar { get; set; }
  }

  /// <summary>
  /// Toast notification type.
  /// </summary>
  // ToastType enum is now defined in VoiceStudio.Core.Services.IToastNotificationService
}