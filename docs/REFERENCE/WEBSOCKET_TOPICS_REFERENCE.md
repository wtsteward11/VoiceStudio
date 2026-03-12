# WebSocket Topics Reference

> **Version**: 1.0.0  
> **Last Updated**: 2026-03-06  
> **Status**: Active  
> **Implementation**: `backend/api/ws/realtime.py`

## Overview

This reference documents the WebSocket topics used by VoiceStudio's real-time update system. Topics are used for pub/sub-style broadcasting from the backend to connected clients over `/ws/realtime`.

## WebSocket Endpoints

| Endpoint | Purpose | Topics |
|----------|---------|--------|
| `/ws/realtime` | Real-time updates (meters, training, batch, general, quality) | Topic-based subscription |
| `/ws/events` | Legacy heartbeat only | N/A |
| `/ws/plugins` | Plugin state synchronization | N/A (sync protocol) |

## Topic Subscription

### Connection

Connect to `/ws/realtime` with optional query parameter:

```
ws://localhost:8000/ws/realtime?topics=meters,training,batch,general
```

- **topics** (optional): Comma-separated list of topics. Default: `general`.
- Valid topic names: `meters`, `training`, `batch`, `general`, `quality`.

### Client Messages (Client→Server)

| Message Type | Payload | Description |
|--------------|---------|-------------|
| `subscribe` | `{"topic": "meters"}` | Subscribe to a topic |
| `unsubscribe` | `{"topic": "meters"}` | Unsubscribe from a topic |
| `ping` | `{}` | Heartbeat request |

### Server Messages (Server→Client)

| Message Type | Description |
|--------------|-------------|
| `pong` | Response to client `ping` |
| `heartbeat` | Server-initiated keep-alive (every 30s on idle) |
| `initial` | Initial data for subscribed topic (on connect) |
| `update` | Batched or immediate update for topic |
| `event` | General event (general topic only) |

---

## Implemented Topics

### `meters`

**Purpose**: VU meter / audio level updates for mixer channels.

**Direction**: Server→Client only.

**Broadcast API**: `broadcast_meter_updates(project_id, channel_id, meter_data, batch=True)`

**Payload**:
```json
{
  "topic": "meters",
  "type": "update",
  "payload": {
    "project_id": "string",
    "channel_id": "string",
    "peak_level": 0.0,
    "rms_level": 0.0,
    "timestamp": "ISO8601"
  },
  "timestamp": "ISO8601"
}
```

**Cache Key**: `{project_id}:{channel_id}`

**Update Rate**: 30–60 Hz (batched for performance).

---

### `training`

**Purpose**: Voice model training progress updates.

**Direction**: Server→Client only.

**Broadcast API**: `broadcast_training_progress(training_id, progress_data, batch=True)`

**Payload**:
```json
{
  "topic": "training",
  "type": "update",
  "payload": {
    "training_id": "string",
    "epoch": 0,
    "loss": 0.0,
    "status": "string",
    "timestamp": "ISO8601"
  },
  "timestamp": "ISO8601"
}
```

**Cache Key**: `training_id`

**Update Rate**: Per-epoch or per-step.

---

### `batch`

**Purpose**: Batch processing job progress (e.g., batch synthesis, batch transcription).

**Direction**: Server→Client only.

**Broadcast API**: `broadcast_batch_progress(batch_id, progress_data, batch=True)`

**Payload**:
```json
{
  "topic": "batch",
  "type": "update",
  "payload": {
    "batch_id": "string",
    "status": "string",
    "progress": 0,
    "current_item": 0,
    "total_items": 0,
    "timestamp": "ISO8601"
  },
  "timestamp": "ISO8601"
}
```

**Cache Key**: `batch_id`

**Update Rate**: Per-item or on status change.

---

### `general`

**Purpose**: General system events (engine status, alerts, etc.).

**Direction**: Server→Client only.

**Broadcast API**: `broadcast_general_event(event_type, payload)`

**Payload**:
```json
{
  "topic": "general",
  "type": "event",
  "event_type": "engine_status",
  "payload": { },
  "timestamp": "ISO8601"
}
```

**Example event_type values**: `engine_status`, `system_alert`, etc.

**Update Rate**: As needed.

---

### `quality`

**Purpose**: Real-time quality preview (IDEA 69).

**Direction**: Server→Client only.

**Status**: Topic registered; broadcast API not yet implemented.

**Update Rate**: TBD.

---

## Message Batching

Topics `meters`, `training`, and `batch` support message batching:

- Messages are queued by priority (LOW, NORMAL, HIGH, CRITICAL).
- Batch size: 10 messages (configurable).
- Batch timeout: 0.1 seconds.
- High-priority messages can trigger immediate send.

## Connection Statistics

Health endpoint exposes WebSocket stats via `get_connection_stats()`:

- `total_connections`, `healthy_connections`, `unhealthy_connections`
- `subscribers_by_topic`: Count per topic
- `batch_queue_sizes`: Queue depth by topic and priority

## Related Documentation

- [WebSocket Guide](../developer/WEBSOCKET_GUIDE.md) — Architecture and integration patterns
- [API Conventions](../backend/api/API_CONVENTIONS.md) — WebSocket message protocol (GAP-INT-002)
- [Protocol Module](../../backend/api/ws/protocol.py) — Standardized message helpers
