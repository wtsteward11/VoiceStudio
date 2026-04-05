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
    Task<EffectChain> GetEffectChainAsync(string projectId, string chainId, CancellationToken cancellationToken = default);
    Task<EffectChain> CreateEffectChainAsync(string projectId, EffectChain chain, CancellationToken cancellationToken = default);
    Task<EffectChain> UpdateEffectChainAsync(string projectId, string chainId, EffectChain chain, CancellationToken cancellationToken = default);
    Task<bool> DeleteEffectChainAsync(string projectId, string chainId, CancellationToken cancellationToken = default);
    /// <param name="bypassChain">GAP-039: When true, POST includes bypass_chain=true (dry signal; input audio id returned).</param>
    /// <param name="preview">GAP-039: When true, POST includes preview=true (same processing; message tagged on server).</param>
    Task<EffectProcessResponse> ProcessAudioWithChainAsync(
        string projectId,
        string chainId,
        string audioId,
        string? outputFilename = null,
        bool bypassChain = false,
        bool preview = false,
        CancellationToken cancellationToken = default);
    Task<List<EffectPreset>> GetEffectPresetsAsync(string? effectType = null, CancellationToken cancellationToken = default);
    Task<EffectPreset> CreateEffectPresetAsync(EffectPreset preset, CancellationToken cancellationToken = default);
    Task<bool> DeleteEffectPresetAsync(string presetId, CancellationToken cancellationToken = default);
  }
}
