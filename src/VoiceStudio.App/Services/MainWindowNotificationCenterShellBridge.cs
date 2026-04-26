using System;
using System.ComponentModel;
using System.Diagnostics;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using VoiceStudio.App.ViewModels;

namespace VoiceStudio.App.Services;

/// <summary>
/// GAP-008 Slice 16: Loaded-time notification center VM binding + unread badge + teardown.
/// </summary>
public sealed class MainWindowNotificationCenterShellBridge
{
    private readonly Func<NotificationCenterViewModel?> _getViewModel;
    private readonly Func<Button?> _getNotificationCenterButton;
    private readonly Func<FrameworkElement?> _getNotificationCenterFlyoutRoot;
    private readonly Func<ListView?> _getNotificationCenterList;
    private readonly Func<Border?> _getUnreadBadge;
    private readonly Func<TextBlock?> _getUnreadBadgeText;
    private readonly DispatcherQueue _dispatcherQueue;

    private NotificationCenterViewModel? _viewModel;

    public MainWindowNotificationCenterShellBridge(
        Func<NotificationCenterViewModel?> getViewModel,
        Func<Button?> getNotificationCenterButton,
        Func<FrameworkElement?> getNotificationCenterFlyoutRoot,
        Func<ListView?> getNotificationCenterList,
        Func<Border?> getUnreadBadge,
        Func<TextBlock?> getUnreadBadgeText,
        DispatcherQueue dispatcherQueue)
    {
        _getViewModel = getViewModel ?? throw new ArgumentNullException(nameof(getViewModel));
        _getNotificationCenterButton = getNotificationCenterButton ?? throw new ArgumentNullException(nameof(getNotificationCenterButton));
        _getNotificationCenterFlyoutRoot = getNotificationCenterFlyoutRoot ?? throw new ArgumentNullException(nameof(getNotificationCenterFlyoutRoot));
        _getNotificationCenterList = getNotificationCenterList ?? throw new ArgumentNullException(nameof(getNotificationCenterList));
        _getUnreadBadge = getUnreadBadge ?? throw new ArgumentNullException(nameof(getUnreadBadge));
        _getUnreadBadgeText = getUnreadBadgeText ?? throw new ArgumentNullException(nameof(getUnreadBadgeText));
        _dispatcherQueue = dispatcherQueue ?? throw new ArgumentNullException(nameof(dispatcherQueue));
    }

    /// <summary>
    /// GAP-067 slice 1: wire notification center VM to shell (Loaded-only; ADR-047).
    /// </summary>
    public void WireNotificationCenter()
    {
        try
        {
            var ncVm = _getViewModel();
            if (ncVm == null)
            {
                return;
            }

            _viewModel = ncVm;
            var button = _getNotificationCenterButton();
            var flyoutRoot = _getNotificationCenterFlyoutRoot();
            var list = _getNotificationCenterList();
            if (button != null)
            {
                button.DataContext = ncVm;
            }

            if (flyoutRoot != null)
            {
                flyoutRoot.DataContext = ncVm;
            }

            if (list != null)
            {
                list.ItemsSource = ncVm.Notifications;
            }

            ncVm.PropertyChanged += OnNotificationCenterViewModelPropertyChanged;
            UpdateNotificationCenterBadge();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[MainWindow] Notification center wire failed: {ex.Message}");
        }
    }

    private void OnNotificationCenterViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(NotificationCenterViewModel.UnreadCount)
            or nameof(NotificationCenterViewModel.HasUnread))
        {
            UpdateNotificationCenterBadge();
        }
    }

    private void UpdateNotificationCenterBadge()
    {
        var vm = _viewModel;
        if (vm == null)
        {
            return;
        }

        _dispatcherQueue.TryEnqueue(() =>
        {
            var unreadBadge = _getUnreadBadge();
            var unreadBadgeText = _getUnreadBadgeText();
            if (unreadBadge != null)
            {
                unreadBadge.Visibility = vm.HasUnread ? Visibility.Visible : Visibility.Collapsed;
            }

            if (unreadBadgeText != null)
            {
                unreadBadgeText.Text = vm.UnreadCount > 99 ? "99+" : vm.UnreadCount.ToString();
            }
        });
    }

    public void OnMarkAllReadClick()
    {
        if (_viewModel != null)
        {
            _viewModel.MarkAllReadCommand.Execute(null);
        }
    }

    public void OnDismissItemClick(AppNotificationItem item)
    {
        if (_viewModel != null)
        {
            _viewModel.DismissItemCommand.Execute(item);
        }
    }

    public void CleanupNotificationCenter()
    {
        if (_viewModel != null)
        {
            _viewModel.PropertyChanged -= OnNotificationCenterViewModelPropertyChanged;
            _viewModel = null;
        }
    }
}
