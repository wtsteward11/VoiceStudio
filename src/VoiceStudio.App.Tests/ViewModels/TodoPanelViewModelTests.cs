using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Threading.Tasks;
using VoiceStudio.App.ViewModels;
using VoiceStudio.App.Services;
using VoiceStudio.Core.Panels;
using VoiceStudio.Core.Services;

namespace VoiceStudio.App.Tests.ViewModels
{
    /// <summary>
    /// Unit tests for TodoPanelViewModel.
    /// Source: TodoPanelViewModel.cs
    /// </summary>
    [TestClass]
    public class TodoPanelViewModelTests : ViewModelTestBase
    {
        private Mock<ITodoPanelClient>? _mockTodoPanelClient;

        [TestInitialize]
        public override void TestInitialize()
        {
            base.TestInitialize();
            _mockTodoPanelClient = new Mock<ITodoPanelClient>();
        }

        private TodoPanelViewModel CreateViewModel()
        {
            return new TodoPanelViewModel(MockContext!, _mockTodoPanelClient!.Object);
        }

        #region Construction and Initialization Tests

        [TestMethod]
        public void Constructor_WithValidDependencies_CreatesInstance()
        {
            // Arrange & Act
            var viewModel = CreateViewModel();

            // Assert
            Assert.IsNotNull(viewModel);
            Assert.AreEqual(PanelIds.TodoPanel, viewModel.PanelId);
            Assert.IsNotNull(viewModel.Todos);
        }

        [TestMethod]
        public void Constructor_WithNullContext_ThrowsArgumentNullException()
        {
            // Arrange & Act & Assert
            Assert.ThrowsException<ArgumentNullException>(() =>
                new TodoPanelViewModel(null!, _mockTodoPanelClient!.Object));
        }

        [TestMethod]
        public void Constructor_WithNullTodoPanelClient_ThrowsArgumentNullException()
        {
            // Arrange & Act & Assert
            Assert.ThrowsException<ArgumentNullException>(() =>
                new TodoPanelViewModel(MockContext!, null!));
        }

        [TestMethod]
        public void Constructor_InitializesDefaultPriorities()
        {
            // Arrange & Act
            var viewModel = CreateViewModel();

            // Assert
            Assert.IsNotNull(viewModel.AvailablePriorities);
            Assert.IsTrue(viewModel.AvailablePriorities.Count >= 4);
            CollectionAssert.Contains(viewModel.AvailablePriorities, "low");
            CollectionAssert.Contains(viewModel.AvailablePriorities, "medium");
            CollectionAssert.Contains(viewModel.AvailablePriorities, "high");
            CollectionAssert.Contains(viewModel.AvailablePriorities, "urgent");
        }

        [TestMethod]
        public void Constructor_InitializesDefaultStatuses()
        {
            // Arrange & Act
            var viewModel = CreateViewModel();

            // Assert
            Assert.IsNotNull(viewModel.AvailableStatuses);
            Assert.IsTrue(viewModel.AvailableStatuses.Count >= 4);
            CollectionAssert.Contains(viewModel.AvailableStatuses, "pending");
            CollectionAssert.Contains(viewModel.AvailableStatuses, "in_progress");
            CollectionAssert.Contains(viewModel.AvailableStatuses, "completed");
            CollectionAssert.Contains(viewModel.AvailableStatuses, "cancelled");
        }

        #endregion

        #region Property Tests

        [TestMethod]
        public void Todos_WhenSet_RaisesPropertyChanged()
        {
            // Arrange
            var viewModel = CreateViewModel();
            var propertyChanged = false;
            viewModel.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(viewModel.Todos))
                    propertyChanged = true;
            };

            // Act
            viewModel.Todos = new ObservableCollection<TodoItem>();

            // Assert
            Assert.IsTrue(propertyChanged);
        }

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
            viewModel.AvailableCategories = new ObservableCollection<string> { "Category1", "Category2" };

            // Assert
            Assert.IsTrue(propertyChanged);
        }

        [TestMethod]
        public void AvailableTags_WhenSet_RaisesPropertyChanged()
        {
            // Arrange
            var viewModel = CreateViewModel();
            var propertyChanged = false;
            viewModel.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(viewModel.AvailableTags))
                    propertyChanged = true;
            };

            // Act
            viewModel.AvailableTags = new ObservableCollection<string> { "Tag1", "Tag2" };

            // Assert
            Assert.IsTrue(propertyChanged);
        }

        [TestMethod]
        public void NewTodoPriority_WhenSet_RaisesPropertyChanged()
        {
            // Arrange
            var viewModel = CreateViewModel();
            var propertyChanged = false;
            viewModel.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(viewModel.NewTodoPriority))
                    propertyChanged = true;
            };

            // Act
            viewModel.NewTodoPriority = "high";

            // Assert
            Assert.IsTrue(propertyChanged);
            Assert.AreEqual("high", viewModel.NewTodoPriority);
        }

        [TestMethod]
        public void AvailablePriorities_WhenSet_RaisesPropertyChanged()
        {
            // Arrange
            var viewModel = CreateViewModel();
            var propertyChanged = false;
            viewModel.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(viewModel.AvailablePriorities))
                    propertyChanged = true;
            };

            // Act
            viewModel.AvailablePriorities = new ObservableCollection<string> { "low", "high" };

            // Assert
            Assert.IsTrue(propertyChanged);
        }

        [TestMethod]
        public void AvailableStatuses_WhenSet_RaisesPropertyChanged()
        {
            // Arrange
            var viewModel = CreateViewModel();
            var propertyChanged = false;
            viewModel.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(viewModel.AvailableStatuses))
                    propertyChanged = true;
            };

            // Act
            viewModel.AvailableStatuses = new ObservableCollection<string> { "pending", "completed" };

            // Assert
            Assert.IsTrue(propertyChanged);
        }

        [TestMethod]
        public void IsCreatingTodo_WhenSet_RaisesPropertyChanged()
        {
            // Arrange
            var viewModel = CreateViewModel();
            var propertyChanged = false;
            viewModel.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(viewModel.IsCreatingTodo))
                    propertyChanged = true;
            };

            // Act
            viewModel.IsCreatingTodo = true;

            // Assert
            Assert.IsTrue(propertyChanged);
            Assert.IsTrue(viewModel.IsCreatingTodo);
        }

        [TestMethod]
        public void SelectedTodo_WhenSet_RaisesPropertyChanged()
        {
            // Arrange
            var viewModel = CreateViewModel();
            var propertyChanged = false;
            viewModel.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(viewModel.SelectedTodo))
                    propertyChanged = true;
            };

            // Act
            var now = DateTime.UtcNow.ToString("O");
            viewModel.SelectedTodo = new TodoItem(
                todoId: "test-1",
                title: "Test Todo",
                description: null,
                status: "pending",
                priority: "medium",
                category: null,
                tags: Array.Empty<string>(),
                dueDate: null,
                createdAt: now,
                updatedAt: now,
                completedAt: null);

            // Assert
            Assert.IsTrue(propertyChanged);
            Assert.IsNotNull(viewModel.SelectedTodo);
            Assert.AreEqual("test-1", viewModel.SelectedTodo.TodoId);
        }

        #endregion

        #region Panel Interface Tests

        [TestMethod]
        public void PanelId_ReturnsCorrectValue()
        {
            // Arrange & Act
            var viewModel = CreateViewModel();

            // Assert
            Assert.AreEqual(PanelIds.TodoPanel, viewModel.PanelId);
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
        public void ViewModel_WhenErrorOccurs_HandlesGracefully()
        {
            // Arrange
            var viewModel = CreateViewModel();

            // Act - setting invalid data should not throw
            viewModel.NewTodoTitle = null;
            viewModel.NewTodoDescription = null;
            viewModel.NewTodoCategory = null;

            // Assert - viewModel should still be in valid state
            Assert.IsNotNull(viewModel);
            Assert.IsNotNull(viewModel.Todos);
        }

        #endregion
    }
}