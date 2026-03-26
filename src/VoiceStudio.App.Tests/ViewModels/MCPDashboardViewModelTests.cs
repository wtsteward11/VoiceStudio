using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using VoiceStudio.App.ViewModels;
using VoiceStudio.App.Services;
using VoiceStudio.Core.Models;
using VoiceStudio.Core.Panels;
using VoiceStudio.Core.Services;

namespace VoiceStudio.App.Tests.ViewModels
{
    /// <summary>
    /// Unit tests for MCPDashboardViewModel.
    /// Source: MCPDashboardViewModel.cs
    /// </summary>
    [TestClass]
    public class MCPDashboardViewModelTests : ViewModelTestBase
    {
        private Mock<IMCPDashboardClient>? _mockClient;

        [TestInitialize]
        public override void TestInitialize()
        {
            base.TestInitialize();
            _mockClient = new Mock<IMCPDashboardClient>();
        }

        private MCPDashboardViewModel CreateViewModel()
        {
            return new MCPDashboardViewModel(MockContext!, _mockClient!.Object);
        }

        #region Construction and Initialization Tests

        [TestMethod]
        public void Constructor_WithValidDependencies_CreatesInstance()
        {
            // Arrange & Act
            var viewModel = CreateViewModel();

            // Assert
            Assert.IsNotNull(viewModel);
            Assert.AreEqual(PanelIds.MCPDashboard, viewModel.PanelId);
            Assert.IsNotNull(viewModel.Servers);
            Assert.IsNotNull(viewModel.ServerOperations);
            Assert.IsNotNull(viewModel.AvailableServerTypes);
        }

        [TestMethod]
        public void Constructor_WithNullContext_ThrowsArgumentNullException()
        {
            Assert.ThrowsException<ArgumentNullException>(() =>
                new MCPDashboardViewModel(null!, _mockClient!.Object));
        }

        [TestMethod]
        public void Constructor_WithNullClient_ThrowsArgumentNullException()
        {
            Assert.ThrowsException<ArgumentNullException>(() =>
                new MCPDashboardViewModel(MockContext!, null!));
        }

        [TestMethod]
        public void Constructor_InitializesEmptyCollections()
        {
            // Arrange & Act
            var viewModel = CreateViewModel();

            // Assert
            Assert.IsNotNull(viewModel.Servers);
            Assert.AreEqual(0, viewModel.Servers.Count);
            Assert.IsNotNull(viewModel.ServerOperations);
            Assert.AreEqual(0, viewModel.ServerOperations.Count);
        }

        #endregion

        #region Property Tests

        [TestMethod]
        public void Servers_WhenSet_RaisesPropertyChanged()
        {
            // Arrange
            var viewModel = CreateViewModel();
            var propertyChanged = false;
            viewModel.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(viewModel.Servers))
                    propertyChanged = true;
            };

            // Act
            viewModel.Servers = new ObservableCollection<MCPServerItem>();

            // Assert
            Assert.IsTrue(propertyChanged);
        }

        [TestMethod]
        public void ServerOperations_WhenSet_RaisesPropertyChanged()
        {
            // Arrange
            var viewModel = CreateViewModel();
            var propertyChanged = false;
            viewModel.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(viewModel.ServerOperations))
                    propertyChanged = true;
            };

            // Act
            viewModel.ServerOperations = new ObservableCollection<MCPOperationItem>();

            // Assert
            Assert.IsTrue(propertyChanged);
        }

        [TestMethod]
        public void AvailableServerTypes_WhenSet_RaisesPropertyChanged()
        {
            // Arrange
            var viewModel = CreateViewModel();
            var propertyChanged = false;
            viewModel.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(viewModel.AvailableServerTypes))
                    propertyChanged = true;
            };

            // Act
            viewModel.AvailableServerTypes = new ObservableCollection<string> { "stdio", "http" };

            // Assert
            Assert.IsTrue(propertyChanged);
        }

        [TestMethod]
        public void IsCreatingServer_WhenSet_RaisesPropertyChanged()
        {
            // Arrange
            var viewModel = CreateViewModel();
            var propertyChanged = false;
            viewModel.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(viewModel.IsCreatingServer))
                    propertyChanged = true;
            };

            // Act
            viewModel.IsCreatingServer = true;

            // Assert
            Assert.IsTrue(propertyChanged);
            Assert.IsTrue(viewModel.IsCreatingServer);
        }

        [TestMethod]
        public void SelectedServer_WhenSet_RaisesPropertyChanged()
        {
            // Arrange
            var viewModel = CreateViewModel();
            var propertyChanged = false;
            viewModel.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(viewModel.SelectedServer))
                    propertyChanged = true;
            };

            // Act
            var serverModel = new MCPServerInfo
            {
                ServerId = "server-1",
                Name = "Test Server",
                Description = "Test Description",
                ServerType = "local",
                Status = "connected"
            };
            viewModel.SelectedServer = new MCPServerItem(serverModel);

            // Assert
            Assert.IsTrue(propertyChanged);
        }

        [TestMethod]
        public void NewServerName_WhenSet_RaisesPropertyChanged()
        {
            // Arrange
            var viewModel = CreateViewModel();
            var propertyChanged = false;
            viewModel.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(viewModel.NewServerName))
                    propertyChanged = true;
            };

            // Act
            viewModel.NewServerName = "TestServer";

            // Assert
            Assert.IsTrue(propertyChanged);
            Assert.AreEqual("TestServer", viewModel.NewServerName);
        }

        #endregion

        #region Panel Interface Tests

        [TestMethod]
        public void PanelId_ReturnsCorrectValue()
        {
            var viewModel = CreateViewModel();
            Assert.AreEqual(PanelIds.MCPDashboard, viewModel.PanelId);
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
            viewModel.NewServerName = null;
            viewModel.NewServerDescription = null;
            viewModel.NewServerType = null;
            viewModel.NewServerEndpoint = null;
            viewModel.SelectedServer = null;

            // Assert
            Assert.IsNotNull(viewModel);
            Assert.IsNotNull(viewModel.Servers);
        }

        #endregion
    }
}