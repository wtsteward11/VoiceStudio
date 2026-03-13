using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using VoiceStudio.Core.Models;

namespace VoiceStudio.Core.Services
{
  /// <summary>
  /// Client for transcription API (/api/transcribe).
  /// Use instead of IBackendClient for languages, engines, transcribe, list, get, delete.
  /// </summary>
  public interface ITranscriptionClient
  {
    Task<List<SupportedLanguage>> GetSupportedLanguagesAsync(CancellationToken ct = default);
    Task<List<TranscriptionEngine>> GetTranscriptionEnginesAsync(CancellationToken ct = default);
    Task<TranscriptionResponse> TranscribeAudioAsync(TranscriptionRequest request, string? projectId = null, CancellationToken ct = default);
    Task<TranscriptionResponse> GetTranscriptionAsync(string transcriptionId, CancellationToken ct = default);
    Task<List<TranscriptionResponse>> ListTranscriptionsAsync(string? audioId = null, string? projectId = null, CancellationToken ct = default);
    Task<bool> DeleteTranscriptionAsync(string transcriptionId, CancellationToken ct = default);
  }
}
