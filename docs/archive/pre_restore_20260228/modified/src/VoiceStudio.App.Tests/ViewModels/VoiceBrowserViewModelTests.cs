using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using VoiceStudio.App.Tests.Fixtures;
using VoiceStudio.App.ViewModels;
using VoiceStudio.Core.Panels;
using VoiceStudio.Core.Services;

namespace VoiceStudio.App.Tests.ViewModels
{
    [TestClass]
    public class VoiceBrowserViewModelTests
    {
        private Mock<IBackendClient>? _mockBackendClient;
        private static MockViewModelContext? _sharedContext;
        private VoiceBrowserViewModel? _viewModel;

        [TestInitialize]
        public void Setup()
        {
            _mockBackendClient = new Mock<IBackendClient>();
            _sharedContext ??= new MockViewModelContext();
            SetupVoiceSearchResponse(Array.Empty<VoiceProfileSummary>(), 0);
            SetupLanguagesResponse(Array.Empty<string>());
            SetupTagsResponse(Array.Empty<string>());
            _viewModel = new VoiceBrowserViewModel(_sharedContext, _mockBackendClient.Object);
        }

        [TestCleanup]
        public void Cleanup()
        {
            _viewModel?.Dispose();
            _viewModel = null;
            _mockBackendClient = null;
        }

        private void SetupVoiceSearchResponse(VoiceProfileSummary[] voices, int total)
        {
            var response = new VoiceBrowserViewModel.VoiceSearchResponse { Voices = voices, Total = total, Limit = 50, Offset = 0 };
            _mockBackendClient!.Setup(x => x.SendRequestAsync<object, VoiceBrowserViewModel.VoiceSearchResponse>(It.Is<string>(s => s.Contains("/api/voice-browser/voices")), It.IsAny<object>(), It.IsAny<HttpMethod>(), It.IsAny<CancellationToken>())).ReturnsAsync(response);
        }

        private void SetupLanguagesResponse(string[] languages)
        {
            var response = new VoiceBrowserViewModel.LanguagesResponse { Languages = languages };
            _mockBackendClient!.Setup(x => x.SendRequestAsync<object, VoiceBrowserViewModel.LanguagesResponse>(It.Is<string>(s => s.Contains("/api/voice-browser/languages")), It.IsAny<object>(), It.IsAny<HttpMethod>(), It.IsAny<CancellationToken>())).ReturnsAsync(response);
        }

        private void SetupTagsResponse(string[] tags)
        {
            var response = new VoiceBrowserViewModel.TagsResponse { Tags = tags };
            _mockBackendClient!.Setup(x => x.SendRequestAsync<object, VoiceBrowserViewModel.TagsResponse>(It.Is<string>(s => s.Contains("/api/voice-browser/tags")), It.IsAny<object>(), It.IsAny<HttpMethod>(), It.IsAny<CancellationToken>())).ReturnsAsync(response);
        }

        [TestMethod]
        public void Constructor_WithValidDependencies_CreatesInstance()
        {
            Assert.IsNotNull(_viewModel);
            Assert.AreEqual("voice-browser", _viewModel!.PanelId);
            Assert.IsNotNull(_viewModel.DisplayName);
            Assert.IsTrue(_viewModel.DisplayName.Length > 0);
            Assert.AreEqual(PanelRegion.Center, _viewModel.Region);
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentNullException))]
        public void Constructor_WithNullBackendClient_ThrowsArgumentNullException()
        {
            _ = new VoiceBrowserViewModel(_sharedContext!, null!);
        }

        [TestMethod]
        public void Initialization_CollectionsAndPropertiesHaveDefaults()
        {
            Assert.IsNotNull(_viewModel!.Voices);
            Assert.AreEqual(0, _viewModel.Voices.Count);
            Assert.IsNotNull(_viewModel.AvailableLanguages);
            Assert.IsNotNull(_viewModel.AvailableTags);
            Assert.IsNotNull(_viewModel.SelectedTags);
            Assert.AreEqual(string.Empty, _viewModel.SearchQuery);
            Assert.AreEqual(0, _viewModel.TotalVoices);
            Assert.AreEqual(0, _viewModel.CurrentPage);
            Assert.AreEqual(50, _viewModel.PageSize);
            Assert.IsNull(_viewModel.SelectedVoice);
        }

        [TestMethod]
        public void ImplementsIPanelView()
        {
            var panelView = _viewModel as IPanelView;
            Assert.IsNotNull(panelView);
            Assert.AreEqual("voice-browser", panelView!.PanelId);
            Assert.IsNotNull(panelView.DisplayName);
            Assert.AreEqual(PanelRegion.Center, panelView.Region);
        }

        [TestMethod]
        public void Commands_AreInitialized()
        {
            Assert.IsNotNull(_viewModel!.SearchCommand);
            Assert.IsNotNull(_viewModel.LoadLanguagesCommand);
            Assert.IsNotNull(_viewModel.LoadTagsCommand);
            Assert.IsNotNull(_viewModel.RefreshCommand);
            Assert.IsNotNull(_viewModel.NextPageCommand);
            Assert.IsNotNull(_viewModel.PreviousPageCommand);
        }

        [TestMethod]
        public async Task SearchCommand_WhenBackendReturnsVoices_UpdatesVoicesCollection()
        {
            var voices = new[] { new VoiceProfileSummary { Id = "v1", Name = "Test Voice 1", Language = "en" }, new VoiceProfileSummary { Id = "v2", Name = "Test Voice 2", Language = "es" } };
            SetupVoiceSearchResponse(voices, 2);
            await _viewModel!.SearchCommand.ExecuteAsync(null);
            await Task.Delay(200);
            Assert.AreEqual(2, _viewModel.Voices.Count);
            Assert.AreEqual("v1", _viewModel.Voices[0].Id);
            Assert.AreEqual("Test Voice 1", _viewModel.Voices[0].Name);
            Assert.AreEqual(2, _viewModel.TotalVoices);
        }

        [TestMethod]
        public async Task RefreshCommand_InvokesSearchAndUpdatesStatus()
        {
            SetupVoiceSearchResponse(Array.Empty<VoiceProfileSummary>(), 0);
            await _viewModel!.RefreshCommand.ExecuteAsync(null);
            await Task.Delay(200);
            Assert.IsNotNull(_viewModel.StatusMessage);
        }

        [TestMethod]
        public void NextPageCommand_CannotExecute_WhenOnLastPage()
        {
            _viewModel!.TotalVoices = 25;
            _viewModel.CurrentPage = 0;
            _viewModel.PageSize = 50;
            _viewModel.IsLoading = false;
            Assert.IsFalse(_viewModel.NextPageCommand.CanExecute(null));
        }

        [TestMethod]
        public void PreviousPageCommand_CannotExecute_WhenOnFirstPage()
        {
            _viewModel!.CurrentPage = 0;
            _viewModel.IsLoading = false;
            Assert.IsFalse(_viewModel.PreviousPageCommand.CanExecute(null));
        }

        [TestMethod]
        public void SelectedVoice_WhenSet_RaisesPropertyChanged()
        {
            var summary = new VoiceProfileSummary { Id = "sel", Name = "Selected", Language = "en" };
            var item = new VoiceProfileSummaryItem(summary);
            var raised = false;
            _viewModel!.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(VoiceBrowserViewModel.SelectedVoice)) raised = true; };
            _viewModel.SelectedVoice = item;
            Assert.IsTrue(raised);
            Assert.AreEqual(item, _viewModel.SelectedVoice);
        }

        [TestMethod]
        public void SearchQuery_WhenSet_UpdatesValue()
        {
            _viewModel!.SearchQuery = "test query";
            Assert.AreEqual("test query", _viewModel.SearchQuery);
        }

        [TestMethod]
        public void SelectedLanguage_WhenSet_UpdatesValue()
        {
            _viewModel!.SelectedLanguage = "en";
            Assert.AreEqual("en", _viewModel.SelectedLanguage);
        }

        [TestMethod]
        public void SelectedGender_WhenSet_UpdatesValue()
        {
            _viewModel!.SelectedGender = "female";
            Assert.AreEqual("female", _viewModel.SelectedGender);
        }

        [TestMethod]
        public void MinQualityScore_WhenSet_UpdatesValue()
        {
            _viewModel!.MinQualityScore = 0.85;
            Assert.AreEqual(0.85, _viewModel.MinQualityScore);
        }
    }
}
