using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using VoiceStudio.App.Core.Models;
using VoiceStudio.App.Core.Services;
using VoiceStudio.Core.Services;

namespace VoiceStudio.App.Services;

/// <inheritdoc />
public sealed class TranscriptRegenerationClient : ITranscriptRegenerationClient
{
  private readonly IBackendClient _backend;

  public TranscriptRegenerationClient(IBackendClient backend)
  {
    _backend = backend ?? throw new ArgumentNullException(nameof(backend));
  }

  /// <inheritdoc />
  public Task<RegenerateSegmentJobStartResponse?> StartRegenerateSegmentAsync(
      RegenerateSegmentStartRequest request,
      CancellationToken cancellationToken = default) =>
      _backend.SendRequestAsync<RegenerateSegmentStartRequest, RegenerateSegmentJobStartResponse>(
          "/api/transcribe/regenerate-segment",
          request,
          HttpMethod.Post,
          cancellationToken);
}
