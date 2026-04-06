using System.IO;
using System.Threading;
using System.Threading.Tasks;
using VoiceStudio.Core.Exceptions;
using VoiceStudio.Core.Models;
using VoiceStudio.Core.Services;

namespace VoiceStudio.App.Services
{
  /// <summary>
  /// Voice synthesis service. Owns policy: response normalization (AudioUrl never null),
  /// request shaping (Engine default "xtts", Text validation), error mapping (404/HttpRequestException).
  /// Policy: No retry on 5xx; single attempt. Callers responsible for retry if needed.
  /// See docs/design/SEAM_MATURITY_AUDIT.md.
  /// </summary>
  public sealed class VoiceSynthesisService : IVoiceSynthesisService
  {
    private readonly IBackendClient _backend;

    public VoiceSynthesisService(IBackendClient backend)
    {
      _backend = backend ?? throw new System.ArgumentNullException(nameof(backend));
    }

    /// <inheritdoc />
    public async Task<VoiceSynthesisResponse> SynthesizeVoiceAsync(VoiceSynthesisRequest request, CancellationToken cancellationToken = default)
    {
      if (request == null)
        throw new System.ArgumentNullException(nameof(request));

      // Request shaping: apply defaults for missing Engine; returns new instance (no caller mutation)
      var shaped = ShapeRequest(request);

      try
      {
        var response = await _backend.SynthesizeVoiceAsync(shaped, cancellationToken);
        // Policy: normalize null/empty AudioUrl to string.Empty for downstream consumers
        if (response != null && string.IsNullOrEmpty(response.AudioUrl) && !string.IsNullOrEmpty(response.AudioId))
        {
          response.AudioUrl = $"/api/audio/{response.AudioId}";
        }
        return response ?? new VoiceSynthesisResponse();
      }
      catch (System.Exception ex)
      {
        throw MapToUserActionableException(ex);
      }
    }

    /// <summary>
    /// Shapes the request with defaults. Returns a new instance; does not mutate the caller's request.
    /// </summary>
    private static VoiceSynthesisRequest ShapeRequest(VoiceSynthesisRequest request)
    {
      if (string.IsNullOrWhiteSpace(request.Text))
        throw new System.ArgumentException("Synthesis text cannot be empty.", nameof(request));

      var engine = string.IsNullOrWhiteSpace(request.Engine) ? "xtts" : request.Engine;

      return new VoiceSynthesisRequest
      {
        Engine = engine,
        ProfileId = request.ProfileId,
        Text = request.Text,
        Language = request.Language,
        Emotion = request.Emotion,
        EnhanceQuality = request.EnhanceQuality,
        Speed = request.Speed,
        Pitch = request.Pitch,
        Stability = request.Stability,
        Clarity = request.Clarity,
        Temperature = request.Temperature
      };
    }

    private static System.Exception MapToUserActionableException(System.Exception ex)
    {
      if (ex is System.OperationCanceledException)
        return ex;
      // GAP-064: Preserve typed backend exceptions for ActionableErrorTranslator.
      if (ex is BackendNotFoundException)
        return ex;
      if (ex is System.Net.Http.HttpRequestException httpEx)
        return new BackendUnavailableException(
            "Cannot reach the VoiceStudio backend. Ensure the backend is running and reachable.",
            httpEx);
      return ex;
    }

    /// <inheritdoc />
    public Task<Stream> GetAudioStreamAsync(string audioId, CancellationToken cancellationToken = default)
      => _backend.GetAudioStreamAsync(audioId, cancellationToken);
  }
}
