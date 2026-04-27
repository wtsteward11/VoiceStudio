using System;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using VoiceStudio.App.Services;

namespace VoiceStudio.App.Tests.Services;

[TestClass]
public sealed class MainWindowCustomizeToolbarMenuItemShellBridgeTests
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
        Path.Combine(FindRepoRoot(), "src", "VoiceStudio.App", "Services", "MainWindowCustomizeToolbarMenuItemShellBridge.cs");

    [TestMethod]
    public void Menu_item_shell_bridge_does_not_declare_Customization_Failed_toast_title()
    {
        var text = File.ReadAllText(BridgePath);
        Assert.IsFalse(
            text.Contains("Customization Failed", StringComparison.Ordinal),
            "Anti-creep: toast title belongs in MainWindowToolbarCustomizationShellBridge (Slice 7).");
    }

    [TestMethod]
    public void Menu_item_shell_bridge_forwards_via_ShowCustomizationDialogAsync()
    {
        var text = File.ReadAllText(BridgePath);
        StringAssert.Contains(text, "ShowCustomizationDialogAsync");
    }

    [TestMethod]
    public void Menu_item_shell_bridge_ctor_rejects_null_inner_bridge()
    {
        Assert.ThrowsException<ArgumentNullException>(() =>
            _ = new MainWindowCustomizeToolbarMenuItemShellBridge(null!));
    }

    [TestMethod]
    public async Task RunFlowAsync_invokes_inner_toolbar_customization_bridge()
    {
        var mockLauncher = new Mock<IToolbarCustomizationDialogLauncher>(MockBehavior.Strict);
        mockLauncher
            .Setup(l => l.ShowAsync(It.IsAny<XamlRoot?>()))
            .Returns(Task.CompletedTask)
            .Verifiable();
        var inner = new MainWindowToolbarCustomizationShellBridge(
            () => null,
            mockLauncher.Object,
            () => null);
        var bridge = new MainWindowCustomizeToolbarMenuItemShellBridge(inner);

        await bridge.RunFlowAsync().ConfigureAwait(false);

        mockLauncher.Verify(l => l.ShowAsync(null), Times.Once);
    }

    [TestMethod]
    public void Menu_item_shell_bridge_source_does_not_reference_SwitchToPanel()
    {
        var text = File.ReadAllText(BridgePath);
        Assert.IsFalse(text.Contains("SwitchToPanel", StringComparison.Ordinal));
    }
}
