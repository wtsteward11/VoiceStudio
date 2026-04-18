using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace VoiceStudio.App.Tests.ViewModels;

/// <summary>
/// Slice 11 — Live backend: synthesis error payloads must not advertise automatic
/// utility substitution engines (<c>gtts_utility</c> / <c>pyttsx3_utility</c>).
/// </summary>
[TestClass]
[TestCategory("LiveBackend")]
public sealed class EngineFailureTruthfulnessLiveBackendTests
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

  [TestMethod]
  public async Task Synthesize_InvalidEngine_ResponseBody_HasNoUtilityFallbackMarkers()
  {
    using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
    var baseUri = BackendBase.TrimEnd('/');
    try
    {
      using var ping = await client.GetAsync(new Uri($"{baseUri}/api/health/ready"));
      if (!ping.IsSuccessStatusCode)
      {
        Assert.Inconclusive($"Backend not reachable at {baseUri} (ready check {ping.StatusCode}).");
      }
    }
    catch (Exception ex)
    {
      Assert.Inconclusive($"Backend not reachable at {baseUri}: {ex.Message}");
    }

    var payload = new
    {
      engine = "__invalid_engine_slice11_probe__",
      profile_id = "local",
      text = "test",
      language = "en",
    };
    var json = JsonSerializer.Serialize(payload);
    using var req = new HttpRequestMessage(HttpMethod.Post, $"{baseUri}/api/voice/synthesize")
    {
      Content = new StringContent(json, Encoding.UTF8, "application/json"),
    };
    req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

    using var response = await client.SendAsync(req);
    var body = await response.Content.ReadAsStringAsync();

    Assert.IsFalse(
      body.Contains("gtts_utility", StringComparison.OrdinalIgnoreCase),
      "Error payloads must not reference automatic gTTS utility substitution.");
    Assert.IsFalse(
      body.Contains("pyttsx3_utility", StringComparison.OrdinalIgnoreCase),
      "Error payloads must not reference automatic pyttsx3 utility substitution.");
  }
}
