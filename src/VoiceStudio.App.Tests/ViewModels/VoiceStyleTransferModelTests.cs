using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Collections.Generic;
using VoiceStudio.App.ViewModels;
using VoiceStudio.Core.Models;

namespace VoiceStudio.App.Tests.ViewModels
{
    [TestClass]
    public class VoiceStyleTransferModelTests
    {
        #region StyleProfileResponse Model Tests

        [TestMethod]
        public void StyleProfileResponse_DefaultValues()
        {
            var response = new VoiceStyleTransferProfileResponse();

            Assert.AreEqual(string.Empty, response.AudioId);
            Assert.AreEqual(0f, response.AveragePitch);
            Assert.AreEqual(0f, response.PitchVariation);
            Assert.AreEqual(0f, response.Energy);
            Assert.AreEqual(0f, response.SpeakingRate);
            Assert.IsNull(response.EmotionTag);
            Assert.IsNull(response.ProsodicFeatures);
            Assert.IsNull(response.StyleEmbedding);
        }

        [TestMethod]
        public void StyleProfileResponse_PropertiesSetCorrectly()
        {
            var prosodicFeatures = new Dictionary<string, object> { { "feature1", 1.0 } };
            var styleEmbedding = new List<float> { 0.1f, 0.2f, 0.3f };

            var response = new VoiceStyleTransferProfileResponse
            {
                AudioId = "audio123",
                AveragePitch = 150.5f,
                PitchVariation = 25.0f,
                Energy = 0.75f,
                SpeakingRate = 1.2f,
                EmotionTag = "happy",
                ProsodicFeatures = prosodicFeatures,
                StyleEmbedding = styleEmbedding
            };

            Assert.AreEqual("audio123", response.AudioId);
            Assert.AreEqual(150.5f, response.AveragePitch);
            Assert.AreEqual(25.0f, response.PitchVariation);
            Assert.AreEqual(0.75f, response.Energy);
            Assert.AreEqual(1.2f, response.SpeakingRate);
            Assert.AreEqual("happy", response.EmotionTag);
            Assert.AreSame(prosodicFeatures, response.ProsodicFeatures);
            Assert.AreSame(styleEmbedding, response.StyleEmbedding);
        }

        #endregion

        #region StyleAnalyzeResponse Model Tests

        [TestMethod]
        public void StyleAnalyzeResponse_DefaultValues()
        {
            var response = new VoiceStyleTransferAnalyzeResponse();

            Assert.AreEqual(string.Empty, response.AudioId);
            Assert.IsNull(response.PitchContour);
            Assert.IsNull(response.EnergyContour);
            Assert.IsNull(response.TimingPatterns);
            Assert.IsNull(response.StyleMarkers);
        }

        [TestMethod]
        public void StyleAnalyzeResponse_PropertiesSetCorrectly()
        {
            var pitchContour = new List<float> { 100f, 120f, 110f };
            var energyContour = new List<float> { 0.5f, 0.7f, 0.6f };
            var timingPatterns = new Dictionary<string, object> { { "pause", 0.5 } };
            var styleMarkers = new List<Dictionary<string, object>>
            {
                new Dictionary<string, object> { { "type", "emphasis" } }
            };

            var response = new VoiceStyleTransferAnalyzeResponse
            {
                AudioId = "audio456",
                PitchContour = pitchContour,
                EnergyContour = energyContour,
                TimingPatterns = timingPatterns,
                StyleMarkers = styleMarkers
            };

            Assert.AreEqual("audio456", response.AudioId);
            Assert.AreSame(pitchContour, response.PitchContour);
            Assert.AreSame(energyContour, response.EnergyContour);
            Assert.AreSame(timingPatterns, response.TimingPatterns);
            Assert.AreSame(styleMarkers, response.StyleMarkers);
        }

        #endregion

        #region StyleProfileItem Model Tests

        [TestMethod]
        public void StyleProfileItem_CreatedFromResponse()
        {
            var response = new VoiceStyleTransferProfileResponse
            {
                AudioId = "a1",
                AveragePitch = 140f,
                PitchVariation = 20f,
                Energy = 0.8f,
                SpeakingRate = 1.1f,
                EmotionTag = "excited"
            };

            var item = new StyleProfileItem(response);

            Assert.AreEqual("a1", item.AudioId);
            Assert.AreEqual(140f, item.AveragePitch);
            Assert.AreEqual(20f, item.PitchVariation);
            Assert.AreEqual(0.8f, item.Energy);
            Assert.AreEqual(1.1f, item.SpeakingRate);
            Assert.AreEqual("excited", item.EmotionTag);
        }

        [TestMethod]
        public void StyleProfileItem_EnergyDisplay_FormatsAsPercent()
        {
            var response = new VoiceStyleTransferProfileResponse { Energy = 0.75f };
            var item = new StyleProfileItem(response);

            Assert.AreEqual("75%", item.EnergyDisplay);
        }

        [TestMethod]
        public void StyleProfileItem_HasEmotion_TrueWhenEmotionSet()
        {
            var response = new VoiceStyleTransferProfileResponse { EmotionTag = "happy" };
            var item = new StyleProfileItem(response);

            Assert.IsTrue(item.HasEmotion);
        }

        [TestMethod]
        public void StyleProfileItem_HasEmotion_FalseWhenEmotionNull()
        {
            var response = new VoiceStyleTransferProfileResponse { EmotionTag = null };
            var item = new StyleProfileItem(response);

            Assert.IsFalse(item.HasEmotion);
        }

        [TestMethod]
        public void StyleProfileItem_HasEmotion_FalseWhenEmotionEmpty()
        {
            var response = new VoiceStyleTransferProfileResponse { EmotionTag = "" };
            var item = new StyleProfileItem(response);

            Assert.IsFalse(item.HasEmotion);
        }

        [TestMethod]
        public void StyleProfileItem_HasEmotion_FalseWhenEmotionWhitespace()
        {
            var response = new VoiceStyleTransferProfileResponse { EmotionTag = "   " };
            var item = new StyleProfileItem(response);

            Assert.IsFalse(item.HasEmotion);
        }

        #endregion

        #region StyleAnalysisItem Model Tests

        [TestMethod]
        public void StyleAnalysisItem_CreatedFromResponse()
        {
            var response = new VoiceStyleTransferAnalyzeResponse
            {
                AudioId = "a1",
                PitchContour = new List<float> { 1f, 2f },
                EnergyContour = new List<float> { 0.5f },
                StyleMarkers = new List<Dictionary<string, object>>
                {
                    new Dictionary<string, object>(),
                    new Dictionary<string, object>()
                }
            };

            var item = new StyleAnalysisItem(response);

            Assert.AreEqual("a1", item.AudioId);
            Assert.AreEqual(2, item.PitchContour?.Count);
            Assert.AreEqual(1, item.EnergyContour?.Count);
            Assert.AreEqual(2, item.StyleMarkers?.Count);
        }

        [TestMethod]
        public void StyleAnalysisItem_MarkerCount_ReturnsCorrectCount()
        {
            var response = new VoiceStyleTransferAnalyzeResponse
            {
                StyleMarkers = new List<Dictionary<string, object>>
                {
                    new Dictionary<string, object>(),
                    new Dictionary<string, object>(),
                    new Dictionary<string, object>()
                }
            };
            var item = new StyleAnalysisItem(response);

            Assert.AreEqual(3, item.MarkerCount);
        }

        [TestMethod]
        public void StyleAnalysisItem_MarkerCount_ZeroWhenNull()
        {
            var response = new VoiceStyleTransferAnalyzeResponse { StyleMarkers = null };
            var item = new StyleAnalysisItem(response);

            Assert.AreEqual(0, item.MarkerCount);
        }

        [TestMethod]
        public void StyleAnalysisItem_HasMarkers_TrueWhenMarkersExist()
        {
            var response = new VoiceStyleTransferAnalyzeResponse
            {
                StyleMarkers = new List<Dictionary<string, object>> { new Dictionary<string, object>() }
            };
            var item = new StyleAnalysisItem(response);

            Assert.IsTrue(item.HasMarkers);
        }

        [TestMethod]
        public void StyleAnalysisItem_HasMarkers_FalseWhenEmpty()
        {
            var response = new VoiceStyleTransferAnalyzeResponse
            {
                StyleMarkers = new List<Dictionary<string, object>>()
            };
            var item = new StyleAnalysisItem(response);

            Assert.IsFalse(item.HasMarkers);
        }

        [TestMethod]
        public void StyleAnalysisItem_HasMarkers_FalseWhenNull()
        {
            var response = new VoiceStyleTransferAnalyzeResponse { StyleMarkers = null };
            var item = new StyleAnalysisItem(response);

            Assert.IsFalse(item.HasMarkers);
        }

        #endregion
    }
}
