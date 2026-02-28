// Phase 5: Notification System
// Task 5.6: Toast notifications and in-app alerts

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.Windows.AppNotifications;
using Microsoft.Windows.AppNotifications.Builder;

namespace VoiceStudio.App.Features.Notifications;

/// <summary>
/// Notification priority levels.
/// </summary>
public enum NotificationPriority
{
    Low,
    Normal,
    High,
    Critical,
}

/// <summary>
/// Notification types.
/// </summary>
public enum NotificationType
{
    Info,
    Success,
    Warning,
    Error,
    Progress,
}

/// <summary>
/// In-app notification.
/// </summary>
public class Notification
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Title { get; set; } = "";
    public string Message { get; set; } = "";
    public NotificationType Type { get; set; } = NotificationType.Info;
    public NotificationPriority Priority { get; set; } = NotificationPriority.Normal;
    public DateTime Timestamp { get; set; } = DateTime.Now;
    public bool IsRead { get; set; } = false;
    public bool IsDismissed { get; set; } = false;
    public TimeSpan? AutoDismissAfter { get; set; }
    public string? ActionLabel { get; set; }
    public Action? Action { get; set; }
    public double? Progress { get; set; }
    public string? ProgressStatus { get; set; }
}

/// <summary>
/// Service for managing notifications.
/// </summary>
public class NotificationService
{
    private readonly ObservableCollection<Notification> _notifications = new();
    private readonly Queue<Notification> _toastQueue = new();
    private bool _isProcessingToasts;
    private int _maxNotifications = 100;

    public event EventHandler<Notification>? NotificationAdded;
    public event EventHandler<Notification>? NotificationDismissed;
    public event EventHandler<int>? UnreadCountChanged;

    public IReadOnlyCollection<Notification> Notifications => _notifications;
    public int UnreadCount => GetUnreadCount();

    /// <summary>
    /// Initialize the notification service.
    /// </summary>
    public void Initialize()
    {
        // Register for app notifications
        var manager = AppNotificationManager.Default;
        manager.NotificationInvoked += OnNotificationInvoked;
        manager.Register();
    }

    /// <summary>
    /// Show an info notification.
    /// </summary>
    public void ShowInfo(string title, string message, TimeSpan? autoDismiss = null)
    {
        Show(new Notification
        {
            Title = title,
            Message = message,
            Type = NotificationType.Info,
            AutoDismissAfter = autoDismiss ?? TimeSpan.FromSeconds(5),
        });
    }

    /// <summary>
    /// Show a success notification.
    /// </summary>
    public void ShowSuccess(string title, string message, TimeSpan? autoDismiss = null)
    {
        Show(new Notification
        {
            Title = title,
            Message = message,
            Type = NotificationType.Success,
            AutoDismissAfter = autoDismiss ?? TimeSpan.FromSeconds(5),
        });
    }

    /// <summary>
    /// Show a warning notification.
    /// </summary>
    public void ShowWarning(string title, string message, TimeSpan? autoDismiss = null)
    {
        Show(new Notification
        {
            Title = title,
            Message = message,
            Type = NotificationType.Warning,
            Priority = NotificationPriority.High,
            AutoDismissAfter = autoDismiss ?? TimeSpan.FromSeconds(10),
        });
    }

    /// <summary>
    /// Show an error notification.
    /// </summary>
    public void ShowError(string title, string message, string? actionLabel = null, Action? action = null)
    {
        Show(new Notification
        {
            Title = title,
            Message = message,
            Type = NotificationType.Error,
            Priority = NotificationPriority.Critical,
            ActionLabel = actionLabel,
            Action = action,
        });
    }

    /// <summary>
    /// Show a progress notification.
    /// </summary>
    public Notification ShowProgress(string title, string status, double progress = 0)
    {
        var notification = new Notification
        {
            Title = title,
            Message = status,
            Type = NotificationType.Progress,
            Progress = progress,
            ProgressStatus = status,
        };
        
        Show(notification);
        return notification;
    }

    /// <summary>
    /// Update a progress notification.
    /// </summary>
    public void UpdateProgress(string notificationId, double progress, string? status = null)
    {
        var notification = FindNotification(notificationId);
        if (notification != null)
        {
            notification.Progress = progress;
            if (status != null)
            {
                notification.ProgressStatus = status;
                notification.Message = status;
            }
            
            // Trigger UI update if bound
            NotificationAdded?.Invoke(this, notification);
        }
    }

    /// <summary>
    /// Show a notification.
    /// </summary>
    public void Show(Notification notification)
    {
        _notifications.Insert(0, notification);
        
        // Trim old notifications
        while (_notifications.Count > _maxNotifications)
        {
            _notifications.RemoveAt(_notifications.Count - 1);
        }
        
        NotificationAdded?.Invoke(this, notification);
        UnreadCountChanged?.Invoke(this, UnreadCount);
        
        // Queue toast for display
        if (notification.Type != NotificationType.Progress)
        {
            _toastQueue.Enqueue(notification);
            ProcessToastQueue();
        }
        
        // Auto-dismiss if configured
        if (notification.AutoDismissAfter.HasValue)
        {
            _ = AutoDismissAsync(notification);
        }
    }

    /// <summary>
    /// Show a Windows toast notification.
    /// </summary>
    public void ShowToast(string title, string message, string? actionLabel = null)
    {
        try
        {
            var builder = new AppNotificationBuilder()
                .AddText(title)
                .AddText(message);
            
            if (actionLabel != null)
            {
                builder.AddButton(new AppNotificationButton(actionLabel)
                    .AddArgument("action", "open"));
            }
            
            var notification = builder.BuildNotification();
            AppNotificationManager.Default.Show(notification);
        }
        // ALLOWED: empty catch - toast notifications may not be available on all systems
        catch
        {
        }
    }

    /// <summary>
    /// Dismiss a notification.
    /// </summary>
    public void Dismiss(string notificationId)
    {
        var notification = FindNotification(notificationId);
        if (notification != null)
        {
            notification.IsDismissed = true;
            _notifications.Remove(notification);
            NotificationDismissed?.Invoke(this, notification);
            UnreadCountChanged?.Invoke(this, UnreadCount);
        }
    }

    /// <summary>
    /// Dismiss all notifications.
    /// </summary>
    public void DismissAll()
    {
        _notifications.Clear();
        UnreadCountChanged?.Invoke(this, 0);
    }

    /// <summary>
    /// Mark a notification as read.
    /// </summary>
    public void MarkAsRead(string notificationId)
    {
        var notification = FindNotification(notificationId);
        if (notification != null && !notification.IsRead)
        {
            notification.IsRead = true;
            UnreadCountChanged?.Invoke(this, UnreadCount);
        }
    }

    /// <summary>
    /// Mark all notifications as read.
    /// </summary>
    public void MarkAllAsRead()
    {
        foreach (var notification in _notifications)
        {
            notification.IsRead = true;
        }
        
        UnreadCountChanged?.Invoke(this, 0);
    }

    private Notification? FindNotification(string id)
    {
        foreach (var notification in _notifications)
        {
            if (notification.Id == id)
            {
                return notification;
            }
        }
        
        return null;
    }

    private int GetUnreadCount()
    {
        int count = 0;
        foreach (var notification in _notifications)
        {
            if (!notification.IsRead)
            {
                count++;
            }
        }
        
        return count;
    }

    private async Task AutoDismissAsync(Notification notification)
    {
        await Task.Delay(notification.AutoDismissAfter!.Value);
        
        if (!notification.IsDismissed)
        {
            Dismiss(notification.Id);
        }
    }

    private async void ProcessToastQueue()
    {
        if (_isProcessingToasts || _toastQueue.Count == 0)
        {
            return;
        }
        
        _isProcessingToasts = true;
        
        while (_toastQueue.Count > 0)
        {
            var notification = _toastQueue.Dequeue();
            
            // Show in-app toast
            await ShowInAppToastAsync(notification);
            
            // Small delay between toasts
            await Task.Delay(100);
        }
        
        _isProcessingToasts = false;
    }

    private async Task ShowInAppToastAsync(Notification notification)
    {
        // In-app toast display logic would go here
        // This could trigger a toast UI element
        await Task.CompletedTask;
    }

    private void OnNotificationInvoked(
        AppNotificationManager sender,
        AppNotificationActivatedEventArgs args)
    {
        // Handle notification action
        var arguments = args.Argument;
        // Parse and handle the action
    }
}
