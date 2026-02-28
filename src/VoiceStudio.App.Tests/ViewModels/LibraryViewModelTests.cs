using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.UI.Dispatching;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using VoiceStudio.App.Tests.Fixtures;
using VoiceStudio.App.ViewModels;
using VoiceStudio.Core.Services;

namespace VoiceStudio.App.Tests.ViewModels
{
    [TestClass]
    public class LibraryViewModelTests
    {
        private IViewModelContext _context = null!;
        private Mock<IBackendClient> _mockBackendClient = null!;
        private DispatcherQueueController? _dispatcherController;
        private LibraryViewModel _viewModel = null!;

        [TestInitialize]
        public void Setup()
        {
            TestAppServicesHelper.EnsureInitialized();
            _dispatcherController = DispatcherQueueController.CreateOnDedicatedThread();
            var dispatcher = _dispatcherController.DispatcherQueue;
            _context = new ViewModelContext(NullLogger.Instance, dispatcher);
            _mockBackendClient = new Mock<IBackendClient>();
            _viewModel = new LibraryViewModel(_context, _mockBackendClient.Object);
        }

        [TestCleanup]
        public void Cleanup()
        {
            _dispatcherController?.ShutdownQueueAsync().AsTask().GetAwaiter().GetResult();
        }

        [TestMethod]
        public void Constructor_InitializesWithDefaultValues()
        {
            Assert.IsNotNull(_viewModel);
            Assert.AreEqual("library", _viewModel.PanelId);
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
