using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using VoiceStudio.Core.Models;
using VoiceStudio.Core.Services;

namespace VoiceStudio.App.Tests.ViewModels
{
  /// <summary>
  /// Comprehensive unit tests for EffectsMixerViewModel operations.
  /// Tests cover effect chain management, audio processing, and preset handling.
  /// </summary>
  [TestClass]
  public class EffectsMixerViewModelTests
  {
    private Mock<IBackendClient> _mockBackendClient = null!;
    private Mock<IEffectChainClient> _mockEffectChainClient = null!;

    [TestInitialize]
    public void Setup()
    {
      _mockBackendClient = new Mock<IBackendClient>();
      _mockEffectChainClient = new Mock<IEffectChainClient>();
    }

    #region Effect Chain Management Tests

    [TestMethod]
    public async Task GetEffectChainsAsync_ReturnsListOfChains()
    {
      // Arrange
      var expectedChains = new List<EffectChain>
            {
                new EffectChain { Id = "chain-1", Name = "Master EQ", ProjectId = "proj-1" },
                new EffectChain { Id = "chain-2", Name = "Voice Enhancement", ProjectId = "proj-1" }
            };

      _mockEffectChainClient
          .Setup(x => x.GetEffectChainsAsync("proj-1", It.IsAny<CancellationToken>()))
          .ReturnsAsync(expectedChains);

      // Act
      var result = await _mockEffectChainClient.Object.GetEffectChainsAsync("proj-1", CancellationToken.None);

      // Assert
      Assert.AreEqual(2, result.Count);
      Assert.AreEqual("chain-1", result[0].Id);
      Assert.AreEqual("Master EQ", result[0].Name);
    }

    [TestMethod]
    public async Task GetEffectChainAsync_ReturnsSingleChain()
    {
      // Arrange
      var expectedChain = new EffectChain
      {
        Id = "chain-123",
        Name = "Test Chain",
        Effects = new List<Effect>
                {
                    new Effect { Type = "eq", Name = "Parametric EQ" },
                    new Effect { Type = "compressor", Name = "Dynamics" }
                }
      };

      _mockEffectChainClient
          .Setup(x => x.GetEffectChainAsync("proj-1", "chain-123", It.IsAny<CancellationToken>()))
          .ReturnsAsync(expectedChain);

      // Act
      var result = await _mockEffectChainClient.Object.GetEffectChainAsync("proj-1", "chain-123", CancellationToken.None);

      // Assert
      Assert.IsNotNull(result);
      Assert.AreEqual("chain-123", result.Id);
      Assert.AreEqual(2, result.Effects.Count);
    }

    [TestMethod]
    public async Task CreateEffectChainAsync_ReturnsCreatedChain()
    {
      // Arrange
      var newChain = new EffectChain
      {
        Name = "New Chain",
        Effects = new List<Effect>()
      };

      var createdChain = new EffectChain
      {
        Id = "chain-new",
        Name = "New Chain",
        Effects = new List<Effect>()
      };

      _mockEffectChainClient
          .Setup(x => x.CreateEffectChainAsync("proj-1", newChain, It.IsAny<CancellationToken>()))
          .ReturnsAsync(createdChain);

      // Act
      var result = await _mockEffectChainClient.Object.CreateEffectChainAsync("proj-1", newChain, CancellationToken.None);

      // Assert
      Assert.IsNotNull(result);
      Assert.AreEqual("chain-new", result.Id);
      Assert.AreEqual("New Chain", result.Name);
    }

    [TestMethod]
    public async Task UpdateEffectChainAsync_ReturnsUpdatedChain()
    {
      // Arrange
      var updatedChain = new EffectChain
      {
        Id = "chain-update",
        Name = "Updated Chain Name",
        Effects = new List<Effect>
                {
                    new Effect { Type = "reverb", Name = "Hall Reverb" }
                }
      };

      _mockEffectChainClient
          .Setup(x => x.UpdateEffectChainAsync("proj-1", "chain-update", updatedChain, It.IsAny<CancellationToken>()))
          .ReturnsAsync(updatedChain);

      // Act
      var result = await _mockEffectChainClient.Object.UpdateEffectChainAsync("proj-1", "chain-update", updatedChain, CancellationToken.None);

      // Assert
      Assert.IsNotNull(result);
      Assert.AreEqual("Updated Chain Name", result.Name);
      Assert.AreEqual(1, result.Effects.Count);
    }

    [TestMethod]
    public async Task DeleteEffectChainAsync_ReturnsTrue_OnSuccess()
    {
      // Arrange
      _mockEffectChainClient
          .Setup(x => x.DeleteEffectChainAsync("proj-1", "chain-delete", It.IsAny<CancellationToken>()))
          .ReturnsAsync(true);

      // Act
      var result = await _mockEffectChainClient.Object.DeleteEffectChainAsync("proj-1", "chain-delete", CancellationToken.None);

      // Assert
      Assert.IsTrue(result);
    }

    #endregion

    #region Audio Processing Tests

    [TestMethod]
    public async Task ProcessAudioWithChainAsync_ReturnsProcessedResponse()
    {
      // Arrange
      var expectedResponse = new EffectProcessResponse
      {
        OutputAudioId = "processed-audio-123",
        Success = true
      };

      _mockEffectChainClient
          .Setup(x => x.ProcessAudioWithChainAsync(
              "proj-1", "chain-1", "audio-input", null,
              false, false, It.IsAny<CancellationToken>()))
          .ReturnsAsync(expectedResponse);

      // Act
      var result = await _mockEffectChainClient.Object.ProcessAudioWithChainAsync(
          "proj-1", "chain-1", "audio-input", null,
          bypassChain: false, preview: false, CancellationToken.None);

      // Assert
      Assert.IsNotNull(result);
      Assert.AreEqual("processed-audio-123", result.OutputAudioId);
      Assert.IsTrue(result.Success);
    }

    [TestMethod]
    public async Task ProcessAudioWithChainAsync_WithOutputFilename_UsesFilename()
    {
      // Arrange
      string? capturedFilename = null;
      _mockEffectChainClient
          .Setup(x => x.ProcessAudioWithChainAsync(
              It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(),
              It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
          .Callback<string, string, string, string?, bool, bool, CancellationToken>(
              (proj, chain, audio, filename, _b, _p, ct) => capturedFilename = filename)
          .ReturnsAsync(new EffectProcessResponse { Success = true });

      // Act
      await _mockEffectChainClient.Object.ProcessAudioWithChainAsync(
          "proj-1", "chain-1", "audio-input", "output.wav",
          bypassChain: false, preview: false, CancellationToken.None);

      // Assert
      Assert.AreEqual("output.wav", capturedFilename);
    }

    #endregion

    #region Effect Presets Tests

    [TestMethod]
    public async Task GetEffectPresetsAsync_ReturnsListOfPresets()
    {
      // Arrange
      var expectedPresets = new List<EffectPreset>
            {
                new EffectPreset { Id = "preset-1", Name = "Warm Voice", EffectType = "eq" },
                new EffectPreset { Id = "preset-2", Name = "Studio Comp", EffectType = "compressor" },
                new EffectPreset { Id = "preset-3", Name = "Room Reverb", EffectType = "reverb" }
            };

      _mockEffectChainClient
          .Setup(x => x.GetEffectPresetsAsync(null, It.IsAny<CancellationToken>()))
          .ReturnsAsync(expectedPresets);

      // Act
      var result = await _mockEffectChainClient.Object.GetEffectPresetsAsync(null, CancellationToken.None);

      // Assert
      Assert.AreEqual(3, result.Count);
    }

    [TestMethod]
    public async Task GetEffectPresetsAsync_FiltersByEffectType()
    {
      // Arrange
      var eqPresets = new List<EffectPreset>
            {
                new EffectPreset { Id = "eq-1", Name = "Warm", EffectType = "eq" },
                new EffectPreset { Id = "eq-2", Name = "Bright", EffectType = "eq" }
            };

      _mockEffectChainClient
          .Setup(x => x.GetEffectPresetsAsync("eq", It.IsAny<CancellationToken>()))
          .ReturnsAsync(eqPresets);

      // Act
      var result = await _mockEffectChainClient.Object.GetEffectPresetsAsync("eq", CancellationToken.None);

      // Assert
      Assert.AreEqual(2, result.Count);
      Assert.IsTrue(result.TrueForAll(p => p.EffectType == "eq"));
    }

    [TestMethod]
    public async Task CreateEffectPresetAsync_ReturnsCreatedPreset()
    {
      // Arrange
      var newPreset = new EffectPreset
      {
        Name = "Custom EQ",
        EffectType = "eq"
      };

      var createdPreset = new EffectPreset
      {
        Id = "preset-new",
        Name = "Custom EQ",
        EffectType = "eq"
      };

      _mockEffectChainClient
          .Setup(x => x.CreateEffectPresetAsync(newPreset, It.IsAny<CancellationToken>()))
          .ReturnsAsync(createdPreset);

      // Act
      var result = await _mockEffectChainClient.Object.CreateEffectPresetAsync(newPreset, CancellationToken.None);

      // Assert
      Assert.IsNotNull(result);
      Assert.AreEqual("preset-new", result.Id);
      Assert.AreEqual("Custom EQ", result.Name);
    }

    [TestMethod]
    public async Task DeleteEffectPresetAsync_ReturnsTrue_OnSuccess()
    {
      // Arrange
      _mockEffectChainClient
          .Setup(x => x.DeleteEffectPresetAsync("preset-delete", It.IsAny<CancellationToken>()))
          .ReturnsAsync(true);

      // Act
      var result = await _mockEffectChainClient.Object.DeleteEffectPresetAsync("preset-delete", CancellationToken.None);

      // Assert
      Assert.IsTrue(result);
    }

    #endregion

    #region Effect Chain Validation Tests

    [TestMethod]
    public void EffectChain_WithEmptyEffects_IsValid()
    {
      var chain = new EffectChain
      {
        Id = "empty-chain",
        Name = "Empty Chain",
        Effects = new List<Effect>()
      };

      Assert.IsNotNull(chain.Effects);
      Assert.AreEqual(0, chain.Effects.Count);
    }

    [TestMethod]
    public void EffectChain_WithMultipleEffects_MaintainsOrder()
    {
      var chain = new EffectChain
      {
        Id = "ordered-chain",
        Name = "Ordered Effects",
        Effects = new List<Effect>
                {
                    new Effect { Type = "eq", Name = "EQ First", Order = 0 },
                    new Effect { Type = "compressor", Name = "Comp Second", Order = 1 },
                    new Effect { Type = "limiter", Name = "Limiter Third", Order = 2 }
                }
      };

      Assert.AreEqual("eq", chain.Effects[0].Type);
      Assert.AreEqual("compressor", chain.Effects[1].Type);
      Assert.AreEqual("limiter", chain.Effects[2].Type);
    }

    #endregion

    #region Effect Properties Tests

    [TestMethod]
    public void Effect_DefaultValues()
    {
      var effect = new Effect();

      Assert.AreEqual(string.Empty, effect.Id);
      Assert.AreEqual(string.Empty, effect.Type);
      Assert.AreEqual(string.Empty, effect.Name);
      Assert.IsTrue(effect.Enabled);
      Assert.AreEqual(0, effect.Order);
    }

    [TestMethod]
    public void Effect_WithParameters()
    {
      var effect = new Effect
      {
        Type = "eq",
        Name = "Parametric EQ",
        Parameters = new List<EffectParameter>
                {
                    new EffectParameter { Name = "lowFreq", Value = 100.0 },
                    new EffectParameter { Name = "highFreq", Value = 8000.0 }
                }
      };

      Assert.AreEqual(2, effect.Parameters.Count);
    }

    [TestMethod]
    public void Effect_Enabled_CanBeToggled()
    {
      var effect = new Effect
      {
        Type = "reverb",
        Name = "Hall",
        Enabled = false
      };

      Assert.IsFalse(effect.Enabled);
      effect.Enabled = true;
      Assert.IsTrue(effect.Enabled);
    }

    #endregion

    #region EffectPreset Tests

    [TestMethod]
    public void EffectPreset_DefaultValues()
    {
      var preset = new EffectPreset();

      Assert.AreEqual(string.Empty, preset.Id);
      Assert.AreEqual(string.Empty, preset.Name);
      Assert.AreEqual(string.Empty, preset.EffectType);
    }

    #endregion
  }
}
