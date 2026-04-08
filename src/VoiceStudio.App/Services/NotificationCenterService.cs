using System;
using System.Collections.ObjectModel;
using System.Linq;
using Microsoft.Windows.AppNotifications;
using Microsoft.Windows.AppNotifications.Builder;

namespace VoiceStudio.App.Services;

public enum AppNotificationType
{
    Info,
    Success,
    Warning,
    Error,
    Progress
}

public enum AppNotificationPriority
{
    Low,
    Normal,
    High,
    Critical
}

public sealed class AppNotificationItem
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public AppNotificationType Type { get; set; }
    public AppNotificationPriority Priority { get; set; } = AppNotificationPriority.Normal;
    public string Title { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public bool IsRead { get; set; }
    public bool IsDismissed { get; set; }
    public double? Progress { get; set; }
    public string? ProgressStatus { get; set; }
    public Action? Action { get; set; }
    public string? ActionLabel { get; set; }
}

public interface INotificationCenterService
{
    event EventHandler<int>? UnreadCountChanged;
    event EventHandler<AppNotificationItem>? NotificationAdded;

    ReadOnlyObservableCollection<AppNotificationItem> Notifications { get; }
    int UnreadCount { get; }

    AppNotificationItem AddNotification(
        AppNotificationType type,
        string message,
        string? title = null,
        AppNotificationPriority priority = AppNotificationPriority.Normal,
        double? progress = null,
        string? progressStatus = null,
        string? actionLabel = null,
        Action? action = null);

    void MarkRead(string id);
    void MarkAllRead();
    void Dismiss(string id);
}

/// <summary>
/// Persistent in-app notification center with unread tracking and optional OS toasts.
/// </summary>
public sealed class NotificationCenterService : INotificationCenterService
{
    private readonly ObservableCollection<AppNotificationItem> _items = new();
    private readonly ReadOnlyObservableCollection<AppNotificationItem> _readonly;
    private readonly int _maxNotifications;

    public NotificationCenterService(int maxNotifications = 100)
    {
        _maxNotifications = Math.Max(10, maxNotifications);
        _readonly = new ReadOnlyObservableCollection<AppNotificationItem>(_items);
    }

    public event EventHandler<int>? UnreadCountChanged;
    public event EventHandler<AppNotificationItem>? NotificationAdded;

    public ReadOnlyObservableCollection<AppNotificationItem> Notifications => _readonly;
    public int UnreadCount => _items.Count(i => !i.IsRead && !i.IsDismissed);

    public AppNotificationItem AddNotification(
        AppNotificationType type,
        string message,
        string? title = null,
        AppNotificationPriority priority = AppNotificationPriority.Normal,
        double? progress = null,
        string? progressStatus = null,
        string? actionLabel = null,
        Action? action = null)
    {
        var item = new AppNotificationItem
        {
            Type = type,
            Message = message,
            Title = title ?? string.Empty,
            Priority = priority,
            Progress = progress,
            ProgressStatus = progressStatus,
            ActionLabel = actionLabel,
            Action = action
        };

        _items.Insert(0, item);
        while (_items.Count > _maxNotifications)
        {
            _items.RemoveAt(_items.Count - 1);
        }

        NotificationAdded?.Invoke(this, item);
        UnreadCountChanged?.Invoke(this, UnreadCount);

        if (priority >= AppNotificationPriority.High)
        {
            TryShowOsNotification(item);
        }

        return item;
    }

    public void MarkRead(string id)
    {
        var item = _items.FirstOrDefault(i => i.Id == id);
        if (item == null || item.IsRead)
            return;

        item.IsRead = true;
        UnreadCountChanged?.Invoke(this, UnreadCount);
    }

    public void MarkAllRead()
    {
        foreach (var item in _items.Where(i => !i.IsDismissed))
        {
            item.IsRead = true;
        }
        UnreadCountChanged?.Invoke(this, UnreadCount);
    }

    public void Dismiss(string id)
    {
        var item = _items.FirstOrDefault(i => i.Id == id);
        if (item == null || item.IsDismissed)
            return;

        item.IsDismissed = true;
        item.IsRead = true;
        UnreadCountChanged?.Invoke(this, UnreadCount);
    }

    private static void TryShowOsNotification(AppNotificationItem item)
    {
        try
        {
            var payload = new AppNotificationBuilder()
                .AddText(string.IsNullOrWhiteSpace(item.Title) ? "VoiceStudio" : item.Title)
                .AddText(item.Message)
                .BuildNotification();
            AppNotificationManager.Default.Show(payload);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[NotificationCenterService] OS notification failed: {ex.Message}");
        }
    }
}
