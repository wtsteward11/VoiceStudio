using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Collections.ObjectModel;
using VoiceStudio.App.ViewModels;
using VoiceStudio.Core.Models;

namespace VoiceStudio.App.Tests.ViewModels
{
    /// <summary>
    /// Unit tests for VoiceMorph related model classes.
    /// Note: VoiceMorphViewModel requires a WinUI DispatcherQueue that cannot be mocked in unit tests.
    /// These tests focus on the testable model classes used by the ViewModel.
    /// </summary>
    [TestClass]
    public class VoiceMorphModelTests
    {
        #region VoiceBlendItem Model Tests

        [TestMethod]
        public void VoiceBlendItem_DefaultConstructor_InitializesDefaults()
        {
            // Arrange & Act
            var item = new VoiceBlendItem();

            // Assert
            Assert.AreEqual(string.Empty, item.VoiceProfileId);
            Assert.AreEqual(0.0, item.Weight);
        }

        [TestMethod]
        public void VoiceBlendItem_SetProperties_PersistsValues()
        {
            // Arrange & Act
            var item = new VoiceBlendItem
            {
                VoiceProfileId = "voice-001",
                Weight = 0.75
            };

            // Assert
            Assert.AreEqual("voice-001", item.VoiceProfileId);
            Assert.AreEqual(0.75, item.Weight, 0.001);
        }

        [TestMethod]
        public void VoiceBlendItem_WeightDisplay_FormatsAsPercentage()
        {
            // Arrange
            var item = new VoiceBlendItem { Weight = 0.5 };

            // Assert
            Assert.AreEqual("50%", item.WeightDisplay);
        }

        [TestMethod]
        public void VoiceBlendItem_WeightDisplay_At0Percent_Formats()
        {
            // Arrange
            var item = new VoiceBlendItem { Weight = 0.0 };

            // Assert
            Assert.AreEqual("0%", item.WeightDisplay);
        }

        [TestMethod]
        public void VoiceBlendItem_WeightDisplay_At100Percent_Formats()
        {
            // Arrange
            var item = new VoiceBlendItem { Weight = 1.0 };

            // Assert
            Assert.AreEqual("100%", item.WeightDisplay);
        }

        [TestMethod]
        public void VoiceBlendItem_WeightDisplay_At33Percent_FormatsWithoutDecimals()
        {
            // Arrange
            var item = new VoiceBlendItem { Weight = 0.333 };

            // Assert - P0 format rounds to nearest integer percentage
            Assert.AreEqual("33%", item.WeightDisplay);
        }

        [TestMethod]
        public void VoiceBlendItem_WeightDisplay_At67Percent_FormatsWithoutDecimals()
        {
            // Arrange
            var item = new VoiceBlendItem { Weight = 0.666 };

            // Assert
            Assert.AreEqual("67%", item.WeightDisplay);
        }

        #endregion

        #region MorphConfigItem Model - Display Property Tests

        [TestMethod]
        public void MorphConfigItem_WithTargetVoices_VoiceCountDisplay_ShowsCorrectCount()
        {
            // Arrange - Create using MorphConfig inner class
            var config = new VoiceMorphConfig
            {
                ConfigId = "config-001",
                Name = "Test Config",
                SourceAudioId = "audio-001",
                TargetVoices = new VoiceMorphBlend[]
                {
                    new VoiceMorphBlend { VoiceProfileId = "voice-1", Weight = 0.5 },
                    new VoiceMorphBlend { VoiceProfileId = "voice-2", Weight = 0.3 },
                    new VoiceMorphBlend { VoiceProfileId = "voice-3", Weight = 0.2 }
                },
                MorphStrength = 0.8,
                PreserveEmotion = true,
                PreserveProsody = true,
                OutputFormat = "wav"
            };

            // Act
            var item = new MorphConfigItem(config);

            // Assert
            Assert.AreEqual("3 voice(s)", item.VoiceCountDisplay);
        }

        [TestMethod]
        public void MorphConfigItem_MorphStrengthDisplay_FormatsAsPercentage()
        {
            // Arrange
            var config = new VoiceMorphConfig
            {
                ConfigId = "config-002",
                Name = "Strength Test",
                SourceAudioId = "audio-002",
                TargetVoices = System.Array.Empty<VoiceMorphBlend>(),
                MorphStrength = 0.65,
                OutputFormat = "wav"
            };

            // Act
            var item = new MorphConfigItem(config);

            // Assert
            Assert.AreEqual("65%", item.MorphStrengthDisplay);
        }

        [TestMethod]
        public void MorphConfigItem_CopiesAllProperties()
        {
            // Arrange
            var config = new VoiceMorphConfig
            {
                ConfigId = "config-003",
                Name = "Full Config",
                SourceAudioId = "audio-003",
                TargetVoices = new VoiceMorphBlend[]
                {
                    new VoiceMorphBlend { VoiceProfileId = "voice-a", Weight = 0.6 }
                },
                MorphStrength = 0.9,
                PreserveEmotion = true,
                PreserveProsody = false,
                OutputFormat = "mp3"
            };

            // Act
            var item = new MorphConfigItem(config);

            // Assert
            Assert.AreEqual("config-003", item.ConfigId);
            Assert.AreEqual("Full Config", item.Name);
            Assert.AreEqual("audio-003", item.SourceAudioId);
            Assert.AreEqual(1, item.TargetVoices.Count);
            Assert.AreEqual(0.9, item.MorphStrength, 0.001);
            Assert.IsTrue(item.PreserveEmotion);
            Assert.IsFalse(item.PreserveProsody);
            Assert.AreEqual("mp3", item.OutputFormat);
        }

        [TestMethod]
        public void MorphConfigItem_TargetVoices_AreConvertedToVoiceBlendItems()
        {
            // Arrange
            var config = new VoiceMorphConfig
            {
                ConfigId = "config-004",
                Name = "Voice Blend Test",
                SourceAudioId = "audio-004",
                TargetVoices = new VoiceMorphBlend[]
                {
                    new VoiceMorphBlend { VoiceProfileId = "voice-x", Weight = 0.4 },
                    new VoiceMorphBlend { VoiceProfileId = "voice-y", Weight = 0.6 }
                },
                MorphStrength = 0.5,
                OutputFormat = "wav"
            };

            // Act
            var item = new MorphConfigItem(config);

            // Assert
            Assert.AreEqual(2, item.TargetVoices.Count);
            Assert.AreEqual("voice-x", item.TargetVoices[0].VoiceProfileId);
            Assert.AreEqual(0.4, item.TargetVoices[0].Weight, 0.001);
            Assert.AreEqual("voice-y", item.TargetVoices[1].VoiceProfileId);
            Assert.AreEqual(0.6, item.TargetVoices[1].Weight, 0.001);
        }

        #endregion

        #region VoiceMorphBlend Inner Class Tests

        [TestMethod]
        public void VoiceBlend_DefaultValues_AreEmpty()
        {
            // Arrange & Act
            var blend = new VoiceMorphBlend();

            // Assert
            Assert.AreEqual(string.Empty, blend.VoiceProfileId);
            Assert.AreEqual(0.0, blend.Weight);
        }

        [TestMethod]
        public void VoiceBlend_SetProperties_PersistsValues()
        {
            // Arrange & Act
            var blend = new VoiceMorphBlend
            {
                VoiceProfileId = "profile-123",
                Weight = 0.85
            };

            // Assert
            Assert.AreEqual("profile-123", blend.VoiceProfileId);
            Assert.AreEqual(0.85, blend.Weight, 0.001);
        }

        #endregion

        #region VoiceMorphConfig Inner Class Tests

        [TestMethod]
        public void MorphConfig_DefaultValues_AreEmpty()
        {
            // Arrange & Act
            var config = new VoiceMorphConfig();

            // Assert
            Assert.AreEqual(string.Empty, config.ConfigId);
            Assert.AreEqual(string.Empty, config.Name);
            Assert.AreEqual(string.Empty, config.SourceAudioId);
            Assert.IsNotNull(config.TargetVoices);
            Assert.AreEqual(0, config.TargetVoices.Length);
            Assert.AreEqual(0.0, config.MorphStrength);
            Assert.IsFalse(config.PreserveEmotion);
            Assert.IsFalse(config.PreserveProsody);
            Assert.AreEqual("wav", config.OutputFormat);
        }

        [TestMethod]
        public void MorphConfig_SetAllProperties_PersistsValues()
        {
            // Arrange & Act
            var config = new VoiceMorphConfig
            {
                ConfigId = "cfg-001",
                Name = "My Morph Config",
                SourceAudioId = "src-audio",
                TargetVoices = new VoiceMorphBlend[]
                {
                    new VoiceMorphBlend { VoiceProfileId = "v1", Weight = 1.0 }
                },
                MorphStrength = 0.75,
                PreserveEmotion = true,
                PreserveProsody = true,
                OutputFormat = "flac"
            };

            // Assert
            Assert.AreEqual("cfg-001", config.ConfigId);
            Assert.AreEqual("My Morph Config", config.Name);
            Assert.AreEqual("src-audio", config.SourceAudioId);
            Assert.AreEqual(1, config.TargetVoices.Length);
            Assert.AreEqual(0.75, config.MorphStrength, 0.001);
            Assert.IsTrue(config.PreserveEmotion);
            Assert.IsTrue(config.PreserveProsody);
            Assert.AreEqual("flac", config.OutputFormat);
        }

        #endregion

        #region Edge Cases and Boundary Tests

        [TestMethod]
        public void VoiceBlendItem_WeightAbove1_FormatsCorrectly()
        {
            // Arrange - Edge case: weight > 1 (shouldn't happen but model allows)
            var item = new VoiceBlendItem { Weight = 1.5 };

            // Assert
            Assert.AreEqual("150%", item.WeightDisplay);
        }

        [TestMethod]
        public void VoiceBlendItem_NegativeWeight_FormatsCorrectly()
        {
            // Arrange - Edge case: negative weight (shouldn't happen but model allows)
            var item = new VoiceBlendItem { Weight = -0.25 };

            // Assert
            Assert.AreEqual("-25%", item.WeightDisplay);
        }

        [TestMethod]
        public void MorphConfigItem_EmptyTargetVoices_VoiceCountDisplay_ShowsZero()
        {
            // Arrange
            var config = new VoiceMorphConfig
            {
                ConfigId = "empty-config",
                Name = "Empty",
                SourceAudioId = "audio",
                TargetVoices = System.Array.Empty<VoiceMorphBlend>(),
                OutputFormat = "wav"
            };

            // Act
            var item = new MorphConfigItem(config);

            // Assert
            Assert.AreEqual("0 voice(s)", item.VoiceCountDisplay);
        }

        [TestMethod]
        public void MorphConfigItem_SingleTargetVoice_VoiceCountDisplay_ShowsOne()
        {
            // Arrange
            var config = new VoiceMorphConfig
            {
                ConfigId = "single-config",
                Name = "Single",
                SourceAudioId = "audio",
                TargetVoices = new VoiceMorphBlend[]
                {
                    new VoiceMorphBlend { VoiceProfileId = "solo", Weight = 1.0 }
                },
                OutputFormat = "wav"
            };

            // Act
            var item = new MorphConfigItem(config);

            // Assert
            Assert.AreEqual("1 voice(s)", item.VoiceCountDisplay);
        }

        [TestMethod]
        public void MorphConfigItem_MorphStrengthAt0_DisplaysZeroPercent()
        {
            // Arrange
            var config = new VoiceMorphConfig
            {
                ConfigId = "zero-strength",
                Name = "Zero",
                SourceAudioId = "audio",
                TargetVoices = System.Array.Empty<VoiceMorphBlend>(),
                MorphStrength = 0.0,
                OutputFormat = "wav"
            };

            // Act
            var item = new MorphConfigItem(config);

            // Assert
            Assert.AreEqual("0%", item.MorphStrengthDisplay);
        }

        [TestMethod]
        public void MorphConfigItem_MorphStrengthAt1_Displays100Percent()
        {
            // Arrange
            var config = new VoiceMorphConfig
            {
                ConfigId = "full-strength",
                Name = "Full",
                SourceAudioId = "audio",
                TargetVoices = System.Array.Empty<VoiceMorphBlend>(),
                MorphStrength = 1.0,
                OutputFormat = "wav"
            };

            // Act
            var item = new MorphConfigItem(config);

            // Assert
            Assert.AreEqual("100%", item.MorphStrengthDisplay);
        }

        #endregion
    }
}
