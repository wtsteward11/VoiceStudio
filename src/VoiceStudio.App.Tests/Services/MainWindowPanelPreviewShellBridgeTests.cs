using Microsoft.VisualStudio.TestTools.UnitTesting;
using VoiceStudio.App.Services;

namespace VoiceStudio.App.Tests.Services;

[TestClass]
public sealed class MainWindowPanelPreviewShellBridgeTests
{
    [TestMethod]
    public void GetPanelInfoForButton_NavStudio_maps_to_Timeline()
    {
        var info = MainWindowPanelPreviewShellBridge.GetPanelInfoForButton("NavStudio");
        Assert.IsNotNull(info);
        Assert.AreEqual("Timeline", info.Value.PanelId);
    }

    [TestMethod]
    public void GetPanelInfoForButton_unknown_returns_null()
    {
        var info = MainWindowPanelPreviewShellBridge.GetPanelInfoForButton("NavUnknown");
        Assert.IsNull(info);
    }

    [TestMethod]
    public void CreatePreviewContent_unknown_returns_null()
    {
        var el = MainWindowPanelPreviewShellBridge.CreatePreviewContent("NoSuchPanel");
        Assert.IsNull(el);
    }
}
