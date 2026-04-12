using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using VoiceStudio.App.Services;
using VoiceStudio.Core.Panels;

namespace VoiceStudio.App.Tests.Services
{
    /// <summary>
    /// Workflow-level tests for ProjectWorkflowCoordinator open-project seam.
    /// Per MAINWINDOW_DECOMPOSITION_PLAN.md — proves gating, happy path, failure path.
    /// </summary>
    [TestClass]
    [TestCategory("Services")]
    public class ProjectWorkflowCoordinatorTests
    {
        private Mock<IStartupStateService> _mockStartup = null!;
        private Mock<IShellNavigationCoordinator> _mockShellNav = null!;
        private Mock<IProjectCreateHandler> _mockCreateHandler = null!;
        private Mock<IProjectOpenHandler> _mockOpenHandler = null!;
        private Mock<IProjectSaveHandler> _mockSaveHandler = null!;
        private RecentProjectsService? _recents;
        private string? _lastSetActiveNavButton;
        private RecordingToastForTests? _recordingToast;

        [TestInitialize]
        public void Setup()
        {
            _mockStartup = new Mock<IStartupStateService>();
            _mockShellNav = new Mock<IShellNavigationCoordinator>();
            _mockCreateHandler = new Mock<IProjectCreateHandler>();
            _mockOpenHandler = new Mock<IProjectOpenHandler>();
            _mockOpenHandler.Setup(x => x.OpenProjectByPathAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);
            _mockSaveHandler = new Mock<IProjectSaveHandler>();
            _recents = new RecentProjectsService();
            _lastSetActiveNavButton = null;
            _recordingToast = null;
        }

        private ProjectWorkflowCoordinator CreateCoordinator(
            bool withRecents = true,
            IToastNotificationService? toast = null,
            ILogger<ProjectWorkflowCoordinator>? logger = null)
        {
            return new ProjectWorkflowCoordinator(
                _mockStartup.Object,
                _mockShellNav.Object,
                _mockCreateHandler.Object,
                _mockOpenHandler.Object,
                _mockSaveHandler.Object,
                s => _lastSetActiveNavButton = s,
                withRecents ? _recents : null,
                toast,
                logger);
        }

        private RecordingToastForTests CreateRecordingToast()
        {
            _recordingToast = new RecordingToastForTests();
            return _recordingToast;
        }

        [TestMethod]
        public async Task CreateNewProjectAsync_WhenReady_CallsCreateHandler()
        {
            _mockStartup.Setup(x => x.IsReady).Returns(true);

            var coordinator = CreateCoordinator(withRecents: false);
            await coordinator.CreateNewProjectAsync();

            _mockCreateHandler.Verify(x => x.CreateNewAsync(It.IsAny<CancellationToken>()), Times.Once);
        }

        [TestMethod]
        public async Task CreateNewProjectAsync_WhenNotReady_ShowsInfoAndReturns()
        {
            _mockStartup.Setup(x => x.IsReady).Returns(false);

            var recordingToast = CreateRecordingToast();
            var coordinator = CreateCoordinator(withRecents: false, toast: recordingToast);
            await coordinator.CreateNewProjectAsync();

            _mockCreateHandler.Verify(x => x.CreateNewAsync(It.IsAny<CancellationToken>()), Times.Never);
            Assert.IsTrue(recordingToast.LastInfoCall.HasValue, "Not-ready path should show info toast");
            Assert.IsTrue(recordingToast.LastInfoCall!.Value.Message.Contains("Starting VoiceStudio", StringComparison.Ordinal),
                "Info message should indicate startup in progress");
        }

        [TestMethod]
        public async Task CreateNewProjectAsync_WhenHandlerThrows_SurfacesError()
        {
            _mockStartup.Setup(x => x.IsReady).Returns(true);
            _mockCreateHandler.Setup(x => x.CreateNewAsync(It.IsAny<CancellationToken>()))
                .ThrowsAsync(new System.Exception("Create failed"));

            var recordingToast = CreateRecordingToast();
            var coordinator = CreateCoordinator(withRecents: false, toast: recordingToast);
            await coordinator.CreateNewProjectAsync();

            Assert.IsTrue(recordingToast.LastErrorCall.HasValue, "Create failure should surface via toast");
            Assert.AreEqual("Create Project Failed", recordingToast.LastErrorCall!.Value.Message);
            Assert.IsTrue(recordingToast.LastErrorCall!.Value.Title!.Contains("Create failed", StringComparison.Ordinal),
                "Toast message should contain exception text");
        }

        [TestMethod]
        public async Task OpenProjectAsync_WhenReady_CallsOpenHandlerAndShellNav()
        {
            _mockStartup.Setup(x => x.IsReady).Returns(true);
            _mockShellNav.Setup(x => x.OpenPanelByIdAsync("Timeline", PanelRegion.Center))
                .ReturnsAsync(true);

            var coordinator = CreateCoordinator(withRecents: false);
            await coordinator.OpenProjectAsync();

            _mockShellNav.Verify(x => x.OpenPanelByIdAsync("Timeline", PanelRegion.Center), Times.Once);
            Assert.AreEqual("NavStudio", _lastSetActiveNavButton);
            _mockOpenHandler.Verify(x => x.OpenProjectPickerAsync(It.IsAny<CancellationToken>()), Times.Once);
        }

        [TestMethod]
        public async Task OpenProjectAsync_WhenNotReady_DoesNotCallOpenHandler()
        {
            _mockStartup.Setup(x => x.IsReady).Returns(false);

            var recordingToast = CreateRecordingToast();
            var coordinator = CreateCoordinator(withRecents: false, toast: recordingToast);
            await coordinator.OpenProjectAsync();

            _mockShellNav.Verify(x => x.OpenPanelByIdAsync(It.IsAny<string>(), It.IsAny<PanelRegion?>()), Times.Never);
            _mockOpenHandler.Verify(x => x.OpenProjectPickerAsync(It.IsAny<CancellationToken>()), Times.Never);
            Assert.IsTrue(recordingToast.LastInfoCall.HasValue,
                "Not-ready path should show info toast");
            Assert.IsTrue(recordingToast.LastInfoCall!.Value.Message
                .Contains("Starting VoiceStudio", StringComparison.Ordinal),
                "Info message should indicate startup in progress");
        }

        [TestMethod]
        public async Task OpenRecentProjectAsync_WhenNotReady_DoesNotCallOpenHandler()
        {
            _mockStartup.Setup(x => x.IsReady).Returns(false);

            var recordingToast = CreateRecordingToast();
            var coordinator = CreateCoordinator(toast: recordingToast);
            await coordinator.OpenRecentProjectAsync("proj-1", "Test Project");

            _mockOpenHandler.Verify(x => x.OpenProjectByIdAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
            Assert.IsTrue(recordingToast.LastInfoCall.HasValue,
                "Not-ready path should show info toast");
            Assert.IsTrue(recordingToast.LastInfoCall!.Value.Message
                .Contains("Starting VoiceStudio", StringComparison.Ordinal),
                "Info message should indicate startup in progress");
        }

        [TestMethod]
        public async Task OpenRecentProjectAsync_HandlerThrows_RemovesFromRecentsAndSurfacesError()
        {
            _mockStartup.Setup(x => x.IsReady).Returns(true);
            await _recents!.AddRecentProjectAsync("proj-1", "Test Project");
            _mockOpenHandler.Setup(x => x.OpenProjectByIdAsync("proj-1", "Test Project", It.IsAny<CancellationToken>()))
                .ThrowsAsync(new System.Exception("Project not found"));

            var coordinator = CreateCoordinator();
            await coordinator.OpenRecentProjectAsync("proj-1", "Test Project");

            Assert.IsFalse(_recents.RecentProjects.Any(p => p.Path == "proj-1"), "Should remove from recents on failure");
        }

        [TestMethod]
        public async Task OpenRecentProjectAsync_WhenReady_Success_AddsToRecents()
        {
            _mockStartup.Setup(x => x.IsReady).Returns(true);
            _mockOpenHandler.Setup(x => x.OpenProjectByIdAsync("proj-1", "Test Project", It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);

            var coordinator = CreateCoordinator();
            await coordinator.OpenRecentProjectAsync("proj-1", "Test Project");

            Assert.IsTrue(_recents!.RecentProjects.Any(p => p.Path == "proj-1" && p.Name == "Test Project"), "Should add to recents on success");
        }

        [TestMethod]
        public async Task OpenProjectAsync_WhenHandlerThrows_SurfacesError_AfterIntentionalStudioNavigation()
        {
            _mockStartup.Setup(x => x.IsReady).Returns(true);
            _mockShellNav.Setup(x => x.OpenPanelByIdAsync("Timeline", PanelRegion.Center))
                .ReturnsAsync(true);
            _mockOpenHandler.Setup(x => x.OpenProjectPickerAsync(It.IsAny<CancellationToken>()))
                .ThrowsAsync(new System.Exception("Load failed"));

            var recordingToast = CreateRecordingToast();
            var coordinator = CreateCoordinator(withRecents: false, toast: recordingToast);
            await coordinator.OpenProjectAsync();

            Assert.IsTrue(recordingToast.LastErrorCall.HasValue, "Open failure should surface via toast");
            Assert.AreEqual("Open Project Failed", recordingToast.LastErrorCall!.Value.Message);
            Assert.IsTrue(recordingToast.LastErrorCall!.Value.Title!.Contains("Load failed", StringComparison.Ordinal),
                "Toast message should contain exception text");
            _mockShellNav.Verify(x => x.OpenPanelByIdAsync("Timeline", PanelRegion.Center), Times.Once);
            Assert.AreEqual("NavStudio", _lastSetActiveNavButton);
            _mockOpenHandler.Verify(x => x.OpenProjectPickerAsync(It.IsAny<CancellationToken>()), Times.Once);
        }

        [TestMethod]
        public async Task SaveProjectAsync_WhenReady_CallsSaveHandler()
        {
            _mockStartup.Setup(x => x.IsReady).Returns(true);

            var coordinator = CreateCoordinator(withRecents: false);
            await coordinator.SaveProjectAsync();

            _mockSaveHandler.Verify(x => x.SaveProjectAsync(It.IsAny<CancellationToken>()), Times.Once);
        }

        [TestMethod]
        public async Task SaveProjectAsync_WhenNotReady_ShowsToastAndReturns()
        {
            _mockStartup.Setup(x => x.IsReady).Returns(false);

            var recordingToast = CreateRecordingToast();
            var coordinator = CreateCoordinator(withRecents: false, toast: recordingToast);
            await coordinator.SaveProjectAsync();

            _mockSaveHandler.Verify(x => x.SaveProjectAsync(It.IsAny<CancellationToken>()), Times.Never);
            Assert.IsTrue(recordingToast.LastInfoCall.HasValue,
                "Not-ready path should show info toast");
            Assert.IsTrue(recordingToast.LastInfoCall!.Value.Message
                .Contains("Starting VoiceStudio", StringComparison.Ordinal),
                "Info message should indicate startup in progress");
        }

        [TestMethod]
        public async Task SaveProjectAsync_WhenHandlerThrows_SurfacesError()
        {
            _mockStartup.Setup(x => x.IsReady).Returns(true);
            _mockSaveHandler.Setup(x => x.SaveProjectAsync(It.IsAny<CancellationToken>()))
                .ThrowsAsync(new System.Exception("Save failed"));

            var recordingToast = CreateRecordingToast();
            var coordinator = CreateCoordinator(withRecents: false, toast: recordingToast);
            await coordinator.SaveProjectAsync();

            Assert.IsTrue(recordingToast.LastErrorCall.HasValue, "Save failure should surface via toast");
            Assert.AreEqual("Save Project Failed", recordingToast.LastErrorCall!.Value.Message);
            Assert.IsTrue(recordingToast.LastErrorCall!.Value.Title!.Contains("Save failed", StringComparison.Ordinal),
                "Toast message should contain exception text");
        }

        [TestMethod]
        public async Task CreateNewProject_WhenHandlerThrows_LogsWarningWithOperationName()
        {
            _mockStartup.Setup(x => x.IsReady).Returns(true);
            _mockCreateHandler.Setup(x => x.CreateNewAsync(It.IsAny<CancellationToken>()))
                .ThrowsAsync(new System.Exception("Create failed"));

            var mockLogger = new Mock<ILogger<ProjectWorkflowCoordinator>>();
            var coordinator = CreateCoordinator(withRecents: false, logger: mockLogger.Object);
            await coordinator.CreateNewProjectAsync();

            mockLogger.Verify(
                x => x.Log(LogLevel.Warning,
                    It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("CreateNewProject")),
                    It.IsAny<Exception>(),
                    It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
                Times.Once);
        }

        [TestMethod]
        public async Task OpenProject_WhenHandlerThrows_LogsWarningWithOperationName()
        {
            _mockStartup.Setup(x => x.IsReady).Returns(true);
            _mockShellNav.Setup(x => x.OpenPanelByIdAsync("Timeline", PanelRegion.Center))
                .ReturnsAsync(true);
            _mockOpenHandler.Setup(x => x.OpenProjectPickerAsync(It.IsAny<CancellationToken>()))
                .ThrowsAsync(new System.Exception("Load failed"));

            var mockLogger = new Mock<ILogger<ProjectWorkflowCoordinator>>();
            var coordinator = CreateCoordinator(withRecents: false, logger: mockLogger.Object);
            await coordinator.OpenProjectAsync();

            mockLogger.Verify(
                x => x.Log(LogLevel.Warning,
                    It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("OpenProject")),
                    It.IsAny<Exception>(),
                    It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
                Times.Once);
        }

        [TestMethod]
        public async Task SaveProject_WhenHandlerThrows_LogsWarningWithOperationName()
        {
            _mockStartup.Setup(x => x.IsReady).Returns(true);
            _mockSaveHandler.Setup(x => x.SaveProjectAsync(It.IsAny<CancellationToken>()))
                .ThrowsAsync(new System.Exception("Save failed"));

            var mockLogger = new Mock<ILogger<ProjectWorkflowCoordinator>>();
            var coordinator = CreateCoordinator(withRecents: false, logger: mockLogger.Object);
            await coordinator.SaveProjectAsync();

            mockLogger.Verify(
                x => x.Log(LogLevel.Warning,
                    It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("SaveProject")),
                    It.IsAny<Exception>(),
                    It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
                Times.Once);
        }

        [TestMethod]
        public async Task OpenRecentProject_WhenHandlerThrows_LogsWarningWithOperationName()
        {
            _mockStartup.Setup(x => x.IsReady).Returns(true);
            _mockOpenHandler.Setup(x => x.OpenProjectByIdAsync("proj-1", "Test Project", It.IsAny<CancellationToken>()))
                .ThrowsAsync(new System.Exception("Project not found"));

            var mockLogger = new Mock<ILogger<ProjectWorkflowCoordinator>>();
            var coordinator = CreateCoordinator(logger: mockLogger.Object);
            await coordinator.OpenRecentProjectAsync("proj-1", "Test Project");

            mockLogger.Verify(
                x => x.Log(LogLevel.Warning,
                    It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("OpenRecentProject")),
                    It.IsAny<Exception>(),
                    It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
                Times.Once);
        }

        [TestMethod]
        public async Task TryAutosaveProjectAsync_WhenReady_CallsSaveHandler()
        {
            _mockStartup.Setup(x => x.IsReady).Returns(true);

            var coordinator = CreateCoordinator(withRecents: false);
            await coordinator.TryAutosaveProjectAsync();

            _mockSaveHandler.Verify(x => x.SaveProjectAsync(It.IsAny<CancellationToken>()), Times.Once);
        }

        [TestMethod]
        public async Task TryAutosaveProjectAsync_WhenNotReady_DoesNotCallSaveHandler()
        {
            _mockStartup.Setup(x => x.IsReady).Returns(false);

            var coordinator = CreateCoordinator(withRecents: false);
            await coordinator.TryAutosaveProjectAsync();

            _mockSaveHandler.Verify(x => x.SaveProjectAsync(It.IsAny<CancellationToken>()), Times.Never);
        }

        [TestMethod]
        public async Task TryAutosaveProjectAsync_WhenHandlerThrows_LogsWithoutToast()
        {
            _mockStartup.Setup(x => x.IsReady).Returns(true);
            _mockSaveHandler.Setup(x => x.SaveProjectAsync(It.IsAny<CancellationToken>()))
                .ThrowsAsync(new Exception("autosave boom"));

            var recordingToast = CreateRecordingToast();
            var mockLogger = new Mock<ILogger<ProjectWorkflowCoordinator>>();
            var coordinator = CreateCoordinator(withRecents: false, toast: recordingToast, logger: mockLogger.Object);
            await coordinator.TryAutosaveProjectAsync();

            Assert.IsFalse(recordingToast.LastErrorCall.HasValue, "Autosave must not surface error toast");
            mockLogger.Verify(
                x => x.Log(LogLevel.Warning,
                    It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Autosave", StringComparison.Ordinal)),
                    It.IsAny<Exception>(),
                    It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
                Times.Once);
        }
    }

    /// <summary>
    /// Test-only implementation that records ShowToast/ShowInfo calls without UI.
    /// </summary>
    internal sealed class RecordingToastForTests : IToastNotificationService
    {
        public (ToastType Type, string Message, string? Title)? LastErrorCall { get; private set; }
        public (string Message, string? Title)? LastInfoCall { get; private set; }

        public void ShowToast(ToastType type, string message, string? title = null)
        {
            if (type == ToastType.Error)
                LastErrorCall = (type, message, title);
        }

        public void ShowInfo(string message, string? title = null)
        {
            LastInfoCall = (message, title);
        }

        public void ShowSuccess(string message, string? title = null)
        {
        }

        public void ShowWarning(string message, string? title = null)
        {
        }

        public void ShowError(string message, string? title = null, Action? viewDetailsAction = null)
        {
            LastErrorCall = (ToastType.Error, message, title);
        }
    }
}
