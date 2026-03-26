using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Collections.Generic;
using VoiceStudio.App.Services;
using VoiceStudio.App.ViewModels;

namespace VoiceStudio.App.Tests.ViewModels
{
    [TestClass]
    public class MultiVoiceGeneratorModelTests
    {
        #region MultiVoiceResultItem Model Tests

        [TestMethod]
        public void MultiVoiceResultItem_DefaultValues()
        {
            var data = new MultiVoiceResultItem();

            Assert.AreEqual(string.Empty, data.ItemId);
            Assert.AreEqual(string.Empty, data.ProfileId);
            Assert.AreEqual(string.Empty, data.Text);
            Assert.AreEqual(string.Empty, data.Engine);
            Assert.AreEqual(string.Empty, data.QualityMode);
            Assert.AreEqual(string.Empty, data.Language);
            Assert.IsNull(data.Emotion);
            Assert.IsNull(data.AudioId);
            Assert.IsNull(data.AudioUrl);
            Assert.IsNull(data.QualityScore);
            Assert.IsNull(data.QualityMetrics);
        }

        [TestMethod]
        public void MultiVoiceResultItem_PropertiesSetCorrectly()
        {
            var metrics = new Dictionary<string, object> { { "mos", 4.5 } };
            var data = new MultiVoiceResultItem
            {
                ItemId = "item1",
                ProfileId = "profile1",
                Text = "Hello world",
                Engine = "xtts",
                QualityMode = "high",
                Language = "en",
                Emotion = "happy",
                AudioId = "audio123",
                AudioUrl = "http://example.com/audio.wav",
                QualityScore = 4.5f,
                QualityMetrics = metrics
            };

            Assert.AreEqual("item1", data.ItemId);
            Assert.AreEqual("profile1", data.ProfileId);
            Assert.AreEqual("Hello world", data.Text);
            Assert.AreEqual("xtts", data.Engine);
            Assert.AreEqual("high", data.QualityMode);
            Assert.AreEqual("en", data.Language);
            Assert.AreEqual("happy", data.Emotion);
            Assert.AreEqual("audio123", data.AudioId);
            Assert.AreEqual("http://example.com/audio.wav", data.AudioUrl);
            Assert.AreEqual(4.5f, data.QualityScore);
            Assert.AreSame(metrics, data.QualityMetrics);
        }

        #endregion

        #region VoiceGenerationItem Model Tests

        [TestMethod]
        public void VoiceGenerationItem_DefaultValues()
        {
            var item = new VoiceGenerationItem();

            Assert.AreEqual(string.Empty, item.ItemId);
            Assert.AreEqual(string.Empty, item.ProfileId);
            Assert.AreEqual(string.Empty, item.Text);
            Assert.AreEqual(string.Empty, item.Engine);
            Assert.AreEqual(string.Empty, item.QualityMode);
            Assert.AreEqual(string.Empty, item.Language);
            Assert.IsNull(item.Emotion);
            Assert.AreEqual(string.Empty, item.Status);
            Assert.AreEqual(0f, item.Progress);
            Assert.IsNull(item.AudioId);
            Assert.IsNull(item.AudioUrl);
            Assert.IsNull(item.QualityScore);
        }

        [TestMethod]
        public void VoiceGenerationItem_StatusDisplay_UpperCase()
        {
            var item = new VoiceGenerationItem { Status = "running" };
            Assert.AreEqual("RUNNING", item.StatusDisplay);
        }

        [TestMethod]
        public void VoiceGenerationItem_ProgressDisplay_FormatsAsPercent()
        {
            var item = new VoiceGenerationItem { Progress = 0.75f };
            Assert.AreEqual("75%", item.ProgressDisplay);
        }

        [TestMethod]
        public void VoiceGenerationItem_ProgressDisplay_Zero()
        {
            var item = new VoiceGenerationItem { Progress = 0f };
            Assert.AreEqual("0%", item.ProgressDisplay);
        }

        [TestMethod]
        public void VoiceGenerationItem_ProgressDisplay_Hundred()
        {
            var item = new VoiceGenerationItem { Progress = 1.0f };
            Assert.AreEqual("100%", item.ProgressDisplay);
        }

        [TestMethod]
        public void VoiceGenerationItem_QualityScoreDisplay_WithValue()
        {
            var item = new VoiceGenerationItem { QualityScore = 4.25f };
            Assert.AreEqual("4.25", item.QualityScoreDisplay);
        }

        [TestMethod]
        public void VoiceGenerationItem_QualityScoreDisplay_NullReturnsNA()
        {
            var item = new VoiceGenerationItem { QualityScore = null };
            Assert.AreEqual("N/A", item.QualityScoreDisplay);
        }

        #endregion
    }
}
