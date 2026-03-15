using System;
using System.Threading;
using System.Threading.Tasks;
using VoiceStudio.Core.Models;
using VoiceStudio.Core.Services;

namespace VoiceStudio.App.Services
{
  /// <summary>
  /// Client for Effects Mixer audio meters API. Thin pass-through to IBackendClient.
  /// </summary>
  public sealed class EffectsMeterClient : IEffectsMeterClient
  {
    private readonly IBackendClient _backend;

    public EffectsMeterClient(IBackendClient backend)
    {
      _backend = backend ?? throw new ArgumentNullException(nameof(backend));
    }

    /// <inheritdoc />
    public Task<AudioMeters> GetAudioMetersAsync(string audioId, CancellationToken cancellationToken = default)
      => _backend.GetAudioMetersAsync(audioId, cancellationToken);
  }
}
