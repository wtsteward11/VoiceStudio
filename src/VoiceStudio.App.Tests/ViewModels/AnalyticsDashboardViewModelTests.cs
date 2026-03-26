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
    /// Unit tests for AnalyticsDashboardViewModel.
    /// Source: AnalyticsDashboardViewModel.cs
    /// </summary>
    [TestClass]
    public class AnalyticsDashboardViewModelTests : ViewModelTestBase
    {
        private Mock<IAnalyticsDashboardClient>? _mockAnalyticsClient;

        [TestInitialize]
        public override void TestInitialize()
        {
            base.TestInitialize();
            _mockAnalyticsClient = new Mock<IAnalyticsDashboardClient>();
        }

        private AnalyticsDashboardViewModel CreateViewModel()
        {
            return new AnalyticsDashboardViewModel(MockContext!, _mockAnalyticsClient!.Object);
        }

        #region Construction and Initialization Tests

        [TestMethod]
        public void Constructor_WithValidDependencies_CreatesInstance()
        {
            // Arrange & Act
            var viewModel = CreateViewModel();

            // Assert
            Assert.IsNotNull(viewModel);
            Assert.AreEqual("analytics-dashboard", viewModel.PanelId);
            Assert.IsNotNull(viewModel.AvailableCategories);
            Assert.IsNotNull(viewModel.CategoryMetrics);
        }

        [TestMethod]
        public void Constructor_WithNullContext_ThrowsArgumentNullException()
        {
            Assert.ThrowsException<ArgumentNullException>(() =>
                new AnalyticsDashboardViewModel(null!, _mockAnalyticsClient!.Object));
        }

        [TestMethod]
        public void Constructor_WithNullBackendClient_ThrowsArgumentNullException()
        {
            Assert.ThrowsException<ArgumentNullException>(() =>
                new AnalyticsDashboardViewModel(MockContext!, null!));
        }

        [TestMethod]
        public void Constructor_InitializesDefaultTimeRanges()
        {
            // Arrange & Act
            var viewModel = CreateViewModel();

            // Assert
            Assert.IsNotNull(viewModel.AvailableTimeRanges);
            Assert.IsTrue(viewModel.AvailableTimeRanges.Count >= 4);
            CollectionAssert.Contains(viewModel.AvailableTimeRanges, "7d");
            CollectionAssert.Contains(viewModel.AvailableTimeRanges, "30d");
            CollectionAssert.Contains(viewModel.AvailableTimeRanges, "90d");
        }

        [TestMethod]
        public void Constructor_InitializesDefaultIntervals()
        {
            // Arrange & Act
            var viewModel = CreateViewModel();

            // Assert
            Assert.IsNotNull(viewModel.AvailableIntervals);
            Assert.IsTrue(viewModel.AvailableIntervals.Count >= 3);
            CollectionAssert.Contains(viewModel.AvailableIntervals, "hour");
            CollectionAssert.Contains(viewModel.AvailableIntervals, "day");
            CollectionAssert.Contains(viewModel.AvailableIntervals, "week");
        }

        #endregion

        #region Property Tests

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
            viewModel.AvailableCategories = new ObservableCollection<string> { "Usage", "Performance" };

            // Assert
            Assert.IsTrue(propertyChanged);
        }

        [TestMethod]
        public void CategoryMetrics_WhenSet_RaisesPropertyChanged()
        {
            // Arrange
            var viewModel = CreateViewModel();
            var propertyChanged = false;
            viewModel.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(viewModel.CategoryMetrics))
                    propertyChanged = true;
            };

            // Act
            viewModel.CategoryMetrics = new ObservableCollection<AnalyticsMetricItem>();

            // Assert
            Assert.IsTrue(propertyChanged);
        }

        [TestMethod]
        public void SelectedTimeRange_WhenSet_RaisesPropertyChanged()
        {
            // Arrange
            var viewModel = CreateViewModel();
            var propertyChanged = false;
            viewModel.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(viewModel.SelectedTimeRange))
                    propertyChanged = true;
            };

            // Act
            viewModel.SelectedTimeRange = "7d";

            // Assert
            Assert.IsTrue(propertyChanged);
            Assert.AreEqual("7d", viewModel.SelectedTimeRange);
        }

        [TestMethod]
        public void AvailableTimeRanges_WhenSet_RaisesPropertyChanged()
        {
            // Arrange
            var viewModel = CreateViewModel();
            var propertyChanged = false;
            viewModel.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(viewModel.AvailableTimeRanges))
                    propertyChanged = true;
            };

            // Act
            viewModel.AvailableTimeRanges = new ObservableCollection<string> { "1d", "7d" };

            // Assert
            Assert.IsTrue(propertyChanged);
        }

        [TestMethod]
        public void SelectedInterval_WhenSet_RaisesPropertyChanged()
        {
            // Arrange
            var viewModel = CreateViewModel();
            var propertyChanged = false;
            viewModel.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(viewModel.SelectedInterval))
                    propertyChanged = true;
            };

            // Act
            viewModel.SelectedInterval = "hour";

            // Assert
            Assert.IsTrue(propertyChanged);
            Assert.AreEqual("hour", viewModel.SelectedInterval);
        }

        [TestMethod]
        public void AvailableIntervals_WhenSet_RaisesPropertyChanged()
        {
            // Arrange
            var viewModel = CreateViewModel();
            var propertyChanged = false;
            viewModel.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(viewModel.AvailableIntervals))
                    propertyChanged = true;
            };

            // Act
            viewModel.AvailableIntervals = new ObservableCollection<string> { "minute", "hour" };

            // Assert
            Assert.IsTrue(propertyChanged);
        }

        [TestMethod]
        public void ShowStatisticalAnalysis_WhenSet_RaisesPropertyChanged()
        {
            // Arrange
            var viewModel = CreateViewModel();
            var propertyChanged = false;
            viewModel.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(viewModel.ShowStatisticalAnalysis))
                    propertyChanged = true;
            };

            // Act
            viewModel.ShowStatisticalAnalysis = true;

            // Assert
            Assert.IsTrue(propertyChanged);
            Assert.IsTrue(viewModel.ShowStatisticalAnalysis);
        }

        [TestMethod]
        public void SelectedCategory_WhenSet_RaisesPropertyChanged()
        {
            // Arrange
            var viewModel = CreateViewModel();
            var propertyChanged = false;
            viewModel.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(viewModel.SelectedCategory))
                    propertyChanged = true;
            };

            // Act
            viewModel.SelectedCategory = "Performance";

            // Assert
            Assert.IsTrue(propertyChanged);
            Assert.AreEqual("Performance", viewModel.SelectedCategory);
        }

        #endregion

        #region Panel Interface Tests

        [TestMethod]
        public void PanelId_ReturnsCorrectValue()
        {
            var viewModel = CreateViewModel();
            Assert.AreEqual("analytics-dashboard", viewModel.PanelId);
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

            // Act - setting null or empty values should not throw
            viewModel.SelectedCategory = null;
            viewModel.SelectedTimeRange = string.Empty;
            viewModel.SelectedInterval = string.Empty;

            // Assert
            Assert.IsNotNull(viewModel);
            Assert.IsNotNull(viewModel.AvailableCategories);
            Assert.IsNotNull(viewModel.CategoryMetrics);
        }

        #endregion
    }
}