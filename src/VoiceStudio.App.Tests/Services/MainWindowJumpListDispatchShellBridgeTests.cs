using System;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using VoiceStudio.App.Services;

namespace VoiceStudio.App.Tests.Services;

[TestClass]
public sealed class MainWindowJumpListDispatchShellBridgeTests
{
    [TestInitialize]
    public void DrainJumpListPending()
    {
        while (JumpListActivation.TryConsumePending() != null)
        {
        }
    }

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
        Path.Combine(FindRepoRoot(), "src", "VoiceStudio.App", "Services", "MainWindowJumpListDispatchShellBridge.cs");

    [TestMethod]
    public void Constructor_throws_when_any_dependency_accessor_is_null()
    {
        var startupMock = new Mock<IStartupStateService>();
        startupMock.SetupGet(s => s.IsReady).Returns(true);
        var startup = startupMock.Object;

        Assert.ThrowsException<ArgumentNullException>(() =>
            new MainWindowJumpListDispatchShellBridge(
                getCoordinator: null!,
                () => startup,
                () => null));

        Assert.ThrowsException<ArgumentNullException>(() =>
            new MainWindowJumpListDispatchShellBridge(
                () => null,
                getStartupStateService: null!,
                () => null));

        Assert.ThrowsException<ArgumentNullException>(() =>
            new MainWindowJumpListDispatchShellBridge(
                () => null,
                () => startup,
                getToast: null!));
    }

    [TestMethod]
    public void TryDispatchPendingJumpListActivation_invokes_create_new_project_when_pending_and_startup_ready()
    {
        JumpListActivation.SetPendingIfParsed(JumpListArgs.NewProject, null);

        var coordMock = new Mock<IProjectWorkflowCoordinator>();
        coordMock.Setup(c => c.CreateNewProjectAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        var startupMock = new Mock<IStartupStateService>();
        startupMock.SetupGet(s => s.IsReady).Returns(true);

        var bridge = new MainWindowJumpListDispatchShellBridge(
            () => coordMock.Object,
            () => startupMock.Object,
            () => null);

        bridge.TryDispatchPendingJumpListActivation();

        coordMock.Verify(c => c.CreateNewProjectAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [TestMethod]
    public void TryDispatchPendingJumpListActivation_with_null_coordinator_does_not_throw_after_consume()
    {
        JumpListActivation.SetPendingIfParsed(JumpListArgs.NewProject, null);

        var startupMock = new Mock<IStartupStateService>();
        startupMock.SetupGet(s => s.IsReady).Returns(true);

        var bridge = new MainWindowJumpListDispatchShellBridge(
            () => null,
            () => startupMock.Object,
            () => null);

        bridge.TryDispatchPendingJumpListActivation();
    }

    [TestMethod]
    public void Bridge_source_creep_forbidden_identifiers_absent()
    {
        var text = File.ReadAllText(BridgePath);
        var forbidden = new[]
        {
            "TryDispatchPendingFileActivation",
            "MainWindowFileActivationShellBridge",
            "WireNotificationCenter",
            "MainWindowCommandPaletteShellBridge",
            "MainWindowToolCatalogShellBridge",
            "engines/audio/rhvoice/",
            "App.MainWindowInstance",
            "WireJumpList",
            "SetWindowHandle",
        };

        foreach (var f in forbidden)
        {
            Assert.IsFalse(
                text.Contains(f, StringComparison.Ordinal),
                $"Bridge source must not contain '{f}'.");
        }
    }
}
