using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using VoiceStudio.Core.Exceptions;
using VoiceStudio.Core.Models;
using VoiceStudio.Core.Services;

namespace VoiceStudio.App.Services
{
  /// <summary>
  /// Delegates to <see cref="IBackendClient.SynthesizeSpeechToSpeechAsync"/> (canonical transport).
  /// </summary>
  public sealed class SpeechToSpeechService : ISpeechToSpeechService
  {
    private readonly IBackendClient _backend;
    private readonly ILogger<SpeechToSpeechService>? _logger;

    public SpeechToSpeechService(IBackendClient backend, ILogger<SpeechToSpeechService>? logger = null)
    {
      _backend = backend ?? throw new ArgumentNullException(nameof(backend));
      _logger = logger;
    }

    /// <inheritdoc />
    public async Task<SpeechToSpeechResponse> ConvertSpeechAsync(
        SpeechToSpeechRequest request,
        CancellationToken cancellationToken = default)
    {
      if (request == null)
        throw new ArgumentNullException(nameof(request));
      if (string.IsNullOrWhiteSpace(request.SourceAudioId))
        throw new ArgumentException("Source audio id is required.", nameof(request));
      if (string.IsNullOrWhiteSpace(request.TargetVoiceProfileId))
        throw new ArgumentException("Target voice profile id is required.", nameof(request));

      try
      {
        return await _backend.SynthesizeSpeechToSpeechAsync(request, cancellationToken).ConfigureAwait(false);
      }
      catch (Exception ex)
      {
        _logger?.LogWarning(ex, "Speech-to-speech conversion request failed.");
        throw MapToUserActionableException(ex);
      }
    }

    /// <inheritdoc />
    public Task<StsMarkingStatus?> GetMarkingAsync(
        string audioId,
        CancellationToken cancellationToken = default)
    {
      return _backend.GetStsMarkingAsync(audioId, cancellationToken);
    }

    private static Exception MapToUserActionableException(Exception ex)
    {
      if (ex is OperationCanceledException)
        return ex;
      if (ex is BackendNotFoundException)
        return ex;
      if (ex is HttpRequestException httpEx)
        return new BackendUnavailableException(
            "Cannot reach the VoiceStudio backend. Ensure the backend is running and reachable.",
            httpEx);
      return ex;
    }
  }
}
