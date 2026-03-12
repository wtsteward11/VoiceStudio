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
    /// Unit tests for TextSpeechEditorViewModel.
    /// Source: TextSpeechEditorViewModel.cs
    /// </summary>
    [TestClass]
    public class TextSpeechEditorViewModelTests : ViewModelTestBase
    {
        private Mock<IBackendClient>? _mockBackendClient;
        private Mock<IProjectsClient>? _mockProjectsClient;
        private Mock<IProfilesClient>? _mockProfilesClient;
        private Mock<IAudioPlayerService>? _mockAudioPlayer;

        [TestInitialize]
        public override void TestInitialize()
        {
            base.TestInitialize();
            _mockBackendClient = new Mock<IBackendClient>();
            _mockProjectsClient = new Mock<IProjectsClient>();
            _mockProjectsClient
                .Setup(x => x.GetProjectsAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<Project>());
            _mockProfilesClient = new Mock<IProfilesClient>();
            _mockProfilesClient
                .Setup(x => x.GetProfilesAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<VoiceProfile>());
            _mockAudioPlayer = new Mock<IAudioPlayerService>();
            _mockAudioPlayer.Setup(x => x.PlayBackendAudioIdAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Action>()))
                .Returns(Task.CompletedTask);
        }

        private TextSpeechEditorViewModel CreateViewModel()
        {
            return new TextSpeechEditorViewModel(MockContext!, _mockBackendClient!.Object, _mockProjectsClient!.Object, _mockProfilesClient!.Object, _mockAudioPlayer!.Object);
        }

        #region Construction and Initialization Tests

        [TestMethod]
        public void Constructor_WithValidDependencies_CreatesInstance()
        {
            // Arrange & Act
            var viewModel = CreateViewModel();

            // Assert
            Assert.IsNotNull(viewModel);
            Assert.AreEqual("text-speech-editor", viewModel.PanelId);
            Assert.IsNotNull(viewModel.Sessions);
            Assert.IsNotNull(viewModel.Segments);
        }

        [TestMethod]
        public void Constructor_WithNullContext_ThrowsArgumentNullException()
        {
            Assert.ThrowsException<ArgumentNullException>(() =>
                new TextSpeechEditorViewModel(null!, _mockBackendClient!.Object, _mockProjectsClient!.Object, _mockProfilesClient!.Object, _mockAudioPlayer!.Object));
        }

        [TestMethod]
        public void Constructor_WithNullBackendClient_ThrowsArgumentNullException()
        {
            Assert.ThrowsException<ArgumentNullException>(() =>
                new TextSpeechEditorViewModel(MockContext!, null!, _mockProjectsClient!.Object, _mockProfilesClient!.Object, _mockAudioPlayer!.Object));
        }

        [TestMethod]
        public void Constructor_WithNullProjectsClient_ThrowsArgumentNullException()
        {
            Assert.ThrowsException<ArgumentNullException>(() =>
                new TextSpeechEditorViewModel(MockContext!, _mockBackendClient!.Object, null!, _mockProfilesClient!.Object, _mockAudioPlayer!.Object));
        }

        [TestMethod]
        public void Constructor_WithNullProfilesClient_ThrowsArgumentNullException()
        {
            Assert.ThrowsException<ArgumentNullException>(() =>
                new TextSpeechEditorViewModel(MockContext!, _mockBackendClient!.Object, _mockProjectsClient!.Object, null!, _mockAudioPlayer!.Object));
        }

        [TestMethod]
        public void Constructor_InitializesEmptyCollections()
        {
            // Arrange & Act
            var viewModel = CreateViewModel();

            // Assert
            Assert.IsNotNull(viewModel.Sessions);
            Assert.AreEqual(0, viewModel.Sessions.Count);
            Assert.IsNotNull(viewModel.Segments);
            Assert.AreEqual(0, viewModel.Segments.Count);
            Assert.IsNotNull(viewModel.AvailableProjects);
            Assert.IsNotNull(viewModel.AvailableVoiceProfiles);
            Assert.IsNotNull(viewModel.AvailableEngines);
        }

        #endregion

        #region Property Tests

        [TestMethod]
        public void Sessions_WhenSet_RaisesPropertyChanged()
        {
            // Arrange
            var viewModel = CreateViewModel();
            var propertyChanged = false;
            viewModel.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(viewModel.Sessions))
                    propertyChanged = true;
            };

            // Act
            viewModel.Sessions = new ObservableCollection<EditorSessionItem>();

            // Assert
            Assert.IsTrue(propertyChanged);
        }

        [TestMethod]
        public void Segments_WhenSet_RaisesPropertyChanged()
        {
            // Arrange
            var viewModel = CreateViewModel();
            var propertyChanged = false;
            viewModel.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(viewModel.Segments))
                    propertyChanged = true;
            };

            // Act
            viewModel.Segments = new ObservableCollection<TextSegmentItem>();

            // Assert
            Assert.IsTrue(propertyChanged);
        }

        [TestMethod]
        public void NewSessionTitle_WhenSet_RaisesPropertyChanged()
        {
            // Arrange
            var viewModel = CreateViewModel();
            var propertyChanged = false;
            viewModel.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(viewModel.NewSessionTitle))
                    propertyChanged = true;
            };

            // Act
            viewModel.NewSessionTitle = "Test Session";

            // Assert
            Assert.IsTrue(propertyChanged);
            Assert.AreEqual("Test Session", viewModel.NewSessionTitle);
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
            viewModel.AvailableProjects = new ObservableCollection<string> { "Project1", "Project2" };

            // Assert
            Assert.IsTrue(propertyChanged);
        }

        [TestMethod]
        public void AvailableVoiceProfiles_WhenSet_RaisesPropertyChanged()
        {
            // Arrange
            var viewModel = CreateViewModel();
            var propertyChanged = false;
            viewModel.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(viewModel.AvailableVoiceProfiles))
                    propertyChanged = true;
            };

            // Act
            viewModel.AvailableVoiceProfiles = new ObservableCollection<string> { "Voice1", "Voice2" };

            // Assert
            Assert.IsTrue(propertyChanged);
        }

        [TestMethod]
        public void AvailableEngines_WhenSet_RaisesPropertyChanged()
        {
            // Arrange
            var viewModel = CreateViewModel();
            var propertyChanged = false;
            viewModel.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(viewModel.AvailableEngines))
                    propertyChanged = true;
            };

            // Act
            viewModel.AvailableEngines = new ObservableCollection<string> { "XTTS", "Silero" };

            // Assert
            Assert.IsTrue(propertyChanged);
        }

        [TestMethod]
        public void SsmlMode_WhenSet_RaisesPropertyChanged()
        {
            // Arrange
            var viewModel = CreateViewModel();
            var propertyChanged = false;
            viewModel.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(viewModel.SsmlMode))
                    propertyChanged = true;
            };

            // Act
            viewModel.SsmlMode = true;

            // Assert
            Assert.IsTrue(propertyChanged);
            Assert.IsTrue(viewModel.SsmlMode);
        }

        [TestMethod]
        public void EditedTranscript_WhenSet_RaisesPropertyChanged()
        {
            // Arrange
            var viewModel = CreateViewModel();
            var propertyChanged = false;
            viewModel.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(viewModel.EditedTranscript))
                    propertyChanged = true;
            };

            // Act
            viewModel.EditedTranscript = "Hello world";

            // Assert
            Assert.IsTrue(propertyChanged);
            Assert.AreEqual("Hello world", viewModel.EditedTranscript);
        }

        [TestMethod]
        public void SelectedSession_WhenSet_RaisesPropertyChanged()
        {
            // Arrange
            var viewModel = CreateViewModel();
            var propertyChanged = false;
            viewModel.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(viewModel.SelectedSession))
                    propertyChanged = true;
            };

            // Act
            var session = new TextSpeechEditorViewModel.EditorSession
            {
                SessionId = "session-1",
                Title = "Test Session",
                Segments = Array.Empty<TextSpeechEditorViewModel.TextSegment>(),
                Language = "en",
                Created = DateTime.UtcNow.ToString("O"),
                Modified = DateTime.UtcNow.ToString("O")
            };
            viewModel.SelectedSession = new EditorSessionItem(session);

            // Assert
            Assert.IsTrue(propertyChanged);
        }

        #endregion

        #region Panel Interface Tests

        [TestMethod]
        public void PanelId_ReturnsCorrectValue()
        {
            var viewModel = CreateViewModel();
            Assert.AreEqual("text-speech-editor", viewModel.PanelId);
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
            viewModel.NewSessionTitle = string.Empty;
            viewModel.EditedTranscript = string.Empty;
            viewModel.SelectedSession = null;
            viewModel.SelectedSegment = null;

            // Assert
            Assert.IsNotNull(viewModel);
            Assert.IsNotNull(viewModel.Sessions);
        }

        #endregion
    }
}