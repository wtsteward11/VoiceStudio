using System;
using System.IO;
using System.Reflection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using VoiceStudio.App.Services;

namespace VoiceStudio.App.Tests.Services;

[TestClass]
public sealed class MainWindowFileActivationShellBridgeTests
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

    private static string BridgePath =>
        Path.Combine(FindRepoRoot(), "src", "VoiceStudio.App", "Services", "MainWindowFileActivationShellBridge.cs");

    [TestMethod]
    public void Constructor_throws_when_any_dependency_accessor_is_null()
    {
        var startupMock = new Mock<IStartupStateService>();
        startupMock.SetupGet(s => s.IsReady).Returns(true);
        var startup = startupMock.Object;
        Assert.ThrowsException<ArgumentNullException>(() =>
            new MainWindowFileActivationShellBridge(
                getCoordinator: null!,
                () => startup,
                () => null,
                () => null));

        Assert.ThrowsException<ArgumentNullException>(() =>
            new MainWindowFileActivationShellBridge(
                () => null,
                getStartupStateService: null!,
                () => null,
                () => null));

        Assert.ThrowsException<ArgumentNullException>(() =>
            new MainWindowFileActivationShellBridge(
                () => null,
                () => startup,
                getToast: null!,
                () => null));

        Assert.ThrowsException<ArgumentNullException>(() =>
            new MainWindowFileActivationShellBridge(
                () => null,
                () => startup,
                () => null,
                getShellNavigation: null!));
    }

    [TestMethod]
    public void Bridge_source_creep_forbidden_identifiers_absent_when_jump_list_dispatch_out_of_slice14()
    {
        var text = File.ReadAllText(BridgePath);
        var forbidden = new[]
        {
            "TryDispatchPendingJumpListActivation",
            "RunJumpListPendingAsync",
            "JumpListActivation",
            "WireNotificationCenter",
            "MainWindowCommandPaletteShellBridge",
            "MainWindowToolCatalogShellBridge",
            "engines/audio/rhvoice/",
        };

        foreach (var f in forbidden)
        {
            Assert.IsFalse(
                text.Contains(f, StringComparison.Ordinal),
                $"Bridge source must not contain '{f}'.");
        }
    }
}
