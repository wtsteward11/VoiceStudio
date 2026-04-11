"""GAP-057: /api/audio and /api/audio/audit routes honor require_auth_if_enabled."""

from __future__ import annotations

import io
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
    """Simulate VOICESTUDIO_REQUIRE_AUTH=true without re-importing the app."""
    import backend.api.middleware.auth_middleware as am

    monkeypatch.setattr(am, "AUTH_REQUIRED", True)


@pytest.fixture
def test_api_key() -> str:
    from backend.api.auth import get_api_key_manager

    _user, key = get_api_key_manager().create_user(
        "gap057_audio_auth_test",
        email=None,
        generate_api_key=True,
    )
    assert key is not None
    return key


class TestAudioRoutesAuthWhenRequired:
    """Unauthorized requests return 401 when global auth is required."""

    def test_marking_requires_auth(self, client, auth_required_on) -> None:
        r = client.get("/api/audio/nonexistent-id/marking")
        assert r.status_code == 401

    def test_file_requires_auth(self, client, auth_required_on) -> None:
        r = client.get("/api/audio/file/00000000-0000-0000-0000-000000000000")
        assert r.status_code == 401

    def test_export_requires_auth(self, client, auth_required_on) -> None:
        r = client.post(
            "/api/audio/export",
            json={"source": "x", "format": "wav"},
        )
        assert r.status_code == 401

    def test_upload_requires_auth(self, client, auth_required_on) -> None:
        r = client.post(
            "/api/audio/upload",
            files={"file": ("t.wav", io.BytesIO(b"notwav"), "audio/wav")},
        )
        assert r.status_code == 401

    def test_formats_requires_auth_same_router(self, client, auth_required_on) -> None:
        """Discovery endpoint is on the same router; it is protected when auth is on."""
        r = client.get("/api/audio/formats")
        assert r.status_code == 401


class TestAudioRoutesAnonymousWhenAuthDisabled:
    """Default local mode: no global auth gate (may 404/400 for bad data)."""

    def test_marking_allows_unauthenticated(self, client) -> None:
        r = client.get("/api/audio/00000000-0000-0000-0000-000000000000/marking")
        assert r.status_code == 404

    def test_formats_ok_without_credentials(self, client) -> None:
        r = client.get("/api/audio/formats")
        assert r.status_code == 200


class TestAudioRoutesWithValidApiKey:
    """Authenticated client is not rejected with 401 when auth is required."""

    def test_marking_not_401_with_key(self, client, auth_required_on, test_api_key: str) -> None:
        r = client.get(
            "/api/audio/00000000-0000-0000-0000-000000000000/marking",
            headers={"X-API-Key": test_api_key},
        )
        assert r.status_code != 401

    def test_formats_200_with_key(self, client, auth_required_on, test_api_key: str) -> None:
        r = client.get("/api/audio/formats", headers={"X-API-Key": test_api_key})
        assert r.status_code == 200


class TestAudioAuditRoutesAuth:
    """GAP-057: /api/audio/audit shares namespace protection."""

    def test_audit_summary_requires_auth(self, client, auth_required_on) -> None:
        r = client.get("/api/audio/audit/summary")
        assert r.status_code == 401

    def test_audit_summary_ok_with_key(self, client, auth_required_on, test_api_key: str) -> None:
        r = client.get("/api/audio/audit/summary", headers={"X-API-Key": test_api_key})
        assert r.status_code != 401
