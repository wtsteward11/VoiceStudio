using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using VoiceStudio.App.Tests.Fixtures;
using VoiceStudio.App.Services;
using VoiceStudio.Core.Models;
using VoiceStudio.Core.Panels;

namespace VoiceStudio.App.Tests.Services
{
    /// <summary>
    /// Unit tests for PanelStateService, including workspace persistence round-trip.
    /// </summary>
    [TestClass]
    public class PanelStateServiceTests
    {
        private string? _savedLocalAppData;
        private string? _tempWorkspaceRoot;

        [TestInitialize]
        public void Setup()
        {
            _savedLocalAppData = Environment.GetEnvironmentVariable("LOCALAPPDATA");
            _tempWorkspaceRoot = Path.Combine(Path.GetTempPath(), "VoiceStudio_Test_" + Guid.NewGuid().ToString("N")[..8]);
            Directory.CreateDirectory(_tempWorkspaceRoot);
            Environment.SetEnvironmentVariable("LOCALAPPDATA", _tempWorkspaceRoot);
        }

        [TestCleanup]
        public void Cleanup()
        {
            if (_savedLocalAppData != null)
                Environment.SetEnvironmentVariable("LOCALAPPDATA", _savedLocalAppData);
            if (_tempWorkspaceRoot != null && Directory.Exists(_tempWorkspaceRoot))
            {
                try { Directory.Delete(_tempWorkspaceRoot, recursive: true); } catch { /* best effort */ }
            }
        }

        /// <summary>
        /// Proves workspace persistence round-trips correctly: save state, switch workspace,
        /// switch back, assert original state is restored.
        /// </summary>
        [TestMethod]
        public async Task WorkspacePersistence_RoundTrip_RestoresOriginalState()
        {
            var mockSettings = new MockSettingsService();
            mockSettings.Settings = mockSettings.GetDefaultSettings();
            mockSettings.Settings.WorkspaceLayout = null;

            var service = new PanelStateService(mockSettings);

            // Allow constructor's LoadCurrentWorkspaceAsync to complete
            await Task.Delay(150);

            const string testPanelId = "Profiles";
            var originalOpenedPanels = new List<string> { testPanelId, "Timeline" };

            // 1. Save region state for current workspace (studio)
            service.SaveRegionState(PanelRegion.Left, testPanelId, originalOpenedPanels);

            // 2. Create a named profile from current layout so it persists to disk
            var profile = await service.CreateWorkspaceProfileAsync("test_roundtrip_ws");
            Assert.IsNotNull(profile);

            // 3. Switch to another workspace
            var switched = await service.SwitchWorkspaceProfileAsync("recording");
            Assert.IsTrue(switched);

            // 4. Save different state in recording workspace
            service.SaveRegionState(PanelRegion.Left, "Timeline", new List<string> { "Timeline", "EffectsMixer" });

            // 5. Switch back to original workspace
            switched = await service.SwitchWorkspaceProfileAsync("test_roundtrip_ws");
            Assert.IsTrue(switched);

            // 6. Assert original state is restored
            var regionState = service.GetRegionState(PanelRegion.Left);
            Assert.IsNotNull(regionState);
            Assert.AreEqual(testPanelId, regionState.ActivePanelId);
            CollectionAssert.AreEquivalent(originalOpenedPanels, regionState.OpenedPanels);
        }

        /// <summary>
        /// Proves that switching workspace saves the current profile to its disk file before loading the new one.
        /// </summary>
        [TestMethod]
        public async Task SwitchWorkspace_SavesCurrentProfileToDisk()
        {
            var mockSettings = new MockSettingsService();
            mockSettings.Settings = mockSettings.GetDefaultSettings();
            mockSettings.Settings.WorkspaceLayout = null;

            var service = new PanelStateService(mockSettings);
            await Task.Delay(150);

            var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var wsDir = Path.Combine(appData, "VoiceStudio", "WorkspaceProfiles");
            var filePath = Path.Combine(wsDir, "isolation_test_ws.json");

            service.SaveRegionState(PanelRegion.Left, "Profiles", new List<string> { "Profiles", "Timeline" });
            var profile = await service.CreateWorkspaceProfileAsync("isolation_test_ws");
            Assert.IsNotNull(profile);
            Assert.IsTrue(File.Exists(filePath), $"Profile file should exist after create at {filePath}");

            service.SaveRegionState(PanelRegion.Left, "Timeline", new List<string> { "Timeline", "EffectsMixer" });

            var switched = await service.SwitchWorkspaceProfileAsync("recording");
            Assert.IsTrue(switched);

            var json = await File.ReadAllTextAsync(filePath);
            Assert.IsTrue(json.Contains("Timeline"), "Profile file should contain modified state (Timeline)");
            Assert.IsTrue(json.Contains("EffectsMixer"), "Profile file should contain modified state (EffectsMixer)");
        }

        /// <summary>
        /// Proves that rename moves the profile file and updates the name.
        /// </summary>
        [TestMethod]
        public async Task RenameWorkspace_MovesProfileFile()
        {
            var mockSettings = new MockSettingsService();
            mockSettings.Settings = mockSettings.GetDefaultSettings();
            mockSettings.Settings.WorkspaceLayout = null;

            var service = new PanelStateService(mockSettings);
            await Task.Delay(150);

            var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var wsDir = Path.Combine(appData, "VoiceStudio", "WorkspaceProfiles");
            var oldPath = Path.Combine(wsDir, "rename_source.json");
            var newPath = Path.Combine(wsDir, "rename_target.json");

            var profile = await service.CreateWorkspaceProfileAsync("rename_source");
            Assert.IsNotNull(profile);
            Assert.IsTrue(File.Exists(oldPath), $"Source file should exist at {oldPath}");

            var renamed = await service.RenameWorkspaceProfileAsync("rename_source", "rename_target");
            Assert.IsTrue(renamed);
            Assert.IsFalse(File.Exists(oldPath), "Old file should be deleted");
            Assert.IsTrue(File.Exists(newPath), "New file should exist");
        }

        /// <summary>
        /// Proves that reset restores a profile to its embedded template layout.
        /// </summary>
        [TestMethod]
        public async Task ResetWorkspace_RestoresEmbeddedLayout()
        {
            var mockSettings = new MockSettingsService();
            mockSettings.Settings = mockSettings.GetDefaultSettings();
            mockSettings.Settings.WorkspaceLayout = null;

            var service = new PanelStateService(mockSettings);
            await Task.Delay(150);

            service.SaveRegionState(PanelRegion.Left, "CustomPanel", new List<string> { "CustomPanel" });
            await service.CreateWorkspaceProfileAsync("recording");

            var reset = await service.ResetWorkspaceProfileAsync("recording");
            Assert.IsTrue(reset);

            service.SaveRegionState(PanelRegion.Left, "x", new List<string> { "x" });
            await service.SwitchWorkspaceProfileAsync("recording");

            var regionState = service.GetRegionState(PanelRegion.Left);
            Assert.IsNotNull(regionState);
            Assert.AreEqual("Recording", regionState.ActivePanelId, "Reset should restore embedded layout (Recording in Left)");
        }
    }
}
