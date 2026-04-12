using System;
using System.IO;
using System.Reflection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace VoiceStudio.App.Tests.Views;

[TestClass]
public sealed class Gap067Tests
{
  private static string FindRepoRoot()
  {
    foreach (var start in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory, Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? "" })
    {
      if (string.IsNullOrEmpty(start))
      {
        continue;
      }

      var dir = new DirectoryInfo(start);
      for (var i = 0; i < 16 && dir != null; i++, dir = dir.Parent)
      {
        var sln = Path.Combine(dir.FullName, "VoiceStudio.sln");
        if (File.Exists(sln))
        {
          return dir.FullName;
        }
      }
    }

    throw new InvalidOperationException("VoiceStudio.sln not found (current dir, base dir, or assembly location).");
  }

  private static string MainWindowXamlPath =>
      Path.Combine(FindRepoRoot(), "src", "VoiceStudio.App", "MainWindow.xaml");

  private static string NotificationCenterViewModelPath =>
      Path.Combine(FindRepoRoot(), "src", "VoiceStudio.App", "ViewModels", "NotificationCenterViewModel.cs");

  private static string StatusBarCoordinatorPath =>
      Path.Combine(FindRepoRoot(), "src", "VoiceStudio.App", "Services", "StatusBarCoordinator.cs");

  [TestMethod]
  public void MainWindow_HasNotificationCenterButton()
  {
    var text = File.ReadAllText(MainWindowXamlPath);
    StringAssert.Contains(text, "MainWindow_NotificationCenterButton");
  }

  [TestMethod]
  public void MainWindow_HasNotificationCenterFlyout()
  {
    var text = File.ReadAllText(MainWindowXamlPath);
    StringAssert.Contains(text, "MainWindow_NotificationCenterFlyout");
  }

  [TestMethod]
  public void MainWindow_HasNotificationCenterList()
  {
    var text = File.ReadAllText(MainWindowXamlPath);
    StringAssert.Contains(text, "MainWindow_NotificationCenterList");
  }

  [TestMethod]
  public void MainWindow_HasUnreadBadge()
  {
    var text = File.ReadAllText(MainWindowXamlPath);
    StringAssert.Contains(text, "MainWindow_NotificationCenterUnreadBadge");
  }

  [TestMethod]
  public void NotificationCenterViewModel_HasUnreadCountProperty()
  {
    var text = File.ReadAllText(NotificationCenterViewModelPath);
    StringAssert.Contains(text, "UnreadCount");
  }

  [TestMethod]
  public void NotificationCenterViewModel_HasHasUnreadProperty()
  {
    var text = File.ReadAllText(NotificationCenterViewModelPath);
    StringAssert.Contains(text, "HasUnread");
  }

  [TestMethod]
  public void NotificationCenterViewModel_HasMarkAllReadCommand()
  {
    var text = File.ReadAllText(NotificationCenterViewModelPath);
    StringAssert.Contains(text, "MarkAllReadCommand");
  }

  [TestMethod]
  public void NotificationCenterViewModel_HasDismissItemCommand()
  {
    var text = File.ReadAllText(NotificationCenterViewModelPath);
    StringAssert.Contains(text, "DismissItemCommand");
  }

  [TestMethod]
  public void StatusBarCoordinator_NotifiesCenterOnDegradedMode()
  {
    var text = File.ReadAllText(StatusBarCoordinatorPath);
    StringAssert.Contains(text, "OnDegradedModeChanged");
    StringAssert.Contains(text, "AddNotification");
    StringAssert.Contains(text, "AppNotificationType.Warning");
    StringAssert.Contains(text, "Backend Unavailable");
  }
}
