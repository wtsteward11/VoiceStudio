using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using VoiceStudio.Core.Models;
using VoiceStudio.Core.Services;

namespace VoiceStudio.App.Services
{
  /// <summary>
  /// Client for /api/voice-morph.
  /// Thin pass-through to IBackendClient.
  /// </summary>
  public sealed class VoiceMorphClient : IVoiceMorphClient
  {
    private readonly IBackendClient _backend;

    public VoiceMorphClient(IBackendClient backend)
    {
      _backend = backend ?? throw new System.ArgumentNullException(nameof(backend));
    }

    /// <inheritdoc />
    public Task<VoiceMorphConfig[]?> GetConfigsAsync(CancellationToken cancellationToken = default)
    {
      return _backend.SendRequestAsync<object, VoiceMorphConfig[]>(
        "/api/voice-morph/configs",
        null,
        HttpMethod.Get,
        cancellationToken);
    }

    /// <inheritdoc />
    public Task<VoiceMorphConfig?> CreateConfigAsync(
      string name,
      string sourceAudioId,
      IReadOnlyList<(string VoiceProfileId, double Weight)> targetVoices,
      double morphStrength,
      bool preserveEmotion,
      bool preserveProsody,
      CancellationToken cancellationToken = default)
    {
      var request = new
      {
        name,
        source_audio_id = sourceAudioId,
        target_voices = targetVoices.Select(v => new { voice_profile_id = v.VoiceProfileId, weight = v.Weight }).ToArray(),
        morph_strength = morphStrength,
        preserve_emotion = preserveEmotion,
        preserve_prosody = preserveProsody,
        output_format = "wav"
      };
      return _backend.SendRequestAsync<object, VoiceMorphConfig>(
        "/api/voice-morph/configs",
        request,
        HttpMethod.Post,
        cancellationToken);
    }

    /// <inheritdoc />
    public Task<VoiceMorphConfig?> UpdateConfigAsync(
      string configId,
      string name,
      string sourceAudioId,
      IReadOnlyList<(string VoiceProfileId, double Weight)> targetVoices,
      double morphStrength,
      bool preserveEmotion,
      bool preserveProsody,
      CancellationToken cancellationToken = default)
    {
      var request = new
      {
        name,
        source_audio_id = sourceAudioId,
        target_voices = targetVoices.Select(v => new { voice_profile_id = v.VoiceProfileId, weight = v.Weight }).ToArray(),
        morph_strength = morphStrength,
        preserve_emotion = preserveEmotion,
        preserve_prosody = preserveProsody,
        output_format = "wav"
      };
      return _backend.SendRequestAsync<object, VoiceMorphConfig>(
        $"/api/voice-morph/configs/{System.Uri.EscapeDataString(configId)}",
        request,
        HttpMethod.Put,
        cancellationToken);
    }

    /// <inheritdoc />
    public Task DeleteConfigAsync(string configId, CancellationToken cancellationToken = default)
    {
      return _backend.SendRequestAsync<object, object>(
        $"/api/voice-morph/configs/{System.Uri.EscapeDataString(configId)}",
        null,
        HttpMethod.Delete,
        cancellationToken);
    }

    /// <inheritdoc />
    public Task<VoiceMorphApplyResponse?> ApplyMorphAsync(string configId, CancellationToken cancellationToken = default)
    {
      var request = new { config_id = configId };
      return _backend.SendRequestAsync<object, VoiceMorphApplyResponse>(
        "/api/voice-morph/apply",
        request,
        HttpMethod.Post,
        cancellationToken);
    }
  }
}
