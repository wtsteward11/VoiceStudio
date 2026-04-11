"""GAP-059: Trust audit wiring on STS + audio routes."""

from __future__ import annotations

import sys
from pathlib import Path
from unittest.mock import AsyncMock, MagicMock

import pytest

_project_root = Path(__file__).resolve().parents[5]
if str(_project_root) not in sys.path:
    sys.path.insert(0, str(_project_root))


@pytest.fixture
def client():
    """TestClient — loopback-exempt via _LOOPBACK_HOSTS ("testclient")."""
    from fastapi.testclient import TestClient

    from backend.api.main import app

    return TestClient(app)


def test_sts_conversion_request_triggers_audit(
    monkeypatch: pytest.MonkeyPatch,
    client,
) -> None:
    spy = AsyncMock()
    mock_tas = MagicMock()
    mock_tas.record_sts_conversion = spy
    monkeypatch.setattr(
        "backend.services.speech_to_speech_service.get_trust_audit_service",
        lambda: mock_tas,
    )
    r = client.post(
        "/api/voice/sts/convert",
        json={
            "source_audio_id": "any",
            "target_voice_profile_id": "p",
            "consent_acknowledged": False,
        },
    )
    assert r.status_code == 400
    assert spy.await_count >= 1
    kw = spy.await_args.kwargs
    assert kw.get("result") == "denied"
    assert kw.get("reason_code") == "CONSENT_REQUIRED"


def test_marking_read_triggers_audit(
    monkeypatch: pytest.MonkeyPatch,
    tmp_path,
    client,
) -> None:
    spy = AsyncMock()
    mock_tas = MagicMock()
    mock_tas.record_marking_read = spy
    monkeypatch.setattr(
        "backend.services.trust_audit_service.get_trust_audit_service",
        lambda: mock_tas,
    )

    wav = tmp_path / "a.wav"
    wav.write_bytes(b"RIFF" + b"\x00" * 36)

    fake_art = MagicMock()
    fake_art.metadata = {"is_transformed": False}
    fake_art.path = str(wav)

    def fake_get(_aid: str):
        return fake_art

    monkeypatch.setattr(
        "backend.services.audio_registry_service.get_registry",
        lambda: MagicMock(get=fake_get),
    )
    monkeypatch.setattr(
        "backend.api.routes.audio._verify_watermark_on_artifact",
        lambda _p: False,
    )

    r = client.get("/api/audio/mark-id-1/marking")
    assert r.status_code == 200
    assert spy.await_count == 1


def test_export_of_transformed_triggers_audit(
    monkeypatch: pytest.MonkeyPatch,
    tmp_path,
    client,
) -> None:
    spy = AsyncMock()
    mock_tas = MagicMock()
    mock_tas.record_audio_export = spy
    monkeypatch.setattr(
        "backend.services.trust_audit_service.get_trust_audit_service",
        lambda: mock_tas,
    )

    src = tmp_path / "in.wav"
    src.write_bytes(b"RIFF" + b"\x00" * 64)

    fake_art = MagicMock()
    fake_art.metadata = {
        "is_transformed": True,
        "transformation_type": "speech_to_speech",
        "source": "src-a",
    }

    async def fake_convert(**_kwargs):
        from types import SimpleNamespace

        return SimpleNamespace(success=True, file_size_bytes=10, error=None)

    monkeypatch.setattr(
        "backend.api.routes.audio._get_audio_path",
        lambda _id: str(src),
    )
    monkeypatch.setattr(
        "backend.services.audio_registry_service.get_registry",
        lambda: MagicMock(get=lambda _s: fake_art),
    )
    monkeypatch.setattr(
        "backend.core.audio.conversion.get_conversion_service",
        lambda: MagicMock(convert_to_format=fake_convert),
    )

    r = client.post(
        "/api/audio/export",
        json={"source": "artifact-1", "format": "wav"},
    )
    assert r.status_code == 200
    assert spy.await_count == 1


def test_non_transformed_download_does_not_trigger_audit(
    monkeypatch: pytest.MonkeyPatch, tmp_path, client
) -> None:
    spy = AsyncMock()
    mock_tas = MagicMock()
    mock_tas.record_audio_download = spy
    monkeypatch.setattr(
        "backend.services.trust_audit_service.get_trust_audit_service",
        lambda: mock_tas,
    )

    wav = tmp_path / "raw.wav"
    wav.write_bytes(b"RIFF" + b"\x00" * 64)

    fake_art = MagicMock()
    fake_art.metadata = {"is_transformed": False}

    monkeypatch.setattr(
        "backend.api.routes.audio._get_audio_path",
        lambda _id: str(wav),
    )
    monkeypatch.setattr(
        "backend.services.audio_registry_service.get_registry",
        lambda: MagicMock(get=lambda _a: fake_art),
    )

    r = client.get("/api/audio/file/artifact-nt")
    assert r.status_code == 200
    assert spy.await_count == 0
