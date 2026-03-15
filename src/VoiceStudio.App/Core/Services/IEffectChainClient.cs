using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using VoiceStudio.Core.Models;

namespace VoiceStudio.Core.Services
{
  /// <summary>
  /// Client for Effects Mixer effect chain API.
  /// Use instead of IBackendClient for effect chain operations in EffectsMixer panel.
  /// </summary>
  public interface IEffectChainClient
  {
    Task<List<EffectChain>> GetEffectChainsAsync(string projectId, CancellationToken cancellationToken = default);
    Task<EffectChain> CreateEffectChainAsync(string projectId, EffectChain chain, CancellationToken cancellationToken = default);
    Task<EffectChain> UpdateEffectChainAsync(string projectId, string chainId, EffectChain chain, CancellationToken cancellationToken = default);
    Task<bool> DeleteEffectChainAsync(string projectId, string chainId, CancellationToken cancellationToken = default);
    Task<EffectProcessResponse> ProcessAudioWithChainAsync(string projectId, string chainId, string audioId, string? outputFilename = null, CancellationToken cancellationToken = default);
    Task<List<EffectPreset>> GetEffectPresetsAsync(string? effectType = null, CancellationToken cancellationToken = default);
  }
}
