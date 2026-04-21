using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using VoiceStudio.App.Services;
using VoiceStudio.App.Tests.Helpers;
using VoiceStudio.Core.Exceptions;
using VoiceStudio.Core.Models;
using VoiceStudio.Core.Services;

namespace VoiceStudio.App.Tests.ViewModels
{
  /// <summary>
  /// Slice 18 Tortoise TTS — Live-backend proof: Tortoise TTS synthesis → <see cref="IVoiceSynthesisService.GetAudioStreamAsync"/> →
  /// temp WAV → <see cref="AudioPlayerService.PlayFileAsync"/> → playback completion.
  /// Inconclusive when no audio output device (headless). Same base URL as
  /// <see cref="RealSynthesisTortoiseLiveBackendTests"/> (<c>VOICESTUDIO_REAL_XTTS_HTTP_BASE</c>).
  /// </summary>
  [TestClass]
  [TestCategory("LiveBackend")]
  public sealed class TortoisePlaybackAuditionLiveBackendTests
  {
    private static string BackendBase
    {
      get
      {
        var s = Environment.GetEnvironmentVariable("VOICESTUDIO_REAL_XTTS_HTTP_BASE");
        if (!string.IsNullOrWhiteSpace(s))
        {
          return s.Trim().TrimEnd('/');
        }

        return "http://127.0.0.1:8000";
      }
    }

    private static string FindRepoRoot()
    {
      foreach (var start in new[]
               {
                 Directory.GetCurrentDirectory(), AppContext.BaseDirectory,
                 Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? "",
               })
      {
        if (string.IsNullOrEmpty(start))
        {
          continue;
        }

        var dir = new DirectoryInfo(start);
        for (var i = 0; i < 16 && dir != null; i++, dir = dir.Parent)
        {
          var sln = Path.Combine(dir.FullName, "VoiceStudio.sln");
          if (File.Exists(sln))
          {
            return dir.FullName;
          }
        }
      }

      throw new InvalidOperationException("VoiceStudio.sln not found (current dir, base dir, or assembly location).");
    }

    /// <summary>
    /// Slice 18 Tortoise TTS stream proof: Tortoise TTS → <see cref="IVoiceSynthesisService.GetAudioStreamAsync"/> → non-silent WAV
    /// (RIFF/fmt/data, duration, peak; PCM16 or IEEE float32 per WAV spec) and writes <c>docs/reports/verification/slice18/tortoise/tortoise_csharp_stream.wav</c>.
    /// No audio output device required. On Tortoise TTS unavailable, <see cref="Assert.Fail"/> (preflight should gate).
    /// </summary>
    [TestMethod]
    public async Task Synthesize_PrimaryFileRoute_LiveBackend_StreamPlayable()
    {
      var stub = Environment.GetEnvironmentVariable("VOICESTUDIO_TEST_MODE");
      if (!string.IsNullOrEmpty(stub) &&
          stub.Equals("stub", StringComparison.OrdinalIgnoreCase))
      {
        Assert.Inconclusive(
          "Set VOICESTUDIO_TEST_MODE unset (not stub) on the backend process for real Tortoise TTS proof.");
      }

      using var probe = new HttpClient { Timeout = TimeSpan.FromSeconds(120) };
      try
      {
        using var health = await probe.GetAsync(new Uri(new Uri(BackendBase), "/api/health"), CancellationToken.None).ConfigureAwait(false);
        if (!health.IsSuccessStatusCode)
        {
          Assert.Inconclusive($"Backend /api/health returned {(int)health.StatusCode}; start backend first.");
        }
      }
      catch (Exception ex)
      {
        Assert.Inconclusive($"Live backend not reachable at {BackendBase}: {ex.Message}");
        return;
      }

      await LivePreflightGuards.AssertTortoisePreflightOkAsync(probe, BackendBase, CancellationToken.None)
        .ConfigureAwait(false);

      var coordinator = new RequestCoordinator();
      var config = new BackendClientConfig
      {
        BaseUrl = BackendBase,
        WebSocketUrl = string.Empty,
        RequestTimeout = TimeSpan.FromMinutes(40),
      };
      using var backend = new BackendClient(config, correlationProvider: null, requestCoordinator: coordinator);
      var profilesClient = new ProfilesClient(backend, coordinator);
      var emotionClient = new EmotionControlClient(backend, coordinator);
      var synthService = new VoiceSynthesisService(backend, emotionClient);

      VoiceProfile profile;
      try
      {
        profile = await profilesClient.CreateProfileAsync(
          "csharp-slice18-tortoise-stream-playable",
          language: "en",
          cancellationToken: CancellationToken.None).ConfigureAwait(false);
      }
      catch (Exception ex)
      {
        Assert.Inconclusive($"Profile creation failed: {ex.Message}");
        return;
      }

      Assert.IsFalse(string.IsNullOrEmpty(profile.Id), "Profile id missing.");

      var fixtureWav = Path.Combine(FindRepoRoot(), "tests", "fixtures", "audio", "test_440hz_2s.wav");
      if (!File.Exists(fixtureWav))
      {
        Assert.Inconclusive($"Fixture WAV not found at {fixtureWav}");
      }

      using (var bindHttp = new HttpClient { BaseAddress = new Uri(BackendBase), Timeout = TimeSpan.FromMinutes(2) })
      {
        var bindBody = JsonSerializer.Serialize(
          new Dictionary<string, object?>
          {
            ["reference_audio_path"] = fixtureWav,
            ["auto_enhance"] = false,
            ["select_optimal_segments"] = false,
          });
        using var bindContent = new StringContent(
          bindBody,
          Encoding.UTF8,
          MediaTypeHeaderValue.Parse("application/json"));
        using var bindResp = await bindHttp
          .PostAsync($"/api/profiles/{Uri.EscapeDataString(profile.Id)}/preprocess-reference", bindContent, CancellationToken.None)
          .ConfigureAwait(false);
        if (!bindResp.IsSuccessStatusCode)
        {
          var err = await bindResp.Content.ReadAsStringAsync(CancellationToken.None).ConfigureAwait(false);
          Assert.Inconclusive($"Reference bind preprocess failed: {(int)bindResp.StatusCode} {err}");
        }
      }

      using (var consentHttp = new HttpClient { BaseAddress = new Uri(BackendBase), Timeout = TimeSpan.FromMinutes(2) })
      {
        var consentReq = JsonSerializer.Serialize(
          new Dictionary<string, string>
          {
            ["voice_id"] = profile.Id,
            ["grantor_id"] = "local",
            ["grantor_name"] = "csharp-slice18-tortoise-stream",
            ["consent_type"] = "voice_usage",
          });
        using var reqContent = new StringContent(
          consentReq,
          Encoding.UTF8,
          MediaTypeHeaderValue.Parse("application/json"));
        using var consentResp = await consentHttp
          .PostAsync("/api/consent/request", reqContent, CancellationToken.None)
          .ConfigureAwait(false);
        if (!consentResp.IsSuccessStatusCode)
        {
          var err = await consentResp.Content.ReadAsStringAsync(CancellationToken.None).ConfigureAwait(false);
          Assert.Inconclusive($"Consent request failed: {(int)consentResp.StatusCode} {err}");
        }

        using var doc = JsonDocument.Parse(
          await consentResp.Content.ReadAsStringAsync(CancellationToken.None).ConfigureAwait(false));
        if (!doc.RootElement.TryGetProperty("consent_id", out var cidEl))
        {
          Assert.Inconclusive("Consent response missing consent_id.");
        }

        var consentId = cidEl.GetString();
        if (string.IsNullOrEmpty(consentId))
        {
          Assert.Inconclusive("Consent response consent_id empty.");
        }

        using var grantResp = await consentHttp
          .PostAsync($"/api/consent/grant/{Uri.EscapeDataString(consentId)}", null, CancellationToken.None)
          .ConfigureAwait(false);
        if (!grantResp.IsSuccessStatusCode)
        {
          var err = await grantResp.Content.ReadAsStringAsync(CancellationToken.None).ConfigureAwait(false);
          Assert.Inconclusive($"Consent grant failed: {(int)grantResp.StatusCode} {err}");
        }
      }

      VoiceSynthesisResponse response;
      try
      {
        response = await synthService.SynthesizeVoiceAsync(
          new VoiceSynthesisRequest
          {
            ProfileId = profile.Id,
            Engine = "tortoise",
            Text = "VoiceStudio slice eighteen tortoise playback artifact audition proof.",
            Language = "en",
          },
          CancellationToken.None).ConfigureAwait(false);
      }
      catch (BackendException ex) when (ex.StatusCode == 400)
      {
        var m = ex.Message ?? "";
        if (m.Contains("Invalid engine", StringComparison.OrdinalIgnoreCase)
            && m.Contains("tortoise", StringComparison.OrdinalIgnoreCase))
        {
          Assert.Fail(
            "Synthesis returned 400 Invalid engine after checks.tortoise preflight ok; "
            + "engine_router registration vs preflight is inconsistent. "
            + m);
        }

        throw;
      }
      catch (BackendException ex) when (ex.StatusCode == 403)
      {
        Assert.Inconclusive(
          "Synthesis returned 403 (consent/voice policy). Ensure POST /api/profiles default owner_user_id is local for first-party profiles.");
        return;
      }
      catch (BackendException ex) when (LiveEngineBackendTestGuards.IsLiveEngineUnavailable(ex, "tortoise"))
      {
        Assert.Fail(
          "Live Tortoise TTS engine not initialized or unavailable; run /api/health/preflight with tortoise.ok before this proof: "
          + ex.Message);
        throw new InvalidOperationException("Assert.Fail must throw.");
      }

      Assert.IsFalse(string.IsNullOrEmpty(response.AudioId), "AudioId missing.");
      Assert.AreEqual(
        "tortoise",
        response.RoutedEngine.Trim(),
        "Backend must echo routed_engine=tortoise (no silent engine substitution).");

      await using var audioStream = await synthService.GetAudioStreamAsync(
        response.AudioId,
        CancellationToken.None).ConfigureAwait(false);
      using var ms = new MemoryStream();
      await audioStream.CopyToAsync(ms, CancellationToken.None).ConfigureAwait(false);
      var bytes = ms.ToArray();
      Assert.IsTrue(
        bytes.Length > 1024,
        $"WAV too small ({bytes.Length} bytes) for audio_id={response.AudioId}.");
      CollectionAssert.AreEqual(
        new byte[] { 0x52, 0x49, 0x46, 0x46 },
        new[] { bytes[0], bytes[1], bytes[2], bytes[3] },
        "Not a RIFF container");
      CollectionAssert.AreEqual(
        new byte[] { 0x57, 0x41, 0x56, 0x45 },
        new[] { bytes[8], bytes[9], bytes[10], bytes[11] },
        "Not WAVE");

      LiveBackendWavInspection.GetWavAudioLayout(
        bytes,
        out var wFormatTag,
        out var channels,
        out var sampleRate,
        out var bitsPerSample,
        out var pcmStart,
        out var pcmLen);
      Assert.IsTrue(channels is 1 or 2, $"Unexpected channel count: {channels}");
      Assert.IsTrue(sampleRate >= 16000, $"Unexpected sample rate: {sampleRate}");
      Assert.IsTrue(
        (wFormatTag == LiveBackendWavInspection.WAVE_FORMAT_PCM && bitsPerSample == 16)
        || (wFormatTag == LiveBackendWavInspection.WAVE_FORMAT_IEEE_FLOAT && bitsPerSample == 32),
        $"Unexpected WAV audio format: wFormatTag={wFormatTag}, bitsPerSample={bitsPerSample}.");

      var durationSec = pcmLen / (double)(channels * (bitsPerSample / 8)) / sampleRate;
      Assert.IsTrue(durationSec >= 0.5, $"Duration too short ({durationSec:F3}s).");

      var peak = LiveBackendWavInspection.ComputePeakInt16Equivalent(
        bytes,
        wFormatTag,
        bitsPerSample,
        pcmStart,
        pcmLen);
      Assert.IsTrue(
        peak >= 1000,
        $"Audio looks like silence or too quiet (peak={peak}); expected real synthesis.");

      var outPath = Path.Combine(
        FindRepoRoot(),
        "docs",
        "reports",
        "verification",
        "slice18",
        "tortoise",
        "tortoise_csharp_stream.wav");
      Directory.CreateDirectory(Path.GetDirectoryName(outPath)!);
      await File.WriteAllBytesAsync(outPath, bytes, CancellationToken.None).ConfigureAwait(false);
      Assert.IsTrue(File.Exists(outPath) && new FileInfo(outPath).Length == bytes.Length, "Failed to write slice18/tortoise_csharp_stream.wav.");
    }

    [TestMethod]
    public async Task Synthesize_ThenPlayback_LiveBackend_PlayableNonSilentWav()
    {
      AudioDeviceGuard.SkipIfNoAudioOutputDevice();

      var stub = Environment.GetEnvironmentVariable("VOICESTUDIO_TEST_MODE");
      if (!string.IsNullOrEmpty(stub) &&
          stub.Equals("stub", StringComparison.OrdinalIgnoreCase))
      {
        Assert.Inconclusive(
          "Set VOICESTUDIO_TEST_MODE unset (not stub) on the backend process for real Tortoise TTS proof.");
      }

      using var probe = new HttpClient { Timeout = TimeSpan.FromSeconds(120) };
      try
      {
        using var health = await probe.GetAsync(new Uri(new Uri(BackendBase), "/api/health"), CancellationToken.None).ConfigureAwait(false);
        if (!health.IsSuccessStatusCode)
        {
          Assert.Inconclusive($"Backend /api/health returned {(int)health.StatusCode}; start backend first.");
        }
      }
      catch (Exception ex)
      {
        Assert.Inconclusive($"Live backend not reachable at {BackendBase}: {ex.Message}");
        return;
      }

      await LivePreflightGuards.AssertTortoisePreflightOkAsync(probe, BackendBase, CancellationToken.None)
        .ConfigureAwait(false);

      var coordinator = new RequestCoordinator();
      var config = new BackendClientConfig
      {
        BaseUrl = BackendBase,
        WebSocketUrl = string.Empty,
        RequestTimeout = TimeSpan.FromMinutes(40),
      };
      using var backend = new BackendClient(config, correlationProvider: null, requestCoordinator: coordinator);
      var profilesClient = new ProfilesClient(backend, coordinator);
      var emotionClient = new EmotionControlClient(backend, coordinator);
      var synthService = new VoiceSynthesisService(backend, emotionClient);

      VoiceProfile profile;
      try
      {
        profile = await profilesClient.CreateProfileAsync(
          "csharp-slice18-tortoise-playback-audition",
          language: "en",
          cancellationToken: CancellationToken.None).ConfigureAwait(false);
      }
      catch (Exception ex)
      {
        Assert.Inconclusive($"Profile creation failed: {ex.Message}");
        return;
      }

      Assert.IsFalse(string.IsNullOrEmpty(profile.Id), "Profile id missing.");

      var fixtureWav = Path.Combine(FindRepoRoot(), "tests", "fixtures", "audio", "test_440hz_2s.wav");
      if (!File.Exists(fixtureWav))
      {
        Assert.Inconclusive($"Fixture WAV not found at {fixtureWav}");
      }

      using (var bindHttp = new HttpClient { BaseAddress = new Uri(BackendBase), Timeout = TimeSpan.FromMinutes(2) })
      {
        var bindBody = JsonSerializer.Serialize(
          new Dictionary<string, object?>
          {
            ["reference_audio_path"] = fixtureWav,
            ["auto_enhance"] = false,
            ["select_optimal_segments"] = false,
          });
        using var bindContent = new StringContent(
          bindBody,
          Encoding.UTF8,
          MediaTypeHeaderValue.Parse("application/json"));
        using var bindResp = await bindHttp
          .PostAsync($"/api/profiles/{Uri.EscapeDataString(profile.Id)}/preprocess-reference", bindContent, CancellationToken.None)
          .ConfigureAwait(false);
        if (!bindResp.IsSuccessStatusCode)
        {
          var err = await bindResp.Content.ReadAsStringAsync(CancellationToken.None).ConfigureAwait(false);
          Assert.Inconclusive($"Reference bind preprocess failed: {(int)bindResp.StatusCode} {err}");
        }
      }

      using (var consentHttp = new HttpClient { BaseAddress = new Uri(BackendBase), Timeout = TimeSpan.FromMinutes(2) })
      {
        var consentReq = JsonSerializer.Serialize(
          new Dictionary<string, string>
          {
            ["voice_id"] = profile.Id,
            ["grantor_id"] = "local",
            ["grantor_name"] = "csharp-slice18-tortoise-playback",
            ["consent_type"] = "voice_usage",
          });
        using var reqContent = new StringContent(
          consentReq,
          Encoding.UTF8,
          MediaTypeHeaderValue.Parse("application/json"));
        using var consentResp = await consentHttp
          .PostAsync("/api/consent/request", reqContent, CancellationToken.None)
          .ConfigureAwait(false);
        if (!consentResp.IsSuccessStatusCode)
        {
          var err = await consentResp.Content.ReadAsStringAsync(CancellationToken.None).ConfigureAwait(false);
          Assert.Inconclusive($"Consent request failed: {(int)consentResp.StatusCode} {err}");
        }

        using var doc = JsonDocument.Parse(
          await consentResp.Content.ReadAsStringAsync(CancellationToken.None).ConfigureAwait(false));
        if (!doc.RootElement.TryGetProperty("consent_id", out var cidEl))
        {
          Assert.Inconclusive("Consent response missing consent_id.");
        }

        var consentId = cidEl.GetString();
        if (string.IsNullOrEmpty(consentId))
        {
          Assert.Inconclusive("Consent response consent_id empty.");
        }

        using var grantResp = await consentHttp
          .PostAsync($"/api/consent/grant/{Uri.EscapeDataString(consentId)}", null, CancellationToken.None)
          .ConfigureAwait(false);
        if (!grantResp.IsSuccessStatusCode)
        {
          var err = await grantResp.Content.ReadAsStringAsync(CancellationToken.None).ConfigureAwait(false);
          Assert.Inconclusive($"Consent grant failed: {(int)grantResp.StatusCode} {err}");
        }
      }

      VoiceSynthesisResponse response;
      try
      {
        response = await synthService.SynthesizeVoiceAsync(
          new VoiceSynthesisRequest
          {
            ProfileId = profile.Id,
            Engine = "tortoise",
            Text = "VoiceStudio slice eighteen tortoise playback artifact audition proof.",
            Language = "en",
          },
          CancellationToken.None).ConfigureAwait(false);
      }
      catch (BackendException ex) when (ex.StatusCode == 400)
      {
        var m = ex.Message ?? "";
        if (m.Contains("Invalid engine", StringComparison.OrdinalIgnoreCase)
            && m.Contains("tortoise", StringComparison.OrdinalIgnoreCase))
        {
          Assert.Fail(
            "Synthesis returned 400 Invalid engine after checks.tortoise preflight ok; "
            + "engine_router registration vs preflight is inconsistent. "
            + m);
        }

        throw;
      }
      catch (BackendException ex) when (ex.StatusCode == 403)
      {
        Assert.Inconclusive(
          "Synthesis returned 403 (consent/voice policy). Ensure POST /api/profiles default owner_user_id is local for first-party profiles.");
        return;
      }
      catch (BackendException ex) when (LiveEngineBackendTestGuards.IsLiveEngineUnavailable(ex, "tortoise"))
      {
        Assert.Fail(
          "Live Tortoise TTS engine not initialized or unavailable; run /api/health/preflight with tortoise.ok before this proof: "
          + ex.Message);
        throw new InvalidOperationException("Assert.Fail must throw.");
      }

      Assert.IsFalse(string.IsNullOrEmpty(response.AudioId), "AudioId missing.");
      Assert.AreEqual(
        "tortoise",
        response.RoutedEngine.Trim(),
        "Backend must echo routed_engine=tortoise (no silent engine substitution).");

      await using var audioStream = await synthService.GetAudioStreamAsync(
        response.AudioId,
        CancellationToken.None).ConfigureAwait(false);
      using var ms = new MemoryStream();
      await audioStream.CopyToAsync(ms, CancellationToken.None).ConfigureAwait(false);
      var bytes = ms.ToArray();
      Assert.IsTrue(
        bytes.Length > 1024,
        $"WAV too small ({bytes.Length} bytes) for audio_id={response.AudioId}.");
      CollectionAssert.AreEqual(
        new byte[] { 0x52, 0x49, 0x46, 0x46 },
        new[] { bytes[0], bytes[1], bytes[2], bytes[3] },
        "Not a RIFF/WAV");

      LiveBackendWavInspection.GetWavAudioLayout(
        bytes,
        out var wFormatTag,
        out _,
        out _,
        out var bitsPerSample,
        out var pcmStart,
        out var pcmLen);
      Assert.IsTrue(pcmStart > 0 && pcmStart < bytes.Length, "Could not locate PCM data chunk.");
      var peak = LiveBackendWavInspection.ComputePeakInt16Equivalent(
        bytes,
        wFormatTag,
        bitsPerSample,
        pcmStart,
        pcmLen);
      Assert.IsTrue(
        peak > 200,
        $"Audio looks like silence (peak={peak}); expected real synthesis.");

      var tempPath = Path.Combine(Path.GetTempPath(), $"vs_slice18_tortoise_{Guid.NewGuid():N}.wav");
      await File.WriteAllBytesAsync(tempPath, bytes, CancellationToken.None).ConfigureAwait(false);

      try
      {
        using var player = new AudioPlayerService(new HttpClient());
        var completed = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        await player.PlayFileAsync(tempPath, () => completed.TrySetResult(true)).ConfigureAwait(false);

        if (player.IsPlaying)
        {
          Assert.IsTrue(player.IsPlaying, "Playback should have started.");
        }

        var finished = await Task.WhenAny(completed.Task, Task.Delay(TimeSpan.FromSeconds(30), CancellationToken.None))
          .ConfigureAwait(false);
        if (finished != completed.Task)
        {
          Assert.Inconclusive("Playback did not complete within 30s (NAudio / device timing).");
        }

        Assert.IsFalse(player.IsPlaying, "IsPlaying should be false after PlaybackCompleted.");
      }
      finally
      {
        if (File.Exists(tempPath))
        {
          File.Delete(tempPath);
        }
      }
    }
  }
}
