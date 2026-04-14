using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.UI.Dispatching;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using VoiceStudio.App.Core.Services;
using VoiceStudio.App.Services;
using VoiceStudio.App.Tests.Fixtures;
using VoiceStudio.App.ViewModels;
using VoiceStudio.Core.Panels;
using VoiceStudio.Core.Services;
using VoiceStudio.Core.Models;
using System.Collections.Generic;
using System.Threading;

namespace VoiceStudio.App.Tests.ViewModels
{
    [TestClass]
    public class SettingsViewModelTests
    {
        private IViewModelContext _context = null!;
        private Mock<ISettingsService> _mockSettingsService = null!;
        private Mock<ISettingsClient> _mockSettingsClient = null!;
        private DispatcherQueueController? _dispatcherController;
        private SettingsViewModel _viewModel = null!;

        [TestInitialize]
        public void Setup()
        {
            _dispatcherController = DispatcherQueueController.CreateOnDedicatedThread();
            var dispatcher = _dispatcherController.DispatcherQueue;
            _context = new ViewModelContext(NullLogger.Instance, dispatcher);
            _mockSettingsService = new Mock<ISettingsService>();
            _mockSettingsClient = new Mock<ISettingsClient>();
            _mockSettingsClient
                .Setup(x => x.GetEffectiveEnginePriorityAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new EffectiveEnginePriorityResponse
                {
                    Source = "default",
                    Order = new List<string> { "xtts_v2", "openvoice", "piper", "espeak" }
                });
            _mockSettingsClient
                .Setup(x => x.GetTorchVenvStatusAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync((TorchVenvStatusResponse?)null);
            _viewModel = new SettingsViewModel(
                _context,
                _mockSettingsService.Object,
                _mockSettingsClient.Object);
        }

        [TestCleanup]
        public void Cleanup()
        {
            DispatcherQueueTestHelpers.ShutdownSyncBounded(_dispatcherController);
        }

        [TestMethod]
        public void Constructor_InitializesWithDefaultValues()
        {
            Assert.IsNotNull(_viewModel);
            Assert.AreEqual(PanelIds.Settings, _viewModel.PanelId);
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
        public void ApiUrl_DefaultsToBackendClientConfigDefault()
        {
            Assert.AreEqual(BackendClientConfig.DefaultHttpBaseUrl, _viewModel.ApiUrl);
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
