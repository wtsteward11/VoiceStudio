using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using VoiceStudio.Core.Models;
using VoiceStudio.Core.Services;

namespace VoiceStudio.App.Services
{
  /// <summary>
  /// Implements profile preview playback with caching.
  /// </summary>
  public sealed class ProfilePreviewService : IProfilePreviewService
  {
    private const string DefaultPreviewText = "Hello, this is a preview of this voice profile.";

    private readonly IBackendClient _backendClient;
    private readonly IAudioPlayerService _audioPlayer;
    private readonly HttpClient _httpClient;

    private readonly Dictionary<string, string> _previewCache = new();
    private readonly Dictionary<string, QualityMetrics?> _previewQualityCache = new();
    private readonly Dictionary<string, double> _previewQualityScoreCache = new();

    public ProfilePreviewService(
      IBackendClient backendClient,
      IAudioPlayerService audioPlayer,
      HttpClient httpClient)
    {
      _backendClient = backendClient ?? throw new ArgumentNullException(nameof(backendClient));
      _audioPlayer = audioPlayer ?? throw new ArgumentNullException(nameof(audioPlayer));
      _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
    }

    public async Task<PreviewResult?> GetOrCreatePreviewAsync(string profileId, VoiceProfile profile, CancellationToken ct)
    {
      if (string.IsNullOrWhiteSpace(profileId) || profile == null)
        return null;

      string? audioUrl;
      QualityMetrics? qualityMetrics;
      double? qualityScore;

      if (_previewCache.TryGetValue(profileId, out var cachedUrl))
      {
        audioUrl = cachedUrl;
        _previewQualityCache.TryGetValue(profileId, out qualityMetrics);
        qualityScore = _previewQualityScoreCache.TryGetValue(profileId, out var s) ? s : null;
      }
      else
      {
        var request = new VoiceSynthesisRequest
        {
          Engine = "xtts",
          ProfileId = profileId,
          Text = DefaultPreviewText,
          Language = profile.Language ?? "en",
          Emotion = string.IsNullOrWhiteSpace(profile.Emotion) ? null : profile.Emotion,
          EnhanceQuality = false
        };

        var response = await _backendClient.SynthesizeVoiceAsync(request, ct).ConfigureAwait(false);
        audioUrl = response.AudioUrl;
        qualityMetrics = response.QualityMetrics;
        qualityScore = response.QualityScore;

        if (!string.IsNullOrWhiteSpace(audioUrl))
        {
          _previewCache[profileId] = audioUrl;
          if (qualityMetrics != null)
            _previewQualityCache[profileId] = qualityMetrics;
          if (qualityScore.HasValue)
            _previewQualityScoreCache[profileId] = qualityScore.Value;
        }
      }

      if (string.IsNullOrWhiteSpace(audioUrl))
        return new PreviewResult(null, qualityMetrics, qualityScore);

      var audioBytes = await _httpClient.GetByteArrayAsync(audioUrl, ct).ConfigureAwait(false);
      var tempPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"voicestudio_preview_{Guid.NewGuid()}.wav");
      await System.IO.File.WriteAllBytesAsync(tempPath, audioBytes, ct).ConfigureAwait(false);

      var tcs = new TaskCompletionSource<PreviewResult?>();
      await _audioPlayer.PlayFileAsync(tempPath, () =>
      {
        try
        {
          if (System.IO.File.Exists(tempPath))
            System.IO.File.Delete(tempPath);
        }
        // ALLOWED: empty catch - best-effort temp file cleanup; failure is non-fatal (file may be locked)
        catch (IOException)
        {
        }
        tcs.TrySetResult(new PreviewResult(audioUrl, qualityMetrics, qualityScore));
      }).ConfigureAwait(false);

      return await tcs.Task.WaitAsync(ct).ConfigureAwait(false);
    }

    public void StopPreview()
    {
      _audioPlayer.Stop();
    }

    public bool TryGetCachedQuality(string profileId, out QualityMetrics? qualityMetrics, out double? qualityScore)
    {
      qualityMetrics = null;
      qualityScore = null;
      if (string.IsNullOrWhiteSpace(profileId))
        return false;
      var hasMetrics = _previewQualityCache.TryGetValue(profileId, out qualityMetrics);
      var hasScore = _previewQualityScoreCache.TryGetValue(profileId, out var s);
      if (hasScore)
        qualityScore = s;
      return hasMetrics || hasScore || _previewCache.ContainsKey(profileId);
    }
  }
}
