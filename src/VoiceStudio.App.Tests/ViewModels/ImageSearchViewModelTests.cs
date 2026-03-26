using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using System;
using System.Threading.Tasks;
using VoiceStudio.App.ViewModels;
using VoiceStudio.Core.Panels;
using VoiceStudio.Core.Services;

namespace VoiceStudio.App.Tests.ViewModels
{
    /// <summary>
    /// Unit tests for ImageSearchViewModel.
    /// Tests cover construction, property changes, default values, and panel interface.
    /// </summary>
    [TestClass]
    public class ImageSearchViewModelTests : ViewModelTestBase
    {
        private Mock<IImageSearchClient>? _mockImageSearchClient;
        private ImageSearchViewModel? _viewModel;

        [TestInitialize]
        public override void TestInitialize()
        {
            base.TestInitialize();
            _mockImageSearchClient = new Mock<IImageSearchClient>();
            _viewModel = CreateViewModel();
        }

        [TestCleanup]
        public override void TestCleanup()
        {
            _viewModel = null;
            _mockImageSearchClient = null;
            base.TestCleanup();
        }

        private ImageSearchViewModel CreateViewModel()
        {
            return new ImageSearchViewModel(MockContext!, _mockImageSearchClient!.Object);
        }

        #region Construction and Initialization Tests

        [TestMethod]
        public void Constructor_WithValidDependencies_CreatesInstance()
        {
            Assert.IsNotNull(_viewModel);
            Assert.IsNotNull(_viewModel.SearchResults);
            Assert.IsNotNull(_viewModel.AvailableSources);
            Assert.IsNotNull(_viewModel.AvailableCategories);
            Assert.IsNotNull(_viewModel.AvailableColors);
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentNullException))]
        public void Constructor_WithNullContext_ThrowsArgumentNullException()
        {
            _ = new ImageSearchViewModel(null!, _mockImageSearchClient!.Object);
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentNullException))]
        public void Constructor_WithNullImageSearchClient_ThrowsArgumentNullException()
        {
            _ = new ImageSearchViewModel(MockContext!, null!);
        }

        #endregion

        #region Panel Interface Tests

        [TestMethod]
        public void PanelId_ReturnsImageSearch()
        {
            Assert.AreEqual(PanelIds.ImageSearch, _viewModel!.PanelId);
        }

        [TestMethod]
        public void DisplayName_IsNotEmpty()
        {
            Assert.IsNotNull(_viewModel!.DisplayName);
            Assert.IsTrue(_viewModel.DisplayName.Length > 0);
        }

        [TestMethod]
        public void Region_ReturnsCenter()
        {
            Assert.AreEqual(VoiceStudio.Core.Panels.PanelRegion.Center, _viewModel!.Region);
        }

        #endregion

        #region Default Values Tests

        [TestMethod]
        public void SearchQuery_DefaultsToEmpty()
        {
            Assert.AreEqual(string.Empty, _viewModel!.SearchQuery);
        }

        [TestMethod]
        public void CurrentPage_DefaultsToOne()
        {
            Assert.AreEqual(1, _viewModel!.CurrentPage);
        }

        [TestMethod]
        public void TotalPages_DefaultsToOne()
        {
            Assert.AreEqual(1, _viewModel!.TotalPages);
        }

        [TestMethod]
        public void TotalResults_DefaultsToZero()
        {
            Assert.AreEqual(0, _viewModel!.TotalResults);
        }

        [TestMethod]
        public void PerPage_DefaultsToTwenty()
        {
            Assert.AreEqual(20, _viewModel!.PerPage);
        }

        [TestMethod]
        public void IsSearching_DefaultsToFalse()
        {
            Assert.IsFalse(_viewModel!.IsSearching);
        }

        #endregion

        #region Property Change Tests

        [TestMethod]
        public void SearchQuery_WhenSet_RaisesPropertyChanged()
        {
            var propertyChanged = false;
            _viewModel!.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(_viewModel.SearchQuery))
                    propertyChanged = true;
            };

            _viewModel.SearchQuery = "test search";

            Assert.IsTrue(propertyChanged);
            Assert.AreEqual("test search", _viewModel.SearchQuery);
        }

        [TestMethod]
        public void CurrentPage_WhenSet_RaisesPropertyChanged()
        {
            var propertyChanged = false;
            _viewModel!.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(_viewModel.CurrentPage))
                    propertyChanged = true;
            };

            _viewModel.CurrentPage = 5;

            Assert.IsTrue(propertyChanged);
            Assert.AreEqual(5, _viewModel.CurrentPage);
        }

        [TestMethod]
        public void TotalPages_WhenSet_RaisesPropertyChanged()
        {
            var propertyChanged = false;
            _viewModel!.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(_viewModel.TotalPages))
                    propertyChanged = true;
            };

            _viewModel.TotalPages = 10;

            Assert.IsTrue(propertyChanged);
            Assert.AreEqual(10, _viewModel.TotalPages);
        }

        [TestMethod]
        public void TotalResults_WhenSet_RaisesPropertyChanged()
        {
            var propertyChanged = false;
            _viewModel!.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(_viewModel.TotalResults))
                    propertyChanged = true;
            };

            _viewModel.TotalResults = 100;

            Assert.IsTrue(propertyChanged);
            Assert.AreEqual(100, _viewModel.TotalResults);
        }

        [TestMethod]
        public void PerPage_WhenSet_RaisesPropertyChanged()
        {
            var propertyChanged = false;
            _viewModel!.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(_viewModel.PerPage))
                    propertyChanged = true;
            };

            _viewModel.PerPage = 50;

            Assert.IsTrue(propertyChanged);
            Assert.AreEqual(50, _viewModel.PerPage);
        }

        [TestMethod]
        public void IsSearching_WhenSet_RaisesPropertyChanged()
        {
            var propertyChanged = false;
            _viewModel!.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(_viewModel.IsSearching))
                    propertyChanged = true;
            };

            _viewModel.IsSearching = true;

            Assert.IsTrue(propertyChanged);
            Assert.IsTrue(_viewModel.IsSearching);
        }

        [TestMethod]
        public void SelectedSource_WhenSet_RaisesPropertyChanged()
        {
            var propertyChanged = false;
            _viewModel!.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(_viewModel.SelectedSource))
                    propertyChanged = true;
            };

            _viewModel.SelectedSource = "unsplash";

            Assert.IsTrue(propertyChanged);
            Assert.AreEqual("unsplash", _viewModel.SelectedSource);
        }

        [TestMethod]
        public void SelectedCategory_WhenSet_RaisesPropertyChanged()
        {
            var propertyChanged = false;
            _viewModel!.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(_viewModel.SelectedCategory))
                    propertyChanged = true;
            };

            _viewModel.SelectedCategory = "nature";

            Assert.IsTrue(propertyChanged);
            Assert.AreEqual("nature", _viewModel.SelectedCategory);
        }

        #endregion

        #region Selection Tests

        [TestMethod]
        public void SelectedResult_InitiallyNull()
        {
            Assert.IsNull(_viewModel!.SelectedResult);
        }

        [TestMethod]
        public void SelectedSource_InitiallyNull()
        {
            Assert.IsNull(_viewModel!.SelectedSource);
        }

        [TestMethod]
        public void SelectedCategory_InitiallyNull()
        {
            Assert.IsNull(_viewModel!.SelectedCategory);
        }

        [TestMethod]
        public void SelectedOrientation_InitiallyNull()
        {
            Assert.IsNull(_viewModel!.SelectedOrientation);
        }

        [TestMethod]
        public void SelectedColor_InitiallyNull()
        {
            Assert.IsNull(_viewModel!.SelectedColor);
        }

        #endregion
    }
}