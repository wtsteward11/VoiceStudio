using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using VoiceStudio.Core.Models;
using VoiceStudio.Core.Services;

namespace VoiceStudio.App.Services
{
  /// <summary>
  /// Timeline transcription service. Delegates to IBackendClient for GetTranscriptionAsync.
  /// Applies policy: never return null; normalize null/empty Segments to empty list.
  /// </summary>
  public sealed class TimelineTranscriptionService : ITimelineTranscriptionService
  {
    private readonly IBackendClient _backend;

    public TimelineTranscriptionService(IBackendClient backend)
    {
      _backend = backend ?? throw new System.ArgumentNullException(nameof(backend));
    }

    /// <inheritdoc />
    public async Task<TranscriptionResponse> GetTranscriptionAsync(string transcriptionId, CancellationToken cancellationToken = default)
    {
      var raw = await _backend.GetTranscriptionAsync(transcriptionId, cancellationToken).ConfigureAwait(false);
      if (raw == null)
      {
        return new TranscriptionResponse { Segments = new List<TranscriptionSegment>() };
      }
      if (raw.Segments == null)
      {
        raw.Segments = new List<TranscriptionSegment>();
      }
      return raw;
    }
  }
}
