using System.Threading;
using System.Threading.Tasks;
using VoiceStudio.Core.Models;

namespace VoiceStudio.App.Services
{
  /// <summary>
  /// Handles reference-audio enhancement: request building, backend call to preprocess-reference.
  /// Used by ProfilesViewModel to enhance reference audio without embedding backend orchestration.
  /// </summary>
  public interface IProfileEnhancementService
  {
    /// <summary>
    /// Enhances reference audio for a profile via the preprocess-reference endpoint.
    /// </summary>
    /// <param name="profileId">Profile ID.</param>
    /// <param name="autoEnhance">Whether to auto-enhance.</param>
    /// <param name="selectOptimalSegments">Whether to select optimal segments.</param>
    /// <param name="minSegmentDuration">Minimum segment duration in seconds.</param>
    /// <param name="maxSegments">Maximum number of segments.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Enhancement response, or null if the backend returns null.</returns>
    Task<ReferenceAudioPreprocessResponse?> EnhanceAsync(
      string profileId,
      bool autoEnhance,
      bool selectOptimalSegments,
      double minSegmentDuration,
      int maxSegments,
      CancellationToken cancellationToken = default);
  }
}
