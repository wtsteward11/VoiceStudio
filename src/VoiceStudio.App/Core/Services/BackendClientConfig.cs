using System;

namespace VoiceStudio.Core.Services
{
  /// <summary>
  /// Loopback host matches uvicorn <c>--host 127.0.0.1</c> from BackendProcessManager to avoid
  /// Windows <c>localhost</c> resolving to <c>::1</c> while the server listens on IPv4 only.
  /// </summary>
  public class BackendClientConfig
  {
    public const string DefaultHttpBaseUrl = "http://127.0.0.1:8000";
    /// <summary>Matches <c>/ws/realtime</c> route in <c>backend/api/route_registry.py</c>.</summary>
    public const string DefaultWebSocketUrl = "ws://127.0.0.1:8000/ws/realtime";

    public string BaseUrl { get; set; } = DefaultHttpBaseUrl;
    public string WebSocketUrl { get; set; } = DefaultWebSocketUrl;
    public TimeSpan RequestTimeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Single authority for HTTP + WS URLs: <c>VOICESTUDIO_BACKEND_URL</c> (absolute http/https)
    /// when set and valid; otherwise <c>VOICESTUDIO_API_HOST</c> (default <c>127.0.0.1</c>) and
    /// <c>VOICESTUDIO_API_PORT</c> (default <c>8000</c>). Aligns DI, diagnostics, and launch profiles.
    /// </summary>
    public static BackendClientConfig FromEnvironment()
    {
      var backendUrlRaw = Environment.GetEnvironmentVariable("VOICESTUDIO_BACKEND_URL");
      if (!string.IsNullOrWhiteSpace(backendUrlRaw)
          && Uri.TryCreate(backendUrlRaw.Trim(), UriKind.Absolute, out var backendUri)
          && (backendUri.Scheme == Uri.UriSchemeHttp || backendUri.Scheme == Uri.UriSchemeHttps))
      {
        var authority = backendUri.GetLeftPart(UriPartial.Authority).TrimEnd('/');
        var host = backendUri.Host;
        var port = backendUri.IsDefaultPort
            ? (backendUri.Scheme == Uri.UriSchemeHttps ? 443 : 80)
            : backendUri.Port;
        var wsScheme = backendUri.Scheme == Uri.UriSchemeHttps ? "wss" : "ws";
        var wsUrl = $"{wsScheme}://{host}:{port}/ws/realtime";
        return ApplyOptionalWebSocketPortOverride(new BackendClientConfig { BaseUrl = authority, WebSocketUrl = wsUrl });
      }

      var apiHost = Environment.GetEnvironmentVariable("VOICESTUDIO_API_HOST");
      if (string.IsNullOrWhiteSpace(apiHost))
      {
        apiHost = "127.0.0.1";
      }

      var apiPort = Environment.GetEnvironmentVariable("VOICESTUDIO_API_PORT") ?? "8000";
      var baseUrl = $"http://{apiHost}:{apiPort}";
      return ApplyOptionalWebSocketPortOverride(new BackendClientConfig
      {
        BaseUrl = baseUrl,
        WebSocketUrl = $"ws://{apiHost}:{apiPort}/ws/realtime",
      });
    }

    /// <summary>Optional <c>VOICESTUDIO_WS_PORT</c>: WebSocket port only (HTTP base unchanged).</summary>
    private static BackendClientConfig ApplyOptionalWebSocketPortOverride(BackendClientConfig config)
    {
      var wsPortRaw = Environment.GetEnvironmentVariable("VOICESTUDIO_WS_PORT");
      if (string.IsNullOrWhiteSpace(wsPortRaw) || !int.TryParse(wsPortRaw, out var wsPort) || wsPort <= 0 || wsPort > 65535)
      {
        return config;
      }

      if (!Uri.TryCreate(config.BaseUrl, UriKind.Absolute, out var httpUri))
      {
        return config;
      }

      var wsScheme = httpUri.Scheme == Uri.UriSchemeHttps ? "wss" : "ws";
      config.WebSocketUrl = $"{wsScheme}://{httpUri.Host}:{wsPort}/ws/realtime";
      return config;
    }
  }
}