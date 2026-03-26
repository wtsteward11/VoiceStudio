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
    /// Unit tests for KeyboardShortcutsViewModel.
    /// Source: KeyboardShortcutsViewModel.cs
    /// </summary>
    [TestClass]
    public class KeyboardShortcutsViewModelTests : ViewModelTestBase
    {
        private Mock<IKeyboardShortcutsClient>? _mockKeyboardShortcutsClient;

        [TestInitialize]
        public override void TestInitialize()
        {
            base.TestInitialize();
            _mockKeyboardShortcutsClient = new Mock<IKeyboardShortcutsClient>();
        }

        private KeyboardShortcutsViewModel CreateViewModel()
        {
            return new KeyboardShortcutsViewModel(MockContext!, _mockKeyboardShortcutsClient!.Object);
        }

        #region Construction and Initialization Tests

        [TestMethod]
        public void Constructor_WithValidDependencies_CreatesInstance()
        {
            // Arrange & Act
            var viewModel = CreateViewModel();

            // Assert
            Assert.IsNotNull(viewModel);
            Assert.AreEqual("keyboard_shortcuts", viewModel.PanelId);
            Assert.IsNotNull(viewModel.Shortcuts);
            Assert.IsNotNull(viewModel.AvailableCategories);
            Assert.IsNotNull(viewModel.AvailablePanels);
        }

        [TestMethod]
        public void Constructor_WithNullContext_ThrowsArgumentNullException()
        {
            Assert.ThrowsException<ArgumentNullException>(() =>
                new KeyboardShortcutsViewModel(null!, _mockKeyboardShortcutsClient!.Object));
        }

        [TestMethod]
        public void Constructor_WithNullClient_ThrowsArgumentNullException()
        {
            Assert.ThrowsException<ArgumentNullException>(() =>
                new KeyboardShortcutsViewModel(MockContext!, null!));
        }

        [TestMethod]
        public void Constructor_InitializesEmptyCollections()
        {
            // Arrange & Act
            var viewModel = CreateViewModel();

            // Assert
            Assert.IsNotNull(viewModel.Shortcuts);
            Assert.AreEqual(0, viewModel.Shortcuts.Count);
            Assert.IsNotNull(viewModel.AvailableCategories);
            Assert.IsNotNull(viewModel.AvailablePanels);
        }

        #endregion

        #region Property Tests

        [TestMethod]
        public void Shortcuts_WhenSet_RaisesPropertyChanged()
        {
            // Arrange
            var viewModel = CreateViewModel();
            var propertyChanged = false;
            viewModel.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(viewModel.Shortcuts))
                    propertyChanged = true;
            };

            // Act
            viewModel.Shortcuts = new ObservableCollection<ShortcutItem>();

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
            viewModel.AvailableCategories = new ObservableCollection<string> { "Navigation", "Editing" };

            // Assert
            Assert.IsTrue(propertyChanged);
        }

        [TestMethod]
        public void AvailablePanels_WhenSet_RaisesPropertyChanged()
        {
            // Arrange
            var viewModel = CreateViewModel();
            var propertyChanged = false;
            viewModel.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(viewModel.AvailablePanels))
                    propertyChanged = true;
            };

            // Act
            viewModel.AvailablePanels = new ObservableCollection<string> { "Timeline", "Library" };

            // Assert
            Assert.IsTrue(propertyChanged);
        }

        [TestMethod]
        public void IsEditing_WhenSet_RaisesPropertyChanged()
        {
            // Arrange
            var viewModel = CreateViewModel();
            var propertyChanged = false;
            viewModel.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(viewModel.IsEditing))
                    propertyChanged = true;
            };

            // Act
            viewModel.IsEditing = true;

            // Assert
            Assert.IsTrue(propertyChanged);
            Assert.IsTrue(viewModel.IsEditing);
        }

        [TestMethod]
        public void SelectedShortcut_WhenSet_RaisesPropertyChanged()
        {
            // Arrange
            var viewModel = CreateViewModel();
            var propertyChanged = false;
            viewModel.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(viewModel.SelectedShortcut))
                    propertyChanged = true;
            };

            // Act
            var shortcut = new KeyboardShortcutsShortcut
            {
                Id = "shortcut-1",
                Key = "A",
                KeyCode = "KeyA",
                Modifiers = new System.Collections.Generic.List<string> { "Ctrl" },
                Description = "Test shortcut",
                Category = "Test",
                PanelId = null,
                IsCustom = false
            };
            viewModel.SelectedShortcut = new ShortcutItem(shortcut);

            // Assert
            Assert.IsTrue(propertyChanged);
        }

        [TestMethod]
        public void SearchQuery_WhenSet_RaisesPropertyChanged()
        {
            // Arrange
            var viewModel = CreateViewModel();
            var propertyChanged = false;
            viewModel.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(viewModel.SearchQuery))
                    propertyChanged = true;
            };

            // Act
            viewModel.SearchQuery = "copy";

            // Assert
            Assert.IsTrue(propertyChanged);
            Assert.AreEqual("copy", viewModel.SearchQuery);
        }

        [TestMethod]
        public void EditingKey_WhenSet_RaisesPropertyChanged()
        {
            // Arrange
            var viewModel = CreateViewModel();
            var propertyChanged = false;
            viewModel.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(viewModel.EditingKey))
                    propertyChanged = true;
            };

            // Act
            viewModel.EditingKey = "Ctrl+C";

            // Assert
            Assert.IsTrue(propertyChanged);
            Assert.AreEqual("Ctrl+C", viewModel.EditingKey);
        }

        #endregion

        #region Panel Interface Tests

        [TestMethod]
        public void PanelId_ReturnsCorrectValue()
        {
            var viewModel = CreateViewModel();
            Assert.AreEqual("keyboard_shortcuts", viewModel.PanelId);
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

            // Act - setting null values should not throw
            viewModel.SearchQuery = null;
            viewModel.SelectedCategory = null;
            viewModel.SelectedPanelId = null;
            viewModel.SelectedShortcut = null;
            viewModel.ConflictMessage = null;

            // Assert
            Assert.IsNotNull(viewModel);
            Assert.IsNotNull(viewModel.Shortcuts);
        }

        #endregion
    }
}