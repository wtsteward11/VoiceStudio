using System.IO;
using System.Threading;
using System.Threading.Tasks;
using VoiceStudio.Core.Models;

namespace VoiceStudio.App.Services
{
  /// <summary>
  /// Service for voice synthesis and playback in the Voice Synthesis panel.
  /// Owns SynthesizeVoiceAsync and GetAudioStreamAsync.
  /// </summary>
  public interface IVoiceSynthesisService
  {
    /// <summary>
    /// Synthesize text to audio.
    /// </summary>
    Task<VoiceSynthesisResponse> SynthesizeVoiceAsync(VoiceSynthesisRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get audio stream by ID for playback.
    /// </summary>
    Task<Stream> GetAudioStreamAsync(string audioId, CancellationToken cancellationToken = default);
  }
}
