"""Tests for backend restart durability proof validation."""
from __future__ import annotations

import json
import struct
import sys
from pathlib import Path
from urllib.error import URLError

import pytest

ROOT = Path(__file__).resolve().parent.parent.parent.parent.parent
sys.path.insert(0, str(ROOT))

from scripts.proof import verify_backend_restart_durability as durability


def _wav(samples: list[int], *, sample_rate: int = 16000) -> bytes:
    pcm = b"".join(struct.pack("<h", sample) for sample in samples)
    byte_rate = sample_rate * 2
    block_align = 2
    fmt = struct.pack("<HHIIHH", 1, 1, sample_rate, byte_rate, block_align, 16)
    riff_size = 4 + (8 + len(fmt)) + (8 + len(pcm))
    return (
        b"RIFF"
        + struct.pack("<I", riff_size)
        + b"WAVE"
        + b"fmt "
        + struct.pack("<I", len(fmt))
        + fmt
        + b"data"
        + struct.pack("<I", len(pcm))
        + pcm
    )


def _proof(tmp_path: Path, *, export_path: Path | None = None) -> Path:
    payload = {
        "project": {"project_id": "project-123", "session_id": "session-123"},
        "synthesis": {"audio_url": "/api/voice/audio/audio-123"},
        "generated_audio": {
            "library_asset_id": "asset-123",
            "timeline_track_id": "track-123",
            "timeline_clip_id": "clip-123",
        },
        "timeline": {"track_id": "track-123", "clip_id": "clip-123", "session_id": "session-123"},
        "export": {"claimed": export_path is not None, "path": str(export_path) if export_path else None},
    }
    path = tmp_path / "proof.json"
    path.write_text(json.dumps(payload), encoding="utf-8")
    return path


class OkProcess:
    returncode = 0
    stderr = ""


class FailedProcess:
    returncode = 7
    stderr = "restart failed"


class FakeResponse:
    def __init__(self, body: bytes) -> None:
        self._body = body

    def __enter__(self) -> FakeResponse:
        return self

    def __exit__(self, exc_type: object, exc: object, tb: object) -> None:
        return None

    def read(self) -> bytes:
        return self._body


def _install_success_http(monkeypatch: pytest.MonkeyPatch) -> None:
    def fake_urlopen(url: str, timeout: float) -> FakeResponse:
        if "/api/health/readiness" in url:
            return FakeResponse(json.dumps({"ready": True}).encode())
        if "/api/voice/audio/audio-123" in url:
            return FakeResponse(_wav([0, 1000, -1000, 0] * 4000))
        if "/api/library/assets/asset-123" in url:
            return FakeResponse(json.dumps({"id": "asset-123"}).encode())
        if "/api/timeline/state" in url:
            return FakeResponse(
                json.dumps(
                    {"tracks": [{"id": "track-123", "clips": [{"id": "clip-123"}]}]}
                ).encode()
            )
        raise URLError(f"unexpected URL {url}")

    monkeypatch.setattr(durability, "urlopen", fake_urlopen)


def _install_success_process(monkeypatch: pytest.MonkeyPatch) -> None:
    monkeypatch.setattr(durability.subprocess, "run", lambda *args, **kwargs: OkProcess())


def test_missing_restart_command_is_blocked_non_claim(tmp_path: Path) -> None:
    result = durability.verify_restart_durability(
        _proof(tmp_path),
        restart_command=None,
        base_url="http://127.0.0.1:8000",
        timeout_seconds=1.0,
    )

    assert result["status"] == "blocked"
    assert result["restart_performed"] is False
    assert any("restart command not supplied" in blocker for blocker in result["blockers"])


def test_restart_command_failure_fails_before_reload(tmp_path: Path, monkeypatch: pytest.MonkeyPatch) -> None:
    monkeypatch.setattr(durability.subprocess, "run", lambda *args, **kwargs: FailedProcess())

    result = durability.verify_restart_durability(
        _proof(tmp_path),
        restart_command="restart-backend",
        base_url="http://127.0.0.1:8000",
        timeout_seconds=1.0,
    )

    assert result["status"] == "fail"
    assert result["restart_performed"] is False
    assert any("restart command failed" in blocker for blocker in result["blockers"])


def test_readiness_failure_blocks_durability_claim(tmp_path: Path, monkeypatch: pytest.MonkeyPatch) -> None:
    _install_success_process(monkeypatch)
    monkeypatch.setattr(durability, "_wait_for_readiness", lambda base_url, timeout_seconds: (False, ["not ready"]))

    result = durability.verify_restart_durability(
        _proof(tmp_path),
        restart_command="restart-backend",
        base_url="http://127.0.0.1:8000",
        timeout_seconds=1.0,
    )

    assert result["restart_performed"] is True
    assert result["readiness_restored"] is False
    assert result["status"] == "fail"


def test_successful_restart_reloads_audio_library_and_timeline(
    tmp_path: Path,
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    _install_success_process(monkeypatch)
    _install_success_http(monkeypatch)

    result = durability.verify_restart_durability(
        _proof(tmp_path),
        restart_command="restart-backend",
        base_url="http://127.0.0.1:8000",
        timeout_seconds=1.0,
    )

    assert result["status"] == "pass"
    assert result["restart_performed"] is True
    assert result["readiness_restored"] is True
    assert result["audio_reloaded"] is True
    assert result["library_asset_reloaded"] is True
    assert result["timeline_clip_reloaded"] is True


def test_audio_reload_json_body_fails(tmp_path: Path, monkeypatch: pytest.MonkeyPatch) -> None:
    _install_success_process(monkeypatch)

    def fake_urlopen(url: str, timeout: float) -> FakeResponse:
        if "/api/health/readiness" in url:
            return FakeResponse(json.dumps({"ready": True}).encode())
        if "/api/voice/audio/audio-123" in url:
            return FakeResponse(b'{"detail":"not audio"}')
        raise URLError(f"unexpected URL {url}")

    monkeypatch.setattr(durability, "urlopen", fake_urlopen)

    result = durability.verify_restart_durability(
        _proof(tmp_path),
        restart_command="restart-backend",
        base_url="http://127.0.0.1:8000",
        timeout_seconds=1.0,
    )

    assert result["status"] == "fail"
    assert result["audio_reloaded"] is False
    assert any("JSON body" in blocker for blocker in result["blockers"])


def test_library_reload_failure_fails(tmp_path: Path, monkeypatch: pytest.MonkeyPatch) -> None:
    _install_success_process(monkeypatch)

    def fake_urlopen(url: str, timeout: float) -> FakeResponse:
        if "/api/health/readiness" in url:
            return FakeResponse(json.dumps({"ready": True}).encode())
        if "/api/voice/audio/audio-123" in url:
            return FakeResponse(_wav([0, 1000, -1000, 0] * 4000))
        if "/api/library/assets/asset-123" in url:
            raise URLError("missing asset")
        raise URLError(f"unexpected URL {url}")

    monkeypatch.setattr(durability, "urlopen", fake_urlopen)

    result = durability.verify_restart_durability(
        _proof(tmp_path),
        restart_command="restart-backend",
        base_url="http://127.0.0.1:8000",
        timeout_seconds=1.0,
    )

    assert result["audio_reloaded"] is True
    assert result["library_asset_reloaded"] is False
    assert any("library asset reload failed" in blocker for blocker in result["blockers"])


def test_export_replay_is_validated_when_claimed(tmp_path: Path, monkeypatch: pytest.MonkeyPatch) -> None:
    _install_success_process(monkeypatch)
    _install_success_http(monkeypatch)
    export_path = tmp_path / "export.wav"
    export_path.write_bytes(_wav([0, 1000, -1000, 0] * 4000))

    result = durability.verify_restart_durability(
        _proof(tmp_path, export_path=export_path),
        restart_command="restart-backend",
        base_url="http://127.0.0.1:8000",
        timeout_seconds=1.0,
    )

    assert result["status"] == "pass"
    assert result["export_replay_validated"] is True
