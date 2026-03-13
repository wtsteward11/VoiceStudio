using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;
using VoiceStudio.App.ViewModels;
using VoiceStudio.App.Services;
using VoiceStudio.Core.Models;
using VoiceStudio.Core.Services;

namespace VoiceStudio.App.Tests.ViewModels
{
    /// <summary>
    /// Unit tests for AssistantViewModel.
    /// Source: AssistantViewModel.cs
    /// </summary>
    [TestClass]
    public class AssistantViewModelTests : ViewModelTestBase
    {
        private Mock<IAssistantClient>? _mockAssistantClient;
        private Mock<IProjectsClient>? _mockProjectsClient;

        [TestInitialize]
        public override void TestInitialize()
        {
            base.TestInitialize();
            _mockAssistantClient = new Mock<IAssistantClient>();
            _mockProjectsClient = new Mock<IProjectsClient>();
            _mockProjectsClient
                .Setup(x => x.GetProjectsAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<Project>());
            _mockAssistantClient
                .Setup(x => x.GetConversationsAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(Array.Empty<AssistantConversation>());
        }

        private AssistantViewModel CreateViewModel()
        {
            return new AssistantViewModel(MockContext!, _mockAssistantClient!.Object, _mockProjectsClient!.Object);
        }

        #region Construction and Initialization Tests

        [TestMethod]
        public void Constructor_WithValidDependencies_CreatesInstance()
        {
            // Arrange & Act
            var viewModel = CreateViewModel();

            // Assert
            Assert.IsNotNull(viewModel);
            Assert.AreEqual("assistant", viewModel.PanelId);
            Assert.IsNotNull(viewModel.Conversations);
            Assert.IsNotNull(viewModel.Messages);
            Assert.IsNotNull(viewModel.TaskSuggestions);
        }

        [TestMethod]
        public void Constructor_WithNullContext_ThrowsArgumentNullException()
        {
            Assert.ThrowsException<ArgumentNullException>(() =>
                new AssistantViewModel(null!, _mockAssistantClient!.Object, _mockProjectsClient!.Object));
        }

        [TestMethod]
        public void Constructor_WithNullAssistantClient_ThrowsArgumentNullException()
        {
            Assert.ThrowsException<ArgumentNullException>(() =>
                new AssistantViewModel(MockContext!, null!, _mockProjectsClient!.Object));
        }

        [TestMethod]
        public void Constructor_InitializesEmptyCollections()
        {
            // Arrange & Act
            var viewModel = CreateViewModel();

            // Assert
            Assert.IsNotNull(viewModel.Conversations);
            Assert.AreEqual(0, viewModel.Conversations.Count);
            Assert.IsNotNull(viewModel.Messages);
            Assert.AreEqual(0, viewModel.Messages.Count);
            Assert.IsNotNull(viewModel.AvailableProjects);
        }

        #endregion

        #region Property Tests

        [TestMethod]
        public void Conversations_WhenSet_RaisesPropertyChanged()
        {
            // Arrange
            var viewModel = CreateViewModel();
            var propertyChanged = false;
            viewModel.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(viewModel.Conversations))
                    propertyChanged = true;
            };

            // Act
            viewModel.Conversations = new ObservableCollection<ConversationItem>();

            // Assert
            Assert.IsTrue(propertyChanged);
        }

        [TestMethod]
        public void Messages_WhenSet_RaisesPropertyChanged()
        {
            // Arrange
            var viewModel = CreateViewModel();
            var propertyChanged = false;
            viewModel.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(viewModel.Messages))
                    propertyChanged = true;
            };

            // Act
            viewModel.Messages = new ObservableCollection<MessageItem>();

            // Assert
            Assert.IsTrue(propertyChanged);
        }

        [TestMethod]
        public void ChatMessage_WhenSet_RaisesPropertyChanged()
        {
            // Arrange
            var viewModel = CreateViewModel();
            var propertyChanged = false;
            viewModel.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(viewModel.ChatMessage))
                    propertyChanged = true;
            };

            // Act
            viewModel.ChatMessage = "Hello, AI";

            // Assert
            Assert.IsTrue(propertyChanged);
            Assert.AreEqual("Hello, AI", viewModel.ChatMessage);
        }

        [TestMethod]
        public void AvailableProjects_WhenSet_RaisesPropertyChanged()
        {
            // Arrange
            var viewModel = CreateViewModel();
            var propertyChanged = false;
            viewModel.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(viewModel.AvailableProjects))
                    propertyChanged = true;
            };

            // Act
            viewModel.AvailableProjects = new ObservableCollection<AssistantProjectItem>();

            // Assert
            Assert.IsTrue(propertyChanged);
        }

        [TestMethod]
        public void TaskSuggestions_WhenSet_RaisesPropertyChanged()
        {
            // Arrange
            var viewModel = CreateViewModel();
            var propertyChanged = false;
            viewModel.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(viewModel.TaskSuggestions))
                    propertyChanged = true;
            };

            // Act
            viewModel.TaskSuggestions = new ObservableCollection<TaskSuggestionItem>();

            // Assert
            Assert.IsTrue(propertyChanged);
        }

        [TestMethod]
        public void IsLoading_WhenSet_RaisesPropertyChanged()
        {
            // Arrange
            var viewModel = CreateViewModel();
            var propertyChanged = false;
            viewModel.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(viewModel.IsLoading))
                    propertyChanged = true;
            };

            // Act
            viewModel.IsLoading = true;

            // Assert
            Assert.IsTrue(propertyChanged);
            Assert.IsTrue(viewModel.IsLoading);
        }

        [TestMethod]
        public void SelectedConversation_WhenSet_RaisesPropertyChanged()
        {
            // Arrange
            var viewModel = CreateViewModel();
            var propertyChanged = false;
            viewModel.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(viewModel.SelectedConversation))
                    propertyChanged = true;
            };

            // Act
            var now = DateTime.UtcNow.ToString("O");
            var conversation = new AssistantConversation
            {
                ConversationId = "conv-1",
                Title = "Test Conversation",
                Messages = Array.Empty<AssistantMessage>(),
                Created = now,
                Updated = now
            };
            viewModel.SelectedConversation = new ConversationItem(conversation);

            // Assert
            Assert.IsTrue(propertyChanged);
        }

        #endregion

        #region Panel Interface Tests

        [TestMethod]
        public void PanelId_ReturnsCorrectValue()
        {
            var viewModel = CreateViewModel();
            Assert.AreEqual("assistant", viewModel.PanelId);
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
            viewModel.ChatMessage = string.Empty;
            viewModel.SelectedConversation = null;
            viewModel.SelectedProjectId = null;
            viewModel.ErrorMessage = null;

            // Assert
            Assert.IsNotNull(viewModel);
            Assert.IsNotNull(viewModel.Messages);
        }

        [TestMethod]
        public void HasError_WhenErrorMessageIsNull_ReturnsFalse()
        {
            // Arrange
            var viewModel = CreateViewModel();
            viewModel.ErrorMessage = null;

            // Assert
            Assert.IsFalse(viewModel.HasError);
        }

        [TestMethod]
        public void HasError_WhenErrorMessageIsSet_ReturnsTrue()
        {
            // Arrange
            var viewModel = CreateViewModel();
            viewModel.ErrorMessage = "An error occurred";

            // Assert
            Assert.IsTrue(viewModel.HasError);
        }

        #endregion
    }
}