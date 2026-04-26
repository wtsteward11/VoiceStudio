using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using VoiceStudio.App.Services;

namespace VoiceStudio.App.Tests.Services;

[TestClass]
public sealed class MainWindowToolbarCommandShellBridgeTests
{
    [TestMethod]
    public void RequestImportAudio_invokes_wired_handler_once()
    {
        var count = 0;
        var bridge = new MainWindowToolbarCommandShellBridge();
        bridge.WireImportAudioHandler(() => count++);

        bridge.RequestImportAudio();

        Assert.AreEqual(1, count);
    }

    [TestMethod]
    public void RequestImportAudio_before_wire_throws_InvalidOperationException()
    {
        var bridge = new MainWindowToolbarCommandShellBridge();

        Assert.ThrowsException<InvalidOperationException>(() => bridge.RequestImportAudio());
    }

    [TestMethod]
    public void WireImportAudioHandler_rejects_null()
    {
        var bridge = new MainWindowToolbarCommandShellBridge();

        Assert.ThrowsException<ArgumentNullException>(() => bridge.WireImportAudioHandler(null!));
    }
}
