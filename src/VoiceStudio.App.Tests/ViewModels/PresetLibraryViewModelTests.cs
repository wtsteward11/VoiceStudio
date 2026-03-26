using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using VoiceStudio.App.ViewModels;
using VoiceStudio.App.Services;
using VoiceStudio.Core.Panels;
using VoiceStudio.Core.Services;

namespace VoiceStudio.App.Tests.ViewModels
{
    /// <summary>
    /// Unit tests for PresetLibraryViewModel.
    /// Source: PresetLibraryViewModel.cs
    /// </summary>
    [TestClass]
    public class PresetLibraryViewModelTests : ViewModelTestBase
    {
        private Mock<IPresetLibraryClient>? _mockPresetLibraryClient;
        private Mock<IDialogService>? _mockDialogService;

        [TestInitialize]
        public override void TestInitialize()
        {
            base.TestInitialize();
            _mockPresetLibraryClient = new Mock<IPresetLibraryClient>();
            _mockDialogService = new Mock<IDialogService>();
        }

        private PresetLibraryViewModel CreateViewModel()
        {
            return new PresetLibraryViewModel(MockContext!, _mockPresetLibraryClient!.Object, _mockDialogService!.Object);
        }

        #region Construction and Initialization Tests

        [TestMethod]
        public void Constructor_WithValidDependencies_CreatesInstance()
        {
            // Arrange & Act
            var viewModel = CreateViewModel();

            // Assert
            Assert.IsNotNull(viewModel);
            Assert.AreEqual(PanelIds.PresetLibrary, viewModel.PanelId);
            Assert.IsNotNull(viewModel.Presets);
            Assert.IsNotNull(viewModel.AvailablePresetTypes);
            Assert.IsNotNull(viewModel.AvailableCategories);
        }

        [TestMethod]
        public void Constructor_WithNullContext_ThrowsArgumentNullException()
        {
            Assert.ThrowsException<ArgumentNullException>(() =>
                new PresetLibraryViewModel(null!, _mockPresetLibraryClient!.Object, _mockDialogService!.Object));
        }

        [TestMethod]
        public void Constructor_WithNullPresetLibraryClient_ThrowsArgumentNullException()
        {
            Assert.ThrowsException<ArgumentNullException>(() =>
                new PresetLibraryViewModel(MockContext!, null!, _mockDialogService!.Object));
        }

        [TestMethod]
        public void Constructor_InitializesEmptyCollections()
        {
            // Arrange & Act
            var viewModel = CreateViewModel();

            // Assert
            Assert.IsNotNull(viewModel.Presets);
            Assert.AreEqual(0, viewModel.Presets.Count);
            Assert.IsNotNull(viewModel.AvailablePresetTypes);
            Assert.IsNotNull(viewModel.AvailableCategories);
        }

        #endregion

        #region Property Tests

        [TestMethod]
        public void Presets_WhenSet_RaisesPropertyChanged()
        {
            // Arrange
            var viewModel = CreateViewModel();
            var propertyChanged = false;
            viewModel.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(viewModel.Presets))
                    propertyChanged = true;
            };

            // Act
            viewModel.Presets = new ObservableCollection<Preset>();

            // Assert
            Assert.IsTrue(propertyChanged);
        }

        [TestMethod]
        public void AvailablePresetTypes_WhenSet_RaisesPropertyChanged()
        {
            // Arrange
            var viewModel = CreateViewModel();
            var propertyChanged = false;
            viewModel.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(viewModel.AvailablePresetTypes))
                    propertyChanged = true;
            };

            // Act
            viewModel.AvailablePresetTypes = new ObservableCollection<string> { "Voice", "Effect" };

            // Assert
            Assert.IsTrue(propertyChanged);
        }

        [TestMethod]
        public void AvailableCategories_WhenSet_RaisesPropertyChanged()
        {
            // Arrange
            var viewModel = CreateViewModel();
            var propertyChanged = false;
            viewModel.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(viewModel.AvailableCategories))
                    propertyChanged = true;
            };

            // Act
            viewModel.AvailableCategories = new ObservableCollection<string> { "Favorites", "Custom" };

            // Assert
            Assert.IsTrue(propertyChanged);
        }

        [TestMethod]
        public void TotalPresets_WhenSet_RaisesPropertyChanged()
        {
            // Arrange
            var viewModel = CreateViewModel();
            var propertyChanged = false;
            viewModel.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(viewModel.TotalPresets))
                    propertyChanged = true;
            };

            // Act
            viewModel.TotalPresets = 42;

            // Assert
            Assert.IsTrue(propertyChanged);
            Assert.AreEqual(42, viewModel.TotalPresets);
        }

        [TestMethod]
        public void SelectedPreset_WhenSet_RaisesPropertyChanged()
        {
            // Arrange
            var viewModel = CreateViewModel();
            var propertyChanged = false;
            viewModel.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(viewModel.SelectedPreset))
                    propertyChanged = true;
            };

            // Act
            viewModel.SelectedPreset = new Preset { Id = "preset-1" };

            // Assert
            Assert.IsTrue(propertyChanged);
        }

        [TestMethod]
        public void SearchQuery_WhenSet_RaisesPropertyChanged()
        {
            // Arrange
            var viewModel = CreateViewModel();
            var propertyChanged = false;
            viewModel.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(viewModel.SearchQuery))
                    propertyChanged = true;
            };

            // Act
            viewModel.SearchQuery = "warm";

            // Assert
            Assert.IsTrue(propertyChanged);
            Assert.AreEqual("warm", viewModel.SearchQuery);
        }

        #endregion

        #region Panel Interface Tests

        [TestMethod]
        public void PanelId_ReturnsCorrectValue()
        {
            var viewModel = CreateViewModel();
            Assert.AreEqual(PanelIds.PresetLibrary, viewModel.PanelId);
        }

        [TestMethod]
        public void DisplayName_ReturnsNonEmptyString()
        {
            var viewModel = CreateViewModel();
            Assert.IsFalse(string.IsNullOrEmpty(viewModel.DisplayName));
        }

        [TestMethod]
        public void Region_ReturnsRightRegion()
        {
            var viewModel = CreateViewModel();
            Assert.AreEqual(VoiceStudio.Core.Panels.PanelRegion.Right, viewModel.Region);
        }

        #endregion

        #region Error Handling Tests

        [TestMethod]
        public void ViewModel_WhenErrorOccurs_HandlesGracefully()
        {
            // Arrange
            var viewModel = CreateViewModel();

            // Act - setting null values should not throw
            viewModel.SearchQuery = null;
            viewModel.SelectedPresetType = null;
            viewModel.SelectedCategory = null;
            viewModel.SelectedPreset = null;
            viewModel.TargetId = null;

            // Assert
            Assert.IsNotNull(viewModel);
            Assert.IsNotNull(viewModel.Presets);
        }

        #endregion
    }
}