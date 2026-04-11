using System.Threading;
using System.Threading.Tasks;
using VoiceStudio.Core.Models;

namespace VoiceStudio.App.Services
{
  /// <summary>
  /// Panel seam for batch speech-to-speech conversion (GAP-051).
  /// </summary>
  public interface ISpeechToSpeechService
  {
    /// <summary>
    /// Convert source speech audio to the target voice profile via backend RVC path.
    /// </summary>
    Task<SpeechToSpeechResponse> ConvertSpeechAsync(
        SpeechToSpeechRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Durable marking status for a registered STS output artifact (GET /api/audio/{audio_id}/marking).
    /// </summary>
    Task<StsMarkingStatus?> GetMarkingAsync(string audioId, CancellationToken cancellationToken = default);
  }
}
