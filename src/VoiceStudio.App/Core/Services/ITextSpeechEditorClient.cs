using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using VoiceStudio.App.Services;

namespace VoiceStudio.Core.Services
{
  /// <summary>
  /// Client for text speech editor API (/api/edit/sessions, /api/ssml/preview).
  /// Use instead of IBackendClient for session CRUD, synthesize, and preview.
  /// </summary>
  public interface ITextSpeechEditorClient
  {
    Task<EditorSession[]?> GetSessionsAsync(CancellationToken cancellationToken = default);
    Task<EditorSession?> CreateSessionAsync(object request, CancellationToken cancellationToken = default);
    Task<EditorSession?> UpdateSessionAsync(string sessionId, object request, CancellationToken cancellationToken = default);
    Task DeleteSessionAsync(string sessionId, CancellationToken cancellationToken = default);
    Task<TextSpeechSynthesisResponse?> SynthesizeSessionAsync(string sessionId, object request, CancellationToken cancellationToken = default);
    Task<SSMLPreviewResponse?> PreviewSynthesisAsync(object request, CancellationToken cancellationToken = default);
    Task<List<string>> GetEnginesAsync(CancellationToken cancellationToken = default);
  }
}
