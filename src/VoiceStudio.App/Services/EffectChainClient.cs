using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using VoiceStudio.Core.Models;
using VoiceStudio.Core.Services;

namespace VoiceStudio.App.Services
{
  /// <summary>
  /// Client for Effects Mixer effect chain API. Thin pass-through to IBackendClient.
  /// </summary>
  public sealed class EffectChainClient : IEffectChainClient
  {
    private readonly IBackendClient _backend;

    public EffectChainClient(IBackendClient backend)
    {
      _backend = backend ?? throw new ArgumentNullException(nameof(backend));
    }

    /// <inheritdoc />
    public Task<List<EffectChain>> GetEffectChainsAsync(string projectId, CancellationToken cancellationToken = default)
      => _backend.GetEffectChainsAsync(projectId, cancellationToken);

    /// <inheritdoc />
    public Task<EffectChain> CreateEffectChainAsync(string projectId, EffectChain chain, CancellationToken cancellationToken = default)
      => _backend.CreateEffectChainAsync(projectId, chain, cancellationToken);

    /// <inheritdoc />
    public Task<EffectChain> UpdateEffectChainAsync(string projectId, string chainId, EffectChain chain, CancellationToken cancellationToken = default)
      => _backend.UpdateEffectChainAsync(projectId, chainId, chain, cancellationToken);

    /// <inheritdoc />
    public Task<bool> DeleteEffectChainAsync(string projectId, string chainId, CancellationToken cancellationToken = default)
      => _backend.DeleteEffectChainAsync(projectId, chainId, cancellationToken);

    /// <inheritdoc />
    public Task<EffectProcessResponse> ProcessAudioWithChainAsync(string projectId, string chainId, string audioId, string? outputFilename = null, CancellationToken cancellationToken = default)
      => _backend.ProcessAudioWithChainAsync(projectId, chainId, audioId, outputFilename, cancellationToken);

    /// <inheritdoc />
    public Task<List<EffectPreset>> GetEffectPresetsAsync(string? effectType = null, CancellationToken cancellationToken = default)
      => _backend.GetEffectPresetsAsync(effectType, cancellationToken);
  }
}
