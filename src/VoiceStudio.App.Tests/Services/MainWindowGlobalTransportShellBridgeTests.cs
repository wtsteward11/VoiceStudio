using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using VoiceStudio.App.Controls;
using VoiceStudio.App.Services;
using VoiceStudio.Core.Events;
using VoiceStudio.Core.Models;
using VoiceStudio.Core.Panels;
using VoiceStudio.Core.Services;

namespace VoiceStudio.App.Tests.Services;

[TestClass]
public sealed class MainWindowGlobalTransportShellBridgeTests
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
        Path.Combine(FindRepoRoot(), "src", "VoiceStudio.App", "Services", "MainWindowGlobalTransportShellBridge.cs");

    [TestMethod]
    public void Global_transport_bridge_does_not_reference_other_mainwindow_shell_bridge_types()
    {
        var text = File.ReadAllText(BridgePath);
        Assert.IsFalse(text.Contains("MainWindowHelpAboutShellBridge", StringComparison.Ordinal));
        Assert.IsFalse(text.Contains("MainWindowEditUndoRedoShellBridge", StringComparison.Ordinal));
        Assert.IsFalse(text.Contains("IBackendClient", StringComparison.Ordinal));
    }

    [TestMethod]
    public void OpenRecordingPanelFromTransportShortcut_publishes_NavigateToEvent_when_ready()
    {
        var published = new List<object>();
        var bridge = new MainWindowGlobalTransportShellBridge();
        var fakeAgg = new FakeEventAggregator(m => published.Add(m));

        bridge.OpenRecordingPanelFromTransportShortcut(
            () => new ReadyFakeStartupStateService(),
            () => null,
            () => fakeAgg);

        Assert.AreEqual(1, published.Count);
        Assert.IsInstanceOfType(published[0], typeof(NavigateToEvent));
        var nav = (NavigateToEvent)published[0];
        Assert.AreEqual(PanelIds.Recording, nav.TargetPanelId);
    }

    [TestMethod]
    public void ZoomIn_does_not_throw_when_no_timeline()
    {
        var bridge = new MainWindowGlobalTransportShellBridge();
        bridge.ZoomIn(() => null);
    }

    private sealed class ReadyFakeStartupStateService : IStartupStateService
    {
        public StartupState CurrentState => StartupState.BackendReady;
        public string? FailureMessage => null;
        public bool IsReady => true;
        public event EventHandler<StartupStateChangedEventArgs>? StateChanged;

        public void SetBackendStarting() { }
        public void SetBackendReady() { }
        public void SetBackendFailed(string message) { }
        public void SetDegraded() { }
    }

    private sealed class FakeEventAggregator : IEventAggregator
    {
        private readonly Action<object> _onPublish;

        public FakeEventAggregator(Action<object> onPublish) => _onPublish = onPublish;

        public void Publish<TEvent>(TEvent eventMessage) where TEvent : class
        {
            if (eventMessage is not null)
            {
                _onPublish(eventMessage);
            }
        }

        public Task PublishAsync<TEvent>(TEvent eventMessage) where TEvent : class => Task.CompletedTask;

        public ISubscriptionToken Subscribe<TEvent>(Action<TEvent> handler) where TEvent : class =>
            throw new NotSupportedException();

        public ISubscriptionToken Subscribe<TEvent>(Func<TEvent, Task> handler) where TEvent : class =>
            throw new NotSupportedException();

        public void Unsubscribe(ISubscriptionToken token) { }

        public void UnsubscribeAll(object subscriber) { }
    }
}
