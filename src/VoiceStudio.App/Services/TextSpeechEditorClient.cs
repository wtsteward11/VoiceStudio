using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using VoiceStudio.Core.Services;
using VoiceStudio.App.Services;

namespace VoiceStudio.App.Services
{
  /// <summary>
  /// Client for text speech editor API. Thin pass-through to IBackendClient.
  /// </summary>
  public sealed class TextSpeechEditorClient : ITextSpeechEditorClient
  {
    private readonly IBackendClient _backend;

    public TextSpeechEditorClient(IBackendClient backend)
    {
      _backend = backend ?? throw new System.ArgumentNullException(nameof(backend));
    }

    /// <inheritdoc />
    public Task<EditorSession[]?> GetSessionsAsync(CancellationToken cancellationToken = default)
      => _backend.SendRequestAsync<object, EditorSession[]>(
          "/api/edit/sessions",
          null,
          HttpMethod.Get,
          cancellationToken);

    /// <inheritdoc />
    public Task<EditorSession?> CreateSessionAsync(object request, CancellationToken cancellationToken = default)
      => _backend.SendRequestAsync<object, EditorSession>(
          "/api/edit/sessions",
          request,
          cancellationToken);

    /// <inheritdoc />
    public Task<EditorSession?> UpdateSessionAsync(string sessionId, object request, CancellationToken cancellationToken = default)
      => _backend.SendRequestAsync<object, EditorSession>(
          $"/api/edit/sessions/{System.Uri.EscapeDataString(sessionId)}",
          request,
          HttpMethod.Put,
          cancellationToken);

    /// <inheritdoc />
    public async Task DeleteSessionAsync(string sessionId, CancellationToken cancellationToken = default)
    {
      _ = await _backend.SendRequestAsync<object, object>(
          $"/api/edit/sessions/{System.Uri.EscapeDataString(sessionId)}",
          null,
          HttpMethod.Delete,
          cancellationToken);
    }

    /// <inheritdoc />
    public Task<TextSpeechSynthesisResponse?> SynthesizeSessionAsync(string sessionId, object request, CancellationToken cancellationToken = default)
      => _backend.SendRequestAsync<object, TextSpeechSynthesisResponse>(
          $"/api/edit/sessions/{System.Uri.EscapeDataString(sessionId)}/synthesize",
          request,
          cancellationToken);

    /// <inheritdoc />
    public Task<SSMLPreviewResponse?> PreviewSynthesisAsync(object request, CancellationToken cancellationToken = default)
      => _backend.SendRequestAsync<object, SSMLPreviewResponse>(
          "/api/ssml/preview",
          request,
          HttpMethod.Post,
          cancellationToken);

    /// <inheritdoc />
    public Task<List<string>> GetEnginesAsync(CancellationToken cancellationToken = default)
      => _backend.GetEnginesAsync(cancellationToken);
  }
}
