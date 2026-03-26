using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using VoiceStudio.App.Views.Panels;
using VoiceStudio.Core.Panels;
using VoiceStudio.Core.Services;

namespace VoiceStudio.App.Tests.ViewModels
{
    [TestClass]
    public class AnalyzerViewModelTests
    {
        private Mock<IAnalyzerClient> _mockAnalyzerClient = null!;
        private Mock<IAudioVisualizationService> _mockAudioVisualizationService = null!;
        private Mock<IAudioPlayerService> _mockAudioPlayer = null!;
        private AnalyzerViewModel _viewModel = null!;

        [TestInitialize]
        public void Setup()
        {
            _mockAnalyzerClient = new Mock<IAnalyzerClient>();
            _mockAudioVisualizationService = new Mock<IAudioVisualizationService>();
            _mockAudioPlayer = new Mock<IAudioPlayerService>();
            _viewModel = new AnalyzerViewModel(_mockAnalyzerClient.Object, _mockAudioVisualizationService.Object, _mockAudioPlayer.Object);
        }

        [TestMethod]
        public void Constructor_InitializesWithDefaultValues()
        {
            Assert.IsNotNull(_viewModel);
            Assert.AreEqual(PanelIds.Analyzer, _viewModel.PanelId);
            Assert.AreEqual("Analyzer", _viewModel.DisplayName);
            Assert.IsFalse(_viewModel.IsLoading);
        }

        [TestMethod]
        public void SelectedTab_DefaultsToWaveform()
        {
            Assert.AreEqual("Waveform", _viewModel.SelectedTab);
        }

        [TestMethod]
        public void WaveformSamples_InitializesAsEmptyList()
        {
            Assert.IsNotNull(_viewModel.WaveformSamples);
            Assert.AreEqual(0, _viewModel.WaveformSamples.Count);
        }

        [TestMethod]
        public void SpectrogramFrames_InitializesAsEmptyCollection()
        {
            Assert.IsNotNull(_viewModel.SpectrogramFrames);
            Assert.AreEqual(0, _viewModel.SpectrogramFrames.Count);
        }

        [TestMethod]
        public void HasAudioData_ReturnsFalse_WhenNoDataLoaded()
        {
            Assert.IsFalse(_viewModel.HasAudioData);
        }

        [TestMethod]
        public void PlaybackPosition_DefaultsToNegativeOne()
        {
            Assert.AreEqual(-1.0, _viewModel.PlaybackPosition);
        }

        [TestCleanup]
        public void Cleanup()
        {
            _viewModel?.Dispose();
        }
    }
}
