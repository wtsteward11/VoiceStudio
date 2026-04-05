using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using VoiceStudio.App.Services;
using VoiceStudio.Core.Panels;
using VoiceStudio.Core.Services;
using VoiceStudio.Core.State;
using VoiceStudio.Core.State.Commands;

namespace VoiceStudio.App.Tests.Services
{
    /// <summary>
    /// Unit tests for DragDropService.
    /// </summary>
    [TestClass]
    public class DragDropServiceTests
    {
        private DragDropService _service = null!;
        private AppStateStore _stateStore = null!;

        [TestInitialize]
        public void Setup()
        {
            _stateStore = new AppStateStore();
            _service = new DragDropService(stateStore: _stateStore);
        }

        [TestCleanup]
        public void Cleanup()
        {
            _service = null!;
            _stateStore = null!;
        }

        #region Drag Lifecycle Tests

        [TestMethod]
        public void StartDrag_SetsCurrentPayload()
        {
            // Arrange
            var payload = DragPayload.FromProfile("profiles-panel", "profile-123", "Test Voice");

            // Act
            _service.StartDrag(payload);

            // Assert
            Assert.IsTrue(_service.IsDragging);
            Assert.IsNotNull(_service.CurrentPayload);
            Assert.AreEqual("profiles-panel", _service.CurrentPayload!.SourcePanelId);
            Assert.AreEqual(DragPayloadType.Profile, _service.CurrentPayload.PayloadType);
        }

        [TestMethod]
        public void StartDrag_RaisesDragStartedEvent()
        {
            // Arrange
            var payload = DragPayload.FromAsset("library-panel", "asset-456", "Audio.wav");
            PanelDragEventArgs? eventArgs = null;
            _service.DragStarted += (s, e) => eventArgs = e;

            // Act
            _service.StartDrag(payload);

            // Assert
            Assert.IsNotNull(eventArgs);
            Assert.AreEqual(payload, eventArgs.Payload);
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentNullException))]
        public void StartDrag_NullPayload_ThrowsException()
        {
            _service.StartDrag(null!);
        }

        [TestMethod]
        public void CancelDrag_ClearsPayload()
        {
            // Arrange
            var payload = DragPayload.FromProfile("profiles-panel", "profile-123", "Test Voice");
            _service.StartDrag(payload);
            Assert.IsTrue(_service.IsDragging);

            // Act
            _service.CancelDrag();

            // Assert
            Assert.IsFalse(_service.IsDragging);
            Assert.IsNull(_service.CurrentPayload);
        }

        [TestMethod]
        public void CancelDrag_RaisesDragEndedEvent()
        {
            // Arrange
            var payload = DragPayload.FromProfile("profiles-panel", "profile-123", "Test Voice");
            _service.StartDrag(payload);
            PanelDragEventArgs? eventArgs = null;
            _service.DragEnded += (s, e) => eventArgs = e;

            // Act
            _service.CancelDrag();

            // Assert
            Assert.IsNotNull(eventArgs);
            Assert.AreEqual(payload, eventArgs.Payload);
        }

        [TestMethod]
        public void UpdateDragTarget_RaisesTargetChangedEvent()
        {
            // Arrange
            var payload = DragPayload.FromProfile("profiles-panel", "profile-123", "Test Voice");
            _service.StartDrag(payload);
            PanelDragEventArgs? eventArgs = null;
            _service.DragTargetChanged += (s, e) => eventArgs = e;

            // Act
            _service.UpdateDragTarget("synthesis-panel");

            // Assert
            Assert.IsNotNull(eventArgs);
            Assert.AreEqual("synthesis-panel", eventArgs.CurrentTargetPanelId);
        }

        [TestMethod]
        public void UpdateDragTarget_SameTarget_DoesNotRaiseEvent()
        {
            // Arrange
            var payload = DragPayload.FromProfile("profiles-panel", "profile-123", "Test Voice");
            _service.StartDrag(payload);
            _service.UpdateDragTarget("synthesis-panel");
            int eventCount = 0;
            _service.DragTargetChanged += (s, e) => eventCount++;

            // Act
            _service.UpdateDragTarget("synthesis-panel");

            // Assert
            Assert.AreEqual(0, eventCount);
        }

        #endregion

        #region Drop Target Registration Tests

        [TestMethod]
        public void RegisterDropTarget_AllowsCanDropChecks()
        {
            // Arrange
            var payload = DragPayload.FromProfile("profiles-panel", "profile-123", "Test Voice");
            _service.RegisterDropTarget("synthesis-panel", p => p.PayloadType == DragPayloadType.Profile);
            _service.StartDrag(payload);

            // Act & Assert
            Assert.IsTrue(_service.CanDrop("synthesis-panel"));
        }

        [TestMethod]
        public void CanDrop_UnregisteredTarget_ReturnsFalse()
        {
            // Arrange
            var payload = DragPayload.FromProfile("profiles-panel", "profile-123", "Test Voice");
            _service.StartDrag(payload);

            // Act & Assert
            Assert.IsFalse(_service.CanDrop("unregistered-panel"));
        }

        [TestMethod]
        public void CanDrop_NoDragActive_ReturnsFalse()
        {
            // Arrange
            _service.RegisterDropTarget("synthesis-panel", p => true);

            // Act & Assert
            Assert.IsFalse(_service.CanDrop("synthesis-panel"));
        }

        [TestMethod]
        public void UnregisterDropTarget_RemovesTarget()
        {
            // Arrange
            var payload = DragPayload.FromProfile("profiles-panel", "profile-123", "Test Voice");
            _service.RegisterDropTarget("synthesis-panel", p => true);
            _service.UnregisterDropTarget("synthesis-panel");
            _service.StartDrag(payload);

            // Act & Assert
            Assert.IsFalse(_service.CanDrop("synthesis-panel"));
        }

        [TestMethod]
        public void CanDrop_WithPayload_WorksWithoutActiveDrag()
        {
            // Arrange
            var payload = DragPayload.FromAsset("library", "asset-1", "Test.wav");
            _service.RegisterDropTarget("timeline", p => p.PayloadType == DragPayloadType.Asset);

            // Act & Assert - can check without starting drag
            Assert.IsTrue(_service.CanDrop("timeline", payload));
        }

        #endregion

        #region ExecuteDropAsync Tests

        [TestMethod]
        public async Task ExecuteDropAsync_NoActiveDrag_ReturnsFailure()
        {
            // Act
            var result = await _service.ExecuteDropAsync(
                "target-panel",
                async (p, ct) => new DropResult { Success = true, TargetPanelId = "target-panel" });

            // Assert
            Assert.IsFalse(result.Success);
            Assert.AreEqual("No active drag operation", result.ErrorMessage);
        }

        [TestMethod]
        public async Task ExecuteDropAsync_CallsHandler()
        {
            // Arrange
            var payload = DragPayload.FromProfile("profiles-panel", "profile-123", "Test Voice");
            _service.StartDrag(payload);
            DragPayload? receivedPayload = null;

            // Act
            var result = await _service.ExecuteDropAsync("synthesis-panel", async (p, ct) =>
            {
                receivedPayload = p;
                return new DropResult { Success = true, TargetPanelId = "synthesis-panel" };
            });

            // Assert
            Assert.IsTrue(result.Success);
            Assert.IsNotNull(receivedPayload);
            Assert.AreEqual("profile-123", receivedPayload.Items[0].Id);
        }

        [TestMethod]
        public async Task ExecuteDropAsync_ClearsDragState()
        {
            // Arrange
            var payload = DragPayload.FromProfile("profiles-panel", "profile-123", "Test Voice");
            _service.StartDrag(payload);

            // Act
            await _service.ExecuteDropAsync("synthesis-panel", async (p, ct) =>
                new DropResult { Success = true, TargetPanelId = "synthesis-panel" });

            // Assert
            Assert.IsFalse(_service.IsDragging);
            Assert.IsNull(_service.CurrentPayload);
        }

        /// <summary>GAP-032: Voice-profile library assets use Asset payload + AssetType metadata (Synthesis drop gate).</summary>
        [TestMethod]
        public void CanDrop_VoiceProfileLibraryAsset_MatchesSynthesisStylePredicate()
        {
            static bool CanAcceptSynthesis(DragPayload payload)
            {
                if (payload.PayloadType == DragPayloadType.Profile ||
                    payload.PayloadType == DragPayloadType.ReferenceAudio)
                    return true;
                if (payload.PayloadType != DragPayloadType.Asset || payload.Items.Count == 0)
                    return false;
                if (payload.Items[0].Metadata == null ||
                    !payload.Items[0].Metadata.TryGetValue("AssetType", out var raw))
                    return false;
                var t = (raw?.ToString() ?? string.Empty).ToLowerInvariant();
                var voiceTypes = new[] { "voice", "voice_profile", "profile", "clone", "xtts", "rvc" };
                return voiceTypes.Contains(t);
            }

            _service.RegisterDropTarget(PanelIds.VoiceSynthesis, CanAcceptSynthesis);
            var payload = new DragPayload
            {
                PayloadType = DragPayloadType.Asset,
                SourcePanelId = PanelIds.Library,
                Items = new[]
                {
                    new DragItem
                    {
                        Id = "prof-9",
                        DisplayName = "My voice",
                        Metadata = new Dictionary<string, object> { ["AssetType"] = "voice_profile" },
                    },
                },
            };
            _service.StartDrag(payload);
            Assert.IsTrue(_service.CanDrop(PanelIds.VoiceSynthesis));
        }

        [TestMethod]
        public async Task ExecuteDropAsync_RaisesDropExecutedEvent()
        {
            // Arrange
            var payload = DragPayload.FromProfile("profiles-panel", "profile-123", "Test Voice");
            _service.StartDrag(payload);
            DropEventArgs? eventArgs = null;
            _service.DropExecuted += (s, e) => eventArgs = e;

            // Act
            await _service.ExecuteDropAsync("synthesis-panel", async (p, ct) =>
                new DropResult { Success = true, TargetPanelId = "synthesis-panel", Action = "assigned" });

            // Assert
            Assert.IsNotNull(eventArgs);
            Assert.IsTrue(eventArgs.Result.Success);
            Assert.AreEqual("assigned", eventArgs.Result.Action);
        }

        #endregion

        #region Command Pattern Integration Tests

        [TestMethod]
        public void ExecuteProfileDrop_UpdatesStateViaCommand()
        {
            // Arrange
            var payload = DragPayload.FromProfile("profiles-panel", "profile-123", "Test Voice");
            _service.StartDrag(payload);

            // Act
            var result = _service.ExecuteProfileDrop("synthesis-panel", "profile-123", "Test Voice");

            // Assert
            Assert.IsTrue(result.Success);
            Assert.IsFalse(_service.IsDragging);
        }

        [TestMethod]
        public void ExecuteAssetDrop_UpdatesStateViaCommand()
        {
            // Arrange
            var payload = DragPayload.FromAsset("library-panel", "asset-456", "Audio.wav", "audio");
            _service.StartDrag(payload);

            // Act
            var result = _service.ExecuteAssetDrop("timeline-panel", "asset-456", "Audio.wav", "audio");

            // Assert
            Assert.IsTrue(result.Success);
            Assert.IsTrue(_stateStore.CanUndo); // Command was executed via store
        }

        [TestMethod]
        public void ExecuteDropCommand_NoActivePayload_ReturnsFailure()
        {
            // Arrange
            var command = DropItemCommand.ForAssetDrop(AppState.Empty, "target", "asset-1", "Test", "audio");

            // Act
            var result = _service.ExecuteDropCommand("target", command);

            // Assert
            Assert.IsFalse(result.Success);
            Assert.AreEqual("No active drag operation", result.ErrorMessage);
        }

        #endregion

        #region Thread Safety Tests

        [TestMethod]
        public void ConcurrentDragOperations_AreThreadSafe()
        {
            // Arrange
            var tasks = new List<Task>();

            // Act - concurrent drag/cancel operations
            for (int i = 0; i < 50; i++)
            {
                int capturedI = i;
                tasks.Add(Task.Run(() =>
                {
                    var payload = DragPayload.FromProfile("panel", $"profile-{capturedI}", $"Profile {capturedI}");
                    _service.StartDrag(payload);
                    _service.UpdateDragTarget($"target-{capturedI}");
                    _service.CancelDrag();
                }));
            }

            Task.WaitAll(tasks.ToArray());

            // Assert - should complete without exceptions
            Assert.IsFalse(_service.IsDragging);
        }

        #endregion
    }

    /// <summary>
    /// Unit tests for DragPayload factory methods.
    /// </summary>
    [TestClass]
    public class DragPayloadTests
    {
        [TestMethod]
        public void FromAsset_CreatesCorrectPayload()
        {
            // Act
            var payload = DragPayload.FromAsset("library", "asset-123", "MyAudio.wav", "audio");

            // Assert
            Assert.AreEqual(DragPayloadType.Asset, payload.PayloadType);
            Assert.AreEqual("library", payload.SourcePanelId);
            Assert.AreEqual(1, payload.Items.Count);
            Assert.AreEqual("asset-123", payload.Items[0].Id);
            Assert.AreEqual("MyAudio.wav", payload.Items[0].DisplayName);
            Assert.AreEqual("audio", payload.Items[0].Metadata?["AssetType"]);
        }

        [TestMethod]
        public void FromProfile_CreatesCorrectPayload()
        {
            // Act
            var payload = DragPayload.FromProfile("profiles", "profile-456", "My Voice", "en-US");

            // Assert
            Assert.AreEqual(DragPayloadType.Profile, payload.PayloadType);
            Assert.AreEqual("profiles", payload.SourcePanelId);
            Assert.AreEqual("profile-456", payload.Items[0].Id);
            Assert.AreEqual("My Voice", payload.Items[0].DisplayName);
            Assert.AreEqual("en-US", payload.Items[0].Metadata?["Language"]);
        }

        [TestMethod]
        public void FromExternalFiles_CreatesCorrectPayload()
        {
            // Arrange
            var files = new[] { @"C:\test\file1.wav", @"C:\test\file2.mp3" };

            // Act
            var payload = DragPayload.FromExternalFiles(files);

            // Assert
            Assert.AreEqual(DragPayloadType.ExternalFile, payload.PayloadType);
            Assert.AreEqual("external", payload.SourcePanelId);
            Assert.AreEqual(2, payload.Items.Count);
            Assert.AreEqual("file1.wav", payload.Items[0].DisplayName);
            Assert.AreEqual(2, payload.FilePaths?.Count);
        }

        [TestMethod]
        public void FromAsset_NullAssetType_HasNoMetadata()
        {
            // Act
            var payload = DragPayload.FromAsset("library", "asset-123", "MyAudio.wav", null);

            // Assert
            Assert.IsNull(payload.Items[0].Metadata);
        }
    }

    /// <summary>
    /// Unit tests for workspace configuration round-trip serialization.
    /// </summary>
    [TestClass]
    public class WorkspaceRoundTripTests
    {
        [TestMethod]
        public void WorkspaceDefinition_JsonRoundTrip_PreservesAllProperties()
        {
            // Arrange
            var original = new WorkspaceDefinition
            {
                Id = "workspace-123",
                Name = "My Custom Layout",
                Description = "A test workspace",
                IconGlyph = "\uE8A5",
                IsPreset = false,
                IsActive = true,
                KeyboardShortcut = "Ctrl+1",
                Panels = new List<PanelPlacement>
                {
                    new PanelPlacement
                    {
                        PanelId = "library",
                        Region = PanelRegion.Left,
                        Order = 0,
                        IsVisible = true,
                        RelativeWidth = 0.25
                    },
                    new PanelPlacement
                    {
                        PanelId = "synthesis",
                        Region = PanelRegion.Center,
                        Order = 0,
                        IsCollapsed = false
                    }
                }
            };

            // Act
            var json = JsonSerializer.Serialize(original);
            var restored = JsonSerializer.Deserialize<WorkspaceDefinition>(json);

            // Assert
            Assert.IsNotNull(restored);
            Assert.AreEqual(original.Id, restored.Id);
            Assert.AreEqual(original.Name, restored.Name);
            Assert.AreEqual(original.Description, restored.Description);
            Assert.AreEqual(original.IconGlyph, restored.IconGlyph);
            Assert.AreEqual(original.IsPreset, restored.IsPreset);
            Assert.AreEqual(original.KeyboardShortcut, restored.KeyboardShortcut);
            Assert.AreEqual(original.Panels.Count, restored.Panels.Count);
            Assert.AreEqual("library", restored.Panels[0].PanelId);
            Assert.AreEqual(PanelRegion.Left, restored.Panels[0].Region);
            Assert.AreEqual(0.25, restored.Panels[0].RelativeWidth);
        }

        [TestMethod]
        public void WorkspaceConfiguration_JsonRoundTrip_PreservesAllWorkspaces()
        {
            // Arrange
            var original = new WorkspaceConfiguration
            {
                ActiveWorkspaceId = "workspace-1",
                Version = 2,
                Workspaces = new List<WorkspaceDefinition>
                {
                    new WorkspaceDefinition
                    {
                        Id = "workspace-1",
                        Name = "Default",
                        IsPreset = true,
                        Panels = new List<PanelPlacement>
                        {
                            new PanelPlacement { PanelId = "library", Region = PanelRegion.Left }
                        }
                    },
                    new WorkspaceDefinition
                    {
                        Id = "workspace-2",
                        Name = "Custom",
                        IsPreset = false,
                        Panels = new List<PanelPlacement>
                        {
                            new PanelPlacement { PanelId = "synthesis", Region = PanelRegion.Center }
                        }
                    }
                }
            };

            // Act
            var json = JsonSerializer.Serialize(original);
            var restored = JsonSerializer.Deserialize<WorkspaceConfiguration>(json);

            // Assert
            Assert.IsNotNull(restored);
            Assert.AreEqual(original.ActiveWorkspaceId, restored.ActiveWorkspaceId);
            Assert.AreEqual(original.Version, restored.Version);
            Assert.AreEqual(2, restored.Workspaces.Count);
            Assert.AreEqual("Default", restored.Workspaces[0].Name);
            Assert.IsTrue(restored.Workspaces[0].IsPreset);
            Assert.AreEqual("Custom", restored.Workspaces[1].Name);
            Assert.IsFalse(restored.Workspaces[1].IsPreset);
        }

        [TestMethod]
        public void PanelPlacement_JsonRoundTrip_PreservesOptionalProperties()
        {
            // Arrange
            var original = new PanelPlacement
            {
                PanelId = "timeline",
                Region = PanelRegion.Bottom,
                Order = 2,
                IsCollapsed = true,
                IsVisible = false,
                RelativeWidth = 0.5,
                RelativeHeight = 0.3,
                PanelState = new Dictionary<string, object>
                {
                    ["scrollPosition"] = 100,
                    ["zoomLevel"] = 1.5
                }
            };

            // Act
            var json = JsonSerializer.Serialize(original);
            var restored = JsonSerializer.Deserialize<PanelPlacement>(json);

            // Assert
            Assert.IsNotNull(restored);
            Assert.AreEqual(original.PanelId, restored.PanelId);
            Assert.AreEqual(original.Region, restored.Region);
            Assert.AreEqual(original.Order, restored.Order);
            Assert.AreEqual(original.IsCollapsed, restored.IsCollapsed);
            Assert.AreEqual(original.IsVisible, restored.IsVisible);
            Assert.AreEqual(original.RelativeWidth, restored.RelativeWidth);
            Assert.AreEqual(original.RelativeHeight, restored.RelativeHeight);
            Assert.IsNotNull(restored.PanelState);
            Assert.AreEqual(2, restored.PanelState.Count);
        }

        [TestMethod]
        public void WorkspaceDefinition_WithModified_UpdatesTimestamp()
        {
            // Arrange
            var original = new WorkspaceDefinition
            {
                Id = "test",
                Name = "Test",
                Panels = Array.Empty<PanelPlacement>(),
                ModifiedAt = DateTimeOffset.UtcNow.AddDays(-1)
            };
            var originalModified = original.ModifiedAt;

            // Act
            var modified = original.WithModified();

            // Assert
            Assert.IsTrue(modified.ModifiedAt > originalModified);
            Assert.AreEqual(original.Id, modified.Id);
            Assert.AreEqual(original.Name, modified.Name);
        }

        [TestMethod]
        public void DropResult_JsonRoundTrip_PreservesData()
        {
            // Arrange
            var original = new DropResult
            {
                Success = true,
                TargetPanelId = "synthesis-panel",
                Action = "assigned",
                AffectedItemIds = new[] { "item-1", "item-2" }
            };

            // Act
            var json = JsonSerializer.Serialize(original);
            var restored = JsonSerializer.Deserialize<DropResult>(json);

            // Assert
            Assert.IsNotNull(restored);
            Assert.AreEqual(original.Success, restored.Success);
            Assert.AreEqual(original.TargetPanelId, restored.TargetPanelId);
            Assert.AreEqual(original.Action, restored.Action);
            Assert.AreEqual(2, restored.AffectedItemIds?.Count);
        }

        [TestMethod]
        public void DragPayload_JsonRoundTrip_PreservesMetadata()
        {
            // Arrange
            var original = DragPayload.FromProfile("profiles", "profile-1", "Test Voice", "en-US");

            // Act
            var json = JsonSerializer.Serialize(original);
            var restored = JsonSerializer.Deserialize<DragPayload>(json);

            // Assert
            Assert.IsNotNull(restored);
            Assert.AreEqual(original.PayloadType, restored.PayloadType);
            Assert.AreEqual(original.SourcePanelId, restored.SourcePanelId);
            Assert.AreEqual(original.Items.Count, restored.Items.Count);
            Assert.AreEqual("profile-1", restored.Items[0].Id);
        }
    }

    /// <summary>
    /// Unit tests for DropItemCommand.
    /// </summary>
    [TestClass]
    public class DropItemCommandTests
    {
        [TestMethod]
        public void ForProfileDrop_CreatesCorrectCommand()
        {
            // Arrange
            var state = AppState.Empty;

            // Act
            var command = DropItemCommand.ForProfileDrop(state, "synthesis", "profile-123", "Test Voice");

            // Assert - Name is dynamic based on PayloadType and TargetPanelId
            Assert.AreEqual("Drop Profile to synthesis", command.Name);
            Assert.AreEqual(DropAction.Select, command.Action);
            Assert.AreEqual(1, command.Items.Count);
            Assert.AreEqual("profile-123", command.Items[0].Id);
        }

        [TestMethod]
        public void ForAssetDrop_CreatesCorrectCommand()
        {
            // Arrange
            var state = AppState.Empty;

            // Act
            var command = DropItemCommand.ForAssetDrop(state, "timeline", "asset-456", "Audio.wav", "audio");

            // Assert - Asset drops use Insert action, not Select
            Assert.AreEqual(DropAction.Insert, command.Action);
            Assert.AreEqual("asset-456", command.Items[0].Id);
        }

        [TestMethod]
        public void Execute_UpdatesAppState()
        {
            // Arrange
            var state = AppState.Empty;
            var command = DropItemCommand.ForAssetDrop(state, "timeline", "asset-123", "Test.wav", "audio");

            // Act
            var newState = command.Execute(state);

            // Assert
            Assert.AreEqual("asset-123", newState.Assets.SelectedAssetId);
            Assert.AreEqual("Test.wav", newState.Assets.SelectedAssetName);
        }

        [TestMethod]
        public void Undo_RestoresPreviousState()
        {
            // Arrange
            var state = AppState.Empty with
            {
                Assets = AssetState.Empty with
                {
                    SelectedAssetId = "original-asset",
                    SelectedAssetName = "Original"
                }
            };
            var command = DropItemCommand.ForAssetDrop(state, "timeline", "new-asset", "New.wav", "audio");

            // Act
            var afterExecute = command.Execute(state);
            var afterUndo = command.Undo(afterExecute);

            // Assert
            Assert.AreEqual("original-asset", afterUndo.Assets.SelectedAssetId);
        }
    }
}
