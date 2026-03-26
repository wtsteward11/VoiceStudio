using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using VoiceStudio.App.ViewModels;

namespace VoiceStudio.App.Tests.ViewModels
{
    [TestClass]
    public class EnsembleSynthesisModelTests
    {
        #region EnsembleJobStatus Model Tests

        [TestMethod]
        public void EnsembleJobStatus_DefaultValues()
        {
            var status = new VoiceStudio.App.Services.EnsembleJobStatus();

            Assert.AreEqual(string.Empty, status.JobId);
            Assert.AreEqual(string.Empty, status.Status);
            Assert.AreEqual(0.0, status.Progress);
            Assert.AreEqual(0, status.CompletedVoices);
            Assert.AreEqual(0, status.TotalVoices);
            Assert.IsNotNull(status.AudioIds);
            Assert.AreEqual(0, status.AudioIds.Length);
            Assert.IsNull(status.Error);
            Assert.AreEqual(string.Empty, status.Created);
            Assert.AreEqual(string.Empty, status.Updated);
        }

        [TestMethod]
        public void EnsembleJobStatus_PropertiesSetCorrectly()
        {
            var audioIds = new[] { "audio1", "audio2" };
            var status = new VoiceStudio.App.Services.EnsembleJobStatus
            {
                JobId = "job123",
                Status = "running",
                Progress = 0.75,
                CompletedVoices = 3,
                TotalVoices = 4,
                AudioIds = audioIds,
                Error = null,
                Created = "2026-01-01T00:00:00Z",
                Updated = "2026-01-01T01:00:00Z"
            };

            Assert.AreEqual("job123", status.JobId);
            Assert.AreEqual("running", status.Status);
            Assert.AreEqual(0.75, status.Progress);
            Assert.AreEqual(3, status.CompletedVoices);
            Assert.AreEqual(4, status.TotalVoices);
            Assert.AreSame(audioIds, status.AudioIds);
            Assert.AreEqual("2026-01-01T00:00:00Z", status.Created);
            Assert.AreEqual("2026-01-01T01:00:00Z", status.Updated);
        }

        [TestMethod]
        public void EnsembleJobStatus_ErrorCanBeSet()
        {
            var status = new VoiceStudio.App.Services.EnsembleJobStatus { Error = "Job failed" };
            Assert.AreEqual("Job failed", status.Error);
        }

        #endregion

        #region EnsembleVoiceItem Model Tests

        [TestMethod]
        public void EnsembleVoiceItem_DefaultValues()
        {
            var item = new EnsembleVoiceItem();

            Assert.AreEqual(string.Empty, item.ProfileId);
            Assert.AreEqual(string.Empty, item.Text);
            Assert.AreEqual("xtts", item.Engine);
            Assert.AreEqual("en", item.Language);
            Assert.IsNull(item.Emotion);
        }

        [TestMethod]
        public void EnsembleVoiceItem_PropertiesSetCorrectly()
        {
            var item = new EnsembleVoiceItem
            {
                ProfileId = "profile1",
                Text = "Hello world",
                Engine = "piper",
                Language = "es",
                Emotion = "happy"
            };

            Assert.AreEqual("profile1", item.ProfileId);
            Assert.AreEqual("Hello world", item.Text);
            Assert.AreEqual("piper", item.Engine);
            Assert.AreEqual("es", item.Language);
            Assert.AreEqual("happy", item.Emotion);
        }

        [TestMethod]
        public void EnsembleVoiceItem_EmotionIsOptional()
        {
            var item = new EnsembleVoiceItem { Emotion = null };
            Assert.IsNull(item.Emotion);

            item.Emotion = "sad";
            Assert.AreEqual("sad", item.Emotion);
        }

        #endregion

        #region EnsembleJobItem Model Tests

        [TestMethod]
        public void EnsembleJobItem_CreatedFromJobStatus()
        {
            var status = new VoiceStudio.App.Services.EnsembleJobStatus
            {
                JobId = "job456",
                Status = "completed",
                Progress = 1.0,
                CompletedVoices = 5,
                TotalVoices = 5,
                AudioIds = new[] { "a1", "a2", "a3" },
                Error = null,
                Created = "2026-02-01",
                Updated = "2026-02-02"
            };

            var item = new EnsembleJobItem(status);

            Assert.AreEqual("job456", item.JobId);
            Assert.AreEqual("completed", item.Status);
            Assert.AreEqual(1.0, item.Progress);
            Assert.AreEqual(5, item.CompletedVoices);
            Assert.AreEqual(5, item.TotalVoices);
            Assert.AreEqual(3, item.AudioIds.Length);
            Assert.IsNull(item.Error);
            Assert.AreEqual("2026-02-01", item.Created);
            Assert.AreEqual("2026-02-02", item.Updated);
        }

        [TestMethod]
        public void EnsembleJobItem_ProgressDisplay_FormatsAsPercent()
        {
            var status = new VoiceStudio.App.Services.EnsembleJobStatus { Progress = 0.75 };
            var item = new EnsembleJobItem(status);

            Assert.AreEqual("75%", item.ProgressDisplay);
        }

        [TestMethod]
        public void EnsembleJobItem_ProgressDisplay_ZeroPercent()
        {
            var status = new VoiceStudio.App.Services.EnsembleJobStatus { Progress = 0 };
            var item = new EnsembleJobItem(status);

            Assert.AreEqual("0%", item.ProgressDisplay);
        }

        [TestMethod]
        public void EnsembleJobItem_ProgressDisplay_HundredPercent()
        {
            var status = new VoiceStudio.App.Services.EnsembleJobStatus { Progress = 1.0 };
            var item = new EnsembleJobItem(status);

            Assert.AreEqual("100%", item.ProgressDisplay);
        }

        [TestMethod]
        public void EnsembleJobItem_VoicesDisplay_FormatsCorrectly()
        {
            var status = new VoiceStudio.App.Services.EnsembleJobStatus
            {
                CompletedVoices = 3,
                TotalVoices = 10
            };
            var item = new EnsembleJobItem(status);

            Assert.AreEqual("3/10 voices", item.VoicesDisplay);
        }

        [TestMethod]
        public void EnsembleJobItem_VoicesDisplay_AllCompleted()
        {
            var status = new VoiceStudio.App.Services.EnsembleJobStatus
            {
                CompletedVoices = 5,
                TotalVoices = 5
            };
            var item = new EnsembleJobItem(status);

            Assert.AreEqual("5/5 voices", item.VoicesDisplay);
        }

        [TestMethod]
        public void EnsembleJobItem_VoicesDisplay_NoneCompleted()
        {
            var status = new VoiceStudio.App.Services.EnsembleJobStatus
            {
                CompletedVoices = 0,
                TotalVoices = 5
            };
            var item = new EnsembleJobItem(status);

            Assert.AreEqual("0/5 voices", item.VoicesDisplay);
        }

        [TestMethod]
        public void EnsembleJobItem_ErrorFromStatus()
        {
            var status = new VoiceStudio.App.Services.EnsembleJobStatus { Error = "Connection timeout" };
            var item = new EnsembleJobItem(status);

            Assert.AreEqual("Connection timeout", item.Error);
        }

        #endregion
    }
}
