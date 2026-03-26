using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using VoiceStudio.App.Services;

namespace VoiceStudio.Core.Services
{
  /// <summary>
  /// Client for text-based speech editor API (/api/transcribe, /api/edit).
  /// Use instead of IBackendClient for transcribe, edit session, align, replace, insert, remove filler, apply.
  /// </summary>
  public interface ITextBasedSpeechEditorClient
  {
    Task<TextEditTranscriptionResponse?> TranscribeAsync(TextEditTranscriptionRequest request, CancellationToken cancellationToken = default);
    Task<EditSessionCreateResponse?> CreateEditSessionAsync(string audioId, string transcript, CancellationToken cancellationToken = default);
    Task<AlignResponse?> AlignAsync(AlignRequest request, CancellationToken cancellationToken = default);
    Task<ReplaceWordResponse?> ReplaceWordAsync(ReplaceWordRequest request, CancellationToken cancellationToken = default);
    Task<InsertTextResponse?> InsertTextAsync(InsertTextRequest request, CancellationToken cancellationToken = default);
    Task<RemoveFillerWordsResponse?> RemoveFillerWordsAsync(RemoveFillerWordsRequest request, CancellationToken cancellationToken = default);
    Task<ApplyEditsResponse?> ApplyEditsAsync(ApplyEditsRequest request, CancellationToken cancellationToken = default);
    Task<List<string>> GetEnginesAsync(CancellationToken cancellationToken = default);
  }
}
