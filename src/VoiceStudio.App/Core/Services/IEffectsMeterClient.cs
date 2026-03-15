using System.Threading;
using System.Threading.Tasks;
using VoiceStudio.Core.Models;

namespace VoiceStudio.Core.Services
{
  /// <summary>
  /// Client for Effects Mixer audio meters API.
  /// Use instead of IBackendClient.GetAudioMetersAsync for EffectsMixer panel.
  /// </summary>
  public interface IEffectsMeterClient
  {
    Task<AudioMeters> GetAudioMetersAsync(string audioId, CancellationToken cancellationToken = default);
  }
}
