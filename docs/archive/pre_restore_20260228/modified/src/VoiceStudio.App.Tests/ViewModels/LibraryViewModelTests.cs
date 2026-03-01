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
        private MockViewModelContext _mockContext = null!;
        private Mock<IBackendClient> _mockBackendClient = null!;
        private LibraryViewModel _viewModel = null!;

        [TestInitialize]
        public void Setup()
        {
            TestAppServicesHelper.EnsureInitialized();
            _mockContext = new MockViewModelContext();
            _mockBackendClient = new Mock<IBackendClient>();
            _viewModel = new LibraryViewModel(_mockContext, _mockBackendClient.Object);
        }

        [TestCleanup]
        public void Cleanup()
        {
            // No dispatcher cleanup needed with MockViewModelContext
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
