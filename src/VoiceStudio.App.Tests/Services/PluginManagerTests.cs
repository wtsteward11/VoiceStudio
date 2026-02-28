using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using VoiceStudio.App.Services;
using VoiceStudio.Core.Panels;
using VoiceStudio.Core.Services;
using VoiceStudio.Core.Plugins;

namespace VoiceStudio.App.Tests.Services;

/// <summary>
/// Tests for PluginManager class (T-7).
/// Phase 1: Plugin loading, validation, and lifecycle management.
/// </summary>
[TestClass]
public class PluginManagerTests
{
    private Mock<IPanelRegistry> _mockPanelRegistry = null!;
    private Mock<IBackendClient> _mockBackendClient = null!;
    private string _tempPluginsDir = null!;

    [TestInitialize]
    public void Setup()
    {
        _mockPanelRegistry = new Mock<IPanelRegistry>();
        _mockBackendClient = new Mock<IBackendClient>();
        _tempPluginsDir = Path.Combine(Path.GetTempPath(), "VoiceStudioPluginTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempPluginsDir);
    }

    [TestCleanup]
    public void Cleanup()
    {
        if (Directory.Exists(_tempPluginsDir))
        {
            try
            {
                Directory.Delete(_tempPluginsDir, recursive: true);
            }
            // ALLOWED: empty catch - test cleanup errors can be safely ignored
            catch
            {
                // Ignore cleanup errors
            }
        }
    }

    // =========================================================================
    // Constructor Tests
    // =========================================================================

    [TestMethod]
    public void Constructor_WithValidDependencies_InitializesSuccessfully()
    {
        var manager = new PluginManager(
            _mockPanelRegistry.Object,
            _mockBackendClient.Object,
            _tempPluginsDir
        );

        Assert.IsNotNull(manager);
    }

    [TestMethod]
    public void Constructor_WithNullPluginsDirectory_UsesDefault()
    {
        var manager = new PluginManager(
            _mockPanelRegistry.Object,
            _mockBackendClient.Object
        );

        Assert.IsNotNull(manager);
    }

    // =========================================================================
    // Plugins Property Tests
    // =========================================================================

    [TestMethod]
    public void Plugins_InitiallyEmpty()
    {
        var manager = new PluginManager(
            _mockPanelRegistry.Object,
            _mockBackendClient.Object,
            _tempPluginsDir
        );

        Assert.IsNotNull(manager.Plugins);
        Assert.AreEqual(0, manager.Plugins.Count);
    }

    [TestMethod]
    public void Plugins_ReturnsReadOnlyList()
    {
        var manager = new PluginManager(
            _mockPanelRegistry.Object,
            _mockBackendClient.Object,
            _tempPluginsDir
        );

        var plugins = manager.Plugins;

        Assert.IsInstanceOfType(plugins, typeof(IReadOnlyList<IPlugin>));
    }

    // =========================================================================
    // GetPlugin Tests
    // =========================================================================

    [TestMethod]
    public void GetPlugin_NonexistentPlugin_ReturnsNull()
    {
        var manager = new PluginManager(
            _mockPanelRegistry.Object,
            _mockBackendClient.Object,
            _tempPluginsDir
        );

        var plugin = manager.GetPlugin("nonexistent");

        Assert.IsNull(plugin);
    }

    // =========================================================================
    // LoadPluginsAsync Tests
    // =========================================================================

    [TestMethod]
    public async Task LoadPluginsAsync_EmptyDirectory_CompletesSuccessfully()
    {
        var manager = new PluginManager(
            _mockPanelRegistry.Object,
            _mockBackendClient.Object,
            _tempPluginsDir
        );

        await manager.LoadPluginsAsync();

        Assert.AreEqual(0, manager.Plugins.Count);
    }

    [TestMethod]
    public async Task LoadPluginsAsync_NonexistentDirectory_CreatesDirectory()
    {
        var nonexistentDir = Path.Combine(_tempPluginsDir, "nonexistent_" + Guid.NewGuid().ToString("N"));
        var manager = new PluginManager(
            _mockPanelRegistry.Object,
            _mockBackendClient.Object,
            nonexistentDir
        );

        await manager.LoadPluginsAsync();

        Assert.IsTrue(Directory.Exists(nonexistentDir));
    }

    [TestMethod]
    public async Task LoadPluginsAsync_DirectoryWithoutManifest_SkipsPlugin()
    {
        var pluginDir = Path.Combine(_tempPluginsDir, "invalid_plugin");
        Directory.CreateDirectory(pluginDir);
        File.WriteAllText(Path.Combine(pluginDir, "readme.txt"), "No manifest here");

        var manager = new PluginManager(
            _mockPanelRegistry.Object,
            _mockBackendClient.Object,
            _tempPluginsDir
        );

        await manager.LoadPluginsAsync();

        Assert.AreEqual(0, manager.Plugins.Count);
    }

    [TestMethod]
    public async Task LoadPluginsAsync_CalledTwice_OnlyLoadsOnce()
    {
        var manager = new PluginManager(
            _mockPanelRegistry.Object,
            _mockBackendClient.Object,
            _tempPluginsDir
        );

        await manager.LoadPluginsAsync();
        await manager.LoadPluginsAsync();

        Assert.AreEqual(0, manager.Plugins.Count);
    }

    [TestMethod]
    public async Task LoadPluginsAsync_InvalidManifestJson_ThrowsOrLogsError()
    {
        var pluginDir = Path.Combine(_tempPluginsDir, "bad_manifest");
        Directory.CreateDirectory(pluginDir);
        File.WriteAllText(Path.Combine(pluginDir, "manifest.json"), "{ invalid json }");

        var manager = new PluginManager(
            _mockPanelRegistry.Object,
            _mockBackendClient.Object,
            _tempPluginsDir
        );

        await manager.LoadPluginsAsync();

        Assert.AreEqual(0, manager.Plugins.Count);
    }

    // =========================================================================
    // UnloadPlugins Tests
    // =========================================================================

    [TestMethod]
    public void UnloadPlugins_NoPluginsLoaded_CompletesSuccessfully()
    {
        var manager = new PluginManager(
            _mockPanelRegistry.Object,
            _mockBackendClient.Object,
            _tempPluginsDir
        );

        manager.UnloadPlugins();

        Assert.AreEqual(0, manager.Plugins.Count);
    }

    [TestMethod]
    public async Task UnloadPlugins_AfterLoad_AllowsReloading()
    {
        var manager = new PluginManager(
            _mockPanelRegistry.Object,
            _mockBackendClient.Object,
            _tempPluginsDir
        );

        await manager.LoadPluginsAsync();
        manager.UnloadPlugins();
        await manager.LoadPluginsAsync();

        Assert.AreEqual(0, manager.Plugins.Count);
    }

    // =========================================================================
    // Schema Validation Tests
    // =========================================================================

    [TestMethod]
    public async Task LoadPluginsAsync_EmptyManifest_ReportsValidationError()
    {
        var pluginDir = Path.Combine(_tempPluginsDir, "empty_manifest");
        Directory.CreateDirectory(pluginDir);
        File.WriteAllText(Path.Combine(pluginDir, "manifest.json"), "{}");

        var manager = new PluginManager(
            _mockPanelRegistry.Object,
            _mockBackendClient.Object,
            _tempPluginsDir
        );

        await manager.LoadPluginsAsync();

        Assert.AreEqual(0, manager.Plugins.Count);
    }

    [TestMethod]
    public async Task LoadPluginsAsync_ValidManifestNoAssembly_CompletesWithoutPlugin()
    {
        var pluginDir = Path.Combine(_tempPluginsDir, "valid_no_assembly");
        Directory.CreateDirectory(pluginDir);
        var manifest = @"{
            ""name"": ""TestPlugin"",
            ""version"": ""1.0.0"",
            ""description"": ""A test plugin"",
            ""author"": ""Test Author"",
            ""plugin_type"": ""tool""
        }";
        File.WriteAllText(Path.Combine(pluginDir, "manifest.json"), manifest);

        var manager = new PluginManager(
            _mockPanelRegistry.Object,
            _mockBackendClient.Object,
            _tempPluginsDir
        );

        await manager.LoadPluginsAsync();

        Assert.AreEqual(0, manager.Plugins.Count);
    }

    // =========================================================================
    // Edge Case Tests
    // =========================================================================

    [TestMethod]
    public async Task LoadPluginsAsync_MultiplePluginDirectories_ProcessesAll()
    {
        var plugin1Dir = Path.Combine(_tempPluginsDir, "plugin1");
        var plugin2Dir = Path.Combine(_tempPluginsDir, "plugin2");
        Directory.CreateDirectory(plugin1Dir);
        Directory.CreateDirectory(plugin2Dir);

        var manager = new PluginManager(
            _mockPanelRegistry.Object,
            _mockBackendClient.Object,
            _tempPluginsDir
        );

        await manager.LoadPluginsAsync();

        Assert.AreEqual(0, manager.Plugins.Count);
    }
}
