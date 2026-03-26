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
    /// Unit tests for APIKeyManagerViewModel.
    /// Source: APIKeyManagerViewModel.cs
    /// </summary>
    [TestClass]
    public class APIKeyManagerViewModelTests : ViewModelTestBase
    {
        private Mock<IAPIKeyManagerClient>? _mockApiKeyManagerClient;

        [TestInitialize]
        public override void TestInitialize()
        {
            base.TestInitialize();
            _mockApiKeyManagerClient = new Mock<IAPIKeyManagerClient>();
            _mockApiKeyManagerClient
                .Setup(x => x.GetKeysAsync(It.IsAny<System.Threading.CancellationToken>()))
                .ReturnsAsync(Array.Empty<APIKeyResponse>());
            _mockApiKeyManagerClient
                .Setup(x => x.GetSupportedServicesAsync(It.IsAny<System.Threading.CancellationToken>()))
                .ReturnsAsync(Array.Empty<string>());
        }

        private APIKeyManagerViewModel CreateViewModel()
        {
            return new APIKeyManagerViewModel(MockContext!, _mockApiKeyManagerClient!.Object);
        }

        #region Construction and Initialization Tests

        [TestMethod]
        public void Constructor_WithValidDependencies_CreatesInstance()
        {
            // Arrange & Act
            var viewModel = CreateViewModel();

            // Assert
            Assert.IsNotNull(viewModel);
            Assert.AreEqual(PanelIds.APIKeyManager, viewModel.PanelId);
            Assert.IsNotNull(viewModel.ApiKeys);
            Assert.IsNotNull(viewModel.SupportedServices);
        }

        [TestMethod]
        public void Constructor_WithNullContext_ThrowsArgumentNullException()
        {
            Assert.ThrowsException<ArgumentNullException>(() =>
                new APIKeyManagerViewModel(null!, _mockApiKeyManagerClient!.Object));
        }

        [TestMethod]
        public void Constructor_WithNullApiKeyManagerClient_ThrowsArgumentNullException()
        {
            Assert.ThrowsException<ArgumentNullException>(() =>
                new APIKeyManagerViewModel(MockContext!, null!));
        }

        [TestMethod]
        public void Constructor_InitializesEmptyCollections()
        {
            // Arrange & Act
            var viewModel = CreateViewModel();

            // Assert
            Assert.IsNotNull(viewModel.ApiKeys);
            Assert.AreEqual(0, viewModel.ApiKeys.Count);
            Assert.IsNotNull(viewModel.SupportedServices);
        }

        #endregion

        #region Property Tests

        [TestMethod]
        public void ApiKeys_WhenSet_RaisesPropertyChanged()
        {
            // Arrange
            var viewModel = CreateViewModel();
            var propertyChanged = false;
            viewModel.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(viewModel.ApiKeys))
                    propertyChanged = true;
            };

            // Act
            viewModel.ApiKeys = new ObservableCollection<APIKeyItem>();

            // Assert
            Assert.IsTrue(propertyChanged);
        }

        [TestMethod]
        public void SupportedServices_WhenSet_RaisesPropertyChanged()
        {
            // Arrange
            var viewModel = CreateViewModel();
            var propertyChanged = false;
            viewModel.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(viewModel.SupportedServices))
                    propertyChanged = true;
            };

            // Act
            viewModel.SupportedServices = new ObservableCollection<string> { "OpenAI", "ElevenLabs" };

            // Assert
            Assert.IsTrue(propertyChanged);
        }

        [TestMethod]
        public void IsCreatingKey_WhenSet_RaisesPropertyChanged()
        {
            // Arrange
            var viewModel = CreateViewModel();
            var propertyChanged = false;
            viewModel.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(viewModel.IsCreatingKey))
                    propertyChanged = true;
            };

            // Act
            viewModel.IsCreatingKey = true;

            // Assert
            Assert.IsTrue(propertyChanged);
            Assert.IsTrue(viewModel.IsCreatingKey);
        }

        [TestMethod]
        public void ShowKeyValue_WhenSet_RaisesPropertyChanged()
        {
            // Arrange
            var viewModel = CreateViewModel();
            var propertyChanged = false;
            viewModel.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(viewModel.ShowKeyValue))
                    propertyChanged = true;
            };

            // Act
            viewModel.ShowKeyValue = true;

            // Assert
            Assert.IsTrue(propertyChanged);
            Assert.IsTrue(viewModel.ShowKeyValue);
        }

        [TestMethod]
        public void SelectedKey_WhenSet_RaisesPropertyChanged()
        {
            // Arrange
            var viewModel = CreateViewModel();
            var propertyChanged = false;
            viewModel.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(viewModel.SelectedKey))
                    propertyChanged = true;
            };

            // Act
            var now = DateTime.UtcNow.ToString("O");
            viewModel.SelectedKey = new APIKeyItem(
                keyId: "key-1",
                serviceName: "TestService",
                keyValueMasked: "****-1234",
                description: null,
                createdAt: now,
                lastUsed: null,
                isActive: true,
                usageCount: 0);

            // Assert
            Assert.IsTrue(propertyChanged);
        }

        [TestMethod]
        public void NewServiceName_WhenSet_RaisesPropertyChanged()
        {
            // Arrange
            var viewModel = CreateViewModel();
            var propertyChanged = false;
            viewModel.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(viewModel.NewServiceName))
                    propertyChanged = true;
            };

            // Act
            viewModel.NewServiceName = "OpenAI";

            // Assert
            Assert.IsTrue(propertyChanged);
            Assert.AreEqual("OpenAI", viewModel.NewServiceName);
        }

        [TestMethod]
        public void NewKeyValue_WhenSet_RaisesPropertyChanged()
        {
            // Arrange
            var viewModel = CreateViewModel();
            var propertyChanged = false;
            viewModel.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(viewModel.NewKeyValue))
                    propertyChanged = true;
            };

            // Act
            viewModel.NewKeyValue = "sk-test123";

            // Assert
            Assert.IsTrue(propertyChanged);
            Assert.AreEqual("sk-test123", viewModel.NewKeyValue);
        }

        #endregion

        #region Panel Interface Tests

        [TestMethod]
        public void PanelId_ReturnsCorrectValue()
        {
            var viewModel = CreateViewModel();
            Assert.AreEqual(PanelIds.APIKeyManager, viewModel.PanelId);
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

            // Act - setting null values should not throw
            viewModel.NewServiceName = null;
            viewModel.NewKeyValue = null;
            viewModel.NewDescription = null;
            viewModel.SelectedKey = null;

            // Assert
            Assert.IsNotNull(viewModel);
            Assert.IsNotNull(viewModel.ApiKeys);
        }

        #endregion
    }
}