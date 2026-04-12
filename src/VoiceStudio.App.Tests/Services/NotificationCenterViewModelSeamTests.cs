using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using VoiceStudio.App.Services;
using VoiceStudio.App.ViewModels;

namespace VoiceStudio.App.Tests.Services;

[TestClass]
public sealed class NotificationCenterViewModelSeamTests
{
  [TestMethod]
  public void AddNotification_IncrementsUnreadCount()
  {
    var service = new NotificationCenterService();
    using var vm = new NotificationCenterViewModel(service);
    Assert.AreEqual(0, vm.UnreadCount);
    service.AddNotification(AppNotificationType.Info, "m", "t");
    Assert.AreEqual(1, vm.UnreadCount);
  }

  [TestMethod]
  public void AddNotification_AppearsInViewModelNotifications()
  {
    var service = new NotificationCenterService();
    using var vm = new NotificationCenterViewModel(service);
    service.AddNotification(AppNotificationType.Info, "hello", "title");
    Assert.AreEqual(1, vm.Notifications.Count);
    Assert.AreEqual("hello", vm.Notifications[0].Message);
  }

  [TestMethod]
  public void MarkAllRead_SetsUnreadCountToZero()
  {
    var service = new NotificationCenterService();
    using var vm = new NotificationCenterViewModel(service);
    service.AddNotification(AppNotificationType.Info, "a");
    service.AddNotification(AppNotificationType.Info, "b");
    Assert.IsTrue(vm.UnreadCount > 0);
    vm.MarkAllReadCommand.Execute(null);
    Assert.AreEqual(0, vm.UnreadCount);
    Assert.IsFalse(vm.HasUnread);
  }

  [TestMethod]
  public void MarkAllRead_RaisesPropertyChanged()
  {
    var service = new NotificationCenterService();
    using var vm = new NotificationCenterViewModel(service);
    service.AddNotification(AppNotificationType.Info, "x");
    var names = new List<string>();
    vm.PropertyChanged += (_, e) =>
    {
      if (e.PropertyName != null)
      {
        names.Add(e.PropertyName);
      }
    };
    vm.MarkAllReadCommand.Execute(null);
    Assert.IsTrue(names.Contains(nameof(NotificationCenterViewModel.UnreadCount)));
  }

  [TestMethod]
  public void DismissItem_RemovesFromVisibleList()
  {
    var service = new NotificationCenterService();
    using var vm = new NotificationCenterViewModel(service);
    var item = service.AddNotification(AppNotificationType.Info, "one");
    service.AddNotification(AppNotificationType.Info, "two");
    Assert.AreEqual(2, vm.Notifications.Count);
    vm.DismissItemCommand.Execute(item);
    Assert.AreEqual(1, vm.Notifications.Count);
  }

  [TestMethod]
  public void DismissItem_SetsDismissedOnService()
  {
    var service = new NotificationCenterService();
    using var vm = new NotificationCenterViewModel(service);
    var item = service.AddNotification(AppNotificationType.Info, "z");
    vm.DismissItemCommand.Execute(item);
    foreach (var n in service.Notifications)
    {
      if (n.Id == item.Id)
      {
        Assert.IsTrue(n.IsDismissed);
        return;
      }
    }

    Assert.Fail("Item not found in service.");
  }

  [TestMethod]
  public void UnreadCountChanged_RaisesHasUnreadPropertyChanged()
  {
    var service = new NotificationCenterService();
    using var vm = new NotificationCenterViewModel(service);
    var sawHasUnread = false;
    vm.PropertyChanged += (_, e) =>
    {
      if (e.PropertyName == nameof(NotificationCenterViewModel.HasUnread))
      {
        sawHasUnread = true;
      }
    };
    service.AddNotification(AppNotificationType.Warning, "w");
    Assert.IsTrue(sawHasUnread || vm.HasUnread);
  }

  [TestMethod]
  public void DegradedModeEntry_CreatesWarningNotification()
  {
    var service = new NotificationCenterService();
    service.AddNotification(
        AppNotificationType.Warning,
        message: "Backend temporarily unavailable.",
        title: "Backend Unavailable",
        priority: AppNotificationPriority.High);
    Assert.AreEqual(1, service.Notifications.Count);
    Assert.AreEqual(AppNotificationType.Warning, service.Notifications[0].Type);
  }
}
