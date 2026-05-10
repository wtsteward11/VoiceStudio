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
    public Task<TranscriptionResponse> UpdateTranscriptionTextAsync(
        string transcriptionId,
        string text,
        List<TranscriptionSegment> segments,
        CancellationToken ct = default)
    {
      if (string.IsNullOrWhiteSpace(transcriptionId))
        throw new ArgumentException("Transcription id is required.", nameof(transcriptionId));
      if (string.IsNullOrWhiteSpace(text))
        throw new ArgumentException("Transcription text is required.", nameof(text));
      if (segments == null)
        throw new ArgumentNullException(nameof(segments));
      if (segments.Count == 0)
        throw new ArgumentException("At least one segment is required.", nameof(segments));

      return _backend.PutAsync<object, TranscriptionResponse>(
          $"/api/transcribe/{Uri.EscapeDataString(transcriptionId)}",
          new
          {
            text,
            segments,
          },
          ct);
    }

    /// <inheritdoc />
    public Task<bool> DeleteTranscriptionAsync(string transcriptionId, CancellationToken ct = default)
      => _backend.DeleteTranscriptionAsync(transcriptionId, ct);

    /// <inheritdoc />
    public async Task<TranscriptionJobResponse> StartTranscriptionJobAsync(
        TranscriptionJobRequest request,
        string? projectId = null,
        CancellationToken ct = default)
    {
      var url = "/api/transcribe/jobs";
      if (!string.IsNullOrEmpty(projectId))
      {
        url += $"?project_id={Uri.EscapeDataString(projectId)}";
      }

      return await _backend.PostAsync<TranscriptionJobRequest, TranscriptionJobResponse>(url, request, ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<TranscriptionJobResponse> GetTranscriptionJobStatusAsync(string jobId, CancellationToken ct = default)
    {
      if (string.IsNullOrWhiteSpace(jobId))
        throw new ArgumentException("Job id is required.", nameof(jobId));
      var url = $"/api/transcribe/jobs/{Uri.EscapeDataString(jobId)}";
      var result = await _backend.GetAsync<TranscriptionJobResponse>(url, ct).ConfigureAwait(false);
      return result ?? throw new InvalidOperationException("Transcription job status response was null.");
    }
  }
}
