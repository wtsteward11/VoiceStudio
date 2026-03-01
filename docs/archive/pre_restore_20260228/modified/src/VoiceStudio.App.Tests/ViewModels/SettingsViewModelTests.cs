using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using VoiceStudio.App.Services;
using VoiceStudio.App.Tests.Fixtures;
using VoiceStudio.App.ViewModels;
using VoiceStudio.Core.Services;

namespace VoiceStudio.App.Tests.ViewModels
{
    [TestClass]
    public class SettingsViewModelTests
    {
        private MockViewModelContext _mockContext = null!;
        private Mock<ISettingsService> _mockSettingsService = null!;
        private Mock<IBackendClient> _mockBackendClient = null!;
        private SettingsViewModel _viewModel = null!;

        [TestInitialize]
        public void Setup()
        {
            _mockContext = new MockViewModelContext();
            _mockSettingsService = new Mock<ISettingsService>();
            _mockBackendClient = new Mock<IBackendClient>();
            _viewModel = new SettingsViewModel(
                _mockContext,
                _mockSettingsService.Object,
                _mockBackendClient.Object);
        }

        [TestCleanup]
        public void Cleanup()
        {
            // No dispatcher cleanup needed with MockViewModelContext
        }

        [TestMethod]
        public void Constructor_InitializesWithDefaultValues()
        {
            Assert.IsNotNull(_viewModel);
            Assert.AreEqual("settings", _viewModel.PanelId);
            Assert.AreEqual("Settings", _viewModel.DisplayName);
        }

        [TestMethod]
        public void Theme_DefaultsToDark()
        {
            Assert.AreEqual("Dark", _viewModel.Theme);
        }

        [TestMethod]
        public void Language_DefaultsToEnUS()
        {
            Assert.AreEqual("en-US", _viewModel.Language);
        }

        [TestMethod]
        public void AutoSave_DefaultsToTrue()
        {
            Assert.IsTrue(_viewModel.AutoSave);
        }

        [TestMethod]
        public void AutoSaveInterval_DefaultsTo300()
        {
            Assert.AreEqual(300, _viewModel.AutoSaveInterval);
        }

        [TestMethod]
        public void DefaultAudioEngine_DefaultsToXtts()
        {
            Assert.AreEqual("xtts", _viewModel.DefaultAudioEngine);
        }

        [TestMethod]
        public void QualityLevel_DefaultsTo5()
        {
            Assert.AreEqual(5, _viewModel.QualityLevel);
        }

        [TestMethod]
        public void SampleRate_DefaultsTo44100()
        {
            Assert.AreEqual(44100, _viewModel.SampleRate);
        }

        [TestMethod]
        public void ApiUrl_DefaultsToLocalhost8000()
        {
            Assert.AreEqual("http://localhost:8000", _viewModel.ApiUrl);
        }

        [TestMethod]
        public void SnapEnabled_DefaultsToTrue()
        {
            Assert.IsTrue(_viewModel.SnapEnabled);
        }

        [TestMethod]
        public void CachingEnabled_DefaultsToTrue()
        {
            Assert.IsTrue(_viewModel.CachingEnabled);
        }
    }
}
