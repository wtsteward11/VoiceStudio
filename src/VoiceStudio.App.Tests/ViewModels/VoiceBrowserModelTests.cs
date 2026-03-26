using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using VoiceStudio.App.Services;
using VoiceStudio.App.ViewModels;

namespace VoiceStudio.App.Tests.ViewModels
{
    [TestClass]
    public class VoiceBrowserModelTests
    {
        #region VoiceProfileSummary Model Tests

        [TestMethod]
        public void VoiceProfileSummary_DefaultValues()
        {
            var summary = new VoiceProfileSummary();

            Assert.AreEqual(string.Empty, summary.Id);
            Assert.AreEqual(string.Empty, summary.Name);
            Assert.IsNull(summary.Description);
            Assert.AreEqual(string.Empty, summary.Language);
            Assert.IsNull(summary.Gender);
            Assert.IsNull(summary.AgeRange);
            Assert.AreEqual(0.0, summary.QualityScore);
            Assert.AreEqual(0, summary.SampleCount);
            Assert.IsNotNull(summary.Tags);
            Assert.AreEqual(0, summary.Tags.Length);
            Assert.IsNull(summary.PreviewAudioId);
            Assert.AreEqual(string.Empty, summary.Created);
        }

        [TestMethod]
        public void VoiceProfileSummary_PropertiesSetCorrectly()
        {
            var tags = new[] { "english", "female" };
            var summary = new VoiceProfileSummary
            {
                Id = "profile123",
                Name = "Sarah",
                Description = "A friendly voice",
                Language = "en-US",
                Gender = "Female",
                AgeRange = "25-35",
                QualityScore = 4.5,
                SampleCount = 10,
                Tags = tags,
                PreviewAudioId = "audio123",
                Created = "2026-01-01"
            };

            Assert.AreEqual("profile123", summary.Id);
            Assert.AreEqual("Sarah", summary.Name);
            Assert.AreEqual("A friendly voice", summary.Description);
            Assert.AreEqual("en-US", summary.Language);
            Assert.AreEqual("Female", summary.Gender);
            Assert.AreEqual("25-35", summary.AgeRange);
            Assert.AreEqual(4.5, summary.QualityScore);
            Assert.AreEqual(10, summary.SampleCount);
            Assert.AreSame(tags, summary.Tags);
            Assert.AreEqual("audio123", summary.PreviewAudioId);
            Assert.AreEqual("2026-01-01", summary.Created);
        }

        #endregion

        #region VoiceProfileSummaryItem Model Tests

        [TestMethod]
        public void VoiceProfileSummaryItem_CreatedFromSummary()
        {
            var summary = new VoiceProfileSummary
            {
                Id = "p1",
                Name = "John",
                Description = "Deep voice",
                Language = "en",
                Gender = "Male",
                AgeRange = "30-40",
                QualityScore = 3.8,
                SampleCount = 5,
                Tags = new[] { "male", "deep" },
                PreviewAudioId = "preview1",
                Created = "2026-01-01"
            };

            var item = new VoiceProfileSummaryItem(summary);

            Assert.AreEqual("p1", item.Id);
            Assert.AreEqual("John", item.Name);
            Assert.AreEqual("Deep voice", item.Description);
            Assert.AreEqual("en", item.Language);
            Assert.AreEqual("Male", item.Gender);
            Assert.AreEqual("30-40", item.AgeRange);
            Assert.AreEqual(3.8, item.QualityScore);
            Assert.AreEqual(5, item.SampleCount);
            Assert.AreEqual(2, item.Tags.Count);
            Assert.AreEqual("preview1", item.PreviewAudioId);
            Assert.AreEqual("2026-01-01", item.Created);
        }

        [TestMethod]
        public void VoiceProfileSummaryItem_QualityScoreDisplay_FormatsCorrectly()
        {
            var summary = new VoiceProfileSummary { QualityScore = 4.567 };
            var item = new VoiceProfileSummaryItem(summary);

            Assert.AreEqual("4.57", item.QualityScoreDisplay);
        }

        [TestMethod]
        public void VoiceProfileSummaryItem_QualityScoreDisplay_Zero()
        {
            var summary = new VoiceProfileSummary { QualityScore = 0 };
            var item = new VoiceProfileSummaryItem(summary);

            Assert.AreEqual("0.00", item.QualityScoreDisplay);
        }

        [TestMethod]
        public void VoiceProfileSummaryItem_SampleCountDisplay_FormatsCorrectly()
        {
            var summary = new VoiceProfileSummary { SampleCount = 15 };
            var item = new VoiceProfileSummaryItem(summary);

            Assert.AreEqual("15 samples", item.SampleCountDisplay);
        }

        [TestMethod]
        public void VoiceProfileSummaryItem_SampleCountDisplay_ZeroSamples()
        {
            var summary = new VoiceProfileSummary { SampleCount = 0 };
            var item = new VoiceProfileSummaryItem(summary);

            Assert.AreEqual("0 samples", item.SampleCountDisplay);
        }

        [TestMethod]
        public void VoiceProfileSummaryItem_SampleCountDisplay_OneSample()
        {
            var summary = new VoiceProfileSummary { SampleCount = 1 };
            var item = new VoiceProfileSummaryItem(summary);

            Assert.AreEqual("1 samples", item.SampleCountDisplay);
        }

        [TestMethod]
        public void VoiceProfileSummaryItem_TagsConvertedToObservableCollection()
        {
            var summary = new VoiceProfileSummary { Tags = new[] { "a", "b", "c" } };
            var item = new VoiceProfileSummaryItem(summary);

            Assert.AreEqual(3, item.Tags.Count);
            Assert.AreEqual("a", item.Tags[0]);
            Assert.AreEqual("b", item.Tags[1]);
            Assert.AreEqual("c", item.Tags[2]);
        }

        #endregion
    }
}
