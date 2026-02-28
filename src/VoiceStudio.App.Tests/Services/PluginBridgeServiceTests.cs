using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using VoiceStudio.App.Services;
using VoiceStudio.Core.Plugins;

namespace VoiceStudio.App.Tests.Services;

/// <summary>
/// Tests for PluginBridgeService class.
/// Phase 1: Tests for frontend-backend plugin synchronization.
/// </summary>
[TestClass]
public class PluginBridgeServiceTests
{
    private Mock<ILogger<PluginBridgeService>> _mockLogger = null!;

    [TestInitialize]
    public void Setup()
    {
        _mockLogger = new Mock<ILogger<PluginBridgeService>>();
    }

    [TestMethod]
    public void Constructor_InitializesCorrectly()
    {
        using var bridge = new PluginBridgeService(_mockLogger.Object);
        Assert.IsNotNull(bridge);
    }

    [TestMethod]
    public void IsConnected_InitiallyFalse()
    {
        using var bridge = new PluginBridgeService(_mockLogger.Object);
        
        Assert.IsFalse(bridge.IsConnected);
    }

    [TestMethod]
    public void GetAllPluginStatuses_InitiallyEmpty()
    {
        using var bridge = new PluginBridgeService(_mockLogger.Object);
        
        var statuses = bridge.GetAllPluginStatuses();
        
        Assert.IsNotNull(statuses);
        Assert.AreEqual(0, statuses.Count);
    }

    [TestMethod]
    public void GetPluginStatus_UnknownPlugin_ReturnsNull()
    {
        using var bridge = new PluginBridgeService(_mockLogger.Object);
        
        var status = bridge.GetPluginStatus("nonexistent_plugin");
        
        Assert.IsNull(status);
    }

    [TestMethod]
    public async Task EnablePluginAsync_WithoutConnection_ThrowsInvalidOperationException()
    {
        using var bridge = new PluginBridgeService(_mockLogger.Object);
        
        // Without WebSocket connected, should throw
        await Assert.ThrowsExceptionAsync<InvalidOperationException>(
            () => bridge.EnablePluginAsync("test_plugin"));
    }

    [TestMethod]
    public async Task DisablePluginAsync_WithoutConnection_ThrowsInvalidOperationException()
    {
        using var bridge = new PluginBridgeService(_mockLogger.Object);
        
        await Assert.ThrowsExceptionAsync<InvalidOperationException>(
            () => bridge.DisablePluginAsync("test_plugin"));
    }

    [TestMethod]
    public async Task ReloadPluginAsync_WithoutConnection_ThrowsInvalidOperationException()
    {
        using var bridge = new PluginBridgeService(_mockLogger.Object);
        
        await Assert.ThrowsExceptionAsync<InvalidOperationException>(
            () => bridge.ReloadPluginAsync("test_plugin"));
    }

    [TestMethod]
    public async Task CheckPluginHealthAsync_WithoutConnection_ThrowsInvalidOperationException()
    {
        using var bridge = new PluginBridgeService(_mockLogger.Object);
        
        await Assert.ThrowsExceptionAsync<InvalidOperationException>(
            () => bridge.CheckPluginHealthAsync("test_plugin"));
    }

    [TestMethod]
    public async Task RequestFullSyncAsync_WithoutConnection_ThrowsInvalidOperationException()
    {
        using var bridge = new PluginBridgeService(_mockLogger.Object);
        
        await Assert.ThrowsExceptionAsync<InvalidOperationException>(
            () => bridge.RequestFullSyncAsync());
    }

    [TestMethod]
    public void Dispose_CleansUpResources()
    {
        var bridge = new PluginBridgeService(_mockLogger.Object);
        
        bridge.Dispose();
        
        // Should not throw on second dispose
        bridge.Dispose();
    }

    [TestMethod]
    public void Connected_CanSubscribe()
    {
        using var bridge = new PluginBridgeService(_mockLogger.Object);
        
        var eventRaised = false;
        bridge.Connected += (s, e) => eventRaised = true;
        
        // Event won't fire without WebSocket, but subscription shouldn't throw
        Assert.IsFalse(eventRaised);
    }

    [TestMethod]
    public void Disconnected_CanSubscribe()
    {
        using var bridge = new PluginBridgeService(_mockLogger.Object);
        
        var eventRaised = false;
        bridge.Disconnected += (s, e) => eventRaised = true;
        
        Assert.IsFalse(eventRaised);
    }

    [TestMethod]
    public void PluginStateChanged_CanSubscribe()
    {
        using var bridge = new PluginBridgeService(_mockLogger.Object);
        
        var eventRaised = false;
        bridge.PluginStateChanged += (s, e) => eventRaised = true;
        
        Assert.IsFalse(eventRaised);
    }

    [TestMethod]
    public void PluginAdded_CanSubscribe()
    {
        using var bridge = new PluginBridgeService(_mockLogger.Object);
        
        var eventRaised = false;
        bridge.PluginAdded += (s, e) => eventRaised = true;
        
        Assert.IsFalse(eventRaised);
    }

    [TestMethod]
    public void PluginRemoved_CanSubscribe()
    {
        using var bridge = new PluginBridgeService(_mockLogger.Object);
        
        var eventRaised = false;
        bridge.PluginRemoved += (s, e) => eventRaised = true;
        
        Assert.IsFalse(eventRaised);
    }

    [TestMethod]
    public void FullSyncReceived_CanSubscribe()
    {
        using var bridge = new PluginBridgeService(_mockLogger.Object);
        
        var eventRaised = false;
        bridge.FullSyncReceived += (s, e) => eventRaised = true;
        
        Assert.IsFalse(eventRaised);
    }

    [TestMethod]
    public async Task ConnectAsync_WithDisposedService_ThrowsObjectDisposedException()
    {
        var bridge = new PluginBridgeService(_mockLogger.Object);
        bridge.Dispose();
        
        await Assert.ThrowsExceptionAsync<ObjectDisposedException>(
            () => bridge.ConnectAsync("http://localhost:8000"));
    }
}
