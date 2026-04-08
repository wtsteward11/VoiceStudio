using Microsoft.VisualStudio.TestTools.UnitTesting;
using VoiceStudio.App.Services;

namespace VoiceStudio.App.Tests.Services;

[TestClass]
public class NotificationCenterServiceTests
{
    [TestMethod]
    public void AddNotification_IncrementsUnreadCount()
    {
        var sut = new NotificationCenterService();

        sut.AddNotification(AppNotificationType.Info, "Message", "Title");

        Assert.AreEqual(1, sut.UnreadCount);
        Assert.AreEqual(1, sut.Notifications.Count);
    }

    [TestMethod]
    public void MarkRead_DecrementsUnreadCount()
    {
        var sut = new NotificationCenterService();
        var item = sut.AddNotification(AppNotificationType.Warning, "Warning");

        sut.MarkRead(item.Id);

        Assert.AreEqual(0, sut.UnreadCount);
    }

    [TestMethod]
    public void MaxNotificationCap_EvictsOldest()
    {
        var sut = new NotificationCenterService(maxNotifications: 10);
        for (var i = 0; i < 12; i++)
        {
            sut.AddNotification(AppNotificationType.Info, $"Message {i}");
        }

        Assert.AreEqual(10, sut.Notifications.Count);
    }
}
