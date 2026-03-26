using System;
using System.Threading;
using System.Threading.Tasks;
using VoiceStudio.Core.Models;
using VoiceStudio.Core.Services;

namespace VoiceStudio.App.Services
{
  /// <summary>
  /// Client for Text Highlighting API. Thin pass-through to IBackendClient.
  /// </summary>
  public sealed class TextHighlightingClient : ITextHighlightingClient
  {
    private readonly IBackendClient _backend;

    public TextHighlightingClient(IBackendClient backend)
    {
      _backend = backend ?? throw new ArgumentNullException(nameof(backend));
    }

    /// <inheritdoc />
    public Task<TextHighlightingSession?> CreateSessionAsync(TextHighlightingCreateRequest request, CancellationToken cancellationToken = default)
    {
      var body = new
      {
        audio_id = request?.AudioId ?? string.Empty,
        text = request?.Text ?? string.Empty,
        highlight_type = request?.HighlightType ?? "word",
        segments = (object?)request?.Segments
      };
      return _backend.SendRequestAsync<object, TextHighlightingSession>(
        "/api/text-highlighting",
        body,
        System.Net.Http.HttpMethod.Post,
        cancellationToken);
    }

    /// <inheritdoc />
    public Task<TextHighlightingSyncResponse?> SyncHighlightingAsync(TextHighlightingSyncRequest request, CancellationToken cancellationToken = default)
    {
      var body = new
      {
        audio_id = request?.AudioId ?? string.Empty,
        current_time = request?.CurrentTime ?? 0
      };
      return _backend.SendRequestAsync<object, TextHighlightingSyncResponse>(
        "/api/text-highlighting/sync",
        body,
        System.Net.Http.HttpMethod.Post,
        cancellationToken);
    }

    /// <inheritdoc />
    public Task<TextHighlightingSession?> UpdateSessionAsync(string sessionId, TextHighlightingUpdateRequest request, CancellationToken cancellationToken = default)
    {
      var body = new
      {
        current_time = request?.CurrentTime ?? 0,
        segments = request?.Segments
      };
      var url = $"/api/text-highlighting/{Uri.EscapeDataString(sessionId ?? "")}";
      return _backend.SendRequestAsync<object, TextHighlightingSession>(
        url,
        body,
        System.Net.Http.HttpMethod.Put,
        cancellationToken);
    }

    /// <inheritdoc />
    public Task DeleteSessionAsync(string sessionId, CancellationToken cancellationToken = default)
    {
      var url = $"/api/text-highlighting/{Uri.EscapeDataString(sessionId ?? "")}";
      return _backend.SendRequestAsync<object, object>(
        url,
        null,
        System.Net.Http.HttpMethod.Delete,
        cancellationToken);
    }

    /// <inheritdoc />
    public Task PersistSessionAsync(string sessionId, TextHighlightingPersistRequest request, CancellationToken cancellationToken = default)
    {
      var body = new
      {
        session_id = request?.SessionId ?? string.Empty,
        audio_id = request?.AudioId,
        text = request?.Text ?? string.Empty,
        segments = request?.Segments,
        created = request?.Created ?? string.Empty
      };
      var url = $"/api/text-highlighting/{Uri.EscapeDataString(sessionId ?? "")}/persist";
      return _backend.SendRequestAsync<object, object>(
        url,
        body,
        System.Net.Http.HttpMethod.Post,
        cancellationToken);
    }

    /// <inheritdoc />
    public Task<TextHighlightingSession[]?> GetSessionsAsync(CancellationToken cancellationToken = default)
    {
      return _backend.SendRequestAsync<object, TextHighlightingSession[]>(
        "/api/text-highlighting/sessions",
        null,
        System.Net.Http.HttpMethod.Get,
        cancellationToken);
    }
  }
}
