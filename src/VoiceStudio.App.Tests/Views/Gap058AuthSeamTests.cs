using System;
using System.Collections.Generic;
using System.Net.WebSockets;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using VoiceStudio.App.Services;

namespace VoiceStudio.App.Tests.Views;

/// <summary>
/// GAP-058 seam tests: WebSocket handshake headers and authentication close handling (no live socket).
/// </summary>
[TestClass]
public sealed class Gap058AuthSeamTests
{
  [TestMethod]
  public void WebSocketService_SetAuthHeaders_ResolveHandshakeHeaders_ContainsApiKey()
  {
    var svc = new WebSocketService("ws://127.0.0.1/ws/realtime");
    svc.SetAuthHeaders(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
      ["X-API-Key"] = "gap058-test-key",
    });

    var headers = svc.ResolveHandshakeHeadersForTests();
    Assert.IsNotNull(headers);
    Assert.IsTrue(headers!.TryGetValue("X-API-Key", out var key));
    Assert.AreEqual("gap058-test-key", key);
  }

  [TestMethod]
  public void WebSocketService_AuthenticationCloseCode4001_RaisesErrorEvent()
  {
    var svc = new WebSocketService("ws://127.0.0.1/ws/realtime");
    Exception? captured = null;
    svc.Error += (_, ex) => captured = ex;

    svc.NotifyAuthenticationRequiredCloseIfNeeded((WebSocketCloseStatus)WebSocketService.AuthenticationRequiredCloseCode);

    Assert.IsNotNull(captured, "Error event should fire for auth close code 4001.");
    StringAssert.Contains(captured!.Message, "authentication required", StringComparison.OrdinalIgnoreCase);
  }

  [TestMethod]
  public void PluginBridgeService_SetAuthHeaders_ResolvePluginHandshakeHeaders_ContainsApiKey()
  {
    var svc = new PluginBridgeService(NullLogger<PluginBridgeService>.Instance);
    svc.SetAuthHeaders(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
      ["X-API-Key"] = "gap058-plugin-key",
    });

    var headers = svc.ResolvePluginHandshakeHeadersForTests();
    Assert.IsNotNull(headers);
    Assert.IsTrue(headers!.TryGetValue("X-API-Key", out var key));
    Assert.AreEqual("gap058-plugin-key", key);
  }

  [TestMethod]
  public async Task PluginBridgeService_ReconnectLoop_StopsOnAuthenticationFailureAsync()
  {
    var svc = new PluginBridgeService(NullLogger<PluginBridgeService>.Instance)
    {
      AutoReconnect = true,
      ConnectHandshakeAsyncOverrideForSeamTests = () =>
        throw new WebSocketException("The server closed the connection with 4001."),
    };

    var t = typeof(PluginBridgeService);
    t.GetField("_lastBackendUrl", BindingFlags.Instance | BindingFlags.NonPublic)!
      .SetValue(svc, "http://127.0.0.1:9");
    t.GetField("_maxRetries", BindingFlags.Instance | BindingFlags.NonPublic)!
      .SetValue(svc, 2);
    t.GetField("_baseDelayMs", BindingFlags.Instance | BindingFlags.NonPublic)!
      .SetValue(svc, 1);

    var reconnect = t.GetMethod("ReconnectLoopAsync", BindingFlags.Instance | BindingFlags.NonPublic);
    Assert.IsNotNull(reconnect);
    var run = (Task)reconnect.Invoke(svc, new object?[] { null })!;
    await run.ConfigureAwait(false);

    Assert.IsFalse(svc.AutoReconnect, "AutoReconnect must be disabled after auth-style failure (GAP-058).");
  }
}
