using System.Threading;
using System.Threading.Tasks;
using VoiceStudio.Core.Models;

namespace VoiceStudio.Core.Services
{
  /// <summary>
  /// Client for Text Highlighting API (/api/text-highlighting).
  /// Use instead of IBackendClient for TextHighlighting panel.
  /// </summary>
  public interface ITextHighlightingClient
  {
    Task<TextHighlightingSession?> CreateSessionAsync(TextHighlightingCreateRequest request, CancellationToken cancellationToken = default);

    Task<TextHighlightingSyncResponse?> SyncHighlightingAsync(TextHighlightingSyncRequest request, CancellationToken cancellationToken = default);

    Task<TextHighlightingSession?> UpdateSessionAsync(string sessionId, TextHighlightingUpdateRequest request, CancellationToken cancellationToken = default);

    Task DeleteSessionAsync(string sessionId, CancellationToken cancellationToken = default);

    Task PersistSessionAsync(string sessionId, TextHighlightingPersistRequest request, CancellationToken cancellationToken = default);

    Task<TextHighlightingSession[]?> GetSessionsAsync(CancellationToken cancellationToken = default);
  }
}
