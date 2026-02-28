using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using VoiceStudio.App.ViewModels;
using VoiceStudio.App.Services;
using VoiceStudio.Core.Services;

namespace VoiceStudio.App.Tests.ViewModels
{
    /// <summary>
    /// Unit tests for VideoEditViewModel.
    /// Source: VideoEditViewModel.cs
    /// </summary>
    [TestClass]
    public class VideoEditViewModelTests : ViewModelTestBase
    {
        private Mock<IBackendClient>? _mockBackendClient;

        [TestInitialize]
        public override void TestInitialize()
        {
            base.TestInitialize();
            _mockBackendClient = new Mock<IBackendClient>();
        }

        private VideoEditViewModel CreateViewModel()
        {
            return new VideoEditViewModel(MockContext!, _mockBackendClient!.Object);
        }

        #region Construction and Initialization Tests

        [TestMethod]
        public void Constructor_WithValidDependencies_CreatesInstance()
        {
            // Arrange & Act
            var viewModel = CreateViewModel();

            // Assert
            Assert.IsNotNull(viewModel);
            Assert.AreEqual("video-edit", viewModel.PanelId);
            Assert.IsNotNull(viewModel.Effects);
            Assert.IsNotNull(viewModel.Transitions);
            Assert.IsNotNull(viewModel.ExportFormats);
        }

        [TestMethod]
        public void Constructor_WithNullContext_ThrowsArgumentNullException()
        {
            Assert.ThrowsException<ArgumentNullException>(() =>
                new VideoEditViewModel(null!, _mockBackendClient!.Object));
        }

        [TestMethod]
        public void Constructor_WithNullBackendClient_ThrowsArgumentNullException()
        {
            Assert.ThrowsException<ArgumentNullException>(() =>
                new VideoEditViewModel(MockContext!, null!));
        }

        [TestMethod]
        public void Constructor_InitializesDefaultEffects()
        {
            // Arrange & Act
            var viewModel = CreateViewModel();

            // Assert
            Assert.IsNotNull(viewModel.Effects);
            Assert.IsTrue(viewModel.Effects.Count >= 5);
            CollectionAssert.Contains(viewModel.Effects, "Brightness");
            CollectionAssert.Contains(viewModel.Effects, "Contrast");
            CollectionAssert.Contains(viewModel.Effects, "Blur");
        }

        [TestMethod]
        public void Constructor_InitializesDefaultTransitions()
        {
            // Arrange & Act
            var viewModel = CreateViewModel();

            // Assert
            Assert.IsNotNull(viewModel.Transitions);
            Assert.IsTrue(viewModel.Transitions.Count >= 2);
            CollectionAssert.Contains(viewModel.Transitions, "Fade In");
            CollectionAssert.Contains(viewModel.Transitions, "Fade Out");
        }

        [TestMethod]
        public void Constructor_InitializesDefaultExportFormats()
        {
            // Arrange & Act
            var viewModel = CreateViewModel();

            // Assert
            Assert.IsNotNull(viewModel.ExportFormats);
            Assert.IsTrue(viewModel.ExportFormats.Count >= 3);
            CollectionAssert.Contains(viewModel.ExportFormats, "mp4");
            CollectionAssert.Contains(viewModel.ExportFormats, "avi");
        }

        #endregion

        #region Property Tests

        [TestMethod]
        public void SelectedVideoPath_WhenSet_RaisesPropertyChanged()
        {
            // Arrange
            var viewModel = CreateViewModel();
            var propertyChanged = false;
            viewModel.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(viewModel.SelectedVideoPath))
                    propertyChanged = true;
            };

            // Act
            viewModel.SelectedVideoPath = @"C:\Videos\test.mp4";

            // Assert
            Assert.IsTrue(propertyChanged);
            Assert.AreEqual(@"C:\Videos\test.mp4", viewModel.SelectedVideoPath);
        }

        [TestMethod]
        public void SelectedEffect_WhenSet_RaisesPropertyChanged()
        {
            // Arrange
            var viewModel = CreateViewModel();
            var propertyChanged = false;
            viewModel.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(viewModel.SelectedEffect))
                    propertyChanged = true;
            };

            // Act
            viewModel.SelectedEffect = "Brightness";

            // Assert
            Assert.IsTrue(propertyChanged);
            Assert.AreEqual("Brightness", viewModel.SelectedEffect);
        }

        [TestMethod]
        public void ExportFormat_WhenSet_RaisesPropertyChanged()
        {
            // Arrange
            var viewModel = CreateViewModel();
            var propertyChanged = false;
            viewModel.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(viewModel.ExportFormat))
                    propertyChanged = true;
            };

            // Act
            viewModel.ExportFormat = "avi";

            // Assert
            Assert.IsTrue(propertyChanged);
            Assert.AreEqual("avi", viewModel.ExportFormat);
        }

        [TestMethod]
        public void TrimStart_WhenSet_RaisesPropertyChanged()
        {
            // Arrange
            var viewModel = CreateViewModel();
            var propertyChanged = false;
            viewModel.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(viewModel.TrimStart))
                    propertyChanged = true;
            };

            // Act
            viewModel.TrimStart = 5.5;

            // Assert
            Assert.IsTrue(propertyChanged);
            Assert.AreEqual(5.5, viewModel.TrimStart);
        }

        [TestMethod]
        public void TrimEnd_WhenSet_RaisesPropertyChanged()
        {
            // Arrange
            var viewModel = CreateViewModel();
            var propertyChanged = false;
            viewModel.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(viewModel.TrimEnd))
                    propertyChanged = true;
            };

            // Act
            viewModel.TrimEnd = 30.0;

            // Assert
            Assert.IsTrue(propertyChanged);
            Assert.AreEqual(30.0, viewModel.TrimEnd);
        }

        #endregion

        #region Panel Interface Tests

        [TestMethod]
        public void PanelId_ReturnsCorrectValue()
        {
            var viewModel = CreateViewModel();
            Assert.AreEqual("video-edit", viewModel.PanelId);
        }

        [TestMethod]
        public void DisplayName_ReturnsNonEmptyString()
        {
            var viewModel = CreateViewModel();
            Assert.IsFalse(string.IsNullOrEmpty(viewModel.DisplayName));
        }

        [TestMethod]
        public void Region_ReturnsCenterRegion()
        {
            var viewModel = CreateViewModel();
            Assert.AreEqual(VoiceStudio.Core.Panels.PanelRegion.Center, viewModel.Region);
        }

        #endregion

        #region Error Handling Tests

        [TestMethod]
        public void ViewModel_WhenErrorOccurs_HandlesGracefully()
        {
            // Arrange
            var viewModel = CreateViewModel();

            // Act - setting null or boundary values should not throw
            viewModel.SelectedVideoPath = null;
            viewModel.SelectedEffect = null;
            viewModel.SelectedTransition = null;
            viewModel.TrimStart = 0;
            viewModel.TrimEnd = 0;

            // Assert
            Assert.IsNotNull(viewModel);
            Assert.IsNotNull(viewModel.Effects);
            Assert.IsNotNull(viewModel.Transitions);
        }

        #endregion
    }
}