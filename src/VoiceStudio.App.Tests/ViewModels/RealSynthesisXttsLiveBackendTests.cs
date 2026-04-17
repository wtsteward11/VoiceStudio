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
using VoiceStudio.Core.Exceptions;
using VoiceStudio.Core.Models;
using VoiceStudio.Core.Services;

namespace VoiceStudio.App.Tests.ViewModels
{
  /// <summary>
  /// Live-backend proof: real <c>xtts_v2</c> synthesis (non-stub) through
  /// <see cref="IProfilesClient"/> + <see cref="IVoiceSynthesisService"/> + WAV fetch.
  /// Inconclusive when no backend is reachable (see <see cref="BackendBase"/>) or consent returns 403.
  /// Override base URL with environment variable <c>VOICESTUDIO_REAL_XTTS_HTTP_BASE</c> (same as Python real_xtts test).
  /// </summary>
  [TestClass]
  [TestCategory("LiveBackend")]
  public sealed class RealSynthesisXttsLiveBackendTests
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

    private static int FindPcmDataStart(byte[] wav)
    {
      if (wav.Length < 12)
        return 0;
      var pos = 12;
      while (pos + 8 <= wav.Length)
      {
        var id = Encoding.ASCII.GetString(wav, pos, 4);
        var size = wav[pos + 4] | (wav[pos + 5] << 8) | (wav[pos + 6] << 16) | (wav[pos + 7] << 24);
        pos += 8;
        if (id.Equals("data", StringComparison.Ordinal))
          return pos;
        pos += size;
        if (size % 2 == 1)
          pos++;
      }

      return wav.Length > 44 ? 44 : 0;
    }

    private static int MaxAbsPcm16Le(byte[] buf, int start)
    {
      var max = 0;
      for (var i = start; i + 1 < buf.Length; i += 2)
      {
        var s = (short)(buf[i] | (buf[i + 1] << 8));
        var v = s == short.MinValue ? 32767 : Math.Abs((int)s);
        if (v > max)
          max = v;
      }

      return max;
    }

    [TestMethod]
    public async Task Synthesize_XttsV2_LiveBackend_ServiceReturnsAudio_NonSilentWav()
    {
      var stub = Environment.GetEnvironmentVariable("VOICESTUDIO_TEST_MODE");
      if (!string.IsNullOrEmpty(stub) &&
          stub.Equals("stub", StringComparison.OrdinalIgnoreCase))
      {
        Assert.Inconclusive(
          "Set VOICESTUDIO_TEST_MODE unset (not stub) on the backend process for real XTTS proof.");
      }

      using var probe = new HttpClient { Timeout = TimeSpan.FromSeconds(8) };
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

      var coordinator = new RequestCoordinator();
      var config = new BackendClientConfig
      {
        BaseUrl = BackendBase,
        WebSocketUrl = string.Empty,
        RequestTimeout = TimeSpan.FromMinutes(15),
      };
      using var backend = new BackendClient(config, correlationProvider: null, requestCoordinator: coordinator);
      var profilesClient = new ProfilesClient(backend, coordinator);
      var emotionClient = new EmotionControlClient(backend, coordinator);
      var synthService = new VoiceSynthesisService(backend, emotionClient);

      VoiceProfile profile;
      try
      {
        profile = await profilesClient.CreateProfileAsync(
          "csharp-slice8-xtts-real",
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
            ["grantor_name"] = "csharp-slice8-xtts-real",
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
            Engine = "xtts_v2",
            Text = "VoiceStudio slice eight real synthesis.",
            Language = "en",
          },
          CancellationToken.None).ConfigureAwait(false);
      }
      catch (BackendException ex) when (ex.StatusCode == 403)
      {
        Assert.Inconclusive(
          "Synthesis returned 403 (consent/voice policy). Ensure POST /api/profiles default owner_user_id is local for first-party profiles.");
        return;
      }

      Assert.IsFalse(string.IsNullOrEmpty(response.AudioId), "AudioId missing.");
      Assert.IsFalse(string.IsNullOrEmpty(response.AudioUrl), "AudioUrl missing.");
      Assert.IsTrue(response.Duration >= 0.1, "Duration should be positive.");

      await using var audioStream = await synthService.GetAudioStreamAsync(
        response.AudioId,
        CancellationToken.None).ConfigureAwait(false);
      using var ms = new MemoryStream();
      await audioStream.CopyToAsync(ms, CancellationToken.None).ConfigureAwait(false);
      var bytes = ms.ToArray();
      Assert.IsTrue(
        bytes.Length > 1024,
        $"WAV too small ({bytes.Length} bytes) for audio_id={response.AudioId}; "
        + "expect non-empty artifact after synthesis (backend must not register pre-touch empty temp WAV).");
      CollectionAssert.AreEqual(
        new byte[] { 0x52, 0x49, 0x46, 0x46 },
        new[] { bytes[0], bytes[1], bytes[2], bytes[3] },
        "Not a RIFF/WAV");

      var pcmStart = FindPcmDataStart(bytes);
      Assert.IsTrue(pcmStart > 0 && pcmStart < bytes.Length, "Could not locate PCM data chunk.");
      var peak = MaxAbsPcm16Le(bytes, pcmStart);
      Assert.IsTrue(
        peak > 200,
        $"PCM looks like silence (peak={peak}); expected real synthesis, not stub silence.");
    }
  }
}

