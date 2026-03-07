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
    }
}
