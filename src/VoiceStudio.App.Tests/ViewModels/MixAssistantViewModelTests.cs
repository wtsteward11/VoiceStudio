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
    /// Unit tests for MixAssistantViewModel.
    /// Tests cover construction, property changes, default values, and panel interface.
    /// </summary>
    [TestClass]
    public class MixAssistantViewModelTests : ViewModelTestBase
    {
        private Mock<IBackendClient>? _mockBackendClient;
        private Mock<IProjectsClient>? _mockProjectsClient;
        private MixAssistantViewModel? _viewModel;

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

        private MixAssistantViewModel CreateViewModel()
        {
            return new MixAssistantViewModel(MockContext!, _mockBackendClient!.Object, _mockProjectsClient!.Object);
        }

        #region Construction and Initialization Tests

        [TestMethod]
        public void Constructor_WithValidDependencies_CreatesInstance()
        {
            Assert.IsNotNull(_viewModel);
            Assert.IsNotNull(_viewModel.AvailableProjects);
            Assert.IsNotNull(_viewModel.Suggestions);
            Assert.IsNotNull(_viewModel.AvailableCategories);
            Assert.IsNotNull(_viewModel.AvailablePriorities);
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentNullException))]
        public void Constructor_WithNullContext_ThrowsArgumentNullException()
        {
            _ = new MixAssistantViewModel(null!, _mockBackendClient!.Object, _mockProjectsClient!.Object);
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentNullException))]
        public void Constructor_WithNullBackendClient_ThrowsArgumentNullException()
        {
            _ = new MixAssistantViewModel(MockContext!, null!, _mockProjectsClient!.Object);
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentNullException))]
        public void Constructor_WithNullProjectsClient_ThrowsArgumentNullException()
        {
            _ = new MixAssistantViewModel(MockContext!, _mockBackendClient!.Object, null!);
        }

        #endregion

        #region Panel Interface Tests

        [TestMethod]
        public void PanelId_ReturnsMixAssistant()
        {
            Assert.AreEqual("mix-assistant", _viewModel!.PanelId);
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
        public void AnalyzeLevels_DefaultsToTrue()
        {
            Assert.IsTrue(_viewModel!.AnalyzeLevels);
        }

        [TestMethod]
        public void AnalyzeFrequency_DefaultsToTrue()
        {
            Assert.IsTrue(_viewModel!.AnalyzeFrequency);
        }

        [TestMethod]
        public void AnalyzeStereo_DefaultsToTrue()
        {
            Assert.IsTrue(_viewModel!.AnalyzeStereo);
        }

        [TestMethod]
        public void AnalyzeDynamics_DefaultsToTrue()
        {
            Assert.IsTrue(_viewModel!.AnalyzeDynamics);
        }

        [TestMethod]
        public void PresetName_DefaultsToEmpty()
        {
            Assert.AreEqual(string.Empty, _viewModel!.PresetName);
        }

        [TestMethod]
        public void AvailableCategories_ContainsExpectedValues()
        {
            Assert.IsTrue(_viewModel!.AvailableCategories.Contains("all"));
            Assert.IsTrue(_viewModel.AvailableCategories.Contains("levels"));
            Assert.IsTrue(_viewModel.AvailableCategories.Contains("frequency"));
            Assert.IsTrue(_viewModel.AvailableCategories.Contains("stereo"));
            Assert.IsTrue(_viewModel.AvailableCategories.Contains("dynamics"));
        }

        [TestMethod]
        public void AvailablePriorities_ContainsExpectedValues()
        {
            Assert.IsTrue(_viewModel!.AvailablePriorities.Contains("all"));
            Assert.IsTrue(_viewModel.AvailablePriorities.Contains("high"));
            Assert.IsTrue(_viewModel.AvailablePriorities.Contains("medium"));
            Assert.IsTrue(_viewModel.AvailablePriorities.Contains("low"));
        }

        #endregion

        #region Property Change Tests

        [TestMethod]
        public void AnalyzeLevels_WhenSet_RaisesPropertyChanged()
        {
            var propertyChanged = false;
            _viewModel!.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(_viewModel.AnalyzeLevels))
                    propertyChanged = true;
            };

            _viewModel.AnalyzeLevels = false;

            Assert.IsTrue(propertyChanged);
            Assert.IsFalse(_viewModel.AnalyzeLevels);
        }

        [TestMethod]
        public void AnalyzeFrequency_WhenSet_RaisesPropertyChanged()
        {
            var propertyChanged = false;
            _viewModel!.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(_viewModel.AnalyzeFrequency))
                    propertyChanged = true;
            };

            _viewModel.AnalyzeFrequency = false;

            Assert.IsTrue(propertyChanged);
            Assert.IsFalse(_viewModel.AnalyzeFrequency);
        }

        [TestMethod]
        public void AnalyzeStereo_WhenSet_RaisesPropertyChanged()
        {
            var propertyChanged = false;
            _viewModel!.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(_viewModel.AnalyzeStereo))
                    propertyChanged = true;
            };

            _viewModel.AnalyzeStereo = false;

            Assert.IsTrue(propertyChanged);
            Assert.IsFalse(_viewModel.AnalyzeStereo);
        }

        [TestMethod]
        public void AnalyzeDynamics_WhenSet_RaisesPropertyChanged()
        {
            var propertyChanged = false;
            _viewModel!.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(_viewModel.AnalyzeDynamics))
                    propertyChanged = true;
            };

            _viewModel.AnalyzeDynamics = false;

            Assert.IsTrue(propertyChanged);
            Assert.IsFalse(_viewModel.AnalyzeDynamics);
        }

        [TestMethod]
        public void PresetName_WhenSet_RaisesPropertyChanged()
        {
            var propertyChanged = false;
            _viewModel!.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(_viewModel.PresetName))
                    propertyChanged = true;
            };

            _viewModel.PresetName = "My Preset";

            Assert.IsTrue(propertyChanged);
            Assert.AreEqual("My Preset", _viewModel.PresetName);
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

            _viewModel.SelectedCategory = "levels";

            Assert.IsTrue(propertyChanged);
            Assert.AreEqual("levels", _viewModel.SelectedCategory);
        }

        [TestMethod]
        public void SelectedPriority_WhenSet_RaisesPropertyChanged()
        {
            var propertyChanged = false;
            _viewModel!.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(_viewModel.SelectedPriority))
                    propertyChanged = true;
            };

            _viewModel.SelectedPriority = "high";

            Assert.IsTrue(propertyChanged);
            Assert.AreEqual("high", _viewModel.SelectedPriority);
        }

        [TestMethod]
        public void SelectedGenre_WhenSet_RaisesPropertyChanged()
        {
            var propertyChanged = false;
            _viewModel!.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(_viewModel.SelectedGenre))
                    propertyChanged = true;
            };

            _viewModel.SelectedGenre = "rock";

            Assert.IsTrue(propertyChanged);
            Assert.AreEqual("rock", _viewModel.SelectedGenre);
        }

        #endregion

        #region Selection Tests

        [TestMethod]
        public void SelectedProject_InitiallyNull()
        {
            Assert.IsNull(_viewModel!.SelectedProject);
        }

        [TestMethod]
        public void SelectedSuggestion_InitiallyNull()
        {
            Assert.IsNull(_viewModel!.SelectedSuggestion);
        }

        [TestMethod]
        public void SelectedProjectId_InitiallyNull()
        {
            Assert.IsNull(_viewModel!.SelectedProjectId);
        }

        #endregion
    }
}