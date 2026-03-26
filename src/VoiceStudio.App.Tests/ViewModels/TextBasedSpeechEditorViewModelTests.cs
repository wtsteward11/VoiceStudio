using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using VoiceStudio.App.ViewModels;
using VoiceStudio.App.Services;
using VoiceStudio.Core.Models;
using VoiceStudio.Core.Services;

namespace VoiceStudio.App.Tests.ViewModels
{
    /// <summary>
    /// Unit tests for TextBasedSpeechEditorViewModel.
    /// Tests cover construction, property changes, default values, and panel interface.
    /// </summary>
    [TestClass]
    public class TextBasedSpeechEditorViewModelTests : ViewModelTestBase
    {
        private static readonly List<string> ExpectedEngines = new() { "xtts", "chatterbox", "tortoise" };

        private Mock<ITextBasedSpeechEditorClient>? _mockEditorClient;
        private Mock<IProfilesClient>? _mockProfilesClient;
        private TextBasedSpeechEditorViewModel? _viewModel;

        [TestInitialize]
        public override void TestInitialize()
        {
            base.TestInitialize();
            _mockEditorClient = new Mock<ITextBasedSpeechEditorClient>();
            _mockEditorClient
                .Setup(x => x.GetEnginesAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(ExpectedEngines);
            _mockProfilesClient = new Mock<IProfilesClient>();
            _mockProfilesClient
                .Setup(x => x.GetProfilesAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<VoiceProfile>());
            _viewModel = CreateViewModel();
        }

        [TestCleanup]
        public override void TestCleanup()
        {
            _viewModel = null;
            _mockEditorClient = null;
            _mockProfilesClient = null;
            base.TestCleanup();
        }

        private TextBasedSpeechEditorViewModel CreateViewModel()
        {
            return new TextBasedSpeechEditorViewModel(MockContext!, _mockEditorClient!.Object, _mockProfilesClient!.Object);
        }

        #region Construction and Initialization Tests

        [TestMethod]
        public void Constructor_WithValidDependencies_CreatesInstance()
        {
            Assert.IsNotNull(_viewModel);
            Assert.IsNotNull(_viewModel.Segments);
            Assert.IsNotNull(_viewModel.AvailableProfiles);
            Assert.IsNotNull(_viewModel.AvailableEngines);
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentNullException))]
        public void Constructor_WithNullContext_ThrowsArgumentNullException()
        {
            _ = new TextBasedSpeechEditorViewModel(null!, _mockEditorClient!.Object, _mockProfilesClient!.Object);
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentNullException))]
        public void Constructor_WithNullEditorClient_ThrowsArgumentNullException()
        {
            _ = new TextBasedSpeechEditorViewModel(MockContext!, null!, _mockProfilesClient!.Object);
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentNullException))]
        public void Constructor_WithNullProfilesClient_ThrowsArgumentNullException()
        {
            _ = new TextBasedSpeechEditorViewModel(MockContext!, _mockEditorClient!.Object, null!);
        }

        #endregion

        #region Panel Interface Tests

        [TestMethod]
        public void PanelId_ReturnsTextBasedSpeechEditor()
        {
            Assert.AreEqual("text-based-speech-editor", _viewModel!.PanelId);
        }

        [TestMethod]
        public void DisplayName_IsNotEmpty()
        {
            Assert.IsNotNull(_viewModel!.DisplayName);
            Assert.IsTrue(_viewModel.DisplayName.Length > 0);
        }

        [TestMethod]
        public void Region_ReturnsCenter()
        {
            Assert.AreEqual(VoiceStudio.Core.Panels.PanelRegion.Center, _viewModel!.Region);
        }

        #endregion

        #region Default Values Tests

        [TestMethod]
        public void ShowWaveform_DefaultsToTrue()
        {
            Assert.IsTrue(_viewModel!.ShowWaveform);
        }

        [TestMethod]
        public void ShowABComparison_DefaultsToFalse()
        {
            Assert.IsFalse(_viewModel!.ShowABComparison);
        }

        [TestMethod]
        public void SelectedEngine_DefaultsToXtts()
        {
            Assert.AreEqual("xtts", _viewModel!.SelectedEngine);
        }

        [TestMethod]
        public void SelectedQualityMode_DefaultsToStandard()
        {
            Assert.AreEqual("standard", _viewModel!.SelectedQualityMode);
        }

        [TestMethod]
        public async Task AvailableEngines_ContainsExpectedEngines()
        {
            await _viewModel!.RefreshCommand.ExecuteAsync(null);
            Assert.IsTrue(_viewModel.AvailableEngines.Contains("xtts"));
            Assert.IsTrue(_viewModel.AvailableEngines.Contains("chatterbox"));
            Assert.IsTrue(_viewModel.AvailableEngines.Contains("tortoise"));
        }

        [TestMethod]
        public void InsertPosition_DefaultsToZero()
        {
            Assert.AreEqual(0.0f, _viewModel!.InsertPosition);
        }

        #endregion

        #region Property Change Tests

        [TestMethod]
        public void ShowWaveform_WhenSet_RaisesPropertyChanged()
        {
            var propertyChanged = false;
            _viewModel!.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(_viewModel.ShowWaveform))
                    propertyChanged = true;
            };

            _viewModel.ShowWaveform = false;

            Assert.IsTrue(propertyChanged);
            Assert.IsFalse(_viewModel.ShowWaveform);
        }

        [TestMethod]
        public void ShowABComparison_WhenSet_RaisesPropertyChanged()
        {
            var propertyChanged = false;
            _viewModel!.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(_viewModel.ShowABComparison))
                    propertyChanged = true;
            };

            _viewModel.ShowABComparison = true;

            Assert.IsTrue(propertyChanged);
            Assert.IsTrue(_viewModel.ShowABComparison);
        }

        [TestMethod]
        public void InsertPosition_WhenSet_RaisesPropertyChanged()
        {
            var propertyChanged = false;
            _viewModel!.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(_viewModel.InsertPosition))
                    propertyChanged = true;
            };

            _viewModel.InsertPosition = 5.5f;

            Assert.IsTrue(propertyChanged);
            Assert.AreEqual(5.5f, _viewModel.InsertPosition);
        }

        [TestMethod]
        public void SelectedEngine_WhenSet_RaisesPropertyChanged()
        {
            var propertyChanged = false;
            _viewModel!.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(_viewModel.SelectedEngine))
                    propertyChanged = true;
            };

            _viewModel.SelectedEngine = "tortoise";

            Assert.IsTrue(propertyChanged);
            Assert.AreEqual("tortoise", _viewModel.SelectedEngine);
        }

        [TestMethod]
        public void SelectedQualityMode_WhenSet_RaisesPropertyChanged()
        {
            var propertyChanged = false;
            _viewModel!.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(_viewModel.SelectedQualityMode))
                    propertyChanged = true;
            };

            _viewModel.SelectedQualityMode = "high";

            Assert.IsTrue(propertyChanged);
            Assert.AreEqual("high", _viewModel.SelectedQualityMode);
        }

        [TestMethod]
        public void EditedTranscript_WhenSet_RaisesPropertyChanged()
        {
            var propertyChanged = false;
            _viewModel!.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(_viewModel.EditedTranscript))
                    propertyChanged = true;
            };

            _viewModel.EditedTranscript = "Hello, this is a test.";

            Assert.IsTrue(propertyChanged);
            Assert.AreEqual("Hello, this is a test.", _viewModel.EditedTranscript);
        }

        [TestMethod]
        public void ReplacementText_WhenSet_RaisesPropertyChanged()
        {
            var propertyChanged = false;
            _viewModel!.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(_viewModel.ReplacementText))
                    propertyChanged = true;
            };

            _viewModel.ReplacementText = "replacement";

            Assert.IsTrue(propertyChanged);
            Assert.AreEqual("replacement", _viewModel.ReplacementText);
        }

        [TestMethod]
        public void InsertText_WhenSet_RaisesPropertyChanged()
        {
            var propertyChanged = false;
            _viewModel!.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(_viewModel.InsertText))
                    propertyChanged = true;
            };

            _viewModel.InsertText = "inserted text";

            Assert.IsTrue(propertyChanged);
            Assert.AreEqual("inserted text", _viewModel.InsertText);
        }

        #endregion

        #region Selection Tests

        [TestMethod]
        public void SelectedSegment_InitiallyNull()
        {
            Assert.IsNull(_viewModel!.SelectedSegment);
        }

        [TestMethod]
        public void SelectedWord_InitiallyNull()
        {
            Assert.IsNull(_viewModel!.SelectedWord);
        }

        [TestMethod]
        public void AudioId_InitiallyNull()
        {
            Assert.IsNull(_viewModel!.AudioId);
        }

        [TestMethod]
        public void EditSessionId_InitiallyNull()
        {
            Assert.IsNull(_viewModel!.EditSessionId);
        }

        #endregion
    }
}