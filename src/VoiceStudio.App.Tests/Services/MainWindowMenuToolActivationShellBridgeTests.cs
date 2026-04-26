using System;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using VoiceStudio.App.Services;
using VoiceStudio.Core.Services;

namespace VoiceStudio.App.Tests.Services;

[TestClass]
public sealed class MainWindowMenuToolActivationShellBridgeTests
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
        Path.Combine(FindRepoRoot(), "src", "VoiceStudio.App", "Services", "MainWindowMenuToolActivationShellBridge.cs");

    [TestMethod]
    public async Task RunCheckForUpdatesAsync_throws_when_getContext_null()
    {
        var bridge = new MainWindowMenuToolActivationShellBridge();
        await Assert.ThrowsExceptionAsync<ArgumentNullException>(() =>
            bridge.RunCheckForUpdatesAsync(
                null!,
                new Mock<IUpdateService>().Object,
                () => new Mock<IErrorDialogService>().Object));
    }

    [TestMethod]
    public async Task RunCheckForUpdatesAsync_throws_when_updateService_null()
    {
        var bridge = new MainWindowMenuToolActivationShellBridge();
        await Assert.ThrowsExceptionAsync<ArgumentNullException>(() =>
            bridge.RunCheckForUpdatesAsync(
                () => throw new InvalidOperationException("unreachable"),
                null!,
                () => new Mock<IErrorDialogService>().Object));
    }

    [TestMethod]
    public async Task RunToggleMiniTimelineAsync_throws_when_getIsMiniTimelineVisible_null()
    {
        var bridge = new MainWindowMenuToolActivationShellBridge();
        await Assert.ThrowsExceptionAsync<ArgumentNullException>(() =>
            bridge.RunToggleMiniTimelineAsync(
                null!,
                _ => { },
                () => null,
                (_, _) => Task.FromResult(true),
                () => { },
                () => null));
    }

    [TestMethod]
    public void ToggleCollaborationPanelVisibility_throws_when_find_null()
    {
        var bridge = new MainWindowMenuToolActivationShellBridge();
        Assert.ThrowsException<ArgumentNullException>(() => bridge.ToggleCollaborationPanelVisibility(null!));
    }

    [TestMethod]
    public void HideCollaborationPanel_throws_when_find_null()
    {
        var bridge = new MainWindowMenuToolActivationShellBridge();
        Assert.ThrowsException<ArgumentNullException>(() => bridge.HideCollaborationPanel(null!));
    }

    [TestMethod]
    public async Task RunManageWorkspacesAsync_throws_when_getXamlRoot_null()
    {
        var bridge = new MainWindowMenuToolActivationShellBridge();
        await Assert.ThrowsExceptionAsync<ArgumentNullException>(() =>
            bridge.RunManageWorkspacesAsync(null!, () => null));
    }

    [TestMethod]
    public void Menu_tool_activation_shell_bridge_source_excludes_rhvoice_path_segment()
    {
        var text = File.ReadAllText(BridgePath);
        Assert.IsFalse(
            text.Contains("engines/audio/rhvoice", StringComparison.OrdinalIgnoreCase),
            "Slice 20 menu/tool activation shell bridge must not reference RHVoice engine path.");
    }

    [TestMethod]
    public void Menu_tool_activation_shell_bridge_source_excludes_unrelated_bridge_type_names()
    {
        var text = File.ReadAllText(BridgePath);
        Assert.IsFalse(text.Contains("MainWindowStatusBarCoordinatorShellBridge", StringComparison.Ordinal));
        Assert.IsFalse(text.Contains("MainWindowNotificationCenterShellBridge", StringComparison.Ordinal));
        Assert.IsFalse(text.Contains("MainWindowCommandPaletteShellBridge", StringComparison.Ordinal));
    }
}
