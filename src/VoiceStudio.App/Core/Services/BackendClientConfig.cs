using System;

namespace VoiceStudio.Core.Services
{
  public class BackendClientConfig
  {
    public string BaseUrl { get; set; } = "http://localhost:8001";
    public string WebSocketUrl { get; set; } = "ws://localhost:8001/ws";
    public TimeSpan RequestTimeout { get; set; } = TimeSpan.FromSeconds(30);
  }
}