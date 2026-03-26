using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using VoiceStudio.Core.Models;

namespace VoiceStudio.Core.Services
{
  /// <summary>
  /// Client for /api/voice-morph.
  /// Thin pass-through to IBackendClient.
  /// </summary>
  public interface IVoiceMorphClient
  {
    /// <summary>
    /// Gets all morph configurations.
    /// </summary>
    Task<VoiceMorphConfig[]?> GetConfigsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a new morph configuration.
    /// </summary>
    Task<VoiceMorphConfig?> CreateConfigAsync(
      string name,
      string sourceAudioId,
      IReadOnlyList<(string VoiceProfileId, double Weight)> targetVoices,
      double morphStrength,
      bool preserveEmotion,
      bool preserveProsody,
      CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates an existing morph configuration.
    /// </summary>
    Task<VoiceMorphConfig?> UpdateConfigAsync(
      string configId,
      string name,
      string sourceAudioId,
      IReadOnlyList<(string VoiceProfileId, double Weight)> targetVoices,
      double morphStrength,
      bool preserveEmotion,
      bool preserveProsody,
      CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes a morph configuration.
    /// </summary>
    Task DeleteConfigAsync(string configId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Applies a morph configuration.
    /// </summary>
    Task<VoiceMorphApplyResponse?> ApplyMorphAsync(string configId, CancellationToken cancellationToken = default);
  }
}
