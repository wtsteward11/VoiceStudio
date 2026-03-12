using System.IO;
using System.Threading;
using System.Threading.Tasks;
using VoiceStudio.Core.Models;

namespace VoiceStudio.App.Services
{
  /// <summary>
  /// Service for A/B testing voice synthesis quality comparison.
  /// Owns RunABTestAsync and GetAudioStreamAsync for playback.
  /// </summary>
  public interface IABTestService
  {
    /// <summary>
    /// Run an A/B test comparing two synthesis configurations.
    /// </summary>
    Task<ABTestResponse> RunABTestAsync(ABTestRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get audio stream by ID for playback.
    /// </summary>
    Task<Stream> GetAudioStreamAsync(string audioId, CancellationToken cancellationToken = default);
  }
}
