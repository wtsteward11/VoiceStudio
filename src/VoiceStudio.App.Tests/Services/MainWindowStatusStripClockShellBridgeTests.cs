using System;
using System.IO;
using System.Reflection;
using Microsoft.UI.Dispatching;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using VoiceStudio.App.Services;
using VoiceStudio.App.Tests.Fixtures;

namespace VoiceStudio.App.Tests.Services;

[TestClass]
public sealed class MainWindowStatusStripClockShellBridgeTests
{
    private DispatcherQueueController? _dispatcherController;

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

    private static string BridgeSourcePath =>
        Path.Combine(FindRepoRoot(), "src", "VoiceStudio.App", "Services", "MainWindowStatusStripClockShellBridge.cs");

    [TestInitialize]
    public void Setup()
    {
        _dispatcherController = DispatcherQueueController.CreateOnDedicatedThread();
    }

    [TestCleanup]
    public void TearDown()
    {
        DispatcherQueueTestHelpers.ShutdownSyncBounded(_dispatcherController);
        _dispatcherController = null;
    }

    [TestMethod]
    public void Constructor_throws_when_any_dependency_accessor_is_null()
    {
        var q = _dispatcherController!.DispatcherQueue;
        Assert.ThrowsException<ArgumentNullException>(() =>
            new MainWindowStatusStripClockShellBridge(
                getClockText: null!,
                q,
                () => false));
        Assert.ThrowsException<ArgumentNullException>(() =>
            new MainWindowStatusStripClockShellBridge(
                () => null,
                dispatcherQueue: null!,
                () => false));
        Assert.ThrowsException<ArgumentNullException>(() =>
            new MainWindowStatusStripClockShellBridge(
                () => null,
                q,
                getDisposed: null!));
    }

    [TestMethod]
    public void RefreshClockText_when_clock_text_null_does_not_throw()
    {
        var q = _dispatcherController!.DispatcherQueue;
        var bridge = new MainWindowStatusStripClockShellBridge(() => null, q, () => false);
        bridge.RefreshClockText();
    }

    [TestMethod]
    public void BeginClockTimer_twice_disposes_prior_timer_without_throw()
    {
        var q = _dispatcherController!.DispatcherQueue;
        var bridge = new MainWindowStatusStripClockShellBridge(() => null, q, () => false);
        bridge.BeginClockTimer();
        bridge.BeginClockTimer();
        bridge.DisposeClockTimer();
    }

    [TestMethod]
    public void Bridge_source_creep_forbidden_identifiers_absent()
    {
        var text = File.ReadAllText(BridgeSourcePath);
        Assert.IsFalse(text.Contains("engines/audio/rhvoice/", StringComparison.Ordinal), "RHVoice path must not appear in clock bridge source.");
        Assert.IsFalse(text.Contains("MainWindowNotificationCenterShellBridge", StringComparison.Ordinal), "Unrelated bridge name must not appear in clock bridge source.");
        Assert.IsFalse(text.Contains("GlobalTransportControl", StringComparison.Ordinal), "Transport control must not appear in clock bridge source.");
    }
}
