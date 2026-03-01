using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using VoiceStudio.App.Services;
using VoiceStudio.Core.Plugins;

namespace VoiceStudio.App.Tests.Plugins;

/// <summary>
/// Tests for PluginPermissionManager class.
/// Phase 1: Tests for permission lifecycle management.
/// </summary>
[TestClass]
public class PluginPermissionManagerTests
{
    private PluginPermissionManager _manager = null!;

    [TestInitialize]
    public void Setup()
    {
        _manager = new PluginPermissionManager();
    }

    [TestCleanup]
    public void Cleanup()
    {
        _manager?.Dispose();
    }

    [TestMethod]
    public void Constructor_InitializesCorrectly()
    {
        using var manager = new PluginPermissionManager();
        Assert.IsNotNull(manager);
    }

    [TestMethod]
    public void GetPermissionStatus_NotRequested_ReturnsNotRequested()
    {
        var status = _manager.GetPermissionStatus("test_plugin", "filesystem.read");
        Assert.AreEqual(PermissionStatus.NotRequested, status);
    }

    [TestMethod]
    public void HasPermission_NoGrants_ReturnsFalse()
    {
        var result = _manager.HasPermission("test_plugin", "filesystem.read");
        Assert.IsFalse(result);
    }

    [TestMethod]
    public async Task GrantPermissionAsync_SetsGrantedStatus()
    {
        await _manager.GrantPermissionAsync("test_plugin", "filesystem.read");

        var status = _manager.GetPermissionStatus("test_plugin", "filesystem.read");
        Assert.AreEqual(PermissionStatus.Granted, status);
    }

    [TestMethod]
    public async Task GrantPermissionAsync_HasPermissionReturnsTrue()
    {
        await _manager.GrantPermissionAsync("test_plugin", "filesystem.read");

        var result = _manager.HasPermission("test_plugin", "filesystem.read");
        Assert.IsTrue(result);
    }

    [TestMethod]
    public async Task DenyPermissionAsync_SetsDeniedStatus()
    {
        await _manager.DenyPermissionAsync("test_plugin", "filesystem.read");

        var status = _manager.GetPermissionStatus("test_plugin", "filesystem.read");
        Assert.AreEqual(PermissionStatus.Denied, status);
    }

    [TestMethod]
    public async Task DenyPermissionAsync_HasPermissionReturnsFalse()
    {
        await _manager.DenyPermissionAsync("test_plugin", "filesystem.read");

        var result = _manager.HasPermission("test_plugin", "filesystem.read");
        Assert.IsFalse(result);
    }

    [TestMethod]
    public async Task RevokePermissionAsync_SetsRevokedStatus()
    {
        await _manager.GrantPermissionAsync("test_plugin", "filesystem.read");
        await _manager.RevokePermissionAsync("test_plugin", "filesystem.read");

        var status = _manager.GetPermissionStatus("test_plugin", "filesystem.read");
        Assert.AreEqual(PermissionStatus.Revoked, status);
    }

    [TestMethod]
    public async Task RevokeAllPermissionsAsync_RevokesAll()
    {
        await _manager.GrantPermissionAsync("test_plugin", "filesystem.read");
        await _manager.GrantPermissionAsync("test_plugin", "filesystem.write");
        await _manager.GrantPermissionAsync("test_plugin", "network.http");

        await _manager.RevokeAllPermissionsAsync("test_plugin");

        Assert.AreEqual(PermissionStatus.Revoked, _manager.GetPermissionStatus("test_plugin", "filesystem.read"));
        Assert.AreEqual(PermissionStatus.Revoked, _manager.GetPermissionStatus("test_plugin", "filesystem.write"));
        Assert.AreEqual(PermissionStatus.Revoked, _manager.GetPermissionStatus("test_plugin", "network.http"));
    }

    [TestMethod]
    public async Task GetPluginPermissions_ReturnsAllGrants()
    {
        await _manager.GrantPermissionAsync("test_plugin", "filesystem.read");
        await _manager.GrantPermissionAsync("test_plugin", "filesystem.write");

        var permissions = _manager.GetPluginPermissions("test_plugin");

        Assert.AreEqual(2, permissions.Count);
        Assert.IsTrue(permissions.ContainsKey("filesystem.read"));
        Assert.IsTrue(permissions.ContainsKey("filesystem.write"));
    }

    [TestMethod]
    public void GetPluginPermissions_NoGrants_ReturnsEmpty()
    {
        var permissions = _manager.GetPluginPermissions("nonexistent_plugin");
        Assert.AreEqual(0, permissions.Count);
    }

    [TestMethod]
    public async Task HasAllPermissions_AllGranted_ReturnsTrue()
    {
        await _manager.GrantPermissionAsync("test_plugin", "filesystem.read");
        await _manager.GrantPermissionAsync("test_plugin", "filesystem.write");

        var result = _manager.HasAllPermissions("test_plugin", new[] { "filesystem.read", "filesystem.write" });
        Assert.IsTrue(result);
    }

    [TestMethod]
    public async Task HasAllPermissions_SomeMissing_ReturnsFalse()
    {
        await _manager.GrantPermissionAsync("test_plugin", "filesystem.read");

        var result = _manager.HasAllPermissions("test_plugin", new[] { "filesystem.read", "filesystem.write" });
        Assert.IsFalse(result);
    }

    [TestMethod]
    public async Task HasAnyPermission_SomeGranted_ReturnsTrue()
    {
        await _manager.GrantPermissionAsync("test_plugin", "filesystem.read");

        var result = _manager.HasAnyPermission("test_plugin", new[] { "filesystem.read", "filesystem.write" });
        Assert.IsTrue(result);
    }

    [TestMethod]
    public void HasAnyPermission_NoneGranted_ReturnsFalse()
    {
        var result = _manager.HasAnyPermission("test_plugin", new[] { "filesystem.read", "filesystem.write" });
        Assert.IsFalse(result);
    }

    [TestMethod]
    public async Task RequestPermissionsAsync_LowRiskAutoGrants()
    {
        // filesystem.read.self is low risk, should auto-grant
        var result = await _manager.RequestPermissionsAsync(
            "test_plugin",
            new[] { PluginPermissions.FileReadSelf }
        );

        Assert.IsFalse(result.RequiresUserInteraction);
        Assert.AreEqual(1, result.AutoGranted.Count);
        Assert.IsTrue(result.AllGranted);
    }

    [TestMethod]
    public async Task RequestPermissionsAsync_HighRiskNeedsConsent()
    {
        // system.execute is high risk, should need consent
        var result = await _manager.RequestPermissionsAsync(
            "test_plugin",
            new[] { PluginPermissions.SystemExecute }
        );

        Assert.IsTrue(result.RequiresUserInteraction);
        Assert.AreEqual(1, result.NeedsUserConsent.Count);
        Assert.IsFalse(result.AllGranted);
    }

    [TestMethod]
    public async Task RequestPermissionsAsync_AlreadyGrantedReturnsCorrectly()
    {
        await _manager.GrantPermissionAsync("test_plugin", PluginPermissions.FileReadSelf);

        var result = await _manager.RequestPermissionsAsync(
            "test_plugin",
            new[] { PluginPermissions.FileReadSelf }
        );

        Assert.AreEqual(1, result.AlreadyGranted.Count);
        Assert.AreEqual(0, result.NeedsUserConsent.Count);
        Assert.IsTrue(result.AllGranted);
    }

    [TestMethod]
    public async Task RequestPermissionsAsync_AlreadyDeniedReturnsCorrectly()
    {
        await _manager.DenyPermissionAsync("test_plugin", PluginPermissions.FileReadSelf);

        var result = await _manager.RequestPermissionsAsync(
            "test_plugin",
            new[] { PluginPermissions.FileReadSelf }
        );

        Assert.AreEqual(1, result.AlreadyDenied.Count);
        Assert.IsFalse(result.AllGranted);
    }

    [TestMethod]
    public async Task PermissionChanged_EventRaised()
    {
        var eventRaised = false;
        string? pluginId = null;
        string? permissionId = null;

        _manager.PermissionChanged += (s, e) =>
        {
            eventRaised = true;
            pluginId = e.PluginId;
            permissionId = e.PermissionId;
        };

        await _manager.GrantPermissionAsync("test_plugin", "filesystem.read");

        Assert.IsTrue(eventRaised);
        Assert.AreEqual("test_plugin", pluginId);
        Assert.AreEqual("filesystem.read", permissionId);
    }

    [TestMethod]
    public async Task GetPluginsWithPermissions_ReturnsPluginIds()
    {
        await _manager.GrantPermissionAsync("plugin1", "filesystem.read");
        await _manager.GrantPermissionAsync("plugin2", "network.http");

        var plugins = _manager.GetPluginsWithPermissions();

        Assert.AreEqual(2, plugins.Count);
        Assert.IsTrue(plugins.Contains("plugin1"));
        Assert.IsTrue(plugins.Contains("plugin2"));
    }

    [TestMethod]
    public async Task GrantWithExpiration_ExpiredPermissionNotValid()
    {
        // Grant with very short duration
        await _manager.GrantPermissionAsync(
            "test_plugin",
            "filesystem.read",
            TimeSpan.FromMilliseconds(1)
        );

        // Wait for expiration
        await Task.Delay(10);

        // Should no longer be granted
        Assert.IsFalse(_manager.HasPermission("test_plugin", "filesystem.read"));
    }

    [TestMethod]
    public async Task MultiplePlugins_IsolatedPermissions()
    {
        await _manager.GrantPermissionAsync("plugin1", "filesystem.read");
        await _manager.GrantPermissionAsync("plugin2", "network.http");

        Assert.IsTrue(_manager.HasPermission("plugin1", "filesystem.read"));
        Assert.IsFalse(_manager.HasPermission("plugin1", "network.http"));
        
        Assert.IsFalse(_manager.HasPermission("plugin2", "filesystem.read"));
        Assert.IsTrue(_manager.HasPermission("plugin2", "network.http"));
    }
}
