using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using System;
using System.Threading;
using System.Threading.Tasks;
using VoiceStudio.App.Services;
using VoiceStudio.App.Tests.Fixtures;
using VoiceStudio.App.ViewModels;
using VoiceStudio.Core.Panels;
using VoiceStudio.Core.Services;

namespace VoiceStudio.App.Tests.ViewModels
{
    /// <summary>
    /// Unit tests for UpdateViewModel.
    /// Tests initialization, check for updates, download/install flow, and version comparison.
    /// </summary>
    [TestClass]
    public class UpdateViewModelTests
    {
        private Mock<IUpdateService> _mockUpdateService = null!;
        private MockViewModelContext _mockContext = null!;

        [TestInitialize]
        public void Setup()
        {
            _mockUpdateService = new Mock<IUpdateService>();
            _mockUpdateService.Setup(x => x.CurrentVersion).Returns(new Version(1, 0, 0));
            _mockUpdateService.Setup(x => x.UpdateDownloadPath).Returns(string.Empty);

            _mockContext = new MockViewModelContext();
        }

        [TestCleanup]
        public void Cleanup()
        {
            // No dispatcher cleanup needed with MockViewModelContext
        }

        private UpdateViewModel CreateViewModel()
        {
            return new UpdateViewModel(_mockContext, _mockUpdateService.Object);
        }

        #region Initialization Tests

        [TestMethod]
        public void Constructor_WithValidDependencies_CreatesInstance()
        {
            var viewModel = CreateViewModel();
            Assert.IsNotNull(viewModel);
            Assert.AreEqual("update", viewModel.PanelId);
            Assert.IsNotNull(viewModel.DisplayName);
            Assert.IsTrue(viewModel.DisplayName.Length > 0);
            Assert.AreEqual(PanelRegion.Floating, viewModel.Region);
        }

        [TestMethod]
        public void Constructor_InitializesCommands()
        {
            var viewModel = CreateViewModel();
            Assert.IsNotNull(viewModel.CheckForUpdatesCommand);
            Assert.IsNotNull(viewModel.DownloadUpdateCommand);
            Assert.IsNotNull(viewModel.InstallUpdateCommand);
            Assert.IsNotNull(viewModel.DismissCommand);
        }

        [TestMethod]
        public void Constructor_InitializesWithDefaultState()
        {
            var viewModel = CreateViewModel();
            Assert.IsFalse(viewModel.IsUpdateAvailable);
            Assert.IsFalse(viewModel.IsCheckingForUpdates);
            Assert.IsFalse(viewModel.IsDownloadingUpdate);
            Assert.AreEqual(0.0, viewModel.DownloadProgress);
            Assert.AreEqual(string.Empty, viewModel.DownloadStatusText);
            Assert.AreEqual(string.Empty, viewModel.ReleaseNotes);
            Assert.IsNull(viewModel.LatestVersion);
            Assert.AreEqual("1.0.0", viewModel.CurrentVersion);
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentNullException))]
        public void Constructor_WithNullUpdateService_ThrowsArgumentNullException()
        {
            _ = new UpdateViewModel(_mockContext, null!);
        }

        #endregion

        #region Check For Updates Tests

        [TestMethod]
        public async Task CheckForUpdatesAsync_WhenUpdateAvailable_SetsIsUpdateAvailableAndLatestVersion()
        {
            var latestVersion = new Version(2, 0, 0);
            _mockUpdateService.Setup(x => x.CheckForUpdatesAsync(It.IsAny<bool>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(true);
            _mockUpdateService.Setup(x => x.LatestVersion).Returns(latestVersion);
            _mockUpdateService.Setup(x => x.GetReleaseNotesAsync(It.IsAny<Version>()))
                .ReturnsAsync("Release notes for v2.0.0");

            var viewModel = CreateViewModel();
            await viewModel.CheckForUpdatesAsync();

            Assert.IsTrue(viewModel.IsUpdateAvailable);
            Assert.IsNotNull(viewModel.LatestVersion);
            Assert.AreEqual(2, viewModel.LatestVersion!.Major);
            Assert.AreEqual(0, viewModel.LatestVersion.Minor);
            Assert.AreEqual("Release notes for v2.0.0", viewModel.ReleaseNotes);
        }

        [TestMethod]
        public async Task CheckForUpdatesAsync_WhenNoUpdateAvailable_KeepsIsUpdateAvailableFalse()
        {
            _mockUpdateService.Setup(x => x.CheckForUpdatesAsync(It.IsAny<bool>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(false);

            var viewModel = CreateViewModel();
            await viewModel.CheckForUpdatesAsync();

            Assert.IsFalse(viewModel.IsUpdateAvailable);
            Assert.IsNull(viewModel.LatestVersion);
        }

        [TestMethod]
        public async Task CheckForUpdatesAsync_WhenException_SetsErrorMessage()
        {
            _mockUpdateService.Setup(x => x.CheckForUpdatesAsync(It.IsAny<bool>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new Exception("Network error"));

            var viewModel = CreateViewModel();
            await viewModel.CheckForUpdatesAsync();

            Assert.IsNotNull(viewModel.ErrorMessage);
            Assert.IsTrue(viewModel.ErrorMessage!.Contains("Network error"));
        }

        #endregion

        #region Download/Install Flow Tests

        [TestMethod]
        public async Task DownloadUpdateAsync_WhenUpdateAvailable_DownloadsAndUpdatesProgress()
        {
            _mockUpdateService.Setup(x => x.DownloadUpdateAsync(It.IsAny<Action<double>>()))
                .Callback<Action<double>>(callback =>
                {
                    callback(0.5);
                    callback(1.0);
                })
                .ReturnsAsync(@"C:\temp\update.msix");

            var viewModel = CreateViewModel();
            viewModel.IsUpdateAvailable = true;
            _mockUpdateService.Setup(x => x.UpdateDownloadPath).Returns(@"C:\temp\update.msix");

            await viewModel.DownloadUpdateAsync();

            Assert.IsFalse(viewModel.IsDownloadingUpdate);
            Assert.AreEqual(1.0, viewModel.DownloadProgress);
        }

        [TestMethod]
        public async Task DownloadUpdateAsync_WhenNotUpdateAvailable_ReturnsWithoutDownloading()
        {
            var viewModel = CreateViewModel();
            viewModel.IsUpdateAvailable = false;

            await viewModel.DownloadUpdateAsync();

            _mockUpdateService.Verify(
                x => x.DownloadUpdateAsync(It.IsAny<Action<double>>()),
                Times.Never);
        }

        [TestMethod]
        public async Task InstallUpdateAsync_WhenNoUpdateFileAvailable_SetsErrorMessage()
        {
            _mockUpdateService.Setup(x => x.UpdateDownloadPath).Returns(string.Empty);
            var viewModel = CreateViewModel();

            await viewModel.InstallUpdateAsync();

            Assert.IsNotNull(viewModel.ErrorMessage);
            _mockUpdateService.Verify(
                x => x.InstallUpdateAsync(It.IsAny<string>(), It.IsAny<bool>()),
                Times.Never);
        }

        [TestMethod]
        public async Task InstallUpdateAsync_WhenUpdateFileAvailable_CallsInstallService()
        {
            var updatePath = @"C:\temp\update.msix";
            _mockUpdateService.Setup(x => x.UpdateDownloadPath).Returns(updatePath);
            _mockUpdateService.Setup(x => x.InstallUpdateAsync(updatePath, true))
                .ReturnsAsync(true);

            var viewModel = CreateViewModel();
            await viewModel.InstallUpdateAsync();

            _mockUpdateService.Verify(
                x => x.InstallUpdateAsync(updatePath, true),
                Times.Once);
        }

        #endregion

        #region Version Comparison Tests

        [TestMethod]
        public void CurrentVersion_ReturnsServiceCurrentVersion()
        {
            _mockUpdateService.Setup(x => x.CurrentVersion).Returns(new Version(1, 2, 3));
            var viewModel = CreateViewModel();
            Assert.AreEqual("1.2.3", viewModel.CurrentVersion);
        }

        [TestMethod]
        public void UpdateDownloadPath_ReturnsServiceUpdateDownloadPath()
        {
            var path = @"C:\updates\VoiceStudio_2.0.0.msix";
            _mockUpdateService.Setup(x => x.UpdateDownloadPath).Returns(path);
            var viewModel = CreateViewModel();
            Assert.AreEqual(path, viewModel.UpdateDownloadPath);
        }

        #endregion

        #region IPanelView Tests

        [TestMethod]
        public void ViewModel_ImplementsIPanelView()
        {
            var viewModel = CreateViewModel();
            var panelView = viewModel as IPanelView;
            Assert.IsNotNull(panelView);
            Assert.AreEqual("update", panelView.PanelId);
            Assert.IsNotNull(panelView.DisplayName);
        }

        #endregion
    }
}
