using System;
using System.Collections.Generic;
using System.Runtime.Serialization;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Microsoft.UI.Xaml;
using Moq;
using VoiceStudio.App.Services;
using VoiceStudio.Core.Panels;
using VoiceStudio.Core.Services;

namespace VoiceStudio.App.Tests.Services;

[TestClass]
public sealed class MainWindowToolCatalogShellBridgeTests
{
    // SAFETY: WinUI XamlRoot is sealed with no test ctor; tests only pass this handle to Moq
    // and never invoke WinUI APIs on it. FormatterServices is obsolete (SYSLIB0050) but is the
    // smallest deterministic non-null token for strict mock matching.
#pragma warning disable SYSLIB0050
    private static XamlRoot CreateUninitializedXamlRoot() =>
        (XamlRoot)FormatterServices.GetUninitializedObject(typeof(XamlRoot));
#pragma warning restore SYSLIB0050

    [TestMethod]
    public async Task RunShowAsync_when_xaml_root_null_does_not_invoke_launcher()
    {
        var mockLauncher = new Mock<IToolCatalogShellLauncher>(MockBehavior.Strict);
        var bridge = new MainWindowToolCatalogShellBridge(
            () => null,
            mockLauncher.Object,
            () => null);
        bridge.WireToolCatalogHandlers(
            (_, _) => Task.FromResult(true),
            (_, _, _) => { });

        await bridge.RunShowAsync().ConfigureAwait(true);

        mockLauncher.Verify(l => l.ShowAsync(It.IsAny<XamlRoot>()), Times.Never);
    }

    [TestMethod]
    public async Task RunShowAsync_when_xaml_root_null_and_toast_available_shows_error()
    {
        var mockLauncher = new Mock<IToolCatalogShellLauncher>(MockBehavior.Strict);
        var mockToast = new Mock<IToastNotificationService>(MockBehavior.Strict);
        mockToast
            .Setup(t => t.ShowError(
                It.Is<string>(s => s.Contains("XamlRoot", StringComparison.Ordinal)),
                "Tool Catalog",
                It.IsAny<Action?>(),
                It.IsAny<string?>()))
            .Verifiable();
        var bridge = new MainWindowToolCatalogShellBridge(
            () => null,
            mockLauncher.Object,
            () => mockToast.Object);
        bridge.WireToolCatalogHandlers(
            (_, _) => Task.FromResult(true),
            (_, _, _) => { });

        await bridge.RunShowAsync().ConfigureAwait(true);

        mockToast.Verify(
            t => t.ShowError(
                It.Is<string>(s => s.Contains("XamlRoot", StringComparison.Ordinal)),
                "Tool Catalog",
                It.IsAny<Action?>(),
                It.IsAny<string?>()),
            Times.Once);
    }

    [TestMethod]
    public async Task RunShowAsync_invokes_launcher_with_xaml_root_from_factory()
    {
        var root = CreateUninitializedXamlRoot();
        var mockLauncher = new Mock<IToolCatalogShellLauncher>(MockBehavior.Strict);
        mockLauncher
            .Setup(l => l.ShowAsync(It.IsAny<XamlRoot>()))
            .ReturnsAsync((ToolCatalogShellChoice?)null)
            .Verifiable();
        var bridge = new MainWindowToolCatalogShellBridge(
            () => root,
            mockLauncher.Object,
            () => null);
        bridge.WireToolCatalogHandlers(
            (_, _) => Task.FromResult(true),
            (_, _, _) => { });

        await bridge.RunShowAsync().ConfigureAwait(true);

        mockLauncher.Verify(l => l.ShowAsync(It.IsAny<XamlRoot>()), Times.Once);
    }

    [TestMethod]
    public async Task RunShowAsync_when_launcher_returns_null_does_not_invoke_open_delegate()
    {
        var root = CreateUninitializedXamlRoot();
        var mockLauncher = new Mock<IToolCatalogShellLauncher>(MockBehavior.Strict);
        mockLauncher.Setup(l => l.ShowAsync(It.IsAny<XamlRoot>())).ReturnsAsync((ToolCatalogShellChoice?)null);
        var openCalls = 0;
        var bridge = new MainWindowToolCatalogShellBridge(
            () => root,
            mockLauncher.Object,
            () => null);
        bridge.WireToolCatalogHandlers(
            (_, _) =>
            {
                openCalls++;
                return Task.FromResult(true);
            },
            (_, _, _) => { });

        await bridge.RunShowAsync().ConfigureAwait(true);

        Assert.AreEqual(0, openCalls);
    }

    [TestMethod]
    public async Task RunShowAsync_when_choice_invokes_open_and_apply_when_open_succeeds()
    {
        var root = CreateUninitializedXamlRoot();
        var choice = new ToolCatalogShellChoice
        {
            PanelId = "Profiles",
            EffectiveRegion = PanelRegion.Left,
            DisplayName = "Profiles",
            Icon = "icon.png"
        };
        var mockLauncher = new Mock<IToolCatalogShellLauncher>(MockBehavior.Strict);
        mockLauncher.Setup(l => l.ShowAsync(It.IsAny<XamlRoot>())).ReturnsAsync(choice);
        string? seenPanelId = null;
        PanelRegion? seenRegion = null;
        var applyCalls = 0;
        PanelRegion? applyRegion = null;
        string? applyTitle = null;
        string? applyIcon = null;
        var bridge = new MainWindowToolCatalogShellBridge(
            () => root,
            mockLauncher.Object,
            () => null);
        bridge.WireToolCatalogHandlers(
            (pid, reg) =>
            {
                seenPanelId = pid;
                seenRegion = reg;
                return Task.FromResult(true);
            },
            (r, t, i) =>
            {
                applyCalls++;
                applyRegion = r;
                applyTitle = t;
                applyIcon = i;
            });

        await bridge.RunShowAsync().ConfigureAwait(true);

        Assert.AreEqual("Profiles", seenPanelId);
        Assert.AreEqual(PanelRegion.Left, seenRegion);
        Assert.AreEqual(1, applyCalls);
        Assert.AreEqual(PanelRegion.Left, applyRegion);
        Assert.AreEqual("Profiles", applyTitle);
        Assert.AreEqual("icon.png", applyIcon);
    }

    [TestMethod]
    public async Task RunShowAsync_when_open_returns_false_does_not_invoke_apply()
    {
        var root = CreateUninitializedXamlRoot();
        var choice = new ToolCatalogShellChoice
        {
            PanelId = "X",
            EffectiveRegion = PanelRegion.Center,
            DisplayName = "X",
            Icon = null
        };
        var mockLauncher = new Mock<IToolCatalogShellLauncher>(MockBehavior.Strict);
        mockLauncher.Setup(l => l.ShowAsync(It.IsAny<XamlRoot>())).ReturnsAsync(choice);
        var applyCalls = 0;
        var bridge = new MainWindowToolCatalogShellBridge(
            () => root,
            mockLauncher.Object,
            () => null);
        bridge.WireToolCatalogHandlers(
            (_, _) => Task.FromResult(false),
            (_, _, _) => applyCalls++);

        await bridge.RunShowAsync().ConfigureAwait(true);

        Assert.AreEqual(0, applyCalls);
    }

    [TestMethod]
    public async Task RunShowAsync_before_wire_throws_InvalidOperationException_when_launcher_returns_choice()
    {
        var root = CreateUninitializedXamlRoot();
        var choice = new ToolCatalogShellChoice
        {
            PanelId = "X",
            EffectiveRegion = PanelRegion.Right,
            DisplayName = "X",
            Icon = null
        };
        var mockLauncher = new Mock<IToolCatalogShellLauncher>(MockBehavior.Strict);
        mockLauncher.Setup(l => l.ShowAsync(It.IsAny<XamlRoot>())).ReturnsAsync(choice);
        var bridge = new MainWindowToolCatalogShellBridge(
            () => root,
            mockLauncher.Object,
            () => null);

        await Assert.ThrowsExceptionAsync<InvalidOperationException>(() => bridge.RunShowAsync())
            .ConfigureAwait(true);
    }

    [TestMethod]
    public async Task RunShowAsync_on_launcher_exception_logs_and_shows_toast()
    {
        var root = CreateUninitializedXamlRoot();
        var mockLauncher = new Mock<IToolCatalogShellLauncher>(MockBehavior.Strict);
        mockLauncher
            .Setup(l => l.ShowAsync(It.IsAny<XamlRoot>()))
            .ThrowsAsync(new InvalidOperationException("catalog dialog failed"));
        var mockToast = new Mock<IToastNotificationService>(MockBehavior.Strict);
        mockToast
            .Setup(t => t.ShowError(
                It.Is<string>(s => s.Contains("catalog dialog failed", StringComparison.Ordinal)),
                "Tool Catalog",
                It.IsAny<Action?>(),
                It.IsAny<string?>()))
            .Verifiable();
        var mockDiag = new Mock<IToolCatalogShellDiagnostics>(MockBehavior.Strict);
        mockDiag
            .Setup(d => d.LogToolCatalogFailure(
                It.Is<string>(s => s.Contains("catalog dialog failed", StringComparison.Ordinal)),
                "MainWindowToolCatalogShellBridge.RunShowAsync",
                It.IsAny<InvalidOperationException>(),
                It.IsAny<IReadOnlyDictionary<string, object>>()))
            .Verifiable();
        var bridge = new MainWindowToolCatalogShellBridge(
            () => root,
            mockLauncher.Object,
            () => mockToast.Object,
            mockDiag.Object);
        bridge.WireToolCatalogHandlers(
            (_, _) => Task.FromResult(true),
            (_, _, _) => { });

        await bridge.RunShowAsync().ConfigureAwait(true);

        mockDiag.Verify(
            d => d.LogToolCatalogFailure(
                It.Is<string>(s => s.Contains("catalog dialog failed", StringComparison.Ordinal)),
                "MainWindowToolCatalogShellBridge.RunShowAsync",
                It.IsAny<InvalidOperationException>(),
                It.IsAny<IReadOnlyDictionary<string, object>>()),
            Times.Once);
        mockToast.Verify(
            t => t.ShowError(
                It.Is<string>(s => s.Contains("catalog dialog failed", StringComparison.Ordinal)),
                "Tool Catalog",
                It.IsAny<Action?>(),
                It.IsAny<string?>()),
            Times.Once);
    }
}
