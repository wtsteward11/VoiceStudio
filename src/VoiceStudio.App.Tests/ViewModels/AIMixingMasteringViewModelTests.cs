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
    /// Unit tests for AIMixingMasteringViewModel.
    /// Source: AIMixingMasteringViewModel.cs
    /// </summary>
    [TestClass]
    public class AIMixingMasteringViewModelTests : ViewModelTestBase
    {
        private Mock<IAIMixingClient>? _mockAIMixingClient;

        [TestInitialize]
        public override void TestInitialize()
        {
            base.TestInitialize();
            _mockAIMixingClient = new Mock<IAIMixingClient>();
        }

        private AIMixingMasteringViewModel CreateViewModel()
        {
            return new AIMixingMasteringViewModel(MockContext!, _mockAIMixingClient!.Object);
        }

        #region Construction and Initialization Tests

        [TestMethod]
        public void Constructor_WithValidDependencies_CreatesInstance()
        {
            // Arrange & Act
            var viewModel = CreateViewModel();

            // Assert
            Assert.IsNotNull(viewModel);
            Assert.AreEqual(PanelIds.AIMixingMastering, viewModel.PanelId);
            Assert.IsNotNull(viewModel.AvailableModes);
            Assert.IsNotNull(viewModel.Suggestions);
        }

        [TestMethod]
        public void Constructor_WithNullContext_ThrowsArgumentNullException()
        {
            Assert.ThrowsException<ArgumentNullException>(() =>
                new AIMixingMasteringViewModel(null!, _mockAIMixingClient!.Object));
        }

        [TestMethod]
        public void Constructor_WithNullAIMixingClient_ThrowsArgumentNullException()
        {
            Assert.ThrowsException<ArgumentNullException>(() =>
                new AIMixingMasteringViewModel(MockContext!, null!));
        }

        [TestMethod]
        public void Constructor_InitializesDefaultModes()
        {
            // Arrange & Act
            var viewModel = CreateViewModel();

            // Assert
            Assert.IsNotNull(viewModel.AvailableModes);
            Assert.IsTrue(viewModel.AvailableModes.Count >= 3);
            CollectionAssert.Contains(viewModel.AvailableModes, "Balance Mix");
            CollectionAssert.Contains(viewModel.AvailableModes, "Master for Podcast");
        }

        [TestMethod]
        public void Constructor_InitializesDefaultFormats()
        {
            // Arrange & Act
            var viewModel = CreateViewModel();

            // Assert
            Assert.IsNotNull(viewModel.AvailableFormats);
            Assert.IsTrue(viewModel.AvailableFormats.Count >= 3);
            CollectionAssert.Contains(viewModel.AvailableFormats, "podcast");
            CollectionAssert.Contains(viewModel.AvailableFormats, "broadcast");
        }

        #endregion

        #region Property Tests

        [TestMethod]
        public void SelectedMode_WhenSet_RaisesPropertyChanged()
        {
            // Arrange
            var viewModel = CreateViewModel();
            var propertyChanged = false;
            viewModel.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(viewModel.SelectedMode))
                    propertyChanged = true;
            };

            // Act
            viewModel.SelectedMode = "Master for Podcast";

            // Assert
            Assert.IsTrue(propertyChanged);
            Assert.AreEqual("Master for Podcast", viewModel.SelectedMode);
        }

        [TestMethod]
        public void AvailableModes_WhenSet_RaisesPropertyChanged()
        {
            // Arrange
            var viewModel = CreateViewModel();
            var propertyChanged = false;
            viewModel.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(viewModel.AvailableModes))
                    propertyChanged = true;
            };

            // Act
            viewModel.AvailableModes = new ObservableCollection<string> { "Mode1", "Mode2" };

            // Assert
            Assert.IsTrue(propertyChanged);
        }

        [TestMethod]
        public void IsAnalyzing_WhenSet_RaisesPropertyChanged()
        {
            // Arrange
            var viewModel = CreateViewModel();
            var propertyChanged = false;
            viewModel.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(viewModel.IsAnalyzing))
                    propertyChanged = true;
            };

            // Act
            viewModel.IsAnalyzing = true;

            // Assert
            Assert.IsTrue(propertyChanged);
            Assert.IsTrue(viewModel.IsAnalyzing);
        }

        [TestMethod]
        public void AnalysisProgress_WhenSet_RaisesPropertyChanged()
        {
            // Arrange
            var viewModel = CreateViewModel();
            var propertyChanged = false;
            viewModel.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(viewModel.AnalysisProgress))
                    propertyChanged = true;
            };

            // Act
            viewModel.AnalysisProgress = 0.5f;

            // Assert
            Assert.IsTrue(propertyChanged);
            Assert.AreEqual(0.5f, viewModel.AnalysisProgress);
        }

        [TestMethod]
        public void Suggestions_WhenSet_RaisesPropertyChanged()
        {
            // Arrange
            var viewModel = CreateViewModel();
            var propertyChanged = false;
            viewModel.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(viewModel.Suggestions))
                    propertyChanged = true;
            };

            // Act
            viewModel.Suggestions = new ObservableCollection<AIMixingMixSuggestionItem>();

            // Assert
            Assert.IsTrue(propertyChanged);
        }

        [TestMethod]
        public void ShowBeforeAfter_WhenSet_RaisesPropertyChanged()
        {
            // Arrange
            var viewModel = CreateViewModel();
            var propertyChanged = false;
            viewModel.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(viewModel.ShowBeforeAfter))
                    propertyChanged = true;
            };

            // Act
            viewModel.ShowBeforeAfter = true;

            // Assert
            Assert.IsTrue(propertyChanged);
            Assert.IsTrue(viewModel.ShowBeforeAfter);
        }

        [TestMethod]
        public void SelectedTargetFormat_WhenSet_RaisesPropertyChanged()
        {
            // Arrange
            var viewModel = CreateViewModel();
            var propertyChanged = false;
            viewModel.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(viewModel.SelectedTargetFormat))
                    propertyChanged = true;
            };

            // Act
            viewModel.SelectedTargetFormat = "broadcast";

            // Assert
            Assert.IsTrue(propertyChanged);
            Assert.AreEqual("broadcast", viewModel.SelectedTargetFormat);
        }

        [TestMethod]
        public void AvailableFormats_WhenSet_RaisesPropertyChanged()
        {
            // Arrange
            var viewModel = CreateViewModel();
            var propertyChanged = false;
            viewModel.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(viewModel.AvailableFormats))
                    propertyChanged = true;
            };

            // Act
            viewModel.AvailableFormats = new ObservableCollection<string> { "format1", "format2" };

            // Assert
            Assert.IsTrue(propertyChanged);
        }

        #endregion

        #region Panel Interface Tests

        [TestMethod]
        public void PanelId_ReturnsCorrectValue()
        {
            var viewModel = CreateViewModel();
            Assert.AreEqual(PanelIds.AIMixingMastering, viewModel.PanelId);
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

            // Act - setting null or empty values should not throw
            viewModel.ProjectId = null;
            viewModel.SelectedMode = string.Empty;
            viewModel.SelectedSuggestion = null;

            // Assert
            Assert.IsNotNull(viewModel);
            Assert.IsNotNull(viewModel.Suggestions);
        }

        #endregion
    }
}