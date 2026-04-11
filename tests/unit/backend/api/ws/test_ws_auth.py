"""GAP-058: App-level WebSocket routes honor require_ws_auth_if_enabled."""

from __future__ import annotations

import sys
from pathlib import Path

import pytest

_project_root = Path(__file__).resolve().parents[5]
if str(_project_root) not in sys.path:
    sys.path.insert(0, str(_project_root))


@pytest.fixture
def client():
    from fastapi.testclient import TestClient

    from backend.api.main import app

    return TestClient(app)


@pytest.fixture
def auth_required_on(monkeypatch: pytest.MonkeyPatch) -> None:
    import backend.api.middleware.auth_middleware as am

    monkeypatch.setattr(am, "AUTH_REQUIRED", True)


@pytest.fixture
def test_api_key() -> str:
    from backend.api.auth import get_api_key_manager

    _user, key = get_api_key_manager().create_user(
        "gap058_ws_auth_test",
        email=None,
        generate_api_key=True,
    )
    assert key is not None
    return key


class TestWsRealtimeAuth:
    def test_requires_auth_when_enabled(self, client, auth_required_on) -> None:
        from starlette.websockets import WebSocketDisconnect

        with pytest.raises(WebSocketDisconnect) as excinfo:
            with client.websocket_connect("/ws/realtime") as ws:
                ws.receive_json()
        assert excinfo.value.code == 4001

    def test_allows_anonymous_when_disabled(self, client) -> None:
        with client.websocket_connect("/ws/realtime") as ws:
            data = ws.receive_json()
            assert "topic" in data or "type" in data

    def test_authenticated_succeeds(self, client, auth_required_on, test_api_key) -> None:
        with client.websocket_connect("/ws/realtime", headers={"X-API-Key": test_api_key}) as ws:
            data = ws.receive_json()
            assert "topic" in data or "type" in data


class TestWsPluginsAuth:
    def test_requires_auth_when_enabled(self, client, auth_required_on) -> None:
        from starlette.websockets import WebSocketDisconnect

        with pytest.raises(WebSocketDisconnect) as excinfo:
            with client.websocket_connect("/ws/plugins") as ws:
                ws.receive_json()
        assert excinfo.value.code == 4001

    def test_allows_anonymous_when_disabled(self, client) -> None:
        with client.websocket_connect("/ws/plugins") as ws:
            data = ws.receive_json()
            assert "type" in data

    def test_authenticated_succeeds(self, client, auth_required_on, test_api_key) -> None:
        with client.websocket_connect("/ws/plugins", headers={"X-API-Key": test_api_key}) as ws:
            data = ws.receive_json()
            assert "type" in data


class TestWsEventsPublic:
    def test_always_accessible_auth_off(self, client) -> None:
        with client.websocket_connect("/ws/events") as ws:
            data = ws.receive_json()
            assert data.get("topic") == "heartbeat"

    def test_always_accessible_auth_on(self, client, auth_required_on) -> None:
        with client.websocket_connect("/ws/events") as ws:
            data = ws.receive_json()
            assert data.get("topic") == "heartbeat"


class TestWsReconnectAuthAntiBypass:
    def test_second_connect_still_fails_without_credentials(self, client, auth_required_on) -> None:
        from starlette.websockets import WebSocketDisconnect

        for _ in range(2):
            with pytest.raises(WebSocketDisconnect) as excinfo:
                with client.websocket_connect("/ws/realtime") as ws:
                    ws.receive_json()
            assert excinfo.value.code == 4001
