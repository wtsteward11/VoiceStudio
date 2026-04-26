using Microsoft.VisualStudio.TestTools.UnitTesting;
using VoiceStudio.App.Services;

namespace VoiceStudio.App.Tests.Services;

[TestClass]
public sealed class MainWindowPanelQuickSwitchShellBridgeTests
{
    [TestMethod]
    public void DisposeQuickSwitchHideTimer_is_idempotent_before_first_show()
    {
        var bridge = new MainWindowPanelQuickSwitchShellBridge();
        bridge.DisposeQuickSwitchHideTimer();
        bridge.DisposeQuickSwitchHideTimer();
    }
}
