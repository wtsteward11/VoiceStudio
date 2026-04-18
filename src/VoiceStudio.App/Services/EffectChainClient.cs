using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using VoiceStudio.Core.Exceptions;
using VoiceStudio.Core.Models;
using VoiceStudio.Core.Services;

namespace VoiceStudio.App.Services
{
  /// <summary>
  /// Client for Effects Mixer effect chain API. PR-11: owns HTTP via BackendClientHttpPipeline; no IBackendClient delegation.
  /// </summary>
  public sealed class EffectChainClient : IEffectChainClient
  {
    private readonly BackendClientHttpPipeline _pipeline;

    /// <summary>
    /// For DI: use BackendHttpContext.Pipeline. Tests use this ctor with mock pipeline.
    /// </summary>
    internal EffectChainClient(BackendClientHttpPipeline pipeline)
    {
      _pipeline = pipeline ?? throw new ArgumentNullException(nameof(pipeline));
    }

    /// <inheritdoc />
    public async Task<List<EffectChain>> GetEffectChainsAsync(string projectId, CancellationToken cancellationToken = default)
    {
      var result = await _pipeline.GetAsync<List<EffectChain>>($"/api/effects/chains?project_id={Uri.EscapeDataString(projectId)}", cancellationToken);
      return result ?? new List<EffectChain>();
    }

    /// <inheritdoc />
    public async Task<EffectChain> GetEffectChainAsync(string projectId, string chainId, CancellationToken cancellationToken = default)
    {
      var result = await _pipeline.GetAsync<EffectChain>($"/api/effects/chains/{Uri.EscapeDataString(projectId)}/{Uri.EscapeDataString(chainId)}", cancellationToken);
      return result ?? throw new BackendDeserializationException("Failed to deserialize effect chain");
    }

    /// <inheritdoc />
    public Task<EffectChain> CreateEffectChainAsync(string projectId, EffectChain chain, CancellationToken cancellationToken = default)
      => _pipeline.PostAsync<EffectChain, EffectChain>($"/api/effects/chains/{Uri.EscapeDataString(projectId)}", chain, cancellationToken);

    /// <inheritdoc />
    public Task<EffectChain> UpdateEffectChainAsync(string projectId, string chainId, EffectChain chain, CancellationToken cancellationToken = default)
      => _pipeline.PutAsync<EffectChain, EffectChain>($"/api/effects/chains/{Uri.EscapeDataString(projectId)}/{Uri.EscapeDataString(chainId)}", chain, cancellationToken);

    /// <inheritdoc />
    public async Task<bool> DeleteEffectChainAsync(string projectId, string chainId, CancellationToken cancellationToken = default)
    {
      await _pipeline.SendRequestAsync<object, object>(
          $"/api/effects/chains/{Uri.EscapeDataString(projectId)}/{Uri.EscapeDataString(chainId)}", null, HttpMethod.Delete, cancellationToken);
      return true;
    }

    /// <inheritdoc />
    public async Task<EffectProcessResponse> ProcessAudioWithChainAsync(
        string projectId,
        string chainId,
        string audioId,
        string? outputFilename = null,
        bool bypassChain = false,
        bool preview = false,
        CancellationToken cancellationToken = default)
    {
      var url = $"/api/effects/chains/{Uri.EscapeDataString(projectId)}/{Uri.EscapeDataString(chainId)}/process?audio_id={Uri.EscapeDataString(audioId)}";
      if (!string.IsNullOrWhiteSpace(outputFilename))
        url += $"&output_filename={Uri.EscapeDataString(outputFilename)}";
      if (bypassChain)
        url += "&bypass_chain=true";
      if (preview)
        url += "&preview=true";
      var result = await _pipeline.SendRequestAsync<object, EffectProcessResponse>(url, null, HttpMethod.Post, cancellationToken);
      return result ?? throw new BackendDeserializationException("Failed to deserialize process response");
    }

    /// <inheritdoc />
    public async Task<List<EffectPreset>> GetEffectPresetsAsync(string? effectType = null, CancellationToken cancellationToken = default)
    {
      var url = "/api/effects/presets";
      if (!string.IsNullOrWhiteSpace(effectType))
        url += $"?effect_type={Uri.EscapeDataString(effectType)}";
      var result = await _pipeline.GetAsync<List<EffectPreset>>(url, cancellationToken);
      return result ?? new List<EffectPreset>();
    }

    /// <inheritdoc />
    public Task<EffectPreset> CreateEffectPresetAsync(EffectPreset preset, CancellationToken cancellationToken = default)
      => _pipeline.PostAsync<EffectPreset, EffectPreset>("/api/effects/presets", preset, cancellationToken);

    /// <inheritdoc />
    public async Task<bool> DeleteEffectPresetAsync(string presetId, CancellationToken cancellationToken = default)
    {
      await _pipeline.SendRequestAsync<object, object>(
          $"/api/effects/presets/{Uri.EscapeDataString(presetId)}", null, HttpMethod.Delete, cancellationToken);
      return true;
    }
  }
}
