using System.Threading;
using System.Threading.Tasks;
using VoiceStudio.Core.Models;

namespace VoiceStudio.Core.Services
{
  /// <summary>
  /// Timeline transcription facade. Provides a focused seam for loading transcription by ID,
  /// delegating to the backend transport. Use this instead of IBackendClient for transcription
  /// loading in Timeline panel to reduce coupling and enable test isolation.
  /// </summary>
  public interface ITimelineTranscriptionService
  {
    Task<TranscriptionResponse> GetTranscriptionAsync(string transcriptionId, CancellationToken cancellationToken = default);
  }
}
