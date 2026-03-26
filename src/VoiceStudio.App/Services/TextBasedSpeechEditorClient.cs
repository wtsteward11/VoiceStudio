using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using VoiceStudio.Core.Services;
using VoiceStudio.App.Services;

namespace VoiceStudio.App.Services
{
  /// <summary>
  /// Client for /api/transcribe and /api/edit. Thin pass-through to IBackendClient.
  /// </summary>
  public sealed class TextBasedSpeechEditorClient : ITextBasedSpeechEditorClient
  {
    private readonly IBackendClient _backend;

    public TextBasedSpeechEditorClient(IBackendClient backend)
    {
      _backend = backend ?? throw new System.ArgumentNullException(nameof(backend));
    }

    /// <inheritdoc />
    public Task<TextEditTranscriptionResponse?> TranscribeAsync(TextEditTranscriptionRequest request, CancellationToken cancellationToken = default)
      => _backend.SendRequestAsync<TextEditTranscriptionRequest, TextEditTranscriptionResponse>(
          "/api/transcribe/",
          request,
          HttpMethod.Post,
          cancellationToken);

    /// <inheritdoc />
    public Task<EditSessionCreateResponse?> CreateEditSessionAsync(string audioId, string transcript, CancellationToken cancellationToken = default)
      => _backend.SendRequestAsync<object, EditSessionCreateResponse>(
          "/api/edit/session/create",
          new { audio_id = audioId, transcript },
          HttpMethod.Post,
          cancellationToken);

    /// <inheritdoc />
    public Task<AlignResponse?> AlignAsync(AlignRequest request, CancellationToken cancellationToken = default)
      => _backend.SendRequestAsync<AlignRequest, AlignResponse>(
          "/api/edit/align",
          request,
          HttpMethod.Post,
          cancellationToken);

    /// <inheritdoc />
    public Task<ReplaceWordResponse?> ReplaceWordAsync(ReplaceWordRequest request, CancellationToken cancellationToken = default)
      => _backend.SendRequestAsync<ReplaceWordRequest, ReplaceWordResponse>(
          "/api/edit/replace-word",
          request,
          HttpMethod.Post,
          cancellationToken);

    /// <inheritdoc />
    public Task<InsertTextResponse?> InsertTextAsync(InsertTextRequest request, CancellationToken cancellationToken = default)
      => _backend.SendRequestAsync<InsertTextRequest, InsertTextResponse>(
          "/api/edit/insert-text",
          request,
          HttpMethod.Post,
          cancellationToken);

    /// <inheritdoc />
    public Task<RemoveFillerWordsResponse?> RemoveFillerWordsAsync(RemoveFillerWordsRequest request, CancellationToken cancellationToken = default)
      => _backend.SendRequestAsync<RemoveFillerWordsRequest, RemoveFillerWordsResponse>(
          "/api/edit/remove-filler-words",
          request,
          HttpMethod.Post,
          cancellationToken);

    /// <inheritdoc />
    public Task<ApplyEditsResponse?> ApplyEditsAsync(ApplyEditsRequest request, CancellationToken cancellationToken = default)
      => _backend.SendRequestAsync<ApplyEditsRequest, ApplyEditsResponse>(
          "/api/edit/apply",
          request,
          HttpMethod.Post,
          cancellationToken);

    /// <inheritdoc />
    public Task<System.Collections.Generic.List<string>> GetEnginesAsync(CancellationToken cancellationToken = default)
      => _backend.GetEnginesAsync(cancellationToken);
  }
}
