using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using CommunityToolkit.Mvvm.Input;
using VoiceStudio.App.Services;

namespace VoiceStudio.App.ViewModels;

/// <summary>
/// GAP-067 slice 1: UI adapter for <see cref="INotificationCenterService"/> — single visible list, read/dismiss commands.
/// </summary>
public sealed class NotificationCenterViewModel : INotifyPropertyChanged, IDisposable
{
    private readonly INotificationCenterService _service;
    private readonly ObservableCollection<AppNotificationItem> _visible = new();
    private readonly ReadOnlyObservableCollection<AppNotificationItem> _readonlyVisible;
    private bool _disposed;

    public NotificationCenterViewModel(INotificationCenterService service)
    {
        _service = service ?? throw new ArgumentNullException(nameof(service));
        _readonlyVisible = new ReadOnlyObservableCollection<AppNotificationItem>(_visible);

        _service.UnreadCountChanged += OnUnreadCountChanged;

        SyncVisibleFromService();

        MarkAllReadCommand = new RelayCommand(ExecuteMarkAllRead);
        DismissItemCommand = new RelayCommand<AppNotificationItem>(ExecuteDismissItem);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ReadOnlyObservableCollection<AppNotificationItem> Notifications => _readonlyVisible;

    public int UnreadCount => _service.UnreadCount;

    public bool HasUnread => UnreadCount > 0;

    public IRelayCommand MarkAllReadCommand { get; }

    public IRelayCommand<AppNotificationItem> DismissItemCommand { get; }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _service.UnreadCountChanged -= OnUnreadCountChanged;
    }

    private void OnUnreadCountChanged(object? sender, int count)
    {
        RaiseUnreadProperties();
        SyncVisibleFromService();
    }

    private void SyncVisibleFromService()
    {
        _visible.Clear();
        foreach (var item in _service.Notifications.Where(static i => !i.IsDismissed))
        {
            _visible.Add(item);
        }
    }

    private void ExecuteMarkAllRead()
    {
        _service.MarkAllRead();
        RaiseUnreadProperties();
        SyncVisibleFromService();
        OnPropertyChanged(nameof(Notifications));
    }

    private void ExecuteDismissItem(AppNotificationItem? item)
    {
        if (item == null)
            return;
        _service.Dismiss(item.Id);
        for (var i = 0; i < _visible.Count; i++)
        {
            if (_visible[i].Id == item.Id)
            {
                _visible.RemoveAt(i);
                break;
            }
        }
        RaiseUnreadProperties();
        OnPropertyChanged(nameof(Notifications));
    }

    private void RaiseUnreadProperties()
    {
        OnPropertyChanged(nameof(UnreadCount));
        OnPropertyChanged(nameof(HasUnread));
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
