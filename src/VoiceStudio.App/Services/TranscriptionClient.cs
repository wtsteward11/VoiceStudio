using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using VoiceStudio.Core.Models;
using VoiceStudio.Core.Services;

namespace VoiceStudio.App.Services
{
  /// <summary>
  /// Client for /api/transcribe. Thin pass-through to IBackendClient.
  /// </summary>
  public sealed class TranscriptionClient : ITranscriptionClient
  {
    private readonly IBackendClient _backend;

    public TranscriptionClient(IBackendClient backend)
    {
      _backend = backend ?? throw new ArgumentNullException(nameof(backend));
    }

    /// <inheritdoc />
    public Task<List<SupportedLanguage>> GetSupportedLanguagesAsync(CancellationToken ct = default)
      => _backend.GetSupportedLanguagesAsync(ct);

    /// <inheritdoc />
    public Task<List<TranscriptionEngine>> GetTranscriptionEnginesAsync(CancellationToken ct = default)
      => _backend.GetTranscriptionEnginesAsync(ct);

    /// <inheritdoc />
    public Task<TranscriptionResponse> TranscribeAudioAsync(TranscriptionRequest request, string? projectId = null, CancellationToken ct = default)
      => _backend.TranscribeAudioAsync(request, projectId, ct);

    /// <inheritdoc />
    public Task<TranscriptionResponse> GetTranscriptionAsync(string transcriptionId, CancellationToken ct = default)
      => _backend.GetTranscriptionAsync(transcriptionId, ct);

    /// <inheritdoc />
    public Task<List<TranscriptionResponse>> ListTranscriptionsAsync(string? audioId = null, string? projectId = null, CancellationToken ct = default)
      => _backend.ListTranscriptionsAsync(audioId, projectId, ct);

    /// <inheritdoc />
    public Task<bool> DeleteTranscriptionAsync(string transcriptionId, CancellationToken ct = default)
      => _backend.DeleteTranscriptionAsync(transcriptionId, ct);
  }
}
