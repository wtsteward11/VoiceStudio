using System;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using VoiceStudio.App.Services;
using VoiceStudio.App.Tests.Fixtures;
using VoiceStudio.App.Utilities;
using VoiceStudio.Core.Models;

namespace VoiceStudio.App.Tests.ViewModels
{
  /// <summary>
  /// Live-backend proof for Synthesis stub: create profile → synthesize → fetch WAV
  /// through real HTTP against the Python backend (VOICESTUDIO_TEST_MODE=stub).
  /// Skips with Inconclusive when no backend is running.
  /// </summary>
  [TestClass]
  [TestCategory("LiveBackend")]
  public sealed class SynthesisStubLiveBackendTests
  {
    private const string BackendBase = "http://127.0.0.1:8000";

    [TestMethod]
    public async Task Synthesize_LiveBackend_ReturnsAudioId_FetchableAsWav()
    {
      TestAppServicesHelper.EnsureInitialized();

      using var http = new HttpClient
      {
        BaseAddress = new Uri(BackendBase),
        Timeout = TimeSpan.FromSeconds(60),
      };
      var jsonOptions = JsonSerializerOptionsFactory.BackendApi;

      // 0. Health probe
      try
      {
        using var health = await http.GetAsync("/api/health", CancellationToken.None).ConfigureAwait(false);
        if (!health.IsSuccessStatusCode)
        {
          Assert.Inconclusive($"Backend /health returned {(int)health.StatusCode}; start backend first.");
        }
      }
      catch (Exception ex)
      {
        Assert.Inconclusive($"Live backend not reachable at {BackendBase}: {ex.Message}");
        return;
      }

      // 1. Create a profile
      var profileResp = await http.PostAsJsonAsync(
        "/api/profiles",
        new { name = "csharp-synth-stub", description = "C# live-backend synth stub test" },
        jsonOptions,
        CancellationToken.None).ConfigureAwait(false);
      Assert.IsTrue(profileResp.IsSuccessStatusCode,
        $"Profile creation failed: {(int)profileResp.StatusCode}");

      var profileBody = await profileResp.Content.ReadAsStringAsync().ConfigureAwait(false);
      using var profileDoc = JsonDocument.Parse(profileBody);
      var profileId = profileDoc.RootElement.TryGetProperty("id", out var idEl) ? idEl.GetString()
        : profileDoc.RootElement.TryGetProperty("profile_id", out var pidEl) ? pidEl.GetString()
        : null;
      Assert.IsFalse(string.IsNullOrEmpty(profileId), $"No profile id in response: {profileBody}");

      // 2. Synthesize (stub mode)
      var synthResp = await http.PostAsJsonAsync(
        "/api/voice/synthesize",
        new { profile_id = profileId, engine = "piper", text = "C# live stub test.", language = "en" },
        jsonOptions,
        CancellationToken.None).ConfigureAwait(false);
      if (synthResp.StatusCode == System.Net.HttpStatusCode.Forbidden)
      {
        Assert.Inconclusive(
          "Synthesis returned 403 (consent/voice policy). Re-run with backend started using VOICESTUDIO_TEST_MODE=stub for stub synthesis live proof.");
      }

      Assert.IsTrue(synthResp.IsSuccessStatusCode,
        $"Synthesis failed: {(int)synthResp.StatusCode} - {await synthResp.Content.ReadAsStringAsync()}");

      var synthBody = await synthResp.Content.ReadAsStringAsync().ConfigureAwait(false);
      using var synthDoc = JsonDocument.Parse(synthBody);
      string? audioId = null;
      if (synthDoc.RootElement.TryGetProperty("audio_id", out var aidEl))
        audioId = aidEl.GetString();
      Assert.IsFalse(string.IsNullOrEmpty(audioId), $"No audio_id in synthesis response: {synthBody}");

      // 3. Fetch the audio file
      var audioResp = await http.GetAsync($"/api/audio/file/{audioId}", CancellationToken.None).ConfigureAwait(false);
      Assert.IsTrue(audioResp.IsSuccessStatusCode,
        $"Audio fetch failed: {(int)audioResp.StatusCode}");

      var audioBytes = await audioResp.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
      Assert.IsTrue(audioBytes.Length > 100,
        $"Audio content too small: {audioBytes.Length} bytes");
      CollectionAssert.AreEqual(
        new byte[] { 0x52, 0x49, 0x46, 0x46 },
        new[] { audioBytes[0], audioBytes[1], audioBytes[2], audioBytes[3] },
        "Response does not start with RIFF header (not valid WAV)");
    }
  }
}
