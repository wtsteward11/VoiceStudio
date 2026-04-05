using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using VoiceStudio.Core.Services;

namespace VoiceStudio.App.Tests.Services;

[TestClass]
public sealed class BackendClientConfigEnvironmentTests
{
  private static void ClearBackendEnv()
  {
    Environment.SetEnvironmentVariable("VOICESTUDIO_BACKEND_URL", null);
    Environment.SetEnvironmentVariable("VOICESTUDIO_API_HOST", null);
    Environment.SetEnvironmentVariable("VOICESTUDIO_API_PORT", null);
    Environment.SetEnvironmentVariable("VOICESTUDIO_WS_PORT", null);
  }

  [TestMethod]
  public void FromEnvironment_Defaults_ToIpv4LoopbackAndRealtimeWs()
  {
    ClearBackendEnv();
    try
    {
      var c = BackendClientConfig.FromEnvironment();
      Assert.AreEqual("http://127.0.0.1:8000", c.BaseUrl);
      StringAssert.StartsWith(c.WebSocketUrl, "ws://127.0.0.1:8000/");
      StringAssert.EndsWith(c.WebSocketUrl, "/ws/realtime");
    }
    finally
    {
      ClearBackendEnv();
    }
  }

  [TestMethod]
  public void FromEnvironment_BackendUrl_Wins_And_BuildsWssWhenHttps()
  {
    ClearBackendEnv();
    try
    {
      Environment.SetEnvironmentVariable("VOICESTUDIO_BACKEND_URL", "https://api.example:8443/");
      Environment.SetEnvironmentVariable("VOICESTUDIO_API_HOST", "10.0.0.1");
      var c = BackendClientConfig.FromEnvironment();
      Assert.AreEqual("https://api.example:8443", c.BaseUrl);
      Assert.AreEqual("wss://api.example:8443/ws/realtime", c.WebSocketUrl);
    }
    finally
    {
      ClearBackendEnv();
    }
  }

  [TestMethod]
  public void FromEnvironment_InvalidBackendUrl_FallsBackToApiHostPort()
  {
    ClearBackendEnv();
    try
    {
      Environment.SetEnvironmentVariable("VOICESTUDIO_BACKEND_URL", "not-a-url");
      Environment.SetEnvironmentVariable("VOICESTUDIO_API_HOST", "192.168.4.4");
      Environment.SetEnvironmentVariable("VOICESTUDIO_API_PORT", "9000");
      var c = BackendClientConfig.FromEnvironment();
      Assert.AreEqual("http://192.168.4.4:9000", c.BaseUrl);
      Assert.AreEqual("ws://192.168.4.4:9000/ws/realtime", c.WebSocketUrl);
    }
    finally
    {
      ClearBackendEnv();
    }
  }

  [TestMethod]
  public void FromEnvironment_WsPortOverride_OnlyChangesWebSocketUrl()
  {
    ClearBackendEnv();
    try
    {
      Environment.SetEnvironmentVariable("VOICESTUDIO_API_HOST", "127.0.0.1");
      Environment.SetEnvironmentVariable("VOICESTUDIO_API_PORT", "8000");
      Environment.SetEnvironmentVariable("VOICESTUDIO_WS_PORT", "8010");
      var c = BackendClientConfig.FromEnvironment();
      Assert.AreEqual("http://127.0.0.1:8000", c.BaseUrl);
      Assert.AreEqual("ws://127.0.0.1:8010/ws/realtime", c.WebSocketUrl);
    }
    finally
    {
      ClearBackendEnv();
    }
  }
}
