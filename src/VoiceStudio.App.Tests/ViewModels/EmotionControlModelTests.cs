using Microsoft.VisualStudio.TestTools.UnitTesting;
using VoiceStudio.App.ViewModels;
using VoiceStudio.Core.Models;

namespace VoiceStudio.App.Tests.ViewModels
{
    [TestClass]
    public class EmotionControlModelTests
    {
        #region EmotionPresetCreateRequest Model Tests

        [TestMethod]
        public void EmotionPresetCreateRequest_DefaultValues()
        {
            var request = new EmotionPresetCreateRequest();

            Assert.AreEqual(string.Empty, request.Name);
            Assert.IsNull(request.Description);
            Assert.AreEqual(string.Empty, request.PrimaryEmotion);
            Assert.AreEqual(0.0, request.PrimaryIntensity);
            Assert.IsNull(request.SecondaryEmotion);
            Assert.AreEqual(0.0, request.SecondaryIntensity);
        }

        [TestMethod]
        public void EmotionPresetCreateRequest_PropertiesSetCorrectly()
        {
            var request = new EmotionPresetCreateRequest
            {
                Name = "Happy Medium",
                Description = "A moderately happy tone",
                PrimaryEmotion = "happy",
                PrimaryIntensity = 70.0,
                SecondaryEmotion = "excited",
                SecondaryIntensity = 30.0
            };

            Assert.AreEqual("Happy Medium", request.Name);
            Assert.AreEqual("A moderately happy tone", request.Description);
            Assert.AreEqual("happy", request.PrimaryEmotion);
            Assert.AreEqual(70.0, request.PrimaryIntensity);
            Assert.AreEqual("excited", request.SecondaryEmotion);
            Assert.AreEqual(30.0, request.SecondaryIntensity);
        }

        #endregion

        #region EmotionPreset Model Tests

        [TestMethod]
        public void EmotionPreset_DefaultValues()
        {
            var preset = new EmotionPreset();

            Assert.AreEqual(string.Empty, preset.PresetId);
            Assert.AreEqual(string.Empty, preset.Name);
            Assert.IsNull(preset.Description);
            Assert.AreEqual(string.Empty, preset.PrimaryEmotion);
            Assert.AreEqual(0.0, preset.PrimaryIntensity);
            Assert.IsNull(preset.SecondaryEmotion);
            Assert.AreEqual(0.0, preset.SecondaryIntensity);
            Assert.AreEqual(string.Empty, preset.CreatedAt);
            Assert.AreEqual(string.Empty, preset.UpdatedAt);
        }

        [TestMethod]
        public void EmotionPreset_PropertiesSetCorrectly()
        {
            var preset = new EmotionPreset
            {
                PresetId = "preset123",
                Name = "Sad Intense",
                Description = "An intense sad tone",
                PrimaryEmotion = "sad",
                PrimaryIntensity = 90.0,
                SecondaryEmotion = "melancholy",
                SecondaryIntensity = 40.0,
                CreatedAt = "2026-01-01T00:00:00Z",
                UpdatedAt = "2026-01-02T00:00:00Z"
            };

            Assert.AreEqual("preset123", preset.PresetId);
            Assert.AreEqual("Sad Intense", preset.Name);
            Assert.AreEqual("An intense sad tone", preset.Description);
            Assert.AreEqual("sad", preset.PrimaryEmotion);
            Assert.AreEqual(90.0, preset.PrimaryIntensity);
            Assert.AreEqual("melancholy", preset.SecondaryEmotion);
            Assert.AreEqual(40.0, preset.SecondaryIntensity);
            Assert.AreEqual("2026-01-01T00:00:00Z", preset.CreatedAt);
            Assert.AreEqual("2026-01-02T00:00:00Z", preset.UpdatedAt);
        }

        #endregion

        #region EmotionControlPresetItem Model Tests

        [TestMethod]
        public void EmotionControlPresetItem_CreatedFromEmotionPreset()
        {
            var preset = new EmotionPreset
            {
                PresetId = "p1",
                Name = "Test Preset",
                Description = "Test description",
                PrimaryEmotion = "anger",
                PrimaryIntensity = 80.0,
                SecondaryEmotion = "frustration",
                SecondaryIntensity = 20.0,
                CreatedAt = "2026-01-01",
                UpdatedAt = "2026-01-02"
            };

            var item = new EmotionControlPresetItem(preset);

            Assert.AreEqual("p1", item.PresetId);
            Assert.AreEqual("Test Preset", item.Name);
            Assert.AreEqual("Test description", item.Description);
            Assert.AreEqual("anger", item.PrimaryEmotion);
            Assert.AreEqual(80f, item.PrimaryIntensity);
            Assert.AreEqual("frustration", item.SecondaryEmotion);
            Assert.AreEqual(20f, item.SecondaryIntensity);
            Assert.AreEqual("2026-01-01", item.CreatedAt);
            Assert.AreEqual("2026-01-02", item.UpdatedAt);
        }

        [TestMethod]
        public void EmotionControlPresetItem_PrimaryEmotionDisplay_UpperCase()
        {
            var preset = new EmotionPreset
            {
                PrimaryEmotion = "happy"
            };
            var item = new EmotionControlPresetItem(preset);

            Assert.AreEqual("HAPPY", item.PrimaryEmotionDisplay);
        }

        [TestMethod]
        public void EmotionControlPresetItem_PrimaryEmotionDisplay_MixedCase()
        {
            var preset = new EmotionPreset
            {
                PrimaryEmotion = "Excited"
            };
            var item = new EmotionControlPresetItem(preset);

            Assert.AreEqual("EXCITED", item.PrimaryEmotionDisplay);
        }

        [TestMethod]
        public void EmotionControlPresetItem_PrimaryIntensityDisplay_FormatsAsPercent()
        {
            var preset = new EmotionPreset
            {
                PrimaryIntensity = 75.0
            };
            var item = new EmotionControlPresetItem(preset);

            Assert.AreEqual("75%", item.PrimaryIntensityDisplay);
        }

        [TestMethod]
        public void EmotionControlPresetItem_PrimaryIntensityDisplay_Zero()
        {
            var preset = new EmotionPreset
            {
                PrimaryIntensity = 0.0
            };
            var item = new EmotionControlPresetItem(preset);

            Assert.AreEqual("0%", item.PrimaryIntensityDisplay);
        }

        [TestMethod]
        public void EmotionControlPresetItem_SecondaryEmotionDisplay_WithSecondary()
        {
            var preset = new EmotionPreset
            {
                SecondaryEmotion = "calm"
            };
            var item = new EmotionControlPresetItem(preset);

            Assert.AreEqual("CALM", item.SecondaryEmotionDisplay);
        }

        [TestMethod]
        public void EmotionControlPresetItem_SecondaryEmotionDisplay_NullShowsNone()
        {
            var preset = new EmotionPreset
            {
                SecondaryEmotion = null
            };
            var item = new EmotionControlPresetItem(preset);

            Assert.AreEqual("None", item.SecondaryEmotionDisplay);
        }

        [TestMethod]
        public void EmotionControlPresetItem_SecondaryIntensityDisplay_WithIntensity()
        {
            var preset = new EmotionPreset
            {
                SecondaryIntensity = 50.0
            };
            var item = new EmotionControlPresetItem(preset);

            Assert.AreEqual("50%", item.SecondaryIntensityDisplay);
        }

        [TestMethod]
        public void EmotionControlPresetItem_SecondaryIntensityDisplay_ZeroShowsZero()
        {
            var preset = new EmotionPreset
            {
                SecondaryIntensity = 0.0
            };
            var item = new EmotionControlPresetItem(preset);

            Assert.AreEqual("0%", item.SecondaryIntensityDisplay);
        }

        [TestMethod]
        public void EmotionControlPresetItem_BlendingDisplay_WithSecondary()
        {
            var preset = new EmotionPreset
            {
                PrimaryEmotion = "happy",
                SecondaryEmotion = "excited"
            };
            var item = new EmotionControlPresetItem(preset);

            Assert.AreEqual("HAPPY + EXCITED", item.BlendingDisplay);
        }

        [TestMethod]
        public void EmotionControlPresetItem_BlendingDisplay_WithoutSecondary()
        {
            var preset = new EmotionPreset
            {
                PrimaryEmotion = "sad",
                SecondaryEmotion = null
            };
            var item = new EmotionControlPresetItem(preset);

            Assert.AreEqual("SAD", item.BlendingDisplay);
        }

        #endregion
    }
}
