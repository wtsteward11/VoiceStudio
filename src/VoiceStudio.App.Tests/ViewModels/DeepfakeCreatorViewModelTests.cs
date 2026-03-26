using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using System;
using System.ComponentModel;
using System.Threading.Tasks;
using VoiceStudio.App.ViewModels;
using VoiceStudio.App.Services;
using VoiceStudio.Core.Panels;
using VoiceStudio.Core.Services;

namespace VoiceStudio.App.Tests.ViewModels
{
    /// <summary>
    /// Unit tests for DeepfakeCreatorViewModel.
    /// Tests MVVM property change notifications and panel interface.
    /// </summary>
    [TestClass]
    public class DeepfakeCreatorViewModelTests : ViewModelTestBase
    {
        private Mock<IDeepfakeCreatorClient>? _mockDeepfakeClient;

        [TestInitialize]
        public override void TestInitialize()
        {
            base.TestInitialize();
            _mockDeepfakeClient = new Mock<IDeepfakeCreatorClient>();
        }

        private DeepfakeCreatorViewModel CreateViewModel()
        {
            return new DeepfakeCreatorViewModel(MockContext!, _mockDeepfakeClient!.Object);
        }

        #region Construction and Initialization Tests

        [TestMethod]
        public void Constructor_WithValidDependencies_CreatesInstance()
        {
            // Arrange & Act
            var viewModel = CreateViewModel();

            // Assert
            Assert.IsNotNull(viewModel);
            Assert.AreEqual(PanelIds.DeepfakeCreator, viewModel.PanelId);
        }

        [TestMethod]
        public void Constructor_SetsDefaultValues()
        {
            // Arrange & Act
            var viewModel = CreateViewModel();

            // Assert
            Assert.IsFalse(viewModel.ConsentGiven);
            Assert.IsTrue(viewModel.ApplyWatermark);
            Assert.IsFalse(viewModel.IsProcessing);
            Assert.AreEqual(0, viewModel.UploadProgress);
            Assert.IsNotNull(viewModel.AvailableMediaTypes);
            Assert.IsNotNull(viewModel.AvailableQualities);
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentNullException))]
        public void Constructor_WithNullContext_ThrowsArgumentNullException()
        {
            _ = new DeepfakeCreatorViewModel(null!, _mockDeepfakeClient!.Object);
        }

        #endregion

        #region Property Tests

        [TestMethod]
        public void SelectedMediaType_WhenSet_RaisesPropertyChanged()
        {
            // Arrange
            var viewModel = CreateViewModel();
            var propertyChanged = false;
            viewModel.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(viewModel.SelectedMediaType))
                    propertyChanged = true;
            };

            // Act
            viewModel.SelectedMediaType = "Video";

            // Assert
            Assert.IsTrue(propertyChanged);
            Assert.AreEqual("Video", viewModel.SelectedMediaType);
        }

        [TestMethod]
        public void ConsentGiven_WhenSet_RaisesPropertyChanged()
        {
            // Arrange
            var viewModel = CreateViewModel();
            var propertyChanged = false;
            viewModel.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(viewModel.ConsentGiven))
                    propertyChanged = true;
            };

            // Act
            viewModel.ConsentGiven = true;

            // Assert
            Assert.IsTrue(propertyChanged);
            Assert.IsTrue(viewModel.ConsentGiven);
        }

        [TestMethod]
        public void ApplyWatermark_WhenToggled_RaisesPropertyChanged()
        {
            // Arrange
            var viewModel = CreateViewModel();
            var propertyChanged = false;
            viewModel.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(viewModel.ApplyWatermark))
                    propertyChanged = true;
            };

            // Act
            viewModel.ApplyWatermark = false;

            // Assert
            Assert.IsTrue(propertyChanged);
            Assert.IsFalse(viewModel.ApplyWatermark);
        }

        [TestMethod]
        public void SelectedQuality_WhenSet_RaisesPropertyChanged()
        {
            // Arrange
            var viewModel = CreateViewModel();
            var propertyChanged = false;
            viewModel.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(viewModel.SelectedQuality))
                    propertyChanged = true;
            };

            // Act
            viewModel.SelectedQuality = "High";

            // Assert
            Assert.IsTrue(propertyChanged);
            Assert.AreEqual("High", viewModel.SelectedQuality);
        }

        [TestMethod]
        public void IsProcessing_WhenSet_RaisesPropertyChanged()
        {
            // Arrange
            var viewModel = CreateViewModel();
            var propertyChanged = false;
            viewModel.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(viewModel.IsProcessing))
                    propertyChanged = true;
            };

            // Act
            viewModel.IsProcessing = true;

            // Assert
            Assert.IsTrue(propertyChanged);
            Assert.IsTrue(viewModel.IsProcessing);
        }

        [TestMethod]
        public void UploadProgress_WhenSet_UpdatesValue()
        {
            // Arrange
            var viewModel = CreateViewModel();

            // Act
            viewModel.UploadProgress = 50;

            // Assert
            Assert.AreEqual(50, viewModel.UploadProgress);
        }

        [TestMethod]
        public void AvailableMediaTypes_ContainsExpectedOptions()
        {
            // Arrange & Act
            var viewModel = CreateViewModel();

            // Assert
            Assert.IsNotNull(viewModel.AvailableMediaTypes);
            Assert.IsTrue(viewModel.AvailableMediaTypes.Count > 0);
        }

        [TestMethod]
        public void AvailableQualities_ContainsExpectedOptions()
        {
            // Arrange & Act
            var viewModel = CreateViewModel();

            // Assert
            Assert.IsNotNull(viewModel.AvailableQualities);
            Assert.IsTrue(viewModel.AvailableQualities.Count > 0);
        }

        #endregion

        #region Panel Interface Tests

        [TestMethod]
        public void PanelId_ReturnsCorrectValue()
        {
            // Arrange & Act
            var viewModel = CreateViewModel();

            // Assert
            Assert.AreEqual(PanelIds.DeepfakeCreator, viewModel.PanelId);
        }

        [TestMethod]
        public void DisplayName_ReturnsNonEmptyString()
        {
            // Arrange & Act
            var viewModel = CreateViewModel();

            // Assert
            Assert.IsFalse(string.IsNullOrEmpty(viewModel.DisplayName));
        }

        #endregion

        #region Error Handling Tests

        [TestMethod]
        public void ViewModel_WithValidState_DoesNotThrowOnPropertyAccess()
        {
            // Arrange
            var viewModel = CreateViewModel();

            // Act & Assert - accessing all properties should not throw
            _ = viewModel.SelectedMediaType;
            _ = viewModel.ConsentGiven;
            _ = viewModel.ApplyWatermark;
            _ = viewModel.SelectedQuality;
            _ = viewModel.IsProcessing;
            _ = viewModel.UploadProgress;
            _ = viewModel.AvailableEngines;
            _ = viewModel.DeepfakeJobs;
        }

        #endregion
    }
}