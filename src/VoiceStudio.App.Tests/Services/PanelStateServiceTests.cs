using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
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
        private string? _tempWorkspaceRoot;

        [TestInitialize]
        public void Setup()
        {
            _tempWorkspaceRoot = Path.Combine(Path.GetTempPath(), "VoiceStudio_Test_" + Guid.NewGuid().ToString("N")[..8]);
            Directory.CreateDirectory(_tempWorkspaceRoot);
        }

        [TestCleanup]
        public void Cleanup()
        {
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

            var service = new PanelStateService(mockSettings, _tempWorkspaceRoot!);

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

            var service = new PanelStateService(mockSettings, _tempWorkspaceRoot!);
            await Task.Delay(150);

            var wsDir = Path.Combine(_tempWorkspaceRoot!, "VoiceStudio", "WorkspaceProfiles");
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

            var service = new PanelStateService(mockSettings, _tempWorkspaceRoot!);
            await Task.Delay(150);

            var wsDir = Path.Combine(_tempWorkspaceRoot!, "VoiceStudio", "WorkspaceProfiles");
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

            var service = new PanelStateService(mockSettings, _tempWorkspaceRoot!);
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

        /// <summary>
        /// Proves MigratePanelState moves panel state from one region to another (moved, not copied).
        /// When a panel is opened in a different region via Tool Catalog, state follows the panel.
        /// </summary>
        [TestMethod]
        public async Task MigratePanelState_MovesStateAcrossRegions()
        {
            var mockSettings = new MockSettingsService();
            mockSettings.Settings = mockSettings.GetDefaultSettings();
            mockSettings.Settings.WorkspaceLayout = null;

            var service = new PanelStateService(mockSettings, _tempWorkspaceRoot!);
            await Task.Delay(150);

            const string testPanelId = "TestPanel";
            var savedState = new PanelState
            {
                PanelId = testPanelId,
                ScrollPosition = 100,
                SelectedItemId = "item-1"
            };

            service.SavePanelState(PanelRegion.Left, testPanelId, savedState);

            service.SaveRegionState(PanelRegion.Right, "Placeholder", new List<string> { "Placeholder" });

            service.MigratePanelState(testPanelId, PanelRegion.Left, PanelRegion.Right);

            var stateInRight = service.GetPanelState(PanelRegion.Right, testPanelId);
            Assert.IsNotNull(stateInRight, "State should exist in Right after migration");
            Assert.AreEqual(100, stateInRight.ScrollPosition);
            Assert.AreEqual("item-1", stateInRight.SelectedItemId);

            var stateInLeft = service.GetPanelState(PanelRegion.Left, testPanelId);
            Assert.IsNull(stateInLeft, "State should be removed from Left (moved, not copied)");
        }

        /// <summary>
        /// Proves that WorkspaceLayout JSON without PinnedPanelIds deserializes cleanly with empty set.
        /// Backward compatibility: layouts saved before PinnedPanelIds was added must not fail.
        /// </summary>
        [TestMethod]
        public void DeserializeLayout_MissingPinnedPanelIds_DefaultsToEmpty()
        {
            var json = """{"profileName":"Test","version":"1.0","regions":[],"modifiedAt":"2025-01-01T00:00:00Z"}""";
            var layout = JsonSerializer.Deserialize<WorkspaceLayout>(json, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
            Assert.IsNotNull(layout);
            Assert.IsNotNull(layout.PinnedPanelIds);
            Assert.AreEqual(0, layout.PinnedPanelIds.Count);
        }

        /// <summary>
        /// Proves PinnedPanelIds round-trips correctly through JSON serialization.
        /// </summary>
        [TestMethod]
        public void PinnedPanelIds_RoundTrips_ThroughJson()
        {
            var layout = new WorkspaceLayout
            {
                ProfileName = "Test",
                Version = "1.0",
                PinnedPanelIds = new HashSet<string> { "PanelA", "PanelB" }
            };
            var options = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
            var json = JsonSerializer.Serialize(layout, options);
            var restored = JsonSerializer.Deserialize<WorkspaceLayout>(json, options);
            Assert.IsNotNull(restored);
            Assert.AreEqual(2, restored.PinnedPanelIds.Count);
            Assert.IsTrue(restored.PinnedPanelIds.Contains("PanelA"));
            Assert.IsTrue(restored.PinnedPanelIds.Contains("PanelB"));
        }

        /// <summary>
        /// Proves CreateWorkspaceProfileAsync rejects invalid profile names (path traversal, reserved, illegal chars).
        /// </summary>
        [DataTestMethod]
        [DataRow("")]
        [DataRow("   ")]
        [DataRow("CON")]
        [DataRow("con")]
        [DataRow("LPT1")]
        [DataRow("a/b")]
        [DataRow(@"a\b")]
        [DataRow("../evil")]
        [DataRow("a<b")]
        [DataRow("a\x01b")]
        [DataRow(".hidden")]
        [DataRow("trail.")]
        [DataRow("NUL.txt")]
        public async Task CreateWorkspace_RejectsInvalidNames(string invalidName)
        {
            var mockSettings = new MockSettingsService();
            mockSettings.Settings = mockSettings.GetDefaultSettings();
            mockSettings.Settings.WorkspaceLayout = null;

            var service = new PanelStateService(mockSettings, _tempWorkspaceRoot!);
            await Task.Delay(150);

            await Assert.ThrowsExceptionAsync<ArgumentException>(async () =>
                await service.CreateWorkspaceProfileAsync(invalidName));
        }

        /// <summary>
        /// Proves CreateWorkspaceProfileAsync rejects names exceeding 64 characters.
        /// </summary>
        [TestMethod]
        public async Task CreateWorkspace_RejectsNameTooLong()
        {
            var mockSettings = new MockSettingsService();
            mockSettings.Settings = mockSettings.GetDefaultSettings();
            mockSettings.Settings.WorkspaceLayout = null;

            var service = new PanelStateService(mockSettings, _tempWorkspaceRoot!);
            await Task.Delay(150);

            var longName = new string('x', 65);
            await Assert.ThrowsExceptionAsync<ArgumentException>(async () =>
                await service.CreateWorkspaceProfileAsync(longName));
        }

        /// <summary>
        /// Proves CreateWorkspaceProfileAsync accepts valid profile names and creates files on disk.
        /// </summary>
        [DataTestMethod]
        [DataRow("My Workspace")]
        [DataRow("recording_v2")]
        [DataRow("A")]
        public async Task CreateWorkspace_AcceptsValidNames(string validName)
        {
            var mockSettings = new MockSettingsService();
            mockSettings.Settings = mockSettings.GetDefaultSettings();
            mockSettings.Settings.WorkspaceLayout = null;

            var service = new PanelStateService(mockSettings, _tempWorkspaceRoot!);
            await Task.Delay(150);

            var profile = await service.CreateWorkspaceProfileAsync(validName);
            Assert.IsNotNull(profile);
            Assert.AreEqual(validName, profile.Name);

            var wsDir = Path.Combine(_tempWorkspaceRoot!, "VoiceStudio", "WorkspaceProfiles");
            var filePath = Path.Combine(wsDir, $"{validName}.json");
            Assert.IsTrue(File.Exists(filePath), $"Profile file should exist at {filePath}");
        }

        /// <summary>
        /// Proves renaming to an existing profile name fails deterministically without data loss.
        /// </summary>
        [TestMethod]
        public async Task RenameWorkspace_ToExistingName_FailsDeterministically()
        {
            var mockSettings = new MockSettingsService();
            mockSettings.Settings = mockSettings.GetDefaultSettings();
            mockSettings.Settings.WorkspaceLayout = null;

            var service = new PanelStateService(mockSettings, _tempWorkspaceRoot!);
            await Task.Delay(150);

            var wsDir = Path.Combine(_tempWorkspaceRoot!, "VoiceStudio", "WorkspaceProfiles");
            var pathA = Path.Combine(wsDir, "profile_a.json");
            var pathB = Path.Combine(wsDir, "profile_b.json");

            await service.CreateWorkspaceProfileAsync("profile_a");
            await service.CreateWorkspaceProfileAsync("profile_b");
            Assert.IsTrue(File.Exists(pathA));
            Assert.IsTrue(File.Exists(pathB));

            var result = await service.RenameWorkspaceProfileAsync("profile_a", "profile_b");
            Assert.IsFalse(result);
            Assert.IsTrue(File.Exists(pathA), "profile_a file should still exist");
            Assert.IsTrue(File.Exists(pathB), "profile_b file should still exist");
        }

        /// <summary>
        /// Proves renaming to the same name returns true as a no-op.
        /// </summary>
        [TestMethod]
        public async Task RenameWorkspace_SameName_ReturnsTrueNoOp()
        {
            var mockSettings = new MockSettingsService();
            mockSettings.Settings = mockSettings.GetDefaultSettings();
            mockSettings.Settings.WorkspaceLayout = null;

            var service = new PanelStateService(mockSettings, _tempWorkspaceRoot!);
            await Task.Delay(150);

            var wsDir = Path.Combine(_tempWorkspaceRoot!, "VoiceStudio", "WorkspaceProfiles");
            var filePath = Path.Combine(wsDir, "noop_test.json");

            await service.CreateWorkspaceProfileAsync("noop_test");
            var contentBefore = await File.ReadAllTextAsync(filePath);

            var result = await service.RenameWorkspaceProfileAsync("noop_test", "noop_test");
            Assert.IsTrue(result);
            Assert.IsTrue(File.Exists(filePath));
            var contentAfter = await File.ReadAllTextAsync(filePath);
            Assert.AreEqual(contentBefore, contentAfter, "Content should be unchanged");
        }

        /// <summary>
        /// Proves rename updates the profile list and removes the old name.
        /// </summary>
        [TestMethod]
        public async Task RenameWorkspace_UpdatesProfileListAndRemovesOldName()
        {
            var mockSettings = new MockSettingsService();
            mockSettings.Settings = mockSettings.GetDefaultSettings();
            mockSettings.Settings.WorkspaceLayout = null;

            var service = new PanelStateService(mockSettings, _tempWorkspaceRoot!);
            await Task.Delay(150);

            await service.CreateWorkspaceProfileAsync("old_name");
            var renamed = await service.RenameWorkspaceProfileAsync("old_name", "new_name");
            Assert.IsTrue(renamed);

            var profiles = await service.ListWorkspaceProfilesAsync();
            var names = profiles.Select(p => p.Name).ToList();
            Assert.IsTrue(names.Contains("new_name"), "List should contain new_name");
            Assert.IsFalse(names.Contains("old_name"), "List should not contain old_name");
        }

        /// <summary>
        /// Proves pinned panel IDs survive export-then-import round-trip.
        /// </summary>
        [TestMethod]
        public async Task PinnedPanelIds_SurviveExportImport()
        {
            var mockSettings = new MockSettingsService();
            mockSettings.Settings = mockSettings.GetDefaultSettings();
            mockSettings.Settings.WorkspaceLayout = null;

            var service = new PanelStateService(mockSettings, _tempWorkspaceRoot!);
            await Task.Delay(150);

            service.TogglePinnedPanel("PanelX");
            service.TogglePinnedPanel("PanelY");
            await service.CreateWorkspaceProfileAsync("pin_test");

            var json = await service.ExportWorkspaceAsync("pin_test");
            var imported = await service.ImportWorkspaceAsync(json);
            Assert.IsNotNull(imported);
            Assert.IsTrue(imported.Name.Contains("Imported"), "Imported profile should have (Imported) suffix");

            var switched = await service.SwitchWorkspaceProfileAsync(imported.Name);
            Assert.IsTrue(switched);
            Assert.IsTrue(service.IsPanelPinned("PanelX"), "PanelX should remain pinned after import");
            Assert.IsTrue(service.IsPanelPinned("PanelY"), "PanelY should remain pinned after import");
        }

        /// <summary>
        /// Proves pinned panel IDs are workspace-scoped; unpinning in one workspace does not affect another.
        /// </summary>
        [TestMethod]
        public async Task PinnedPanelIds_AreWorkspaceScoped()
        {
            var mockSettings = new MockSettingsService();
            mockSettings.Settings = mockSettings.GetDefaultSettings();
            mockSettings.Settings.WorkspaceLayout = null;

            var service = new PanelStateService(mockSettings, _tempWorkspaceRoot!);
            await Task.Delay(150);

            service.TogglePinnedPanel("PanelA");
            await service.CreateWorkspaceProfileAsync("studio_copy");

            var switched = await service.SwitchWorkspaceProfileAsync("studio_copy");
            Assert.IsTrue(switched);
            Assert.IsTrue(service.IsPanelPinned("PanelA"), "PanelA should be pinned in studio_copy (inherited from layout)");

            service.TogglePinnedPanel("PanelA");
            Assert.IsFalse(service.IsPanelPinned("PanelA"), "PanelA should now be unpinned in studio_copy");

            switched = await service.SwitchWorkspaceProfileAsync("studio");
            Assert.IsTrue(switched);
            Assert.IsTrue(service.IsPanelPinned("PanelA"), "PanelA should still be pinned in studio (no bleed)");
        }
    }
}
