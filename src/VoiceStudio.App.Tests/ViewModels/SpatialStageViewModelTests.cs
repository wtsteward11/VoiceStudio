using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using VoiceStudio.App.ViewModels;
using VoiceStudio.App.Services;
using VoiceStudio.Core.Models;
using VoiceStudio.Core.Services;

namespace VoiceStudio.App.Tests.ViewModels
{
    /// <summary>
    /// Unit tests for SpatialStageViewModel.
    /// Tests cover construction, property changes, default values, commands, and panel interface.
    /// </summary>
    [TestClass]
    public class SpatialStageViewModelTests : ViewModelTestBase
    {
        private Mock<IBackendClient>? _mockBackendClient;
        private Mock<IProjectsClient>? _mockProjectsClient;
        private SpatialStageViewModel? _viewModel;

        [TestInitialize]
        public override void TestInitialize()
        {
            base.TestInitialize();
            _mockBackendClient = new Mock<IBackendClient>();
            _mockProjectsClient = new Mock<IProjectsClient>();
            _mockProjectsClient
                .Setup(x => x.GetProjectsAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<Project>());
            _viewModel = CreateViewModel();
        }

        [TestCleanup]
        public override void TestCleanup()
        {
            _viewModel = null;
            _mockBackendClient = null;
            _mockProjectsClient = null;
            base.TestCleanup();
        }

        private SpatialStageViewModel CreateViewModel()
        {
            return new SpatialStageViewModel(MockContext!, _mockBackendClient!.Object, _mockProjectsClient!.Object);
        }

        #region Construction and Initialization Tests

        [TestMethod]
        public void Constructor_WithValidDependencies_CreatesInstance()
        {
            Assert.IsNotNull(_viewModel);
            Assert.IsNotNull(_viewModel.Configs);
            Assert.IsNotNull(_viewModel.AvailableAudioIds);
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentNullException))]
        public void Constructor_WithNullContext_ThrowsArgumentNullException()
        {
            _ = new SpatialStageViewModel(null!, _mockBackendClient!.Object, _mockProjectsClient!.Object);
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentNullException))]
        public void Constructor_WithNullBackendClient_ThrowsArgumentNullException()
        {
            _ = new SpatialStageViewModel(MockContext!, null!, _mockProjectsClient!.Object);
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentNullException))]
        public void Constructor_WithNullProjectsClient_ThrowsArgumentNullException()
        {
            _ = new SpatialStageViewModel(MockContext!, _mockBackendClient!.Object, null!);
        }

        #endregion

        #region Panel Interface Tests

        [TestMethod]
        public void PanelId_ReturnsSpatialStage()
        {
            Assert.AreEqual("spatial-stage", _viewModel!.PanelId);
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
        public void ConfigName_DefaultsToEmpty()
        {
            Assert.AreEqual(string.Empty, _viewModel!.ConfigName);
        }

        [TestMethod]
        public void PositionX_DefaultsToZero()
        {
            Assert.AreEqual(0.0, _viewModel!.PositionX);
        }

        [TestMethod]
        public void PositionY_DefaultsToZero()
        {
            Assert.AreEqual(0.0, _viewModel!.PositionY);
        }

        [TestMethod]
        public void PositionZ_DefaultsToZero()
        {
            Assert.AreEqual(0.0, _viewModel!.PositionZ);
        }

        [TestMethod]
        public void Distance_DefaultsToOne()
        {
            Assert.AreEqual(1.0, _viewModel!.Distance);
        }

        [TestMethod]
        public void RoomSize_DefaultsToOne()
        {
            Assert.AreEqual(1.0, _viewModel!.RoomSize);
        }

        [TestMethod]
        public void ReverbAmount_DefaultsToZero()
        {
            Assert.AreEqual(0.0, _viewModel!.ReverbAmount);
        }

        [TestMethod]
        public void Occlusion_DefaultsToZero()
        {
            Assert.AreEqual(0.0, _viewModel!.Occlusion);
        }

        [TestMethod]
        public void EnableDoppler_DefaultsToFalse()
        {
            Assert.IsFalse(_viewModel!.EnableDoppler);
        }

        [TestMethod]
        public void EnableHrtf_DefaultsToTrue()
        {
            Assert.IsTrue(_viewModel!.EnableHrtf);
        }

        #endregion

        #region Property Change Tests

        [TestMethod]
        public void ConfigName_WhenSet_RaisesPropertyChanged()
        {
            var propertyChanged = false;
            _viewModel!.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(_viewModel.ConfigName))
                    propertyChanged = true;
            };

            _viewModel.ConfigName = "Test Config";

            Assert.IsTrue(propertyChanged);
            Assert.AreEqual("Test Config", _viewModel.ConfigName);
        }

        [TestMethod]
        public void PositionX_WhenSet_RaisesPropertyChanged()
        {
            var propertyChanged = false;
            _viewModel!.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(_viewModel.PositionX))
                    propertyChanged = true;
            };

            _viewModel.PositionX = 5.0;

            Assert.IsTrue(propertyChanged);
            Assert.AreEqual(5.0, _viewModel.PositionX);
        }

        [TestMethod]
        public void PositionY_WhenSet_RaisesPropertyChanged()
        {
            var propertyChanged = false;
            _viewModel!.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(_viewModel.PositionY))
                    propertyChanged = true;
            };

            _viewModel.PositionY = 3.5;

            Assert.IsTrue(propertyChanged);
            Assert.AreEqual(3.5, _viewModel.PositionY);
        }

        [TestMethod]
        public void PositionZ_WhenSet_RaisesPropertyChanged()
        {
            var propertyChanged = false;
            _viewModel!.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(_viewModel.PositionZ))
                    propertyChanged = true;
            };

            _viewModel.PositionZ = -2.0;

            Assert.IsTrue(propertyChanged);
            Assert.AreEqual(-2.0, _viewModel.PositionZ);
        }

        [TestMethod]
        public void Distance_WhenSet_RaisesPropertyChanged()
        {
            var propertyChanged = false;
            _viewModel!.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(_viewModel.Distance))
                    propertyChanged = true;
            };

            _viewModel.Distance = 10.0;

            Assert.IsTrue(propertyChanged);
            Assert.AreEqual(10.0, _viewModel.Distance);
        }

        [TestMethod]
        public void RoomSize_WhenSet_RaisesPropertyChanged()
        {
            var propertyChanged = false;
            _viewModel!.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(_viewModel.RoomSize))
                    propertyChanged = true;
            };

            _viewModel.RoomSize = 2.5;

            Assert.IsTrue(propertyChanged);
            Assert.AreEqual(2.5, _viewModel.RoomSize);
        }

        [TestMethod]
        public void ReverbAmount_WhenSet_RaisesPropertyChanged()
        {
            var propertyChanged = false;
            _viewModel!.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(_viewModel.ReverbAmount))
                    propertyChanged = true;
            };

            _viewModel.ReverbAmount = 0.75;

            Assert.IsTrue(propertyChanged);
            Assert.AreEqual(0.75, _viewModel.ReverbAmount);
        }

        [TestMethod]
        public void Occlusion_WhenSet_RaisesPropertyChanged()
        {
            var propertyChanged = false;
            _viewModel!.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(_viewModel.Occlusion))
                    propertyChanged = true;
            };

            _viewModel.Occlusion = 0.5;

            Assert.IsTrue(propertyChanged);
            Assert.AreEqual(0.5, _viewModel.Occlusion);
        }

        [TestMethod]
        public void EnableDoppler_WhenSet_RaisesPropertyChanged()
        {
            var propertyChanged = false;
            _viewModel!.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(_viewModel.EnableDoppler))
                    propertyChanged = true;
            };

            _viewModel.EnableDoppler = true;

            Assert.IsTrue(propertyChanged);
            Assert.IsTrue(_viewModel.EnableDoppler);
        }

        [TestMethod]
        public void EnableHrtf_WhenSet_RaisesPropertyChanged()
        {
            var propertyChanged = false;
            _viewModel!.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(_viewModel.EnableHrtf))
                    propertyChanged = true;
            };

            _viewModel.EnableHrtf = false;

            Assert.IsTrue(propertyChanged);
            Assert.IsFalse(_viewModel.EnableHrtf);
        }

        #endregion

        #region Command Existence Tests

        [TestMethod]
        public void LoadConfigsCommand_IsNotNull()
        {
            Assert.IsNotNull(_viewModel!.LoadConfigsCommand);
        }

        [TestMethod]
        public void CreateConfigCommand_IsNotNull()
        {
            Assert.IsNotNull(_viewModel!.CreateConfigCommand);
        }

        [TestMethod]
        public void UpdateConfigCommand_IsNotNull()
        {
            Assert.IsNotNull(_viewModel!.UpdateConfigCommand);
        }

        [TestMethod]
        public void DeleteConfigCommand_IsNotNull()
        {
            Assert.IsNotNull(_viewModel!.DeleteConfigCommand);
        }

        [TestMethod]
        public void ApplySpatialCommand_IsNotNull()
        {
            Assert.IsNotNull(_viewModel!.ApplySpatialCommand);
        }

        [TestMethod]
        public void PreviewSpatialCommand_IsNotNull()
        {
            Assert.IsNotNull(_viewModel!.PreviewSpatialCommand);
        }

        [TestMethod]
        public void RefreshCommand_IsNotNull()
        {
            Assert.IsNotNull(_viewModel!.RefreshCommand);
        }

        #endregion

        #region Selection Tests

        [TestMethod]
        public void SelectedConfig_InitiallyNull()
        {
            Assert.IsNull(_viewModel!.SelectedConfig);
        }

        [TestMethod]
        public void SelectedAudioId_InitiallyNull()
        {
            Assert.IsNull(_viewModel!.SelectedAudioId);
        }

        #endregion
    }
}