# WebSocket Guide

> **Version**: 1.1.0  
> **Last Updated**: 2026-03-06  
> **Status**: Active

## Overview

VoiceStudio uses WebSocket connections for real-time communication between the UI and backend. This guide documents the WebSocket architecture, topics, and integration patterns.

## Architecture

```
┌─────────────────┐     WebSocket      ┌─────────────────┐
│   WinUI 3 UI    │◄──────────────────►│  FastAPI Backend │
│  (C# Client)    │    /ws/realtime    │  (Python Server) │
└─────────────────┘                    └─────────────────┘
        │                                       │
        │  Publishes/Subscribes                 │  Broadcasts Events
        │  to Topics                            │  from Services
        ▼                                       ▼
   ┌─────────┐                           ┌─────────────┐
   │ Topics  │                           │ realtime.py │
   └─────────┘                           └─────────────┘
```

## Topics (Implemented)

**Canonical reference**: [WebSocket Topics Reference](../REFERENCE/WEBSOCKET_TOPICS_REFERENCE.md)

The `/ws/realtime` endpoint supports these topics:

| Topic | Direction | Description |
|-------|-----------|-------------|
| `meters` | Server→Client | VU meter / audio level updates |
| `training` | Server→Client | Voice model training progress |
| `batch` | Server→Client | Batch processing job progress |
| `general` | Server→Client | General events (engine status, alerts) |
| `quality` | Server→Client | Real-time quality preview (IDEA 69) |

Connect with optional query: `?topics=meters,training,batch,general`

Client can subscribe/unsubscribe at runtime via `{"type":"subscribe","topic":"meters"}` and `{"type":"unsubscribe","topic":"meters"}`.

## Message Format

### Client to Server

```json
{
  "topic": "synthesis/start",
  "payload": {
    "text": "Hello world",
    "voice_id": "voice_001",
    "options": {
      "speed": 1.0,
      "pitch": 0.0
    }
  },
  "request_id": "uuid-1234"
}
```

### Server to Client

```json
{
  "topic": "synthesis/progress",
  "payload": {
    "job_id": "job_abc123",
    "progress": 45,
    "stage": "inference",
    "eta_seconds": 3.5
  },
  "timestamp": "2026-02-04T12:00:00Z"
}
```

## Backend Implementation

### WebSocket Endpoint

```python
# backend/api/ws/realtime.py
from fastapi import WebSocket
from app.core.events import EventBus

@router.websocket("/ws/realtime")
async def websocket_endpoint(websocket: WebSocket):
    await websocket.accept()
    
    async def on_event(topic: str, payload: dict):
        await websocket.send_json({
            "topic": topic,
            "payload": payload,
            "timestamp": datetime.utcnow().isoformat()
        })
    
    EventBus.subscribe("*", on_event)
    
    try:
        while True:
            data = await websocket.receive_json()
            topic = data.get("topic")
            payload = data.get("payload", {})
            
            await handle_client_message(topic, payload)
    except WebSocketDisconnect:
        EventBus.unsubscribe(on_event)
```

### Event Bus

```python
# app/core/events.py
class EventBus:
    _subscribers: Dict[str, List[Callable]] = {}
    
    @classmethod
    def publish(cls, topic: str, payload: dict):
        for subscriber in cls._subscribers.get(topic, []):
            asyncio.create_task(subscriber(topic, payload))
        for subscriber in cls._subscribers.get("*", []):
            asyncio.create_task(subscriber(topic, payload))
    
    @classmethod
    def subscribe(cls, topic: str, callback: Callable):
        cls._subscribers.setdefault(topic, []).append(callback)
```

## Frontend Implementation

### WebSocket Service

```csharp
// src/VoiceStudio.App/Services/WebSocketService.cs
public class WebSocketService : IWebSocketService
{
    private ClientWebSocket _socket;
    private readonly ConcurrentDictionary<string, Action<JsonElement>> _handlers;

    public async Task ConnectAsync(string url)
    {
        _socket = new ClientWebSocket();
        await _socket.ConnectAsync(new Uri(url), CancellationToken.None);
        _ = ReceiveLoopAsync();
    }

    public void Subscribe(string topic, Action<JsonElement> handler)
    {
        _handlers[topic] = handler;
    }

    private async Task ReceiveLoopAsync()
    {
        var buffer = new byte[4096];
        while (_socket.State == WebSocketState.Open)
        {
            var result = await _socket.ReceiveAsync(buffer, CancellationToken.None);
            if (result.MessageType == WebSocketMessageType.Text)
            {
                var message = JsonSerializer.Deserialize<WebSocketMessage>(buffer[..result.Count]);
                if (_handlers.TryGetValue(message.Topic, out var handler))
                {
                    handler(message.Payload);
                }
            }
        }
    }
}
```

### ViewModel Integration

```csharp
public class SynthesisViewModel : BaseViewModel
{
    private readonly IWebSocketService _ws;

    public SynthesisViewModel(IWebSocketService ws)
    {
        _ws = ws;
        _ws.Subscribe("synthesis/progress", OnProgress);
        _ws.Subscribe("synthesis/complete", OnComplete);
    }

    private void OnProgress(JsonElement payload)
    {
        var progress = payload.GetProperty("progress").GetInt32();
        DispatcherQueue.TryEnqueue(() => Progress = progress);
    }
}
```

## Connection Management

### Reconnection

The WebSocket service implements automatic reconnection:

```csharp
private async Task ReconnectLoopAsync()
{
    while (!_disposed)
    {
        if (_socket?.State != WebSocketState.Open)
        {
            try
            {
                await ConnectAsync(_serverUrl);
                await ResubscribeAllAsync();
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Reconnect failed: {Error}", ex.Message);
                await Task.Delay(_reconnectDelay);
                _reconnectDelay = Math.Min(_reconnectDelay * 2, MaxReconnectDelay);
            }
        }
        await Task.Delay(1000);
    }
}
```

### Heartbeat

```json
{
  "topic": "system/ping",
  "payload": {},
  "request_id": "heartbeat-123"
}

{
  "topic": "system/pong",
  "payload": {
    "server_time": "2026-02-04T12:00:00Z"
  }
}
```

## Standardized Protocol (GAP-INT-002)

All WebSocket messages use the standardized protocol from `backend/api/ws/protocol.py`:

### Message Types

| Type | Description |
|------|-------------|
| `data` | General data payload |
| `error` | Error notification |
| `ack` | Acknowledgment |
| `ping` / `pong` | Keep-alive |
| `subscribe` / `unsubscribe` | Topic management |
| `start` / `stop` | Operation control |
| `complete` | Operation complete |
| `progress` | Progress update |
| `audio_chunk` | Audio data chunk |
| `audio_complete` | Audio stream complete |
| `training_update` | Training progress |

### Error Codes

| Code | Description |
|------|-------------|
| `VALIDATION_ERROR` | Invalid parameters |
| `ENGINE_ERROR` | Engine failure |
| `NOT_FOUND` | Resource not found |
| `UNAVAILABLE` | Service unavailable |
| `RATE_LIMITED` | Rate limit exceeded |
| `UNAUTHORIZED` | Auth required |
| `INTERNAL_ERROR` | Server error |
| `TIMEOUT` | Operation timeout |

### WebSocket Endpoints

| Endpoint | Purpose |
|----------|---------|
| `/ws/realtime` | Topic-based real-time updates (meters, training, batch, general, quality) |
| `/ws/events` | Legacy heartbeat only |
| `/ws/plugins` | Plugin state synchronization |
| `/api/pipeline/stream` | STT→LLM→TTS pipeline (streaming) |
| `/api/voice/synthesize/stream` | TTS streaming |
| `/api/rvc/convert/realtime` | Voice conversion |
| `/api/realtime-converter/{id}/stream` | Format conversion |
| `/api/realtime-visualizer/{id}/stream` | Audio visualization |

See [WebSocket Topics Reference](../REFERENCE/WEBSOCKET_TOPICS_REFERENCE.md) for topic payloads and broadcast APIs.

## Best Practices

1. **Use topic namespacing** - Organize topics by feature (e.g., `synthesis/`, `job/`)
2. **Include request_id** - Enable request/response correlation
3. **Handle disconnections** - Implement reconnection with backoff
4. **Validate messages** - Verify topic and payload structure
5. **Log events** - Track message flow for debugging

## Related Documentation

- [WebSocket Topics Reference](../REFERENCE/WEBSOCKET_TOPICS_REFERENCE.md) — Canonical topic reference
- [API Conventions](../../backend/api/API_CONVENTIONS.md) — WebSocket message protocol
- [Backend Services](SERVICE_ARCHITECTURE.md)
