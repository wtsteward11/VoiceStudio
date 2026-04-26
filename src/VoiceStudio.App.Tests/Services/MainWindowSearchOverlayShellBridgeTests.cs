using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using VoiceStudio.App.Services;
using VoiceStudio.App.Views;
using VoiceStudio.Core.Models;

namespace VoiceStudio.App.Tests.Services;

[TestClass]
public sealed class MainWindowSearchOverlayShellBridgeTests
{
    [TestMethod]
    public void Show_invokes_coordinator_Show_when_present()
    {
        var mock = new Mock<ISearchOverlayCoordinator>(MockBehavior.Strict);
        mock.Setup(c => c.Show()).Verifiable();
        object? findName(string _) => null;
        var bridge = new MainWindowSearchOverlayShellBridge(mock.Object, findName);

        bridge.Show();

        mock.Verify(c => c.Show(), Times.Once);
    }

    [TestMethod]
    public void Show_noop_when_coordinator_null()
    {
        object? findName(string _) => null;
        var bridge = new MainWindowSearchOverlayShellBridge(null, findName);
        bridge.Show();
    }

    [TestMethod]
    public async Task OnNavigateRequestedAsync_invokes_HandleNavigateRequestedAsync()
    {
        var item = new SearchResultItem { Id = "a", Title = "T", Type = "profile", PanelId = "library" };
        var args = new SearchNavigationEventArgs(item);
        var mock = new Mock<ISearchOverlayCoordinator>(MockBehavior.Strict);
        mock.Setup(c => c.HandleNavigateRequestedAsync(item))
            .Returns(Task.CompletedTask)
            .Verifiable();
        object? findName(string _) => null;
        var bridge = new MainWindowSearchOverlayShellBridge(mock.Object, findName);

        await bridge.OnNavigateRequestedAsync(args).ConfigureAwait(false);

        mock.Verify(c => c.HandleNavigateRequestedAsync(item), Times.Once);
    }

    [TestMethod]
    public async Task OnNavigateRequestedAsync_noop_when_coordinator_null()
    {
        var item = new SearchResultItem { Id = "x", Title = "Y", Type = "profile" };
        var args = new SearchNavigationEventArgs(item);
        object? findName(string _) => null;
        var bridge = new MainWindowSearchOverlayShellBridge(null, findName);

        await bridge.OnNavigateRequestedAsync(args).ConfigureAwait(false);
    }

    [TestMethod]
    public void ShouldDismissSearchOverlayOnBackgroundTap_true_when_source_equals_overlay()
    {
        var overlay = new object();
        Assert.IsTrue(MainWindowSearchOverlayShellBridge.ShouldDismissSearchOverlayOnBackgroundTap(overlay, overlay));
    }

    [TestMethod]
    public void ShouldDismissSearchOverlayOnBackgroundTap_false_when_overlay_null()
    {
        Assert.IsFalse(MainWindowSearchOverlayShellBridge.ShouldDismissSearchOverlayOnBackgroundTap(new object(), null));
    }

    [TestMethod]
    public void ShouldDismissSearchOverlayOnBackgroundTap_false_when_source_differs_from_overlay()
    {
        var overlay = new object();
        Assert.IsFalse(MainWindowSearchOverlayShellBridge.ShouldDismissSearchOverlayOnBackgroundTap(new object(), overlay));
    }

    [TestMethod]
    public void OnOverlayBackgroundTapDismiss_calls_Hide_when_source_is_overlay_from_findName()
    {
        var overlay = new object();
        object? FindName(string name) => name == "GlobalSearchOverlay" ? overlay : null;

        var mock = new Mock<ISearchOverlayCoordinator>(MockBehavior.Strict);
        mock.Setup(c => c.Hide()).Verifiable();
        var bridge = new MainWindowSearchOverlayShellBridge(mock.Object, FindName);

        bridge.OnOverlayBackgroundTapDismiss(overlay);

        mock.Verify(c => c.Hide(), Times.Once);
    }

    [TestMethod]
    public void OnOverlayBackgroundTapDismiss_does_not_Hide_when_source_is_not_overlay()
    {
        var overlay = new object();
        object? FindName(string name) => name == "GlobalSearchOverlay" ? overlay : null;
        var mock = new Mock<ISearchOverlayCoordinator>(MockBehavior.Strict);
        var bridge = new MainWindowSearchOverlayShellBridge(mock.Object, FindName);

        bridge.OnOverlayBackgroundTapDismiss(new object());

        mock.Verify(c => c.Hide(), Times.Never);
    }

    [TestMethod]
    public void OnOverlayBackgroundTapDismiss_noop_when_coordinator_null()
    {
        var overlay = new object();
        object? FindName(string name) => name == "GlobalSearchOverlay" ? overlay : null;
        var bridge = new MainWindowSearchOverlayShellBridge(null, FindName);

        bridge.OnOverlayBackgroundTapDismiss(overlay);
    }

    [TestMethod]
    public void EnsureGlobalSearchOverlayCollapsed_non_FrameworkElement_does_not_throw()
    {
        var notFe = new object();
        object? FindName(string name) => name == "GlobalSearchOverlay" ? notFe : null;
        var bridge = new MainWindowSearchOverlayShellBridge(null, FindName);
        bridge.EnsureGlobalSearchOverlayCollapsed();
    }

    [TestMethod]
    public void TryCollapseGlobalSearchOverlayIfFrameworkElement_false_for_null()
    {
        Assert.IsFalse(MainWindowSearchOverlayShellBridge.TryCollapseGlobalSearchOverlayIfFrameworkElement(null));
    }

    [TestMethod]
    public void TryCollapseGlobalSearchOverlayIfFrameworkElement_false_for_plain_object()
    {
        Assert.IsFalse(MainWindowSearchOverlayShellBridge.TryCollapseGlobalSearchOverlayIfFrameworkElement(new object()));
    }
}
