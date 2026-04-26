using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Reflection;
using Microsoft.UI.Dispatching;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using VoiceStudio.App.Services;
using VoiceStudio.App.Tests.Fixtures;
using VoiceStudio.App.ViewModels;

namespace VoiceStudio.App.Tests.Services;

[TestClass]
public sealed class MainWindowNotificationCenterShellBridgeTests
{
    private DispatcherQueueController? _dispatcherController;

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

    private static string BridgePath =>
        Path.Combine(FindRepoRoot(), "src", "VoiceStudio.App", "Services", "MainWindowNotificationCenterShellBridge.cs");

    [TestInitialize]
    public void Setup()
    {
        _dispatcherController = DispatcherQueueController.CreateOnDedicatedThread();
    }

    [TestCleanup]
    public void TearDown()
    {
        DispatcherQueueTestHelpers.ShutdownSyncBounded(_dispatcherController);
        _dispatcherController = null;
    }

    private MainWindowNotificationCenterShellBridge CreateBridge(Func<NotificationCenterViewModel?> getVm)
    {
        var q = _dispatcherController!.DispatcherQueue;
        return new MainWindowNotificationCenterShellBridge(
            getVm,
            () => null,
            () => null,
            () => null,
            () => null,
            () => null,
            q);
    }

    [TestMethod]
    public void Constructor_throws_when_any_dependency_accessor_is_null()
    {
        var q = _dispatcherController!.DispatcherQueue;
        Assert.ThrowsException<ArgumentNullException>(() =>
            new MainWindowNotificationCenterShellBridge(
                getViewModel: null!,
                () => null,
                () => null,
                () => null,
                () => null,
                () => null,
                q));

        Assert.ThrowsException<ArgumentNullException>(() =>
            new MainWindowNotificationCenterShellBridge(
                () => null,
                getNotificationCenterButton: null!,
                () => null,
                () => null,
                () => null,
                () => null,
                q));

        Assert.ThrowsException<ArgumentNullException>(() =>
            new MainWindowNotificationCenterShellBridge(
                () => null,
                () => null,
                getNotificationCenterFlyoutRoot: null!,
                () => null,
                () => null,
                () => null,
                q));

        Assert.ThrowsException<ArgumentNullException>(() =>
            new MainWindowNotificationCenterShellBridge(
                () => null,
                () => null,
                () => null,
                getNotificationCenterList: null!,
                () => null,
                () => null,
                q));

        Assert.ThrowsException<ArgumentNullException>(() =>
            new MainWindowNotificationCenterShellBridge(
                () => null,
                () => null,
                () => null,
                () => null,
                getUnreadBadge: null!,
                () => null,
                q));

        Assert.ThrowsException<ArgumentNullException>(() =>
            new MainWindowNotificationCenterShellBridge(
                () => null,
                () => null,
                () => null,
                () => null,
                () => null,
                getUnreadBadgeText: null!,
                q));

        Assert.ThrowsException<ArgumentNullException>(() =>
            new MainWindowNotificationCenterShellBridge(
                () => null,
                () => null,
                () => null,
                () => null,
                () => null,
                () => null,
                dispatcherQueue: null!));
    }

    [TestMethod]
    public void CleanupNotificationCenter_clears_vm_so_subsequent_mark_all_read_is_no_op_for_service()
    {
        var items = new ObservableCollection<AppNotificationItem>();
        var ro = new ReadOnlyObservableCollection<AppNotificationItem>(items);
        var serviceMock = new Mock<INotificationCenterService>();
        serviceMock.SetupGet(s => s.Notifications).Returns(ro);
        serviceMock.SetupGet(s => s.UnreadCount).Returns(0);
        var vm = new NotificationCenterViewModel(serviceMock.Object);
        var bridge = CreateBridge(() => vm);
        bridge.WireNotificationCenter();
        bridge.OnMarkAllReadClick();
        bridge.CleanupNotificationCenter();
        bridge.OnMarkAllReadClick();
        serviceMock.Verify(s => s.MarkAllRead(), Times.Once);
    }

    [TestMethod]
    public void OnMarkAllReadClick_invokes_service_when_wired()
    {
        var items = new ObservableCollection<AppNotificationItem>();
        var ro = new ReadOnlyObservableCollection<AppNotificationItem>(items);
        var serviceMock = new Mock<INotificationCenterService>();
        serviceMock.SetupGet(s => s.Notifications).Returns(ro);
        serviceMock.SetupGet(s => s.UnreadCount).Returns(1);
        var vm = new NotificationCenterViewModel(serviceMock.Object);
        var bridge = CreateBridge(() => vm);
        bridge.WireNotificationCenter();

        bridge.OnMarkAllReadClick();

        serviceMock.Verify(s => s.MarkAllRead(), Times.Once);
    }

    [TestMethod]
    public void Bridge_source_does_not_contain_forbidden_cross_seam_symbols_or_rhvoice_path()
    {
        var text = File.ReadAllText(BridgePath);
        Assert.IsFalse(text.Contains("TryDispatchPendingFileActivation", StringComparison.Ordinal));
        Assert.IsFalse(text.Contains("MainWindowFileActivationShellBridge", StringComparison.Ordinal));
        Assert.IsFalse(text.Contains("WireJumpList", StringComparison.Ordinal));
        Assert.IsFalse(text.Contains("MainWindowJumpListTaskbarProgressShellBridge", StringComparison.Ordinal));
        Assert.IsFalse(text.Contains("engines/audio/rhvoice/", StringComparison.Ordinal));
        Assert.IsFalse(text.Contains("App.MainWindowInstance", StringComparison.Ordinal));
    }
}
