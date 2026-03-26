using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using System;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;
using VoiceStudio.App.ViewModels;
using VoiceStudio.App.Services;
using VoiceStudio.Core.Models;
using VoiceStudio.Core.Panels;
using VoiceStudio.Core.Services;

namespace VoiceStudio.App.Tests.ViewModels
{
    /// <summary>
    /// Unit tests for UpscalingViewModel.
    /// Tests cover construction, property changes, default values, and panel interface.
    /// </summary>
    [TestClass]
    public class UpscalingViewModelTests : ViewModelTestBase
    {
        private Mock<IUpscalingClient>? _mockUpscalingClient;
        private UpscalingViewModel? _viewModel;

        [TestInitialize]
        public override void TestInitialize()
        {
            base.TestInitialize();
            _mockUpscalingClient = new Mock<IUpscalingClient>();
            _mockUpscalingClient.Setup(x => x.GetEnginesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(Array.Empty<UpscalingEngineResponse>());
            _mockUpscalingClient.Setup(x => x.GetJobsAsync(It.IsAny<CancellationToken>())).ReturnsAsync(Array.Empty<UpscalingJobResponse>());
            _viewModel = CreateViewModel();
        }

        [TestCleanup]
        public override void TestCleanup()
        {
            _viewModel = null;
            _mockUpscalingClient = null;
            base.TestCleanup();
        }

        private UpscalingViewModel CreateViewModel()
        {
            return new UpscalingViewModel(MockContext!, _mockUpscalingClient!.Object);
        }

        #region Construction and Initialization Tests

        [TestMethod]
        public void Constructor_WithValidDependencies_CreatesInstance()
        {
            // Assert
            Assert.IsNotNull(_viewModel);
            Assert.IsNotNull(_viewModel.AvailableEngines);
            Assert.IsNotNull(_viewModel.UpscalingJobs);
            Assert.IsNotNull(_viewModel.AvailableMediaTypes);
            Assert.IsNotNull(_viewModel.AvailableScaleFactors);
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentNullException))]
        public void Constructor_WithNullContext_ThrowsArgumentNullException()
        {
            _ = new UpscalingViewModel(null!, _mockUpscalingClient!.Object);
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentNullException))]
        public void Constructor_WithNullUpscalingClient_ThrowsArgumentNullException()
        {
            _ = new UpscalingViewModel(MockContext!, null!);
        }

        #endregion

        #region Panel Interface Tests

        [TestMethod]
        public void PanelId_ReturnsUpscaling()
        {
            Assert.AreEqual(PanelIds.Upscaling, _viewModel!.PanelId);
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
        public void SelectedMediaType_DefaultsToImage()
        {
            Assert.AreEqual("image", _viewModel!.SelectedMediaType);
        }

        [TestMethod]
        public void SelectedScaleFactor_DefaultsTo2()
        {
            Assert.AreEqual(2.0, _viewModel!.SelectedScaleFactor);
        }

        [TestMethod]
        public void AvailableMediaTypes_ContainsImageAndVideo()
        {
            Assert.IsTrue(_viewModel!.AvailableMediaTypes.Contains("image"));
            Assert.IsTrue(_viewModel.AvailableMediaTypes.Contains("video"));
        }

        [TestMethod]
        public void AvailableScaleFactors_ContainsExpectedValues()
        {
            Assert.IsTrue(_viewModel!.AvailableScaleFactors.Contains(2.0));
            Assert.IsTrue(_viewModel.AvailableScaleFactors.Contains(4.0));
            Assert.IsTrue(_viewModel.AvailableScaleFactors.Contains(8.0));
        }

        [TestMethod]
        public void IsProcessing_DefaultsToFalse()
        {
            Assert.IsFalse(_viewModel!.IsProcessing);
        }

        [TestMethod]
        public void IsUploading_DefaultsToFalse()
        {
            Assert.IsFalse(_viewModel!.IsUploading);
        }

        [TestMethod]
        public void UploadProgress_DefaultsToZero()
        {
            Assert.AreEqual(0.0, _viewModel!.UploadProgress);
        }

        #endregion

        #region Property Change Tests

        [TestMethod]
        public void SelectedMediaType_WhenSet_RaisesPropertyChanged()
        {
            var propertyChanged = false;
            _viewModel!.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(_viewModel.SelectedMediaType))
                    propertyChanged = true;
            };

            _viewModel.SelectedMediaType = "video";

            Assert.IsTrue(propertyChanged);
            Assert.AreEqual("video", _viewModel.SelectedMediaType);
        }

        [TestMethod]
        public void SelectedScaleFactor_WhenSet_RaisesPropertyChanged()
        {
            var propertyChanged = false;
            _viewModel!.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(_viewModel.SelectedScaleFactor))
                    propertyChanged = true;
            };

            _viewModel.SelectedScaleFactor = 4.0;

            Assert.IsTrue(propertyChanged);
            Assert.AreEqual(4.0, _viewModel.SelectedScaleFactor);
        }

        [TestMethod]
        public void IsProcessing_WhenSet_RaisesPropertyChanged()
        {
            var propertyChanged = false;
            _viewModel!.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(_viewModel.IsProcessing))
                    propertyChanged = true;
            };

            _viewModel.IsProcessing = true;

            Assert.IsTrue(propertyChanged);
            Assert.IsTrue(_viewModel.IsProcessing);
        }

        [TestMethod]
        public void UploadProgress_WhenSet_RaisesPropertyChanged()
        {
            var propertyChanged = false;
            _viewModel!.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(_viewModel.UploadProgress))
                    propertyChanged = true;
            };

            _viewModel.UploadProgress = 50.0;

            Assert.IsTrue(propertyChanged);
            Assert.AreEqual(50.0, _viewModel.UploadProgress);
        }

        [TestMethod]
        public void IsUploading_WhenSet_RaisesPropertyChanged()
        {
            var propertyChanged = false;
            _viewModel!.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(_viewModel.IsUploading))
                    propertyChanged = true;
            };

            _viewModel.IsUploading = true;

            Assert.IsTrue(propertyChanged);
            Assert.IsTrue(_viewModel.IsUploading);
        }

        [TestMethod]
        public void SelectedFilePath_WhenSet_RaisesPropertyChanged()
        {
            var propertyChanged = false;
            _viewModel!.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(_viewModel.SelectedFilePath))
                    propertyChanged = true;
            };

            _viewModel.SelectedFilePath = @"C:\test\image.png";

            Assert.IsTrue(propertyChanged);
            Assert.AreEqual(@"C:\test\image.png", _viewModel.SelectedFilePath);
        }

        #endregion

        #region Selection Tests

        [TestMethod]
        public void SelectedEngine_InitiallyNull()
        {
            Assert.IsNull(_viewModel!.SelectedEngine);
        }

        [TestMethod]
        public void SelectedJob_InitiallyNull()
        {
            Assert.IsNull(_viewModel!.SelectedJob);
        }

        [TestMethod]
        public void SelectedFilePath_InitiallyNull()
        {
            Assert.IsNull(_viewModel!.SelectedFilePath);
        }

        [TestMethod]
        public void OutputFormat_InitiallyNull()
        {
            Assert.IsNull(_viewModel!.OutputFormat);
        }

        #endregion
    }
}