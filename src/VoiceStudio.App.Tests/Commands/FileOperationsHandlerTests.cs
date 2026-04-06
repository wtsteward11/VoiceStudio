using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using VoiceStudio.App.Commands;
using VoiceStudio.App.Core.Commands;
using VoiceStudio.App.Services;
using VoiceStudio.App.UseCases;
using VoiceStudio.App.Tests.Fixtures;
using VoiceStudio.Core.Models;
using VoiceStudio.Core.Services;

namespace VoiceStudio.App.Tests.Commands
{
    /// <summary>
    /// Unit tests for FileOperationsHandler.
    /// </summary>
    [TestClass]
    [TestCategory("Commands")]
    public class FileOperationsHandlerTests : CommandHandlerTestBase
    {
        private FileOperationsHandler _handler = null!;

        [TestInitialize]
        public override void SetupBase()
        {
            base.SetupBase();
            _handler = new FileOperationsHandler(
                Registry,
                MockProjectRepository.Object,
                MockDialogService.Object,
                null);
        }

        #region Registration Tests

        [TestMethod]
        public void Constructor_RegistersAllFileCommands()
        {
            AssertCommandsRegistered(
                "file.new",
                "file.open",
                "file.save",
                "file.saveAs",
                "file.import",
                "file.export",
                "file.close"
            );
        }

        [TestMethod]
        public void Commands_HaveCorrectCategory()
        {
            AssertCommandMetadata("file.new", "New Project", "File");
            AssertCommandMetadata("file.open", "Open Project", "File");
            AssertCommandMetadata("file.save", "Save Project", "File");
        }

        #endregion

        #region New Project Tests

        [TestMethod]
        public async Task NewProject_WithValidName_CreatesProject()
        {
            // Arrange
            SetupInputDialog("My New Project");

            // Act
            await Registry.ExecuteAsync("file.new");

            // Assert
            Assert.IsNotNull(_handler.CurrentProject);
            Assert.AreEqual("My New Project", _handler.CurrentProject.Name);
        }

        /// <summary>GAP-045 lifecycle: new project identity must not carry subtitle restore metadata.</summary>
        [TestMethod]
        public async Task NewProject_LastSubtitleTranscriptionId_IsNull()
        {
            SetupInputDialog("Lifecycle New");
            await Registry.ExecuteAsync("file.new");
            Assert.IsNotNull(_handler.CurrentProject);
            Assert.IsNull(_handler.CurrentProject.LastSubtitleTranscriptionId);
        }

        [TestMethod]
        public async Task NewProject_UserCancels_DoesNotCreateProject()
        {
            // Arrange
            SetupInputDialog(null);

            // Act
            await Registry.ExecuteAsync("file.new");

            // Assert
            Assert.IsNull(_handler.CurrentProject);
        }

        [TestMethod]
        public async Task NewProject_EmptyName_DoesNotCreateProject()
        {
            // Arrange
            SetupInputDialog("");

            // Act
            await Registry.ExecuteAsync("file.new");

            // Assert
            Assert.IsNull(_handler.CurrentProject);
        }

        #endregion

        #region Open Project Tests

        [TestMethod]
        public async Task OpenProject_WithValidPath_LoadsProject()
        {
            // Arrange
            var testProject = new Project { Id = "test-id", Name = "Test Project" };
            SetupFileDialog("test-path");
            MockProjectRepository.Setup(r => r.OpenAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(testProject);

            // Act
            await Registry.ExecuteAsync("file.open");

            // Assert
            Assert.IsNotNull(_handler.CurrentProject);
            Assert.AreEqual("Test Project", _handler.CurrentProject.Name);
        }

        [TestMethod]
        public async Task OpenProject_UserCancels_DoesNotLoadProject()
        {
            // Arrange
            SetupFileDialog(null);

            // Act
            await Registry.ExecuteAsync("file.open");

            // Assert
            Assert.IsNull(_handler.CurrentProject);
        }

        #endregion

        #region Save Project Tests

        [TestMethod]
        public async Task SaveProject_WithCurrentProject_SavesSuccessfully()
        {
            // Arrange - First create a project
            SetupInputDialog("Project to Save");
            await Registry.ExecuteAsync("file.new");
            Assert.IsTrue(_handler.HasUnsavedChanges);

            // Act
            await Registry.ExecuteAsync("file.save");

            // Assert
            Assert.IsFalse(_handler.HasUnsavedChanges);
            MockProjectRepository.Verify(r => r.SaveAsync(
                It.Is<Project>(p => p.Name == "Project to Save"),
                It.IsAny<CancellationToken>()), Times.Once);
        }

        [TestMethod]
        public void SaveProject_NoCurrentProject_CannotExecute()
        {
            // Assert
            AssertCannotExecute("file.save");
        }

        #endregion

        #region Save As Tests

        [TestMethod]
        public async Task SaveAs_WithNewName_CreatesNewProject()
        {
            // Arrange - First create a project
            SetupInputDialog("Original Project");
            await Registry.ExecuteAsync("file.new");

            // Now set up for save as
            SetupInputDialog("Renamed Project");

            // Act
            await Registry.ExecuteAsync("file.saveAs");

            // Assert
            Assert.AreEqual("Renamed Project", _handler.CurrentProject?.Name);
        }

        /// <summary>GAP-045 lifecycle: Save As must not copy LastSubtitleTranscriptionId onto the new project id.</summary>
        [TestMethod]
        public async Task SaveAs_AfterOpenWithStoredSubtitle_DoesNotInheritLastSubtitleTranscriptionId()
        {
            var createdAt = DateTime.UtcNow.ToString("o");
            var source = new Project
            {
                Id = "src-proj-id",
                Name = "Source",
                CreatedAt = createdAt,
                UpdatedAt = createdAt,
                LastSubtitleTranscriptionId = "tr-should-not-copy",
                Tracks = new List<AudioTrack>(),
                VoiceProfileIds = new List<string>(),
            };
            MockProjectRepository.Setup(r => r.OpenAsync("path-open", It.IsAny<CancellationToken>()))
                .ReturnsAsync(source);
            SetupFileDialog("path-open");
            await Registry.ExecuteAsync("file.open");
            Assert.AreEqual("tr-should-not-copy", _handler.CurrentProject?.LastSubtitleTranscriptionId);

            Project? saved = null;
            MockProjectRepository.Setup(r => r.SaveAsync(It.IsAny<Project>(), It.IsAny<CancellationToken>()))
                .Callback<Project, CancellationToken>((p, _) => saved = p)
                .ReturnsAsync((Project p, CancellationToken _) => p);

            SetupInputDialog("Forked Name");
            await Registry.ExecuteAsync("file.saveAs");

            Assert.IsNotNull(saved);
            Assert.IsNull(saved.LastSubtitleTranscriptionId);
            Assert.AreEqual("Forked Name", saved.Name);
            Assert.AreNotEqual("src-proj-id", saved.Id);
        }

        #endregion

        #region Close Project Tests

        [TestMethod]
        public async Task CloseProject_ClosesCurrentProject()
        {
            // Arrange - First create a project
            SetupInputDialog("Project to Close");
            await Registry.ExecuteAsync("file.new");
            Assert.IsNotNull(_handler.CurrentProject);

            // Don't prompt to save
            SetupConfirmationDialog(false);

            // Act
            await Registry.ExecuteAsync("file.close");

            // Assert
            Assert.IsNull(_handler.CurrentProject);
        }

        #endregion

        #region Dirty State Tests

        [TestMethod]
        public void MarkDirty_SetsHasUnsavedChanges()
        {
            // Act
            _handler.MarkDirty();

            // Assert
            Assert.IsTrue(_handler.HasUnsavedChanges);
        }

        #endregion

        #region Export authority (GAP-029)

        [TestMethod]
        public async Task ExportAudio_CallsTimelineExport_NotDirectAudioExport()
        {
            var reg = new UnifiedCommandRegistry(MockShortcutService.Object);
            var mockBackend = new Mock<IBackendClient>();
            var mockTimeline = new Mock<ITimelineUseCase>();
            var mockCtx = new Mock<IContextManager>();
            mockCtx.Setup(c => c.ActiveEffectChainId).Returns((string?)null);

            mockBackend.Setup(b => b.ListProjectAudioAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<ProjectAudioFile>
                {
                    new() { AudioId = "aid-fallback", Filename = "a.wav" },
                });

            mockTimeline.Setup(t => t.ExportAsync(It.IsAny<string>(), It.IsAny<ExportOptions>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(@"C:\export\mix.wav");

            var handler = new FileOperationsHandler(
                reg,
                MockProjectRepository.Object,
                MockDialogService.Object,
                mockBackend.Object,
                null,
                mockTimeline.Object,
                mockCtx.Object);

            SetupInputDialog("Export Proj");
            await reg.ExecuteAsync("file.new");
            SetupSaveFileDialog(@"C:\export\mix.wav");

            await handler.ExportAudioAsync();

            mockTimeline.Verify(
                t => t.ExportAsync(
                    @"C:\export\mix.wav",
                    It.Is<ExportOptions>(o =>
                        o.FallbackProjectAudioId == "aid-fallback"
                        && o.ProjectId == handler.CurrentProject!.Id
                        && !o.ApplyEffectsDuringExport
                        && o.LufsPreset == "podcast_stereo"),
                    It.IsAny<CancellationToken>()),
                Times.Once);

            mockBackend.Verify(
                b => b.ExportAudioAsync(
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.IsAny<int?>(),
                    It.IsAny<int?>(),
                    It.IsAny<int?>(),
                    It.IsAny<bool>(),
                    It.IsAny<CancellationToken>()),
                Times.Never);
        }

        /// <summary>GAP-031: Empty / invalid timeline export surfaces as non-fatal handler completion with user message path (toast).</summary>
        [TestMethod]
        public async Task ExportAudio_DoesNotRethrow_WhenTimelineExportThrowsInvalidOperation()
        {
            var reg = new UnifiedCommandRegistry(MockShortcutService.Object);
            var mockBackend = new Mock<IBackendClient>();
            var mockTimeline = new Mock<ITimelineUseCase>();
            var mockCtx = new Mock<IContextManager>();
            mockCtx.Setup(c => c.ActiveEffectChainId).Returns((string?)null);

            mockBackend.Setup(b => b.ListProjectAudioAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<ProjectAudioFile>
                {
                    new() { AudioId = "aid-fallback", Filename = "a.wav" },
                });

            mockTimeline.Setup(t => t.ExportAsync(It.IsAny<string>(), It.IsAny<ExportOptions>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new InvalidOperationException("Timeline has no audible audio to export."));

            var handler = new FileOperationsHandler(
                reg,
                MockProjectRepository.Object,
                MockDialogService.Object,
                mockBackend.Object,
                null,
                mockTimeline.Object,
                mockCtx.Object);

            SetupInputDialog("Export Proj");
            await reg.ExecuteAsync("file.new");
            SetupSaveFileDialog(@"C:\export\mix.wav");

            await handler.ExportAudioAsync();

            mockTimeline.Verify(
                t => t.ExportAsync(It.IsAny<string>(), It.IsAny<ExportOptions>(), It.IsAny<CancellationToken>()),
                Times.Once);
        }

        [TestMethod]
        public async Task ExportAudio_UsesSettingsDefaultLufsPreset_WhenNoPicker()
        {
            var reg = new UnifiedCommandRegistry(MockShortcutService.Object);
            var mockBackend = new Mock<IBackendClient>();
            var mockTimeline = new Mock<ITimelineUseCase>();
            var mockCtx = new Mock<IContextManager>();
            mockCtx.Setup(c => c.ActiveEffectChainId).Returns((string?)null);

            var st = TestDataGenerators.CreateDefaultSettings();
            st.General!.DefaultExportLufsPreset = "neutral";
            MockSettingsService.Setup(s => s.LoadSettingsAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(st);

            mockBackend.Setup(b => b.ListProjectAudioAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<ProjectAudioFile>
                {
                    new() { AudioId = "aid-fallback", Filename = "a.wav" },
                });

            mockTimeline.Setup(t => t.ExportAsync(It.IsAny<string>(), It.IsAny<ExportOptions>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(@"C:\export\mix.wav");

            var handler = new FileOperationsHandler(
                reg,
                MockProjectRepository.Object,
                MockDialogService.Object,
                mockBackend.Object,
                null,
                mockTimeline.Object,
                mockCtx.Object,
                MockSettingsService.Object,
                null);

            SetupInputDialog("Export Proj");
            await reg.ExecuteAsync("file.new");
            SetupSaveFileDialog(@"C:\export\mix.wav");

            await handler.ExportAudioAsync();

            mockTimeline.Verify(
                t => t.ExportAsync(
                    @"C:\export\mix.wav",
                    It.Is<ExportOptions>(o => o.LufsPreset == "neutral"),
                    It.IsAny<CancellationToken>()),
                Times.Once);
        }

        [TestMethod]
        public async Task ExportAudio_PresetPickerOverridesSettings()
        {
            var reg = new UnifiedCommandRegistry(MockShortcutService.Object);
            var mockBackend = new Mock<IBackendClient>();
            var mockTimeline = new Mock<ITimelineUseCase>();
            var mockCtx = new Mock<IContextManager>();
            mockCtx.Setup(c => c.ActiveEffectChainId).Returns((string?)null);

            var st = TestDataGenerators.CreateDefaultSettings();
            st.General!.DefaultExportLufsPreset = "podcast_stereo";
            MockSettingsService.Setup(s => s.LoadSettingsAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(st);

            var mockPresetUi = new Mock<IExportLufsPresetUi>();
            mockPresetUi
                .Setup(u => u.PickPresetAsync("podcast_stereo", It.IsAny<CancellationToken>()))
                .ReturnsAsync("streaming");

            mockBackend.Setup(b => b.ListProjectAudioAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<ProjectAudioFile>
                {
                    new() { AudioId = "aid-fallback", Filename = "a.wav" },
                });

            mockTimeline.Setup(t => t.ExportAsync(It.IsAny<string>(), It.IsAny<ExportOptions>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(@"C:\export\mix.wav");

            var handler = new FileOperationsHandler(
                reg,
                MockProjectRepository.Object,
                MockDialogService.Object,
                mockBackend.Object,
                null,
                mockTimeline.Object,
                mockCtx.Object,
                MockSettingsService.Object,
                mockPresetUi.Object);

            SetupInputDialog("Export Proj");
            await reg.ExecuteAsync("file.new");
            SetupSaveFileDialog(@"C:\export\mix.wav");

            await handler.ExportAudioAsync();

            mockTimeline.Verify(
                t => t.ExportAsync(
                    @"C:\export\mix.wav",
                    It.Is<ExportOptions>(o => o.LufsPreset == "streaming"),
                    It.IsAny<CancellationToken>()),
                Times.Once);
        }

        [TestMethod]
        public async Task ExportAudio_PresetPickerCancel_SkipsTimelineExport()
        {
            var reg = new UnifiedCommandRegistry(MockShortcutService.Object);
            var mockBackend = new Mock<IBackendClient>();
            var mockTimeline = new Mock<ITimelineUseCase>();
            var mockCtx = new Mock<IContextManager>();
            mockCtx.Setup(c => c.ActiveEffectChainId).Returns((string?)null);

            var st = TestDataGenerators.CreateDefaultSettings();
            MockSettingsService.Setup(s => s.LoadSettingsAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(st);

            var mockPresetUi = new Mock<IExportLufsPresetUi>();
            mockPresetUi
                .Setup(u => u.PickPresetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((string?)null);

            mockBackend.Setup(b => b.ListProjectAudioAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<ProjectAudioFile>
                {
                    new() { AudioId = "aid-fallback", Filename = "a.wav" },
                });

            var handler = new FileOperationsHandler(
                reg,
                MockProjectRepository.Object,
                MockDialogService.Object,
                mockBackend.Object,
                null,
                mockTimeline.Object,
                mockCtx.Object,
                MockSettingsService.Object,
                mockPresetUi.Object);

            SetupInputDialog("Export Proj");
            await reg.ExecuteAsync("file.new");
            SetupSaveFileDialog(@"C:\export\mix.wav");

            await handler.ExportAudioAsync();

            mockTimeline.Verify(
                t => t.ExportAsync(It.IsAny<string>(), It.IsAny<ExportOptions>(), It.IsAny<CancellationToken>()),
                Times.Never);
        }

        #endregion
    }
}
