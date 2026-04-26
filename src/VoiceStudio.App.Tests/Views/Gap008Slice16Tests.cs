using System;
using System.IO;
using System.Reflection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace VoiceStudio.App.Tests.Views;

[TestClass]
public sealed class Gap008Slice16Tests
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

        throw new InvalidOperationException("VoiceStudio.sln not found.");
    }

    private static string MainWindowPath =>
        Path.Combine(FindRepoRoot(), "src", "VoiceStudio.App", "MainWindow.xaml.cs");

    [TestMethod]
    public void MainWindow_Loaded_bootstrap_hooks_delegate_notification_center_wire_to_slice16_bridge()
    {
        var text = File.ReadAllText(MainWindowPath);
        StringAssert.Contains(text, "_notificationCenterShellBridge");
        StringAssert.Contains(text, "WireNotificationCenter = () => _notificationCenterShellBridge.WireNotificationCenter()");
    }

    [TestMethod]
    public void MainWindow_constructed_notification_center_bridge_after_jump_list_dispatch_bridge()
    {
        var text = File.ReadAllText(MainWindowPath);
        var idxJump = text.IndexOf("MainWindowJumpListDispatchShellBridge Created", StringComparison.Ordinal);
        var idxNc = text.IndexOf("MainWindowNotificationCenterShellBridge Created", StringComparison.Ordinal);
        Assert.IsTrue(idxJump >= 0, "Expected jump list dispatch bridge profiler checkpoint.");
        Assert.IsTrue(idxNc >= 0, "Expected notification center shell bridge profiler checkpoint.");
        Assert.IsTrue(idxJump < idxNc, "Notification center bridge should construct after jump list dispatch bridge.");
    }

    [TestMethod]
    public void MainWindow_does_not_embed_private_WireNotificationCenter_or_badge_helpers()
    {
        var text = File.ReadAllText(MainWindowPath);
        Assert.IsFalse(
            text.Contains("private void WireNotificationCenter", StringComparison.Ordinal),
            "WireNotificationCenter body should live on MainWindowNotificationCenterShellBridge.");
        Assert.IsFalse(
            text.Contains("UpdateNotificationCenterBadge", StringComparison.Ordinal),
            "Badge updates should live on MainWindowNotificationCenterShellBridge.");
    }

    [TestMethod]
    public void MainWindow_lifetime_cleanup_delegates_notification_center_teardown_to_bridge()
    {
        var text = File.ReadAllText(MainWindowPath);
        StringAssert.Contains(text, "CleanupNotificationCenterViewModel = () => _notificationCenterShellBridge.CleanupNotificationCenter()");
    }
}
