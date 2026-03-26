using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Threading.Tasks;
using VoiceStudio.App.ViewModels;
using VoiceStudio.App.Services;
using VoiceStudio.Core.Models;
using VoiceStudio.Core.Services;

namespace VoiceStudio.App.Tests.ViewModels
{
    /// <summary>
    /// Unit tests for AdvancedSettingsViewModel.
    /// Tests MVVM property change notifications and panel interface implementation.
    /// </summary>
    [TestClass]
    public class AdvancedSettingsViewModelTests : ViewModelTestBase
    {
        private Mock<IAdvancedSettingsClient>? _mockAdvancedSettingsClient;

        [TestInitialize]
        public override void TestInitialize()
        {
            base.TestInitialize();
            _mockAdvancedSettingsClient = new Mock<IAdvancedSettingsClient>();
            _mockAdvancedSettingsClient
                .Setup(x => x.GetSettingsAsync(It.IsAny<System.Threading.CancellationToken>()))
                .ReturnsAsync((AdvancedSettingsData?)null);
            _mockAdvancedSettingsClient
                .Setup(x => x.GetGpuDevicesAsync(It.IsAny<System.Threading.CancellationToken>()))
                .ReturnsAsync(new List<VoiceStudio.Core.Models.GpuDeviceInfo>());
        }

        private AdvancedSettingsViewModel CreateViewModel()
        {
            return new AdvancedSettingsViewModel(MockContext!, _mockAdvancedSettingsClient!.Object);
        }

        #region Construction and Initialization Tests

        [TestMethod]
        public void Constructor_WithValidDependencies_CreatesInstance()
        {
            // Arrange & Act
            var viewModel = CreateViewModel();

            // Assert
            Assert.IsNotNull(viewModel);
            Assert.AreEqual(VoiceStudio.Core.Panels.PanelIds.AdvancedSettings, viewModel.PanelId);
        }

        [TestMethod]
        public void Constructor_SetsDefaultValues()
        {
            // Arrange & Act
            var viewModel = CreateViewModel();

            // Assert - UI defaults
            Assert.AreEqual("Dark", viewModel.SelectedTheme);
            Assert.AreEqual("#0078D4", viewModel.AccentColor);
            Assert.AreEqual("Medium", viewModel.SelectedFontSize);
            Assert.AreEqual(1.0, viewModel.UiScale);
            Assert.IsTrue(viewModel.AnimationEnabled);
            Assert.IsFalse(viewModel.TransparencyEnabled);
            Assert.IsFalse(viewModel.CompactMode);

            // Assert - Performance defaults
            Assert.IsTrue(viewModel.CacheEnabled);
            Assert.AreEqual(512, viewModel.CacheSizeMb);
            Assert.AreEqual(4, viewModel.MaxThreads);
            Assert.IsTrue(viewModel.GpuEnabled);

            // Assert - Audio defaults
            Assert.AreEqual(44100, viewModel.DefaultSampleRate);
            Assert.AreEqual(16, viewModel.DefaultBitDepth);
            Assert.IsTrue(viewModel.DitherEnabled);
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentNullException))]
        public void Constructor_WithNullContext_ThrowsArgumentNullException()
        {
            _ = new AdvancedSettingsViewModel(null!, _mockAdvancedSettingsClient!.Object);
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentNullException))]
        public void Constructor_WithNullAdvancedSettingsClient_ThrowsArgumentNullException()
        {
            _ = new AdvancedSettingsViewModel(MockContext!, null!);
        }

        #endregion

        #region Property Tests

        [TestMethod]
        public void SelectedTheme_WhenSet_RaisesPropertyChanged()
        {
            // Arrange
            var viewModel = CreateViewModel();
            var propertyChanged = false;
            viewModel.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(viewModel.SelectedTheme))
                    propertyChanged = true;
            };

            // Act
            viewModel.SelectedTheme = "Light";

            // Assert
            Assert.IsTrue(propertyChanged);
            Assert.AreEqual("Light", viewModel.SelectedTheme);
        }

        [TestMethod]
        public void AvailableThemes_ContainsExpectedOptions()
        {
            // Arrange & Act
            var viewModel = CreateViewModel();

            // Assert
            Assert.IsNotNull(viewModel.AvailableThemes);
            CollectionAssert.Contains(viewModel.AvailableThemes, "Light");
            CollectionAssert.Contains(viewModel.AvailableThemes, "Dark");
            CollectionAssert.Contains(viewModel.AvailableThemes, "System");
        }

        [TestMethod]
        public void AccentColor_WhenSet_RaisesPropertyChanged()
        {
            // Arrange
            var viewModel = CreateViewModel();
            var propertyChanged = false;
            viewModel.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(viewModel.AccentColor))
                    propertyChanged = true;
            };

            // Act
            viewModel.AccentColor = "#FF0000";

            // Assert
            Assert.IsTrue(propertyChanged);
            Assert.AreEqual("#FF0000", viewModel.AccentColor);
        }

        [TestMethod]
        public void UiScale_WhenSet_RaisesPropertyChanged()
        {
            // Arrange
            var viewModel = CreateViewModel();
            var propertyChanged = false;
            viewModel.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(viewModel.UiScale))
                    propertyChanged = true;
            };

            // Act
            viewModel.UiScale = 1.25;

            // Assert
            Assert.IsTrue(propertyChanged);
            Assert.AreEqual(1.25, viewModel.UiScale);
        }

        [TestMethod]
        public void AnimationEnabled_WhenToggled_RaisesPropertyChanged()
        {
            // Arrange
            var viewModel = CreateViewModel();
            var propertyChanged = false;
            viewModel.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(viewModel.AnimationEnabled))
                    propertyChanged = true;
            };

            // Act
            viewModel.AnimationEnabled = false;

            // Assert
            Assert.IsTrue(propertyChanged);
            Assert.IsFalse(viewModel.AnimationEnabled);
        }

        [TestMethod]
        public void CacheEnabled_WhenToggled_RaisesPropertyChanged()
        {
            // Arrange
            var viewModel = CreateViewModel();
            var propertyChanged = false;
            viewModel.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(viewModel.CacheEnabled))
                    propertyChanged = true;
            };

            // Act
            viewModel.CacheEnabled = false;

            // Assert
            Assert.IsTrue(propertyChanged);
            Assert.IsFalse(viewModel.CacheEnabled);
        }

        [TestMethod]
        public void CacheSizeMb_WhenSet_UpdatesValue()
        {
            // Arrange
            var viewModel = CreateViewModel();

            // Act
            viewModel.CacheSizeMb = 1024;

            // Assert
            Assert.AreEqual(1024, viewModel.CacheSizeMb);
        }

        [TestMethod]
        public void GpuEnabled_WhenToggled_RaisesPropertyChanged()
        {
            // Arrange
            var viewModel = CreateViewModel();
            var propertyChanged = false;
            viewModel.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(viewModel.GpuEnabled))
                    propertyChanged = true;
            };

            // Act
            viewModel.GpuEnabled = false;

            // Assert
            Assert.IsTrue(propertyChanged);
            Assert.IsFalse(viewModel.GpuEnabled);
        }

        [TestMethod]
        public void DefaultSampleRate_WhenSet_UpdatesValue()
        {
            // Arrange
            var viewModel = CreateViewModel();

            // Act
            viewModel.DefaultSampleRate = 48000;

            // Assert
            Assert.AreEqual(48000, viewModel.DefaultSampleRate);
        }

        #endregion

        #region Panel Interface Tests

        [TestMethod]
        public void PanelId_ReturnsCorrectValue()
        {
            // Arrange & Act
            var viewModel = CreateViewModel();

            // Assert
            Assert.AreEqual(VoiceStudio.Core.Panels.PanelIds.AdvancedSettings, viewModel.PanelId);
        }

        [TestMethod]
        public void DisplayName_ReturnsNonEmptyString()
        {
            // Arrange & Act
            var viewModel = CreateViewModel();

            // Assert
            Assert.IsFalse(string.IsNullOrEmpty(viewModel.DisplayName));
        }

        [TestMethod]
        public void Region_ReturnsCenterRegion()
        {
            // Arrange & Act
            var viewModel = CreateViewModel();

            // Assert
            Assert.AreEqual(VoiceStudio.Core.Panels.PanelRegion.Center, viewModel.Region);
        }

        #endregion

        #region Error Handling Tests

        [TestMethod]
        public void ViewModel_WithValidState_DoesNotThrowOnPropertyAccess()
        {
            // Arrange
            var viewModel = CreateViewModel();

            // Act & Assert - accessing all properties should not throw
            _ = viewModel.SelectedTheme;
            _ = viewModel.AccentColor;
            _ = viewModel.UiScale;
            _ = viewModel.AnimationEnabled;
            _ = viewModel.CacheEnabled;
            _ = viewModel.GpuEnabled;
            _ = viewModel.DefaultSampleRate;
            _ = viewModel.AvailableThemes;
            _ = viewModel.AvailableFontSizes;
        }

        #endregion
    }
}