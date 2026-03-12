using System;
using System.Threading;
using System.Threading.Tasks;
using VoiceStudio.Core.Models;
using VoiceStudio.Core.Services;

namespace VoiceStudio.App.Services
{
  /// <summary>
  /// Implements reference-audio enhancement: request building and backend call to preprocess-reference.
  /// </summary>
  public sealed class ProfileEnhancementService : IProfileEnhancementService
  {
    private readonly IBackendClient _backendClient;

    public ProfileEnhancementService(IBackendClient backendClient)
    {
      _backendClient = backendClient ?? throw new ArgumentNullException(nameof(backendClient));
    }

    /// <inheritdoc />
    public async Task<ReferenceAudioPreprocessResponse?> EnhanceAsync(
      string profileId,
      bool autoEnhance,
      bool selectOptimalSegments,
      double minSegmentDuration,
      int maxSegments,
      CancellationToken cancellationToken = default)
    {
      var request = new ReferenceAudioPreprocessRequest
      {
        ProfileId = profileId,
        AutoEnhance = autoEnhance,
        SelectOptimalSegments = selectOptimalSegments,
        MinSegmentDuration = minSegmentDuration,
        MaxSegments = maxSegments
      };

      return await _backendClient.SendRequestAsync<ReferenceAudioPreprocessRequest, ReferenceAudioPreprocessResponse>(
        $"/api/profiles/{profileId}/preprocess-reference",
        request,
        cancellationToken).ConfigureAwait(false);
    }
  }
}
