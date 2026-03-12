using System;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using VoiceStudio.App.Logging;
using VoiceStudio.Core.Models;
using VoiceStudio.Core.Services;

namespace VoiceStudio.App.Services
{
  /// <summary>
  /// Timeline synthesis service. Builds requests, calls backend, optionally saves to project.
  /// Owns filename sanitization and soft-degradation on save failure.
  /// </summary>
  public sealed class TimelineSynthesisService : ITimelineSynthesisService
  {
    private readonly IBackendClient _backend;
    private readonly IProjectAudioClient _projectAudio;

    public TimelineSynthesisService(IBackendClient backend, IProjectAudioClient projectAudio)
    {
      _backend = backend ?? throw new ArgumentNullException(nameof(backend));
      _projectAudio = projectAudio ?? throw new ArgumentNullException(nameof(projectAudio));
    }

    public async Task<SynthesisResult> SynthesizeAndSaveAsync(
      string engine,
      string profileId,
      string text,
      bool enhanceQuality,
      string? projectId,
      IProgress<int>? progress,
      CancellationToken cancellationToken = default)
    {
      var request = new VoiceSynthesisRequest
      {
        Engine = engine,
        ProfileId = profileId,
        Text = text,
        Language = "en",
        EnhanceQuality = enhanceQuality
      };

      progress?.Report(25);
      var response = await _backend.SynthesizeVoiceAsync(request, cancellationToken).ConfigureAwait(false);
      progress?.Report(75);

      string? savedFilename = null;
      if (projectId != null && !string.IsNullOrEmpty(response.AudioId))
      {
        var filename = BuildFilename(text);
        try
        {
          await _projectAudio.SaveAudioToProjectAsync(projectId, response.AudioId, filename, cancellationToken).ConfigureAwait(false);
          savedFilename = filename;
        }
        catch (Exception ex)
        {
          ErrorLogger.LogWarning(
            $"Save audio to project failed: {ex.Message}",
            "TimelineSynthesisService.SynthesizeAndSaveAsync");
        }
      }

      progress?.Report(100);
      return new SynthesisResult(
        response.AudioId ?? string.Empty,
        response.AudioUrl ?? string.Empty,
        response.QualityScore,
        response.Duration,
        savedFilename);
    }

    private static string BuildFilename(string text)
    {
      var stem = text.Substring(0, Math.Min(30, text.Length)).Replace(" ", "_");
      stem = Regex.Replace(stem, @"[^\w\.-]", "");
      return $"{stem}_{DateTime.Now:yyyyMMdd_HHmmss}.wav";
    }
  }
}
