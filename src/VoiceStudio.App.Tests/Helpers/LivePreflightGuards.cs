using System;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace VoiceStudio.App.Tests.Helpers;

/// <summary>
/// Shared preflight gates for opt-in live-backend engine proofs (Slice 9+).
/// Ensures the process at <paramref name="backendBase"/> exposes honest engine checks before expensive synthesis.
/// </summary>
internal static class LivePreflightGuards
{
  /// <summary>
  /// Requires <c>GET /api/health/preflight</c> → <c>checks.espeak_ng.ok == true</c>.
  /// Use the same <paramref name="backendBase"/> as <see cref="VoiceStudio.App.Services.BackendClientConfig.BaseUrl"/>.
  /// </summary>
  public static async Task AssertEspeakNgPreflightOkAsync(
    HttpClient probe,
    string backendBase,
    CancellationToken cancellationToken = default)
  {
    ArgumentNullException.ThrowIfNull(probe);
    if (string.IsNullOrWhiteSpace(backendBase))
    {
      throw new ArgumentException("backendBase is required.", nameof(backendBase));
    }

    var baseUri = new Uri(backendBase.TrimEnd('/') + "/", UriKind.Absolute);
    using var resp = await probe
      .GetAsync(new Uri(baseUri, "/api/health/preflight"), cancellationToken)
      .ConfigureAwait(false);
    if (!resp.IsSuccessStatusCode)
    {
      Assert.Inconclusive(
        $"GET /api/health/preflight returned {(int)resp.StatusCode} at {backendBase}; cannot prove espeak_ng.");
      return;
    }

    var json = await resp.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
    using var doc = JsonDocument.Parse(json);
    if (!doc.RootElement.TryGetProperty("checks", out var checks))
    {
      Assert.Inconclusive("Preflight JSON missing \"checks\"; cannot gate espeak_ng proof.");
      return;
    }

    if (!checks.TryGetProperty("espeak_ng", out var esEl))
    {
      Assert.Inconclusive(
        "Preflight missing checks.espeak_ng; use repo backend with current health routes.");
      return;
    }

    if (!esEl.TryGetProperty("ok", out var okEl) || okEl.ValueKind != JsonValueKind.True)
    {
      var msg = "";
      if (esEl.TryGetProperty("message", out var msgEl) && msgEl.ValueKind == JsonValueKind.String)
      {
        msg = msgEl.GetString() ?? "";
      }

      Assert.Inconclusive(
        $"checks.espeak_ng.ok is not true (install eSpeak NG on PATH or set manifest executable_path). {msg}".Trim());
    }
  }

  /// <summary>
  /// Requires <c>GET /api/health/preflight</c> → <c>checks.rhvoice.ok == true</c>.
  /// Use the same <paramref name="backendBase"/> as <see cref="VoiceStudio.App.Services.BackendClientConfig.BaseUrl"/>.
  /// </summary>
  public static async Task AssertRhVoicePreflightOkAsync(
    HttpClient probe,
    string backendBase,
    CancellationToken cancellationToken = default)
  {
    ArgumentNullException.ThrowIfNull(probe);
    if (string.IsNullOrWhiteSpace(backendBase))
    {
      throw new ArgumentException("backendBase is required.", nameof(backendBase));
    }

    var baseUri = new Uri(backendBase.TrimEnd('/') + "/", UriKind.Absolute);
    using var resp = await probe
      .GetAsync(new Uri(baseUri, "/api/health/preflight"), cancellationToken)
      .ConfigureAwait(false);
    if (!resp.IsSuccessStatusCode)
    {
      Assert.Inconclusive(
        $"GET /api/health/preflight returned {(int)resp.StatusCode} at {backendBase}; cannot prove rhvoice.");
      return;
    }

    var json = await resp.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
    using var doc = JsonDocument.Parse(json);
    if (!doc.RootElement.TryGetProperty("checks", out var checks))
    {
      Assert.Inconclusive("Preflight JSON missing \"checks\"; cannot gate rhvoice proof.");
      return;
    }

    if (!checks.TryGetProperty("rhvoice", out var rvEl))
    {
      Assert.Inconclusive(
        "Preflight missing checks.rhvoice; use repo backend with current health routes.");
      return;
    }

    if (!rvEl.TryGetProperty("ok", out var okEl) || okEl.ValueKind != JsonValueKind.True)
    {
      var msg = "";
      if (rvEl.TryGetProperty("message", out var msgEl) && msgEl.ValueKind == JsonValueKind.String)
      {
        msg = msgEl.GetString() ?? "";
      }

      Assert.Inconclusive(
        $"checks.rhvoice.ok is not true (install RHVoice on PATH or set parameters.executable_path). {msg}".Trim());
    }
  }

  /// <summary>
  /// Requires <c>GET /api/health/preflight</c> → <c>checks.silero.ok == true</c>.
  /// Use the same <paramref name="backendBase"/> as <see cref="VoiceStudio.App.Services.BackendClientConfig.BaseUrl"/>.
  /// </summary>
  public static async Task AssertSileroPreflightOkAsync(
    HttpClient probe,
    string backendBase,
    CancellationToken cancellationToken = default)
  {
    ArgumentNullException.ThrowIfNull(probe);
    if (string.IsNullOrWhiteSpace(backendBase))
    {
      throw new ArgumentException("backendBase is required.", nameof(backendBase));
    }

    var baseUri = new Uri(backendBase.TrimEnd('/') + "/", UriKind.Absolute);
    using var resp = await probe
      .GetAsync(new Uri(baseUri, "/api/health/preflight"), cancellationToken)
      .ConfigureAwait(false);
    if (!resp.IsSuccessStatusCode)
    {
      Assert.Inconclusive(
        $"GET /api/health/preflight returned {(int)resp.StatusCode} at {backendBase}; cannot prove silero.");
      return;
    }

    var json = await resp.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
    using var doc = JsonDocument.Parse(json);
    if (!doc.RootElement.TryGetProperty("checks", out var checks))
    {
      Assert.Inconclusive("Preflight JSON missing \"checks\"; cannot gate silero proof.");
      return;
    }

    if (!checks.TryGetProperty("silero", out var siEl))
    {
      Assert.Inconclusive(
        "Preflight missing checks.silero; use repo backend with current health routes.");
      return;
    }

    if (!siEl.TryGetProperty("ok", out var okEl) || okEl.ValueKind != JsonValueKind.True)
    {
      var msg = "";
      if (siEl.TryGetProperty("message", out var msgEl) && msgEl.ValueKind == JsonValueKind.String)
      {
        msg = msgEl.GetString() ?? "";
      }

      Assert.Inconclusive(
        $"checks.silero.ok is not true (warm torch.hub cache for snakers4/silero-models or fix torch). {msg}".Trim());
    }
  }

  /// <summary>
  /// Requires <c>GET /api/health/preflight</c> → <c>checks.chatterbox.ok == true</c>.
  /// Use the same <paramref name="backendBase"/> as <see cref="VoiceStudio.App.Services.BackendClientConfig.BaseUrl"/>.
  /// </summary>
  public static async Task AssertChatterboxPreflightOkAsync(
    HttpClient probe,
    string backendBase,
    CancellationToken cancellationToken = default)
  {
    ArgumentNullException.ThrowIfNull(probe);
    if (string.IsNullOrWhiteSpace(backendBase))
    {
      throw new ArgumentException("backendBase is required.", nameof(backendBase));
    }

    var baseUri = new Uri(backendBase.TrimEnd('/') + "/", UriKind.Absolute);
    using var resp = await probe
      .GetAsync(new Uri(baseUri, "/api/health/preflight"), cancellationToken)
      .ConfigureAwait(false);
    if (!resp.IsSuccessStatusCode)
    {
      Assert.Inconclusive(
        $"GET /api/health/preflight returned {(int)resp.StatusCode} at {backendBase}; cannot prove chatterbox.");
      return;
    }

    var json = await resp.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
    using var doc = JsonDocument.Parse(json);
    if (!doc.RootElement.TryGetProperty("checks", out var checks))
    {
      Assert.Inconclusive("Preflight JSON missing \"checks\"; cannot gate chatterbox proof.");
      return;
    }

    if (!checks.TryGetProperty("chatterbox", out var cbEl))
    {
      Assert.Inconclusive(
        "Preflight missing checks.chatterbox; use repo backend with current health routes.");
      return;
    }

    if (!cbEl.TryGetProperty("ok", out var okEl) || okEl.ValueKind != JsonValueKind.True)
    {
      var msg = "";
      if (cbEl.TryGetProperty("message", out var msgEl) && msgEl.ValueKind == JsonValueKind.String)
      {
        msg = msgEl.GetString() ?? "";
      }

      Assert.Inconclusive(
        $"checks.chatterbox.ok is not true (install chatterbox-tts deps + HF cache ResembleAI/chatterbox). {msg}".Trim());
    }
  }

  /// <summary>
  /// Requires <c>GET /api/health/preflight</c> → <c>checks.tortoise.ok == true</c>.
  /// Use the same <paramref name="backendBase"/> as <see cref="VoiceStudio.App.Services.BackendClientConfig.BaseUrl"/>.
  /// </summary>
  public static async Task AssertTortoisePreflightOkAsync(
    HttpClient probe,
    string backendBase,
    CancellationToken cancellationToken = default)
  {
    ArgumentNullException.ThrowIfNull(probe);
    if (string.IsNullOrWhiteSpace(backendBase))
    {
      throw new ArgumentException("backendBase is required.", nameof(backendBase));
    }

    var baseUri = new Uri(backendBase.TrimEnd('/') + "/", UriKind.Absolute);
    using var resp = await probe
      .GetAsync(new Uri(baseUri, "/api/health/preflight"), cancellationToken)
      .ConfigureAwait(false);
    if (!resp.IsSuccessStatusCode)
    {
      Assert.Inconclusive(
        $"GET /api/health/preflight returned {(int)resp.StatusCode} at {backendBase}; cannot prove tortoise.");
      return;
    }

    var json = await resp.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
    using var doc = JsonDocument.Parse(json);
    if (!doc.RootElement.TryGetProperty("checks", out var checks))
    {
      Assert.Inconclusive("Preflight JSON missing \"checks\"; cannot gate tortoise proof.");
      return;
    }

    if (!checks.TryGetProperty("tortoise", out var toEl))
    {
      Assert.Inconclusive(
        "Preflight missing checks.tortoise; use repo backend with current health routes.");
      return;
    }

    if (!toEl.TryGetProperty("ok", out var okEl) || okEl.ValueKind != JsonValueKind.True)
    {
      var msg = "";
      if (toEl.TryGetProperty("message", out var msgEl) && msgEl.ValueKind == JsonValueKind.String)
      {
        msg = msgEl.GetString() ?? "";
      }

      Assert.Inconclusive(
        $"checks.tortoise.ok is not true (install tortoise-tts + torch + warm tortoise_models cache). {msg}".Trim());
    }
  }
}
