"""GAP-061: RBAC on STS + audio export trust surfaces; trust events carry user_role."""

from __future__ import annotations

import sys
from pathlib import Path
from unittest.mock import AsyncMock, patch

import pytest

_project_root = Path(__file__).resolve().parents[4]
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


def _create_key(role):
    from backend.api.auth import get_api_key_manager

    _u, key = get_api_key_manager().create_user(
        f"gap061_{role.value}_user",
        email=None,
        role=role,
        generate_api_key=True,
    )
    assert key is not None
    return key


def test_sts_route_blocks_guest_role(client, auth_required_on) -> None:
    from backend.api.auth import UserRole

    gkey = _create_key(UserRole.GUEST)
    r = client.post(
        "/api/voice/sts/convert",
        headers={"X-API-Key": gkey},
        json={
            "source_audio_id": "any",
            "target_voice_profile_id": "p",
            "consent_acknowledged": False,
        },
    )
    assert r.status_code == 403
    assert "Role denied" in r.text or "403" in str(r.status_code)


def test_sts_route_allows_user_role(client, auth_required_on) -> None:
    from backend.api.auth import UserRole

    ukey = _create_key(UserRole.USER)
    r = client.post(
        "/api/voice/sts/convert",
        headers={"X-API-Key": ukey},
        json={
            "source_audio_id": "any",
            "target_voice_profile_id": "p",
            "consent_acknowledged": False,
        },
    )
    assert r.status_code == 400


def test_sts_route_allows_admin_role(client, auth_required_on) -> None:
    from backend.api.auth import UserRole

    akey = _create_key(UserRole.ADMIN)
    r = client.post(
        "/api/voice/sts/convert",
        headers={"X-API-Key": akey},
        json={
            "source_audio_id": "any",
            "target_voice_profile_id": "p",
            "consent_acknowledged": False,
        },
    )
    assert r.status_code == 400


def test_export_route_blocks_guest_role(client, auth_required_on) -> None:
    from backend.api.auth import UserRole

    gkey = _create_key(UserRole.GUEST)
    r = client.post(
        "/api/audio/export",
        headers={"X-API-Key": gkey},
        json={"source": "x", "format": "wav"},
    )
    assert r.status_code == 403


@pytest.mark.asyncio
async def test_trust_audit_sts_includes_user_role_field() -> None:
    from backend.api.models_additional import SpeechToSpeechRequest
    from backend.services.trust_audit_service import TrustAuditService

    req = SpeechToSpeechRequest(
        source_audio_id="src1",
        target_voice_profile_id="p1",
        consent_acknowledged=True,
    )
    svc = TrustAuditService()
    with patch("backend.services.trust_audit_service.get_audit_logger") as gal:
        log_mock = AsyncMock(return_value="id")
        gal.return_value.log = log_mock
        await svc.record_sts_conversion(
            request=req,
            audio_id="out1",
            result="success",
            reason_code=None,
            auth_subject="u1",
            correlation_id="corr-gap061",
            watermark_applied=False,
            user_role="user",
        )
        meta = log_mock.await_args.kwargs["metadata"]
        assert meta["user_role"] == "user"


@pytest.mark.asyncio
async def test_trust_audit_export_includes_user_role_field() -> None:
    from backend.services.trust_audit_service import TrustAuditService

    svc = TrustAuditService()
    with patch("backend.services.trust_audit_service.get_audit_logger") as gal:
        log_mock = AsyncMock(return_value="id")
        gal.return_value.log = log_mock
        await svc.record_audio_export(
            source_audio_id="aid1",
            artifact_meta={"is_transformed": True, "source": "s0"},
            result="success",
            reason_code=None,
            auth_subject="u1",
            correlation_id="c-exp",
            user_role="admin",
        )
        meta = log_mock.await_args.kwargs["metadata"]
        assert meta["user_role"] == "admin"


def test_role_gate_does_not_fire_in_local_mode(client) -> None:
    import backend.api.middleware.auth_middleware as am

    assert am.AUTH_REQUIRED is False
    r = client.post(
        "/api/voice/sts/convert",
        json={
            "source_audio_id": "any",
            "target_voice_profile_id": "p",
            "consent_acknowledged": False,
        },
    )
    assert r.status_code == 400


@pytest.mark.asyncio
async def test_trust_event_user_role_joinable_with_audit_artifact_id() -> None:
    from backend.api.models_additional import SpeechToSpeechRequest
    from backend.services.trust_audit_service import TrustAuditService

    req = SpeechToSpeechRequest(
        source_audio_id="src1",
        target_voice_profile_id="p1",
        consent_acknowledged=True,
    )
    svc = TrustAuditService()
    with patch("backend.services.trust_audit_service.get_audit_logger") as gal:
        log_mock = AsyncMock(return_value="id")
        gal.return_value.log = log_mock
        await svc.record_sts_conversion(
            request=req,
            audio_id="artifact-join-1",
            result="success",
            reason_code=None,
            auth_subject="subj-1",
            correlation_id="corr-join-1",
            watermark_applied=True,
            user_role="user",
        )
        meta = log_mock.await_args.kwargs["metadata"]
        assert meta["artifact_id"] == "artifact-join-1"
        assert meta["correlation_id"] == "corr-join-1"
        assert meta["user_role"] == "user"
