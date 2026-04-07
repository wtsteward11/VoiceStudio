using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using VoiceStudio.Core.Exceptions;
using VoiceStudio.Core.Models;
using VoiceStudio.Core.Services;

namespace VoiceStudio.App.Services
{
  /// <summary>
  /// Voice synthesis service. Owns policy: response normalization (AudioUrl never null),
  /// request shaping (Engine default "xtts", Text validation), error mapping (404/HttpRequestException).
  /// GAP-050: canonical emotion presets chain base synthesis → <c>IEmotionControlClient.ApplyEmotionAsync</c>
  /// (no local preset→prosody math; engine <c>emotion</c> omitted for presets to avoid double-stack).
  /// Policy: No retry on 5xx; single attempt. Callers responsible for retry if needed.
  /// See docs/design/SEAM_MATURITY_AUDIT.md.
  /// </summary>
  public sealed class VoiceSynthesisService : IVoiceSynthesisService
  {
    private readonly IBackendClient _backend;
    private readonly IEmotionControlClient _emotionControlClient;
    private readonly ILogger<VoiceSynthesisService>? _logger;

    public VoiceSynthesisService(
        IBackendClient backend,
        IEmotionControlClient emotionControlClient,
        ILogger<VoiceSynthesisService>? logger = null)
    {
      _backend = backend ?? throw new System.ArgumentNullException(nameof(backend));
      _emotionControlClient = emotionControlClient ?? throw new System.ArgumentNullException(nameof(emotionControlClient));
      _logger = logger;
    }

    /// <inheritdoc />
    public async Task<VoiceSynthesisResponse> SynthesizeVoiceAsync(VoiceSynthesisRequest request, CancellationToken cancellationToken = default)
    {
      if (request == null)
        throw new System.ArgumentNullException(nameof(request));

      // Request shaping: apply defaults for missing Engine; returns new instance (no caller mutation)
      var shaped = ShapeRequest(request);
      var canonicalPreset = CanonicalEmotionPresetKey.Normalize(request.Emotion);
      if (canonicalPreset != null)
      {
        // Avoid engine-native emotion + authority prosody stacking for GAP-050 presets.
        shaped.Emotion = null;
      }

      try
      {
        var response = await _backend.SynthesizeVoiceAsync(shaped, cancellationToken);
        // Policy: normalize null/empty AudioUrl to string.Empty for downstream consumers
        if (response != null && string.IsNullOrEmpty(response.AudioUrl) && !string.IsNullOrEmpty(response.AudioId))
        {
          response.AudioUrl = $"/api/audio/{response.AudioId}";
        }
        response ??= new VoiceSynthesisResponse();

        if (canonicalPreset != null && !string.IsNullOrEmpty(response.AudioId))
        {
          await ApplyCanonicalPresetProsodyAsync(response, canonicalPreset, cancellationToken).ConfigureAwait(false);
        }

        return response;
      }
      catch (System.Exception ex)
      {
        throw MapToUserActionableException(ex);
      }
    }

    private async Task ApplyCanonicalPresetProsodyAsync(
        VoiceSynthesisResponse response,
        string canonicalPresetKey,
        CancellationToken cancellationToken)
    {
      try
      {
        var applied = await _emotionControlClient.ApplyEmotionAsync(
            new EmotionApplyExtendedRequest
            {
              AudioId = response.AudioId,
              PrimaryEmotion = canonicalPresetKey,
              PrimaryIntensity = 100f,
              SecondaryEmotion = null,
              SecondaryIntensity = 0f,
            },
            cancellationToken).ConfigureAwait(false);

        if (applied == null || string.IsNullOrEmpty(applied.AudioId))
        {
          response.EmotionPresetApplyFailureMessage =
              "Emotion preset could not be applied; using base synthesis audio.";
          return;
        }

        response.AudioId = applied.AudioId;
        response.AudioUrl = string.IsNullOrEmpty(applied.AudioUrl)
            ? $"/api/audio/{applied.AudioId}"
            : applied.AudioUrl;
        response.ProsodyHandling = applied.ProsodyHandling;
        response.EmotionMappingSource = applied.EmotionMappingSource ?? string.Empty;
      }
      catch (System.Exception ex)
      {
        _logger?.LogWarning(ex, "GAP-050 apply-extended failed for preset {Preset}; using base synthesis audio.", canonicalPresetKey);
        response.EmotionPresetApplyFailureMessage =
            "Emotion preset could not be applied; using base synthesis audio.";
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

  /// <summary>GAP-050 canonical preset labels (must match backend emotion list / mapper).</summary>
  internal static class CanonicalEmotionPresetKey
  {
    private static readonly System.Collections.Generic.HashSet<string> Keys =
        new(System.StringComparer.OrdinalIgnoreCase)
        {
            "neutral",
            "warm",
            "energetic",
            "calm",
        };

    /// <summary>Returns lowercase canonical key, or null if not a GAP-050 preset.</summary>
    public static string? Normalize(string? emotion)
    {
      if (string.IsNullOrWhiteSpace(emotion))
        return null;
      var t = emotion.Trim();
      return Keys.Contains(t) ? t.ToLowerInvariant() : null;
    }
  }
}
