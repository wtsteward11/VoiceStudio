using System;
using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using VoiceStudio.App.Services;
using VoiceStudio.Core.Services;

namespace VoiceStudio.App.Tests.Services;

[TestClass]
public sealed class MainWindowCommandPaletteShellBridgeTests
{
    [TestMethod]
    public void Show_invokes_launcher_with_registry_and_theme_from_factories()
    {
        var registry = new Mock<IPanelRegistry>(MockBehavior.Loose).Object;
        var theme = new ThemeManager();
        var mockLauncher = new Mock<ICommandPaletteShellLauncher>(MockBehavior.Strict);
        mockLauncher
            .Setup(l => l.Show(registry, theme))
            .Verifiable();
        var bridge = new MainWindowCommandPaletteShellBridge(
            () => registry,
            () => theme,
            mockLauncher.Object,
            () => null);

        bridge.Show();

        mockLauncher.Verify(l => l.Show(registry, theme), Times.Once);
    }

    [TestMethod]
    public void Show_on_launcher_exception_logs_and_shows_toast()
    {
        var registry = new Mock<IPanelRegistry>(MockBehavior.Loose).Object;
        var theme = new ThemeManager();
        var mockLauncher = new Mock<ICommandPaletteShellLauncher>(MockBehavior.Strict);
        mockLauncher
            .Setup(l => l.Show(It.IsAny<IPanelRegistry>(), It.IsAny<ThemeManager>()))
            .Throws(new InvalidOperationException("palette failed"));
        var mockToast = new Mock<IToastNotificationService>(MockBehavior.Strict);
        mockToast
            .Setup(t => t.ShowError(
                It.Is<string>(s => s.Contains("palette failed", StringComparison.Ordinal)),
                "Command Palette",
                It.IsAny<Action?>()))
            .Verifiable();
        var mockDiag = new Mock<ICommandPaletteShellDiagnostics>(MockBehavior.Strict);
        mockDiag
            .Setup(d => d.LogCommandPaletteOpenFailure(
                It.Is<string>(s => s.Contains("palette failed", StringComparison.Ordinal)),
                "MainWindowCommandPaletteShellBridge.Show",
                It.IsAny<InvalidOperationException>(),
                It.IsAny<IReadOnlyDictionary<string, object>>()))
            .Verifiable();
        var bridge = new MainWindowCommandPaletteShellBridge(
            () => registry,
            () => theme,
            mockLauncher.Object,
            () => mockToast.Object,
            mockDiag.Object);

        bridge.Show();

        mockDiag.Verify(
            d => d.LogCommandPaletteOpenFailure(
                It.Is<string>(s => s.Contains("palette failed", StringComparison.Ordinal)),
                "MainWindowCommandPaletteShellBridge.Show",
                It.IsAny<InvalidOperationException>(),
                It.IsAny<IReadOnlyDictionary<string, object>>()),
            Times.Once);
        mockToast.Verify(
            t => t.ShowError(
                It.Is<string>(s => s.Contains("palette failed", StringComparison.Ordinal)),
                "Command Palette",
                It.IsAny<Action?>()),
            Times.Once);
    }

    [TestMethod]
    public void Show_on_launcher_exception_noop_when_toast_null()
    {
        var registry = new Mock<IPanelRegistry>(MockBehavior.Loose).Object;
        var theme = new ThemeManager();
        var mockLauncher = new Mock<ICommandPaletteShellLauncher>(MockBehavior.Strict);
        mockLauncher
            .Setup(l => l.Show(It.IsAny<IPanelRegistry>(), It.IsAny<ThemeManager>()))
            .Throws(new InvalidOperationException("x"));
        var bridge = new MainWindowCommandPaletteShellBridge(
            () => registry,
            () => theme,
            mockLauncher.Object,
            () => null);

        bridge.Show();
    }
}
