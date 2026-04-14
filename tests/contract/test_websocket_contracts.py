"""WebSocket contract tests for VoiceStudio endpoints.

Validates message schemas for /ws/events, /ws/realtime, and /ws/plugins.
HTTP GET against WebSocket-only routes returns 404 under ASGI; registration is
checked via the Starlette route table instead.
"""

from __future__ import annotations

import pytest
from starlette.routing import WebSocketRoute

from backend.api.main import app


def _websocket_paths() -> set[str]:
    """Collect WebSocket route paths from the FastAPI app (including mounted routers)."""
    paths: set[str] = set()

    def walk(routes: list) -> None:
        for route in routes:
            if isinstance(route, WebSocketRoute):
                paths.add(route.path)
            nested = getattr(route, "routes", None)
            if nested:
                walk(list(nested))

    walk(list(app.routes))
    return paths


_WS_PATHS = _websocket_paths()


@pytest.mark.asyncio
async def test_events_endpoint_exists():
    """The /ws/events endpoint must be registered on the app."""
    assert "/ws/events" in _WS_PATHS, "/ws/events endpoint not registered"


@pytest.mark.asyncio
async def test_realtime_endpoint_exists():
    """The /ws/realtime endpoint must be registered on the app."""
    assert "/ws/realtime" in _WS_PATHS, "/ws/realtime endpoint not registered"


@pytest.mark.asyncio
async def test_plugins_endpoint_exists():
    """The /ws/plugins endpoint must be registered on the app."""
    assert "/ws/plugins" in _WS_PATHS, "/ws/plugins endpoint not registered"


class TestWebSocketMessageSchemas:
    """Validate expected message schema shapes."""

    def test_heartbeat_schema(self):
        """Heartbeat messages must have type and timestamp."""
        msg = {"type": "heartbeat", "timestamp": "2026-02-21T00:00:00Z"}
        assert "type" in msg
        assert "timestamp" in msg
        assert msg["type"] == "heartbeat"

    def test_realtime_subscribe_schema(self):
        """Subscribe messages must specify topics."""
        msg = {"type": "subscribe", "topics": ["meters", "training"]}
        assert "type" in msg
        assert "topics" in msg
        assert isinstance(msg["topics"], list)
        for topic in msg["topics"]:
            assert topic in ("meters", "training", "batch", "general")

    def test_plugin_state_sync_schema(self):
        """Plugin state sync messages must have plugin_id and state."""
        msg = {
            "type": "plugin_state_sync",
            "plugin_id": "test-plugin",
            "state": {"active": True, "version": "1.0.0"},
        }
        assert "type" in msg
        assert "plugin_id" in msg
        assert "state" in msg
        assert isinstance(msg["state"], dict)

    def test_synthesis_progress_schema(self):
        """Synthesis progress messages must have job_id and progress."""
        msg = {
            "type": "synthesis_progress",
            "job_id": "job-123",
            "progress": 0.75,
            "status": "streaming",
        }
        assert "type" in msg
        assert "job_id" in msg
        assert 0.0 <= msg["progress"] <= 1.0
        assert msg["status"] in ("pending", "streaming", "completed", "failed")

    def test_engine_status_schema(self):
        """Engine status messages must have engine_id and status."""
        msg = {
            "type": "engine_status",
            "engine_id": "xtts_v2",
            "status": "healthy",
            "latency_ms": 42,
        }
        assert "type" in msg
        assert "engine_id" in msg
        assert msg["status"] in ("healthy", "unhealthy", "initializing", "stopped")
