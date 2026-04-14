using System;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.UI.Dispatching;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using VoiceStudio.App.Services;
using VoiceStudio.App.Tests.Fixtures;
using VoiceStudio.App.ViewModels;
using VoiceStudio.Core.Panels;
using VoiceStudio.Core.Services;

namespace VoiceStudio.App.Tests.ViewModels
{
    [TestClass]
    public class LibraryViewModelTests
    {
        private IViewModelContext _context = null!;
        private Mock<ILibraryClient> _mockLibraryClient = null!;
        private Mock<IDialogService> _mockDialogService = null!;
        private DispatcherQueueController? _dispatcherController;
        private LibraryViewModel _viewModel = null!;

        [TestInitialize]
        public void Setup()
        {
            TestAppServicesHelper.EnsureInitialized();
            _dispatcherController = DispatcherQueueController.CreateOnDedicatedThread();
            var dispatcher = _dispatcherController.DispatcherQueue;
            _context = new ViewModelContext(NullLogger.Instance, dispatcher);
            _mockLibraryClient = new Mock<ILibraryClient>();
            _mockLibraryClient.Setup(x => x.GetLibraryFoldersAsync(It.IsAny<string?>(), It.IsAny<System.Threading.CancellationToken>()))
                .ReturnsAsync(new LibraryFoldersResponse { Folders = Array.Empty<LibraryFolder>() });
            _mockLibraryClient.Setup(x => x.SearchAssetsAsync(It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<System.Threading.CancellationToken>()))
                .ReturnsAsync(new AssetSearchResponse { Assets = Array.Empty<LibraryAsset>(), Total = 0 });
            _mockLibraryClient.Setup(x => x.GetAssetTypesAsync(It.IsAny<System.Threading.CancellationToken>()))
                .ReturnsAsync(new AssetTypesResponse { Types = Array.Empty<AssetTypeInfo>() });
            _mockDialogService = new Mock<IDialogService>();
            _mockDialogService.Setup(d => d.ShowConfirmationAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
                .ReturnsAsync(true);
            _viewModel = new LibraryViewModel(_context, _mockLibraryClient.Object, _mockDialogService.Object);
        }

        [TestCleanup]
        public void Cleanup()
        {
            DispatcherQueueTestHelpers.ShutdownSyncBounded(_dispatcherController);
        }

        [TestMethod]
        public void Constructor_InitializesWithDefaultValues()
        {
            Assert.IsNotNull(_viewModel);
            Assert.AreEqual(PanelIds.Library, _viewModel.PanelId);
            Assert.AreEqual("Library", _viewModel.DisplayName);
        }

        [TestMethod]
        public void Folders_InitializesAsEmptyCollection()
        {
            Assert.IsNotNull(_viewModel.Folders);
            Assert.AreEqual(0, _viewModel.Folders.Count);
        }

        [TestMethod]
        public void Assets_InitializesAsEmptyCollection()
        {
            Assert.IsNotNull(_viewModel.Assets);
            Assert.AreEqual(0, _viewModel.Assets.Count);
        }

        [TestMethod]
        public void SelectedFolder_DefaultsToNull()
        {
            Assert.IsNull(_viewModel.SelectedFolder);
        }

        [TestMethod]
        public void SelectedAsset_DefaultsToNull()
        {
            Assert.IsNull(_viewModel.SelectedAsset);
        }

        [TestMethod]
        public void SearchQuery_DefaultsToNull()
        {
            Assert.IsNull(_viewModel.SearchQuery);
        }

        [TestMethod]
        public void ShowFolders_DefaultsToTrue()
        {
            Assert.IsTrue(_viewModel.ShowFolders);
        }

        [TestMethod]
        public void TotalAssets_DefaultsToZero()
        {
            Assert.AreEqual(0, _viewModel.TotalAssets);
        }

        [TestMethod]
        public void SelectedAssetCount_DefaultsToZero()
        {
            Assert.AreEqual(0, _viewModel.SelectedAssetCount);
        }

        [TestMethod]
        public void HasMultipleAssetSelection_DefaultsToFalse()
        {
            Assert.IsFalse(_viewModel.HasMultipleAssetSelection);
        }

        [TestMethod]
        public void AvailableAssetTypes_InitializesAsEmptyCollection()
        {
            Assert.IsNotNull(_viewModel.AvailableAssetTypes);
        }
    }
}
