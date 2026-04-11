using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using VoiceStudio.App.Services;
using VoiceStudio.App.Utilities;
using VoiceStudio.Core.Services;

namespace VoiceStudio.App.Tests.Services
{
  /// <summary>
  /// Closure-grade transport proof for meter WebSocket payload handling (GAP-036).
  /// Uses <see cref="WebSocketMessage"/> as <see cref="WebSocketService"/> builds it after parsing wire JSON.
  /// See <c>docs/governance/TEST_CLASSIFICATION.md</c> (seam / contract proof).
  /// </summary>
  [TestClass]
  public class MeterWebSocketClientTests
  {
    /// <summary>Matches <see cref="WebSocketService.ProcessMessage"/> inner payload materialization.</summary>
    private static object MaterializePayloadFromInnerJson(string innerJson)
    {
      return JsonSerializer.Deserialize<object>(innerJson, JsonSerializerOptionsFactory.BackendApi)
             ?? throw new InvalidOperationException("Deserialize<object> returned null");
    }

    private sealed class TestWebSocketService : IWebSocketService
    {
      public event EventHandler? Connected;
      public event EventHandler<string>? Disconnected;
      public event EventHandler<Exception>? Error;
      public event EventHandler<WebSocketMessage>? MessageReceived;

      public WebSocketState State => WebSocketState.Connected;
      public bool IsConnected { get; set; } = true;

      public void Emit(WebSocketMessage message)
      {
        MessageReceived?.Invoke(this, message);
      }

      public Task ConnectAsync(string[]? topics = null, CancellationToken cancellationToken = default) => Task.CompletedTask;
      public Task DisconnectAsync() => Task.CompletedTask;
      public Task SubscribeAsync(string topic) => Task.CompletedTask;
      public Task UnsubscribeAsync(string topic) => Task.CompletedTask;
      public Task PingAsync() => Task.CompletedTask;
      public Task SendMessageAsync(object message) => Task.CompletedTask;
      public void SetAuthHeaders(IReadOnlyDictionary<string, string>? headers) { }
      public void SetCredentialProvider(Func<IReadOnlyDictionary<string, string>?>? provider) { }
      public void Dispose() { }
    }

    [TestMethod]
    public void DeserializeObject_InnerJson_MaterializesAsJsonElementLikeProduction()
    {
      const string inner = """{"project_id":"proj-1","channel_id":"ch-9","peak_level":0.65,"rms_level":0.42}""";
      var obj = MaterializePayloadFromInnerJson(inner);
      Assert.IsInstanceOfType(obj, typeof(JsonElement), "WebSocketService uses Deserialize<object>; expect JsonElement for objects.");
    }

    [TestMethod]
    public void MeterLevelUpdate_Deserializes_FromFrozenPayloadJson()
    {
      const string inner = """{"project_id":"proj-1","channel_id":"ch-9","peak_level":0.65,"rms_level":0.42}""";
      using var doc = JsonDocument.Parse(inner);
      var u = JsonSerializer.Deserialize<MeterLevelUpdate>(
          doc.RootElement.GetRawText(),
          JsonSerializerOptionsFactory.BackendApi);
      Assert.IsNotNull(u);
      Assert.AreEqual("proj-1", u!.ProjectId);
      Assert.AreEqual("ch-9", u.ChannelId);
      Assert.AreEqual(0.65, u.PeakLevelLinear, 1e-6);
      Assert.AreEqual(0.42, u.RmsLevelLinear, 1e-6);
    }

    [TestMethod]
    public void MessageReceived_BackendPayloadAfterWebSocketService_MapsToLevelsUpdated()
    {
      var ws = new TestWebSocketService();
      using var client = new MeterWebSocketClient(ws);

      MeterLevelUpdate? captured = null;
      client.LevelsUpdated += (_, e) => captured = e;

      const string inner = """{"project_id":"proj-1","channel_id":"ch-9","peak_level":0.65,"rms_level":0.42}""";
      ws.Emit(new WebSocketMessage
      {
        Topic = "meters",
        Type = "update",
        Payload = MaterializePayloadFromInnerJson(inner),
        Timestamp = DateTime.UtcNow
      });

      Assert.IsNotNull(captured);
      Assert.AreEqual("proj-1", captured!.ProjectId);
      Assert.AreEqual("ch-9", captured.ChannelId);
      Assert.AreEqual(0.65, captured.PeakLevelLinear, 1e-6);
      Assert.AreEqual(0.42, captured.RmsLevelLinear, 1e-6);
    }

    [TestMethod]
    public void MessageReceived_WrongTopic_DoesNotRaiseLevelsUpdated()
    {
      var ws = new TestWebSocketService();
      using var client = new MeterWebSocketClient(ws);

      var count = 0;
      client.LevelsUpdated += (_, _) => count++;

      const string inner = """{"project_id":"p","channel_id":"c","peak_level":1,"rms_level":0.5}""";
      ws.Emit(new WebSocketMessage
      {
        Topic = "training",
        Type = "update",
        Payload = MaterializePayloadFromInnerJson(inner),
        Timestamp = DateTime.UtcNow
      });

      Assert.AreEqual(0, count);
    }

    [TestMethod]
    public void MessageReceived_MalformedPayload_DoesNotThrowToSubscriber()
    {
      var ws = new TestWebSocketService();
      using var client = new MeterWebSocketClient(ws);

      var count = 0;
      client.LevelsUpdated += (_, _) => count++;

      try
      {
        ws.Emit(new WebSocketMessage
        {
          Topic = "meters",
          Type = "update",
          Payload = "not valid meter json object",
          Timestamp = DateTime.UtcNow
        });
      }
      catch (Exception ex)
      {
        Assert.Fail($"Handler should not throw to caller: {ex}");
      }

      Assert.AreEqual(0, count);
    }

    [TestMethod]
    public void MessageReceived_NullPayload_DoesNotThrowToSubscriber()
    {
      var ws = new TestWebSocketService();
      using var client = new MeterWebSocketClient(ws);

      try
      {
        ws.Emit(new WebSocketMessage
        {
          Topic = "meters",
          Type = "update",
          Payload = null,
          Timestamp = DateTime.UtcNow
        });
      }
      catch (Exception ex)
      {
        Assert.Fail($"Handler should not throw to caller: {ex}");
      }
    }

    [TestMethod]
    public void MessageReceived_BatchUnwrappedChildEnvelope_MapsToLevelsUpdated()
    {
      var ws = new TestWebSocketService();
      using var client = new MeterWebSocketClient(ws);

      MeterLevelUpdate? captured = null;
      client.LevelsUpdated += (_, e) => captured = e;

      const string envelope =
          """{"topic":"meters","type":"update","payload":{"project_id":"batch-p","channel_id":"batch-c","peak_level":0.12,"rms_level":0.08},"timestamp":"2026-03-30T00:00:00Z"}""";
      using var doc = JsonDocument.Parse(envelope);
      var child = doc.RootElement;
      var topic = child.GetProperty("topic").GetString() ?? "general";
      var type = child.GetProperty("type").GetString() ?? "update";
      object? payloadInner = null;
      if (child.TryGetProperty("payload", out var payloadProp))
      {
        payloadInner = JsonSerializer.Deserialize<object>(payloadProp.GetRawText(), JsonSerializerOptionsFactory.BackendApi);
      }

      ws.Emit(new WebSocketMessage
      {
        Topic = topic,
        Type = type,
        Payload = payloadInner,
        Timestamp = DateTime.UtcNow
      });

      Assert.IsNotNull(captured);
      Assert.AreEqual("batch-p", captured!.ProjectId);
      Assert.AreEqual("batch-c", captured.ChannelId);
      Assert.AreEqual(0.12, captured.PeakLevelLinear, 1e-6);
      Assert.AreEqual(0.08, captured.RmsLevelLinear, 1e-6);
    }

    [TestMethod]
    public void Dispose_CeasesProcessingIncomingMessages()
    {
      var ws = new TestWebSocketService();
      var client = new MeterWebSocketClient(ws);
      var count = 0;
      client.LevelsUpdated += (_, _) => count++;

      client.Dispose();

      const string inner = """{"project_id":"p","channel_id":"c","peak_level":1,"rms_level":0.5}""";
      ws.Emit(new WebSocketMessage
      {
        Topic = "meters",
        Type = "update",
        Payload = MaterializePayloadFromInnerJson(inner),
        Timestamp = DateTime.UtcNow
      });

      Assert.AreEqual(0, count);
    }
  }
}
