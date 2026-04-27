using System;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using VoiceStudio.App.Services;

namespace VoiceStudio.App.Tests.Services;

[TestClass]
public sealed class MainWindowToolbarCustomizationShellBridgeTests
{
    [TestMethod]
    public async Task ShowCustomizationDialogAsync_invokes_launcher_with_shell_xaml_root()
    {
        var mockLauncher = new Mock<IToolbarCustomizationDialogLauncher>(MockBehavior.Strict);
        mockLauncher
            .Setup(l => l.ShowAsync(It.IsAny<XamlRoot?>()))
            .Returns(Task.CompletedTask)
            .Verifiable();
        var bridge = new MainWindowToolbarCustomizationShellBridge(
            () => null,
            mockLauncher.Object,
            () => null);

        await bridge.ShowCustomizationDialogAsync().ConfigureAwait(false);

        mockLauncher.Verify(l => l.ShowAsync(null), Times.Once);
    }

    [TestMethod]
    public async Task ShowCustomizationDialogAsync_on_launcher_exception_shows_toast()
    {
        var mockLauncher = new Mock<IToolbarCustomizationDialogLauncher>(MockBehavior.Strict);
        mockLauncher
            .Setup(l => l.ShowAsync(It.IsAny<XamlRoot?>()))
            .ThrowsAsync(new InvalidOperationException("dialog failed"));
        var mockToast = new Mock<IToastNotificationService>(MockBehavior.Strict);
        mockToast
            .Setup(t => t.ShowError(
                "Customization Failed",
                It.Is<string>(s => s.Contains("dialog failed", StringComparison.Ordinal)),
                It.IsAny<Action?>(),
                It.IsAny<string?>()))
            .Verifiable();
        var bridge = new MainWindowToolbarCustomizationShellBridge(
            () => null,
            mockLauncher.Object,
            () => mockToast.Object);

        await bridge.ShowCustomizationDialogAsync().ConfigureAwait(false);

        mockToast.Verify(
            t => t.ShowError(
                "Customization Failed",
                It.Is<string>(s => s.Contains("dialog failed", StringComparison.Ordinal)),
                It.IsAny<Action?>(),
                It.IsAny<string?>()),
            Times.Once);
    }

    [TestMethod]
    public async Task ShowCustomizationDialogAsync_on_launcher_exception_noop_when_toast_null()
    {
        var mockLauncher = new Mock<IToolbarCustomizationDialogLauncher>(MockBehavior.Strict);
        mockLauncher
            .Setup(l => l.ShowAsync(It.IsAny<XamlRoot?>()))
            .ThrowsAsync(new InvalidOperationException("x"));
        var bridge = new MainWindowToolbarCustomizationShellBridge(
            () => null,
            mockLauncher.Object,
            () => null);

        await bridge.ShowCustomizationDialogAsync().ConfigureAwait(false);
    }
}
