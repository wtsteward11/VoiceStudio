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
    /// Unit tests for LexiconViewModel.
    /// Source: LexiconViewModel.cs
    /// </summary>
    [TestClass]
    public class LexiconViewModelTests : ViewModelTestBase
    {
        private Mock<ILexiconClient>? _mockLexiconClient;

        [TestInitialize]
        public override void TestInitialize()
        {
            base.TestInitialize();
            _mockLexiconClient = new Mock<ILexiconClient>();
        }

        private LexiconViewModel CreateViewModel()
        {
            return new LexiconViewModel(MockContext!, _mockLexiconClient!.Object);
        }

        #region Construction and Initialization Tests

        [TestMethod]
        public void Constructor_WithValidDependencies_CreatesInstance()
        {
            // Arrange & Act
            var viewModel = CreateViewModel();

            // Assert
            Assert.IsNotNull(viewModel);
            Assert.AreEqual("lexicon", viewModel.PanelId);
            Assert.IsNotNull(viewModel.Lexicons);
            Assert.IsNotNull(viewModel.Entries);
            Assert.IsNotNull(viewModel.SearchResults);
        }

        [TestMethod]
        public void Constructor_WithNullContext_ThrowsArgumentNullException()
        {
            Assert.ThrowsException<ArgumentNullException>(() =>
                new LexiconViewModel(null!, _mockLexiconClient!.Object));
        }

        [TestMethod]
        public void Constructor_WithNullLexiconClient_ThrowsArgumentNullException()
        {
            Assert.ThrowsException<ArgumentNullException>(() =>
                new LexiconViewModel(MockContext!, null!));
        }

        [TestMethod]
        public void Constructor_InitializesDefaultLanguage()
        {
            // Arrange & Act
            var viewModel = CreateViewModel();

            // Assert
            Assert.AreEqual("en", viewModel.NewLexiconLanguage);
        }

        #endregion

        #region Property Tests

        [TestMethod]
        public void Lexicons_WhenSet_RaisesPropertyChanged()
        {
            // Arrange
            var viewModel = CreateViewModel();
            var propertyChanged = false;
            viewModel.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(viewModel.Lexicons))
                    propertyChanged = true;
            };

            // Act
            viewModel.Lexicons = new ObservableCollection<LexiconItem>();

            // Assert
            Assert.IsTrue(propertyChanged);
        }

        [TestMethod]
        public void Entries_WhenSet_RaisesPropertyChanged()
        {
            // Arrange
            var viewModel = CreateViewModel();
            var propertyChanged = false;
            viewModel.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(viewModel.Entries))
                    propertyChanged = true;
            };

            // Act
            viewModel.Entries = new ObservableCollection<LexiconEntryItem>();

            // Assert
            Assert.IsTrue(propertyChanged);
        }

        [TestMethod]
        public void NewLexiconName_WhenSet_RaisesPropertyChanged()
        {
            // Arrange
            var viewModel = CreateViewModel();
            var propertyChanged = false;
            viewModel.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(viewModel.NewLexiconName))
                    propertyChanged = true;
            };

            // Act
            viewModel.NewLexiconName = "MyLexicon";

            // Assert
            Assert.IsTrue(propertyChanged);
            Assert.AreEqual("MyLexicon", viewModel.NewLexiconName);
        }

        [TestMethod]
        public void NewLexiconLanguage_WhenSet_RaisesPropertyChanged()
        {
            // Arrange
            var viewModel = CreateViewModel();
            var propertyChanged = false;
            viewModel.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(viewModel.NewLexiconLanguage))
                    propertyChanged = true;
            };

            // Act
            viewModel.NewLexiconLanguage = "es";

            // Assert
            Assert.IsTrue(propertyChanged);
            Assert.AreEqual("es", viewModel.NewLexiconLanguage);
        }

        [TestMethod]
        public void NewEntryWord_WhenSet_RaisesPropertyChanged()
        {
            // Arrange
            var viewModel = CreateViewModel();
            var propertyChanged = false;
            viewModel.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(viewModel.NewEntryWord))
                    propertyChanged = true;
            };

            // Act
            viewModel.NewEntryWord = "tomato";

            // Assert
            Assert.IsTrue(propertyChanged);
            Assert.AreEqual("tomato", viewModel.NewEntryWord);
        }

        [TestMethod]
        public void NewEntryPronunciation_WhenSet_RaisesPropertyChanged()
        {
            // Arrange
            var viewModel = CreateViewModel();
            var propertyChanged = false;
            viewModel.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(viewModel.NewEntryPronunciation))
                    propertyChanged = true;
            };

            // Act
            viewModel.NewEntryPronunciation = "təˈmeɪtoʊ";

            // Assert
            Assert.IsTrue(propertyChanged);
            Assert.AreEqual("təˈmeɪtoʊ", viewModel.NewEntryPronunciation);
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
            viewModel.SearchQuery = "hello";

            // Assert
            Assert.IsTrue(propertyChanged);
            Assert.AreEqual("hello", viewModel.SearchQuery);
        }

        [TestMethod]
        public void SearchResults_WhenSet_RaisesPropertyChanged()
        {
            // Arrange
            var viewModel = CreateViewModel();
            var propertyChanged = false;
            viewModel.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(viewModel.SearchResults))
                    propertyChanged = true;
            };

            // Act
            viewModel.SearchResults = new ObservableCollection<LexiconSearchResultItem>();

            // Assert
            Assert.IsTrue(propertyChanged);
        }

        [TestMethod]
        public void SelectedLexicon_WhenSet_RaisesPropertyChanged()
        {
            // Arrange
            var viewModel = CreateViewModel();
            var propertyChanged = false;
            viewModel.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(viewModel.SelectedLexicon))
                    propertyChanged = true;
            };

            // Act
            var lexiconModel = new LexiconViewModel.Lexicon
            {
                LexiconId = "lex-1",
                Name = "Test Lexicon",
                Language = "en"
            };
            viewModel.SelectedLexicon = new LexiconItem(lexiconModel);

            // Assert
            Assert.IsTrue(propertyChanged);
        }

        #endregion

        #region Panel Interface Tests

        [TestMethod]
        public void PanelId_ReturnsCorrectValue()
        {
            var viewModel = CreateViewModel();
            Assert.AreEqual("lexicon", viewModel.PanelId);
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

            // Act - setting empty or null values should not throw
            viewModel.NewLexiconName = string.Empty;
            viewModel.NewEntryWord = string.Empty;
            viewModel.SearchQuery = string.Empty;
            viewModel.SelectedLexicon = null;
            viewModel.SelectedEntry = null;

            // Assert
            Assert.IsNotNull(viewModel);
            Assert.IsNotNull(viewModel.Lexicons);
            Assert.IsNotNull(viewModel.Entries);
        }

        #endregion
    }
}